using System.IO;
using Iljip.Models;
using SharpCompress.Archives;
using SharpCompress.Readers;
using SharpSevenZip;
using SharpSevenZip.Exceptions;

namespace Iljip.Services;

/// <summary>
/// 7Z 포맷.
/// W2-01: 목록·해제는 7z.dll(<see cref="SevenZipEngine"/>). 파일 이름까지 암호가 걸린(헤더 암호) 7z 는 열기 전에 머리를 읽어 알아내
/// 암호를 묻게 하고(I1-7·I0-1 — 우클릭 '여기에 풀기'도 같은 길), 틀린 암호를 '암호 문제'로 알린다(B5).
/// '.7z.001' 숫자 분할은 조각을 이어 붙여 연다(<see cref="SplitVolumeStream"/> — 빠진 조각·덜 받아진 조각을 이름으로 알림, I2-2 해제).
/// 7z.dll 을 못 쓰는 PC 에서는 SharpCompress 로 대신 연다(아래 — main 의 구현 그대로).
/// W2-02: 7z 만들기(<see cref="CompressAsync"/> — 수준·솔리드·AES-256·파일 이름 암호화, 7z.dll 로만). 목록까지 잠긴 7z 를 암호 없이 열면
/// <see cref="ListPasswordRequiredException"/>.
/// W2-03: 나눠 만들기(<see cref="CompressionOptions.SplitBytes"/>) — 7z 하나를 임시 폴더에 다 만든 뒤 숫자 조각(이름.7z.001 …)으로 자른다
/// (<see cref="SplitArchiveOutput"/>·<see cref="VolumeSplitter"/>).
/// SharpCompress 대체 경로: 7z 는 암호 확인값이 없어 틀린 암호가 'Data Error' 로만 드러난다 — 베이스가 암호 항목의 데이터 오류를 틀린 암호로 바꾸고(B5),
/// 여기서는 헤더 암호 7z 를 열 때의 오류를 암호 예외로 바꾼다.
/// </summary>
public sealed class SevenZipArchiveService : SevenZipEngineServiceBase
{
    public override IReadOnlyList<string> SupportedExtensions { get; } = new[] { ".7z", ".7z.001" };

    internal override SevenZipEngine.Source? CreateSource(string archivePath)
    {
        if (!SevenZipLibrary.IsAvailable) return null;
        Func<Stream> open = SplitVolumeStream.IsNumberedFirstVolume(archivePath)
            ? () => OpenSplit(archivePath)
            : () => OpenRead(archivePath);
        bool headerEncrypted;
        using (var probe = open())
            headerEncrypted = ArchiveHeaderProbe.IsSevenZipHeaderEncrypted(probe);
        return new SevenZipEngine.Source
        {
            ArchivePath = archivePath,
            Format = InArchiveFormat.SevenZip,
            OpenStream = open,
            HeaderEncrypted = headerEncrypted,
            // 7z 는 잘린 모든 지점에서 7z.dll 열기가 실패한다(실측 179/179) — 따로 구조 검사를 하지 않는다
            Structure = StructureKind.None,
        };
    }

    /// <summary>숫자 분할 조각을 이어 붙이고, 뒤 조각이 빠졌거나 덜 받아졌으면 그 이름으로 알린다.</summary>
    private static SplitVolumeStream OpenSplit(string firstVolume)
    {
        var s = SplitVolumeStream.Open(firstVolume);
        try
        {
            s.ThrowIfSevenZipIncomplete();
            return s;
        }
        catch
        {
            s.Dispose();
            throw;
        }
    }

    /// <summary>7z.dll 을 못 쓸 때(SharpCompress): 숫자 분할도 이어 붙인 스트림으로 연다.</summary>
    protected override IArchive OpenForRead(string archivePath, ReaderOptions options)
    {
        if (!SplitVolumeStream.IsNumberedFirstVolume(archivePath)) return base.OpenForRead(archivePath, options);
        var s = OpenSplit(archivePath);
        try
        {
            return SharpCompress.Archives.SevenZip.SevenZipArchive.OpenArchive(s, options with { LeaveStreamOpen = false });
        }
        catch
        {
            s.Dispose();
            throw;
        }
    }

