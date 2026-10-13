using System.IO;
using ICSharpCode.SharpZipLib.Checksum;
using Iljip.Models;
using SharpSevenZip;
using SharpSevenZip.Exceptions;

namespace Iljip.Services.Editing;

/// <summary>
/// 7z 고치기(W2-06, I2-3) — 7z.dll 의 고치기(지우기·이름 바꾸기)와 덧붙이기를 원본의 '사본'(원본 옆 숨김 임시 파일)에 차례로 한다.
/// <list type="bullet">
/// <item>지우기 → 이름 바꾸기 → 파일 덧붙이기 → 폴더 덧붙이기(임시 이름) → 폴더 이름 바꾸기, 필요한 것만. SharpSevenZip 2.0.128 의 ModifyArchive 는 지우기와 이름 바꾸기를
///   한 번에 하면 지운 자리로 옮겨진 항목의 새 이름을 잃는다(실측) — 그래서 나눠 하고, 단계마다 목록을 다시 읽어 번호를 맞춘다.</item>
/// <item>폴더 항목은 이름표 방식으로 못 넣어(폴더면 NullReferenceException — W2-02 실측) 경로 방식으로 넣는데, 경로 방식은 위 폴더까지 다시 넣어 같은 이름이 둘이 된다.
///   그래서 맨 위의 임시 이름(한 조각)으로 넣고 이름 바꾸기로 제자리에 둔다.</item>
/// <item>같은 암호·같은 파일 이름 암호화(헤더 암호)·같은 솔리드로 쓴다(7z 암호는 AES-256 하나). 암호 항목이 있으면 암호가 꼭 필요하다(솔리드 묶음을 다시 쓰려면 풀어야 함) —
///   없으면 <see cref="PasswordRequiredException"/>(목록까지 잠겼으면 <see cref="ListPasswordRequiredException"/>), 틀리면 <see cref="WrongPasswordException"/>.</item>
/// <item>넣을 파일은 <see cref="LazySourceStream"/>(진행률·취소·읽는 동안만 핸들). 미리 열어 봐서 못 여는 원본은 건너뛰고, 읽는 도중 막히면 그 파일을 빼고 처음부터 다시 한다.</item>
/// <item>SharpSevenZip 이 단계마다 쓰는 임시 파일은 원본 폴더 안 숨김 임시 폴더(<see cref="StagingFolder"/>)에 — 끝나면(실패해도) 지운다.</item>
/// <item>진행·취소(W2-06 검증 지적 — 솔리드 7z 에서 하나만 지워도 묶음 전체를 다시 압축해 200MB 에 100초, 그동안 0/0 에 취소가 안 먹음):
///   SharpSevenZip 의 고치기·덧붙이기는 7z.dll 의 진행·취소 콜백을 잇지 않고 작업 사본을 스스로 열어 읽는다. 그래서 단계마다 <see cref="PassGuard"/> 가
///   ① 0.2초마다 임시 폴더에 쓰인 결과 크기를 재 진행률로 알리고 ② 취소하면 작업 사본 전체를 잠가(FileStream.Lock) 7z.dll 의 다음 읽기가
///   ERROR_LOCK_VIOLATION 으로 실패해 멈추게 한다. 다시 압축하는 블록은 2MB(c=2m — 만들기의 8MB 보다 작게: 취소 때 이미 맡은 블록만 마저 압축하므로
///   200MB 솔리드에서 취소 0.5~1.4초, 크기는 8MB 와 차이 없음 — 실측). 검사(7z.dll 시험)도 우리 읽기 스트림으로 넘겨 취소·진행을 본다.</item>
/// </list>
/// 다 되면 검사(<see cref="IArchiveTester"/> — 목록 + 7z.dll 시험)가 맞을 때만 원본과 바꾼다(<see cref="ArchiveSwap"/> — 읽기 시작할 때 잰 원본의 크기·시각이
/// 그대로일 때만). 7z.dll 을 못 쓰는 PC·나뉜 7z(.7z.001)는 고칠 수 없다(7z.dll 이 없으면 그 이유를 알림).
/// </summary>
public sealed class SevenZipArchiveEditor : IArchiveEditor
{
    private readonly IArchiveTester _tester;

    public SevenZipArchiveEditor(IArchiveTester? tester = null) => _tester = tester ?? ArchiveTester.Instance;

    /// <summary>시험용: 단계 하나가 끝날 때마다 부른다(단계 이름) — 중간 실패 주입. 실제 동작에서는 null.</summary>
    internal Action<string>? AfterPass { get; init; }

    /// <summary>임시 폴더 이름 머리(맨 위에 잠깐 두는 새 폴더 이름).</summary>
    private const string PlaceholderPrefix = "~일집-새폴더-";

