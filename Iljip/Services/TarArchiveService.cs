using System.IO;
using System.Text;
using Iljip.Models;
using SharpCompress.Common;
using SharpCompress.Writers;
using SharpCompress.Writers.Tar;

namespace Iljip.Services;

/// <summary>
/// TAR 포맷. 압축 자체는 없고 묶기만 함.
/// .tar.gz, .tar.bz2 같은 합성 확장자는 일단 단일 .tar만 지원.
/// </summary>
public sealed class TarArchiveService : SharpCompressArchiveServiceBase
{
    public override IReadOnlyList<string> SupportedExtensions { get; } = new[] { ".tar" };

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
                throw new NotSupportedException("TAR는 암호화를 지원하지 않아요.");

            // NOTE(잔존): SharpCompress TarWriter는 디렉터리 엔트리 쓰기 공개 API가 없어
            //   빈 폴더 보존(ZIP은 지원)은 TAR에서 미지원이다. 추후 TarWriter 확장/직접 헤더 기록 필요.
            var files = CollectFiles(sourcePaths);
            long totalBytes = SumFileSizes(files);
            int totalFiles = files.Count;
            long bytesProcessed = 0;
            int filesProcessed = 0;

            string? archiveDir = Path.GetDirectoryName(archivePath);
            if (!string.IsNullOrEmpty(archiveDir))
                Directory.CreateDirectory(archiveDir);

            var writerOptions = new TarWriterOptions(CompressionType.None, finalizeArchiveOnClose: true)
            {
                ArchiveEncoding = new ArchiveEncoding { Default = Encoding.UTF8 }
            };

            using var stream = File.Create(archivePath);
            using var writer = new TarWriter(stream, writerOptions);

            foreach (var (sourceFile, entryName) in files)
            {
                cancellationToken.ThrowIfCancellationRequested();
                progress?.Report(new ArchiveProgress
                {
                    CurrentFile = entryName,
                    BytesProcessed = bytesProcessed,
                    TotalBytes = totalBytes,
                    FilesProcessed = filesProcessed,
                    TotalFiles = totalFiles
                });

                using var input = File.OpenRead(sourceFile);
                writer.Write(entryName, input, File.GetLastWriteTime(sourceFile));

                try { bytesProcessed += new FileInfo(sourceFile).Length; }
                catch { }
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
}
