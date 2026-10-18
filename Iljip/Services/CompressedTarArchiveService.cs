using System.IO;
using ICSharpCode.SharpZipLib.GZip;
using Iljip.Models;
using SharpCompress.Compressors.BZip2;
using SharpCompress.IO;
using SharpSevenZip;

namespace Iljip.Services;

/// <summary>
/// 압축된 tar(.tgz·.tar.gz·.tbz2·.tbz·.tar.bz2·.txz·.tar.xz, 그리고 안이 tar 인 .gz·.bz2·.xz) — tar 안 내용을 바로 보여 주고 푼다(I2-9, W2-01).
/// 반디집은 .tar.gz 는 바로 펼치지만 .tar.xz·.tbz2 는 안쪽 .tar 하나만 보여 준다 — 일집은 모두 바로 펼친다(반디집보다 나은 의도한 차이).
///
/// 풀기는 '전부 아니면 전무'(1차 W1-B 검증 지적 (c) — 예전엔 앞에서부터 대상에 바로 써서, 잘리거나 확인값이 틀린 파일도 앞 파일들이 남았다):
/// 1. 대상 폴더 안 숨김 임시 폴더(<see cref="StagingFolder"/>)에 바깥 압축을 풀어 <c>.inner.tar</c> 를 만든다(진행률 0~50% = 읽은 압축 바이트).
///    gzip = SharpZipLib GZipInputStream(멤버마다 CRC32·크기, 이어 붙인 멤버도), bzip2 = SharpCompress BZip2Stream(블록 CRC, 이어 붙인 스트림도),
///    xz = 7z.dll(<c>ExtractFile(0)</c>). ★ SharpCompress 0.50.5 의 XZ 해제기는 잘린 xz 에서 끝없이 돌아(취소도 안 먹음 — 1차 검증 지적 (b)) 쓰지 않는다.
/// 2. main 의 TAR 처리기(<see cref="TarArchiveService"/> — 한글 이름 판별·경로 공격 방어·장부)로 임시 폴더 안에 푼다(50~100%).
/// 3. 둘 다 성공해야 대상으로 옮긴다(같은 이름이면 "이름 (1).ext"). 어느 단계든 실패하면 임시 폴더째 지우고 손상으로 알린다 — 대상에는 아무것도 남지 않는다.
/// 목록·선택 풀기: 안쪽 tar 를 일집 임시 폴더(<see cref="AppPaths.ListCacheRoot"/>\&lt;guid&gt;\inner.tar)에 풀어 두고 (경로·길이·수정 시각)으로
/// 이번 실행 동안 다시 쓴다(끝날 때·하루 지나면 지움). 7z.dll 을 못 쓰면 .txz·.tar.xz 는 <see cref="EngineUnavailableException"/>.
/// 안쪽 tar 와 풀린 내용이 함께 있어 디스크를 두 배 쓴다 — 알 수 있으면 미리 남은 공간을 본다.
/// </summary>
public sealed class CompressedTarArchiveService : IArchiveService, IArchiveTestable
{
    public IReadOnlyList<string> SupportedExtensions { get; } = new[] { ".tgz", ".tar.gz", ".tbz2", ".tbz", ".tar.bz2", ".txz", ".tar.xz" };

    private enum Outer { GZip, BZip2, Xz }

    private const string InnerTarName = ".inner.tar";
    private const int ErrorDiskFull = unchecked((int)0x80070070);

    private readonly TarArchiveService _tar = new();

    // ───────────────────────────── 목록 ─────────────────────────────

