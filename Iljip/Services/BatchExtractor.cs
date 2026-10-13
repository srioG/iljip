using System.IO;
using Iljip.Models;

namespace Iljip.Services;

/// <summary>여러 개 풀기에서 압축마다 풀 곳을 정하는 규칙(I2-6, W2-05).</summary>
public enum ExtractTargetKind
{
    /// <summary>압축 파일이 있는 폴더에 바로('여기에 풀기').</summary>
    Here,
    /// <summary>압축 파일 옆 '이름' 폴더(이미 있으면 '이름 (2)' — '"이름"\ 에 풀기').</summary>
    NamedFolder,
    /// <summary>고른 폴더 안의 '이름' 폴더(일집 안 '모두 풀기'·--extract 여러 개).</summary>
    NamedFolderIn,
    /// <summary>고른 폴더에 바로(대화형 --extract-to).</summary>
    Into,
}

/// <summary>풀 곳 규칙. <see cref="Folder"/> 는 <see cref="ExtractTargetKind.NamedFolderIn"/>·<see cref="ExtractTargetKind.Into"/> 에서만.</summary>
public sealed record ExtractTargetRule(ExtractTargetKind Kind, string? Folder = null)
{
    public static readonly ExtractTargetRule Here = new(ExtractTargetKind.Here);
    public static readonly ExtractTargetRule NamedFolder = new(ExtractTargetKind.NamedFolder);

    /// <summary>새 '이름' 폴더를 만드는 규칙인가(실패·취소로 파일이 하나도 없으면 그 폴더를 지움).</summary>
    public bool MakesNewFolder => Kind is ExtractTargetKind.NamedFolder or ExtractTargetKind.NamedFolderIn;

    /// <summary>
    /// 이 압축을 풀 곳. '이름' 폴더 규칙은 풀기 직전에 정한다(그사이 생긴 폴더와 겹치지 않게 — 기존 '"이름"\ 에 풀기'와 같음).
    /// </summary>
    public string DestinationFor(string archivePath)
    {
        string parent = Path.GetDirectoryName(archivePath) ?? Environment.CurrentDirectory;
        return Kind switch
        {
            ExtractTargetKind.Here => parent,
            ExtractTargetKind.NamedFolder => ArchiveNaming.UniqueFolder(parent, ArchiveNaming.FolderNameFor(archivePath)),
            ExtractTargetKind.NamedFolderIn => ArchiveNaming.UniqueFolder(Folder!, ArchiveNaming.FolderNameFor(archivePath)),
            _ => Folder!,
        };
    }
}

/// <summary>압축 하나를 푼 결과(여러 개 풀기).</summary>
public sealed record ArchiveOutcome(
    string ArchivePath,
    string? Destination,
    bool Succeeded,
    int Skipped,
    string? Reason = null,
    Exception? Error = null)
{
    public string Name => Path.GetFileName(ArchivePath);
}

/// <summary>여러 개 풀기 한 번의 결과.</summary>
public sealed class BatchResult
{
    public required IReadOnlyList<ArchiveOutcome> Outcomes { get; init; }

    /// <summary>진행 창 [취소] 또는 같은 이름 창 [취소]로 멈췄는가.</summary>
    public bool Cancelled { get; init; }

    /// <summary>멈춰서 손대지 않은 압축(경로).</summary>
    public IReadOnlyList<string> NotStarted { get; init; } = Array.Empty<string>();

    /// <summary>모두(시작하지 못한 것 포함).</summary>
    public int Total => Outcomes.Count + NotStarted.Count;

    public int SucceededCount => Outcomes.Count(o => o.Succeeded);

