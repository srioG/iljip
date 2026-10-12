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
/// 실행 파일은 만들지 않는다(압축 불가 — 자체 스텁은 W2-10).
/// </summary>
public sealed class SfxArchiveService : SharpCompressArchiveServiceBase
{
    public override IReadOnlyList<string> SupportedExtensions { get; } = Array.Empty<string>();

    private readonly EggAlzArchiveService _eggAlz = new();

    private static (ArchiveKind Kind, long Offset) Detect(string archivePath)
    {
        var kind = ArchiveSniffer.Detect(archivePath, out long offset);
        if (kind is not (ArchiveKind.ZipSfx or ArchiveKind.SevenZipSfx or ArchiveKind.RarSfx or ArchiveKind.EggSfx or ArchiveKind.AlzSfx))
            throw new CorruptArchiveException();   // 그새 바뀌었거나 안쪽 압축을 못 찾음
        return (kind, offset);
    }

    private static Stream OpenInner(string archivePath, long offset) =>
        new OffsetStream(new FileStream(archivePath, FileMode.Open, FileAccess.Read, FileShare.Read), offset);

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
            var (kind, offset) = Detect(archivePath);
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
            var (kind, offset) = Detect(archivePath);
            Func<string, bool> shouldExtract = entryPaths is null ? _ => true : EntrySelector(entryPaths);
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
    /// SharpCompress 로 여는 안쪽 압축: ZIP SFX(늘), 7z·RAR SFX(7z.dll 을 못 쓸 때). 서명(ZIP 은 위치값 기준점)부터 보이는 스트림을
    /// 그 형식으로 연다(스트림은 아카이브가 닫을 때 같이 닫힘).
    /// </summary>
    protected override IArchive OpenForRead(string archivePath, ReaderOptions options)
    {
        var (kind, offset) = Detect(archivePath);
        var stream = OpenInner(archivePath, offset);
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
            var (kind, offset) = Detect(archivePath);
            using var s = OpenInner(archivePath, offset);
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

    public override Task<CompressResult> CompressAsync(
        IEnumerable<string> sourcePaths,
        string archivePath,
        CompressionOptions? options = null,
        IProgress<ArchiveProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException("자동 풀림 실행 파일(.exe)은 만들 수 없어요. (풀기만 가능)\n압축은 ZIP을 사용해주세요.");
    }
}