    public Task<IReadOnlyList<ArchiveEntry>> ListEntriesAsync(
        string archivePath,
        string? password = null,
        CancellationToken cancellationToken = default)
    {
        return Task.Run<IReadOnlyList<ArchiveEntry>>(async () =>
        {
            // 풀기와 같이 캐시보다 먼저 부품을 본다 — 7z.dll 이 막힌 PC 에서 앞서 캐시해 둔 .tar.xz 목록만 보이고 풀기는 안 되는 어긋남 방지
            if (DetectOuter(archivePath) == Outer.Xz) SevenZipLibrary.ThrowIfUnavailable();
            using var lease = ListCache.Acquire(archivePath, tarPath => Decompress(archivePath, tarPath, null, 0, cancellationToken));
            IReadOnlyList<ArchiveEntry> entries;
            try
            {
                entries = await _tar.ListEntriesAsync(lease.TarPath, null, cancellationToken);
            }
            catch (Exception ex) when (ex is not (OperationCanceledException or ArchiveException))
            {
                throw new CorruptArchiveException(ex);
            }
            // 바깥 압축이 통째라 항목별 압축 크기는 없다
            return entries.Select(e => new ArchiveEntry
            {
                Path = e.Path,
                Size = e.Size,
                CompressedSize = 0,
                IsDirectory = e.IsDirectory,
                LastModified = e.LastModified,
            }).ToList();
        }, cancellationToken);
    }

    // ───────────────────────────── 해제 ─────────────────────────────

    public Task ExtractAsync(
        string archivePath,
        string destinationFolder,
        string? password = null,
        IProgress<ArchiveProgress>? progress = null,
        CancellationToken cancellationToken = default)
        => Task.Run(() => Extract(archivePath, destinationFolder, null, progress, cancellationToken), cancellationToken);

    public Task ExtractEntriesAsync(
        string archivePath,
        string destinationFolder,
        IReadOnlyCollection<string> entryPaths,
        string? password = null,
        IProgress<ArchiveProgress>? progress = null,
        CancellationToken cancellationToken = default)
        => Task.Run(() => Extract(archivePath, destinationFolder, entryPaths, progress, cancellationToken), cancellationToken);

    private async Task Extract(
        string archivePath,
        string destinationFolder,
        IReadOnlyCollection<string>? entryPaths,
        IProgress<ArchiveProgress>? progress,
        CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var outer = DetectOuter(archivePath);
        if (outer == Outer.Xz) SevenZipLibrary.ThrowIfUnavailable();
        long inputLength = new FileInfo(archivePath).Length;
        // 대상 폴더(없던 조상까지)를 장부에 적으며 만든다 — 실패·취소로 아무것도 옮기지 못했으면 이번에 만든 빈 폴더를 지운다
        var created = new ExtractionLedger();
        created.EnsureDirectory(Path.GetFullPath(destinationFolder));
        try
        {
            using var cacheLease = ListCache.TryAcquire(archivePath);   // 푸는 동안 캐시가 지워지지 않게 빌린다
            string? cached = cacheLease?.TarPath;
            if (cached is null) EnsureFreeSpace(archivePath, outer, destinationFolder, inputLength);

            int committed;
            bool canceled = false;
            using (var staging = StagingFolder.Create(destinationFolder))
            {
                string inner = cached ?? Path.Combine(staging.FullPath, InnerTarName);
                if (cached is null)
                    Decompress(archivePath, inner, progress, inputLength, ct);

                // 2단계: 안쪽 tar → 임시 폴더 안 out\ (진행률 50~100%)
                string outDir = Path.Combine(staging.FullPath, "out");
                var tarProgress = progress is null ? null : new MappedProgress(progress, inputLength);
                try
                {
                    if (entryPaths is null) await _tar.ExtractAsync(inner, outDir, null, tarProgress, ct);
                    else await _tar.ExtractEntriesAsync(inner, outDir, entryPaths, null, tarProgress, ct);
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    // 취소: 안쪽 tar 는 1단계에서 확인값까지 맞았다 → 끝까지 풀린 파일만 옮긴다(쓰던 파일은 TAR 처리기가 지움 — 다른 형식의 취소 계약과 같음)
                    canceled = true;
                }
                catch (Exception ex) when (IsArchiveSideFailure(ex))
                {
                    throw ex is ArchiveException ? ex : new CorruptArchiveException(ex);
                }
                canceled |= ct.IsCancellationRequested;

                // 3단계: 다 맞았을 때만 대상으로 옮긴다(실패하면 옮긴 것을 되돌림 — StagingFolder.Commit)
                var files = new List<(string From, string TargetRel)>();
                var dirs = new List<string>();
                if (Directory.Exists(outDir))
                {
                    if (!canceled)
                        foreach (var d in Directory.EnumerateDirectories(outDir, "*", SearchOption.AllDirectories))
                            dirs.Add(Path.GetRelativePath(outDir, d));
                    foreach (var f in Directory.EnumerateFiles(outDir, "*", SearchOption.AllDirectories))
                        files.Add((f, Path.GetRelativePath(outDir, f)));
                }
                StagingFolder.Commit(destinationFolder, files, dirs);
                committed = files.Count;
            }
            if (canceled) throw new OperationCanceledException(ct);
            progress?.Report(new ArchiveProgress
            {
                CurrentFile = string.Empty,
                BytesProcessed = inputLength * 2,
                TotalBytes = inputLength * 2,
                FilesProcessed = committed,
                TotalFiles = committed,
            });
        }
        catch
        {
            // 임시 폴더는 위 using 이 이미 지웠다. 장부의 폴더는 비어 있을 때만 지워진다(옮긴 파일·원래 있던 것은 그대로).
            created.Rollback();
            throw;
        }
    }

