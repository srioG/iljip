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

            // sourcePaths를 두 번(파일·빈폴더) 열거하므로 한 번만 구체화한다(지연 열거 재실행 방지).
            var srcList = sourcePaths as IList<string> ?? sourcePaths.ToList();
            var files = CollectFiles(srcList);
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

                // TarWriter 는 원본을 한 번에 통째로 복사하므로, 읽기마다 취소를 확인하고 진행률을 올리는
                // 래퍼로 감싼다(B6: 큰 파일을 쓰는 동안 누른 취소가 안 먹히고 마지막 파일이면 '완료'로 끝나던 문제).
                // 크기를 미리 넘겨 라이브러리가 길이를 따로 재지 않게 한다.
                long fileStart = bytesProcessed;
                long size;
                using (var input = new CancellableReadStream(File.OpenRead(sourceFile), cancellationToken, read =>
                       {
                           progress?.Report(new ArchiveProgress
                           {
                               CurrentFile = entryName,
                               BytesProcessed = Math.Min(totalBytes, fileStart + read),
                               TotalBytes = totalBytes,
                               FilesProcessed = filesProcessed,
                               TotalFiles = totalFiles
                           });
                       }))
                {
                    size = input.Length;
                    writer.Write(entryName, input, File.GetLastWriteTime(sourceFile), size);

                    // TAR 는 머리에 크기를 먼저 적으므로, 쓰는 도중 원본이 줄어들면 압축 파일이 조용히 깨진다 → 오류로 알린다.
                    if (input.BytesRead != size)
                        throw new IOException($"'{entryName}' 파일이 압축하는 도중에 바뀌었어요. 다시 해 주세요.");
                }

                bytesProcessed = Math.Min(totalBytes, fileStart + size);
                filesProcessed++;
            }

            // 빈 폴더 보존: 파일이 하나도 없는 폴더는 위 루프에서 빠지므로 폴더 항목으로 따로 기록한다(ZIP 과 같은 규칙).
            foreach (var (sourceDir, dirEntry) in CollectEmptyDirectoriesWithSource(srcList))
            {
                cancellationToken.ThrowIfCancellationRequested();
                DateTime? mtime = null;
                try { mtime = Directory.GetLastWriteTime(sourceDir); }
                catch { /* 권한 등 — 시각 없이 기록 */ }
                writer.WriteDirectory(dirEntry, mtime);
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
