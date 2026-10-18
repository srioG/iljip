using System.IO;
using Iljip.Models;
using SharpSevenZip;

namespace Iljip.Services;

/// <summary>
/// 7z.dll 로만 여는 형식(I2-9, W2-01): ZIPX(WinZip 확장 ZIP — LZMA·PPMd·xz 등), ISO, CAB, LZH/LHA, WIM, 단일 .xz.
/// 해제만 한다(압축은 지원하지 않음). 7z.dll 을 못 쓰면 쉬운 안내(<see cref="EngineUnavailableException"/>).
/// 형식은 내용(서명)으로 먼저 고르고, 모르면 확장자로 고른다. 어느 쪽이든 7z.dll 에 형식을 못박아 넘긴다.
/// ISO: UDF 가 들어 있으면(UDF 만·ISO9660+UDF 다리) UDF 로 열고, 못 열면 ISO9660 으로 한 번 더 연다(7-Zip·반디집처럼 UDF 우선 —
/// 1차 검증 지적 (d): UDF 만 있는 디스크가 '손상'으로, Joliet 없는 다리 이미지의 이름이 8.3 으로 나오던 것).
/// Joliet 이 없는 순수 ISO9660 은 원래 8.3 대문자 이름이다(반디집·7-Zip 도 같음).
/// 끝 잘림은 형식별 구조 검사(<see cref="ArchiveCompleteness"/> — ISO PVD·UDF AVDP, CAB 전체 크기, LZH 끝 바이트, XZ 꼬리)로 먼저 본다.
/// xz 는 7z.dll 로만 푼다 — SharpCompress 0.50.5 의 XZStream 은 잘린 xz 에서 끝없이 돈다(실측, 1차 검증 지적 (b)).
/// </summary>
public sealed class SevenZipFormatsArchiveService : IArchiveService, IArchiveTestable
{
    public IReadOnlyList<string> SupportedExtensions { get; } = new[] { ".zipx", ".iso", ".cab", ".lzh", ".lha", ".wim", ".xz" };

    /// <summary>압축 검사(W2-08): 7z.dll 시험(<see cref="SevenZipEngine.Test"/>) — 디스크에 쓰지 않음.</summary>
    TestReport IArchiveTestable.TestArchive(string archivePath, string? password, IProgress<ArchiveProgress>? progress, CancellationToken cancellationToken) =>
        SevenZipEngine.Test(CreateSource(archivePath), password, progress, cancellationToken);

    internal static SevenZipEngine.Source CreateSource(string archivePath)
    {
        SevenZipLibrary.ThrowIfUnavailable();
        var kind = ArchiveSniffer.Detect(archivePath, out _, peekInner: false);
        var format = kind switch
        {
            ArchiveKind.Zip => InArchiveFormat.Zip,
            ArchiveKind.Iso => InArchiveFormat.Iso,
            ArchiveKind.Udf => InArchiveFormat.Udf,
            ArchiveKind.Cab => InArchiveFormat.Cab,
            ArchiveKind.Lzh => InArchiveFormat.Lzh,
            ArchiveKind.Wim => InArchiveFormat.Wim,
            ArchiveKind.Xz or ArchiveKind.TarXz => InArchiveFormat.XZ,
            _ => FormatByExtension(archivePath),
        };
        return new SevenZipEngine.Source
        {
            ArchivePath = archivePath,
            Format = format,
            FallbackFormat = format == InArchiveFormat.Udf ? InArchiveFormat.Iso : null,
            OpenStream = () => new FileStream(archivePath, FileMode.Open, FileAccess.Read, FileShare.Read),
            Structure = format switch
            {
                InArchiveFormat.Iso or InArchiveFormat.Udf => StructureKind.DiscImage,
                InArchiveFormat.Cab => StructureKind.Cab,
                InArchiveFormat.Lzh => StructureKind.Lzh,
                InArchiveFormat.XZ => StructureKind.Xz,
                _ => StructureKind.None,   // ZIPX: 잘린 5,351 지점 모두 7z.dll·검증기가 잡음(실측), WIM: 7z.dll 에 맡김
            },
            SingleStream = format == InArchiveFormat.XZ,
            // ZIPX 이름: 머리의 날 이름 바이트를 SharpCompress 로 읽어 다른 ZIP 과 같은 한글 판별로(못 읽으면 7z.dll 이름 — OEM 코드 페이지)
            NameResolver = format == InArchiveFormat.Zip
                ? data => SevenZipEngineServiceBase.NamesFromSharpCompress(
                    options => SharpCompress.Archives.Zip.ZipArchive.OpenArchive(archivePath, options), data)
                : null,
            // ZIPX 목록 손상: ZIP 과 같은 목록·로컬 머리 대조(I4-1 검증 지적 — 7z.dll 은 목록 이름이 깨져도 말없이 풀어 '문제없어요'·종료 코드 0 이었다)
            ListCheck = format == InArchiveFormat.Zip ? items => ZipListCheck(archivePath, items) : null,
        };
    }

