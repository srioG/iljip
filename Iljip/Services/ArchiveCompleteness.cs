using System.Buffers.Binary;
using System.IO;

namespace Iljip.Services;

/// <summary>형식별 구조 검사(<see cref="ArchiveCompleteness"/>)의 종류.</summary>
internal enum StructureKind
{
    /// <summary>따로 보지 않는다(7z·ZIPX·WIM — 7z.dll 오류와 검증기로 충분, 실측).</summary>
    None,
    Rar,
    Lzh,
    /// <summary>ISO9660·UDF 디스크 이미지.</summary>
    DiscImage,
    Cab,
    Xz,
}

/// <summary>
/// 압축 파일의 '구조가 끝까지 있는지'를 바이트로 본다(W2-01 — 1차 W1-B 검증 지적 (a)).
/// 7z.dll 은 머리와 머리 사이에서 정확히 잘린 RAR4·LZH 를 '읽은 데까지만' 열고 오류 표시도 내지 않아, 뒤쪽 항목이 목록에서 사라진 채
/// '완료'로 끝났다(실측: LZH 2,026개 절단 중 7개). 그래서 열 때(목록)와 풀기 직전에 이 검사를 먼저 해, 잘렸으면 아무것도 쓰지 않고 알린다.
/// 판단할 수 없으면(머리 암호·모르는 모양) null — 7z.dll 과 검증기(<see cref="ExtractionVerifier"/>)에 맡긴다. 예외는 내지 않는다.
/// <list type="bullet">
/// <item>RAR4: MARK → MAIN → FILE… → ENDARC(0x7B) 블록을 따라간다. UNP_VER ≥ 29(RAR 3.x 이상)인 파일이 있는데 ENDARC 없이 끝나면 잘림
///   (RAR 3.x 이상은 '-en' 을 주지 않는 한 늘 ENDARC 를 쓴다 — '-en' 으로 만든 드문 파일은 잘림으로 거절된다). 블록이 파일 끝을 넘으면 잘림.</item>
/// <item>RAR5: 끝 머리(종류 5)까지 머리를 따라간다. RAR5 는 끝 머리가 꼭 있다.</item>
/// <item>LZH: 레벨 0·1·2·3 머리를 따라가며 '머리 크기 + 압축 크기'를 더하고, 마지막에 0x00 끝 바이트가 있어야 한다(엄격 — 옛 도구가 0x00 없이 쓴
///   정상 LZH 도 잘림으로 거절된다. 문구는 '다른 프로그램으로 확인해 보세요'를 붙인 잘림).</item>
/// <item>ISO·UDF: 7z.dll 의 '갑자기 끝남' 표시는 정상 이미지에도 떠서(반디집 iso_default) 믿지 않는다. 주 볼륨 설명자(PVD)의
///   볼륨 크기(0x8050 LE32) × 블록 크기(0x8080 LE16) ≤ 파일 길이인지, UDF 는 끝쪽 기준점(AVDP — 마지막 섹터 또는 N-257)이 있는지 본다.
///   둘 다 아니면 잘림.</item>
/// <item>CAB: 머리의 전체 크기(cbCabinet, 8 LE32)가 파일 길이보다 크면 잘림.</item>
/// <item>XZ: 끝의 스트림 꼬리(… 'YZ', CRC32 까지 맞음)가 있어야 한다(끝의 0 채움은 뗌).</item>
/// </list>
/// </summary>
internal static class ArchiveCompleteness
{
    /// <summary>LZH 를 잘림으로 거절할 때 덧붙이는 말(옛 도구가 끝 바이트를 빼고 쓴 정상 파일일 수도 있어서).</summary>
    internal const string LzhNote = " 다른 프로그램으로 확인해 보세요.";