    /// <summary>
    /// (7z.dll 을 못 쓸 때) 열기·목록 단계(항목 없음)에서 암호 오류·데이터 오류가 나면 헤더 암호인지 확인해(암호 없이 다시 열어 보면 '암호 없음'이 남)
    /// 헤더 암호면 암호 없음 → <see cref="ListPasswordRequiredException"/>(W2-02), 틀린 암호 → <see cref="WrongPasswordException"/>(손상과 구분 안 됨 문구).
    /// </summary>
    protected override Exception? TranslateError(Exception ex, string archivePath, string? password, IArchiveEntry? entry, string? entryPath)
    {
        if (entry is null && ex is not ArchiveException and not OperationCanceledException
            && (ex is SharpCompress.Common.CryptographicException || EngineErrorTranslator.IsDataError(ex))
            && NeedsPasswordToList(archivePath))   // 암호 없이 머리를 읽으면 'Encrypted 7Zip archive has no password specified'
        {
            return string.IsNullOrEmpty(password)
                ? new ListPasswordRequiredException(ex)
                : new WrongPasswordException(isHeaderEncrypted: true, inner: ex);
        }
        return base.TranslateError(ex, archivePath, password, entry, entryPath);
    }

    // ───────────────────────────── 만들기(W2-02, I2-1) ─────────────────────────────

    /// <summary>압축을 다 읽은 뒤 7z.dll 이 마지막 묶음을 쓰고 목록을 적는 동안의 진행 창 문구.</summary>
    internal const string StageFinishing = "마무리하는 중";

    /// <summary>폴더 항목(빈 폴더 포함)을 넣는 동안의 진행 창 문구(파일보다 먼저 — 금방 끝남).</summary>
    internal const string StageFolders = "폴더 넣는 중";

    /// <summary>첫 원본을 읽기 전(7z.dll 준비)의 진행 창 문구.</summary>
    internal const string StageStarting = "압축하는 중";

    /// <summary>임시 파일 이름 머리 — 대상 옆 <c>.~이름.8글자.tmp</c>(설계 §3).</summary>
    internal const string TempPrefix = ".~";

    /// <summary>
    /// 7z 로 압축한다(I2-1, W2-02 — 1차 검증 지적: 진행 막대가 몇 초 만에 100%·취소가 끝날 때까지 안 먹음·메모리 1.7~3.4GB).
    /// <list type="bullet">
    /// <item>원본 모으기는 ZIP·TAR 와 같은 <see cref="SourceCollector"/>(못 들어간 폴더·없어진 원본은 건너뜀으로 적음, I0-2). 만들 압축 파일 자신은 담지 않는다.
    ///   열 수 없는 원본(다른 프로그램이 공유를 막음)은 미리 열어 봐서 건너뛴다(7z.dll 은 하나라도 못 읽으면 통째로 실패). 하나도 못 담으면
    ///   <see cref="NothingToCompressException"/>.</item>
    /// <item>파일은 <c>CompressStreamDictionary</c> + <see cref="LazySourceStream"/>: 진행률은 7z.dll 이 실제로 읽은 바이트, 취소는 읽기·쓰기마다
    ///   (큰 파일 한가운데서도 1초 남짓), 원본 핸들은 읽는 동안만. 읽는 도중 막히거나 크기가 바뀐 파일은 빼고 처음부터 다시 쓴다(건너뜀으로 안내).</item>
    /// <item>메모리·진행·취소를 위해 사전 크기·블록 크기·스레드 수를 줄인다(<see cref="EngineParameters"/> — 이 PC 실측 보통 254MB).</item>
    /// <item>폴더(빈 폴더와 파일이 든 폴더 모두 — 7-Zip·반디집 7z 와 같은 목록)는 이름표 방식이 받지 못해(SharpSevenZip 2.0.128 — 폴더 항목이면
    ///   NullReferenceException, 실측) 폴더만 든 작은 7z 를 경로 방식으로 먼저 만들고 파일을 덧붙인다(같은 암호·파일 이름 암호화 —
    ///   옮겨 담을 옛 데이터가 없어 큰 압축을 두 번 쓰지 않음). 파일만 골랐으면 덧붙이지 않고 바로 쓴다.</item>
    /// <item>쓰기는 대상 옆 임시 파일(<c>.~이름.8글자.tmp</c>)에 하고, 다 맞게 끝났을 때만 그 이름으로 바꿔 넣는다 — 실패·취소면 임시 파일을 지워
    ///   반쪽 파일을 남기지 않고, 덮어쓰려던 기존 파일도 그대로다(스태시의 '쓰다 만 압축 파일은 지우지 않는다'를 뒤집음).
    ///   덧붙이기 결과는 SharpSevenZip 이 대상 폴더 안 숨김 임시 폴더에 쓴 뒤 임시 파일 자리로 옮긴다(끝나면 그 폴더도 지움).</item>
    /// </list>
    /// 7z.dll 을 못 쓰면 <see cref="EngineUnavailableException"/>(SharpCompress 는 7z 를 쓰지 못함).
    /// </summary>
    public override Task<CompressResult> CompressAsync(
        IEnumerable<string> sourcePaths,
        string archivePath,
        CompressionOptions? options = null,
        IProgress<ArchiveProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        options ??= CompressionOptions.Default;
        var sources = sourcePaths as IList<string> ?? sourcePaths.ToList();
        return Task.Run(() =>
        {
            SevenZipLibrary.ThrowIfUnavailable();
            // 빼기 목록(W2-07, I3-6)에 걸리는 것은 여기서 빠지고 결과의 ExcludedPaths 로 알린다
            var collected = SourceCollector.Collect(sources, options.ExcludeFilter, cancellationToken);

            // 나눠 만들기(W2-03): 대상 폴더 안 숨김 임시 폴더에 7z 하나를 다 만든 뒤 숫자 조각(.7z.001 …)으로 자른다
            // (7z 볼륨은 단순 절단이라 같은 바이트 — 7z.dll 볼륨 쓰기는 빈 폴더 덧붙이기와 같이 못 씀). 원본을 다 모은 뒤에 임시 폴더를 만든다.
            if (options.SplitBytes is > 0 and long split)
                return SplitArchiveOutput.Write(archivePath, split, SplitArchiveOutput.VolumeKind.SevenZip, progress, cancellationToken,
                    (single, p) => WriteWithRetries(collected, single, options, new CompressProgress(p), cancellationToken, alsoSkip: archivePath))
                    .WithExcluded(collected.Excluded);

            return WriteWithRetries(collected, archivePath, options, new CompressProgress(progress), cancellationToken)
                .WithExcluded(collected.Excluded);
        }, cancellationToken);
    }

