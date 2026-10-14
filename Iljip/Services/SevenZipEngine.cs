using System.IO;
using Iljip.Models;
using SharpSevenZip;
using SharpSevenZip.Exceptions;

namespace Iljip.Services;

/// <summary>
/// 7z.dll(SharpSevenZip) 공통 엔진 — 7z·RAR·zipx·ISO·CAB·LZH·WIM·xz·자동 풀림 exe 안의 7z·RAR 의 목록·해제를 맡는다(W2-01, 1차 W1-B 재작업).
///
/// 열기: 우리가 연 스트림(FileShare.Read)을 넘기고 finally 에서 반드시 닫는다(실패해도 핸들이 남지 않음 — B8 계약). 형식은 늘 못박는다(공격면 축소).
/// 그 스트림이 읽을 때마다 취소를 확인해 큰 파일 한가운데서도 멈추고(<see cref="CancellableSeekableReadStream"/>), 진행률은 입력 위치로 센다.
/// RAR 여러 조각만 경로 생성자(조각 자동 탐색, 취소는 파일 단위).
/// 열 때(목록·풀기 모두) 먼저 형식별 구조 검사(<see cref="ArchiveCompleteness"/>)로 끝이 잘렸는지 보고, 7z.dll 이 알린 압축 단위 오류 표시
/// (ErrorFlags — 끝 잘림·머리 손상)와 '조각 없음'(ErrorMessage 'Missing volume')도 본다 — 7z.dll 은 머리 사이에서 잘린 RAR4·LZH 를
/// '읽은 데까지만' 열어 뒤쪽 항목이 목록에서 조용히 사라졌다(1차 검증 지적 (a)).
///
/// 해제: '임시 폴더 → 검증 → 옮기기' 한 길.
/// - 대상 폴더 안의 숨김 임시 폴더(<see cref="StagingFolder"/>)에 푼다. 7z.dll 은 오류를 해제가 다 끝난 뒤 한꺼번에 알리고, ZIP 암호가 틀리면
///   모든 항목을 0바이트로 만들어 둔다(실측) → 대상에 바로 풀면 틀린·절반짜리 파일이 남는다.
/// - 항목마다 검증(<see cref="ExtractionVerifier"/> — 있는가·크기·필요하면 확인값). 모두 맞으면 옮긴다(같은 이름이 있으면 "이름 (1).ext").
/// - 하나라도 틀리면(손상·잘림·암호 오류) 아무것도 옮기지 않고 임시 폴더째 지운 뒤 알린다(전부 아니면 전무). 이번에 만든 빈 대상 폴더도 지운다.
///   단 '한 파일의 확인값(CRC) 오류'는 main 의 ZIP·RAR 계약대로 그 앞에서 맞게 풀린(검증된) 파일만 옮기고 <see cref="ChecksumMismatchException"/>.
/// - 취소: 끝났고 검증된 파일만 옮기고 나머지는 지운 뒤 <see cref="OperationCanceledException"/>(CancellationTests 계약).
/// 쓰기 전에 경로 공격(대상 밖)과 '암호 항목인데 암호 없음'을 먼저 막는다.
/// Windows 가 그대로 만들 수 없는 이름(장치 이름 'CON.txt'·점만 있는 폴더 '...')과 이름 없는 단일 스트림(xz)은 7z.dll 의 한꺼번에 풀기에서 빼고
/// 하나씩 스트림으로 꺼낸 뒤 바꾼 이름(<see cref="EntryPaths"/>)으로 옮긴다.
/// ★ <c>ExtractFiles(ExtractFileCallback)</c> 은 쓰지 않는다 — 솔리드 압축에서 'Solid archives are not supported' 를 던지고 LZH·XZ 도 솔리드로 친다(실측).
/// </summary>
internal static class SevenZipEngine
{
    /// <summary>대상 폴더 안에 만드는 임시 폴더 이름 머리(숨김) — <see cref="StagingFolder.Prefix"/>.</summary>
    internal const string StagingPrefix = StagingFolder.Prefix;

    /// <summary>임시 폴더 안에서 '따로 꺼낸' 항목(<see cref="Item.Separate"/>)을 두는 폴더 이름 머리(뒤에 무작위 8글자).</summary>
    private const string SeparatePrefix = ".일집-따로-";

    /// <summary>열기·해제에 필요한 것.</summary>
    internal sealed class Source
    {
        public required string ArchivePath { get; init; }

        /// <summary>7z.dll 형식(못박음). RAR 은 서명으로 RAR4/RAR5 를 골라 둔다.</summary>
        public required InArchiveFormat Format { get; init; }

        /// <summary>
        /// <see cref="Format"/> 으로 열지 못하면(손상으로 보임) 한 번 더 열어 볼 형식. 예: ISO9660+UDF 다리 이미지를 UDF 로 먼저 열고,
        /// UDF 쪽이 깨졌으면 ISO9660 으로(7-Zip·반디집처럼 UDF 우선).
        /// </summary>
        public InArchiveFormat? FallbackFormat { get; init; }

