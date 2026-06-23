using System.IO;
using EggDotNet;
using Iljip.Models;

namespace Iljip.Services;

/// <summary>
/// 이스트소프트 EGG/ALZ 해제 전용 서비스 (EggDotNet 기반).
///
/// SharpCompress는 egg/alz를 못 풀기 때문에 <see cref="SharpCompressArchiveServiceBase"/>를
/// 상속하지 않고 <see cref="IArchiveService"/>를 직접 구현한다. 따라서 베이스가 주던 안전장치
/// (Zip Slip 방어·동명파일 비파괴 자동개명·진행률 보고)를 여기서 직접 갖춘다.
///
/// EggDotNet은 store/deflate/bzip2/lzma + 이스트소프트 독자 AZO + 분할볼륨(.volN.egg)을 모두
/// 해제한다(자체 테스트 픽스처로 CRC 일치 검증 완료). 압축(생성)은 지원하지 않는다(반디집/알집 영역).
/// EGG 파일명은 유니코드, ALZ는 CP949이며 EggDotNet이 디코드한 FullName을 그대로 쓴다(이중 디코드 금지).
/// </summary>
public sealed class EggAlzArchiveService : IArchiveService
{
    public IReadOnlyList<string> SupportedExtensions { get; } = new[] { ".egg", ".alz" };

    public Task<IReadOnlyList<ArchiveEntry>> ListEntriesAsync(
        string archivePath,
        string? password = null,
        CancellationToken cancellationToken = default)
    {
        return Task.Run<IReadOnlyList<ArchiveEntry>>(() =>
        {
            using var archive = EggFile.Open(archivePath);
            var entries = new List<ArchiveEntry>();
            foreach (var e in archive.Entries)
            {
                cancellationToken.ThrowIfCancellationRequested();
                string full = (e.FullName ?? e.Name ?? string.Empty).Replace('\\', '/');
                entries.Add(new ArchiveEntry
                {
                    Path = full,
                    Size = e.UncompressedLength,
                    CompressedSize = e.CompressedLength,
                    IsDirectory = full.EndsWith("/", StringComparison.Ordinal),
                    LastModified = e.LastWriteTime
                });
            }
            return entries;
        }, cancellationToken);
    }

    public Task ExtractAsync(
        string archivePath,
        string destinationFolder,
        string? password = null,
        IProgress<ArchiveProgress>? progress = null,
        CancellationToken cancellationToken = default)
        => ExtractCore(archivePath, destinationFolder, _ => true, progress, cancellationToken);

    public Task ExtractEntriesAsync(
        string archivePath,
        string destinationFolder,
        IReadOnlyCollection<string> entryPaths,
        string? password = null,
        IProgress<ArchiveProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
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

        return ExtractCore(archivePath, destinationFolder, ShouldExtract, progress, cancellationToken);
    }

    private static Task ExtractCore(
        string archivePath,
        string destinationFolder,
        Func<string, bool> shouldExtract,
        IProgress<ArchiveProgress>? progress,
        CancellationToken cancellationToken)
    {
        return Task.Run(() =>
        {
            Directory.CreateDirectory(destinationFolder);
            using var archive = EggFile.Open(archivePath);

            // 진행률 총량 선계산 (디렉터리·필터제외·암호화 제외분은 빼고 파일만)
            long totalBytes = 0;
            int totalFiles = 0;
            foreach (var e in archive.Entries)
            {
                string fp = (e.FullName ?? e.Name ?? string.Empty).Replace('\\', '/');
                if (fp.EndsWith("/", StringComparison.Ordinal)) continue;
                if (!shouldExtract(fp)) continue;
                totalBytes += e.UncompressedLength;
                totalFiles++;
            }

            long bytesProcessed = 0;
            int filesProcessed = 0;
            string fullDestRoot = Path.GetFullPath(destinationFolder + Path.DirectorySeparatorChar);

            foreach (var entry in archive.Entries)
            {
                cancellationToken.ThrowIfCancellationRequested();

                string decodedPath = (entry.FullName ?? entry.Name ?? string.Empty).Replace('\\', '/');
                if (decodedPath.Length == 0) continue;
                if (!shouldExtract(decodedPath)) continue;

                string outputPath = Path.Combine(destinationFolder, decodedPath);
                string fullOutputPath = Path.GetFullPath(outputPath);

                // Zip Slip 방어: 해제 경로가 대상 폴더 밖으로 나가면 거부.
                if (!fullOutputPath.StartsWith(fullDestRoot, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException($"안전하지 않은 경로: {decodedPath}");

                if (decodedPath.EndsWith("/", StringComparison.Ordinal))
                {
                    Directory.CreateDirectory(fullOutputPath);
                    continue;
                }

                string? parent = Path.GetDirectoryName(fullOutputPath);
                if (!string.IsNullOrEmpty(parent))
                    Directory.CreateDirectory(parent);

                // 동명 파일이 이미 있으면 덮어쓰지 않고 "이름 (1).ext" 식으로 → 원본 유실 방지.
                string writePath = GetNonCollidingPath(fullOutputPath);

                progress?.Report(new ArchiveProgress
                {
                    CurrentFile = decodedPath,
                    BytesProcessed = bytesProcessed,
                    TotalBytes = totalBytes,
                    FilesProcessed = filesProcessed,
                    TotalFiles = totalFiles
                });

                // entry.Open()이 store/deflate/bzip2/lzma/AZO·암호화를 알아서 해제해 평문 스트림을 준다.
                // 암호화인데 비번이 없으면 여기서 예외 → 해제 전체 실패(부분 추출로 위장하지 않음).
                using (var input = entry.Open())
                using (var output = File.Create(writePath))
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

                if (entry.LastWriteTime is { } lwt)
                {
                    try { File.SetLastWriteTime(writePath, lwt); }
                    catch { /* 일부 값/파일에서 실패 가능 — 무시 */ }
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

    public Task CompressAsync(
        IEnumerable<string> sourcePaths,
        string archivePath,
        CompressionOptions? options = null,
        IProgress<ArchiveProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException(
            "EGG/ALZ 압축(생성)은 지원하지 않아요. (해제만 가능)\n" +
            "압축은 ZIP을 사용해주세요.");
    }

    /// <summary>
    /// 대상 경로에 동명 파일/디렉터리가 이미 있으면 "이름 (1).ext" … 식으로 충돌 없는 새 경로를
    /// 만들어 반환한다(기존 파일 비파괴). SharpCompressArchiveServiceBase와 동일 규칙.
    /// </summary>
    private static string GetNonCollidingPath(string desiredPath)
    {
        if (!File.Exists(desiredPath) && !Directory.Exists(desiredPath))
            return desiredPath;

        string? dir = Path.GetDirectoryName(desiredPath);
        string name = Path.GetFileNameWithoutExtension(desiredPath);
        string ext = Path.GetExtension(desiredPath);

        for (int i = 1; i < int.MaxValue; i++)
        {
            string candidateName = $"{name} ({i}){ext}";
            string candidate = string.IsNullOrEmpty(dir)
                ? candidateName
                : Path.Combine(dir, candidateName);
            if (!File.Exists(candidate) && !Directory.Exists(candidate))
                return candidate;
        }
        return desiredPath;
    }
}
