using System.Buffers.Binary;
using System.IO;

namespace Iljip.Services;

/// <summary>
/// 헤더(파일 이름) 암호 탐지. 7z.dll 은 '헤더 암호인데 암호가 틀림'과 '손상'을 똑같은 "Invalid archive: open/read error! Is it encrypted…" 로만
/// 알려 주므로, 열기 전에 머리 바이트를 직접 읽어 구분한다:
/// 헤더 암호인데 암호가 없으면 7z.dll 을 부르지 않고 <see cref="PasswordRequiredException"/>, 7z.dll 열기가 실패했는데 헤더 암호면
/// <see cref="WrongPasswordException"/>(손상일 수도 있다는 말을 덧붙임), 아니면 손상.
/// </summary>
public static class ArchiveHeaderProbe
{
    private const int MaxNextHeaderRead = 1024 * 1024;
    private static readonly byte[] SevenZipAesCoderId = { 0x06, 0xF1, 0x07, 0x01 };

    /// <summary>7z 의 '다음 머리'가 7zAES 로 암호화돼 있는지(파일 경로).</summary>
    public static bool IsSevenZipHeaderEncrypted(string path)
    {
        try
        {
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            return IsSevenZipHeaderEncrypted(fs);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// 7z 규칙(모든 픽스처와 7z.dll EncryptHeaders 로 만든 파일로 검증): 32바이트 시작 머리의 NextHeaderOffset·Size 로 다음 머리를 읽어,
    /// 첫 바이트가 0x17(kEncodedHeader)이고 그 안에 7zAES 코더 ID(06 F1 07 01)가 있으면 헤더 암호.
    /// 스트림은 7z 시작 위치가 0 인 것(분할·SFX 는 이어 붙이거나 잘라 낸 스트림)이어야 한다. 못 읽으면 false.
    /// </summary>
    public static bool IsSevenZipHeaderEncrypted(Stream s)
    {
        try
        {
            if (s.Length < 32) return false;
            var start = ReadAt(s, 0, 32);
            if (start.Length < 32 || start[0] != 0x37 || start[1] != 0x7A) return false;
            ulong nextOffset = BinaryPrimitives.ReadUInt64LittleEndian(start.AsSpan(12, 8));
            ulong nextSize = BinaryPrimitives.ReadUInt64LittleEndian(start.AsSpan(20, 8));
            if (nextSize == 0 || nextOffset > (ulong)s.Length) return false;
            long pos = 32 + (long)nextOffset;
            if (pos >= s.Length) return false;
            int n = (int)Math.Min(Math.Min(nextSize, MaxNextHeaderRead), (ulong)(s.Length - pos));
            var next = ReadAt(s, pos, n);
            return next.Length > 0 && next[0] == 0x17 && next.AsSpan().IndexOf(SevenZipAesCoderId) >= 0;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>RAR 머리에서 읽어 낸 것.</summary>
    public readonly record struct RarInfo(bool IsRar, bool IsRar5, bool HeaderEncrypted, bool IsVolume);

    public static RarInfo ProbeRar(string path)
    {
        try
        {
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            return ProbeRar(ReadAt(fs, 0, 64));
        }
        catch
        {
            return default;
        }
    }

    /// <summary>
    /// RAR 머리(서명 위치 0) 판정 — 실물 픽스처가 없어 규격대로 만든 합성 바이트로만 시험했다.
    /// RAR5: 서명 8바이트 뒤 첫 머리(CRC32 4 + 크기 vint + 종류 vint)의 종류가 4(암호 머리)면 헤더 암호. 1(주 머리)이면 그 플래그의 0x0001 이 볼륨.
    /// RAR4: 서명 7바이트 뒤 MAIN_HEAD(종류 0x73)의 플래그 0x0080 이 헤더 암호, 0x0001 이 볼륨.
    /// </summary>
    public static RarInfo ProbeRar(ReadOnlySpan<byte> b)
    {
        ReadOnlySpan<byte> rar5 = stackalloc byte[] { 0x52, 0x61, 0x72, 0x21, 0x1A, 0x07, 0x01, 0x00 };
        ReadOnlySpan<byte> rar4 = stackalloc byte[] { 0x52, 0x61, 0x72, 0x21, 0x1A, 0x07, 0x00 };
        if (b.StartsWith(rar5))
        {
            int p = 8 + 4;   // 머리 CRC32 건너뜀
            if (!ReadVint(b, ref p, out _) || !ReadVint(b, ref p, out ulong type)) return new RarInfo(true, true, false, false);
            if (type == 4) return new RarInfo(true, true, true, false);
            bool volume = false;
            if (type == 1 && ReadVint(b, ref p, out ulong headerFlags))
            {
                if ((headerFlags & 0x0001) != 0 && !ReadVint(b, ref p, out _)) return new RarInfo(true, true, false, false);   // 덧붙은 영역 크기
                if ((headerFlags & 0x0002) != 0 && !ReadVint(b, ref p, out _)) return new RarInfo(true, true, false, false);   // 데이터 크기
                if (ReadVint(b, ref p, out ulong archiveFlags)) volume = (archiveFlags & 0x0001) != 0;
            }
            return new RarInfo(true, true, false, volume);
        }
        if (b.StartsWith(rar4))
        {
            int m = 7;   // MARK_HEAD(=서명) 뒤 MAIN_HEAD: CRC 2, 종류 1, 플래그 2, 크기 2
            if (b.Length < m + 7 || b[m + 2] != 0x73) return new RarInfo(true, false, false, false);
            int flags = BinaryPrimitives.ReadUInt16LittleEndian(b.Slice(m + 3, 2));
            return new RarInfo(true, false, (flags & 0x0080) != 0, (flags & 0x0001) != 0);
        }
        return default;
    }

    private static readonly System.Text.RegularExpressions.Regex RarPartName =
        new(@"^(?<base>.*\.part)(?<n>\d+)\.rar$", System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.CultureInvariant);

    /// <summary>
    /// 여러 조각 RAR 에서 빠진 조각 이름(경로 없이)을 추정한다 — 열기·풀기가 실패했을 때만 부른다.
    /// 이름 규칙: 새 방식 '이름.partN.rar'(N 자릿수 유지), 옛 방식 '이름.rar' → '이름.r00', '이름.r01' ….
    /// 있는 조각을 차례로 모은 뒤, 마지막 조각의 끝 머리가 '뒤에 조각이 더 있음'이면 그다음 이름을 돌려준다(아니면 null — 손상으로 봄).
    /// 끝 머리: RAR5 는 끝 머리(종류 5)의 플래그 0x0001, RAR4 는 ENDARC_HEAD(0x7B)의 플래그 0x0001.
    /// ★ 실물 RAR 픽스처가 없어(반디집은 RAR 를 못 만듦) 규격으로 만든 합성 바이트로만 시험했다.
    /// </summary>
    public static string? MissingRarVolume(string anyVolumePath)
    {
        try
        {
            string dir = Path.GetDirectoryName(anyVolumePath) ?? string.Empty;
            string name = Path.GetFileName(anyVolumePath);
            var names = new List<string>();
            var m = RarPartName.Match(name);
            Func<int, string> nameOf;
            if (m.Success)
            {
                int digits = m.Groups["n"].Value.Length;
                string b = m.Groups["base"].Value;
                nameOf = k => $"{b}{(k + 1).ToString().PadLeft(digits, '0')}.rar";
            }
            else if (name.EndsWith(".rar", StringComparison.OrdinalIgnoreCase))
            {
                string b = name[..^4];
                nameOf = k => k == 0 ? name : $"{b}.r{k - 1:00}";
            }
            else
            {
                return null;
            }
            for (int k = 0; k < 1000; k++)
            {
                string n = nameOf(k);
                if (!File.Exists(Path.Combine(dir, n))) break;
                names.Add(n);
            }
            if (names.Count == 0) return nameOf(0);
            string last = Path.Combine(dir, names[^1]);
            return RarHasNextVolume(last) ? nameOf(names.Count) : null;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>RAR 조각 파일 끝의 끝 머리에 '다음 조각 있음' 표시가 있는지.</summary>
    internal static bool RarHasNextVolume(string volumePath)
    {
        using var fs = new FileStream(volumePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        var head = ReadAt(fs, 0, 8);
        int tailLen = (int)Math.Min(fs.Length, 64);
        var tail = ReadAt(fs, fs.Length - tailLen, tailLen);
        return RarTailSaysMoreVolumes(head, tail);
    }

    /// <summary>
    /// 끝 머리 판정(바이트만). RAR5 끝 머리 = CRC32(4) + 크기 vint + 종류 5 + 머리 플래그 vint + 끝 플래그 vint(0x0001 = 마지막 조각 아님),
    /// 파일 맨 끝에 온다. RAR4 ENDARC_HEAD = CRC(2) + 종류 0x7B + 플래그(2, 0x0001 = 다음 조각 있음) + 크기(2), 크기가 파일 끝까지.
    /// </summary>
    internal static bool RarTailSaysMoreVolumes(ReadOnlySpan<byte> head, ReadOnlySpan<byte> tail)
    {
        var info = ProbeRar(head);
        if (!info.IsRar) return false;
        if (info.IsRar5)
        {
            // 끝 머리는 보통 8바이트(CRC 4 + 03 05 xx yy). 뒤에서 앞으로 '크기 = 남은 길이'인 자리를 찾는다.
            for (int start = tail.Length - 7; start >= 0; start--)
            {
                int p = start + 4;
                if (!ReadVint(tail, ref p, out ulong size)) continue;
                int bodyStart = p;
                if ((ulong)(tail.Length - bodyStart) != size) continue;
                if (!ReadVint(tail, ref p, out ulong type) || type != 5) continue;
                if (!ReadVint(tail, ref p, out ulong headerFlags)) continue;
                if ((headerFlags & 0x0001) != 0 && !ReadVint(tail, ref p, out _)) continue;
                if (!ReadVint(tail, ref p, out ulong endFlags)) continue;
                return (endFlags & 0x0001) != 0;
            }
            return false;
        }
        for (int start = tail.Length - 7; start >= 0; start--)
        {
            if (tail[start + 2] != 0x7B) continue;
            int size = BinaryPrimitives.ReadUInt16LittleEndian(tail.Slice(start + 5, 2));
            if (size != tail.Length - start) continue;
            int flags = BinaryPrimitives.ReadUInt16LittleEndian(tail.Slice(start + 3, 2));
            return (flags & 0x0001) != 0;
        }
        return false;
    }

    /// <summary>
    /// RAR5 파일이 끝 머리(종류 5, CRC 까지 맞음)로 끝나는지 — 끝이 잘린 RAR5 를 7z.dll 없이 알아보는 데 쓴다(W1-B 2차 검증 지적:
    /// SharpCompress 는 머리 사이에서 잘린 RAR5 를 읽은 데까지만 풀고 오류를 내지 않음). RAR5 는 끝 머리가 꼭 있다.
    /// RAR5 가 아니거나(RAR4 는 옛 판에 끝 머리가 없을 수 있음) 머리 암호라 읽을 수 없으면 판단하지 않고 true.
    /// </summary>
    public static bool Rar5HasEndHeader(string path)
    {
        try
        {
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            var info = ProbeRar(ReadAt(fs, 0, 64));
            if (!info.IsRar5 || info.HeaderEncrypted) return true;
            int tailLen = (int)Math.Min(fs.Length, 64);
            var tail = ReadAt(fs, fs.Length - tailLen, tailLen);
            for (int start = tail.Length - 7; start >= 0; start--)
            {
                int p = start + 4;
                if (!ReadVint(tail, ref p, out ulong size) || (ulong)(tail.Length - p) != size) continue;
                int q = p;
                if (!ReadVint(tail, ref q, out ulong type) || type != 5) continue;
                uint crc = BinaryPrimitives.ReadUInt32LittleEndian(tail.AsSpan(start, 4));
                if (crc == ArchiveSniffer.Crc32Of(tail.AsSpan(start + 4))) return true;
            }
            return false;
        }
        catch
        {
            return true;
        }
    }

    private static bool ReadVint(ReadOnlySpan<byte> b, ref int p, out ulong value)
    {
        value = 0;
        for (int shift = 0; shift < 64 && p < b.Length; shift += 7)
        {
            byte c = b[p++];
            value |= (ulong)(c & 0x7F) << shift;
            if ((c & 0x80) == 0) return true;
        }
        return false;
    }

    private static byte[] ReadAt(Stream s, long pos, int count)
    {
        s.Position = pos;
        var buf = new byte[count];
        int got = 0;
        while (got < count)
        {
            int n = s.Read(buf, got, count - got);
            if (n <= 0) break;
            got += n;
        }
        return got == count ? buf : buf[..got];
    }
}