        /// <summary>입력 스트림을 여는 함수(호출마다 새로 열어 소유권을 넘김). null 이면 경로 생성자(RAR 여러 조각).</summary>
        public Func<Stream>? OpenStream { get; init; }

        /// <summary>헤더(파일 이름) 암호인지 — <see cref="ArchiveHeaderProbe"/> 결과.</summary>
        public bool HeaderEncrypted { get; init; }

        /// <summary>
        /// 7z.dll 목록 → (index → 화면·해제에 쓸 이름). ZIPX·RAR4 의 날 이름 바이트를 SharpCompress 로 읽어 한글 판별로 덮어쓸 때(못 맞추면 null).
        /// </summary>
        public Func<IReadOnlyList<ArchiveFileInfo>, IReadOnlyDictionary<int, string>?>? NameResolver { get; init; }

        /// <summary>열기·해제 실패를 '조각 빠짐·덜 받아짐'으로 바꿔 줄 검사(없으면 null). RAR 여러 조각용.</summary>
        public Func<ArchiveException?>? VolumeCheck { get; init; }

        /// <summary>열기 전에 할 형식별 구조 검사(<see cref="ArchiveCompleteness"/>).</summary>
        public StructureKind Structure { get; init; }

        /// <summary>이름 없는 단일 스트림(xz) — 한꺼번에 풀기 대신 스트림으로 꺼낸다(7z.dll 의 임시 이름 규칙에 기대지 않게).</summary>
        public bool SingleStream { get; init; }
    }

    /// <summary>7z.dll 항목 하나와 우리가 정한 이름들.</summary>
    internal sealed record Item(
        int Index,
        string Path,          // 화면·선택 비교용 이름('/' 구분, 끝 '/' 없음)
        string StagingRel,    // 7z.dll 이 임시 폴더에 쓰는 상대 경로(SharpSevenZip 규칙 그대로)
        string TargetRel,     // 대상 폴더에 옮길 상대 경로(Path 를 EntryPaths 규칙으로 — 금지 문자 '_', 장치 이름 앞 '_' 등)
        bool IsDirectory,
        long Size,
        bool Encrypted,
        DateTime? Modified,
        string Method,
        uint Crc,             // 7z.dll 이 주는 확인값(LZH 는 CRC-16, 나머지는 CRC32 — 없으면 0)
        bool Separate);       // 한꺼번에 풀기에서 빼고 스트림으로 따로 꺼낸다(Windows 가 그 이름을 못 만듦·단일 스트림)

    // ───────────────────────────── 목록 ─────────────────────────────

    public static IReadOnlyList<ArchiveEntry> List(Source src, string? password, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        using var session = Session.Open(src, password, ct, null);
        return session.Items.Select(i => new ArchiveEntry
        {
            Path = i.Path,
            Size = i.Size,
            CompressedSize = 0,   // 7z.dll 은 항목별 압축 크기를 주지 않는다(SharpCompress 7z 와 같게 0)
            IsDirectory = i.IsDirectory,
            LastModified = i.Modified,
        }).ToList();
    }

    // ───────────────────────────── 해제 ─────────────────────────────