    /// <summary>
    /// 압축 검사(W2-08):
    /// <list type="bullet">
    /// <item>목록 보기로 이미 풀어 둔 안쪽 tar(일집 임시 폴더의 캐시)가 있으면 그것을 TAR 처리기로 검사한다(다시 풀지 않음).</item>
    /// <item>없으면 gzip·bzip2 는 디스크에 쓰지 않고 바깥 압축을 흘려 풀며 안쪽 tar 를 앞에서부터 읽는다(<see cref="TestStreaming"/> — W2-08 검증 지적:
    ///   예전엔 안쪽 tar 전체를 임시 폴더에 풀어 큰 tgz 는 C: 에 그만큼 자리가 필요했고 모자라면 '공간 부족'으로 끝났다).</item>
    /// <item>xz 는 7z.dll 이 스트림으로 내주지 않아 목록과 같은 임시 파일(일집 임시 폴더 — 압축 파일 옆이 아님)로 풀어 검사한다.</item>
    /// </list>
    /// 바깥이 깨졌으면 전체 문제.
    /// </summary>
    TestReport IArchiveTestable.TestArchive(string archivePath, string? password, IProgress<ArchiveProgress>? progress, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var outer = DetectOuter(archivePath);
        if (outer == Outer.Xz) SevenZipLibrary.ThrowIfUnavailable();
        long inputLength = new FileInfo(archivePath).Length;
        // 쓰는 동안 캐시가 지워지지 않게 빌린다(W2-08 검증 지적 2차 — 같은 .tar.xz 를 동시에 검사하면 다른 검사가 지운 폴더를 읽음)
        using var lease = ListCache.TryAcquire(archivePath)
            ?? (outer == Outer.Xz ? ListCache.Acquire(archivePath, tarPath => Decompress(archivePath, tarPath, progress, inputLength, cancellationToken)) : null);
        if (lease is null) return TestStreaming(archivePath, outer, progress, inputLength, cancellationToken);
        var tarProgress = progress is null ? null : new MappedProgress(progress, inputLength);
        try
        {
            return ((IArchiveTestable)_tar).TestArchive(lease.TarPath, null, tarProgress, cancellationToken);
        }
        catch (Exception ex) when (IsArchiveSideFailure(ex))
        {
            throw ex is ArchiveException ? ex : new CorruptArchiveException(ex);
        }
    }

