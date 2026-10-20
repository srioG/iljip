using System.IO;
using System.Text;
using ICSharpCode.SharpZipLib.Zip;
using Iljip.Models;
using SharpCompress.Archives;

namespace Iljip.Services;

/// <summary>
/// ZIP 포맷.
/// - 해제/목록: 베이스(SharpCompress)에서 처리. 한글 파일명 자동 디코딩 포함.
///   해제 전에 <see cref="ZipPasswordProbe"/> 로 암호를 판정한다(아무것도 만들기 전): 암호 없음·틀림은 바로 예외,
///   반디집의 CP949 한글 암호(B3)는 ZipCrypto 면 엔진에 암호 바이트를 그대로 넘기고, WinZip AES 항목은 <see cref="WinZipAes"/> 가 직접 푼다
///   (인증값 HMAC 까지 확인 — AE-2 는 CRC 칸이 0 이라 이것이 유일한 무결성 확인). 풀린 내용의 CRC-32 도 맞춰 본다(B4).
///   두 후보가 다 통과해 못 가리면 다른 후보로 다시 풀 계획을 함께 넘기고, 판정이 암호를 충분히 확인했으면(<see cref="ZipPasswordProbeResult.Confirmed"/>)
///   풀다가 나는 데이터 오류를 틀린 암호가 아니라 손상으로 알린다.
/// - 압축: SharpZipLib로 처리. AES-256(기본) 또는 ZipCrypto(호환, I2-11). ZipCrypto 암호는 CP949 로 바꿀 수 있으면 CP949 바이트로 쓴다
///   (한국어 Windows 반디집과 양방향 호환 실측). AES 암호는 SharpZipLib 가 늘 UTF-8 로 쓴다(바꿀 수 없음 — 한글이면 화면이 경고).
/// - 나눠 만들기(W2-03, <see cref="CompressionOptions.SplitBytes"/>): 보통 ZIP 을 임시 폴더에 다 만든 뒤 PKZIP 나눔(이름.z01 … 이름.zip)으로 바꾼다
///   (<see cref="SplitArchiveOutput"/>·<see cref="ZipSpanWriter"/>). 읽을 때 나눔 세트는 <see cref="SpannedZipStream"/> 로 보통 ZIP 처럼 연다(빠진 조각은 그 이름).
/// </summary>
public sealed class ZipArchiveService : SharpCompressArchiveServiceBase
{
    /// <summary>'.zip.001' = 숫자 분할 ZIP 의 첫 조각(W2-01, I2-2 해제 — 조각을 이어 붙여 보통 ZIP 처럼 연다).</summary>
    public override IReadOnlyList<string> SupportedExtensions { get; } = new[] { ".zip", ".zip.001" };

    /// <summary>
    /// ZIP 을 바이트로 읽는 스트림(암호 판정·WinZip AES 해제용): 숫자 분할 첫 조각('.zip.001')이면 조각을 이어 붙인 스트림
    /// (뒤 조각이 빠졌거나 덜 받아졌으면 그 이름으로 알림 — <see cref="SplitVolumeStream"/>),
    /// PKZIP 나눔(.z01 … .zip)의 마지막 조각이면 세트를 보통 ZIP 처럼 보이게 한 스트림(앞 조각이 없으면 그 이름으로 알림 — <see cref="SpannedZipStream"/>, W2-03),
    /// 아니면 파일 그대로.
    /// </summary>
    internal static Stream OpenRawStream(string zipPath)
    {
        if (!SplitVolumeStream.IsNumberedFirstVolume(zipPath))
        {
            if (SpannedZipStream.IsLastVolume(zipPath)) return SpannedZipStream.Open(zipPath);
            if (IsSfxPath(zipPath)) return OpenSfxZip(zipPath);
            return new FileStream(zipPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 1 << 16);
        }
        var s = SplitVolumeStream.Open(zipPath);
        try
        {
            s.ThrowIfZipIncomplete();
            return s;
        }
        catch
        {
            s.Dispose();
            throw;
        }
    }

