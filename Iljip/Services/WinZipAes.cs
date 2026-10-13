using System.IO;
using System.IO.Compression;

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
/// <remarks>
/// 암호 계산(키 만들기·AES-CTR·HMAC)은 자동 풀림 스텁과 같은 공유 소스 <see cref="WinZipAesKeys"/>·<see cref="WinZipAesCipher"/>(Services/Shared — W2-10)를 쓴다.
/// 여기에는 ZIP 에서 항목 데이터를 찾아 여는 일(본체 전용)만 남는다.
/// </remarks>
internal static class WinZipAes
{
    public const int AuthCodeLength = WinZipAesKeys.AuthCodeLength;

    public static int KeyLength(int strength) => WinZipAesKeys.KeyLength(strength);

    public static int SaltLength(int strength) => WinZipAesKeys.SaltLength(strength);

    /// <summary>암호 바이트가 이 salt·확인값(PV)과 맞는가.</summary>
    public static bool PasswordVerifierMatches(byte[] password, byte[] salt, byte[] pv, int strength) =>
        WinZipAesKeys.PasswordMatches(password, salt, pv[0], pv[1], strength);

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
            int saltLen = SaltLength(e.AesStrength);
            byte[] saltPv = ZipPasswordProbe.ReadAt(fs, dataStart, saltLen + 2);
            byte[] derived = WinZipAesKeys.Derive(password, saltPv[..saltLen], e.AesStrength);
            if (!WinZipAesKeys.VerifierMatches(derived, saltPv[saltLen], saltPv[saltLen + 1]))
                throw new WrongPasswordException(entryPath: e.Name);

            long cipherLen = e.CompressedSize - saltLen - 2 - AuthCodeLength;
            if (cipherLen < 0) throw new InvalidDataException($"AES 항목 크기가 맞지 않아요: {e.Name}");

            fs.Position = dataStart + saltLen + 2;
            var dec = new DecryptStream(fs, cipherLen, new WinZipAesCipher(derived, e.AesStrength));
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

    /// <summary>AES-CTR 로 암호문을 풀고, 읽은 암호문으로 HMAC 을 쌓는다(계산은 공유 <see cref="WinZipAesCipher"/>). 원본 파일 스트림을 소유한다.</summary>
    private sealed class DecryptStream : Stream
    {
        private readonly Stream _base;
        private long _remaining;
        private readonly WinZipAesCipher _cipher;
        private bool _verified;

        public DecryptStream(Stream @base, long cipherLength, WinZipAesCipher cipher)
        {
            _base = @base;
            _remaining = cipherLength;
            _cipher = cipher;
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            if (_remaining <= 0 || count == 0) return 0;
            int want = (int)Math.Min(count, _remaining);
            int n = _base.Read(buffer, offset, want);
            if (n <= 0) throw new EndOfStreamException("AES 데이터가 중간에 끝났어요(파일이 잘렸을 수 있어요).");
            _remaining -= n;
            _cipher.Decrypt(buffer, offset, n);
            return n;
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
                _cipher.AppendCiphertext(buf, 0, n);
                _remaining -= n;
            }
            var stored = new byte[AuthCodeLength];
            _base.ReadExactly(stored);
            if (!_cipher.AuthCodeMatches(stored, 0))
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
                _cipher.Dispose();
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