    public bool CanEdit(string path) => IsEditablePath(path);

    /// <summary>확장자 .7z(나뉜 .7z.001 은 아님)이고 7z.dll 을 쓸 수 있다.</summary>
    internal static bool IsEditablePath(string path) =>
        path.EndsWith(".7z", StringComparison.OrdinalIgnoreCase) && SevenZipLibrary.IsAvailable;

    public Task<EditResult> EditAsync(
        string archivePath, IReadOnlyList<EditOp> ops, string? password, IProgress<ArchiveProgress>? progress, CancellationToken cancellationToken)
    {
        var resolver = ExtractionScope.Current?.Resolver;
        return Task.Run(() => Edit(Path.GetFullPath(archivePath), ops, password, resolver, progress, cancellationToken), cancellationToken);
    }

    /// <summary>7z 안 항목 하나(목록에서 읽은 것).</summary>
    private sealed record Entry(int Index, string Key, bool IsDirectory, long Size, bool Encrypted, uint Crc, DateTime? Modified, string Method);

    private EditResult Edit(
        string archivePath, IReadOnlyList<EditOp> ops, string? password, ICollisionResolver? resolver,
        IProgress<ArchiveProgress>? progress, CancellationToken ct)
    {
        if (!IsEditablePath(archivePath)) throw new ArchiveNotEditableException(ArchiveServiceLocator.NotEditableMessage(archivePath));
        SevenZipLibrary.ThrowIfUnavailable();
        ArchiveSwap.EnsureWritable(archivePath);
        ct.ThrowIfCancellationRequested();
        var stamp = ArchiveSwap.Stamp(archivePath);   // 바꾸기 직전에 같은지 본다(그 사이 다른 곳에서 바뀌었으면 덮어쓰지 않음)

        bool headerEncrypted = ArchiveHeaderProbe.IsSevenZipHeaderEncrypted(archivePath);
        bool hasPassword = !string.IsNullOrEmpty(password);
        if (headerEncrypted && !hasPassword) throw new ListPasswordRequiredException();
        var (entries, solid) = ReadEntries(archivePath, hasPassword ? password : null, headerEncrypted);
        bool anyEncrypted = headerEncrypted || entries.Any(e => e.Encrypted && !e.IsDirectory);
        if (anyEncrypted && !hasPassword) throw new PasswordRequiredException(isHeaderEncrypted: headerEncrypted);
        if (anyEncrypted && !headerEncrypted) VerifyPassword(archivePath, password!, entries, ct);
        string pw = anyEncrypted ? password! : string.Empty;

        // ── 계획 ──
        var planner = new EditPlanner<Entry>(entries.Select(e => new EditPlanner<Entry>.Item
        {
            Source = e,
            OriginalKey = e.Key,
            IsDirectory = e.IsDirectory,
            Size = e.Size,
            Modified = e.Modified,
        }));
        var files = new List<EditSupport.PlannedSource>();
        var skipped = new List<SkippedSource>();
        var addFolders = new Dictionary<string, string>(StringComparer.Ordinal);   // 넣는 파일 키 → 그 넣기의 대상 폴더
        int tempCounter = 0;
        bool anyAdd = false;
        foreach (var op in ops)
        {
            ct.ThrowIfCancellationRequested();
            switch (op)
            {
                case EditOp.Delete d: planner.Delete(d.EntryKeys); break;
                case EditOp.Rename r: planner.Rename(r.EntryKey, r.NewName); break;
                case EditOp.NewFolder n: planner.NewFolder(n.Path); break;
                case EditOp.Add a:
                    anyAdd = true;
                    int before = files.Count;
                    var folders = new List<(string Key, string SourceDir)>();
                    EditSupport.PlanAdd(planner, a, resolver, () => (++tempCounter).ToString("D7", System.Globalization.CultureInfo.InvariantCulture), files, skipped, folders);
                    string target = EditPlanner<Entry>.Normalize(a.TargetFolder);
                    foreach (var f in files.Skip(before)) addFolders[f.TempKey] = target;
                    foreach (var (key, _) in folders) addFolders["dir:" + key] = target;
                    break;
            }
        }

        // 못 여는 원본은 미리 뺀다(7z.dll 은 하나라도 못 읽으면 통째로 실패)
        var lengths = new Dictionary<string, long>(StringComparer.Ordinal);
        foreach (var a in planner.AddedFiles.ToList())
        {
            if (!SourceFileOpener.TryOpenRead(a.SourcePath, out var probe, out var reason))
            {
                skipped.Add(new SkippedSource(a.SourcePath, reason!));
                a.Removed = true;
                continue;
            }
            using (probe) lengths[a.TempKey] = probe!.Length;
        }
        if (anyAdd && !planner.AddedFiles.Any() && skipped.Count > 0 && !planner.HasChanges)
            throw new ArchiveEditException(EditSupport.NothingToAddMessage);
        if (!planner.HasChanges)
            return new EditResult { CollisionSkipped = planner.CollisionSkipped, Skipped = skipped };

        var options = new CompressionOptions
        {
            Level = entries.Where(e => !e.IsDirectory && e.Size > 0).All(e => e.Method.StartsWith("Copy", StringComparison.OrdinalIgnoreCase))
                    && entries.Any(e => !e.IsDirectory && e.Size > 0)
                ? CompressionLevelOption.Store
                : CompressionLevelOption.Normal,
            Solid = solid,
            Password = anyEncrypted ? password : null,
            EncryptFileNames = headerEncrypted,
        };

        var tracker = new EditSupport.Tracker(progress);
        var dropped = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var originalKeys = entries.Select(e => e.Key).ToList();
        while (true)
        {
            try
            {
                RunOnce(archivePath, stamp, planner, entries, originalKeys, addFolders, lengths, options, pw, headerEncrypted, tracker, ct);
                tracker.Final();
                return new EditResult
                {
                    AddedFiles = planner.AddedFiles.Count(),
                    CollisionSkipped = planner.CollisionSkipped,
                    Skipped = skipped,
                };
            }
            catch (SourceUnreadableException ex) when (!dropped.ContainsKey(ex.SourcePath))
            {
                // 읽는 도중 막힌 원본 — 그 파일을 빼고 처음부터(원본 사본부터) 다시
                dropped[ex.SourcePath] = ex.Reason;
                skipped.Add(new SkippedSource(ex.SourcePath, ex.Reason));
                foreach (var a in planner.AddedFiles.Where(a => string.Equals(a.SourcePath, ex.SourcePath, StringComparison.OrdinalIgnoreCase)).ToList())
                    a.Removed = true;
                if (!planner.HasChanges) throw new ArchiveEditException(EditSupport.NothingToAddMessage, ex);
            }
        }
    }