    /// <summary>
    /// 읽는 도중 막힌(또는 크기가 바뀐) 원본은 그 파일을 빼고 처음부터 다시 쓴다(ZIP·TAR 의 RewriteWithoutUnreadable 과 같은 규칙).
    /// 파일마다 한 번뿐이라 끝이 있다. 임시 파일에 쓰므로 대상(덮어쓰려던 기존 파일)은 회차 사이에도 그대로다.
    /// </summary>
    private static CompressResult WriteWithRetries(
        SourceSet collected, string archivePath, CompressionOptions options, CompressProgress tracker, CancellationToken ct, string? alsoSkip = null)
    {
        var dropped = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        while (true)
        {
            try
            {
                return WriteOnce(collected, dropped, archivePath, options, tracker, ct, alsoSkip);
            }
            catch (SourceUnreadableException ex) when (!dropped.ContainsKey(ex.SourcePath))
            {
                dropped[ex.SourcePath] = ex.Reason;
            }
        }
    }

    /// <summary>담을 원본 파일 하나(7z 안 이름은 '\' 구분 — 7-Zip·반디집이 만든 7z 와 같은 모양).</summary>
    private sealed record SourceItem(string Path, string EntryName, long Length, DateTime? Created, DateTime? Written, DateTime? Accessed);