    /// <summary>
    /// 디스크에 쓰지 않는 검사(gzip·bzip2): 바깥 압축을 흘려 풀며(SharpZipLib GZipInputStream — 멤버마다 CRC32·크기, SharpCompress BZip2Stream — 블록 CRC)
    /// 안쪽 tar 를 앞에서부터 읽어(SharpCompress TarReader — 목록과 같은 한글 이름 판별) 항목마다 끝까지 읽고 크기를 맞춰 본다. tar 끝 뒤에 남은 것까지 끝까지 풀어
    /// 바깥 압축의 마지막 확인값도 본다. 흘려 읽는 것이라 한 군데가 깨지면 뒤는 읽을 수 없어 전체 문제 하나로 알린다(앞서 확인한 파일 수는 남김).
    /// 진행률은 읽은 압축 바이트.
    /// </summary>
    private static TestReport TestStreaming(string archivePath, Outer outer, IProgress<ArchiveProgress>? progress, long inputLength, CancellationToken ct)
    {
        int checkedFiles = 0;
        long readCompressed = 0;
        string? current = null;
        void Report(string file) => progress?.Report(new ArchiveProgress
        {
            CurrentFile = file,
            BytesProcessed = Math.Min(inputLength, readCompressed),
            TotalBytes = inputLength,
            FilesProcessed = checkedFiles,
            TotalFiles = 0,   // 흘려 읽어서 전체 파일 수는 끝까지 읽어야 안다
        });

        try
        {
            using var file = new FileStream(archivePath, FileMode.Open, FileAccess.Read, FileShare.Read, 81920);
            using var input = new CancellableReadStream(file, ct, read => readCompressed = read);
            using Stream dec = outer == Outer.GZip
                ? new GZipInputStream(input) { IsStreamOwner = false }
                : BZip2Stream.Create(input, SharpCompress.Compressors.CompressionMode.Decompress, decompressConcatenated: true);
            var buffer = new byte[81920];
            // TarReader 는 형식을 알아보려고 앞부분을 되감는다 — 흘려 읽는 스트림이라 SharpCompress 의 되감기 버퍼에 담아 넘기고(ReaderFactory 와 같은 순서) 알아본 뒤엔 담기를 멈춘다
            using var rewindable = SharpCompressStream.Create(dec, Math.Max(SharpCompress.Common.Constants.RewindableBufferSize, SharpCompress.Factories.TarWrapper.MaximumRewindBufferSize));
            rewindable.StartRecording();
            using (var reader = SharpCompress.Readers.Tar.TarReader.OpenReader(rewindable, SharpCompressArchiveServiceBase.BuildReaderOptions(null) with { LeaveStreamOpen = true }))
            {
                rewindable.Rewind(stopRecording: true);
                while (reader.MoveToNextEntry())
                {
                    ct.ThrowIfCancellationRequested();
                    var entry = reader.Entry;
                    if (entry.IsDirectory) continue;
                    current = (entry.Key ?? string.Empty).Replace('\\', '/');
                    Report(current);
                    long got = 0;
                    using (var data = reader.OpenEntryStream())
                    {
                        int n;
                        while ((n = data.Read(buffer, 0, buffer.Length)) > 0)
                        {
                            ct.ThrowIfCancellationRequested();
                            got += n;
                            Report(current);
                        }
                    }
                    if (got != entry.Size)
                        throw CorruptArchiveException.Truncated(new EndOfStreamException($"TAR 항목이 짧음: {got:N0} / {entry.Size:N0}"), current);
                    checkedFiles++;
                    current = null;
                }
            }
            // tar 끝(0 블록) 뒤에 남은 것까지 풀어 바깥 압축의 마지막 확인값(gzip 의 CRC·크기, bzip2 의 전체 CRC)을 본다
            while (rewindable.Read(buffer, 0, buffer.Length) > 0) ct.ThrowIfCancellationRequested();
        }
        catch (Exception ex) when (ex is not OperationCanceledException && !ct.IsCancellationRequested && !ErrorCatalog.IsEnvironmentProblem(ex))
        {
            Logger.LogError($"압축 검사 — 압축된 TAR 문제: {archivePath} / {current}", ex);
            var problem = ex is ArchiveException a ? a
                : IsTruncation(ex) || outer == Outer.BZip2 && !Bzip2Complete(archivePath) ? CorruptArchiveException.Truncated(ex) : new CorruptArchiveException(ex);
            return TestReport.ForArchive(problem, checkedFiles);
        }
        catch (Exception) when (ct.IsCancellationRequested)
        {
            throw new OperationCanceledException(ct);
        }
        progress?.Report(new ArchiveProgress { CurrentFile = string.Empty, BytesProcessed = inputLength, TotalBytes = inputLength, FilesProcessed = checkedFiles, TotalFiles = checkedFiles });
        return new TestReport { CheckedCount = checkedFiles };
    }

