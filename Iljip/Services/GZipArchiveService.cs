using System.IO;
using Iljip.Models;
using SharpCompress.Compressors;
using SharpCompress.Compressors.Deflate;

namespace Iljip.Services;

/// <summary>
/// GZIP 포맷 — 단일 파일 압축 전용.
/// 해제는 베이스 클래스가 ArchiveFactory.Open으로 처리.
/// 압축은 SharpCompress.Compressors.Deflate.GZipStream 사용.
/// 여러 파일이 입력되면 첫 파일만 압축 (.tar.gz는 별도 향후 작업).
/// </summary>
public sealed class GZipArchiveService : SharpCompressArchiveServiceBase
{
    public override IReadOnlyList<string> SupportedExtensions { get; } = new[] { ".gz" };

    public override Task CompressAsync(
        IEnumerable<string> sourcePaths,
        string archivePath,
        CompressionOptions? options = null,
        IProgress<ArchiveProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        return Task.Run(() =>
        {
            if (options?.HasPassword == true)
                throw new NotSupportedException("GZIP은 암호화를 지원하지 않아요.");

            var srcList = sourcePaths.ToList();
            if (srcList.Count == 0)
                throw new InvalidOperationException("압축할 파일이 없습니다.");

            // GZ는 단일 파일 포맷이라 여러 항목을 담을 수 없다. 조용히 첫 파일만 압축하지 않고 명확히 안내.
            if (srcList.Count > 1)
                throw new InvalidOperationException(
                    "GZIP(.gz)은 파일 하나만 압축할 수 있어요.\n" +
                    "여러 개를 한 번에 압축하려면 ZIP을 사용하거나, 하나로 묶어 .tar.gz로 만들어주세요. (.tar.gz는 추후 지원 예정)");

            string sourceFile = srcList[0];
            if (Directory.Exists(sourceFile))
                throw new InvalidOperationException("GZIP은 단일 파일만 압축할 수 있어요. 폴더는 .tar.gz를 사용해주세요. (추후 지원 예정)");

            string? archiveDir = Path.GetDirectoryName(archivePath);
            if (!string.IsNullOrEmpty(archiveDir))
                Directory.CreateDirectory(archiveDir);

            long totalBytes = new FileInfo(sourceFile).Length;
            string entryName = Path.GetFileName(sourceFile);

            progress?.Report(new ArchiveProgress
            {
                CurrentFile = entryName,
                BytesProcessed = 0,
                TotalBytes = totalBytes,
                FilesProcessed = 0,
                TotalFiles = 1
            });

            using (var input = File.OpenRead(sourceFile))
            using (var output = File.Create(archivePath))
            using (var gz = new GZipStream(output, CompressionMode.Compress, ToGZipLevel(options?.Level)))
            {
                var buffer = new byte[81920];
                long copied = 0;
                int read;
                while ((read = input.Read(buffer, 0, buffer.Length)) > 0)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    gz.Write(buffer, 0, read);
                    copied += read;

                    progress?.Report(new ArchiveProgress
                    {
                        CurrentFile = entryName,
                        BytesProcessed = copied,
                        TotalBytes = totalBytes,
                        FilesProcessed = 0,
                        TotalFiles = 1
                    });
                }
            }

            progress?.Report(new ArchiveProgress
            {
                CurrentFile = string.Empty,
                BytesProcessed = totalBytes,
                TotalBytes = totalBytes,
                FilesProcessed = 1,
                TotalFiles = 1
            });
        }, cancellationToken);
    }

    private static CompressionLevel ToGZipLevel(CompressionLevelOption? opt) => opt switch
    {
        CompressionLevelOption.Store => CompressionLevel.None,
        CompressionLevelOption.Fastest => CompressionLevel.BestSpeed,
        CompressionLevelOption.Normal => CompressionLevel.Default,
        CompressionLevelOption.Maximum => CompressionLevel.BestCompression,
        _ => CompressionLevel.Default
    };
}
