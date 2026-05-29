using System.IO;
using Iljip.Models;
using SharpCompress.Compressors;
using SharpCompress.Compressors.BZip2;

namespace Iljip.Services;

/// <summary>
/// BZIP2 포맷 — 단일 파일 압축 전용. GZip과 마찬가지로 폴더는 .tar.bz2 필요.
/// </summary>
public sealed class BZip2ArchiveService : SharpCompressArchiveServiceBase
{
    public override IReadOnlyList<string> SupportedExtensions { get; } = new[] { ".bz2" };

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
                throw new NotSupportedException("BZIP2는 암호화를 지원하지 않아요.");

            var srcList = sourcePaths.ToList();
            if (srcList.Count == 0)
                throw new InvalidOperationException("압축할 파일이 없습니다.");

            // BZ2도 단일 파일 포맷. 여러 항목이면 조용히 첫 파일만 압축하지 않고 명확히 안내.
            if (srcList.Count > 1)
                throw new InvalidOperationException(
                    "BZIP2(.bz2)는 파일 하나만 압축할 수 있어요.\n" +
                    "여러 개를 한 번에 압축하려면 ZIP을 사용하거나, 하나로 묶어 .tar.bz2로 만들어주세요. (.tar.bz2는 추후 지원 예정)");

            string sourceFile = srcList[0];
            if (Directory.Exists(sourceFile))
                throw new InvalidOperationException("BZIP2는 단일 파일만 압축할 수 있어요. 폴더는 .tar.bz2를 사용해주세요. (추후 지원 예정)");

            string? archiveDir = Path.GetDirectoryName(archivePath);
            if (!string.IsNullOrEmpty(archiveDir))
                Directory.CreateDirectory(archiveDir);

            long totalBytes = new FileInfo(sourceFile).Length;
            string entryName = Path.GetFileName(sourceFile);

            progress?.Report(new ArchiveProgress
            {
                CurrentFile = entryName,
                TotalBytes = totalBytes,
                TotalFiles = 1
            });

            using (var input = File.OpenRead(sourceFile))
            using (var output = File.Create(archivePath))
            using (var bz = new BZip2Stream(output, CompressionMode.Compress, decompressConcatenated: false))
            {
                var buffer = new byte[81920];
                long copied = 0;
                int read;
                while ((read = input.Read(buffer, 0, buffer.Length)) > 0)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    bz.Write(buffer, 0, read);
                    copied += read;

                    progress?.Report(new ArchiveProgress
                    {
                        CurrentFile = entryName,
                        BytesProcessed = copied,
                        TotalBytes = totalBytes,
                        TotalFiles = 1
                    });
                }
            }

            progress?.Report(new ArchiveProgress
            {
                BytesProcessed = totalBytes,
                TotalBytes = totalBytes,
                FilesProcessed = 1,
                TotalFiles = 1
            });
        }, cancellationToken);
    }
}