    /// <summary>
    /// 원본 사본에 단계들을 하고 검사한 뒤 바꾼다. 실패하면 사본·임시 폴더를 지우고 예외(원본 그대로).
    /// 진행률은 단계별 무게(바이트)로 한 막대에: 사본 복사·지우기·이름 바꾸기·새 폴더(덧붙이기 + 이름 바꾸기)·검사는 압축 크기만큼, 넣기는 넣을 파일 크기만큼.
    /// </summary>
    private void RunOnce(
        string archivePath, ArchiveSwap.FileStamp stamp, EditPlanner<Entry> planner, List<Entry> entries, IReadOnlyCollection<string> originalKeys,
        Dictionary<string, string> addFolders, Dictionary<string, long> lengths, CompressionOptions options, string pw, bool headerEncrypted,
        EditSupport.Tracker tracker, CancellationToken ct)
    {
        string folder = Path.GetDirectoryName(archivePath)!;
        string work = ArchiveSwap.TempPath(archivePath);
        bool committed = false;
        var streams = new List<LazySourceStream>();
        StagingFolder? staging = null;

        var removed = planner.Items.Where(i => i.Removed).Select(i => i.Source.Index).ToList();
        var renamed = planner.Kept.Where(i => i.Renamed).ToList();
        var added = planner.AddedFiles.ToList();
        var newDirs = NewDirectories(planner, addFolders);
        long size = Math.Max(1, stamp.Length);
        long addBytes = added.Sum(a => lengths.GetValueOrDefault(a.TempKey));
        tracker.SetTotals(size + (removed.Count > 0 ? size : 0) + (renamed.Count > 0 ? size : 0) + addBytes + (newDirs.Count > 0 ? 2 * size : 0) + size, added.Count);
        try
        {
            tracker.Stage(EditSupport.StagePreparing);
            CopyOriginal(archivePath, work, ct, tracker.OnBytes);
            tracker.EndPhase(size);
            try { File.SetAttributes(work, File.GetAttributes(work) | FileAttributes.Hidden); } catch { /* 숨김 실패는 무시 */ }
            staging = StagingFolder.Create(folder);
            string temp = staging.FullPath;
            tracker.Stage(EditSupport.StageRewriting);

            // 1) 지우기(원래 번호) — 솔리드면 남는 파일을 다시 압축한다(결과 크기 ≈ 남는 내용의 몫)
            if (removed.Count > 0)
            {
                var map = removed.ToDictionary(i => i, _ => (string?)null);
                long expectedOut = (long)(LengthOf(work) * KeptShare(entries, removed));
                Modify(work, map, options, pw, temp, ct, OutputProgress(tracker, size, expectedOut));
                tracker.EndPhase(size);
                Pass("delete", ct);
            }

            // 2) 이름 바꾸기(지운 뒤 목록에서 원래 이름으로 번호를 찾음)
            if (renamed.Count > 0)
            {
                var current = ReadEntries(work, pw.Length > 0 ? pw : null, headerEncrypted).Entries;
                var slots = MatchKept(planner, current);
                var map = new Dictionary<int, string?>();
                foreach (var it in renamed) map[slots[it]] = ToSevenZipName(it.Key);
                Modify(work, map, options, pw, temp, ct, OutputProgress(tracker, size, LengthOf(work)));
                tracker.EndPhase(size);
                Pass("rename", ct);
            }

            // 3) 파일 덧붙이기(이름표 방식 — 최종 이름 그대로). 진행률은 넣을 파일을 읽은 바이트(LazySourceStream).
            var failure = new SourceFailure();
            if (added.Count > 0)
            {
                var dict = new Dictionary<string, StreamWithAttributes>(added.Count, StringComparer.Ordinal);
                foreach (var a in added)
                {
                    var stream = new LazySourceStream(a.SourcePath, lengths.GetValueOrDefault(a.TempKey), ct,
                        onBytes: n => tracker.OnBytes(n), onFailure: failure.Record);
                    streams.Add(stream);
                    var fi = new FileInfo(a.SourcePath);
                    dict.Add(ToSevenZipName(a.Key), new StreamWithAttributes(stream, SafeTime(() => fi.CreationTime), SafeTime(() => fi.LastWriteTime), SafeTime(() => fi.LastAccessTime)));
                }
                var append = NewCompressor(options, temp);
                append.CompressionMode = CompressionMode.Append;
                try
                {
                    using (new PassGuard(work, temp, ct, onOutput: null))
                        append.CompressStreamDictionary(dict, work, pw);
                }
                catch (Exception ex)
                {
                    throw Classify(ex, ct, failure);
                }
                ct.ThrowIfCancellationRequested();
                if (failure.First is { } late) throw late;
                foreach (var s in streams) s.Dispose();
                tracker.EndPhase(addBytes);
                Pass("append", ct);
            }

            // 4) 새 폴더: 맨 위 임시 이름으로 덧붙인 뒤 제자리 이름으로 바꾼다(경로 방식이 위 폴더를 다시 넣지 않게)
            if (newDirs.Count > 0)
            {
                string root = Path.Combine(temp, "d");
                Directory.CreateDirectory(root);
                string tag = Guid.NewGuid().ToString("N")[..8];
                var placeholders = new Dictionary<string, string>(StringComparer.Ordinal);   // 임시 이름 → 최종 키
                var paths = new List<string>();
                for (int i = 0; i < newDirs.Count; i++)
                {
                    string name = $"{PlaceholderPrefix}{tag}-{i}";
                    string dir = Path.Combine(root, name);
                    Directory.CreateDirectory(dir);
                    placeholders[name] = newDirs[i];
                    paths.Add(dir);
                }
                var appendDirs = NewCompressor(options, temp);
                appendDirs.CompressionMode = CompressionMode.Append;
                try
                {
                    using (new PassGuard(work, temp, ct, OutputProgress(tracker, size, LengthOf(work))))
                        appendDirs.CompressFilesEncrypted(work, root.Length + 1, pw, paths.ToArray());
                }
                catch (Exception ex)
                {
                    throw Classify(ex, ct, new SourceFailure());
                }
                tracker.EndPhase(size);
                Pass("folders", ct);

                var current = ReadEntries(work, pw.Length > 0 ? pw : null, headerEncrypted).Entries;
                var map = new Dictionary<int, string?>();
                foreach (var e in current)
                    if (placeholders.TryGetValue(e.Key, out var final)) map[e.Index] = ToSevenZipName(final);
                if (map.Count != placeholders.Count)
                    throw new InvalidDataException("새 폴더 임시 이름을 찾지 못함");
                Modify(work, map, options, pw, temp, ct, OutputProgress(tracker, size, LengthOf(work)));
                tracker.EndPhase(size);
                Pass("folders-rename", ct);
            }

            // 5) 검사 → 바꾸기(검사가 틀리면 원래 압축도 보아 '원래부터 손상'을 가린다)
            ct.ThrowIfCancellationRequested();
            tracker.Stage(EditSupport.StageTesting);
            var expected = planner.Kept.Select(i => i.Key)
                .Concat(newDirs)
                .Concat(added.Select(a => a.Key))
                .ToList();
            staging.Dispose();
            staging = null;
            ArchiveTester.TestOrBlameOriginal(_tester, work, archivePath, EditableFormat.SevenZip, expected, originalKeys,
                pw.Length > 0 ? pw : null, ct, f => tracker.Within(size, f));
            ct.ThrowIfCancellationRequested();
            ArchiveSwap.Commit(work, archivePath, stamp);
            committed = true;
        }
        catch (ArchiveEditException ex) when (ex.Message == EngineFailedMessage && !ct.IsCancellationRequested)
        {
            // 7z.dll 이 다시 쓰다 실패했는데 원래 압축도 시험에 틀리면 원래부터 손상(고치다 생긴 문제가 아님)
            if (ArchiveTester.OriginalFails(archivePath, EditableFormat.SevenZip, originalKeys, pw.Length > 0 ? pw : null, ct))
                throw new ArchiveEditException(ArchiveTester.OriginalDamagedMessage, ex);
            throw;
        }
        finally
        {
            foreach (var s in streams) s.Dispose();
            if (!committed) ArchiveSwap.DeleteQuietly(work);
            staging?.Dispose();
        }
    }

