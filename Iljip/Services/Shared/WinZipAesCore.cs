#nullable enable
using System;
using System.Security.Cryptography;

namespace Iljip.Services;

// ★ 공유 소스(W2-10): 일집 본체(net8.0-windows)와 자동 풀림 스텁(Iljip.SfxStub — .NET Framework 4.6.2)이 '같은 파일'을 컴파일한다
//   (스텁 csproj 가 이 폴더를 링크). 그래서 두 쪽 모두에 있는 API 만 쓴다 — Span·범위 연산자(^1·..)·Rfc2898DeriveBytes.Pbkdf2·Aes.EncryptEcb·
//   CryptographicOperations 는 .NET Framework 에 없다. 고칠 때는 두 프로젝트를 다 빌드할 것(본체 WinZipAes 의 시험 + SfxCreateTests 가 같이 지킨다).
//   설계는 'WinZipAes.cs 를 그대로 링크'였지만 그 파일은 Span·ZipPasswordProbe 등 본체 전용 코드에 묶여 net462 에서 컴파일되지 않아,
//   암호 계산 부분만 여기로 떼어 본체 WinZipAes 도 이것을 쓰게 했다(두 판이 어긋나지 않게 — 구현은 하나).

/// <summary>
/// WinZip AES(AE-1/AE-2) 키 만들기: PBKDF2-HMAC-SHA1(암호 바이트, salt, 1000회)로 '키 길이×2+2' 바이트 — 앞은 AES 키, 다음은 HMAC 키, 끝 2바이트는 암호 확인값(PV).
/// </summary>
internal static class WinZipAesKeys
{
    public const int Iterations = 1000;

    /// <summary>인증값(HMAC-SHA1 앞 10바이트) 길이.</summary>
    public const int AuthCodeLength = 10;

    /// <summary>세기(1·2·3) → AES 키 길이(16·24·32). 모르는 세기면 <see cref="System.IO.InvalidDataException"/>.</summary>
    public static int KeyLength(int strength)
    {
        switch (strength)
        {
            case 1: return 16;
            case 2: return 24;
            case 3: return 32;
            default: throw new System.IO.InvalidDataException("알 수 없는 AES 세기: " + strength);
        }
    }

    /// <summary>salt 길이(키 길이의 절반 — 8·12·16).</summary>
    public static int SaltLength(int strength) => KeyLength(strength) / 2;

    /// <summary>키 재료 전체(AES 키 + HMAC 키 + PV 2바이트).</summary>
    public static byte[] Derive(byte[] password, byte[] salt, int strength)
    {
        int length = KeyLength(strength) * 2 + 2;
#if NETFRAMEWORK
        using (var kdf = new Rfc2898DeriveBytes(password, salt, Iterations))
            return kdf.GetBytes(length);
#else
        return Rfc2898DeriveBytes.Pbkdf2(password, salt, Iterations, HashAlgorithmName.SHA1, length);
#endif
    }

    /// <summary>키 재료의 끝 2바이트가 파일의 암호 확인값(PV)과 같은가.</summary>
    public static bool VerifierMatches(byte[] derived, byte pv0, byte pv1) =>
        derived.Length >= 2 && derived[derived.Length - 2] == pv0 && derived[derived.Length - 1] == pv1;

    /// <summary>암호 바이트가 이 salt·확인값과 맞는가.</summary>
    public static bool PasswordMatches(byte[] password, byte[] salt, byte pv0, byte pv1, int strength) =>
        VerifierMatches(Derive(password, salt, strength), pv0, pv1);
}

/// <summary>
/// WinZip AES 항목 풀기: AES-CTR(16바이트 리틀 엔디언 카운터를 1부터)로 XOR 하고, 읽은 '암호문'으로 HMAC-SHA1 을 쌓는다.
/// 끝까지 읽은 뒤 <see cref="AuthCodeMatches"/> 로 파일의 인증값(10바이트)과 맞춰 본다 — AE-2 는 CRC 칸이 0 이라 이것이 유일한 무결성 확인이다.
/// </summary>
internal sealed class WinZipAesCipher : IDisposable
{
    private const int BlocksPerRefill = 256;   // 한 번에 4 KiB 씩 키 흐름을 만든다

