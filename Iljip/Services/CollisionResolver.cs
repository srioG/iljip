using System.IO;

namespace Iljip.Services;

// 같은 이름 파일 처리(I1-5, W2-05). 풀 때 대상에 같은 이름의 '원래 있던' 파일이 있으면 덮어쓰기·건너뛰기·이름 바꾸기(사본)·취소 중 하나로 정한다.
// 서비스(IArchiveService)는 시그니처가 그대로이고, 화면이 해제 한 번(또는 여러 개 풀기 한 묶음)을 ExtractionScope 로 감싸면
// 모든 해제 길목(OutputFiles — SharpCompress 직접 쓰기·EGG/ALZ·raw bz2, 7z.dll·압축된 tar 의 임시 폴더 옮기기)이 그 범위의 처리기에 묻는다.
// 범위가 없으면(창 없이 풀기 --extract-to·재사용장치·서비스를 바로 부르는 시험) 예전 그대로 '이름 (1).ext' 로 비켜 쓴다 — 묻지 않는다.
// IJ3-03: 화면은 범위가 적어 둔 '이번에 만든 파일'(ExtractionScope.CreatedFiles)에만 인터넷 표식(Zone.Identifier)을 붙인다 — 범위가 없는 창 없이 풀기에는 붙지 않는다.

/// <summary>같은 이름 파일을 만났을 때 고른 것.</summary>
public enum CollisionChoice
{
    /// <summary>새 내용으로 바꾼다(같은 폴더의 숨김 임시 파일에 다 쓴 뒤 한 번에 바꿈 — 실패하면 원래 파일 그대로).</summary>
    Overwrite,
    /// <summary>그 파일은 풀지 않는다(원래 파일 그대로).</summary>
    Skip,
    /// <summary>'이름 (1).ext' 로 비켜 쓴다(예전 동작).</summary>
    Rename,
    /// <summary>풀기를 멈추고 이번에 만든 것을 모두 되돌린다(원래 있던 파일은 건드리지 않음).</summary>
    Cancel,
}

/// <summary>설정 extract.onCollision — 같은 이름이 있을 때 기본 동작(기본 <see cref="Ask"/>).</summary>
public enum CollisionPolicy
{
    /// <summary>매번 묻는다(‘남은 파일도 똑같이’를 고르면 그 묶음에서는 다시 묻지 않음).</summary>
    Ask,
    Overwrite,
    Skip,
    Rename,
}

/// <summary>
/// 같은 이름 질문. <see cref="Remaining"/> 은 이 파일 뒤에 남은 파일 수(모르면 0). 새 파일 크기를 모르면 -1(raw bz2).
/// </summary>
public sealed record CollisionQuestion(
    string TargetPath,
    long ExistingSize,
    DateTime ExistingTime,
    long IncomingSize,
    DateTime? IncomingTime,
    int Remaining);

/// <summary>질문 창의 답. <see cref="ApplyToAll"/> = ‘남은 파일도 똑같이’.</summary>
public sealed record CollisionAnswer(CollisionChoice Choice, bool ApplyToAll);

/// <summary>같은 이름을 만났을 때 어떻게 할지 정하는 것(해제 작업 스레드에서 불린다).</summary>
public interface ICollisionResolver
{
    CollisionChoice Resolve(CollisionQuestion question);
}

/// <summary>늘 같은 답(설정에서 고정한 동작).</summary>
public sealed class FixedResolver : ICollisionResolver
{
    public FixedResolver(CollisionChoice choice) => Choice = choice;

    public CollisionChoice Choice { get; }

    public CollisionChoice Resolve(CollisionQuestion question) => Choice;
}

/// <summary>
/// 화면에 묻는 처리기(<see cref="IUserInteraction.AskCollision"/>). ‘남은 파일도 똑같이’ 답은 기억해서 다음부터 묻지 않는다
/// (여러 개 풀기에서는 처리기 하나를 묶음 전체가 같이 써서 그 답이 묶음 전체에 적용된다). [취소]는 기억하지 않는다.
/// 설정이 '묻기'가 아니면 묻지 않고 그 동작.
/// </summary>
public sealed class AskingResolver : ICollisionResolver
{
    private readonly IUserInteraction _ui;
    private readonly CollisionPolicy _policy;
    private readonly object _gate = new();
    private CollisionChoice? _remembered;