    /// <summary>
    /// 안쪽 tar 를 풀다 난 예외 중 '압축 파일 쪽' 문제(손상)인가. 취소·경로 공격('안전하지 않은 경로: …' — 기존 계약)·
    /// 대상 쪽 입출력 오류(디스크 가득·권한)는 그대로 둔다.
    /// </summary>
    private static bool IsArchiveSideFailure(Exception ex)
    {
        if (ex is OperationCanceledException) return false;
        if (ex is InvalidDataException ide && ide.Message.StartsWith(ErrorMessages.UnsafePathPrefix, StringComparison.Ordinal)) return false;
        if (ex is ArchiveException or EndOfStreamException) return true;
        return ex is not (IOException or UnauthorizedAccessException);
    }

    // ───────────────────────────── 바깥 압축 ─────────────────────────────

    /// <summary>바깥 압축 종류: 서명으로, 모르면 확장자로. 둘 다 아니면 손상.</summary>
    private static Outer DetectOuter(string path)
    {
        var head = new byte[6];
        int n;
        using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            n = fs.Read(head, 0, head.Length);
        if (n >= 3 && head[0] == 0x1F && head[1] == 0x8B) return Outer.GZip;
        if (n >= 3 && head[0] == (byte)'B' && head[1] == (byte)'Z' && head[2] == (byte)'h') return Outer.BZip2;
        if (n >= 6 && head[0] == 0xFD && head[1] == 0x37 && head[2] == 0x7A && head[3] == 0x58 && head[4] == 0x5A && head[5] == 0x00) return Outer.Xz;
        if (n == 0) throw new CorruptArchiveException();
        string name = Path.GetFileName(path).ToLowerInvariant();
        // 서명이 없으면 그 형식의 손상 — 7z.dll 이 없을 때 .txz 가 '부품 없음' 대신 손상으로 나오지 않게 xz 이름이면 먼저 부품을 본다
        if (name.EndsWith(".txz") || name.EndsWith(".xz")) SevenZipLibrary.ThrowIfUnavailable();
        throw new CorruptArchiveException();
    }

    /// <summary>
    /// 바깥 압축을 <paramref name="tarPath"/> 로 끝까지 푼다(확인값 검사 포함). 실패하면 쓰다 만 파일을 지우고 손상(잘림이면 잘림)으로 알린다.
    /// 진행률은 읽은 압축 바이트(0~50% — 총량은 압축 크기 × 2).
    /// </summary>
    private static void Decompress(string archivePath, string tarPath, IProgress<ArchiveProgress>? progress, long inputLength, CancellationToken ct)
    {
        var outer = DetectOuter(archivePath);
        if (outer == Outer.Xz)
        {
            SevenZipLibrary.ThrowIfUnavailable();
            // 끝이 잘린 xz 는 7z.dll 을 부르기 전에 꼬리로 알아본다(목록에서도 '잘림'으로)
            using (var check = new FileStream(archivePath, FileMode.Open, FileAccess.Read, FileShare.Read))
                if (ArchiveCompleteness.Check(check, StructureKind.Xz) is { } truncated) throw truncated;
        }
        string? dir = Path.GetDirectoryName(tarPath);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        void Report(long pos)
        {
            progress?.Report(new ArchiveProgress
            {
                CurrentFile = Path.GetFileName(archivePath),
                BytesProcessed = Math.Min(inputLength, pos),
                TotalBytes = inputLength * 2,
                FilesProcessed = 0,
                TotalFiles = 0,   // 총 파일 수는 tar 를 풀 때 안다
            });
        }

        bool outputFailed = false;
        try
        {
            using var output = new FileStream(tarPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920);
            var guardedOutput = new CancellableWriteStream(output, ct);
            try
            {
                if (outer == Outer.Xz)
                {
                    using var input = new CancellableSeekableReadStream(
                        new FileStream(archivePath, FileMode.Open, FileAccess.Read, FileShare.Read), ct, (p, _) => Report(p));
                    using var extractor = new SharpSevenZipExtractor(input, leaveOpen: true, InArchiveFormat.XZ);
                    extractor.ExtractFile(0, guardedOutput);
                }
                else
                {
                    using var file = new FileStream(archivePath, FileMode.Open, FileAccess.Read, FileShare.Read, 81920);
                    using var input = new CancellableReadStream(file, ct, read => Report(read));
                    using Stream dec = outer == Outer.GZip
                        ? new GZipInputStream(input) { IsStreamOwner = false }   // 이어 붙인 멤버도 끝까지, 정상 멤버 뒤의 0 채움은 무시(GZip 처리기와 같음)
                        : BZip2Stream.Create(input, SharpCompress.Compressors.CompressionMode.Decompress, decompressConcatenated: true);
                    var buffer = new byte[81920];
                    int n;
                    while ((n = dec.Read(buffer, 0, buffer.Length)) > 0)
                        guardedOutput.Write(buffer, 0, n);
                }
            }
            finally
            {
                outputFailed = guardedOutput.WriteFailed;
            }
        }
        catch (Exception ex)
        {
            try { File.Delete(tarPath); } catch { /* 임시 폴더째 지워짐 */ }
            if (ex is OperationCanceledException || ct.IsCancellationRequested) throw new OperationCanceledException(ct);
            if (ex is ArchiveException) throw;
            // 임시 폴더 쪽 쓰기 실패(디스크 가득·권한)는 손상이 아니다
            if (outputFailed && ex is IOException or UnauthorizedAccessException) throw;
            if (ex is SharpSevenZip.Exceptions.SharpSevenZipLibraryException) throw new EngineUnavailableException(ex);
            throw IsTruncation(ex) || outer == Outer.BZip2 && !Bzip2Complete(archivePath)
                ? CorruptArchiveException.Truncated(ex)
                : new CorruptArchiveException(ex);
        }
        Report(inputLength);
    }

