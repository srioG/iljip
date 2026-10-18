using System.IO;
using Iljip.Models;
using SharpCompress.Archives;
using SharpCompress.Readers;
using SharpSevenZip;

namespace Iljip.Services;

/// <summary>
/// 자동 풀림 실행 파일(SFX .exe, I2-9·W2-01) — 실행하지 않고 안쪽 압축만 연다. 확장자로는 고르지 않고 내용으로만(<see cref="ArchiveSniffer"/> —
/// 일집 안의 열기 창·파일 연결 '--open'·창 없이 풀기에서만. 탐색기 우클릭·끌어 놓기는 확장자만 보므로 .exe 는 압축으로 치지 않는다).
/// - ZIP SFX(반디집 등): ZIP 위치값의 기준점부터 보이는 스트림을 SharpCompress ZIP 으로(한글 이름 판별이 ZIP 과 같게).
/// - 7z SFX(7-Zip)·RAR SFX(WinRAR): 서명 위치부터 보이는 스트림을 7z.dll 로. 7z.dll 을 못 쓰면 같은 스트림을 SharpCompress 로.
/// - EGG·ALZ SFX(알집): EGG/ALZ 처리기(<see cref="EggAlzArchiveService"/>)가 서명 위치부터 연다.
/// 만들기는 일집 자체 스텁으로 된 자동 풀림 exe 만(W2-10 — <see cref="SfxBuilder"/>). 일집이 만든 것은 꼬리('ILJIPSFX')로 알아보고 꼬리를 뺀 ZIP 부분만 연다.
/// </summary>
public sealed class SfxArchiveService : SharpCompressArchiveServiceBase
{
    public override IReadOnlyList<string> SupportedExtensions { get; } = Array.Empty<string>();

    private readonly EggAlzArchiveService _eggAlz = new();

    /// <summary>
    /// ZIP 자동 풀림(일집·반디집 등)은 ZIP 처리기에 맡긴다(W2-10) — exe 경로를 받으면 ZIP 부분만 보고(<see cref="ZipArchiveService.OpenRawStream"/>)
    /// 보통 ZIP 과 같은 암호 판정(반디집·일집의 CP949 한글 ZipCrypto 암호)·WinZip AES 인증값·CRC 검사·이름 판별을 한다.
    /// (W2-01 에서는 여기서 SharpCompress 로 바로 열어 한글 ZipCrypto 암호·AES 인증값을 못 봤다)
    /// </summary>
    private readonly ZipArchiveService _zip = new();

    /// <summary>
    /// 모든 항목을 끝까지 풀어 보며 확인값까지 본다(디스크에 쓰지 않음 — '다 만들고 시험하기', <see cref="CreatedArchiveTester"/>). ZIP 자동 풀림은 ZIP 처리기로.
    /// </summary>
    internal int TestSync(string archivePath, string? password, CancellationToken cancellationToken)
    {
        var (kind, _, _) = Detect(archivePath);
        return kind == ArchiveKind.ZipSfx
            ? _zip.TestEntriesSync(archivePath, password, _ => true, cancellationToken)
            : TestEntriesSync(archivePath, password, _ => true, cancellationToken);
    }