    /// <summary>화면에 보일 요약(날 오류는 '자세히'로).</summary>
    public OperationSummary ToSummary(IReadOnlyList<string>? notes = null) => new()
    {
        Title = "압축 풀기 결과",
        Total = Total,
        Succeeded = SucceededCount,
        Failures = Outcomes.Where(o => !o.Succeeded && o.Reason is not null)
            .Select(o => new OperationFailure(o.Name, o.Reason!, o.Error?.ToString()))
            .ToList(),
        NotStarted = NotStarted.Select(Path.GetFileName).Select(n => n ?? string.Empty).ToList(),
        Notes = notes ?? Array.Empty<string>(),
        Cancelled = Cancelled,
    };
}

/// <summary>
/// 여러 압축 파일을 차례로 푼다(I2-6, W2-05). 진행 창 하나에 '2/5 · b.zip' 과 전체 막대(<see cref="ArchiveProgress.BatchIndex"/>).
/// - 암호: 이 묶음에서 맞았던 암호(마지막 것부터 3개까지)를 다음 암호 압축에 먼저 조용히 써 보고, 모두 틀리면 그 파일에 대해서만 묻는다
///   ('앞에서 쓴 암호가 맞지 않아요. b.zip 암호를 넣어 주세요.') — 암호가 다른 파일이 섞여도 그 파일에서만 묻는다(그 뒤 원래 암호 파일은 다시 안 물음).
///   취소하면 그 파일만 건너뛰고 요약에 적는다. 암호는 묶음이 끝나면 지운다.
/// - 같은 이름: 처리기 하나를 묶음 전체가 같이 써서 ‘남은 파일도 똑같이’ 답이 묶음 전체에 적용된다.
/// - 오류(손상·형식 미지원 등)는 그 파일만 실패로 적고 다음 파일로 간다.
/// - 진행 창 [취소]·같은 이름 [취소]: 남은 것은 손대지 않고 멈춘다(지금 하던 압축은 서비스의 취소 규칙대로 되돌림).
/// - 돌아가는 중에도 <see cref="TryAdd"/> 로 압축을 더할 수 있다(여러 개를 골라 우클릭하면 파일마다 일집이 떠 파이프로 거의 동시에 들어옴).
/// 창(암호·진행)은 이 메서드를 부른 흐름(UI 스레드)에서만 띄운다 — 해제만 작업 스레드.
/// </summary>
public sealed class BatchExtractor
{
    /// <summary>앞에서 맞은 암호가 이 파일에 맞지 않을 때의 암호 창 문구.</summary>
    public static string ReusedPasswordWrongMessage(string archiveName) =>
        $"앞에서 쓴 암호가 맞지 않아요. {archiveName} 암호를 넣어 주세요.";

    /// <summary>암호 창을 취소해 건너뛴 파일의 이유.</summary>
    public const string PasswordSkippedReason = "암호를 넣지 않아 건너뛰었어요.";

    /// <summary>풀던 중에 취소한 파일의 이유(그 압축은 서비스 취소 규칙대로 되돌림).</summary>
    public const string CancelledReason = "풀던 중에 취소했어요.";

    private readonly ArchiveServiceLocator _locator;
    private readonly IUserInteraction _ui;
    private readonly ICollisionResolver _resolver;
    private readonly List<string> _queue = new();
    private readonly object _gate = new();
    private bool _finished;

    public BatchExtractor(ArchiveServiceLocator locator, IUserInteraction ui, ICollisionResolver resolver, ExtractTargetRule rule, IEnumerable<string> archives)
    {
        _locator = locator;
        _ui = ui;
        _resolver = resolver;
        Rule = rule;
        foreach (var a in archives) AddOne(a);
    }

    public ExtractTargetRule Rule { get; }

    /// <summary>지금까지 들어온 압축 수.</summary>
    public int Count
    {
        get { lock (_gate) return _queue.Count; }
    }

    /// <summary>돌아가는 묶음에 압축을 더한다(이미 있는 경로는 무시). 묶음이 이미 끝났으면 false(새 묶음으로 할 것).</summary>
    public bool TryAdd(IEnumerable<string> archives)
    {
        lock (_gate)
        {
            if (_finished) return false;
            foreach (var a in archives) AddOne(a);
            return true;
        }
    }