    /// <summary>
    /// 7z 를 한 번 쓴다(임시 파일 → 다 맞으면 바꿔 넣기). <paramref name="dropped"/>(앞 회차에서 읽는 도중 막힌 파일)는 열지 않고 건너뜀으로 적는다.
    /// </summary>
    /// <param name="alsoSkip">이것도 담지 않는다(나눠 만들 때 저장 창에서 고른 이름 — 실제로 쓰는 곳은 임시 폴더, W2-03).</param>
    private static CompressResult WriteOnce(
        SourceSet collected, IReadOnlyDictionary<string, string> dropped, string archivePath,
        CompressionOptions options, CompressProgress tracker, CancellationToken ct, string? alsoSkip = null)
    {
        string target = Path.GetFullPath(archivePath);
        string folder = Path.GetDirectoryName(target) ?? throw new IOException("압축 파일을 둘 폴더를 알 수 없어요: " + archivePath);
        var skipped = new List<SkippedSource>(collected.Skipped);
        var items = new List<SourceItem>();
        long totalBytes = 0, skippedBytes = 0;
        int totalFiles = 0, skippedFiles = 0;

        foreach (var (sourceFile, entryName) in collected.Files)
        {
            ct.ThrowIfCancellationRequested();
            // 만들 압축 파일 자신(원본 폴더 안에 덮어 저장)과 이 서비스의 임시 파일은 담지 않는다(스태시 WithoutOutput)
            if (IsSameFile(sourceFile, target) || IsOwnTempFile(sourceFile, target)
                || (alsoSkip is not null && (IsSameFile(sourceFile, alsoSkip) || IsOwnTempFile(sourceFile, Path.GetFullPath(alsoSkip))))) continue;
            long length = SafeLength(sourceFile);
            totalBytes += length;
            totalFiles++;
            if (dropped.TryGetValue(sourceFile, out var droppedReason))
            {
                skipped.Add(new SkippedSource(sourceFile, droppedReason));
                skippedBytes += length;
                skippedFiles++;
                continue;
            }
            // 미리 열어 본다(첫 블록까지 — SourceFileOpener): 7z.dll 은 원본 하나라도 못 읽으면 압축 전체가 실패하므로
            // 공유를 막은 파일은 여기서 건너뛴다(ZIP·TAR 와 같은 I0-2 안내). 길이도 이때 다시 재서 고정한다.
            if (!SourceFileOpener.TryOpenRead(sourceFile, out var probe, out var reason))
            {
                skipped.Add(new SkippedSource(sourceFile, reason!));
                skippedBytes += length;
                skippedFiles++;
                continue;
            }
            using (probe)
                length = probe!.Length;
            var fi = new FileInfo(sourceFile);
            items.Add(new SourceItem(sourceFile, entryName.Replace('/', '\\'), length,
                SafeTime(() => fi.CreationTime), SafeTime(() => fi.LastWriteTime), SafeTime(() => fi.LastAccessTime)));
        }

        // 담은 파일이 하나도 없는데 건너뛴 것이 있다 = 고른 것을 하나도 못 담음 → 빈(또는 빈 폴더뿐인) 7z 를 만들지 않는다(ZIP·TAR 와 같음)
        if (items.Count == 0 && skipped.Count > 0)
            throw new NothingToCompressException(skipped);

        // 폴더 항목: 담는 파일이 든 폴더(원본 폴더 안) + 빈 폴더. 7-Zip·반디집 7z 처럼 모든 폴더를 항목으로 넣는다(폴더 시각도 보존).
        // 중간 폴더('자료' ← '자료\하위')는 7z.dll 경로 방식이 스스로 더한다.
        var dirs = new List<(string SourceDir, string Name)>();
        foreach (var it in items)
        {
            int cut = it.EntryName.LastIndexOf('\\');
            if (cut > 0) dirs.Add((Path.GetDirectoryName(it.Path)!, it.EntryName[..cut]));
        }
        dirs.AddRange(collected.EmptyDirectories.Select(d => (d.SourceDir, Name: d.EntryName.TrimEnd('/').Replace('/', '\\'))));
        var dirGroups = GroupDirectories(dirs);
        if (items.Count == 0 && dirGroups.Count == 0)
            throw new InvalidOperationException("압축할 파일이 없습니다.");

        // 7z 안에서 같은 이름이 둘이면(서로 다른 폴더의 같은 이름 원본) 묶을 수 없다 — 이름표가 겹친다(7-Zip 도 'Duplicate filename' 으로 거절)
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var it in items)
            if (!names.Add(it.EntryName)) throw DuplicateName(it.EntryName);
        var dirOwner = new Dictionary<string, EmptyDirGroup>(StringComparer.OrdinalIgnoreCase);
        foreach (var g in dirGroups)
        {
            foreach (var name in g.EntryNames)
            {
                if (names.Contains(name)) throw DuplicateName(name);
                if (dirOwner.TryGetValue(name, out var owner) && !ReferenceEquals(owner, g)) throw DuplicateName(name);
                dirOwner[name] = g;
            }
        }