    /// <summary>7z.dll 이 다시 쓰다 알 수 없는 이유로 실패했을 때(취소·원본 파일·디스크 가득·7z.dll 없음이 아닐 때)의 화면 문구.</summary>
    internal const string EngineFailedMessage = "7z 압축 파일을 다시 쓰다가 실패했어요. 원래 압축 파일은 그대로예요.";

    private void Pass(string name, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        AfterPass?.Invoke(name);
    }

    private static long LengthOf(string path)
    {
        try { return new FileInfo(path).Length; }
        catch { return 0; }
    }

    /// <summary>지운 뒤 남는 내용의 몫(남는 파일 크기 합 / 전체 파일 크기 합) — 지우기 단계 결과 크기 짐작. 파일 크기가 다 0 이면 1.</summary>
    private static double KeptShare(List<Entry> entries, List<int> removed)
    {
        var gone = removed.ToHashSet();
        long all = entries.Where(e => !e.IsDirectory).Sum(e => Math.Max(0, e.Size));
        if (all <= 0) return 1;
        long kept = entries.Where(e => !e.IsDirectory && !gone.Contains(e.Index)).Sum(e => Math.Max(0, e.Size));
        return Math.Clamp((double)kept / all, 0.01, 1);
    }

    /// <summary>임시 폴더에 쓰인 결과 크기 → 이 단계(무게 <paramref name="weight"/>)의 진행률(짐작한 결과 크기 <paramref name="expectedOutput"/> 에 견줌).</summary>
    private static Action<long> OutputProgress(EditSupport.Tracker tracker, long weight, long expectedOutput) =>
        written => tracker.Within(weight, expectedOutput > 0 ? (double)written / expectedOutput : 0);

