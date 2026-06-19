using System.IO;
using ICSharpCode.SharpZipLib.Zip;
using Iljip.Models;

namespace Iljip.Services;

/// <summary>
/// ZIP 포맷.
/// - 해제/목록: 베이스(SharpCompress, ArchiveFactory)에서 처리. 한글 파일명 자동 디코딩 포함.
/// - 압축: SharpZipLib로 처리. ZipCrypto + AES-256 암호 압축 지원.
/// </summary>
public sealed class ZipArchiveService : SharpCompressArchiveServiceBase
{
    public override IReadOnlyList<string> SupportedExtensions { get; } = new[] { ".zip" };

    public override Task CompressAsync(
        IEnumerable<string> sourcePaths,
        string archivePath,
        CompressionOptions? options = null,
        IProgress<ArchiveProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        options ??= CompressionOptions.Default;
        return Task.Run(() =>
        {
            var files = CollectFiles(sourcePaths);
            long totalBytes = SumFileSizes(files);
            int totalFiles = files.Count;
            long bytesProcessed = 0;
            int filesProcessed = 0;

            string? archiveDir = Path.GetDirectoryName(archivePath);
            if (!string.IsNullOrEmpty(archiveDir))
                Directory.CreateDirectory(archiveDir);

            using var fs = File.Create(archivePath);
            using var zipStream = new ZipOutputStream(fs)
            {
                IsStreamOwner = false,
                UseZip64 = UseZip64.Dynamic
            };

            zipStream.SetLevel(ToSharpZipLevel(options.Level));
            if (options.HasPassword)
            {
                zipStream.Password = options.Password;
            }

            foreach (var (sourceFile, entryName) in files)
            {
                cancellationToken.ThrowIfCancellationRequested();

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
                    // AES-256 (WinZip 호환). 암호 없으면 무시됨.
                    AESKeySize = options.HasPassword ? 256 : 0
                };

                zipStream.PutNextEntry(entry);

                progress?.Report(new ArchiveProgress
                {
                    CurrentFile = entryName,
                    BytesProcessed = bytesProcessed,
                    TotalBytes = totalBytes,
                    FilesProcessed = filesProcessed,
                    TotalFiles = totalFiles
                });

                using (var input = File.OpenRead(sourceFile))
                {
                    var buffer = new byte[81920];
                    int read;
                    while ((read = input.Read(buffer, 0, buffer.Length)) > 0)
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
                }

                zipStream.CloseEntry();
                filesProcessed++;
            }

            zipStream.Finish();

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

    private static int ToSharpZipLevel(CompressionLevelOption opt) => opt switch
    {
        CompressionLevelOption.Store => 0,
        CompressionLevelOption.Fastest => 1,
        CompressionLevelOption.Normal => 6,
        CompressionLevelOption.Maximum => 9,
        _ => 6
    };
}