    private void AddOne(string path)
    {
        string full = Path.GetFullPath(path);
        lock (_gate)
        {
            if (!_queue.Contains(full, StringComparer.OrdinalIgnoreCase)) _queue.Add(full);
        }
    }

    /// <summary>묶음 안에서 맞았던 암호를 몇 개까지 기억해 다음 파일에 조용히 써 볼지(최근 것부터).</summary>
    private const int MaxRememberedPasswords = 3;

    /// <summary>차례로 푼다. <paramref name="session"/>(진행 창 하나)은 부르는 쪽이 띄우고 닫는다.</summary>
    public async Task<BatchResult> RunAsync(IProgressSession session)
    {
        var outcomes = new List<ArchiveOutcome>();
        // 이 묶음에서 맞았던 암호(최근 것부터) — 다음 암호 파일에 먼저 조용히 써 본다. 묶음이 끝나면 지운다.
        var knownPasswords = new List<string>();
        bool cancelled = false;
        int index = 0;
        try
        {
            while (true)
            {
                string archive;
                lock (_gate)
                {
                    if (session.CancellationToken.IsCancellationRequested) cancelled = true;
                    if (cancelled || index >= _queue.Count)
                    {
                        _finished = true;
                        break;
                    }
                    archive = _queue[index];
                }
                index++;
                var (outcome, stop, usedPassword) = await ExtractOneAsync(archive, index, session, knownPasswords);
                outcomes.Add(outcome);
                if (outcome.Succeeded && !string.IsNullOrEmpty(usedPassword))
                {
                    knownPasswords.Remove(usedPassword);
                    knownPasswords.Insert(0, usedPassword);
                    if (knownPasswords.Count > MaxRememberedPasswords) knownPasswords.RemoveAt(knownPasswords.Count - 1);
                }
                if (stop) cancelled = true;
            }
        }
        finally
        {
            lock (_gate) _finished = true;
            knownPasswords.Clear();   // 암호를 메모리에 오래 두지 않는다(설계 위험 — 묶음이 끝나면 지움)
        }

        List<string> notStarted;
        lock (_gate) notStarted = _queue.Skip(index).ToList();
        return new BatchResult { Outcomes = outcomes, Cancelled = cancelled, NotStarted = notStarted };
    }