    /// <summary>원본을 사본으로 복사(취소 확인, 복사한 바이트를 알림). 원본은 읽기만.</summary>
    private static void CopyOriginal(string from, string to, CancellationToken ct, Action<long>? onBytes)
    {
        using var src = new FileStream(from, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 1 << 16);
        using var dst = new FileStream(to, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1 << 16);
        var buffer = new byte[1 << 16];
        int n;
        while ((n = src.Read(buffer, 0, buffer.Length)) > 0)
        {
            ct.ThrowIfCancellationRequested();
            dst.Write(buffer, 0, n);
            onBytes?.Invoke(n);
        }
    }

    /// <summary>7z.dll 고치기(번호 → 새 이름, null 이면 지움). 같은 암호·헤더 암호·솔리드로 다시 쓴다(<see cref="PassGuard"/> 로 진행·취소).</summary>
    private static void Modify(
        string archive, Dictionary<int, string?> map, CompressionOptions options, string pw, string tempFolder, CancellationToken ct, Action<long>? onOutput)
    {
        ct.ThrowIfCancellationRequested();
        var c = NewCompressor(options, tempFolder);
        try
        {
            using (new PassGuard(archive, tempFolder, ct, onOutput))
                c.ModifyArchive(archive, map, pw);
        }
        catch (Exception ex)
        {
            throw Classify(ex, ct, new SourceFailure());
        }
        ct.ThrowIfCancellationRequested();
    }