    /// <summary>
    /// 안쪽 압축의 종류·시작 위치·길이(-1 = 파일 끝까지). 일집이 만든 자동 풀림 exe(W2-10 — 꼬리 'ILJIPSFX')는 꼬리가 적은 ZIP 부분만
    /// (꼬리 24바이트를 빼서 보통 ZIP 과 똑같이 보이게 — <see cref="SfxBuilder"/>).
    /// </summary>
    private static (ArchiveKind Kind, long Offset, long Length) Detect(string archivePath)
    {
        if (IljipSfxData(archivePath) is { } own) return (ArchiveKind.ZipSfx, own.Start, own.Length);
        var kind = ArchiveSniffer.Detect(archivePath, out long offset);
        if (kind is not (ArchiveKind.ZipSfx or ArchiveKind.SevenZipSfx or ArchiveKind.RarSfx or ArchiveKind.EggSfx or ArchiveKind.AlzSfx))
            throw new CorruptArchiveException();   // 그새 바뀌었거나 안쪽 압축을 못 찾음
        // 끝이 잘린 ZIP 자동 풀림(일집 꼬리·ZIP 끝 머리가 없음 — 덜 받아진 첨부)은 엔진에 맡기지 않고 바로 '끝부분이 잘려 있어요'(W2-10 검증 지적 2차 —
        // 예전엔 '지원하지 않는 형식'이었고, 엔진에 맡기면 'Failed to locate the Zip Header' 같은 영어로 끝남). 목록·풀기·시험 모두 여기를 지난다.
        if (kind == ArchiveKind.ZipSfx && ArchiveSniffer.IsTruncatedZipSfx(archivePath)) throw CorruptArchiveException.Truncated();
        return (kind, offset, -1);
    }