    public AskingResolver(IUserInteraction ui, CollisionPolicy policy = CollisionPolicy.Ask)
    {
        _ui = ui;
        _policy = policy;
    }

    /// <summary>‘남은 파일도 똑같이’로 기억한 답(없으면 null).</summary>
    public CollisionChoice? Remembered
    {
        get { lock (_gate) return _remembered; }
    }

    public CollisionChoice Resolve(CollisionQuestion question)
    {
        switch (_policy)
        {
            case CollisionPolicy.Overwrite: return CollisionChoice.Overwrite;
            case CollisionPolicy.Skip: return CollisionChoice.Skip;
            case CollisionPolicy.Rename: return CollisionChoice.Rename;
        }
        // 한 번에 하나씩 묻는다(해제는 한 스레드에서 차례로 부르지만, 혹시 겹쳐도 창이 둘 뜨지 않게)
        lock (_gate)
        {
            if (_remembered is { } r) return r;
            var answer = _ui.AskCollision(question);
            if (answer.ApplyToAll && answer.Choice != CollisionChoice.Cancel)
                _remembered = answer.Choice;
            return answer.Choice;
        }
    }

    /// <summary>설정 값으로 처리기를 만든다.</summary>
    public static ICollisionResolver For(CollisionPolicy policy, IUserInteraction ui) => policy switch
    {
        CollisionPolicy.Overwrite => new FixedResolver(CollisionChoice.Overwrite),
        CollisionPolicy.Skip => new FixedResolver(CollisionChoice.Skip),
        CollisionPolicy.Rename => new FixedResolver(CollisionChoice.Rename),
        _ => new AskingResolver(ui),
    };
}

/// <summary>
/// 같은 이름 창에서 [취소]를 골랐다. 취소(<see cref="OperationCanceledException"/>)의 한 종류라 화면은 '취소됨'으로 끝내고 오류 창을 띄우지 않는다.
/// 해제 서비스는 이 예외면 이번에 만든 파일·폴더를 모두 지운다(<see cref="ExtractionLedger"/> — 원래 있던 파일·덮어쓴 파일은 건드리지 않음).
/// </summary>
public sealed class CollisionCancelledException : OperationCanceledException
{
    public CollisionCancelledException()
        : base("같은 이름 파일 창에서 풀기를 취소했어요.")
    {
    }
}

/// <summary>
/// 해제 한 번(또는 여러 개 풀기에서 압축 하나)의 '같은 이름 처리' 범위(I1-5). 화면이 <see cref="Begin"/> 으로 열고 Dispose 로 닫는다.
/// 이 비동기 흐름에서 부르는 서비스(그 안의 Task.Run 포함 — ExecutionContext 를 따라감)가 <see cref="OutputFiles"/> 에서 이 범위를 본다.
/// 하는 일:
/// - 원래 있던 같은 이름 파일이면 처리기(<see cref="Resolver"/>)에 묻는다.
/// - 이번 해제가 만든 파일과 겹치면(압축 안에 같은 이름이 두 번 — 같은 폴더의 다른 대소문자 등) 묻지 않고 예전처럼 '이름 (1)' 로 비켜 쓴다
///   (새로 푼 내용끼리 덮어쓰지 않게 — 반디집도 이때는 안 묻고 비켜 씀).
/// - 건너뛴·덮어쓴·이름을 바꾼 수를 센다(풀고 나서 압축 파일 지우기는 건너뛴 것이 0일 때만 — I2-5).
/// - I5-B(최종 감사 IA-SELFNAME): 풀고 있는 압축 파일(과 그 조각 — <see cref="ArchiveVolumes.AllParts"/>)은 묻지도 덮지도 않고 늘 '이름 (1)' 로 비켜 쓴다
///   (이중 압축 첨부 '첨부.7z' 안의 '첨부.7z' — 예전엔 [덮어쓰기]·설정 덮어쓰기가 바깥 압축을 안쪽 것으로 바꿨다). 비켜 쓴 것은 <see cref="Notes"/> 로 알린다.
/// - I5-B: 알릴 것(<see cref="ExtractionNotes"/> — 비켜 쓴 자기 이름·다른 프로그램이 쓰는 중이라 건너뛴 파일·내용 없이 만든 링크)을 모은다.
/// </summary>
public sealed class ExtractionScope : IDisposable
{
    private static readonly AsyncLocal<ExtractionScope?> CurrentValue = new();