    /// <summary>
    /// 고치기용 7z.dll 설정: 만들기와 같은 값(<see cref="SevenZipArchiveService.EngineParameters"/>)에서 다시 압축하는 블록만 2MB(c=2m).
    /// 취소하면 7z.dll 은 이미 맡은 블록(스레드마다 하나)을 마저 압축한 뒤 멈추므로 블록이 작을수록 빨리 멈춘다 — 실측(무작위 96MB 솔리드 지우기 도중 취소):
    /// c=8m 4.6초 / c=2m 0.5~1.4초, 결과 크기는 c=2m 이 오히려 0.3% 작음(글자 24MB). 저장만이면 블록 설정이 없다.
    /// </summary>
    internal static IReadOnlyList<(string Name, string Value)> EditEngineParameters(CompressionOptions options) =>
        SevenZipArchiveService.EngineParameters(options).Select(p => p.Name == "c" ? (p.Name, "2m") : p).ToList();

    private static SharpSevenZipCompressor NewCompressor(CompressionOptions options, string tempFolder)
    {
        var compressor = new SharpSevenZipCompressor(tempFolder)
        {
            ArchiveFormat = OutArchiveFormat.SevenZip,
            CompressionLevel = SevenZipArchiveService.ToSevenZipLevel(options.Level),
            EncryptHeaders = options.HasPassword && options.EncryptFileNames,
        };
        foreach (var (name, value) in EditEngineParameters(options))
            compressor.CustomParameters[name] = value;
        return compressor;
    }

    /// <summary>
    /// 7z.dll 이 작업 사본을 다시 쓰는 한 단계(고치기·덧붙이기) 동안의 진행과 취소(클래스 설명 참고). 끝나면(실패해도) Dispose — 잠금을 풀고 재기를 멈춘다.
    /// <list type="bullet">
    /// <item>진행: 0.2초마다 임시 폴더 바로 아래 파일(SharpSevenZip 이 쓰는 결과)의 크기를 재 <c>onOutput</c> 에 알린다(Dispose 뒤에는 부르지 않음).</item>
    /// <item>취소: 작업 사본을 FileShare.ReadWrite|Delete 로 따로 열어 전체를 잠근다(LockFile). 7z.dll 이 쓰는 핸들의 다음 읽기가
    ///   ERROR_LOCK_VIOLATION 으로 실패하고 SharpSevenZip 이 그것을 HRESULT 로 넘겨 7z.dll 이 멈춘다. 작업 사본은 우리 것이라(원본 아님) 망가져도 된다.
    ///   잠그지 못하면(DLP 가 잠깐 잡음 등) 그 단계가 끝난 뒤에 멈춘다.</item>
    /// </list>
    /// </summary>
    private sealed class PassGuard : IDisposable
    {
        internal const int PollMs = 200;

        private readonly string _work;
        private readonly string _outputFolder;
        private readonly Action<long>? _onOutput;
        private readonly object _gate = new();
        private readonly Timer? _timer;
        private readonly CancellationTokenRegistration _registration;
        private FileStream? _lock;
        private bool _disposed;
        private int _measuring;

        public PassGuard(string work, string outputFolder, CancellationToken ct, Action<long>? onOutput)
        {
            _work = work;
            _outputFolder = outputFolder;
            _onOutput = onOutput;
            if (onOutput is not null) _timer = new Timer(_ => Measure(), null, PollMs, PollMs);
            // 취소 버튼(화면 스레드)을 잡아 두지 않게 잠그기는 스레드 풀에서
            _registration = ct.Register(() => ThreadPool.QueueUserWorkItem(_ => LockWork()));
        }

        private void Measure()
        {
            if (Interlocked.Exchange(ref _measuring, 1) == 1) return;
            try
            {
                long written = 0;
                foreach (var f in Directory.EnumerateFiles(_outputFolder))
                {
                    try { written += new FileInfo(f).Length; }
                    catch { /* 막 옮겨짐 */ }
                }
                lock (_gate)
                {
                    if (!_disposed) _onOutput!(written);
                }
            }
            catch
            {
                // 재기는 덤
            }
            finally
            {
                Volatile.Write(ref _measuring, 0);
            }
        }

        private void LockWork()
        {
            lock (_gate)
            {
                if (_disposed || _lock is not null) return;
                FileStream? fs = null;
                try
                {
                    fs = new FileStream(_work, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                    fs.Lock(0, long.MaxValue / 2);
                    _lock = fs;
                }
                catch (Exception ex)
                {
                    fs?.Dispose();
                    Logger.LogError("고치기 취소: 작업 사본을 잠그지 못함(이 단계가 끝난 뒤 멈춤): " + _work, ex);
                }
            }
        }

        public void Dispose()
        {
            lock (_gate)
            {
                if (_disposed) return;
                _disposed = true;
            }
            _registration.Dispose();
            _timer?.Dispose();
            lock (_gate)
            {
                try { _lock?.Dispose(); }
                catch { /* 닫기 실패는 무시 */ }
                _lock = null;
            }
        }
    }