    /// <summary>
    /// 자동 풀림 exe 안의 ZIP 인가(경로가 .exe — <see cref="SfxArchiveService"/> 가 ZIP 자동 풀림을 이 처리기에 맡길 때만 온다, W2-10).
    /// 그러면 암호 판정(CP949 한글 암호)·WinZip AES 인증값·CRC·이름 판별이 보통 ZIP 과 똑같다.
    /// </summary>
    internal static bool IsSfxPath(string path) => path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// 자동 풀림 exe 안의 ZIP 만 보이는 스트림: 일집이 만든 것(꼬리 'ILJIPSFX')은 꼬리가 적은 ZIP 부분만, 다른 ZIP 자동 풀림(반디집 등)은
    /// ZIP 위치값의 기준점부터 파일 끝까지(<see cref="ArchiveSniffer"/>). ZIP 을 못 찾으면 파일 그대로(엔진이 손상으로 알림).
    /// </summary>
    private static Stream OpenSfxZip(string path)
    {
        var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 1 << 16);
        try
        {
            if (ArchiveSniffer.IljipSfxData(fs) is { } own) return new OffsetStream(fs, own.Start, own.Length);
            fs.Position = 0;
            if (ArchiveSniffer.Detect(fs, out long zipBase, peekInner: false) == ArchiveKind.ZipSfx && zipBase > 0) return new OffsetStream(fs, zipBase);
            fs.Position = 0;
            return fs;
        }
        catch
        {
            fs.Dispose();
            throw;
        }
    }

    /// <summary>
    /// 숫자 분할 첫 조각·PKZIP 나눔의 마지막 조각이면 이어 붙인 스트림으로 연다(한글 이름 판별·암호 판정은 보통 ZIP 과 같음). 아니면 베이스 그대로.
    /// (W2-03 전에는 나눔 ZIP 을 SharpCompress 가 형제 조각을 찾아 열었다 — 목록이 마지막 조각을 넘으면 못 읽어 <see cref="SpannedZipStream"/> 로 바꿈)
    /// 흘려 쓴 암호 항목(서술자 + 로컬 머리의 압축 크기 0 — Java·서버 모양)이 있으면 그 칸만 목록 값으로 덮어 보이는 스트림으로 연다
    /// (<see cref="StreamedZipHeaders"/> — 엔진이 그 모양을 형식 알아보기부터 거절해 맞는 암호로도 '손상'으로 보였음). 목록·풀기·검사가 모두 이 길로 연다.
    /// </summary>
    protected override IArchive OpenForRead(string archivePath, SharpCompress.Readers.ReaderOptions options)
    {
        bool plain = !SplitVolumeStream.IsNumberedFirstVolume(archivePath) && !SpannedZipStream.IsLastVolume(archivePath) && !IsSfxPath(archivePath);
        var s = OpenRawStream(archivePath);
        try
        {
            var patched = StreamedZipHeaders.TryWrap(s);
            if (patched is null && plain)
            {
                s.Dispose();
                return base.OpenForRead(archivePath, options);
            }
            return SharpCompress.Archives.Zip.ZipArchive.OpenArchive(patched ?? s, options with { LeaveStreamOpen = false });
        }
        catch
        {
            s.Dispose();
            throw;
        }
    }

    /// <summary>목록 구조 검사에서 읽을 목록 크기 한도(그보다 크면 보지 않음 — 엔진에 맡김).</summary>
    private const long MaxStructureDirectoryBytes = 256L * 1024 * 1024;

    /// <summary>
    /// 목록(중앙 디렉터리) 구조 검사(W2-08 검증 지적 2차). SharpCompress 는 목록을 읽다 서명이 틀린 기록을 만나면 오류 없이 거기서 멈추고(뒤 파일이 말없이 빠져
    /// 시험 '문제없어요. 파일 0개'·풀기 종료 코드 0), 기록이 가리키는 자리의 로컬 머리가 다른 항목이어도 그대로 푼다(다른 파일 내용이 그 이름으로 풀림).
    /// 목록을 끝 머리 앞까지 직접 읽어:
    /// <list type="bullet">
    /// <item>끝 머리의 목록 크기 안에서 읽을 수 없는 기록을 만났고 읽은 기록이 끝 머리의 항목 수보다 적거나, 엔진이 그보다 적게 읽었으면 → 압축 전체 문제(목록에서 파일이 빠짐).
    ///   항목 수 칸만 틀린 것(기록은 목록 크기만큼 다 읽히고 목록이 끝 머리 바로 앞에서 끝남)은 문제로 보지 않는다.</item>
    /// <item>기록마다 가리키는 자리에 로컬 머리 서명과 같은 이름이 없으면 → 그 항목(엔진 순번 — 엔진은 목록 차례대로 읽는다) 문제. 목록 암호(bit 13 — 로컬 이름을 가림)는 맞지 않아도 넘어간다.
    ///   엔진이 읽은 항목이 직접 읽은 기록보다 많으면(엔진은 깨진 길이 칸을 넘어 읽는다 — 이름에 다음 바이트가 붙음) 그 뒤 항목도 문제(IJ3-02 (e)).</item>
    /// <item>IJ3-02((e)·(f)): 목록 위치 칸이 끝 머리 뒤·파일 밖이거나, 목록이 통째로 없거나(기록 0개인데 끝 머리가 파일이 있다고 함·맨 앞에 로컬 머리),
    ///   기록 길이 칸이 커져 뒤 기록을 삼켰거나(어느 기록도 가리키지 않는 로컬 머리가 맨 앞·어떤 항목 데이터 바로 뒤에 있음),
    ///   목록 크기·위치가 끝 머리와 어긋나고 기록이 항목 수보다 적으면 → 압축 전체 문제(예전: '문제없어요. 파일 0개'·0개 풀고 종료 코드 0).</item>
    /// <item>IJ3-02(4차 검증): 기록을 하나도 못 읽었으면(위치 칸이 0 이라 목록 자리에 로컬 머리가 있는 등) 맨 앞 로컬 머리를 끝 머리 앞까지에서 본다
    ///   (예전: 흘려 쓴 ZIP 의 끝 머리 항목 수·크기·위치 칸이 0 이면 '문제없어요. 파일 0개'·0개 풀고 종료 코드 0).
    ///   목록 기록의 유니코드 이름(0x7075 — 엔진이 이 이름으로 푼다)·AES 정보(0x9901 — 암호 풀기에 씀)가 로컬 머리와 다르면 그 항목 문제.</item>
    /// <item>I4-1 검토: 방식·암호 표시가 로컬 머리와 다르거나, 엔진이 데이터를 읽을 때 쓰는 CRC·크기(서술자 표시가 없으면 로컬 머리, 있으면 목록 + 데이터 서술자)가
    ///   서로 맞지 않으면 그 항목 문제 — 예전엔 그 값이 깨져 0 이 되면 0바이트 파일이 '문제없어요'로 풀렸다(<see cref="EngineValuesAgree"/>).
    ///   목록 암호 표시(bit 13)가 켜진 기록도 로컬 머리가 맞으면 같게 견준다(맞지 않는 것만 가려진 이름으로 보고 넘어감).</item>
    /// </list>
    /// 나눠진 ZIP 은 <see cref="OpenRawStream"/>(보통 ZIP 처럼 보이게 한 스트림)으로 본다.
    /// <paramref name="engineEntryCount"/> 가 음수면 엔진 항목 수를 모른다(암호 오류의 원인을 볼 때 — 목록 기록 차례로만 본다).
    /// </summary>
    protected override StructureProblems? CheckStructure(string archivePath, int engineEntryCount)
    {
        using var s = OpenRawStream(archivePath);
        return CheckZipStructure(s, engineEntryCount);
    }

    /// <summary>
    /// <see cref="CheckStructure"/> 의 본체(스트림을 받음 — 7z.dll 로 여는 ZIPX 도 같은 검사를 쓴다, I4-1 검증 지적: ZIPX 는 목록 이름 손상을 보지 않아
    /// '문제없어요'·풀기 종료 코드 0 에 파일이 빠지거나 틀린 이름으로 풀렸다). 항목 순번은 목록 기록 차례.
    /// </summary>
    internal static StructureProblems? CheckZipStructure(Stream s, int engineEntryCount) => CheckZipStructure(s, engineEntryCount, out _);

    /// <param name="records">직접 읽은 목록 기록(차례대로 — 목록을 못 읽었으면 비어 있음).</param>
    internal static StructureProblems? CheckZipStructure(Stream s, int engineEntryCount, out IReadOnlyList<ZipDirectory.CentralRecord> records)
    {
        records = Array.Empty<ZipDirectory.CentralRecord>();
        long len = s.Length;
        // 엔진(SharpCompress)처럼 끝 머리의 주석 길이 칸·디스크 번호 칸은 보지 않는다 — 나눠진 ZIP 도 여기서는 보통 ZIP 모양(디스크 0)이라, 그 칸이 깨진 것이다.
        // 예전엔 그 칸이 깨지면 검사를 건너뛰어(문제 없음), 목록 기록 서명까지 깨진 ZIP 을 '문제없어요. 파일 0개'·0개 풀고 종료 코드 0(IJ3-02 5차)
        var end = ZipDirectory.FindEnd(s, lenient: true);
        // 끝 머리 서명이 아예 없는데 엔진이 0개를 읽었으면 빈 ZIP 이 아니라 손상이다 — 빈 ZIP 도 끝 머리(22바이트)는 있다. 0 으로만 채워진 파일
        // (받다 만 파일·복사 실패)을 엔진은 빈 tar(0 블록 두 개)로 보고 오류 없이 0개를 내놓아, 예전엔 검사 '문제없어요. 파일 0개'·풀기 종료 코드 0
        // (I4-1 2차 검증 지적 — 1,000바이트 이상의 0 파일)
        if (end is null)
            return engineEntryCount == 0 ? new StructureProblems(new CorruptArchiveException(), new HashSet<int>()) : null;
        if (end.HasZip64Locator)
        {
            if (end.Zip64Disk != 0) return null;
            ZipDirectory.ApplyZip64(s, end, end.Zip64Offset);
        }
        bool engineKnown = engineEntryCount >= 0;
        int engineCount = Math.Max(0, engineEntryCount);
        long cdStart = end.CdOffset, limit = end.DirectoryLimit;
        var allBad = new HashSet<int>(Enumerable.Range(0, engineCount));
        // 목록 위치가 끝 머리 뒤·파일 밖 — 목록을 읽을 수 없다. 파일이 있다고 적혀 있거나 맨 앞에 로컬 머리가 있으면 파일이 빠진 것(엔진이 읽은 것도 믿지 않음)
        if (cdStart < 0 || cdStart > limit)
            return end.TotalEntries > 0 || end.CdSize > 0 || engineCount > 0 || IsPlausibleLocalHeader(s, 0, limit)
                ? new StructureProblems(ZipListDamagedException.Missing(), allBad)
                : null;
        if (limit - cdStart > MaxStructureDirectoryBytes) return null;
        byte[] cd = ZipDirectory.ReadAt(s, cdStart, (int)(limit - cdStart));
        var parsed = ZipDirectory.ParseCentralPrefix(cd, out int consumed);
        records = parsed;

        bool missing = false;
        bool stoppedInside = consumed < Math.Min(cd.Length, end.CdSize);
        // 목록 크기·위치가 끝 머리와 어긋남(위치 칸이 깨져 목록 한가운데부터 읽음 등) — 항목 수 칸만 틀린 것(목록은 끝 머리 바로 앞까지 온전)과 가른다
        bool shapeOff = consumed != end.CdSize || cdStart + end.CdSize != limit;
        if (parsed.Count < end.TotalEntries && (stoppedInside || shapeOff) || engineKnown && engineCount < parsed.Count)
            missing = true;
        if (parsed.Count == 0 && (end.TotalEntries > 0 || engineCount > 0)) missing = true;

        var bad = new HashSet<int>();
        // 직접 읽지 못한 기록 뒤의 엔진 항목은 확인할 수 없다(깨진 길이 칸 너머를 읽은 것 — 이름·내용이 틀릴 수 있음)
        for (int i = parsed.Count; i < engineCount; i++) bad.Add(i);

        var offsets = new HashSet<long>(parsed.Select(r => r.LocalOffset));
        var matched = new List<(ZipDirectory.CentralRecord Record, LocalHead Head)>();
        int n = engineKnown ? Math.Min(parsed.Count, engineCount) : parsed.Count;
        foreach (int i in Enumerable.Range(0, n).OrderBy(i => parsed[i].LocalOffset))
        {
            var r = parsed[i];
            if (ReadMatchingLocalHead(s, len, r) is { } head) matched.Add((r, head));
            // 목록 암호(bit 13)는 로컬 이름·크기가 가려져 있어 맞지 않아도 문제로 보지 않는다 — 단 로컬 머리의 이름이 그대로 보이면 가려진 것이 아니라
            // 표시 칸이 깨진 것이므로 문제로 본다(I4-1 검토: 목록 기록의 표시·방식 칸이 깨져 bit 13 이 켜진 AES 항목이 틀린 내용으로 '문제없어요' 풀렸다)
            else if ((ZipDirectory.U16(r.Raw, 8) & 0x2000) == 0 || LocalNameVisible(s, len, r)) bad.Add(i);
        }

        // 어느 기록도 가리키지 않는 로컬 머리(맨 앞, 또는 맞는 항목의 데이터·서술자 바로 뒤) = 목록에서 빠진 파일.
        // 기록을 하나도 못 읽었으면 목록 위치 칸은 믿을 수 없다 — 끝 머리 앞 어디까지든 맨 앞 머리를 본다(IJ3-02 (f): 위치 칸이 0 이면 한도가 0 이 되어 늘 못 봄)
        long headLimit = parsed.Count > 0 ? Math.Min(cdStart, limit) : limit;
        if (!missing && !offsets.Contains(0) && IsPlausibleLocalHeader(s, 0, headLimit)) missing = true;
        if (!missing)
        {
            var sorted = offsets.Where(o => o >= 0).OrderBy(o => o).ToList();
            foreach (var (r, head) in matched)
            {
                int k = sorted.BinarySearch(r.LocalOffset);
                long next = k >= 0 && k + 1 < sorted.Count ? Math.Min(sorted[k + 1], cdStart) : cdStart;
                if (HasUnlistedHeaderAfter(s, r, head, next, offsets)) { missing = true; break; }
            }
        }
        CorruptArchiveException? whole = missing ? ZipListDamagedException.Missing() : null;
        return whole is null && bad.Count == 0 ? null : new StructureProblems(whole, bad);
    }

    /// <summary>로컬 머리에서 읽은 것(이름 뒤 데이터 위치를 셈하는 데 씀).</summary>
    private readonly record struct LocalHead(int Flags, long CompressedSize, long DataStart);

    /// <summary>
    /// 기록이 가리키는 자리에 로컬 머리 서명이 있고 이름이 같으면 그 머리('\'·'/' 는 같게 봄). 아니면 null.
    /// 이름 바이트가 같아도 목록 기록의 유니코드 이름(0x7075 — 엔진은 판·CRC 를 보지 않고 이 이름으로 푼다)이 로컬 머리의 것과 다르거나(로컬 머리에 없으면 적힌 CRC 가
    /// 이름 바이트와 다르거나), 목록·로컬 머리의 AES 정보(0x9901 — 세기·안쪽 방식)가 둘 다 말이 되는데 서로 다르면 null(IJ3-02 — 풀린 이름이 틀리거나 맞는 암호로도 안 풀림).
    /// </summary>
    private static LocalHead? ReadMatchingLocalHead(Stream s, long len, ZipDirectory.CentralRecord r)
    {
        long off = r.LocalOffset;
        if (off < 0 || off + ZipDirectory.LocalHeaderLength > len) return null;
        byte[] f = ZipDirectory.ReadAt(s, off, ZipDirectory.LocalHeaderLength);
        if (ZipDirectory.U32(f, 0) != ZipDirectory.LocalHeaderSig) return null;
        int nameLen = ZipDirectory.U16(f, 26), extraLen = ZipDirectory.U16(f, 28);
        if (nameLen != r.NameLength || off + ZipDirectory.LocalHeaderLength + nameLen > len) return null;
        byte[] local = ZipDirectory.ReadAt(s, off + ZipDirectory.LocalHeaderLength, nameLen);
        for (int k = 0; k < nameLen; k++)
        {
            byte a = local[k], b = r.Raw[ZipDirectory.CentralFixedLength + k];
            if (a == 0x5C) a = 0x2F;
            if (b == 0x5C) b = 0x2F;
            if (a != b) return null;
        }
        // 방식·암호 표시도 같아야 한다 — 암호 판정은 목록 값으로 암호를 맞춰 보므로 그 칸이 깨지면 맞는 암호도 틀려 보이고(IJ3-02 I2-8),
        // WinZip AES 는 목록 값으로 짝지어 직접 풀므로 목록의 방식 칸이 깨지면 엔진이 대신 푸는데 엔진은 인증값을 보지 않고 AE-2 는 CRC 도 없어
        // 틀린 내용이 '문제없어요'로 풀렸다(I4-1 검토 — 예전엔 풀기·검사에서는 이 칸을 보지 않았다).
        int cdFlags = ZipDirectory.U16(r.Raw, 8), localFlags = ZipDirectory.U16(f, 6);
        if (ZipDirectory.U16(r.Raw, 10) != ZipDirectory.U16(f, 8) || ((cdFlags ^ localFlags) & 0x41) != 0) return null;   // 암호·강한 암호 표시
        long extraAt = off + ZipDirectory.LocalHeaderLength + nameLen;
        byte[] localExtra = extraLen > 0 && extraAt + extraLen <= len ? ZipDirectory.ReadAt(s, extraAt, extraLen) : Array.Empty<byte>();
        if (!ExtraAgrees(r, local, localExtra, localFlags)) return null;
        if (!EngineValuesAgree(s, len, r, f, localExtra, extraAt + extraLen)) return null;
        return new LocalHead(ZipDirectory.U16(f, 6), ZipDirectory.U32(f, 18), extraAt + extraLen);
    }

    /// <summary>기록이 가리키는 자리에 로컬 머리 서명이 있고 이름 바이트가 목록 기록과 같은가(목록 암호로 가려지지 않았는가).</summary>
    private static bool LocalNameVisible(Stream s, long len, ZipDirectory.CentralRecord r)
    {
        long off = r.LocalOffset;
        if (off < 0 || off + ZipDirectory.LocalHeaderLength + r.NameLength > len) return false;
        byte[] f = ZipDirectory.ReadAt(s, off, ZipDirectory.LocalHeaderLength);
        if (ZipDirectory.U32(f, 0) != ZipDirectory.LocalHeaderSig || ZipDirectory.U16(f, 26) != r.NameLength) return false;
        return ZipDirectory.ReadAt(s, off + ZipDirectory.LocalHeaderLength, r.NameLength).AsSpan()
            .SequenceEqual(r.Raw.AsSpan(ZipDirectory.CentralFixedLength, r.NameLength));
    }

    /// <summary>
    /// 엔진이 이 항목을 읽을 때 쓰는 CRC·크기가 맞는가(I4-1 검토 — 망가뜨림 시험이 찾은 거짓 성공). SharpCompress 는 로컬 머리에 서술자 표시(bit 3)가 없으면
    /// 로컬 머리의 CRC·크기로, 있으면 목록의 값으로 데이터를 읽고 같은 값으로 확인한다 — 그 값이 깨져 0 이 되면 빈 파일로 풀리고 깨진 CRC(0)와도 맞아
    /// 검사는 '문제없어요', 풀기는 0바이트 파일을 오류 없이 만들었다(로컬 머리의 표시 칸이 0 이 된 흘려 쓴 ZIP, 목록의 CRC·크기 칸이 0 이 된 ZIP).
    /// 서술자 표시가 없으면 로컬 머리의 CRC·크기(ZIP64 면 그 추가 필드 값)가 목록과 같아야 하고, 있으면 목록의 압축 크기만큼 뒤에 목록 값과 같은
    /// 데이터 서술자(서명 있음·없음 × 4·8바이트 크기)가 있어야 한다(빈 항목은 서술자 대신 바로 다음 기록·파일 끝이어도 됨).
    /// </summary>
    private static bool EngineValuesAgree(Stream s, long len, ZipDirectory.CentralRecord r, byte[] f, byte[] localExtra, long dataStart)
    {
        uint crc = ZipDirectory.U32(r.Raw, 16);
        long csize = r.CompressedSize, usize = r.UncompressedSize;
        if ((ZipDirectory.U16(f, 6) & 0x08) == 0)
        {
            if (ZipDirectory.U32(f, 14) != crc) return false;
            long lc = ZipDirectory.U32(f, 18), lu = ZipDirectory.U32(f, 22);
            bool cFull = lc == uint.MaxValue, uFull = lu == uint.MaxValue;
            if (cFull || uFull)
            {
                // ZIP64: 로컬 머리는 원래 크기, 압축 크기 차례(가득 찬 칸만) — 값이 없으면 그 칸은 견주지 않는다
                foreach (var (id, dat, size) in Editing.ZipRawRewriter.ExtraBlocks(localExtra, 0, localExtra.Length))
                {
                    if (id != 0x0001) continue;
                    int q = dat, qEnd = dat + size;
                    if (uFull && q + 8 <= qEnd) { lu = (long)Math.Min(ZipDirectory.U64(localExtra, q), long.MaxValue); uFull = false; q += 8; }
                    if (cFull && q + 8 <= qEnd) { lc = (long)Math.Min(ZipDirectory.U64(localExtra, q), long.MaxValue); cFull = false; }
                    break;
                }
            }
            return (cFull || lc == csize) && (uFull || lu == usize);
        }

        if (csize < 0 || usize < 0) return false;
        long at = dataStart + csize;
        if (at >= 0 && at + 12 <= len)
        {
            byte[] d = ZipDirectory.ReadAt(s, at, (int)Math.Min(24, len - at));
            bool Narrow(int o) => o + 12 <= d.Length && ZipDirectory.U32(d, o) == crc && ZipDirectory.U32(d, o + 4) == csize && ZipDirectory.U32(d, o + 8) == usize;
            bool Wide(int o) => o + 20 <= d.Length && ZipDirectory.U32(d, o) == crc && (long)ZipDirectory.U64(d, o + 4) == csize && (long)ZipDirectory.U64(d, o + 12) == usize;
            bool signed = ZipDirectory.U32(d, 0) == Editing.ZipRawRewriter.DescriptorSig;
            if (signed && (Narrow(4) || Wide(4)) || Narrow(0) || Wide(0)) return true;
        }
        // 서술자가 없는 빈 항목(폴더 등): 목록 값이 모두 0 이고 데이터 자리가 곧 다음 기록·파일 끝일 때만
        if (crc != 0 || csize != 0 || usize != 0) return false;
        if (dataStart == len) return true;
        if (dataStart < 0 || dataStart + 4 > len) return false;
        return ZipDirectory.U32(ZipDirectory.ReadAt(s, dataStart, 4), 0) is ZipDirectory.LocalHeaderSig or ZipDirectory.CentralSig or ZipDirectory.EndSig or ZipDirectory.Zip64EndSig;
    }

    /// <summary>
    /// 목록 기록의 추가 필드 중 풀기에 쓰이는 것(유니코드 이름 0x7075·AES 0x9901)이 로컬 머리와 맞는가(IJ3-02 — 반디집 ZIP 은 목록 바이트의 절반 넘게가 추가 필드라
    /// 목록 손상이 자주 여기 떨어진다). 로컬 머리 쪽만 깨진 것(목록 쪽은 이름 바이트와 맞음·AES 정보가 말이 됨)은 풀기에 쓰이지 않으므로 맞는 것으로 본다.
    /// </summary>
    internal static bool ExtraAgrees(ZipDirectory.CentralRecord r, byte[] nameBytes, byte[] localExtra, int localFlags)
    {
        int extraAt = ZipDirectory.CentralFixedLength + r.NameLength;
        string? central = ZipEntryNames.UnicodePathOf(r.Raw, extraAt, r.ExtraLength);
        if (central is not null)
        {
            string? localName = ZipEntryNames.UnicodePathOf(localExtra, 0, localExtra.Length);
            bool agrees = localName is not null
                ? string.Equals(central, localName, StringComparison.Ordinal) || string.Equals(central, StandardName(nameBytes, localFlags), StringComparison.Ordinal)
                : UnicodePathCrcMatches(r.Raw, extraAt, r.ExtraLength, nameBytes);
            if (!agrees) return false;
        }
        return AesAgrees(AesBlockOf(r.Raw, extraAt, r.ExtraLength), AesBlockOf(localExtra, 0, localExtra.Length), ZipDirectory.U16(r.Raw, 10) == 99);
    }

    /// <summary>
    /// 목록·로컬 머리의 AES 정보(0x9901 내용 7바이트)가 맞는가. 로컬 쪽이 없거나 둘 다 말이 안 되면 맞는 것으로(가를 수 없음), 목록 쪽만 말이 안 되거나
    /// AES 항목(방식 99, <paramref name="centralIsAes"/>)인데 목록 쪽에 없으면 틀림(풀 때 쓰는 값이 깨짐), 로컬 쪽만 말이 안 되면 맞음(쓰지 않는 값), 둘 다 말이 되면 같아야.
    /// </summary>
    internal static bool AesAgrees(byte[]? central, byte[]? local, bool centralIsAes)
    {
        if (local is null) return true;
        bool l = PlausibleAes(local);
        if (central is null) return !(centralIsAes && l);
        bool c = PlausibleAes(central);
        if (!c) return !l;
        return !l || central.AsSpan().SequenceEqual(local);
    }

    /// <summary>이름 바이트를 유니코드 이름 없이 푼 것(UTF-8 표시 또는 올바른 UTF-8 이면 UTF-8, 아니면 CP949) — 목록·로컬의 유니코드 이름 중 어느 쪽이 맞는지 가를 때.</summary>
    private static string StandardName(byte[] nameBytes, int flags)
    {
        if ((flags & 0x0800) != 0 || System.Text.Unicode.Utf8.IsValid(nameBytes)) return Encoding.UTF8.GetString(nameBytes);
        return KoreanFileNameDecoder.DecodeBytes(nameBytes, 0, nameBytes.Length);
    }

    /// <summary>첫 0x7075(크기 5 이상) 블록에 적힌 CRC 가 이름 바이트의 CRC 와 같은가(엔진과 같은 블록을 본다).</summary>
    private static bool UnicodePathCrcMatches(byte[] b, int extraAt, int extraLen, byte[] nameBytes)
    {
        int end = extraAt + extraLen;
        for (int x = extraAt; x + 4 <= end;)
        {
            int id = ZipDirectory.U16(b, x), size = ZipDirectory.U16(b, x + 2);
            if (x + 4 + size > end) break;
            if (id == ZipEntryNames.UnicodePathExtra && size >= 5) return ZipDirectory.U32(b, x + 5) == ArchiveSniffer.Crc32Of(nameBytes);
            x += 4 + size;
        }
        return true;
    }

    /// <summary>첫 AES 블록(0x9901, 크기 7 이상)의 내용 7바이트(판·'AE'·세기·안쪽 방식). 없으면 null.</summary>
    internal static byte[]? AesBlockOf(byte[] b, int extraAt, int extraLen)
    {
        int end = extraAt + extraLen;
        for (int x = extraAt; x + 4 <= end;)
        {
            int id = ZipDirectory.U16(b, x), size = ZipDirectory.U16(b, x + 2);
            if (x + 4 + size > end) break;
            if (id == 0x9901 && size >= 7) return b.AsSpan(x + 4, 7).ToArray();
            x += 4 + size;
        }
        return null;
    }

    /// <summary>AES 블록 내용이 말이 되는가(판 1·2, 'AE', 세기 1~3).</summary>
    internal static bool PlausibleAes(byte[] aes) =>
        ZipDirectory.U16(aes, 0) is 1 or 2 && aes[2] == (byte)'A' && aes[3] == (byte)'E' && aes[4] is >= 1 and <= 3;

    /// <summary>
    /// 항목 데이터(목록·로컬 머리의 압축 크기로 — 둘 중 하나가 깨졌을 수 있어 둘 다) 바로 뒤나 데이터 서술자(12·16·20·24바이트) 뒤, 다음 기록이 가리키는 곳보다 앞에
    /// 어느 기록도 가리키지 않는 로컬 머리가 있는가(목록 기록 길이 칸이 커져 뒤 기록을 삼킨 경우 — IJ3-02 (e)).
    /// </summary>
    private static bool HasUnlistedHeaderAfter(Stream s, ZipDirectory.CentralRecord r, LocalHead head, long next, HashSet<long> offsets)
    {
        bool descriptor = (head.Flags & 0x08) != 0;
        var sizes = new List<long> { r.CompressedSize };
        if (!descriptor && head.CompressedSize != uint.MaxValue && head.CompressedSize != r.CompressedSize) sizes.Add(head.CompressedSize);
        ReadOnlySpan<int> tails = descriptor ? [0, 12, 16, 20, 24] : [0];
        foreach (long size in sizes)
            foreach (int tail in tails)
            {
                long p = head.DataStart + size + tail;
                if (p < next && !offsets.Contains(p) && IsPlausibleLocalHeader(s, p, next)) return true;
            }
        return false;
    }

    /// <summary><paramref name="at"/> 에 그럴듯한 로컬 머리(서명·필요 버전·이름 길이가 말이 되고 이름까지 <paramref name="limit"/> 안)가 있는가.</summary>
    private static bool IsPlausibleLocalHeader(Stream s, long at, long limit)
    {
        if (at < 0 || at + ZipDirectory.LocalHeaderLength > limit) return false;
        byte[] h = ZipDirectory.ReadAt(s, at, ZipDirectory.LocalHeaderLength);
        if (ZipDirectory.U32(h, 0) != ZipDirectory.LocalHeaderSig) return false;
        int version = h[4], nameLen = ZipDirectory.U16(h, 26);
        return version <= 63 && nameLen is > 0 and <= 4096 && at + ZipDirectory.LocalHeaderLength + nameLen <= limit;
    }

    protected override ExtractPlan PrepareExtract(string archivePath, string? password, Func<string, bool> shouldExtract)
    {
        // 나뉜 ZIP(.zip.001·.z01 … .zip)의 조각이 빠졌으면 아무것도 만들기 전에 그 이름으로 알린다 — 대상 폴더도 안 생긴다(W2-03, RAR·7z 와 같게).
        // (판정은 실패를 삼키므로 여기서 먼저 열어 본다. 나눔 ZIP 의 모양은 기억해 두어 판정·해제가 다시 만들지 않음)
        if (SplitVolumeStream.IsNumberedFirstVolume(archivePath) || SpannedZipStream.IsLastVolume(archivePath))
            OpenRawStream(archivePath).Dispose();

        var probe = ZipPasswordProbe.Probe(archivePath, password, shouldExtract);
        if (!probe.AnyEncrypted || probe.Match is PasswordBytes.Unknown or PasswordBytes.NotApplicable)
        {
            // 판정 못 함 → 엔진이 확인(+ CRC·번역·장부). 단 목록을 읽었고 WinZip AES 항목이 있으면 그 항목은 일집이 직접(암호의 UTF-8 바이트 — 엔진과 같음) 푼다:
            // 엔진(SharpCompress)은 AES 의 인증값을 보지 않고 '저장' 방식 AES 항목을 앞의 암호 머리까지 붙여 내놓는데 AE-2 는 CRC 도 없어
            // 틀린 내용이 '문제없어요'로 풀렸다(I4-1 검토 — 목록의 한 기록이 깨져 판정을 못 한 AES ZIP)
            bool aes = !string.IsNullOrEmpty(password) && probe.Entries.Any(e => e.IsAes);
            return new ExtractPlan(BuildReaderOptions(password))
            {
                VerifyCrc = true,
                OpenOverride = aes ? AesOpener(archivePath, probe.Entries, Encoding.UTF8.GetBytes(password!), engineFallback: true) : null,
            };
        }

        if (string.IsNullOrEmpty(password)) throw new PasswordRequiredException();
        // 일부 항목의 확인값과는 맞았으면 틀린 암호와 손상을 가를 근거가 모자란 것 — '손상된 경우에도 이렇게 보일 수 있어요'를 붙인다(I4-1 검토)
        if (probe.Match == PasswordBytes.None || probe.MatchedPasswordBytes is not { } bytes) throw new WrongPasswordException(mayBeCorrupt: probe.SomeEntriesMatched);

        // 두 후보(UTF-8·CP949)가 모두 검사를 통과해 못 가렸으면, 먼저 쓴 후보로 데이터가 틀릴 때 다른 후보로 처음부터 다시 푼다(B3).
        var alternate = probe.AlternatePasswordBytes is { } alt
            ? PlanFor(archivePath, password, probe, probe.AlternateMatch, alt, alternate: null)
            : null;
        return PlanFor(archivePath, password, probe, probe.Match, bytes, alternate);
    }

    private static ExtractPlan PlanFor(
        string archivePath, string password, ZipPasswordProbeResult probe, PasswordBytes kind, byte[] bytes, ExtractPlan? alternate)
    {
        // 엔진에 암호 '문자열'을 그대로 줘도 되는가: UTF-8 로 맞았고, ASCII 이거나 ZipCrypto 항목이 없을 때(AES 는 엔진도 UTF-8 로 키를 만듦).
        // 아니면 맞은 '바이트'를 그대로 넘긴다 — CP949 는 물론, SharpCompress 는 ZipCrypto 암호를 글자 단위로 바꿔서
        // 이모지(서로게이트 쌍) 같은 UTF-8 암호도 문자열로는 안 맞는다(W1-C 실측).
        bool asIs = kind == PasswordBytes.Utf8 && (IsAscii(password) || !probe.HasZipCrypto);
        return new ExtractPlan(asIs ? BuildReaderOptions(password) : BuildReaderOptions(password, bytes))
        {
            VerifyCrc = true,
            PasswordConfirmed = probe.Confirmed,
            Alternate = alternate,
            OpenOverride = probe.HasAes ? AesOpener(archivePath, probe.Entries, bytes, engineFallback: asIs) : null,
        };
    }

    private static bool IsAscii(string s)
    {
        foreach (char c in s)
            if (c > 0x7F) return false;
        return true;
    }

    /// <summary>
    /// WinZip AES 항목을 <see cref="WinZipAes"/> 로 푸는 여닫이. 엔진 항목과 중앙 디렉터리 항목은 순번으로 짝짓고(크기·CRC·암호 여부로 확인),
    /// 어긋나면 이름으로 한 번 더 찾는다. 짝을 못 찾거나 안쪽 방식을 직접 못 풀면: 엔진에 암호 문자열을 그대로 준 경우(<paramref name="engineFallback"/>)는
    /// 엔진(SharpCompress)에 맡기고, 바이트로 넘긴 경우(CP949 등)는 엔진이 AES 키를 못 만들므로 '못 푸는 방식'으로 알린다.
    /// </summary>
    private static Func<int, IArchiveEntry, Stream?> AesOpener(
        string archivePath, IReadOnlyList<ZipCentralEntry> central, byte[] passwordBytes, bool engineFallback) => (index, entry) =>
    {
        if (!IsEncrypted(entry)) return null;
        var cd = FindCentral(central, index, entry);
        if (cd is { IsAes: true } && WinZipAes.CanDecompress(cd))
            return WinZipAes.OpenEntry(archivePath, cd, passwordBytes);
        if (cd is { IsAes: false } || engineFallback) return null;
        throw new UnsupportedMethodException(cd is null ? "한글 암호 AES" : $"한글 암호 AES + 방식 {cd.AesMethod}", entryPath: entry.Key);
    };

    private static ZipCentralEntry? FindCentral(IReadOnlyList<ZipCentralEntry> central, int index, IArchiveEntry entry)
    {
        bool Same(ZipCentralEntry c) => c.UncompressedSize == entry.Size && c.Crc == unchecked((uint)entry.Crc) && c.IsEncrypted;
        if (index < central.Count && Same(central[index])) return central[index];
        var byName = central.Where(c => c.Name == entry.Key && Same(c)).Take(2).ToList();
        return byName.Count == 1 ? byName[0] : null;
    }

    public override Task<CompressResult> CompressAsync(
        IEnumerable<string> sourcePaths,
        string archivePath,
        CompressionOptions? options = null,
        IProgress<ArchiveProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        options ??= CompressionOptions.Default;
        return Task.Run(() =>
        {
            // 파일·빈 폴더·못 들어간 폴더를 한 번에 모은다(I0-2 — 하위 폴더 권한 오류로 전체가 실패하지 않게).
            // 빼기 목록(W2-07, I3-6)에 걸리는 것은 여기서 빠지고 결과의 ExcludedPaths 로 알린다.
            var sources = SourceCollector.Collect(sourcePaths, options.ExcludeFilter, cancellationToken);

            // 나눠 만들기(W2-03): 대상 폴더 안 숨김 임시 폴더에 보통 ZIP 을 다 만든 뒤 PKZIP 나눔(.z01 … .zip)으로 바꾼다(다시 압축하지 않음).
            // 원본을 다 모은 뒤에 임시 폴더를 만든다 — 원본 폴더 안에 저장해도 임시 폴더가 원본으로 담기지 않게. 저장 창에서 고른 이름은 계속 담지 않는다.
            if (options.SplitBytes is > 0 and long split)
                return SplitArchiveOutput.Write(archivePath, split, SplitArchiveOutput.VolumeKind.Zip, progress, cancellationToken,
                    (single, p) => RewriteWithoutUnreadable(single,
                        dropped => WriteZip(sources, dropped, single, options, p, cancellationToken, alsoSkip: archivePath)))
                    .WithExcluded(sources.Excluded);

            // 읽는 도중 막힌 원본이 있으면 그 파일을 빼고 처음부터 다시 쓴다(베이스 RewriteWithoutUnreadable)
            // I5-B(최종 감사 IB-zip-replace-cancel-deletes-old): 같은 이름 zip 이 있으면([바꾸기]) 옆 임시 파일에 다 쓴 뒤에만 바꿔 넣는다 —
            // 취소·실패면 기존 zip 바이트 그대로, 임시 파일도 남지 않는다(7z·나눔·자동 풀림과 같게). 원본 폴더 안의 기존 zip 은 예전처럼 담지 않는다.
            return OutputFiles.WriteReplacing(archivePath, path => RewriteWithoutUnreadable(path,
                    dropped => WriteZip(sources, dropped, path, options, progress, cancellationToken, alsoSkip: archivePath)))
                .WithExcluded(sources.Excluded);
        }, cancellationToken);
    }

    /// <summary>
    /// 자동 풀림 exe 의 데이터가 될 보통 ZIP 을 <paramref name="zipPath"/>(임시 파일)에 쓴다(W2-10 — <see cref="SfxBuilder"/>).
    /// <see cref="CompressAsync"/> 와 같은 규칙(잠긴 원본 건너뛰기·빼기 목록·암호 방식·수준)이고, 나누지 않으며,
    /// 만들 exe(<paramref name="alsoSkip"/> — 덮어쓸 기존 exe 가 원본 폴더 안에 있을 수 있음)는 담지 않는다.
    /// </summary>
    internal static Task<CompressResult> CompressForSfxAsync(
        IEnumerable<string> sourcePaths, string zipPath, CompressionOptions options, string alsoSkip,
        IProgress<ArchiveProgress>? progress, CancellationToken cancellationToken)
    {
        return Task.Run(() =>
        {
            var sources = SourceCollector.Collect(sourcePaths, options.ExcludeFilter, cancellationToken);
            return RewriteWithoutUnreadable(zipPath,
                    dropped => WriteZip(sources, dropped, zipPath, options, progress, cancellationToken, alsoSkip: alsoSkip))
                .WithExcluded(sources.Excluded);
        }, cancellationToken);
    }

    /// <summary>
    /// 이미 모은 원본(파일마다 정해 준 항목 이름)으로 ZIP 을 쓴다(W2-06 — 압축 고치기의 '넣기'가 넣을 파일만 담은 임시 ZIP 을 만들 때).
    /// 압축 고치기는 이 ZIP 의 압축된 데이터만 원래 압축으로 옮겨 담고 이름·암호는 그쪽에서 정하므로, 여기서는 암호 없이 수준만 맞춘다.
    /// 잠긴 원본 건너뛰기·읽는 도중 막힌 파일 빼고 다시 쓰기·하나도 못 담으면 <see cref="NothingToCompressException"/> 은 <see cref="CompressAsync"/> 와 같다.
    /// </summary>
    /// 수준이 '저장만'이면 방식도 저장(0)으로 쓴다(만들기의 '저장만'은 예전처럼 Deflate 수준 0 — 그대로 둠).
    internal static CompressResult WriteCollected(
        SourceSet sources, string archivePath, CompressionOptions options, IProgress<ArchiveProgress>? progress, CancellationToken cancellationToken)
        => RewriteWithoutUnreadable(archivePath, dropped => WriteZip(sources, dropped, archivePath, options, progress, cancellationToken,
            storedMethod: options.Level == CompressionLevelOption.Store));

    /// <summary>
    /// ZIP 을 한 번 쓴다. <paramref name="dropped"/>(앞 회차에서 읽는 도중 막힌 파일)는 열지 않고 건너뜀으로 적는다.
    /// 담은 파일이 하나도 없고 건너뛴 것이 있으면 압축 파일을 만들지 않고 <see cref="NothingToCompressException"/>(I0-2).
    /// </summary>
    /// <param name="alsoSkip">이것도 담지 않는다(나눠 만들 때 저장 창에서 고른 이름 — 실제로 쓰는 곳은 임시 폴더, W2-03).</param>
    /// <param name="storedMethod">true 면 파일을 방식 0(저장)으로 쓴다(W2-06 — 저장만 ZIP 에 넣는 파일).</param>
    private static CompressResult WriteZip(
        SourceSet sources, IReadOnlyDictionary<string, string> dropped, string archivePath,
        CompressionOptions options, IProgress<ArchiveProgress>? progress, CancellationToken cancellationToken, string? alsoSkip = null,
        bool storedMethod = false)
    {
        // 압축 파일을 원본 폴더 안에 저장하는 경우: 지금 쓰고 있는 자기 자신(나눠 만들 때는 저장 창에서 고른 이름도)은 담지 않는다 —
        // 못 열어 '건너뜀'으로 보이지 않게. 진행률의 총 파일 수·바이트에서도 미리 뺀다(W2-03 — 예전엔 총수에 들어가 끝에도 N-1/N 으로 보였다).
        var files = sources.Files
            .Where(f => !IsSameFile(f.SourceFile, archivePath) && (alsoSkip is null || !IsSameFile(f.SourceFile, alsoSkip)))
            .ToList();
        var skipped = new List<SkippedSource>(sources.Skipped);
        long totalBytes = SumFileSizes(files);
        int totalFiles = files.Count;
        long bytesProcessed = 0;
        int filesProcessed = 0;

        // 암호 방식(I2-11): AES-256(기본, 안전) 또는 ZipCrypto(호환 — 탐색기·오래된 프로그램).
        bool zipCrypto = options.HasPassword && options.Encryption == ZipEncryption.ZipCrypto;

        // 압축 파일은 담을 첫 파일을 연 뒤에 만든다 — 모두 못 읽으면 빈 zip 을 남기지 않게(덮어쓰려던 기존 파일도 그대로)
        using var output = new LazyArchiveOutput<ZipOutputStream>(archivePath, fs =>
        {
            var zip = new ZipOutputStream(fs, ZipCryptoCodec(zipCrypto ? options.Password : null))
            {
                IsStreamOwner = false,
                UseZip64 = UseZip64.Dynamic
            };
            zip.SetLevel(ToSharpZipLevel(options.Level));
            if (options.HasPassword)
            {
                zip.Password = options.Password;
            }
            return zip;
        });

        var buffer = new byte[81920];
        foreach (var (sourceFile, entryName) in files)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // 앞 회차에서 읽는 도중 막혔던 파일 — 다시 열지 않고 건너뜀
            if (dropped.TryGetValue(sourceFile, out var droppedReason))
            {
                skipped.Add(new SkippedSource(sourceFile, droppedReason));
                bytesProcessed += SafeLength(sourceFile);
                continue;
            }

            // 원본을 '먼저' 연다(I0-2): 다른 프로그램이 쓰는 중이어도 공유를 허락했으면 읽고, 아예 막았으면 이 파일만 건너뛴다.
            // 항목 머리(PutNextEntry)를 쓰기 전에 열어야(첫 블록까지 읽어 봄) 못 연 파일이 빈 항목으로 남지 않는다.
            if (!SourceFileOpener.TryOpenRead(sourceFile, out var opened, out var reason))
            {
                skipped.Add(new SkippedSource(sourceFile, reason!));
                bytesProcessed += SafeLength(sourceFile);   // 진행률이 건너뛴 만큼 뒤처지지 않게
                continue;
            }

            using var input = opened!;
            var zipStream = output.Writer;
            var entry = new ZipEntry(entryName)
            {
                DateTime = File.GetLastWriteTime(sourceFile),
                // Size는 명시하지 않는다. FileInfo.Length로 미리 박아두면, 폴더 압축 도중
                //   쓰기 중인 파일(로그/DB 등)의 길이가 스트리밍 사이에 바뀔 때 실제 기록 바이트와
                //   불일치해 CloseEntry가 'size was incorrect' 예외를 던지고 압축 전체가 깨진다.
                //   생략하면 SharpZipLib이 CloseEntry에서 실제 기록 길이로 헤더를 채운다.
                // 파일명을 UTF-8 + EFS(General Purpose Bit 11) 플래그로 기록.
                // 한글 파일명이 일집/탐색기/macOS 등 어디서든 올바르게 보이게 함.
                IsUnicodeText = true,
                // AES-256 (WinZip 호환). 0 이면 암호가 있을 때 ZipCrypto. 암호 없으면 무시됨.
                AESKeySize = options.HasPassword && !zipCrypto ? 256 : 0
            };
            if (storedMethod) entry.CompressionMethod = CompressionMethod.Stored;

            zipStream.PutNextEntry(entry);

            progress?.Report(new ArchiveProgress
            {
                CurrentFile = entryName,
                BytesProcessed = bytesProcessed,
                TotalBytes = totalBytes,
                FilesProcessed = filesProcessed,
                TotalFiles = totalFiles
            });

            int read;
            // 읽는 도중 막히면(뒤쪽 바이트 범위 잠금) SourceUnreadableException → 베이스가 이 파일을 빼고 처음부터 다시 쓴다
            while ((read = SourceFileOpener.Read(input, buffer, sourceFile)) > 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
                zipStream.Write(buffer, 0, read);
                bytesProcessed += read;

                progress?.Report(new ArchiveProgress
                {
                    CurrentFile = entryName,
                    BytesProcessed = bytesProcessed,
                    TotalBytes = totalBytes,
                    FilesProcessed = filesProcessed,
                    TotalFiles = totalFiles
                });
            }

            zipStream.CloseEntry();
            filesProcessed++;
        }

        // 담은 파일이 하나도 없는데 건너뛴 것이 있다 = 사용자가 고른 것을 하나도 못 담음 → 빈(또는 빈 폴더뿐인) 압축을 만들지 않는다
        if (filesProcessed == 0 && skipped.Count > 0)
            throw new NothingToCompressException(skipped);

        // 빈 디렉터리 보존: 파일이 하나도 없는 폴더는 위 루프에서 누락되므로
        // 끝에 '/'를 붙인 디렉터리 엔트리로 별도 기록한다(SharpZipLib이 디렉터리로 인식).
        foreach (var (_, dirEntry) in sources.EmptyDirectories)
        {
            cancellationToken.ThrowIfCancellationRequested();
            output.Writer.PutNextEntry(new ZipEntry(dirEntry)
            {
                DateTime = DateTime.Now,
                IsUnicodeText = true
            });
            output.Writer.CloseEntry();
        }

        // 담을 것이 원래 없었으면(예: 원본 폴더 안의 압축 파일 자신뿐) 예전처럼 빈 zip 을 만든다
        output.Writer.Finish();
        output.Complete();

        progress?.Report(new ArchiveProgress
        {
            CurrentFile = string.Empty,
            BytesProcessed = totalBytes,
            TotalBytes = totalBytes,
            FilesProcessed = filesProcessed,
            TotalFiles = totalFiles
        });
        return new CompressResult(skipped);
    }

    /// <summary>
    /// ZipCrypto 암호를 쓸 바이트 인코딩. CP949 로 바꿀 수 있는 암호면 CP949(한국어 Windows 의 반디집·알집이 쓰는 방식 — 반디집 7.46
    /// bz.exe 로 양방향 실측), 이모지처럼 못 바꾸는 글자가 있으면 UTF-8(화면이 '다른 프로그램에서 안 풀릴 수 있어요' 경고 후 고른 경우).
    /// 이름은 항목마다 IsUnicodeText(UTF-8 + EFS 플래그)라 이 코덱의 이름 인코딩은 쓰이지 않는다.
    /// </summary>
    private static StringCodec ZipCryptoCodec(string? zipCryptoPassword)
    {
        bool cp949 = zipCryptoPassword is not null && ZipPasswordProbe.TryCp949Bytes(zipCryptoPassword) is not null;
        return StringCodec.Default.WithZipCryptoEncoding(cp949 ? Encoding.GetEncoding(949) : Encoding.UTF8);
    }

    private static int ToSharpZipLevel(CompressionLevelOption opt) => opt switch
    {
        CompressionLevelOption.Store => 0,
        CompressionLevelOption.Fastest => 1,
        CompressionLevelOption.Normal => 6,
        CompressionLevelOption.Maximum => 9,
        _ => 6
    };
}