    /// <summary>압축 하나. stop = 묶음을 멈출 취소(진행 창·같은 이름 창). Password = 맞은 암호(없으면 null).</summary>
    private async Task<(ArchiveOutcome Outcome, bool Stop, string? Password)> ExtractOneAsync(
        string archive, int index, IProgressSession session, IReadOnlyList<string> knownPasswords)
    {
        string name = Path.GetFileName(archive);
        if (!File.Exists(archive))
            return (new ArchiveOutcome(archive, null, false, 0, "파일을 찾을 수 없어요."), false, null);
        var service = _locator.ResolveByContent(archive);
        if (service is null)
        {
            var unsupported = new UnsupportedFormatException(Path.GetExtension(archive));
            return (new ArchiveOutcome(archive, null, false, 0, unsupported.Message, unsupported), false, null);
        }

        string dest = Rule.DestinationFor(archive);
        var progress = new BatchProgress(session.Progress, index, () => Count, name);
        string? password = null;
        int silentTried = 0;          // 앞에서 맞은 암호를 몇 개 써 봤나
        bool askedOnce = false;       // 이 파일에 대해 암호 창을 띄웠나
        while (true)
        {
            using var scope = ExtractionScope.Begin(_resolver);
            try
            {
                await service.ExtractAsync(archive, dest, password, progress, session.CancellationToken);
                return (new ArchiveOutcome(archive, dest, true, scope.Skipped), false, password);
            }
            catch (OperationCanceledException)
            {
                // 진행 창 [취소] 또는 같은 이름 창 [취소] — 서비스가 이미 되돌렸다(취소 규칙). 새로 만든 '이름' 폴더가 비었으면 지운다.
                CleanupNewFolder(dest);
                return (new ArchiveOutcome(archive, dest, false, scope.Skipped, CancelledReason), true, null);
            }
            catch (Exception ex) when (IsPasswordProblem(ex))
            {
                // 앞에서 맞은 암호를 먼저 조용히 써 본다(묻기 전에만)
                if (!askedOnce && silentTried < knownPasswords.Count)
                {
                    password = knownPasswords[silentTried++];
                    continue;
                }
                PasswordPromptReason reason;
                string message;
                if (!askedOnce && silentTried > 0)
                {
                    // 앞에서 쓴 암호가 이 파일에는 맞지 않음 — 이 파일에 대해서만 묻는다
                    reason = PasswordPromptReason.Wrong;
                    message = ReusedPasswordWrongMessage(name);
                }
                else if (!string.IsNullOrEmpty(password))
                {
                    reason = PasswordPromptReason.Wrong;
                    message = ex is ArchivePasswordException ? ex.Message : WrongPasswordException.DefaultMessage;
                }
                else if (ex is ListPasswordRequiredException)
                {
                    reason = PasswordPromptReason.ListLocked;
                    message = ListPasswordRequiredException.ListMessage;
                }
                else
                {
                    reason = PasswordPromptReason.Required;
                    message = ex is ArchivePasswordException ? ex.Message : PasswordRequiredException.DefaultMessage;
                }
                askedOnce = true;
                var answer = _ui.AskPassword(name, reason, message);
                if (answer is null)
                {
                    CleanupNewFolder(dest);
                    return (new ArchiveOutcome(archive, dest, false, 0, PasswordSkippedReason), false, null);
                }
                password = answer;
            }
            catch (Exception ex)
            {
                // 이유는 영어 엔진 문구 없는 쉬운 한국어(모르는 오류는 '손상됐거나 끝까지 받아지지 않은 것 같아요') — 날 문구는 로그와 요약 '자세히'(Error)에
                Logger.LogError($"여러 개 풀기 — 실패: {archive}", ex);
                CleanupNewFolder(dest);
                return (new ArchiveOutcome(archive, dest, false, 0, ErrorMessages.ToPlainMessage(ex), ex), false, null);
            }
        }
    }

    /// <summary>새로 만든 '이름' 폴더에 파일이 하나도 없으면 지운다(다음에 풀 때 번호가 밀리지 않게 — 기존 '"이름"\ 에 풀기' 규칙).</summary>
    private void CleanupNewFolder(string dest)
    {
        if (Rule.MakesNewFolder) ArchiveNaming.RemoveFolderIfNoFiles(dest);
    }

    /// <summary>암호 문제(암호 창을 띄울 일)인가 — 일집 암호 예외만(화면의 IsPasswordRelated 와 같은 타입 판정, B5).</summary>
    private static bool IsPasswordProblem(Exception ex) => ex is ArchivePasswordException;

    /// <summary>압축 하나의 진행률에 '몇 번째 · 이름'을 붙여 넘긴다(전체 수는 그때그때 — 중간에 더 들어올 수 있음).</summary>
    private sealed class BatchProgress : IProgress<ArchiveProgress>
    {
        private readonly IProgress<ArchiveProgress> _inner;
        private readonly int _index;
        private readonly Func<int> _count;
        private readonly string _name;

        public BatchProgress(IProgress<ArchiveProgress> inner, int index, Func<int> count, string name)
        {
            _inner = inner;
            _index = index;
            _count = count;
            _name = name;
        }

        public void Report(ArchiveProgress value) => _inner.Report(new ArchiveProgress
        {
            CurrentFile = value.CurrentFile,
            BytesProcessed = value.BytesProcessed,
            TotalBytes = value.TotalBytes,
            FilesProcessed = value.FilesProcessed,
            TotalFiles = value.TotalFiles,
            BatchIndex = _index,
            BatchCount = _count(),
            BatchName = _name,
        });
    }
}