    /// <summary>7z 안 이름: '\' 구분(7-Zip·반디집과 같은 모양).</summary>
    private static string ToSevenZipName(string key) => key.Replace('/', '\\');

    /// <summary>
    /// 새로 만들 폴더 항목(7-Zip·반디집 7z 처럼 폴더도 항목으로): 새 폴더·넣은 빈 폴더와 그 위 폴더, 넣은 폴더 안 파일의 위 폴더(넣기 대상 폴더보다 아래만) —
    /// 이미 폴더 항목으로 있는 것은 뺀다. 위 폴더부터.
    /// </summary>
    private static List<string> NewDirectories(EditPlanner<Entry> planner, Dictionary<string, string> addFolders)
    {
        var existing = new HashSet<string>(planner.Kept.Where(i => i.IsDirectory).Select(i => i.Key), StringComparer.OrdinalIgnoreCase);
        var result = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        void AddWithParents(string key, string floor)
        {
            var chain = new List<string>();
            for (string k = key; k.Length > floor.Length && k.Length > 0; k = EditPlanner<Entry>.ParentOf(k))
                chain.Add(k);
            chain.Reverse();
            foreach (var k in chain)
                if (!existing.Contains(k) && seen.Add(k)) result.Add(k);
        }
        foreach (var dir in planner.AddedDirectories)
        {
            // 넣기의 빈 폴더면 그 넣기 대상 폴더 아래까지만, 새 폴더면 없는 위 폴더 모두
            string floor = addFolders.TryGetValue("dir:" + dir, out var target) ? target : string.Empty;
            AddWithParents(dir, floor);
        }
        foreach (var a in planner.AddedFiles)
        {
            if (!addFolders.TryGetValue(a.TempKey, out var target)) continue;
            string parent = EditPlanner<Entry>.ParentOf(a.Key);
            if (parent.Length > target.Length) AddWithParents(parent, target);
        }
        return result;
    }

    /// <summary>남길 항목(원래 차례)을 지운 뒤 목록의 번호로 — 같은 이름이 여럿이면 나온 차례대로.</summary>
    private static Dictionary<EditPlanner<Entry>.Item, int> MatchKept(EditPlanner<Entry> planner, List<Entry> current)
    {
        var byName = new Dictionary<string, Queue<int>>(StringComparer.Ordinal);
        foreach (var e in current.OrderBy(e => e.Index))
        {
            if (!byName.TryGetValue(e.Key, out var q)) byName[e.Key] = q = new Queue<int>();
            q.Enqueue(e.Index);
        }
        var result = new Dictionary<EditPlanner<Entry>.Item, int>();
        foreach (var it in planner.Items.Where(i => !i.Removed).OrderBy(i => i.Source.Index))
        {
            if (!byName.TryGetValue(it.OriginalKey, out var q) || q.Count == 0)
                throw new InvalidDataException("고친 7z 에서 항목을 찾지 못함: " + it.OriginalKey);
            result[it] = q.Dequeue();
        }
        return result;
    }

    /// <summary>7z.dll 로 목록을 읽는다(번호·이름·폴더·크기·암호·CRC·방식·솔리드).</summary>
    private static (List<Entry> Entries, bool Solid) ReadEntries(string path, string? password, bool headerEncrypted)
    {
        try
        {
            using var x = string.IsNullOrEmpty(password)
                ? new SharpSevenZipExtractor(path, InArchiveFormat.SevenZip)
                : new SharpSevenZipExtractor(path, password, InArchiveFormat.SevenZip);
            var list = x.ArchiveFileData.Select(f => new Entry(
                f.Index,
                EditPlanner<Entry>.Normalize(f.FileName ?? string.Empty),
                f.IsDirectory,
                (long)f.Size,
                f.Encrypted,
                f.Crc,
                f.LastWriteTime == default ? null : f.LastWriteTime,
                f.Method ?? string.Empty)).ToList();
            return (list, IsSolid(x));
        }
        catch (Exception ex) when (ex is not ArchiveException and not OperationCanceledException)
        {
            if (ex is SharpSevenZipLibraryException) throw new EngineUnavailableException(ex);
            if (ex is IOException or UnauthorizedAccessException) throw;
            if (headerEncrypted && !string.IsNullOrEmpty(password)) throw new WrongPasswordException(isHeaderEncrypted: true, inner: ex);
            throw new CorruptArchiveException(ex);
        }
    }

