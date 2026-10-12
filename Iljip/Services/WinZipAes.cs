using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;

namespace Iljip.Services;

/// <summary>
/// WinZip AES(AE-1/AE-2) ZIP 항목을 일집이 직접 푼다 — 암호 '바이트'를 받으므로 반디집의 CP949 한글 암호(B3)도 풀 수 있다
/// (SharpCompress 는 암호를 늘 UTF-8 로 바꿔 키를 만들어 못 푼다). 규격:
/// <list type="bullet">
/// <item>데이터 = salt(8·12·16바이트) + 암호 확인값(PV, 2) + 암호문 + 인증값(HMAC-SHA1 앞 10바이트).</item>
/// <item>PBKDF2-HMAC-SHA1(암호 바이트, salt, 1000회)로 키 길이×2+2 바이트를 만든다: 앞은 AES 키, 다음은 HMAC 키, 끝 2바이트는 PV.</item>
/// <item>AES-CTR: 16바이트 리틀 엔디언 카운터를 1부터 올려 가며 암호화한 값을 XOR.</item>
/// <item>인증값은 '암호문'의 HMAC — 끝까지 읽은 뒤 맞춰 본다. SharpCompress 는 이 값을 보지 않아 AE-2(CRC 칸이 0 — 일집 SharpZipLib 이 만드는 AES)는
///   데이터가 깨져도 조용히 틀린 내용을 풀었다(W1-C 실측). 여기서는 틀리면 예외 → 해제 공통 구현이 그 파일을 지우고 암호 예외로 알린다.</item>
/// </list>
/// 안쪽 압축 방식은 저장(0)·Deflate(8)·BZip2(12)만 직접 푼다. 그 밖은 null(호출자가 엔진에 맡기거나 '못 푸는 방식'으로 알림).
/// </summary>
internal static class WinZipAes
{
    public const int AuthCodeLength = 10;
    private const int Iterations = 1000;

    public static int KeyLength(int strength) => strength switch
    {
        1 => 16,
        2 => 24,
        3 => 32,
        _ => throw new InvalidDataException($"알 수 없는 AES 세기: {strength}"),
    };

    public static int SaltLength(int strength) => KeyLength(strength) / 2;

    /// <summary>암호 바이트가 이 salt·확인값(PV)과 맞는가.</summary>
    public static bool PasswordVerifierMatches(byte[] password, byte[] salt, byte[] pv, int strength)
    {
        int keyLen = KeyLength(strength);
        byte[] derived = Rfc2898DeriveBytes.Pbkdf2(password, salt, Iterations, HashAlgorithmName.SHA1, keyLen * 2 + 2);
        return derived[^2] == pv[0] && derived[^1] == pv[1];
    }

    /// <summary>이 항목의 안쪽 압축 방식을 직접 풀 수 있는가.</summary>
    public static bool CanDecompress(ZipCentralEntry e) => e.AesMethod is 0 or 8 or 12;

    /// <summary>
    /// AES 항목의 '풀린 내용' 스트림. 끝까지 읽으면(Read 가 0) 인증값을 맞춰 보고 틀리면 <see cref="InvalidDataException"/>.
    /// 확인값(PV)이 틀리면 <see cref="WrongPasswordException"/>(판정은 앞 3개 항목만 보므로 그 뒤 항목은 여기서 걸러진다).
    /// </summary>
    public static Stream OpenEntry(string zipPath, ZipCentralEntry e, byte[] password)
    {
        if (!CanDecompress(e)) throw new UnsupportedMethodException($"AES 안쪽 방식 {e.AesMethod}", entryPath: e.Name);

        var fs = ZipArchiveService.OpenRawStream(zipPath);   // .zip.001 숫자 분할은 이어 붙인 스트림(W2-01)
        try
        {
            var (dataStart, _) = ZipPasswordProbe.LocalData(fs, e);
            int keyLen = KeyLength(e.AesStrength);
            int saltLen = keyLen / 2;
            byte[] saltPv = ZipPasswordProbe.ReadAt(fs, dataStart, saltLen + 2);
            byte[] derived = Rfc2898DeriveBytes.Pbkdf2(password, saltPv.AsSpan(0, saltLen), Iterations, HashAlgorithmName.SHA1, keyLen * 2 + 2);
            if (derived[^2] != saltPv[saltLen] || derived[^1] != saltPv[saltLen + 1])
                throw new WrongPasswordException(entryPath: e.Name);

            long cipherLen = e.CompressedSize - saltLen - 2 - AuthCodeLength;
            if (cipherLen < 0) throw new InvalidDataException($"AES 항목 크기가 맞지 않아요: {e.Name}");

            fs.Position = dataStart + saltLen + 2;
            var dec = new DecryptStream(fs, cipherLen, derived[..keyLen], derived[keyLen..(keyLen * 2)]);
            Stream plain = e.AesMethod switch
            {
                0 => dec,
                8 => new DeflateStream(dec, CompressionMode.Decompress, leaveOpen: true),
                _ => new ICSharpCode.SharpZipLib.BZip2.BZip2InputStream(dec) { IsStreamOwner = false },
            };
            return new VerifyAtEndStream(plain, dec);
        }
        catch
        {
            fs.Dispose();
            throw;
        }
    }