    private readonly Aes _aes;
    private readonly ICryptoTransform _ecb;
    private readonly HMACSHA1 _hmac;
    private readonly byte[] _counter = new byte[16];
    private readonly byte[] _counterBlocks = new byte[16 * BlocksPerRefill];
    private readonly byte[] _keyStream = new byte[16 * BlocksPerRefill];
    private int _keyPos;
    private byte[]? _mac;

    /// <param name="derived"><see cref="WinZipAesKeys.Derive"/> 의 결과.</param>
    public WinZipAesCipher(byte[] derived, int strength)
    {
        int keyLen = WinZipAesKeys.KeyLength(strength);
        var aesKey = new byte[keyLen];
        var hmacKey = new byte[keyLen];
        Buffer.BlockCopy(derived, 0, aesKey, 0, keyLen);
        Buffer.BlockCopy(derived, keyLen, hmacKey, 0, keyLen);
        _aes = Aes.Create();
        _aes.Mode = CipherMode.ECB;
        _aes.Padding = PaddingMode.None;
        _aes.Key = aesKey;
        _ecb = _aes.CreateEncryptor();
        _hmac = new HMACSHA1(hmacKey);
        _keyPos = _keyStream.Length;
    }

    /// <summary>암호문을 그 자리에서 풀린 내용으로 바꾼다(HMAC 에는 바꾸기 전 암호문을 넣음).</summary>
    public void Decrypt(byte[] buffer, int offset, int count)
    {
        AppendCiphertext(buffer, offset, count);
        for (int i = 0; i < count; i++)
        {
            if (_keyPos == _keyStream.Length) Refill();
            buffer[offset + i] ^= _keyStream[_keyPos++];
        }
    }

    /// <summary>풀지 않고 HMAC 에만 넣는다(남은 암호문을 건너뛸 때 — 인증값은 끝까지 맞춰 봐야 함).</summary>
    public void AppendCiphertext(byte[] buffer, int offset, int count)
    {
        if (_mac is not null) throw new InvalidOperationException("인증값을 이미 계산했어요.");
        if (count > 0) _hmac.TransformBlock(buffer, offset, count, null, 0);
    }

    /// <summary>쌓은 HMAC 의 앞 10바이트가 <paramref name="stored"/>[<paramref name="offset"/>..] 와 같은가(시간이 늘 같게 비교).</summary>
    public bool AuthCodeMatches(byte[] stored, int offset)
    {
        if (_mac is null)
        {
            _hmac.TransformFinalBlock(new byte[0], 0, 0);
            _mac = _hmac.Hash ?? new byte[0];
        }
        if (_mac.Length < WinZipAesKeys.AuthCodeLength || stored.Length - offset < WinZipAesKeys.AuthCodeLength) return false;
        int diff = 0;
        for (int i = 0; i < WinZipAesKeys.AuthCodeLength; i++) diff |= _mac[i] ^ stored[offset + i];
        return diff == 0;
    }

    private void Refill()
    {
        for (int b = 0; b < BlocksPerRefill; b++)
        {
            // 리틀 엔디언 128비트 카운터 +1 (첫 블록은 1)
            for (int i = 0; i < 16 && ++_counter[i] == 0; i++) { }
            Buffer.BlockCopy(_counter, 0, _counterBlocks, b * 16, 16);
        }
        _ecb.TransformBlock(_counterBlocks, 0, _counterBlocks.Length, _keyStream, 0);
        _keyPos = 0;
    }

    public void Dispose()
    {
        _ecb.Dispose();
        _aes.Dispose();
        _hmac.Dispose();
    }
}