        Directory.CreateDirectory(folder);
        string temp = Path.Combine(folder, $"{TempPrefix}{Path.GetFileName(target)}.{Guid.NewGuid().ToString("N")[..8]}.tmp");
        bool existed = File.Exists(target);
        bool committed = false;
        var streams = new List<LazySourceStream>();
        StagingFolder? staging = null;
        tracker.Begin(totalBytes, totalFiles, skippedBytes, skippedFiles);
        try
        {
            string password = options.Password ?? string.Empty;
            var failure = new SourceFailure();
            var dict = new Dictionary<string, StreamWithAttributes>(items.Count, StringComparer.Ordinal);
            foreach (var it in items)
            {
                string shown = it.EntryName.Replace('\\', '/');
                var stream = new LazySourceStream(it.Path, it.Length, ct,
                    onBytes: n => tracker.OnBytes(shown, n),
                    onFailure: failure.Record,
                    onFinished: tracker.OnFileFinished);
                streams.Add(stream);
                dict.Add(it.EntryName, new StreamWithAttributes(stream, it.Created, it.Written, it.Accessed));
            }

            if (dirGroups.Count == 0)
            {
                // 폴더가 없음(파일만 고름): 이름표 방식으로 임시 파일에 바로 쓴다 — 쓰기마다 취소·쓰기 실패를 본다(CancellableWriteStream)
                using var file = new FileStream(temp, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None);
                var output = new CancellableWriteStream(file, ct);
                try
                {
                    NewCompressor(options).CompressStreamDictionary(dict, output, password);
                }
                catch (Exception ex)
                {
                    throw Classify(ex, ct, failure, output);
                }
                file.Flush(flushToDisk: false);
            }
            else
            {
                // 폴더가 있음: 이름표 방식은 폴더 항목을 못 받아(SharpSevenZip 2.0.128 — 폴더면 NullReferenceException, 실측) 폴더만 든 7z 를
                // 경로 방식으로 먼저 만들고(작음) 파일을 덧붙인다. 덧붙이기는 옛 묶음(폴더뿐 — 데이터 없음)을 옮겨 담고 파일은 한 번만 압축하므로
                // 큰 압축을 두 번 쓰지 않는다. 덧붙인 결과는 SharpSevenZip 이 대상 폴더 안 숨김 임시 폴더(같은 볼륨)에 쓴 뒤 임시 파일 자리로 옮긴다.
                tracker.Stage(StageFolders);
                var first = dirGroups[0];
                using (var file = new FileStream(temp, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None))
                {
                    var output = new CancellableWriteStream(file, ct);
                    try
                    {
                        NewCompressor(options).CompressFilesEncrypted(output, first.RootLength, password, first.Paths.ToArray());
                    }
                    catch (Exception ex)
                    {
                        throw Classify(ex, ct, new SourceFailure(), output);
                    }
                }
                staging = StagingFolder.Create(folder);
                AppendDirectories(temp, staging.FullPath, dirGroups.Skip(1), options);
                if (items.Count > 0)
                {
                    ct.ThrowIfCancellationRequested();
                    var append = NewCompressor(options, staging.FullPath);
                    append.CompressionMode = CompressionMode.Append;
                    try
                    {
                        append.CompressStreamDictionary(dict, temp, password);
                    }
                    catch (Exception ex)
                    {
                        throw Classify(ex, ct, failure, output: null);
                    }
                }
            }
            // 파일이 바뀌는 순간 누른 취소를 7z.dll 이 조용히 넘기는 경우가 있어 여기서도 본다
            ct.ThrowIfCancellationRequested();
            if (failure.First is { } late) throw late;

            foreach (var s in streams) s.Dispose();   // 7z.dll 이 다 읽었다 — 남은 핸들이 없게(바꿔 넣기 전에)
            tracker.Stage(StageFinishing);
            Commit(temp, target, overwrite: existed);
            committed = true;
        }
        finally
        {
            foreach (var s in streams) s.Dispose();
            if (!committed) DeleteQuietly(temp);
            staging?.Dispose();
        }
        tracker.Final();
        return new CompressResult(skipped);
    }

    /// <summary>
    /// 7z.dll 이 실패를 알렸을 때(콜백 예외는 모두 SharpSevenZipException '0x8013153b' 로 돌아옴) 진짜 원인을 가른다:
    /// 취소 → <see cref="OperationCanceledException"/>, 원본 문제(열기·읽기 막힘, 크기 바뀜) → 그 <see cref="SourceUnreadableException"/>
    /// (서비스가 그 파일을 빼고 다시 씀), 출력 쓰기 실패(디스크 가득 등) → 그 원래 예외(화면이 '디스크 공간이 모자라요' 등으로 알림),
    /// 7z.dll 을 못 부름 → <see cref="EngineUnavailableException"/>. 그 밖은 그대로.
    /// </summary>
    /// <param name="output">우리가 감싼 출력(덧붙이기는 SharpSevenZip 이 직접 쓰므로 null — 그때 디스크 가득은 7z.dll 결과 코드로 가른다).</param>
    private static Exception Classify(Exception ex, CancellationToken ct, SourceFailure failure, CancellableWriteStream? output)
    {
        if (ex is OperationCanceledException || ct.IsCancellationRequested)
            return ex as OperationCanceledException ?? new OperationCanceledException("압축을 취소했어요.", ex, ct);
        if (failure.First is { } source) return source;
        if (output is { WriteFailed: true, WriteError: { } write }) return write;
        if (ex is SharpSevenZipLibraryException) return new EngineUnavailableException(ex);
        if (ex is SharpSevenZipException && ex.Message.Contains("0x80070070", StringComparison.OrdinalIgnoreCase))
            return new IOException("디스크 공간이 모자라요.", ex) { HResult = DiskFullHResult };
        return ex;
    }

    /// <summary>ERROR_DISK_FULL — 화면은 이 결과 코드의 IOException 을 '디스크 공간이 모자라요'로 알린다(<see cref="ErrorMessages"/>).</summary>
    private const int DiskFullHResult = unchecked((int)0x80070070);

    /// <summary>원본 문제의 첫 기록(7z.dll 콜백 스레드에서 올라옴).</summary>
    private sealed class SourceFailure
    {
        private SourceUnreadableException? _first;

        public SourceUnreadableException? First => Volatile.Read(ref _first);

        public void Record(SourceUnreadableException ex) => Interlocked.CompareExchange(ref _first, ex, null);
    }

    /// <summary>
    /// 임시 파일을 대상 이름으로 바꿔 넣는다. 덮어쓰기는 시작할 때 대상이 있었을 때만(사용자가 저장 창에서 덮어쓰기를 고른 경우) —
    /// 그사이 같은 이름이 새로 생겼으면 덮어쓰지 않고 '이미 있음'으로 실패한다. DLP·백신이 잠깐 잡으면 200ms 간격으로 3번 다시 해 본다.
    /// </summary>
    private static void Commit(string temp, string target, bool overwrite)
    {
        for (int attempt = 0; ; attempt++)
        {
            try
            {
                File.Move(temp, target, overwrite);
                return;
            }
            catch (Exception ex) when (OutputFiles.IsTransientLock(ex) && attempt < OutputFiles.MoveLockRetries)
            {
                Thread.Sleep(OutputFiles.MoveLockDelayMs);
            }
        }
    }

    private static void DeleteQuietly(string path)
    {
        for (int attempt = 0; attempt <= OutputFiles.MoveLockRetries; attempt++)
        {
            try
            {
                if (File.Exists(path)) File.Delete(path);
                return;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                if (attempt == OutputFiles.MoveLockRetries)
                {
                    Logger.LogError("7z 임시 파일을 지우지 못함: " + path, ex);
                    return;
                }
                Thread.Sleep(OutputFiles.MoveLockDelayMs);
            }
        }
    }

    /// <summary>이 서비스가 <paramref name="target"/> 을 만들 때 쓰는 임시 파일(<c>.~이름.8글자.tmp</c>)인가 — 같은 폴더에서만.</summary>
    private static bool IsOwnTempFile(string path, string target)
    {
        try
        {
            string name = Path.GetFileName(path);
            string prefix = TempPrefix + Path.GetFileName(target) + ".";
            return name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
                   && name.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase)
                   && name.Length == prefix.Length + 8 + 4
                   && string.Equals(Path.GetDirectoryName(Path.GetFullPath(path)), Path.GetDirectoryName(target), StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    private static DateTime? SafeTime(Func<DateTime> get)
    {
        try { return get(); }
        catch { return null; }
    }

    private static InvalidOperationException DuplicateName(string entryName) =>
        new($"같은 이름이 여러 개라 7Z 로 묶을 수 없어요: {entryName.Replace('\\', '/')}\n폴더를 하나로 모아서 다시 압축해 주세요.");

    /// <summary>
    /// 원본 위치(넣을 이름의 기준 폴더)가 같은 폴더 묶음. RootLength = 기준 폴더 경로 + '\' 의 길이(7z.dll 이 이름을 자르는 자리).
    /// Paths = 넘길 폴더(겹침 없음), EntryNames = 7z 안에 생길 폴더 이름 전부(7z.dll 이 더하는 중간 폴더 포함 — 이름 겹침 검사용).
    /// </summary>
    private sealed record EmptyDirGroup(int RootLength, List<string> Paths, HashSet<string> EntryNames);

    /// <summary>폴더들을 기준 폴더(원본 목록에서 고른 항목의 부모)별로 묶는다. 보통은 하나(한 폴더에서 고름) — 첫 묶음으로 만들고 나머지는 묶음마다 한 번 덧붙인다.</summary>
    private static List<EmptyDirGroup> GroupDirectories(List<(string SourceDir, string Name)> dirs)
    {
        var groups = new Dictionary<string, EmptyDirGroup>(StringComparer.OrdinalIgnoreCase);
        var order = new List<EmptyDirGroup>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (sourceDir, name) in dirs)
        {
            string full = Path.GetFullPath(sourceDir).TrimEnd(Path.DirectorySeparatorChar);
            if (name.Length == 0 || full.Length <= name.Length || !full.EndsWith(name, StringComparison.OrdinalIgnoreCase)) continue;
            if (!seen.Add(full)) continue;
            string root = full[..^name.Length];   // 'C:\a\' (끝 '\' 포함) — 이름은 원본 경로의 끝부분이다
            if (!groups.TryGetValue(root, out var g))
            {
                groups[root] = g = new EmptyDirGroup(root.Length, new List<string>(), new HashSet<string>(StringComparer.OrdinalIgnoreCase));
                order.Add(g);
            }
            g.Paths.Add(full);
            for (int i = name.IndexOf('\\'); i > 0; i = name.IndexOf('\\', i + 1))
                g.EntryNames.Add(name[..i]);
            g.EntryNames.Add(name);
        }
        return order;
    }

    /// <summary>
    /// 이미 만든 7z(<paramref name="archive"/> — 임시 파일)에 폴더를 덧붙인다(7z.dll 덧붙이기 — 원래 묶음은 다시 압축하지 않고 옮겨 담음,
    /// 파일 이름 암호화·암호도 그대로). SharpSevenZip 은 덧붙인 결과를 자기 임시 폴더에 새로 쓴 뒤 바꿔 넣는다 — 그 폴더를 대상 폴더 안
    /// 숨김 임시 폴더(<paramref name="tempFolder"/>, 같은 볼륨)로 준다(기본은 %TEMP% — C: 로 복사하게 됨).
    /// 파일보다 먼저(폴더뿐인 작은 7z 일 때) 부르므로 옮겨 담을 데이터가 없다.
    /// </summary>
    private static void AppendDirectories(string archive, string tempFolder, IEnumerable<EmptyDirGroup> groups, CompressionOptions options)
    {
        foreach (var g in groups)
        {
            var append = NewCompressor(options, tempFolder);
            append.CompressionMode = CompressionMode.Append;
            try
            {
                append.CompressFilesEncrypted(archive, g.RootLength, options.Password ?? string.Empty, g.Paths.ToArray());
            }
            catch (SharpSevenZipLibraryException ex)
            {
                throw new EngineUnavailableException(ex);
            }
        }
    }

    private static SharpSevenZipCompressor NewCompressor(CompressionOptions options, string? tempFolder = null)
    {
        var compressor = tempFolder is null ? new SharpSevenZipCompressor() : new SharpSevenZipCompressor(tempFolder);
        compressor.ArchiveFormat = OutArchiveFormat.SevenZip;
        compressor.CompressionLevel = ToSevenZipLevel(options.Level);
        // 파일 이름 암호화(헤더 암호)는 암호가 있을 때만. 암호가 있으면 7z 기본 AES-256(암호는 UTF-16 — 반디집·7-Zip 과 호환).
        compressor.EncryptHeaders = options.HasPassword && options.EncryptFileNames;
        foreach (var (name, value) in EngineParameters(options))
            compressor.CustomParameters[name] = value;
        return compressor;
    }

    /// <summary>7z.dll 압축 수준(x): 저장만=0, 빠름=1, 보통=5, 최대=9.</summary>
    internal static CompressionLevel ToSevenZipLevel(CompressionLevelOption opt) => opt switch
    {
        CompressionLevelOption.Store => CompressionLevel.None,
        CompressionLevelOption.Fastest => CompressionLevel.Fast,
        CompressionLevelOption.Normal => CompressionLevel.Normal,
        CompressionLevelOption.Maximum => CompressionLevel.Ultra,
        _ => CompressionLevel.Normal,
    };

    /// <summary>
    /// 7z.dll 에 넘길 추가 설정: 솔리드(s), 스레드 수(mt), 사전 크기(d), LZMA2 블록 크기(c).
    /// 7-Zip 기본(보통: 스레드 전부·사전 16~64MB·블록 = 사전 × 4 이상)은 7z.dll 이 원본을 몇 초 만에 메모리로 다 읽어(진행 막대가 바로 100%)
    /// 첫 블록이 끝날 때까지 출력이 없고(취소가 안 먹음) 메모리를 1.2~3.4GB 쓴다(1차 W1-B 검증 지적). 블록·사전·스레드를 줄이면 읽기·쓰기가
    /// 몇 초마다 일어나 진행률이 실제를 따라가고 취소도 곧 먹힌다. 대가는 크기 조금(블록마다 사전을 새로 시작 — 이 실측에서는 차이 0.2% 안).
    /// 실측(이 PC 논리 CPU 8·RAM 7.8GB, 원본 220MB = 무작위 120 + 글자 80 + 0 20MB, 보통):
    ///   기본 91.7초·최대 작업 세트 1,153MB / mt=2;d=16m 103.9초·224MB / s=on;mt=on;c=8m 38.8초·506MB /
    ///   <b>mt=4;d=16m;c=8m 42.0초·254MB(채택)</b> / 최대 mt=2;d=32m;c=16m 77.4초·214MB. 출력 크기는 모두 136.8~137.0MB.
    /// 스레드는 min(4, 논리 CPU). 저장만은 추가 값 없음(복사라 원래 짧음). 솔리드는 늘 넘긴다.
    /// </summary>
    internal static IReadOnlyList<(string Name, string Value)> EngineParameters(CompressionOptions options)
    {
        string mt = Math.Clamp(Environment.ProcessorCount, 1, 4).ToString(System.Globalization.CultureInfo.InvariantCulture);
        var list = new List<(string, string)> { ("s", options.Solid ? "on" : "off") };
        switch (options.Level)
        {
            case CompressionLevelOption.Fastest:
                list.Add(("mt", mt));
                list.Add(("d", "4m"));
                list.Add(("c", "4m"));
                break;
            case CompressionLevelOption.Normal:
                list.Add(("mt", mt));
                list.Add(("d", "16m"));
                list.Add(("c", "8m"));
                break;
            case CompressionLevelOption.Maximum:
                list.Add(("mt", "2"));
                list.Add(("d", "32m"));
                list.Add(("c", "16m"));
                break;
        }
        return list;
    }

    /// <summary>
    /// 7z 만들기 진행률: 7z.dll 이 원본에서 실제로 읽은 바이트(<see cref="LazySourceStream"/>) / 원본 합. 건너뛴 원본은 처음부터 '처리됨'으로 센다(ZIP 과 같음).
    /// 줄어들지 않게(읽는 도중 막힌 파일을 빼고 다시 쓰는 회차에서도 — 총량·총 파일 수는 회차마다 같다), 총량을 넘지 않게.
    /// 읽기는 7z.dll 의 여러 스레드에서 올 수 있어 잠근다. 단계 문구는 '압축하는 중'(첫 읽기 전) → '폴더 넣는 중'(폴더가 있을 때, 금방) → 원본 이름 → '마무리하는 중'.
    /// </summary>
    private sealed class CompressProgress
    {
        private readonly IProgress<ArchiveProgress>? _progress;
        private readonly object _gate = new();
        private long _total;
        private int _totalFiles;
        private long _read;
        private int _files;
        private long _lastBytes;
        private int _lastFiles;
        private string _current = string.Empty;

        public CompressProgress(IProgress<ArchiveProgress>? progress) => _progress = progress;

        /// <summary>한 회차의 시작(다시 쓰는 회차면 건너뛴 파일만큼에서 다시 센다 — 보이는 값은 줄지 않음).</summary>
        public void Begin(long totalBytes, int totalFiles, long skippedBytes, int skippedFiles)
        {
            lock (_gate)
            {
                // 다시 쓰는 회차에서 크기가 바뀐 원본을 다시 재면 총량이 조금 달라질 수 있다 — 보이는 값이 줄지 않게 큰 쪽
                _total = Math.Max(_total, totalBytes);
                _totalFiles = Math.Max(_totalFiles, totalFiles);
                _read = skippedBytes;
                _files = skippedFiles;
                _current = StageStarting;
                Report();
            }
        }

        public void OnBytes(string entryName, int count)
        {
            lock (_gate)
            {
                _read += count;
                _current = entryName;
                Report();
            }
        }

        public void OnFileFinished()
        {
            lock (_gate)
            {
                _files = Math.Min(_files + 1, _totalFiles);
                if (_files == _totalFiles)
                {
                    // 원본을 다 읽었다 — 7z.dll 이 마지막 묶음을 압축해 쓰는 동안
                    _current = StageFinishing;
                    Report();
                }
            }
        }

        public void Stage(string text)
        {
            lock (_gate)
            {
                _current = text;
                Report();
            }
        }

        private void Report()
        {
            if (_progress is null) return;
            _lastBytes = Math.Min(_total, Math.Max(_lastBytes, _read));
            _lastFiles = Math.Min(_totalFiles, Math.Max(_lastFiles, _files));
            _progress.Report(new ArchiveProgress
            {
                CurrentFile = _current,
                BytesProcessed = _lastBytes,
                TotalBytes = _total,
                FilesProcessed = _lastFiles,
                TotalFiles = _totalFiles,
            });
        }

        public void Final()
        {
            lock (_gate)
            {
                _progress?.Report(new ArchiveProgress
                {
                    CurrentFile = string.Empty,
                    BytesProcessed = _total,
                    TotalBytes = _total,
                    FilesProcessed = _totalFiles,
                    TotalFiles = _totalFiles,
                });
            }
        }
    }
}