    /// <summary>AES-CTR 로 암호문을 풀고, 읽은 암호문으로 HMAC 을 쌓는다. 원본 파일 스트림을 소유한다.</summary>
    private sealed class DecryptStream : Stream
    {
        private const int BlocksPerRefill = 256;   // 한 번에 4 KiB 씩 키 흐름을 만든다

        private readonly Stream _base;
        private long _remaining;
        private readonly Aes _aes;
        private readonly IncrementalHash _hmac;
        private readonly byte[] _counter = new byte[16];
        private readonly byte[] _counterBlocks = new byte[16 * BlocksPerRefill];
        private readonly byte[] _keyStream = new byte[16 * BlocksPerRefill];
        private int _keyPos;
        private bool _verified;

        public DecryptStream(Stream @base, long cipherLength, byte[] aesKey, byte[] hmacKey)
        {
            _base = @base;
            _remaining = cipherLength;
            _aes = Aes.Create();
            _aes.Key = aesKey;
            _hmac = IncrementalHash.CreateHMAC(HashAlgorithmName.SHA1, hmacKey);
            _keyPos = _keyStream.Length;
        }

        public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));

        public override int Read(Span<byte> buffer)
        {
            if (_remaining <= 0 || buffer.Length == 0) return 0;
            int want = (int)Math.Min(buffer.Length, _remaining);
            int n = _base.Read(buffer[..want]);
            if (n <= 0) throw new EndOfStreamException("AES 데이터가 중간에 끝났어요(파일이 잘렸을 수 있어요).");
            _remaining -= n;
            _hmac.AppendData(buffer[..n]);
            for (int i = 0; i < n; i++)
            {
                if (_keyPos == _keyStream.Length) Refill();
                buffer[i] ^= _keyStream[_keyPos++];
            }
            return n;
        }

        private void Refill()
        {
            for (int b = 0; b < BlocksPerRefill; b++)
            {
                // 리틀 엔디언 128비트 카운터 +1 (첫 블록은 1)
                for (int i = 0; i < 16 && ++_counter[i] == 0; i++) { }
                Buffer.BlockCopy(_counter, 0, _counterBlocks, b * 16, 16);
            }
            _aes.EncryptEcb(_counterBlocks, _keyStream, PaddingMode.None);
            _keyPos = 0;
        }

        /// <summary>남은 암호문을 HMAC 에 넣고(풀지는 않음) 뒤의 인증값 10바이트와 맞춰 본다.</summary>
        public void Verify()
        {
            if (_verified) return;
            var buf = new byte[81920];
            while (_remaining > 0)
            {
                int n = _base.Read(buf, 0, (int)Math.Min(buf.Length, _remaining));
                if (n <= 0) throw new EndOfStreamException("AES 데이터가 중간에 끝났어요(파일이 잘렸을 수 있어요).");
                _hmac.AppendData(buf, 0, n);
                _remaining -= n;
            }
            var stored = new byte[AuthCodeLength];
            _base.ReadExactly(stored);
            byte[] mac = _hmac.GetHashAndReset();
            if (!CryptographicOperations.FixedTimeEquals(mac.AsSpan(0, AuthCodeLength), stored))
                throw new InvalidDataException("AES 인증값(HMAC)이 맞지 않아요 — 데이터가 손상됐어요.");
            _verified = true;
        }

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _aes.Dispose();
                _hmac.Dispose();
                _base.Dispose();
            }
            base.Dispose(disposing);
        }
    }

    /// <summary>풀린 내용을 다 읽은 순간(Read 가 0) 인증값을 맞춰 본다 — 해제 공통 구현의 복사 고리가 그대로 검사를 부르게.</summary>
    private sealed class VerifyAtEndStream : Stream
    {
        private readonly Stream _plain;
        private readonly DecryptStream _dec;

        public VerifyAtEndStream(Stream plain, DecryptStream dec)
        {
            _plain = plain;
            _dec = dec;
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            int n = _plain.Read(buffer, offset, count);
            if (n == 0 && count > 0) _dec.Verify();
            return n;
        }

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (!ReferenceEquals(_plain, _dec)) _plain.Dispose();
                _dec.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