    /// <summary>스트림(위치 0 이 압축 시작)의 구조를 본다. 문제 없거나 판단할 수 없으면 null. 스트림 위치는 바뀐다.</summary>
    public static ArchiveException? Check(Stream s, StructureKind kind)
    {
        try
        {
            return kind switch
            {
                StructureKind.Rar => CheckSingleRar(s),
                StructureKind.Lzh => CheckLzh(s),
                StructureKind.DiscImage => CheckDiscImage(s),
                StructureKind.Cab => CheckCab(s),
                StructureKind.Xz => CheckXz(s),
                _ => null,
            };
        }
        catch (IOException)
        {
            return null;   // 읽기 문제는 여기서 판단하지 않는다(엔진이 같은 파일을 다시 연다)
        }
    }

    // ───────────────────────────── RAR ─────────────────────────────

    /// <summary>RAR 머리를 따라간 결과.</summary>
    internal enum RarWalk { Complete, Truncated, Corrupt, Unknown }

    private static ArchiveException? CheckSingleRar(Stream s)
    {
        var r = WalkRar(s, out _);
        return r switch
        {
            RarWalk.Truncated => CorruptArchiveException.Truncated(),
            RarWalk.Corrupt => new CorruptArchiveException(),
            _ => null,
        };
    }

    /// <summary>
    /// 여러 조각 RAR('이름.partN.rar' 또는 '이름.rar' + '.r00' …): 있는 조각을 차례로 모아 조각마다 구조를 보고,
    /// 마지막 조각의 끝 머리가 '다음 조각 있음'이면 그 이름으로 <see cref="VolumeMissingException"/>, 잘린 조각은 <see cref="VolumeIncompleteException"/>.
    /// 첫 조각부터 없으면 첫 조각 이름으로 알린다.
    /// </summary>
    public static ArchiveException? CheckRarVolumes(string anyVolumePath)
    {
        try
        {
            var (existing, nameOf) = RarVolumeChain(anyVolumePath);
            if (nameOf is null) return null;
            if (existing.Count == 0) return new VolumeMissingException(nameOf(0));
            for (int i = 0; i < existing.Count; i++)
            {
                RarWalk r;
                bool more;
                using (var fs = new FileStream(existing[i], FileMode.Open, FileAccess.Read, FileShare.Read))
                    r = WalkRar(fs, out more);
                string name = Path.GetFileName(existing[i]);
                if (r == RarWalk.Truncated) return new VolumeIncompleteException(name);
                if (r == RarWalk.Corrupt) return new CorruptArchiveException();
                if (r == RarWalk.Unknown) return null;   // 머리 암호 등 — 7z.dll 에 맡김
                if (i == existing.Count - 1 && more) return new VolumeMissingException(nameOf(existing.Count));
            }
            return null;
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>
    /// 여러 조각 RAR 의 조각 이름 규칙과 지금 있는 조각(첫 조각부터 빈 번호 전까지). 새 방식 '이름.partN.rar'(자릿수 유지),
    /// 옛 방식 '이름.rar' → '이름.r00', '이름.r01' …. 이름이 둘 다 아니면 (빈 목록, null).
    /// </summary>
    internal static (List<string> Existing, Func<int, string>? NameOf) RarVolumeChain(string anyVolumePath)
    {
        string dir = Path.GetDirectoryName(anyVolumePath) ?? string.Empty;
        string name = Path.GetFileName(anyVolumePath);
        Func<int, string> nameOf;
        var m = System.Text.RegularExpressions.Regex.Match(name, @"^(?<base>.*\.part)(?<n>\d+)\.rar$",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.CultureInvariant);
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
            return (new List<string>(), null);
        }
        var list = new List<string>();
        for (int k = 0; k < 10000; k++)
        {
            string p = Path.Combine(dir, nameOf(k));
            if (!File.Exists(p)) break;
            list.Add(p);
        }
        return (list, nameOf);
    }

    /// <summary>RAR4·RAR5 머리를 따라간다. <paramref name="moreVolumes"/> = 끝 머리에 '다음 조각 있음' 표시.</summary>
    internal static RarWalk WalkRar(Stream s, out bool moreVolumes)
    {
        moreVolumes = false;
        var sig = ReadAt(s, 0, 8);
        if (sig.Length >= 8 && sig[..8].SequenceEqual(new byte[] { 0x52, 0x61, 0x72, 0x21, 0x1A, 0x07, 0x01, 0x00 }))
            return WalkRar5(s, out moreVolumes);
        if (sig.Length >= 7 && sig[..7].SequenceEqual(new byte[] { 0x52, 0x61, 0x72, 0x21, 0x1A, 0x07, 0x00 }))
            return WalkRar4(s, out moreVolumes);
        return RarWalk.Unknown;   // 서명이 없으면 7z.dll 이 손상으로 알린다
    }

    private static RarWalk WalkRar4(Stream s, out bool moreVolumes)
    {
        moreVolumes = false;
        long len = s.Length;
        long pos = 7;
        bool modern = false;   // UNP_VER ≥ 29(RAR 3.x 이상) 파일이 있었나
        bool anyFile = false;
        bool first = true;
        while (true)
        {
            // 끝 머리 없이 블록 경계에서 끝남: 3.x 이상 파일이 있었으면 잘림(3.x 이상은 -en 이 아니면 늘 끝 머리를 씀),
            // 파일이 하나도 없으면(주 머리 바로 뒤에서 끊김 — WinRAR 은 빈 RAR 을 만들지 않음) 역시 잘림.
            if (pos == len) return modern || !anyFile ? RarWalk.Truncated : RarWalk.Complete;
            if (pos + 7 > len) return first ? RarWalk.Corrupt : RarWalk.Truncated;
            var h = ReadAt(s, pos, (int)Math.Min(40, len - pos));
            byte type = h[2];
            int flags = BinaryPrimitives.ReadUInt16LittleEndian(h.AsSpan(3, 2));
            int size = BinaryPrimitives.ReadUInt16LittleEndian(h.AsSpan(5, 2));
            // 표시 블록 바로 뒤는 주 머리(0x73)여야 한다 — 아니면 서명만 RAR 인 깨진 파일(잘림이 아님)
            if (first && type != 0x73) return RarWalk.Corrupt;
            first = false;
            if (size < 7) return RarWalk.Corrupt;
            if (pos + size > len) return RarWalk.Truncated;
            // 머리 확인값(형식부터 머리 끝까지 CRC32 의 아래 16비트 — unrar 와 같은 계산)이 틀리면 손상(잘림이 아님)
            var whole = ReadAt(s, pos, size);
            ushort crc = BinaryPrimitives.ReadUInt16LittleEndian(whole.AsSpan(0, 2));
            if (crc != (ushort)(ArchiveSniffer.Crc32Of(whole.AsSpan(2)) & 0xFFFF)) return RarWalk.Corrupt;
            long data = 0;
            switch (type)
            {
                case 0x73:   // MAIN_HEAD
                    if ((flags & 0x0080) != 0) return RarWalk.Unknown;   // 머리 암호 — 뒤 머리를 읽을 수 없다
                    break;
                case 0x7B:   // ENDARC_HEAD
                    moreVolumes = (flags & 0x0001) != 0;
                    return RarWalk.Complete;
                case 0x74:   // FILE_HEAD
                case 0x7A:   // NEWSUB_HEAD(주석·복구 기록 등)
                    if (size < 32 || h.Length < 32) return RarWalk.Corrupt;
                    data = BinaryPrimitives.ReadUInt32LittleEndian(h.AsSpan(7, 4));
                    if ((flags & 0x0100) != 0 && h.Length >= 36)   // LHD_LARGE: 4GB 넘는 파일의 높은 32비트
                        data |= (long)BinaryPrimitives.ReadUInt32LittleEndian(h.AsSpan(32, 4)) << 32;
                    if (type == 0x74)
                    {
                        anyFile = true;
                        if (h[24] >= 29) modern = true;
                    }
                    break;
                default:
                    if ((flags & 0x8000) != 0)   // LONG_BLOCK: 머리 뒤에 ADD_SIZE 만큼 데이터
                    {
                        if (h.Length < 11) return RarWalk.Truncated;
                        data = BinaryPrimitives.ReadUInt32LittleEndian(h.AsSpan(7, 4));
                    }
                    break;
            }
            long next = pos + size + data;
            if (next > len) return RarWalk.Truncated;
            pos = next;
        }
    }

    private static RarWalk WalkRar5(Stream s, out bool moreVolumes)
    {
        moreVolumes = false;
        long len = s.Length;
        long pos = 8;
        bool first = true;
        while (true)
        {
            if (pos >= len) return RarWalk.Truncated;   // RAR5 는 끝 머리가 꼭 있다
            var h = ReadAt(s, pos, (int)Math.Min(64, len - pos));
            int p = 4;   // 머리 CRC32
            if (!ReadVint(h, ref p, out ulong headerSize)) return h.Length < 64 && !first ? RarWalk.Truncated : RarWalk.Corrupt;
            if (headerSize == 0 || headerSize > 2 * 1024 * 1024) return RarWalk.Corrupt;
            int q = p;
            // 서명 바로 뒤는 주 머리(1) 또는 머리 암호(4)여야 한다 — 아니면 서명만 RAR 인 깨진 파일(잘림이 아님)
            if (first && (!ReadVint(h, ref q, out ulong firstType) || firstType is not (1 or 4))) return RarWalk.Corrupt;
            first = false;
            long headerEnd = pos + p + (long)headerSize;
            if (headerEnd > len) return RarWalk.Truncated;
            // 머리 확인값(머리 크기 칸부터 머리 끝까지의 CRC32)이 틀리면 손상(잘림이 아님)
            var whole = ReadAt(s, pos, (int)(headerEnd - pos));
            if (BinaryPrimitives.ReadUInt32LittleEndian(whole.AsSpan(0, 4)) != ArchiveSniffer.Crc32Of(whole.AsSpan(4))) return RarWalk.Corrupt;
            if (!ReadVint(h, ref p, out ulong type) || !ReadVint(h, ref p, out ulong headerFlags)) return RarWalk.Corrupt;
            if (type == 4) return RarWalk.Unknown;   // 머리 암호 — 뒤 머리를 읽을 수 없다
            if ((headerFlags & 0x0001) != 0 && !ReadVint(h, ref p, out _)) return RarWalk.Corrupt;   // 덧붙은 영역 크기
            ulong dataSize = 0;
            if ((headerFlags & 0x0002) != 0 && !ReadVint(h, ref p, out dataSize)) return RarWalk.Corrupt;
            if (type == 5)
            {
                if (ReadVint(h, ref p, out ulong endFlags)) moreVolumes = (endFlags & 0x0001) != 0;
                return RarWalk.Complete;
            }
            if (dataSize > (ulong)len) return RarWalk.Truncated;
            long next = headerEnd + (long)dataSize;
            if (next > len) return RarWalk.Truncated;
            pos = next;
        }
    }

    // ───────────────────────────── LZH ─────────────────────────────

    private static ArchiveException? CheckLzh(Stream s)
    {
        long len = s.Length;
        long pos = 0;
        for (int guard = 0; guard < 1_000_000; guard++)
        {
            if (pos >= len) return LzhTruncated();   // 끝 바이트(0x00) 없이 끝남
            var h = ReadAt(s, pos, (int)Math.Min(32, len - pos));
            bool looksLikeHeader = h.Length >= 22 && h[2] == (byte)'-' && h[3] == (byte)'l' && h[6] == (byte)'-';
            // 0 이면 끝 표시. 단 레벨 2 머리는 첫 2바이트가 머리 크기라 아래 바이트가 0 일 수 있다(크기 256 등) — 그때는 머리로 본다.
            if (h[0] == 0 && !(looksLikeHeader && h[20] == 2)) return null;
            if (h.Length < 22) return LzhTruncated();
            if (!looksLikeHeader) return new CorruptArchiveException();
            long packed = BinaryPrimitives.ReadUInt32LittleEndian(h.AsSpan(7, 4));
            long next;
            switch (h[20])
            {
                case 0:
                    next = pos + h[0] + 2 + packed;
                    break;
                case 1:
                    next = pos + h[0] + 2 + packed;   // 레벨 1 의 7번 칸은 '압축 크기 + 확장 머리들'
                    break;
                case 2:
                    next = pos + BinaryPrimitives.ReadUInt16LittleEndian(h.AsSpan(0, 2)) + packed;
                    break;
                case 3:
                    if (h.Length < 28) return LzhTruncated();
                    next = pos + BinaryPrimitives.ReadUInt32LittleEndian(h.AsSpan(24, 4)) + packed;
                    break;
                default:
                    return new CorruptArchiveException();
            }
            if (next <= pos) return new CorruptArchiveException();
            if (next > len) return LzhTruncated();
            pos = next;
        }
        return null;
    }

    private static CorruptArchiveException LzhTruncated() => new LzhTruncatedException();

    /// <summary>LZH 잘림(끝 바이트 없음 포함) — 옛 도구가 끝 바이트 없이 쓴 정상 파일일 수도 있어 '다른 프로그램으로 확인' 을 덧붙인다.</summary>
    private sealed class LzhTruncatedException : CorruptArchiveException
    {
        public LzhTruncatedException()
            : base(TruncatedMessage + LzhNote, null, null, isTruncated: true)
        {
        }
    }

    // ───────────────────────────── ISO·UDF ─────────────────────────────

    private const int Sector = 2048;

    private static ArchiveException? CheckDiscImage(Stream s)
    {
        long len = s.Length;
        bool pvdFound = false, pvdOk = false;
        // 볼륨 설명자는 16번째 구역부터 이어진다(끝 표시 255 까지)
        for (long sector = 16; sector < 16 + 64 && (sector + 1) * Sector <= len; sector++)
        {
            var d = ReadAt(s, sector * Sector, 136);
            if (d.Length < 136 || d[1] != (byte)'C' || d[2] != (byte)'D' || d[3] != (byte)'0' || d[4] != (byte)'0' || d[5] != (byte)'1') break;
            if (d[0] == 255) break;
            if (d[0] == 1 && !pvdFound)
            {
                pvdFound = true;
                long space = BinaryPrimitives.ReadUInt32LittleEndian(d.AsSpan(80, 4));
                long block = BinaryPrimitives.ReadUInt16LittleEndian(d.AsSpan(128, 2));
                pvdOk = space > 0 && block > 0 && space * block <= len;
            }
        }
        bool udf = IsAvdp(s, 256);
        if (!pvdFound && !udf) return null;   // 모르는 모양 — 7z.dll 에 맡김
        long sectors = len / Sector;
        // 끝쪽 기준점: 마지막 구역 또는 그보다 256 앞(N-256). 256 구역의 첫 기준점을 끝쪽으로 잘못 세지 않는다(잘린 길이가 513 구역일 때).
        // ★ IMAPI2FS(윈도 굽기)는 UDF 내용 뒤를 기준점 사본으로 채운다 — 그 채움 안에서 잘리면 내용은 온전해 잘림으로 보지 않는다(풀면 다 맞음).
        bool endAvdp = udf && sectors > 257 && (IsAvdp(s, sectors - 1) || (sectors - 257 > 256 && IsAvdp(s, sectors - 257)));
        return endAvdp || pvdOk ? null : CorruptArchiveException.Truncated();
    }

    /// <summary>UDF 기준점(Anchor Volume Descriptor Pointer, 태그 2): 태그 확인값과 '자기 위치' 칸이 맞는지.</summary>
    private static bool IsAvdp(Stream s, long sector)
    {
        if (sector < 0 || (sector + 1) * Sector > s.Length) return false;
        var t = ReadAt(s, sector * Sector, 16);
        if (t.Length < 16 || BinaryPrimitives.ReadUInt16LittleEndian(t.AsSpan(0, 2)) != 2) return false;
        int sum = 0;
        for (int i = 0; i < 16; i++) if (i != 4) sum += t[i];
        return (byte)sum == t[4] && BinaryPrimitives.ReadUInt32LittleEndian(t.AsSpan(12, 4)) == (uint)sector;
    }

    // ───────────────────────────── CAB·XZ ─────────────────────────────

    private static ArchiveException? CheckCab(Stream s)
    {
        long len = s.Length;
        if (len < 12) return CorruptArchiveException.Truncated();
        var h = ReadAt(s, 0, 12);
        if (h.Length < 12 || h[0] != (byte)'M' || h[1] != (byte)'S' || h[2] != (byte)'C' || h[3] != (byte)'F') return null;
        long cb = BinaryPrimitives.ReadUInt32LittleEndian(h.AsSpan(8, 4));
        return cb > len ? CorruptArchiveException.Truncated() : null;
    }

    private static ArchiveException? CheckXz(Stream s)
    {
        long end = s.Length;
        // 스트림 뒤의 0 채움(4바이트 단위)은 xz 규격상 허용
        while (end >= 4)
        {
            var pad = ReadAt(s, end - 4, 4);
            if (pad.Length < 4 || pad[0] != 0 || pad[1] != 0 || pad[2] != 0 || pad[3] != 0) break;
            end -= 4;
        }
        if (end < 24) return CorruptArchiveException.Truncated();
        var f = ReadAt(s, end - 12, 12);
        if (f.Length < 12 || f[10] != (byte)'Y' || f[11] != (byte)'Z') return CorruptArchiveException.Truncated();
        uint crc = BinaryPrimitives.ReadUInt32LittleEndian(f.AsSpan(0, 4));
        return crc == ArchiveSniffer.Crc32Of(f.AsSpan(4, 6)) ? null : CorruptArchiveException.Truncated();
    }

    /// <summary>
    /// bzip2 가 끝 표시로 끝나는가: 마지막 스트림의 끝 표시(48비트 0x177245385090 — 바이트에 맞춰 있지 않음) + 전체 CRC 32비트 + 0~7비트 채움.
    /// 해제가 실패했을 때 '잘림'(끝 표시 없음)과 '데이터 손상'(끝 표시는 있음)을 가르는 데 쓴다. 뒤에 붙은 0 채움은 건너뛰어 본다.
    /// </summary>
    internal static bool Bzip2HasEndMarker(Stream s)
    {
        long len = s.Length;
        int zeros = 0;
        while (zeros < len && zeros < 64 * 1024)
        {
            var b = ReadAt(s, len - zeros - 1, 1);
            if (b.Length == 0 || b[0] != 0) break;
            zeros++;
        }
        foreach (int strip in Enumerable.Range(0, Math.Min(zeros, 16) + 1).Append(zeros).Distinct())
        {
            long end = len - strip;
            if (end < 11) continue;
            var tail = ReadAt(s, end - 11, 11);
            if (tail.Length < 11) continue;
            ulong hi = 0;   // 88비트를 위 24비트 + 아래 64비트로
            for (int i = 0; i < 3; i++) hi = (hi << 8) | tail[i];
            ulong lo = BinaryPrimitives.ReadUInt64BigEndian(tail.AsSpan(3, 8));
            for (int pad = 0; pad <= 7; pad++)
            {
                // 끝 표시는 끝에서 pad + 32 비트 앞에서 끝난다(shift = 32~39, 끝 표시 위 끝 = 80~87비트 — 88비트 안) → 그 48비트를 꺼낸다
                int shift = pad + 32;
                ulong marker = ((lo >> shift) | (hi << (64 - shift))) & 0xFFFF_FFFF_FFFFUL;
                if (marker == 0x1772_4538_5090UL) return true;
            }
        }
        return false;
    }

    // ───────────────────────────── 도우미 ─────────────────────────────

    private static bool ReadVint(byte[] b, ref int p, out ulong value)
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
        if (count <= 0 || pos < 0 || pos >= s.Length) return Array.Empty<byte>();
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