    private readonly ExtractionScope? _previous;
    private readonly object _gate = new();
    private readonly HashSet<string> _created = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _protected = new(StringComparer.OrdinalIgnoreCase);
    private int _skipped;
    private int _overwritten;
    private int _renamed;
    private bool _disposed;

    private ExtractionScope(ICollisionResolver resolver, ExtractionScope? previous, string? sourceArchive)
    {
        Resolver = resolver;
        _previous = previous;
        if (!string.IsNullOrEmpty(sourceArchive))
        {
            _protected.Add(Normalize(sourceArchive));
            try
            {
                foreach (var part in ArchiveVolumes.AllParts(sourceArchive)) _protected.Add(Normalize(part));
            }
            catch
            {
                // 조각을 못 모아도 고른 파일 하나는 지킨다
            }
        }
    }

    /// <summary>이 비동기 흐름에서 지금 열린 범위(없으면 null — 예전 동작).</summary>
    internal static ExtractionScope? Current => CurrentValue.Value;

    /// <summary>범위를 연다(이 흐름과 여기서 시작하는 작업에 걸림). using 으로 닫는다.</summary>
    /// <param name="sourceArchive">
    /// 지금 풀고 있는 압축 파일(I5-B) — 그 파일과 같은 세트의 조각은 같은 이름 항목이 나와도 덮지 않고 '이름 (1)' 로 비켜 쓴다(묻지 않음). 모르면 null.
    /// </param>
    public static ExtractionScope Begin(ICollisionResolver resolver, string? sourceArchive = null)
    {
        var scope = new ExtractionScope(resolver, CurrentValue.Value, sourceArchive);
        CurrentValue.Value = scope;
        return scope;
    }

    public ICollisionResolver Resolver { get; }

    /// <summary>이번 풀기에서 알릴 것(I5-B — 결과 창·여러 개 풀기 요약이 보인다).</summary>
    public ExtractionNotes Notes { get; } = new();

    /// <summary>
    /// '다 푼 뒤 압축 파일을 휴지통으로'를 막을 수(I2-5 — 건너뛴 파일 + I5-B 내용 없이 만든 링크: 압축 파일에만 온전한 것이 있으니 지우지 않음).
    /// </summary>
    public int KeepArchiveCount => Skipped + Notes.Links.Count;

    /// <summary>풀고 있는 압축 파일(또는 그 조각)인가 — 같은 이름 항목이 나와도 덮지 않는다(I5-B).</summary>
    internal bool IsProtectedSource(string path)
    {
        if (_protected.Count == 0) return false;
        return _protected.Contains(Normalize(path));
    }

    /// <summary>
    /// 덮어쓰기로 정했지만 다른 프로그램이 그 파일을 쓰는 중이라 바꾸지 못하고 건너뛰었다(I5-B — 원래 파일 그대로). 덮어쓴 수에서 빼고 건너뛴 수에 더한다
    /// (풀고 나서 압축 파일 지우기를 하지 않게), 알릴 것에 적는다.
    /// </summary>
    internal void NoteInUseSkipped(string path)
    {
        Interlocked.Decrement(ref _overwritten);
        Interlocked.Increment(ref _skipped);
        Notes.AddInUse(path);
    }