    /// <summary>
    /// bzip2 가 끝 표시로 끝나는가(<see cref="ArchiveCompleteness.Bzip2HasEndMarker"/>) — 해제기의 오류 문구만으로는 잘림과 손상이 안 갈려
    /// (앞부분에서 자르면 'CRC'·'블록' 오류로 나옴) 파일 끝을 직접 본다. 못 읽으면 판단하지 않는다(true).
    /// </summary>
    private static bool Bzip2Complete(string archivePath)
    {
        try
        {
            using var fs = new FileStream(archivePath, FileMode.Open, FileAccess.Read, FileShare.Read);
            return ArchiveCompleteness.Bzip2HasEndMarker(fs);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return true;
        }
    }

    /// <summary>'갑자기 끝남'(바깥 해제기의 EOF·7z.dll 의 끝 잘림)인지 — 잘림 문구로 알린다.</summary>
    private static bool IsTruncation(Exception ex)
    {
        for (Exception? e = ex; e is not null; e = e.InnerException)
        {
            if (e is EndOfStreamException or SharpCompress.Common.IncompleteArchiveException) return true;
            string m = e.Message ?? string.Empty;
            if (m.Contains("EOF", StringComparison.OrdinalIgnoreCase) || m.Contains("end of stream", StringComparison.OrdinalIgnoreCase)
                || m.Contains("Unexpected end", StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    /// <summary>
    /// 남은 공간 확인(알 수 있을 때만): 안쪽 tar 크기 × 2(임시 폴더의 tar + 풀린 내용)보다 대상 드라이브의 남은 공간이 적으면 미리 알린다.
    /// gzip 은 끝 4바이트(원래 크기 % 4GB — 그보다 크면 작게 나와 막지 않음), 나머지는 압축 크기로 어림한다.
    /// </summary>
    private static void EnsureFreeSpace(string archivePath, Outer outer, string destinationFolder, long inputLength)
    {
        try
        {
            long estimate = inputLength;
            if (outer == Outer.GZip && inputLength >= 18)
            {
                using var fs = new FileStream(archivePath, FileMode.Open, FileAccess.Read, FileShare.Read);
                fs.Position = inputLength - 4;
                var b = new byte[4];
                if (fs.Read(b, 0, 4) == 4) estimate = Math.Max(estimate, BitConverter.ToUInt32(b, 0));
            }
            string? root = Path.GetPathRoot(Path.GetFullPath(destinationFolder));
            if (string.IsNullOrEmpty(root)) return;
            var drive = new DriveInfo(root);
            if (!drive.IsReady) return;
            if (drive.AvailableFreeSpace < estimate * 2)
                throw new IOException($"저장할 공간이 부족해요. 풀려면 {FormatBytes(estimate * 2)} 정도가 필요해요.") { HResult = ErrorDiskFull };
        }
        catch (IOException ex) when (ex.HResult == ErrorDiskFull)
        {
            throw;
        }
        catch
        {
            // 확인은 덤 — 못 하면 그냥 푼다(모자라면 쓰다가 '디스크 공간이 모자라요')
        }
    }

    private static string FormatBytes(long bytes) =>
        bytes >= 1L << 30 ? $"{bytes / (double)(1L << 30):F1}GB" : bytes >= 1L << 20 ? $"{bytes / (double)(1L << 20):F0}MB" : $"{bytes / 1024.0:F0}KB";

    /// <summary>
    /// 안쪽 tar 해제 진행률(바이트·파일 수)을 전체 진행률의 50~100% 로 옮긴다(총량 = 압축 크기 × 2).
    /// TAR 처리기의 '완료' 보고(현재 파일 이름 없음)는 넘기지 않는다 — 옮기기까지 끝난 뒤 이 처리기가 완료를 한 번만 알린다(진행률 계약).
    /// </summary>
    private sealed class MappedProgress(IProgress<ArchiveProgress> target, long inputLength) : IProgress<ArchiveProgress>
    {
        public void Report(ArchiveProgress p)
        {
            if (string.IsNullOrEmpty(p.CurrentFile)) return;
            double frac = p.TotalBytes > 0 ? Math.Min(1.0, (double)p.BytesProcessed / p.TotalBytes) : 0;
            target.Report(new ArchiveProgress
            {
                CurrentFile = p.CurrentFile,
                BytesProcessed = inputLength + (long)(Math.Max(0, inputLength - 1) * frac),
                TotalBytes = inputLength * 2,
                FilesProcessed = p.FilesProcessed,
                TotalFiles = p.TotalFiles,
            });
        }
    }

    // ───────────────────────────── 목록 캐시 ─────────────────────────────

    /// <summary>
    /// 목록·선택 풀기용 안쪽 tar 캐시(설계 §3 — <c>%LOCALAPPDATA%\Iljip\temp\list\&lt;guid&gt;\inner.tar</c>).
    /// 같은 압축(전체 경로·길이·수정 시각)이면 이번 실행 동안 다시 쓴다. 최근 것 몇 개만 두고, 프로그램이 끝날 때 이번 실행 것을 지우며,
    /// 하루 지난 것(비정상 종료 흔적)은 처음 쓸 때 지운다.
    /// 쓰는 동안은 빌려 간다(<see cref="Lease"/> — W2-08 검증 지적 2차: 같은 .tar.xz 를 동시에 검사하면 열쇠마다 캐시가 여럿 생기고, 개수 한도를 넘어
    /// 가장 오래된 폴더를 지울 때 그것을 막 받은 다른 검사가 '찾을 수 없어요: …\inner.tar' 로 끝났다). 같은 열쇠는 한 번만 만들고(기다린 호출은 그것을 씀),
    /// 빌려 간 것은 지우지 않는다(돌려받을 때 한도를 넘었으면 그때 지움).
    /// </summary>
    private static class ListCache
    {
        private const int MaxEntries = 3;
        private static readonly object Gate = new();
        private static readonly List<Entry> Entries = new();                    // 오래된 것부터
        private static readonly Dictionary<string, object> Creating = new();    // 열쇠마다 만들기 자물쇠
        private static bool _hooked;

        private sealed class Entry
        {
            public required string Key { get; init; }
            public required string Folder { get; init; }
            public required string TarPath { get; init; }
            public int Users { get; set; }
        }

        /// <summary>빌려 쓰는 캐시 하나(다 쓰면 Dispose — 그동안은 지워지지 않는다).</summary>
        public sealed class Lease : IDisposable
        {
            private Entry? _entry;

            internal Lease(object entry)
            {
                _entry = (Entry)entry;
                TarPath = _entry.TarPath;
            }

            public string TarPath { get; }

            public void Dispose()
            {
                var e = Interlocked.Exchange(ref _entry, null);
                if (e is null) return;
                lock (Gate)
                {
                    e.Users--;
                    EvictOverflow();
                }
            }
        }

        private static string KeyOf(string archivePath)
        {
            var fi = new FileInfo(archivePath);
            return $"{Path.GetFullPath(archivePath).ToUpperInvariant()}|{fi.Length}|{fi.LastWriteTimeUtc.Ticks}";
        }

        /// <summary>이미 만든 캐시가 있으면 빌린다(없으면 null).</summary>
        public static Lease? TryAcquire(string archivePath)
        {
            string key;
            try
            {
                key = KeyOf(archivePath);
            }
            catch
            {
                return null;
            }
            lock (Gate) return TryAcquireLocked(key);
        }

        private static Lease? TryAcquireLocked(string key)
        {
            var hit = Entries.FirstOrDefault(e => e.Key == key);
            if (hit is null || !File.Exists(hit.TarPath)) return null;
            hit.Users++;
            Entries.Remove(hit);   // 최근에 쓴 것으로(뒤로)
            Entries.Add(hit);
            return new Lease(hit);
        }

        /// <summary>캐시를 빌린다 — 없으면 만든다(같은 열쇠를 동시에 만들지 않음: 나중 호출은 먼저 호출이 다 만들 때까지 기다렸다 그것을 빌림).</summary>
        public static Lease Acquire(string archivePath, Action<string> create)
        {
            if (TryAcquire(archivePath) is { } existing) return existing;
            string key = KeyOf(archivePath);
            object keyLock;
            lock (Gate)
            {
                if (!Creating.TryGetValue(key, out keyLock!)) Creating[key] = keyLock = new object();
            }
            lock (keyLock)
            {
                lock (Gate)
                {
                    if (TryAcquireLocked(key) is { } made) return made;   // 기다리는 동안 다른 호출이 만들었다
                }
                string root = AppPaths.ListCacheRoot;
                CleanupStale(root);
                string folder = Path.Combine(root, Guid.NewGuid().ToString("N"));
                string tar = Path.Combine(folder, "inner.tar");
                Directory.CreateDirectory(folder);
                try
                {
                    create(tar);
                }
                catch
                {
                    StagingFolder.Delete(folder, attempts: 2);
                    lock (Gate) Creating.Remove(key);
                    throw;
                }
                lock (Gate)
                {
                    HookExit();
                    var entry = new Entry { Key = key, Folder = folder, TarPath = tar, Users = 1 };
                    Entries.Add(entry);
                    Creating.Remove(key);
                    EvictOverflow();
                    return new Lease(entry);
                }
            }
        }

        /// <summary>한도를 넘으면 빌려 가지 않은 것 중 오래된 것부터 지운다(Gate 안에서).</summary>
        private static void EvictOverflow()
        {
            while (Entries.Count > MaxEntries)
            {
                var victim = Entries.FirstOrDefault(e => e.Users <= 0);
                if (victim is null) return;   // 모두 쓰는 중 — 돌려받을 때 다시 본다
                Entries.Remove(victim);
                StagingFolder.Delete(victim.Folder, attempts: 1);
            }
        }

        private static void HookExit()
        {
            if (_hooked) return;
            _hooked = true;
            AppDomain.CurrentDomain.ProcessExit += (_, _) =>
            {
                lock (Gate)
                {
                    foreach (var e in Entries) StagingFolder.Delete(e.Folder, attempts: 1);
                    Entries.Clear();
                }
            };
        }

        private static void CleanupStale(string root)
        {
            try
            {
                if (!Directory.Exists(root)) return;
                HashSet<string> live;
                lock (Gate) live = Entries.Select(e => Path.GetFullPath(e.Folder)).ToHashSet(StringComparer.OrdinalIgnoreCase);
                foreach (var d in Directory.EnumerateDirectories(root))
                {
                    if (live.Contains(Path.GetFullPath(d))) continue;   // 이번 실행이 쓰는 것
                    if (Directory.GetCreationTime(d) < DateTime.Now.AddDays(-1))
                        StagingFolder.Delete(d, attempts: 1);
                }
            }
            catch
            {
                // 정리는 덤
            }
        }
    }

    public Task<CompressResult> CompressAsync(
        IEnumerable<string> sourcePaths,
        string archivePath,
        CompressionOptions? options = null,
        IProgress<ArchiveProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException(
            "압축된 TAR(.tar.gz 등) 만들기는 아직 지원하지 않아요. (풀기만 가능)\n" +
            "압축은 ZIP이나 TAR을 사용해주세요.");
    }
}
