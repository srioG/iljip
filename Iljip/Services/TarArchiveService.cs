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

    public override Task<CompressResult> CompressAsync(
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

            // 파일·빈 폴더·못 들어간 폴더를 한 번에 모은다(I0-2 — 하위 폴더 권한 오류로 전체가 실패하지 않게).
            var sources = SourceCollector.Collect(sourcePaths);
            // 읽는 도중 막힌 원본이 있으면 그 파일을 빼고 처음부터 다시 쓴다(베이스 RewriteWithoutUnreadable)
            return RewriteWithoutUnreadable(archivePath,
                dropped => WriteTar(sources, dropped, archivePath, progress, cancellationToken));
        }, cancellationToken);
    }

    /// <summary>
    /// TAR 를 한 번 쓴다. <paramref name="dropped"/>(앞 회차에서 읽는 도중 막힌 파일)는 열지 않고 건너뜀으로 적는다.
    /// 담은 파일이 하나도 없고 건너뛴 것이 있으면 압축 파일을 만들지 않고 <see cref="NothingToCompressException"/>(I0-2).
    /// </summary>
    private static CompressResult WriteTar(
        SourceSet sources, IReadOnlyDictionary<string, string> dropped, string archivePath,
        IProgress<ArchiveProgress>? progress, CancellationToken cancellationToken)
    {
        var files = sources.Files;
        var skipped = new List<SkippedSource>(sources.Skipped);
        long totalBytes = SumFileSizes(files);
        int totalFiles = files.Count;
        long bytesProcessed = 0;
        int filesProcessed = 0;

        var writerOptions = new TarWriterOptions(CompressionType.None, finalizeArchiveOnClose: true)
        {
            ArchiveEncoding = new ArchiveEncoding { Default = Encoding.UTF8 }
        };

        // 압축 파일은 담을 첫 파일을 연 뒤에 만든다 — 모두 못 읽으면 빈 tar 를 남기지 않게(덮어쓰려던 기존 파일도 그대로)
        using var output = new LazyArchiveOutput<TarWriter>(archivePath, fs => new TarWriter(fs, writerOptions));

        foreach (var (sourceFile, entryName) in files)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // 압축 파일을 원본 폴더 안에 저장하는 경우 자기 자신은 담지 않는다
            if (IsSameFile(sourceFile, archivePath)) continue;

            progress?.Report(new ArchiveProgress
            {
                CurrentFile = entryName,
                BytesProcessed = bytesProcessed,
                TotalBytes = totalBytes,
                FilesProcessed = filesProcessed,
                TotalFiles = totalFiles
            });

            // 앞 회차에서 읽는 도중 막혔던 파일 — 다시 열지 않고 건너뜀
            if (dropped.TryGetValue(sourceFile, out var droppedReason))
            {
                skipped.Add(new SkippedSource(sourceFile, droppedReason));
                bytesProcessed = Math.Min(totalBytes, bytesProcessed + SafeLength(sourceFile));
                continue;
            }

            // 원본을 '먼저' 연다(I0-2): 쓰는 중이어도 공유를 허락했으면 읽고, 아예 막았으면 이 파일만 건너뛴다(항목 머리를 쓰기 전에 — 첫 블록까지 읽어 봄).
            if (!SourceFileOpener.TryOpenRead(sourceFile, out var opened, out var reason))
            {
                skipped.Add(new SkippedSource(sourceFile, reason!));
                bytesProcessed = Math.Min(totalBytes, bytesProcessed + SafeLength(sourceFile));   // 진행률이 건너뛴 만큼 뒤처지지 않게
                continue;
            }

            // TarWriter 는 원본을 한 번에 통째로 복사하므로, 읽기마다 취소를 확인하고 진행률을 올리는
            // 래퍼로 감싼다(B6: 큰 파일을 쓰는 동안 누른 취소가 안 먹히고 마지막 파일이면 '완료'로 끝나던 문제).
            // 원본 경로를 넘겨, 읽는 도중 막히면(뒤쪽 바이트 범위 잠금) SourceUnreadableException → 베이스가 이 파일을 빼고 다시 쓴다.
            // 크기를 미리 넘겨 라이브러리가 길이를 따로 재지 않게 한다.
            long fileStart = bytesProcessed;
            long size;
            using (var input = new CancellableReadStream(opened!, cancellationToken, read =>
                   {
                       progress?.Report(new ArchiveProgress
                       {
                           CurrentFile = entryName,
                           BytesProcessed = Math.Min(totalBytes, fileStart + read),
                           TotalBytes = totalBytes,
                           FilesProcessed = filesProcessed,
                           TotalFiles = totalFiles
                       });
                   }, sourceFile))
            {
                size = input.Length;
                output.Writer.Write(entryName, input, File.GetLastWriteTime(sourceFile), size);

                // TAR 는 머리에 크기를 먼저 적으므로, 쓰는 도중 원본이 줄어들면 압축 파일이 조용히 깨진다 → 오류로 알린다.
                if (input.BytesRead != size)
                    throw new IOException($"'{entryName}' 파일이 압축하는 도중에 바뀌었어요. 다시 해 주세요.");
            }

            bytesProcessed = Math.Min(totalBytes, fileStart + size);
            filesProcessed++;
        }

        // 담은 파일이 하나도 없는데 건너뛴 것이 있다 = 사용자가 고른 것을 하나도 못 담음 → 빈(또는 빈 폴더뿐인) 압축을 만들지 않는다
        if (filesProcessed == 0 && skipped.Count > 0)
            throw new NothingToCompressException(skipped);

        // 빈 폴더 보존: 파일이 하나도 없는 폴더는 위 루프에서 빠지므로 폴더 항목으로 따로 기록한다(ZIP 과 같은 규칙).
        foreach (var (sourceDir, dirEntry) in sources.EmptyDirectories)
        {
            cancellationToken.ThrowIfCancellationRequested();
            DateTime? mtime = null;
            try { mtime = Directory.GetLastWriteTime(sourceDir); }
            catch { /* 권한 등 — 시각 없이 기록 */ }
            output.Writer.WriteDirectory(dirEntry, mtime);
        }

        // 담을 것이 원래 없었으면(예: 원본 폴더 안의 압축 파일 자신뿐) 예전처럼 빈 tar 를 만든다. 닫을 때 끝 표시(0 블록 둘)를 쓴다.
        _ = output.Writer;
        output.Complete();

        progress?.Report(new ArchiveProgress
        {
            CurrentFile = string.Empty,
            BytesProcessed = totalBytes,
            TotalBytes = totalBytes,
            FilesProcessed = filesProcessed,
            TotalFiles = totalFiles
        });
        return new CompressResult(skipped);
    }
}