    /// <summary>‘건너뛰기’로 풀지 않은 파일 수.</summary>
    public int Skipped => Volatile.Read(ref _skipped);

    /// <summary>‘덮어쓰기’로 바꾼 파일 수.</summary>
    public int Overwritten => Volatile.Read(ref _overwritten);

    /// <summary>원래 있던 파일 때문에 ‘이름 바꾸기’로 비켜 쓴 파일 수.</summary>
    public int Renamed => Volatile.Read(ref _renamed);

    /// <summary>같은 이름 창에서 [취소]를 골랐는지.</summary>
    public bool CancelledByUser { get; private set; }

    /// <summary>
    /// <paramref name="targetPath"/> 에 쓰려는데 그 자리에 무엇이 있을 때 어떻게 할지. 이번 해제가 만든 파일이거나 폴더면 묻지 않고 <see cref="CollisionChoice.Rename"/>.
    /// [취소]면 <see cref="CollisionCancelledException"/>.
    /// </summary>
    internal CollisionChoice Decide(string targetPath, long incomingSize, DateTime? incomingTime, int remaining)
    {
        // I5-B: 풀고 있는 압축 파일 자신(또는 그 조각)은 묻지 않고 비켜 쓴다 — [덮어쓰기]·'남은 파일도 똑같이'·설정 덮어쓰기여도
        if (IsProtectedSource(targetPath))
        {
            Interlocked.Increment(ref _renamed);
            return CollisionChoice.Rename;
        }
        if (Directory.Exists(targetPath) || WasCreated(targetPath)) return CollisionChoice.Rename;
        FileInfo existing;
        try
        {
            existing = new FileInfo(targetPath);
            if (!existing.Exists) return CollisionChoice.Rename;   // 그새 사라짐 — 아래 '없을 때만 만들기'가 그 이름을 씀
        }
        catch
        {
            return CollisionChoice.Rename;
        }
        var question = new CollisionQuestion(targetPath, existing.Length, existing.LastWriteTime, incomingSize, incomingTime, Math.Max(0, remaining));
        var choice = Resolver.Resolve(question);
        switch (choice)
        {
            case CollisionChoice.Skip: Interlocked.Increment(ref _skipped); break;
            case CollisionChoice.Overwrite: Interlocked.Increment(ref _overwritten); break;
            case CollisionChoice.Rename: Interlocked.Increment(ref _renamed); break;
            case CollisionChoice.Cancel:
                CancelledByUser = true;
                throw new CollisionCancelledException();
        }
        return choice;
    }

    /// <summary>이번 해제가 만든(또는 새 내용으로 바꾼) 파일로 적는다.</summary>
    internal void NoteCreated(string path)
    {
        string key = Normalize(path);
        lock (_gate) _created.Add(key);
    }

    /// <summary>지운 파일(쓰다 만 파일·되돌린 파일)은 뺀다.</summary>
    internal void NoteRemoved(string path)
    {
        string key = Normalize(path);
        lock (_gate) _created.Remove(key);
    }

    internal bool WasCreated(string path)
    {
        string key = Normalize(path);
        lock (_gate) return _created.Contains(key);
    }

    /// <summary>
    /// 이번 해제가 만든(또는 새 내용으로 바꾼) 파일 — 지금까지의 사본(전체 경로). 되돌린·쓰다 만 파일은 빠져 있고, 원래 있던 파일·건너뛴 파일은 없다.
    /// IJ3-03: 일집 창·우클릭 풀기가 다 푼 뒤 이 파일들에만 인터넷 표식을 붙인다(<see cref="ZoneIdentifier.MarkExtractedAsync"/>).
    /// </summary>
    internal IReadOnlyList<string> CreatedFiles
    {
        get { lock (_gate) return _created.ToList(); }
    }

    private static string Normalize(string path)
    {
        try { return Path.GetFullPath(path); }
        catch { return path; }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (ReferenceEquals(CurrentValue.Value, this))
            CurrentValue.Value = _previous;
    }
}