    /// <summary>
    /// 솔리드인가. 파일 내용이 하나도 없는 7z(빈 7z — 일집으로 모두 지운 결과 32바이트 포함, 빈 폴더만 있는 7z)는 7z.dll 이 솔리드 정보를 주지 않아
    /// SharpSevenZip 의 IsSolid 가 InvalidOperationException(Nullable object must have a value)을 낸다 — 그때는 솔리드 아님
    /// (W2-06 검증 지적 2차: 멀쩡한 빈 7z 가 '손상됐어요'로 보여 넣기·새 폴더를 못 함).
    /// </summary>
    private static bool IsSolid(SharpSevenZipExtractor x)
    {
        try { return x.IsSolid; }
        catch (InvalidOperationException) { return false; }
    }

    /// <summary>
    /// 파일 이름은 보이고 내용만 암호인 7z: 암호 확인값이 없어 가장 작은 암호 파일 하나를 풀어 CRC 로 본다(틀리면 <see cref="WrongPasswordException"/>).
    /// 틀린 암호로 고치기를 시작하면 7z.dll 이 솔리드 묶음을 다시 쓰다 실패하거나, 이름만 바꾸는 경우 끝 검사에서야 드러난다.
    /// </summary>
    private static void VerifyPassword(string path, string password, List<Entry> entries, CancellationToken ct)
    {
        var target = entries.Where(e => e.Encrypted && !e.IsDirectory && e.Size > 0).OrderBy(e => e.Size).FirstOrDefault();
        if (target is null) return;
        ct.ThrowIfCancellationRequested();
        var crc = new Crc32();
        try
        {
            // 솔리드면 그 파일 앞까지 풀어야 해서 오래 걸릴 수 있다 — 우리 읽기 스트림으로 넘겨 읽을 때마다 취소를 본다
            using var input = new CancellableSeekableReadStream(
                new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 1 << 16), ct);
            using var x = new SharpSevenZipExtractor(input, password, leaveOpen: true, InArchiveFormat.SevenZip);
            using var sink = new CrcSink(crc, ct);
            x.ExtractFile(target.Index, sink);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex) when (ex is not ArchiveException)
        {
            ct.ThrowIfCancellationRequested();   // 읽기에서 난 취소는 7z.dll 을 거쳐 다른 예외로 돌아온다
            if (ex is SharpSevenZipLibraryException) throw new EngineUnavailableException(ex);
            throw new WrongPasswordException(inner: ex, entryPath: target.Key, mayBeCorrupt: true);
        }
        if (target.Crc != 0 && (uint)crc.Value != target.Crc)
            throw new WrongPasswordException(entryPath: target.Key, mayBeCorrupt: true);
    }

    /// <summary>쓰는 바이트의 CRC 만 세는 스트림(암호 확인용).</summary>
    private sealed class CrcSink : Stream
    {
        private readonly Crc32 _crc;
        private readonly CancellationToken _ct;
        private long _length;

        public CrcSink(Crc32 crc, CancellationToken ct)
        {
            _crc = crc;
            _ct = ct;
        }

        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => _length;
        public override long Position { get => _length; set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count)
        {
            _ct.ThrowIfCancellationRequested();
            _crc.Update(new ArraySegment<byte>(buffer, offset, count));
            _length += count;
        }
    }

    /// <summary>
    /// 7z.dll 이 실패를 알렸을 때 진짜 원인(<see cref="SevenZipArchiveService"/> 와 같은 규칙): 취소, 원본 문제(그 파일을 빼고 다시), 7z.dll 없음, 디스크 가득.
    /// </summary>
    private static Exception Classify(Exception ex, CancellationToken ct, SourceFailure failure)
    {
        if (ex is OperationCanceledException || ct.IsCancellationRequested)
            return ex as OperationCanceledException ?? new OperationCanceledException("고치기를 취소했어요.", ex, ct);
        if (failure.First is { } source) return source;
        if (ex is ArchiveException) return ex;
        if (ex is SharpSevenZipLibraryException) return new EngineUnavailableException(ex);
        if (ex is SharpSevenZipException && ex.Message.Contains("0x80070070", StringComparison.OrdinalIgnoreCase))
            return new IOException("디스크 공간이 모자라요.", ex) { HResult = unchecked((int)0x80070070) };
        if (ex is SharpSevenZipException) return new ArchiveEditException(EngineFailedMessage, ex);   // 영어 엔진 문구 대신(원래부터 손상인지는 RunOnce 가 가림)
        return ex;
    }

    private sealed class SourceFailure
    {
        private SourceUnreadableException? _first;

        public SourceUnreadableException? First => Volatile.Read(ref _first);

        public void Record(SourceUnreadableException ex) => Interlocked.CompareExchange(ref _first, ex, null);
    }

    private static DateTime? SafeTime(Func<DateTime> get)
    {
        try { return get(); }
        catch { return null; }
    }
}
