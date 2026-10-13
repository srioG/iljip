#nullable enable
using System;

namespace Iljip.Services;

// ★ 공유 소스(W2-10): 일집 본체(net8.0-windows)와 자동 풀림 스텁(Iljip.SfxStub — .NET Framework 4.6.2)이 같은 파일을 컴파일한다.
//   두 쪽 모두에 있는 API 만 쓸 것(WinZipAesCore.cs 머리 주석 참고).

/// <summary>ZIP 의 CRC-32(다항식 0xEDB88320, 시작 0xFFFFFFFF·끝에 뒤집기). ZipCrypto 키 갱신도 이 표를 쓴다.</summary>
internal static class ZipCrc32
{
    public static readonly uint[] Table = BuildTable();

    private static uint[] BuildTable()
    {
        var t = new uint[256];
        for (uint n = 0; n < 256; n++)
        {
            uint c = n;
            for (int k = 0; k < 8; k++) c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
            t[n] = c;
        }
        return t;
    }

    /// <summary>
    /// 이어서 계산한다. 처음에는 <paramref name="crc"/> = 0, 끝난 값이 곧 CRC(안에서 뒤집기를 앞뒤로 함 — 여러 번 나눠 불러도 같은 값).
    /// </summary>
    public static uint Append(uint crc, byte[] buffer, int offset, int count)
    {
        uint c = ~crc;
        for (int i = 0; i < count; i++) c = Table[(c ^ buffer[offset + i]) & 0xFF] ^ (c >> 8);
        return ~c;
    }
}

/// <summary>
/// 전통 PKWARE 암호(ZipCrypto)의 키 셋. 암호 바이트로 시작하고, 12바이트 암호 머리를 풀어 마지막 바이트로 암호를 확인한 뒤 데이터를 푼다.
/// </summary>
internal sealed class ZipCryptoKeys
{
    /// <summary>암호 머리 길이.</summary>
    public const int HeaderLength = 12;

    private uint _k0 = 0x12345678, _k1 = 0x23456789, _k2 = 0x34567890;

    public ZipCryptoKeys(byte[] password)
    {
        foreach (byte c in password) Update(c);
    }

    private void Update(byte c)
    {
        _k0 = ZipCrc32.Table[(_k0 ^ c) & 0xFF] ^ (_k0 >> 8);
        _k1 = unchecked((_k1 + (_k0 & 0xFF)) * 134775813 + 1);
        _k2 = ZipCrc32.Table[(_k2 ^ (byte)(_k1 >> 24)) & 0xFF] ^ (_k2 >> 8);
    }

    /// <summary>한 바이트를 풀고 키를 갱신한다.</summary>
    public byte DecryptByte(byte cipher)
    {
        uint t = (_k2 | 2) & 0xFFFF;
        byte plain = (byte)(cipher ^ (byte)((t * (t ^ 1)) >> 8));
        Update(plain);
        return plain;
    }

    /// <summary>그 자리에서 푼다.</summary>
    public void Decrypt(byte[] buffer, int offset, int count)
    {
        for (int i = 0; i < count; i++) buffer[offset + i] = DecryptByte(buffer[offset + i]);
    }

    /// <summary>암호 바이트로 12바이트 암호 머리를 풀어 마지막 바이트가 받아들일 값 중 하나인지(맞으면 키는 데이터 첫 바이트를 풀 차례).</summary>
    public static bool CheckHeader(byte[] password, byte[] head12, byte[] accepted, out ZipCryptoKeys keys)
    {
        keys = new ZipCryptoKeys(password);
        byte last = 0;
        for (int i = 0; i < HeaderLength && i < head12.Length; i++) last = keys.DecryptByte(head12[i]);
        return Array.IndexOf(accepted, last) >= 0;
    }
}