    public static void Extract(
        Source src,
        string destinationFolder,
        Func<string, bool> shouldExtract,
        string? password,
        IProgress<ArchiveProgress>? progress,
        CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        string destFull = Path.GetFullPath(destinationFolder);
        string destRoot = destFull.EndsWith(Path.DirectorySeparatorChar) ? destFull : destFull + Path.DirectorySeparatorChar;

        var tracker = new ProgressTracker(progress);
        using var session = Session.Open(src, password, ct, tracker.OnPosition);
        var selected = session.Items.Where(i => shouldExtract(i.Path)).ToList();

        // 경로 공격 사전 검사: 7z.dll 도 자체 검사를 하지만 결과를 모아 나중에 던지므로 쓰기 전에 우리가 먼저 막는다.
        // 원래 이름과 금지 문자를 바꾼 이름 둘 다 대상 폴더 안이어야 한다.
        foreach (var it in selected)
        {
            if (!IsInside(destRoot, it.Path.Replace('/', Path.DirectorySeparatorChar)) || !IsInside(destRoot, it.TargetRel))
                throw new InvalidDataException(ErrorMessages.UnsafePathPrefix + it.Path);
        }

        // 암호 사전 검사: 암호 걸린 파일을 골랐는데 암호가 없으면 7z.dll 을 부르지 않는다(py7zr 는 폴더에도 암호 표시를 해서 파일만 본다).
        if (string.IsNullOrEmpty(password) && selected.Any(i => !i.IsDirectory && i.Encrypted && i.Size > 0))
            throw PasswordRequiredException.For(src.HeaderEncrypted);

        var files = selected.Where(i => !i.IsDirectory).ToList();
        tracker.SetTotals(files.Sum(f => f.Size), files.Count);
        // 대상 폴더(없던 조상까지)를 장부에 적으며 만든다 — 실패·취소로 아무것도 옮기지 못했으면 이번에 만든 빈 폴더를 지운다
        // (암호 오류에 새 폴더가 남지 않게 — RarTests·PasswordTests 계약, main 의 ExtractionLedger 규칙과 같음).
        var created = new ExtractionLedger();
        created.EnsureDirectory(destFull);
        try
        {
            if (selected.Count == 0)
            {
                tracker.Final();
                return;
            }

            var format = session.Format;   // 실제로 연 형식(대신 형식으로 열었을 수 있음)
            using (var staging = StagingFolder.Create(destFull))
            {
                RunResult run;
                try
                {
                    run = session.Run(staging.FullPath, selected, selected.Count == session.Items.Count, tracker, ct);
                }
                finally
                {
                    // 7z.dll 이 실패한 항목의 파일을 열어 둔 채일 수 있다 → 먼저 닫아야 검증하고 옮기고 지울 수 있다.
                    session.Dispose();
                }
                Finish(src, format, destFull, staging.FullPath, selected, files, run, ct);
            }
            tracker.Final();
        }
        catch
        {
            // 임시 폴더는 위 using 이 이미 지웠다. 장부의 폴더는 비어 있을 때만 지워진다(옮긴 파일·원래 있던 것은 그대로).
            created.Rollback();
            throw;
        }
    }

    /// <summary>검증하고, 다 맞으면 옮기고, 아니면 알맞은 예외를 던진다(옮기지 않음 — 임시 폴더는 호출자가 지움).</summary>
    private static void Finish(Source src, InArchiveFormat format, string dest, string staging, List<Item> selected, List<Item> files,
        RunResult run, CancellationToken ct)
    {
        string StagingPath(Item f) => run.StagingOverride.TryGetValue(f.Index, out var o) ? o : Path.Combine(staging, f.StagingRel);

        var verdicts = files.ToDictionary(f => f.Index, f => ExtractionVerifier.Check(format, f, StagingPath(f), run.Finished.Contains(f.Index)));
        bool Ok(Item f) => verdicts[f.Index] == ExtractionVerifier.Problem.None;

        // RAR4: 풀린 파일의 수정 시각을 압축에 적힌 벽시계로(목록과 같은 값 — 7z.dll 이 '지금' 시간대 편차로 바꾼 시각을 되돌린 것, DosWallClock).
        // 옮기기 전에 임시 폴더 안에서 맞춘다(옮겨도 시각은 그대로, 같은 이름 묻는 창도 이 시각을 보임).
        if (format == InArchiveFormat.Rar4)
        {
            foreach (var f in files)
            {
                if (f.Modified is not { } wall || !Ok(f) || !run.Finished.Contains(f.Index)) continue;
                try { File.SetLastWriteTime(StagingPath(f), wall); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentOutOfRangeException)
                {
                    Logger.LogError("RAR4 풀린 파일 시각 맞추기 실패(내용은 그대로): " + f.Path, ex);
                }
            }
        }

        if (ct.IsCancellationRequested)
        {
            // 취소: 끝났고 맞는 파일만 옮긴다(쓰다 만 파일은 임시 폴더째 지워짐)
            StagingFolder.Commit(dest, files.Where(f => Ok(f) && run.Finished.Contains(f.Index)).Select(f => (StagingPath(f), f.TargetRel)));
            throw new OperationCanceledException(ct);
        }

        var failed = files.Where(f => !Ok(f)).ToList();
        if (failed.Count == 0)
        {
            if (run.Error is not null)
                Logger.LogError($"7z.dll 이 오류를 알렸지만 모든 파일이 크기·확인값까지 맞음 — 성공으로 처리: {src.ArchivePath}", run.Error);
            StagingFolder.Commit(dest,
                files.Select(f => (StagingPath(f), f.TargetRel)),
                selected.Where(i => i.IsDirectory).Select(i => i.TargetRel));
            return;
        }

        var err = run.Error;
        string text = err?.ToString() ?? string.Empty;

        // 출력 쪽 문제(디스크 가득·권한·경로)는 엔진 오류가 아니라 그대로 알린다.
        if (err is IOException or UnauthorizedAccessException && err is not FileNotFoundException)
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw(err);

        var first = failed[0];
        // 크기·확인값이 다르다 = 그 파일의 내용이 틀림(7z.dll 이 '끝냈다'고 했거나, 암호 없는 ZIP·RAR 이 끝까지 나왔는데 CRC 만 다름).
        // main 의 ZIP·RAR 계약(W1-C, RarTests): 깨진 그 파일은 버리고 그 앞에서 끝까지 맞게 풀린 파일은 남긴다(목록 순서 — 검증을 통과한 것만).
        if (verdicts[first.Index] is ExtractionVerifier.Problem.SizeMismatch or ExtractionVerifier.Problem.ChecksumMismatch)
        {
            StagingFolder.Commit(dest, files.TakeWhile(f => f.Index != first.Index).Where(Ok).Select(f => (StagingPath(f), f.TargetRel)));
            throw new ChecksumMismatchException(first.Path);
        }

        // 암호 문제: 실패한 항목 중 암호 항목이 있거나 7z.dll 이 '암호 틀림'을 알렸다.
        // 단, 암호가 맞다고 이미 드러났으면 손상이다(맞는 암호를 넣고도 '암호가 맞지 않아요'를 끝없이 되묻지 않게):
        //  - 헤더 암호 압축을 이 암호로 열었다(틀린 암호면 목록부터 못 연다 — Session.Open),
        //  - 이번에 같은 암호로 다른 암호 파일(크기 > 0)이 확인값까지 맞게 풀렸다.
        // 7z·RAR4 는 암호 확인값이 없어(AES + CRC) 그 밖의 경우엔 틀린 암호와 손상을 가를 수 없다 → '손상일 수도' 덧말(7z.dll 이 'Wrong password.' 로
        // 딱 잘라 알린 경우 — ZIP AES·RAR5 확인값 — 만 덧말 없이).
        bool definiteWrong = text.Contains("Wrong password.", StringComparison.Ordinal);
        if (definiteWrong || failed.Any(f => f.Encrypted))
        {
            bool passwordProven = src.HeaderEncrypted || files.Any(f => f.Encrypted && f.Size > 0 && Ok(f));
            if (!passwordProven || definiteWrong && !src.HeaderEncrypted)
                throw new WrongPasswordException(src.HeaderEncrypted, err, first.Path, mayBeCorrupt: !definiteWrong);
        }

        if (src.VolumeCheck?.Invoke() is { } volume) throw volume;

        if (text.Contains("Unsupported method", StringComparison.OrdinalIgnoreCase))
            throw new UnsupportedMethodException(string.IsNullOrEmpty(first.Method) ? "알 수 없음" : first.Method, err, first.Path);
        if (text.Contains("Unexpected end", StringComparison.OrdinalIgnoreCase))
            throw CorruptArchiveException.Truncated(err, first.Path);
        throw new CorruptArchiveException(err, first.Path);
    }

