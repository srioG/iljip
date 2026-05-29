using System.IO;
using System.Text;
using Iljip.Models;
using SharpCompress.Archives;
using SharpCompress.Common;
using SharpCompress.Readers;

namespace Iljip.Services;

/// <summary>
/// SharpCompress 기반 포맷들의 공통 베이스.
/// 압축 해제와 항목 목록 읽기는 ArchiveFactory.Open으로 모든 지원 포맷에서 동일하게 동작하므로
/// 여기에 공통 구현을 두고, 각 포맷별 압축 로직은 파생 클래스가 구현한다.
/// </summary>
public abstract class SharpCompressArchiveServiceBase : IArchiveService
{
    public abstract IReadOnlyList<string> SupportedExtensions { get; }

    public virtual Task<IReadOnlyList<ArchiveEntry>> ListEntriesAsync(
        string archivePath,
        string? password = null,
        CancellationToken cancellationToken = default)
    {
        return Task.Run<IReadOnlyList<ArchiveEntry>>(() =>
        {
            var options = BuildReaderOptions(password);
            using var archive = ArchiveFactory.Open(archivePath, options);

            var entries = new List<ArchiveEntry>();
            foreach (var e in archive.Entries)
            {
                cancellationToken.ThrowIfCancellationRequested();

                string key = e.Key ?? string.Empty;
                string decoded = KoreanFileNameDecoder.Decode(key);

                entries.Add(new ArchiveEntry
                {
                    Path = decoded,
                    Size = e.Size,
                    CompressedSize = e.CompressedSize,
                    IsDirectory = e.IsDirectory,
                    LastModified = e.LastModifiedTime
                });
            }
            return entries;
        }, cancellationToken);
    }

    public virtual Task ExtractAsync(
        string archivePath,
        string destinationFolder,
        string? password = null,
        IProgress<ArchiveProgress>? progress = null,
        CancellationToken cancellationToken = default)
        => ExtractCore(archivePath, destinationFolder, _ => true, password, progress, cancellationToken);

    public virtual Task ExtractEntriesAsync(
        string archivePath,
        string destinationFolder,
        IReadOnlyCollection<string> entryPaths,
        string? password = null,
        IProgress<ArchiveProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        // 선택자 정규화: 파일은 정확히 일치, 폴더는 그 하위 전체를 포함
        var selectors = entryPaths
            .Select(p => p.Replace('\\', '/').Trim('/'))
            .Where(p => p.Length > 0)
            .ToList();

        bool ShouldExtract(string decodedPath)
        {
            string d = decodedPath.Replace('\\', '/').TrimEnd('/');
            foreach (var s in selectors)
            {
                if (d.Equals(s, StringComparison.OrdinalIgnoreCase)) return true;
                if (d.StartsWith(s + "/", StringComparison.OrdinalIgnoreCase)) return true;
            }
            return false;
        }

        return ExtractCore(archivePath, destinationFolder, ShouldExtract, password, progress, cancellationToken);
    }