    /// <summary>
    /// ZIPX 의 목록 손상 검사(<see cref="ZipArchiveService.CheckZipStructure(Stream, int, out IReadOnlyList{ZipDirectory.CentralRecord})"/> — ZIP 과 같은 규칙).
    /// 7z.dll 항목은 목록 기록 차례라고 보되, 문제없는 기록마다 크기·CRC(폴더는 크기 0)가 7z.dll 항목과 맞을 때만 그 순번을 그대로 쓴다.
    /// 맞지 않으면 어느 항목이 문제인지 가릴 수 없어 모든 항목을 문제로 본다(풀지 않고 손상으로 알림 — 다른 파일 내용이 그 이름으로 풀리지 않게).
    /// </summary>
    internal static SevenZipEngine.ListProblems? ZipListCheck(string archivePath, IReadOnlyList<SevenZipEngine.Item> items)
    {
        SharpCompressArchiveServiceBase.StructureProblems? p;
        IReadOnlyList<ZipDirectory.CentralRecord> records;
        using (var s = new FileStream(archivePath, FileMode.Open, FileAccess.Read, FileShare.Read))
            p = ZipArchiveService.CheckZipStructure(s, items.Count, out records);
        if (p is null) return null;
        bool aligned = true;
        for (int i = 0; i < Math.Min(items.Count, records.Count) && aligned; i++)
        {
            if (p.BadEntries.Contains(i)) continue;
            var it = items[i];
            var r = records[i];
            uint crc = ZipDirectory.U32(r.Raw, 16);
            aligned = it.Index == i
                && (it.IsDirectory ? r.UncompressedSize == 0
                    : it.Size == r.UncompressedSize && (it.Crc == crc || it.Crc == 0 || crc == 0));
        }
        if (aligned) return new SevenZipEngine.ListProblems(p.Whole, new HashSet<int>(p.BadEntries));
        return new SevenZipEngine.ListProblems(p.Whole ?? ZipListDamagedException.Mismatch(null), new HashSet<int>(items.Select(i => i.Index)));
    }

    private static InArchiveFormat FormatByExtension(string path) => Path.GetExtension(path).ToLowerInvariant() switch
    {
        ".zipx" => InArchiveFormat.Zip,
        ".iso" => InArchiveFormat.Iso,
        ".cab" => InArchiveFormat.Cab,
        ".lzh" or ".lha" => InArchiveFormat.Lzh,
        ".wim" => InArchiveFormat.Wim,
        ".xz" => InArchiveFormat.XZ,
        _ => throw new UnsupportedFormatException(Path.GetExtension(path)),
    };

    public Task<IReadOnlyList<ArchiveEntry>> ListEntriesAsync(
        string archivePath,
        string? password = null,
        CancellationToken cancellationToken = default)
        => Task.Run(() => SevenZipEngine.List(CreateSource(archivePath), password, cancellationToken), cancellationToken);

    public Task ExtractAsync(
        string archivePath,
        string destinationFolder,
        string? password = null,
        IProgress<ArchiveProgress>? progress = null,
        CancellationToken cancellationToken = default)
        => Task.Run(() => SevenZipEngine.Extract(CreateSource(archivePath), destinationFolder, _ => true, password, progress, cancellationToken),
            cancellationToken);

    public Task ExtractEntriesAsync(
        string archivePath,
        string destinationFolder,
        IReadOnlyCollection<string> entryPaths,
        string? password = null,
        IProgress<ArchiveProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var selector = SharpCompressArchiveServiceBase.EntrySelector(entryPaths);
        return Task.Run(() => SevenZipEngine.Extract(CreateSource(archivePath), destinationFolder, selector, password, progress, cancellationToken),
            cancellationToken);
    }

    public Task<CompressResult> CompressAsync(
        IEnumerable<string> sourcePaths,
        string archivePath,
        CompressionOptions? options = null,
        IProgress<ArchiveProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException(
            $"{Path.GetExtension(archivePath).TrimStart('.').ToUpperInvariant()} 압축(생성)은 지원하지 않아요. (해제만 가능)\n" +
            "압축은 ZIP을 사용해주세요.");
    }
}
