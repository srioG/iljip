#nullable enable
using System;
using System.IO;
using System.Text;

namespace Iljip.Services;

// ★ 공유 소스(W2-10 검증 지적 2차): 일집 본체(SfxBuilder·ArchiveSniffer)와 자동 풀림 스텁(SfxPayload — .NET Framework 4.6.2)이 같은 규칙으로 꼬리를 찾는다.
//   두 쪽 모두에 있는 API 만 쓸 것(Span·BinaryPrimitives 없음 — BitConverter 는 Windows 에서 리틀 엔디언).

/// <summary>
/// 일집 자동 풀림 exe 의 꼬리(<c>'ILJIPSFX'</c> + ZIP 데이터 시작 위치 8 + 데이터 길이 8, 리틀 엔디언) 찾기.
/// <list type="bullet">
/// <item>꼬리는 파일 맨 끝에 있고 데이터 시작 + 길이 + 24 가 꼬리 끝과 맞아야 한다(잘림·덧붙여짐은 꼬리가 없는 것 = 손상).</item>
/// <item>단 만든 exe 에 나중에 코드 서명(Authenticode — 회사가 SmartScreen·백신 오탐을 줄이려고 서명하는 경우)을 하면 서명 도구가 파일 끝을
///   8바이트 경계까지 0 으로 채우고 서명 표를 덧붙인다. 그래서 PE 머리의 보안 디렉터리(데이터 디렉터리 4번)가 가리키는 서명 표가
///   <b>정확히 파일 끝까지</b>면 그 앞(0 채움 0~7바이트를 건너)에서 찾는다(W2-10 검증 지적 2차 — 예전엔 서명하면 '손상'이 됐음).
///   서명 표 뒤에 무엇이 더 붙었거나 보안 디렉터리가 파일 끝을 가리키지 않으면 예전처럼 손상이다.</item>
/// </list>
/// </summary>
internal static class SfxTrailer
{
    /// <summary>꼬리 머리 글자(8바이트 ASCII).</summary>
    public const string Magic = "ILJIPSFX";

    /// <summary>꼬리 길이: 머리 8 + 데이터 시작 위치 8 + 데이터 길이 8.</summary>
    public const int Length = 24;

    /// <summary>ZIP 데이터의 가장 작은 길이(빈 ZIP = 끝 머리 22바이트).</summary>
    private const int MinDataLength = 22;

    private static readonly byte[] MagicBytes = Encoding.ASCII.GetBytes(Magic);

    /// <summary>
    /// 꼬리를 찾아 ZIP 데이터의 시작·길이를 돌려준다(위 규칙). 없으면 false. 스트림 위치는 바뀐다.
    /// 읽기 실패(IOException 등)는 부르는 쪽이 받는다.
    /// </summary>
    public static bool TryLocate(Stream s, out long dataStart, out long dataLength)
    {
        long len = s.Length;
        if (TryAt(s, len, out dataStart, out dataLength)) return true;

        long table = SignatureTableStart(s, len);
        if (table <= 0) return false;
        // 꼬리 끝 = 서명 표 시작 − 0 채움(0~7바이트). 채움 자리는 모두 0 이어야 한다.
        for (int pad = 0; pad < 8; pad++)
        {
            long end = table - pad;
            if (pad > 0)
            {
                byte[]? b = ReadAt(s, end, 1);
                if (b is null || b[0] != 0) return false;
            }
            if (TryAt(s, end, out dataStart, out dataLength)) return true;
        }
        return false;
    }

    /// <summary>[<paramref name="end"/> − 24, <paramref name="end"/>) 가 꼬리이고 데이터 시작 + 길이 + 24 = <paramref name="end"/> 인가.</summary>
    private static bool TryAt(Stream s, long end, out long dataStart, out long dataLength)
    {
        dataStart = dataLength = 0;
        if (end < Length + MinDataLength) return false;
        byte[]? t = ReadAt(s, end - Length, Length);
        if (t is null) return false;
        for (int i = 0; i < MagicBytes.Length; i++)
            if (t[i] != MagicBytes[i]) return false;
        long start = BitConverter.ToInt64(t, 8);
        long length = BitConverter.ToInt64(t, 16);
        if (start <= 0 || length < MinDataLength || start > end || length > end || start + length + Length != end) return false;
        dataStart = start;
        dataLength = length;
        return true;
    }

    /// <summary>
    /// PE 보안 디렉터리가 가리키는 서명 표(WIN_CERTIFICATE 들)의 시작 위치 — 표가 8바이트 경계에서 시작해 정확히 파일 끝까지 이어질 때만. 아니면 -1.
    /// </summary>
    private static long SignatureTableStart(Stream s, long len)
    {
        byte[]? dos = ReadAt(s, 0, 64);
        if (dos is null || dos[0] != (byte)'M' || dos[1] != (byte)'Z') return -1;
        int pe = BitConverter.ToInt32(dos, 0x3C);
        if (pe < 64 || pe > 64 * 1024 || pe + 24 + 2 > len) return -1;
        byte[]? coff = ReadAt(s, pe, 24 + 2);
        if (coff is null || coff[0] != (byte)'P' || coff[1] != (byte)'E' || coff[2] != 0 || coff[3] != 0) return -1;
        int optionalSize = BitConverter.ToUInt16(coff, 20);
        int magic = BitConverter.ToUInt16(coff, 24);
        // 데이터 디렉터리 개수 칸·보안 디렉터리(4번) 칸의 위치: PE32 는 92·128, PE32+ 는 108·144(선택 머리 처음 기준)
        int countAt = magic == 0x10B ? 92 : magic == 0x20B ? 108 : -1;
        if (countAt < 0) return -1;
        int securityAt = countAt + 4 + 4 * 8;
        if (securityAt + 8 > optionalSize) return -1;
        byte[]? opt = ReadAt(s, pe + 24, securityAt + 8);
        if (opt is null || BitConverter.ToUInt32(opt, countAt) < 5) return -1;
        long table = BitConverter.ToUInt32(opt, securityAt);
        long size = BitConverter.ToUInt32(opt, securityAt + 4);
        if (table <= 0 || size < 8 || table % 8 != 0 || table + size != len) return -1;
        // 첫 WIN_CERTIFICATE 머리(길이 4 + 판 2 + 종류 2)의 길이가 표 안에 들어야
        byte[]? head = ReadAt(s, table, 8);
        if (head is null) return -1;
        long first = BitConverter.ToUInt32(head, 0);
        return first >= 8 && first <= size ? table : -1;
    }

    /// <summary>위치 <paramref name="pos"/> 에서 <paramref name="count"/> 바이트(모자라면 null).</summary>
    private static byte[]? ReadAt(Stream s, long pos, int count)
    {
        if (pos < 0 || pos + count > s.Length) return null;
        s.Position = pos;
        var buf = new byte[count];
        int got = 0;
        while (got < count)
        {
            int n = s.Read(buf, got, count - got);
            if (n <= 0) return null;
            got += n;
        }
        return buf;
    }
}