    /// <summary>해제 공통 구현. <paramref name="shouldExtract"/>로 항목을 필터링한다.</summary>
    private Task ExtractCore(
        string archivePath,
        string destinationFolder,
        Func<string, bool> shouldExtract,
        string? password,
        IProgress<ArchiveProgress>? progress,
        CancellationToken cancellationToken)
    {
        return Task.Run(() =>
        {
            Directory.CreateDirectory(destinationFolder);

            var options = BuildReaderOptions(password);
            using var archive = ArchiveFactory.Open(archivePath, options);

            long totalBytes = 0;
            int totalFiles = 0;
            foreach (var e in archive.Entries)
            {
                if (e.IsDirectory) continue;
                string dp = KoreanFileNameDecoder.Decode(e.Key ?? string.Empty);
                if (!shouldExtract(dp)) continue;
                totalBytes += e.Size;
                totalFiles++;
            }

            long bytesProcessed = 0;
            int filesProcessed = 0;
            string fullDestRoot = Path.GetFullPath(destinationFolder + Path.DirectorySeparatorChar);

            foreach (var entry in archive.Entries)
            {
                cancellationToken.ThrowIfCancellationRequested();

                string key = entry.Key ?? string.Empty;
                string decodedPath = KoreanFileNameDecoder.Decode(key);
                if (!shouldExtract(decodedPath)) continue;

                string outputPath = Path.Combine(destinationFolder, decodedPath);
                string fullOutputPath = Path.GetFullPath(outputPath);

                // Zip Slip 방어
                if (!fullOutputPath.StartsWith(fullDestRoot, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException($"안전하지 않은 경로: {decodedPath}");

                if (entry.IsDirectory)
                {
                    Directory.CreateDirectory(fullOutputPath);
                    continue;
                }

                string? parent = Path.GetDirectoryName(fullOutputPath);
                if (!string.IsNullOrEmpty(parent))
                    Directory.CreateDirectory(parent);

                progress?.Report(new ArchiveProgress
                {
                    CurrentFile = decodedPath,
                    BytesProcessed = bytesProcessed,
                    TotalBytes = totalBytes,
                    FilesProcessed = filesProcessed,
                    TotalFiles = totalFiles
                });

                using (var input = entry.OpenEntryStream())
                using (var output = File.Create(fullOutputPath))
                {
                    var buffer = new byte[81920];
                    int read;
                    while ((read = input.Read(buffer, 0, buffer.Length)) > 0)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        output.Write(buffer, 0, read);
                        bytesProcessed += read;

                        progress?.Report(new ArchiveProgress
                        {
                            CurrentFile = decodedPath,
                            BytesProcessed = bytesProcessed,
                            TotalBytes = totalBytes,
                            FilesProcessed = filesProcessed,
                            TotalFiles = totalFiles
                        });
                    }
                }

                if (entry.LastModifiedTime is DateTime dt)
                {
                    try { File.SetLastWriteTime(fullOutputPath, dt); }
                    catch { /* 일부 파일에서 실패 가능 */ }
                }

                filesProcessed++;
            }

            progress?.Report(new ArchiveProgress
            {
                CurrentFile = string.Empty,
                BytesProcessed = totalBytes,
                TotalBytes = totalBytes,
                FilesProcessed = filesProcessed,
                TotalFiles = totalFiles
            });
        }, cancellationToken);
    }

    public abstract Task CompressAsync(
        IEnumerable<string> sourcePaths,
        string archivePath,
        CompressionOptions? options = null,
        IProgress<ArchiveProgress>? progress = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 압축 시 소스 파일 평탄화. 폴더면 재귀, 파일이면 그대로.
    /// 폴더 자체의 이름을 entry 루트로 두는 반디집/알집 기본 동작과 동일.
    /// </summary>
    protected static List<(string sourceFile, string entryName)> CollectFiles(IEnumerable<string> sourcePaths)
    {
        var result = new List<(string, string)>();
        foreach (var src in sourcePaths)
        {
            if (File.Exists(src))
            {
                result.Add((src, Path.GetFileName(src)));
            }
            else if (Directory.Exists(src))
            {
                string baseDir = src.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                string parent = Path.GetDirectoryName(baseDir) ?? string.Empty;

                foreach (var file in Directory.EnumerateFiles(baseDir, "*", SearchOption.AllDirectories))
                {
                    string rel = Path.GetRelativePath(parent, file).Replace(Path.DirectorySeparatorChar, '/');
                    result.Add((file, rel));
                }
            }
        }
        return result;
    }

    /// <summary>입력 파일들의 총 바이트 합계 (진행률 계산용).</summary>
    protected static long SumFileSizes(IEnumerable<(string sourceFile, string entryName)> files)
    {
        long total = 0;
        foreach (var (sourceFile, _) in files)
        {
            try { total += new FileInfo(sourceFile).Length; }
            catch { /* 권한 등 */ }
        }
        return total;
    }

    private static ReaderOptions BuildReaderOptions(string? password) => new()
    {
        Password = password,
        ArchiveEncoding = new ArchiveEncoding
        {
            // CP437은 0x00~0xFF를 무손실 1:1 매핑 → CustomDecoder가 적용되지 않는
            // 경로에서도 원본 바이트가 보존되어 호출부의 KoreanFileNameDecoder.Decode로 복구 가능.
            Default = Encoding.GetEncoding(437),
            // 파일명 원본 바이트를 직접 받아 UTF-8/CP949를 자동 판별 (가장 견고).
            CustomDecoder = (bytes, index, count) =>
                KoreanFileNameDecoder.DecodeBytes(bytes, index, count)
        }
    };
}
