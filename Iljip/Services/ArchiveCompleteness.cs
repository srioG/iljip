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

    // ───────────────────────────── EGG·ALZ(W2-08 검증 지적) ─────────────────────────────

    /// <summary>EGG·ALZ 구조를 끝 표시까지 따라간 결과.</summary>
    internal enum EggAlzWalk { Complete, Truncated, Unknown }

    private const uint EggMagic = 0x41474745;      // 'EGGA'
    private const uint EggEnd = 0x08E28222;        // 머리 끝·파일 머리 끝·보관 끝(같은 값)
    private const uint EggFile = 0x0A8590E3;
    private const uint EggFilename = 0x0A8591AC;
    private const uint EggComment = 0x04C63672;
    private const uint EggWinInfo = 0x2C86950B;
    private const uint EggPosixInfo = 0x1EE922E5;
    private const uint EggEncrypt = 0x08D1470F;
    private const uint EggDummy = 0x07463307;
    private const uint EggBlock = 0x02B50C13;
    private const uint EggSplit = 0x24F5A262;
    private const uint EggSolid = 0x24E5A060;

    /// <summary>
    /// EGG·ALZ 가 끝 표시까지 온전한가(W2-08 검증 지적). EggDotNet 은 항목 머리와 머리 사이(또는 머리 안의 서명 자리)에서 잘린 파일을 오류 없이
    /// '읽은 데까지만' 보여 줘서, 압축 검사가 '문제없어요', 창 없이 풀기가 종료 코드 0 으로 끝나며 뒤쪽 파일을 조용히 잃었다.
    /// 잘렸으면 <see cref="CorruptArchiveException.Truncated"/>(분할 EGG 면 마지막 조각의 <see cref="VolumeIncompleteException"/>),
    /// 온전하거나 판단할 수 없으면(모르는 머리·분할 ALZ·조각을 못 찾음) null. 예외는 내지 않는다.
    /// <list type="bullet">
    /// <item>EGG: EggDotNet 이 읽는 그대로 머리(분할·솔리드 머리)·파일 머리(이름·윈도/POSIX 정보·암호·주석·더미)·블록(머리 18바이트 + 압축 크기)을 따라가
    ///   보관 끝(0x08E28222)을 만나야 온전하다. 분할이면 조각들의 본문(머리 뒤)을 이어 붙여 본다.</item>
    /// <item>ALZ: <see cref="AlzNameScanner.TryScan"/> 이 끝 표시('CLZ')까지 읽었는가. 끝 표시 앞에서 끝나면 항목 경계에서 끊겼어도 잘림이다
    ///   (끝 표시만 빠진 것과 뒤 항목이 통째로 빠진 것을 바이트로는 가를 수 없어, 조용히 잃지 않는 쪽을 고름).</item>
    /// </list>
    /// </summary>
    /// <param name="offset">압축이 시작하는 위치(알집 자동 풀림 exe 안이면 그 위치, 보통 0).</param>
    public static ArchiveException? CheckEggAlz(string archivePath, long offset = 0)
    {
        try
        {
            byte[] sig;
            using (var fs = new FileStream(archivePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                sig = ReadAt(fs, offset, 4);
            if (sig.Length < 4) return null;   // 서명도 없으면 EggDotNet 이 손상으로 알린다
            uint magic = BinaryPrimitives.ReadUInt32LittleEndian(sig);
            if (magic == 0x015A4C41)   // 'ALZ\1'
            {
                var scan = AlzNameScanner.TryScan(archivePath, offset);
                return scan is { Complete: false } ? CorruptArchiveException.Truncated() : null;
            }
            if (magic != EggMagic) return null;
            return CheckEgg(archivePath, offset);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;   // 읽기 문제는 여기서 판단하지 않는다(엔진이 같은 파일을 다시 연다)
        }
    }

    private static ArchiveException? CheckEgg(string archivePath, long offset)
    {
        var segments = new List<(FileStream Stream, long Start, long Length, string Name)>();
        try
        {
            var first = new FileStream(archivePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            var head = ParseEggHeader(first, offset);
            if (head is null)
            {
                first.Dispose();
                return CorruptArchiveException.Truncated();   // 머리 끝 표시도 없이 끝남
            }
            segments.Add((first, head.Value.End, first.Length - head.Value.End, Path.GetFileName(archivePath)));
            if (head.Value.Split)
            {
                // 분할 EGG: EggDotNet 과 같이 첫 조각(앞 ID = 0)부터 '다음 ID' 를 따라 같은 폴더의 .egg 를 잇는다(자동 풀림 exe 는 분할을 찾지 않음)
                if (offset != 0 || head.Value.Prev != 0) return null;
                var others = new List<(string Path, long End, int Id, int Next)>();
                foreach (var sibling in EggAlzArchiveService.SplitVolumeCandidates(Path.GetFullPath(archivePath)))
                {
                    try
                    {
                        using var fs = new FileStream(sibling, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                        if (ParseEggHeader(fs, 0) is { Split: true } h) others.Add((sibling, h.End, h.HeaderId, h.Next));
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                    {
                        // 잠긴 형제 파일은 조각일 리 없다(EggDotNet 처럼 건너뜀)
                    }
                }
                int next = head.Value.Next;
                for (int guard = 0; next != 0 && guard < 10_000; guard++)
                {
                    int want = next;
                    var v = others.FirstOrDefault(o => o.Id == want);
                    if (v.Path is null) return null;   // 조각이 없음 — EggDotNet 이 '조각 없음'으로 알린다
                    var fs = new FileStream(v.Path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                    segments.Add((fs, v.End, Math.Max(0, fs.Length - v.End), Path.GetFileName(v.Path)));
                    others.Remove(v);
                    next = v.Next;
                }
                if (next != 0) return null;
            }

            var body = new SegmentReader(segments.Select(s => ((Stream)s.Stream, s.Start, s.Length)).ToList());
            return WalkEggBody(body) switch
            {
                EggAlzWalk.Truncated => segments.Count > 1 ? new VolumeIncompleteException(segments[^1].Name) : CorruptArchiveException.Truncated(),
                _ => null,
            };
        }
        finally
        {
            foreach (var s in segments) s.Stream.Dispose();
        }
    }

    /// <summary>
    /// EGG 머리('EGGA' 자리부터): 머리 끝 위치·머리 ID·분할이면 앞/다음 ID. EggDotNet 처럼 분할(11바이트)·솔리드(3바이트) 머리를 건너뛰고
    /// 그 밖의 값은 4바이트씩 넘기며 머리 끝(0x08E28222)을 찾는다. 서명이 아니거나 머리 끝 없이 끝나면 null.
    /// </summary>
    private static (long End, int HeaderId, bool Split, int Prev, int Next)? ParseEggHeader(Stream s, long start)
    {
        var h = ReadAt(s, start, 14);
        if (h.Length < 14 || BinaryPrimitives.ReadUInt32LittleEndian(h) != EggMagic) return null;
        int headerId = BinaryPrimitives.ReadInt32LittleEndian(h.AsSpan(6, 4));
        long pos = start + 14, len = s.Length;
        bool split = false;
        int prev = 0, next = 0;
        while (pos + 4 <= len)
        {
            uint v = BinaryPrimitives.ReadUInt32LittleEndian(ReadAt(s, pos, 4));
            pos += 4;
            if (v == EggEnd) return (pos, headerId, split, prev, next);
            if (v == EggSplit)
            {
                var sp = ReadAt(s, pos, 11);
                if (sp.Length < 11) return null;
                prev = BinaryPrimitives.ReadInt32LittleEndian(sp.AsSpan(3, 4));
                next = BinaryPrimitives.ReadInt32LittleEndian(sp.AsSpan(7, 4));
                split = true;
                pos += 11;
            }
            else if (v == EggSolid)
            {
                pos += 3;
            }
        }
        return null;
    }

    /// <summary>
    /// EGG 본문(머리 뒤)을 EggDotNet 이 읽는 그대로 따라가 보관 끝(맨 바깥의 0x08E28222)을 만나면 온전. 그 전에 바이트가 모자라면(머리·블록이
    /// 중간에 끊기거나 항목 경계에서 끝남) 잘림. 모르는 서명·말이 안 되는 블록 머리면 판단하지 않는다(EggDotNet 과 다르게 읽을 수 있어서).
    /// </summary>
    private static EggAlzWalk WalkEggBody(SegmentReader r)
    {
        long len = r.Length, pos = 0;
        bool inFile = false;   // 파일 머리의 추가 필드를 읽는 중(파일 머리 끝 0x08E28222 까지)
        for (long guard = 0; guard < 100_000_000; guard++)
        {
            if (pos + 4 > len) return EggAlzWalk.Truncated;
            uint magic = BinaryPrimitives.ReadUInt32LittleEndian(r.Read(pos, 4));
            pos += 4;
            long skip;
            if (inFile)
            {
                switch (magic)
                {
                    case EggEnd:
                        inFile = false;
                        continue;
                    case EggFilename:
                    {
                        var f = r.Read(pos, 3);
                        if (f.Length < 3) return EggAlzWalk.Truncated;
                        int flags = f[0];
                        skip = 3 + ((flags & 0x08) != 0 ? 2 : 0) + ((flags & 0x10) != 0 ? 4 : 0) + BinaryPrimitives.ReadUInt16LittleEndian(f.AsSpan(1, 2));
                        break;
                    }
                    case EggWinInfo:
                        skip = 12;   // 플래그·크기 3 + FILETIME 8 + 속성 1(EggDotNet 이 늘 12바이트로 읽음)
                        break;
                    case EggPosixInfo:
                        skip = 23;
                        break;
                    case EggComment:
                    case EggEncrypt:
                    case EggDummy:
                        skip = ExtraFieldLength(r, pos);
                        if (skip < 0) return EggAlzWalk.Truncated;
                        break;
                    default:
                        return EggAlzWalk.Unknown;
                }
            }
            else
            {
                switch (magic)
                {
                    case EggEnd:
                        return EggAlzWalk.Complete;   // 보관 끝
                    case EggFile:
                        skip = 12;                     // 파일 ID 4 + 원래 크기 8
                        inFile = true;
                        break;
                    case EggBlock:
                    {
                        var b = r.Read(pos, 18);       // 방식 1·힌트 1·원래 크기 4·압축 크기 4·CRC 4·블록 머리 끝 4
                        if (b.Length < 18) return EggAlzWalk.Truncated;
                        if (BinaryPrimitives.ReadUInt32LittleEndian(b.AsSpan(14, 4)) != EggEnd) return EggAlzWalk.Unknown;
                        skip = 18 + BinaryPrimitives.ReadUInt32LittleEndian(b.AsSpan(6, 4));
                        break;
                    }
                    case EggComment:
                    case EggDummy:
                        skip = ExtraFieldLength(r, pos);
                        if (skip < 0) return EggAlzWalk.Truncated;
                        break;
                    default:
                        return EggAlzWalk.Unknown;
                }
            }
            if (pos + skip > len) return EggAlzWalk.Truncated;
            pos += skip;
        }
        return EggAlzWalk.Unknown;
    }

    /// <summary>EGG 추가 필드(플래그 1 + 크기 2 + 내용)의 길이 — 플래그·크기도 못 읽으면 -1.</summary>
    private static long ExtraFieldLength(SegmentReader r, long pos)
    {
        var f = r.Read(pos, 3);
        return f.Length < 3 ? -1 : 3 + BinaryPrimitives.ReadUInt16LittleEndian(f.AsSpan(1, 2));
    }

    /// <summary>여러 스트림의 구간을 이어 붙인 것처럼 읽는다(분할 EGG 본문 — 조각마다 머리 뒤부터).</summary>
    private sealed class SegmentReader
    {
        private readonly List<(Stream Stream, long Start, long Length)> _parts;

        public SegmentReader(List<(Stream Stream, long Start, long Length)> parts)
        {
            _parts = parts;
            Length = parts.Sum(p => Math.Max(0, p.Length));
        }

        public long Length { get; }

        /// <summary><paramref name="pos"/>(이은 위치)부터 최대 <paramref name="count"/> 바이트(끝이면 짧게).</summary>
        public byte[] Read(long pos, int count)
        {
            var result = new byte[Math.Max(0, (int)Math.Min(count, Length - pos))];
            int got = 0;
            long cursor = 0;
            foreach (var (stream, start, length) in _parts)
            {
                if (got == result.Length) break;
                long at = pos + got;
                if (at >= cursor + length)
                {
                    cursor += length;
                    continue;
                }
                int want = (int)Math.Min(result.Length - got, cursor + length - at);
                var piece = ReadAt(stream, start + (at - cursor), want);
                piece.CopyTo(result, got);
                got += piece.Length;
                if (piece.Length < want) break;
                cursor += length;
            }
            return got == result.Length ? result : result[..got];
        }
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