    /// <summary>일집 자동 풀림 exe 면 ZIP 데이터의 시작·길이(꼬리가 맞고 그 자리가 ZIP 머리로 시작). 아니면 null.</summary>
    internal static (long Start, long Length)? IljipSfxData(string archivePath)
    {
        try
        {
            using var fs = new FileStream(archivePath, FileMode.Open, FileAccess.Read, FileShare.Read);
            return ArchiveSniffer.IljipSfxData(fs);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static Stream OpenInner(string archivePath, long offset, long length = -1)
    {
        var fs = new FileStream(archivePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        return length >= 0 ? new OffsetStream(fs, offset, length) : new OffsetStream(fs, offset);
    }

    /// <summary>7z.dll 로 열 안쪽 압축(7z·RAR). 서명 위치부터 보이는 스트림. 7z.dll 을 못 쓰면 null(SharpCompress 로).</summary>
    private static SevenZipEngine.Source? EngineSource(string archivePath, ArchiveKind kind, long offset)
    {
        if (!SevenZipLibrary.IsAvailable) return null;
        Func<Stream> open = () => OpenInner(archivePath, offset);
        bool headerEncrypted;
        InArchiveFormat format;
        using (var probe = open())
        {
            if (kind == ArchiveKind.RarSfx)
            {
                var head = new byte[64];
                int n = probe.Read(head, 0, head.Length);
                var info = ArchiveHeaderProbe.ProbeRar(head.AsSpan(0, n));
                headerEncrypted = info.HeaderEncrypted;
                format = info.IsRar && !info.IsRar5 ? InArchiveFormat.Rar4 : InArchiveFormat.Rar;
            }
            else
            {
                headerEncrypted = ArchiveHeaderProbe.IsSevenZipHeaderEncrypted(probe);
                format = InArchiveFormat.SevenZip;
            }
        }
        return new SevenZipEngine.Source
        {
            ArchivePath = archivePath,
            Format = format,
            OpenStream = open,
            HeaderEncrypted = headerEncrypted,
            Structure = kind == ArchiveKind.RarSfx ? StructureKind.Rar : StructureKind.None,
        };
    }

    /// <summary>(7z.dll 을 못 쓸 때) 안쪽 RAR 이 잘렸으면 SharpCompress 로 열기 전에 알린다.</summary>
    private static void ThrowIfInnerRarTruncated(string archivePath, ArchiveKind kind, long offset)
    {
        if (kind != ArchiveKind.RarSfx) return;
        using var s = OpenInner(archivePath, offset);
        if (ArchiveCompleteness.Check(s, StructureKind.Rar) is { } problem) throw problem;
    }

    public override Task<IReadOnlyList<ArchiveEntry>> ListEntriesAsync(
        string archivePath,
        string? password = null,
        CancellationToken cancellationToken = default)
    {
        return Task.Run(async () =>
        {
            var (kind, offset, _) = Detect(archivePath);
            if (kind == ArchiveKind.ZipSfx)
                return await _zip.ListEntriesAsync(archivePath, password, cancellationToken);
            if (kind is ArchiveKind.EggSfx or ArchiveKind.AlzSfx)
                return await _eggAlz.ListEntriesAsync(archivePath, password, cancellationToken);
            if (kind is (ArchiveKind.SevenZipSfx or ArchiveKind.RarSfx) && EngineSource(archivePath, kind, offset) is { } src)
                return SevenZipEngine.List(src, password, cancellationToken);
            ThrowIfInnerRarTruncated(archivePath, kind, offset);
            return ListCore(archivePath, password, cancellationToken);
        }, cancellationToken);
    }

    public override Task ExtractAsync(
        string archivePath,
        string destinationFolder,
        string? password = null,
        IProgress<ArchiveProgress>? progress = null,
        CancellationToken cancellationToken = default)
        => Extract(archivePath, destinationFolder, null, password, progress, cancellationToken);

    public override Task ExtractEntriesAsync(
        string archivePath,
        string destinationFolder,
        IReadOnlyCollection<string> entryPaths,
        string? password = null,
        IProgress<ArchiveProgress>? progress = null,
        CancellationToken cancellationToken = default)
        => Extract(archivePath, destinationFolder, entryPaths, password, progress, cancellationToken);

    private Task Extract(
        string archivePath,
        string destinationFolder,
        IReadOnlyCollection<string>? entryPaths,
        string? password,
        IProgress<ArchiveProgress>? progress,
        CancellationToken cancellationToken)
    {
        return Task.Run(async () =>
        {
            var (kind, offset, _) = Detect(archivePath);
            Func<string, bool> shouldExtract = entryPaths is null ? _ => true : EntrySelector(entryPaths);
            if (kind == ArchiveKind.ZipSfx)
            {
                if (entryPaths is null) await _zip.ExtractAsync(archivePath, destinationFolder, password, progress, cancellationToken);
                else await _zip.ExtractEntriesAsync(archivePath, destinationFolder, entryPaths, password, progress, cancellationToken);
                return;
            }
            if (kind is ArchiveKind.EggSfx or ArchiveKind.AlzSfx)
            {
                if (entryPaths is null) await _eggAlz.ExtractAsync(archivePath, destinationFolder, password, progress, cancellationToken);
                else await _eggAlz.ExtractEntriesAsync(archivePath, destinationFolder, entryPaths, password, progress, cancellationToken);
                return;
            }
            if (kind is (ArchiveKind.SevenZipSfx or ArchiveKind.RarSfx) && EngineSource(archivePath, kind, offset) is { } src)
            {
                SevenZipEngine.Extract(src, destinationFolder, shouldExtract, password, progress, cancellationToken);
                return;
            }
            ThrowIfInnerRarTruncated(archivePath, kind, offset);
            ExtractCoreSync(archivePath, destinationFolder, shouldExtract, password, progress, cancellationToken);
        }, cancellationToken);
    }

    /// <summary>
    /// 압축 검사(W2-08 — IJ3-02 에서 다시): 안쪽 압축의 처리기 그대로(ZIP → ZIP 처리기 — 일집·반디집의 CP949 한글 ZipCrypto 암호·AES 인증값·목록 맞춰 보기,
    /// EGG·ALZ → EGG/ALZ 처리기, 7z·RAR → 7z.dll, 7z.dll 없음 → SharpCompress).
    /// </summary>
    internal override TestReport TestArchive(string archivePath, string? password, IProgress<ArchiveProgress>? progress, CancellationToken cancellationToken)
    {
        var (kind, offset, _) = Detect(archivePath);
        if (kind == ArchiveKind.ZipSfx)
            return _zip.TestArchive(archivePath, password, progress, cancellationToken);
        if (kind is ArchiveKind.EggSfx or ArchiveKind.AlzSfx)
            return ((IArchiveTestable)_eggAlz).TestArchive(archivePath, password, progress, cancellationToken);
        if (kind is (ArchiveKind.SevenZipSfx or ArchiveKind.RarSfx) && EngineSource(archivePath, kind, offset) is { } src)
            return SevenZipEngine.Test(src, password, progress, cancellationToken);
        ThrowIfInnerRarTruncated(archivePath, kind, offset);
        return TestArchiveCore(archivePath, password, progress, cancellationToken);
    }

    /// <summary>
    /// SharpCompress 로 여는 안쪽 압축: ZIP SFX(늘), 7z·RAR SFX(7z.dll 을 못 쓸 때). 서명(ZIP 은 위치값 기준점)부터 보이는 스트림을
    /// 그 형식으로 연다(스트림은 아카이브가 닫을 때 같이 닫힘).
    /// </summary>
    protected override IArchive OpenForRead(string archivePath, ReaderOptions options)
    {
        var (kind, offset, length) = Detect(archivePath);
        var stream = OpenInner(archivePath, offset, length);
        try
        {
            var o = options with { LeaveStreamOpen = false };
            return kind switch
            {
                ArchiveKind.SevenZipSfx => SharpCompress.Archives.SevenZip.SevenZipArchive.OpenArchive(stream, o),
                ArchiveKind.RarSfx => SharpCompress.Archives.Rar.RarArchive.OpenArchive(stream, o),
                _ => SharpCompress.Archives.Zip.ZipArchive.OpenArchive(stream, o),
            };
        }
        catch
        {
            stream.Dispose();
            throw;
        }
    }

    /// <summary>
    /// (7z.dll 을 못 쓸 때 — SharpCompress 로 여는 안쪽 7z·RAR) 안쪽이 목록까지 잠긴 압축(7z 헤더 암호·RAR 머리 암호)이면
    /// 열기·목록 단계의 암호·데이터 오류를 <see cref="ListPasswordRequiredException"/>(암호 없음)·머리 암호 틀림으로 바꾼다(W2-02, I1-7 —
    /// 7z.dll 경로는 <see cref="SevenZipEngine"/> 가 머리 바이트로 같은 예외를 낸다).
    /// </summary>
    protected override Exception? TranslateError(Exception ex, string archivePath, string? password, IArchiveEntry? entry, string? entryPath)
    {
        if (entry is null && ex is not ArchiveException and not OperationCanceledException
            && (ex is SharpCompress.Common.CryptographicException || EngineErrorTranslator.IsDataError(ex))
            && InnerHeaderEncrypted(archivePath))
        {
            return string.IsNullOrEmpty(password)
                ? new ListPasswordRequiredException(ex)
                : new WrongPasswordException(isHeaderEncrypted: true, inner: ex);
        }
        return base.TranslateError(ex, archivePath, password, entry, entryPath);
    }

    /// <summary>안쪽 7z·RAR 의 머리(목록)가 암호로 잠겼는지 — 실패 번역 때만 읽는다. 못 읽으면 false.</summary>
    private static bool InnerHeaderEncrypted(string archivePath)
    {
        try
        {
            var (kind, offset, length) = Detect(archivePath);
            using var s = OpenInner(archivePath, offset, length);
            if (kind == ArchiveKind.SevenZipSfx) return ArchiveHeaderProbe.IsSevenZipHeaderEncrypted(s);
            if (kind != ArchiveKind.RarSfx) return false;
            var head = new byte[64];
            int n = s.Read(head, 0, head.Length);
            return ArchiveHeaderProbe.ProbeRar(head.AsSpan(0, n)).HeaderEncrypted;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// 자동 풀림 exe 만들기(W2-10, I3-1) — 일집 자체 스텁 + ZIP + 꼬리(<see cref="SfxBuilder"/>). 압축 창의 '자동 풀림 EXE' 형식이 이 처리기로 온다
    /// (<see cref="ArchiveServiceLocator.ForExtension"/> 의 '.exe' — 열기·풀기 라우팅은 여전히 내용으로만).
    /// </summary>
    public override Task<CompressResult> CompressAsync(
        IEnumerable<string> sourcePaths,
        string archivePath,
        CompressionOptions? options = null,
        IProgress<ArchiveProgress>? progress = null,
        CancellationToken cancellationToken = default)
        => SfxBuilder.CreateAsync(sourcePaths, archivePath, options, progress, cancellationToken);
}