    private static bool IsInside(string destRoot, string relative)
    {
        try
        {
            string full = Path.GetFullPath(Path.Combine(destRoot, relative));
            return full.StartsWith(destRoot, StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    // ─────────────────────────── 이름 규칙 ───────────────────────────

    /// <summary>
    /// SharpSevenZip(ArchiveExtractCallback.RemoveIllegalCharacters)이 파일을 쓸 때 쓰는 이름 규칙을 그대로 따른다:
    /// '/'→'\', 조각마다 파일 이름 금지 문자를 '_' 로, '\\' 로 시작하면 그 머리를 살림. 임시 폴더 안 위치를 계산하는 데 쓴다.
    /// </summary>
    internal static string SanitizeLikeSharpSevenZip(string name)
    {
        string s = name.Replace('/', '\\');
        var parts = new List<string>(s.Split('\\'));
        var invalid = Path.GetInvalidFileNameChars();
        for (int i = 0; i < parts.Count; i++)
        {
            if (string.IsNullOrEmpty(parts[i])) continue;
            foreach (char c in invalid) parts[i] = parts[i].Replace(c, '_');
        }
        if (s.StartsWith(@"\\", StringComparison.Ordinal) && parts.Count >= 3)
        {
            parts.RemoveAt(0);
            parts.RemoveAt(0);
            parts[0] = @"\\" + parts[0];
        }
        return string.Join('\\', parts);
    }

    private static List<Item> BuildItems(Source src, IReadOnlyList<ArchiveFileInfo> data, DateTime openedFrom, DateTime openedTo, InArchiveFormat format)
    {
        IReadOnlyDictionary<int, string>? overrides = null;
        if (src.NameResolver is not null)
        {
            try
            {
                overrides = src.NameResolver(data);
            }
            catch (Exception ex)
            {
                Logger.LogError("압축 안 이름 대조 실패(7z.dll 이름 그대로 씀): " + src.ArchivePath, ex);
            }
        }

        var items = new List<Item>(data.Count);
        foreach (var afi in data)
        {
            string raw = afi.FileName ?? string.Empty;
            bool noName = raw.Length == 0 || raw.StartsWith("[no name]", StringComparison.Ordinal);
            string display;
            if (overrides is not null && overrides.TryGetValue(afi.Index, out var over) && !string.IsNullOrEmpty(over))
                display = over;
            else if (noName)
                display = data.Count == 1 ? ArchiveNaming.SingleStreamName(src.ArchivePath) : $"{ArchiveNaming.FallbackName} {afi.Index}";
            else
                display = raw;
            // W2-09: 맥 ZIP 의 NFD 한글 이름은 NFC 로 보이고 NFC 로 푼다(임시 폴더 안 자리는 7z.dll 이 쓰는 원래 이름 그대로)
            display = EntryNames.Nfc(display.Replace('\\', '/').TrimEnd('/'));

            // 시각이 없는 항목(xz·ISO 폴더)은 SharpSevenZip 이 '지금'을 채운다 → 여는 동안의 시각이면 '모름'으로 둔다.
            DateTime t = afi.LastWriteTime;
            DateTime? modified = t >= openedFrom.AddSeconds(-2) && t <= openedTo.AddSeconds(2) ? null : t;
            // RAR4 는 DOS 벽시계 — 7z.dll 이 '지금' 시간대 편차로 UTC 로 바꾼 것을 되돌려 적힌 그대로(<see cref="DosWallClock"/>, SharpCompress 길과 같게).
            if (modified is { } m && format == InArchiveFormat.Rar4) modified = DosWallClock.FromSevenZip(m);

            string stagingRel = SanitizeLikeSharpSevenZip(noName ? $"[no name] {afi.Index}" : raw);
            bool separate = src.SingleStream || noName || EntryPaths.HasUnrepresentableComponent(stagingRel);
            items.Add(new Item(
                afi.Index, display, stagingRel, EntryPaths.ToSafeRelative(display),
                afi.IsDirectory, (long)afi.Size, afi.Encrypted, modified, afi.Method ?? string.Empty, afi.Crc,
                separate));
        }
        return items;
    }

    // ─────────────────────────── 열기·실행 ───────────────────────────

    private sealed record RunResult(HashSet<int> Started, HashSet<int> Finished, Dictionary<int, string> StagingOverride, Exception? Error);

    /// <summary>연 압축 하나(7z.dll 추출기 + 우리가 소유한 입력 스트림). Dispose 에서 모두 닫는다.</summary>
    private sealed class Session : IDisposable
    {
        private readonly SharpSevenZipExtractor _extractor;
        private readonly Stream? _input;
        public List<Item> Items { get; }

        /// <summary>실제로 연 형식(<see cref="Source.FallbackFormat"/> 으로 열었으면 그것).</summary>
        public InArchiveFormat Format { get; }

        private Session(SharpSevenZipExtractor extractor, Stream? input, List<Item> items, InArchiveFormat format)
        {
            _extractor = extractor;
            _input = input;
            Items = items;
            Format = format;
        }

        public static Session Open(Source src, string? password, CancellationToken ct, Action<long, long>? onPosition)
        {
            SevenZipLibrary.ThrowIfUnavailable();
            // 헤더 암호인데 암호가 없으면 7z.dll 을 부르지 않는다(부르면 손상과 같은 오류만 돌아옴). 목록까지 잠긴 압축 — 화면이 열기 단계에서 묻는다(I1-7, W2-02).
            if (src.HeaderEncrypted && string.IsNullOrEmpty(password))
                throw new ListPasswordRequiredException();
            CheckStructure(src);
            try
            {
                return OpenAs(src, src.Format, password, ct, onPosition);
            }
            catch (CorruptArchiveException first) when (src.FallbackFormat is not null && !ct.IsCancellationRequested)
            {
                try
                {
                    return OpenAs(src, src.FallbackFormat.Value, password, ct, onPosition);
                }
                catch (CorruptArchiveException)
                {
                    // 대신 형식으로도 못 열면 처음 형식의 오류를 알린다
                    System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw(first);
                    throw;
                }
            }
        }

        /// <summary>형식별 구조 검사 — 끝이 잘렸거나 조각이 빠졌으면 7z.dll 을 부르기 전에 알린다(목록 단계에서도).</summary>
        private static void CheckStructure(Source src)
        {
            ArchiveException? problem;
            if (src.OpenStream is null)
            {
                problem = src.Structure == StructureKind.Rar ? ArchiveCompleteness.CheckRarVolumes(src.ArchivePath) : null;
            }
            else
            {
                if (src.Structure == StructureKind.None) return;
                using var s = src.OpenStream();
                problem = ArchiveCompleteness.Check(s, src.Structure);
            }
            if (problem is not null) throw problem;
        }

        private static Session OpenAs(Source src, InArchiveFormat format, string? password, CancellationToken ct, Action<long, long>? onPosition)
        {
            bool hasPassword = !string.IsNullOrEmpty(password);
            Stream? input = null;
            SharpSevenZipExtractor? extractor = null;
            try
            {
                if (src.OpenStream is not null)
                {
                    input = new CancellableSeekableReadStream(src.OpenStream(), ct, onPosition);
                    if (input.Length == 0) throw new CorruptArchiveException();   // SharpSevenZip 은 길이 0 스트림을 ArgumentException 으로 거절
                    extractor = new SharpSevenZipExtractor(input, password ?? string.Empty, leaveOpen: true, format);
                }
                else
                {
                    extractor = hasPassword
                        ? new SharpSevenZipExtractor(src.ArchivePath, password!, format)
                        : new SharpSevenZipExtractor(src.ArchivePath, format);
                }
                // ★ 보안 결정 보류(보고 안건): 인터넷에서 받은 압축의 '웹에서 받은 파일 표시(MOTW)'를 풀린 파일에 옮기지 않는다.
                //   다른 엔진(SharpCompress·EggDotNet)도 옮기지 않아 동작을 맞춘 것. 켜면 Office 보호된 보기가 살아나지만,
                //   재사용장치(엑셀 COM 으로 여는 자동 처리)에 영향이 있을 수 있어 사용자 결정 전까지 그대로 둔다(KnownGapTests 기록).
                extractor.PropagateMarkOfTheWeb = false;
                var from = DateTime.Now;
                var data = extractor.ArchiveFileData;      // 여기서 실제로 연다
                var to = DateTime.Now;
                // 항목이 하나도 없는 RAR 은 손상(머리 바로 뒤에서 잘린 RAR4 는 오류 표시 없이 '0개'로 열린다 — 실측).
                // WinRAR 은 빈 RAR 을 만들지 않는다(7z.dll 을 못 쓸 때의 RarArchiveService.OpenForRead 와 같은 규칙).
                if (data.Count == 0 && format is InArchiveFormat.Rar or InArchiveFormat.Rar4)
                    throw new CorruptArchiveException();
                if (ArchiveProblem(src, extractor, format, hasPassword) is { } problem) throw problem;
                return new Session(extractor, input, BuildItems(src, data, from, to, format), format);
            }
            catch (Exception ex)
            {
                try { extractor?.Dispose(); } catch { /* 닫기 실패는 무시 */ }
                try { input?.Dispose(); } catch { /* 닫기 실패는 무시 */ }
                throw TranslateOpen(ex, src, hasPassword, ct);
            }
        }

        /// <summary>
        /// 압축 전체의 문제(항목별 성공·실패로는 안 보이는 것) — 없으면 null. 풀기 전에(목록에서도) 알린다.
        /// - 'Missing volume : 경로'(여러 조각 RAR 의 뒤 조각 없음) → <see cref="VolumeMissingException"/>.
        /// - 끝 잘림(UnexpectedEnd) → 잘림, 머리 손상 등 → 손상. 여러 조각이면 먼저 '조각 없음'인지 본다.
        ///   ISO·UDF 는 정상 이미지에도 끝 잘림 표시가 떠서(반디집 iso_default 실측) 그 표시는 보지 않는다(구조 검사가 PVD·AVDP 로 대신 봄).
        /// </summary>
        private static ArchiveException? ArchiveProblem(Source src, SharpSevenZipExtractor extractor, InArchiveFormat format, bool hasPassword)
        {
            ArchiveErrorFlags flags;
            string? message;
            try
            {
                flags = extractor.ErrorFlags;
                message = extractor.ErrorMessage;
            }
            catch (Exception ex)
            {
                Logger.LogError("7z.dll 오류 표시를 읽지 못함(무시): " + src.ArchivePath, ex);
                return null;
            }
            if (message is { } m && m.StartsWith("Missing volume", StringComparison.OrdinalIgnoreCase))
            {
                string firstLine = m.Split('\n')[0];
                string name = firstLine.IndexOf(':') is var c and >= 0 ? Path.GetFileName(firstLine[(c + 1)..].Trim()) : string.Empty;
                return src.VolumeCheck?.Invoke() ?? new VolumeMissingException(name.Length > 0 ? name : "다음");
            }
            // 머리 바이트로 못 알아본 '목록까지 잠긴' 압축(W2-02 — 판정 2단계): 7z.dll 이 머리 암호 오류를 알리면 암호 문제로 본다
            // (7z 는 이때 열기부터 실패해 여기까지 오지 않는다 — 머리 바이트 판정이 1단계로 막는다, 실측).
            if ((flags & ArchiveErrorFlags.EncryptedHeadersError) != 0)
                return hasPassword ? new WrongPasswordException(isHeaderEncrypted: true) : new ListPasswordRequiredException();
            if (format is InArchiveFormat.Iso or InArchiveFormat.Udf)
                flags &= ~ArchiveErrorFlags.UnexpectedEnd;
            if ((flags & ArchiveErrorFlags.UnexpectedEnd) != 0)
                return src.VolumeCheck?.Invoke() ?? CorruptArchiveException.Truncated();
            const ArchiveErrorFlags damaged = ArchiveErrorFlags.IsNotArchive | ArchiveErrorFlags.HeadersError | ArchiveErrorFlags.EncryptedHeadersError
                                              | ArchiveErrorFlags.UnavailableStart | ArchiveErrorFlags.DataError | ArchiveErrorFlags.CrcError;
            if ((flags & damaged) != 0)
                return src.VolumeCheck?.Invoke() ?? new CorruptArchiveException();
            return null;
        }

        private static Exception TranslateOpen(Exception ex, Source src, bool hasPassword, CancellationToken ct)
        {
            if (ex is ArchiveException or OperationCanceledException) return ex;
            if (ct.IsCancellationRequested) return new OperationCanceledException(ct);
            if (ex is SharpSevenZipLibraryException) return new EngineUnavailableException(ex);
            if (src.VolumeCheck?.Invoke() is { } volume) return volume;
            if (src.HeaderEncrypted && hasPassword) return new WrongPasswordException(isHeaderEncrypted: true, ex);
            // 원본 파일 자체를 못 연 경우(잠김·권한·없음)는 엔진 문제가 아니므로 그대로
            if (ex is IOException or UnauthorizedAccessException) return ex;
            // "Invalid archive: open/read error! Is it encrypted…", 서명 없음(ArgumentException) 등 → 손상
            return new CorruptArchiveException(ex);
        }

        /// <summary>
        /// 임시 폴더에 푼다(전부면 ExtractArchive, 일부면 ExtractFiles(폴더, 번호들) — 솔리드에서도 됨). 엔진 예외는 잡아서 결과에 담는다.
        /// <see cref="Item.Separate"/> 항목은 한꺼번에 풀기에서 건너뛰고(e.Skip), 끝난 뒤 하나씩 스트림으로 꺼내 임시 폴더 안의
        /// 안전한 이름(<see cref="SeparatePrefix"/>)에 둔다 — 솔리드 압축이면 앞 항목을 다시 풀어야 해 느리지만, 이런 이름은 드물다.
        /// </summary>
        public RunResult Run(string staging, List<Item> selected, bool all, ProgressTracker tracker, CancellationToken ct)
        {
            var byIndex = Items.ToDictionary(i => i.Index);
            var selectedSet = selected.Select(i => i.Index).ToHashSet();
            var separateSet = selected.Where(i => i.Separate).Select(i => i.Index).ToHashSet();
            var started = new HashSet<int>();
            var finished = new HashSet<int>();
            var stagingOverride = new Dictionary<int, string>();
            (string Original, string Renamed)? pendingRename = null;
            var gate = new object();   // 7z.dll 콜백이 다른 스레드에서 올 수 있어 기록은 잠그고 한다

            // 같은 이름이 둘 든 압축: 두 번째를 임시 폴더 안에서 다른 이름으로 받아 두고, 대상으로 옮길 때 다시 비켜 쓴다.
            _extractor.FileExists += (_, e) =>
            {
                lock (gate)
                {
                    string renamed = UniqueName(e.FileName);
                    pendingRename = (e.FileName, renamed);
                    e.FileName = renamed;
                }
            };
            _extractor.FileExtractionStarted += (_, e) =>
            {
                int idx = e.FileInfo.Index;
                if (ct.IsCancellationRequested) e.Cancel = true;   // 경로 생성자(RAR 여러 조각)는 이것이 유일한 취소 — 파일 단위
                if (separateSet.Contains(idx))
                {
                    e.Skip = true;   // 7z.dll 이 그 이름을 만들지 않게(아래에서 따로 꺼냄)
                    return;
                }
                if (!selectedSet.Contains(idx) || !byIndex.TryGetValue(idx, out var it)) return;
                lock (gate)
                {
                    started.Add(idx);
                    if (pendingRename is { } pr)
                    {
                        if (string.Equals(Path.GetFullPath(pr.Original), Path.GetFullPath(Path.Combine(staging, it.StagingRel)), StringComparison.OrdinalIgnoreCase))
                            stagingOverride[idx] = pr.Renamed;
                        pendingRename = null;
                    }
                }
                if (!it.IsDirectory) tracker.OnStarted(it.Path);
            };
            _extractor.FileExtractionFinished += (_, e) =>
            {
                int idx = e.FileInfo.Index;
                // 솔리드 일부 풀기는 고르지 않은 항목도 알린다. 건너뛴(따로 꺼낼) 항목도 '끝남'으로 알려 오므로 뺀다.
                if (!selectedSet.Contains(idx) || separateSet.Contains(idx) || !byIndex.TryGetValue(idx, out var it)) return;
                lock (gate) finished.Add(idx);
                if (!it.IsDirectory) tracker.OnFinished(it.Size);
            };

            Exception? error = null;
            bool anyBulk = selected.Any(i => !i.Separate);
            if (anyBulk)
            {
                try
                {
                    if (all) _extractor.ExtractArchive(staging);
                    else _extractor.ExtractFiles(staging, selected.Where(i => !i.Separate).Select(i => i.Index).ToArray());
                }
                catch (Exception ex)
                {
                    error = ex;
                }
            }

            // 따로 꺼낼 파일(폴더는 대상에 바꾼 이름으로 만들기만 하면 됨)
            var separateFiles = selected.Where(i => i.Separate && !i.IsDirectory).ToList();
            if (separateFiles.Count > 0 && !ct.IsCancellationRequested)
            {
                string folder = Path.Combine(staging, SeparatePrefix + Guid.NewGuid().ToString("N")[..8]);
                Directory.CreateDirectory(folder);
                foreach (var it in separateFiles)
                {
                    if (ct.IsCancellationRequested) break;
                    string path = Path.Combine(folder, it.Index.ToString(System.Globalization.CultureInfo.InvariantCulture));
                    tracker.OnStarted(it.Path);
                    lock (gate) started.Add(it.Index);
                    try
                    {
                        // 쓸 때마다 취소를 확인한다(xz 처럼 입력은 다 읽고 출력만 나오는 구간에서도 멈추게 — 7z.dll 이 HRESULT 로 받아 1초 안에 멈춤, 실측)
                        using (var fs = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                            _extractor.ExtractFile(it.Index, new CancellableWriteStream(fs, ct));
                        lock (gate)
                        {
                            finished.Add(it.Index);
                            stagingOverride[it.Index] = path;
                        }
                        tracker.OnFinished(it.Size);
                    }
                    catch (Exception ex)
                    {
                        error ??= ex;
                        lock (gate) stagingOverride[it.Index] = path;   // 검증이 '끝나지 않음'으로 보게(지우는 것은 임시 폴더째)
                    }
                }
            }
            return new RunResult(started, finished, stagingOverride, error);
        }

        private static string UniqueName(string path)
        {
            string dir = Path.GetDirectoryName(path) ?? string.Empty;
            string name = Path.GetFileNameWithoutExtension(path);
            string ext = Path.GetExtension(path);
            for (int i = 1; ; i++)
            {
                string cand = Path.Combine(dir, $"{name} ({i}){ext}");
                if (!File.Exists(cand) && !Directory.Exists(cand)) return cand;
            }
        }

        private bool _disposed;

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            try { _extractor.Dispose(); } catch { /* 닫기 실패는 무시 */ }
            try { _input?.Dispose(); } catch { /* 닫기 실패는 무시 */ }
        }
    }

    // ─────────────────────────── 진행률 ───────────────────────────

    /// <summary>
    /// 진행률: 입력 위치/전체 길이 × 총량(줄어들지 않게), 파일 경계에서는 끝난 파일 크기 합으로도 올린다.
    /// 7z.dll 의 Extracting 이벤트(퍼센트만, 거칠다)는 쓰지 않는다. 읽기는 7z.dll 의 다른 스레드에서 올 수 있어 잠근다.
    /// 첫 파일이 시작되기 전(머리 읽기)의 위치는 무시한다 — 7z 머리는 파일 끝에 있어 100% 로 튀기 때문.
    /// </summary>
    internal sealed class ProgressTracker
    {
        private readonly IProgress<ArchiveProgress>? _progress;
        private readonly object _gate = new();
        private long _total;
        private int _totalFiles;
        private long _last;
        private long _done;
        private int _files;
        private string _current = string.Empty;
        private bool _active;

        public ProgressTracker(IProgress<ArchiveProgress>? progress) => _progress = progress;

        public void SetTotals(long totalBytes, int totalFiles)
        {
            lock (_gate)
            {
                _total = totalBytes;
                _totalFiles = totalFiles;
            }
        }

        public void OnPosition(long position, long length)
        {
            if (_progress is null) return;
            lock (_gate)
            {
                if (!_active || length <= 0) return;
                Report((long)(_total * Math.Min(1.0, (double)position / length)));
            }
        }

        public void OnStarted(string path)
        {
            lock (_gate)
            {
                _active = true;
                _current = path;
                Report(_done);
            }
        }

        public void OnFinished(long size)
        {
            lock (_gate)
            {
                _files = Math.Min(_files + 1, _totalFiles);
                _done = Math.Min(_total, _done + size);
            }
        }

        private void Report(long candidate)
        {
            if (_progress is null) return;
            long v = Math.Min(_total, Math.Max(_last, Math.Max(candidate, _done)));
            _last = v;
            _progress.Report(new ArchiveProgress
            {
                CurrentFile = _current,
                BytesProcessed = v,
                TotalBytes = _total,
                FilesProcessed = _files,
                TotalFiles = _totalFiles,
            });
        }

        public void Final()
        {
            lock (_gate)
            {
                _active = false;
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
