using Iljip.Services;

namespace Iljip.Tests.TestSupport;

/// <summary>
/// 가짜 셸(W2-05 — 모양은 W2-04 보관본과 같게): '열기'·'폴더 열기' 호출만 기록한다 — 시험은 진짜 ShellExecute·탐색기를 절대 부르지 않는다(창이 뜸).
/// </summary>
public sealed class FakeShellLauncher : IShellLauncher
{
    private readonly object _gate = new();

    public List<string> OpenedFiles { get; } = new();
    public List<(string Path, bool Select)> OpenedFolders { get; } = new();

    /// <summary>열기에서 이 예외를 던진다(연결된 프로그램이 없을 때 흉내).</summary>
    public Exception? ThrowOnOpen { get; set; }

    public void OpenFile(string path)
    {
        lock (_gate) OpenedFiles.Add(path);
        if (ThrowOnOpen is { } ex) throw ex;
    }

    public void OpenFolder(string path, bool select = false)
    {
        lock (_gate) OpenedFolders.Add((path, select));
    }
}

/// <summary>
/// 가짜 휴지통(W2-05): 보낸 파일 목록만 기록한다 — 진짜 휴지통·삭제는 절대 하지 않는다(파일은 그대로 남음).
/// <see cref="NotRecyclable"/> 로 '휴지통이 없는 곳'(네트워크·이동식 드라이브)을 흉내 낸다.
/// </summary>
public sealed class FakeRecycleBin : IRecycleBin
{
    private readonly object _gate = new();

    /// <summary>Send 로 받은 묶음(차례대로).</summary>
    public List<IReadOnlyList<string>> Sent { get; } = new();

    /// <summary>이 경로(앞부분이 같으면)는 휴지통이 없는 곳으로 본다.</summary>
    public List<string> NotRecyclable { get; } = new();

    /// <summary>Send 가 돌려줄 결과(기본 성공).</summary>
    public RecycleResult Result { get; set; } = RecycleResult.Ok;

    public bool IsRecyclable(string path) =>
        !NotRecyclable.Any(p => Path.GetFullPath(path).StartsWith(Path.GetFullPath(p), StringComparison.OrdinalIgnoreCase));

    public RecycleResult Send(IReadOnlyList<string> paths)
    {
        lock (_gate) Sent.Add(paths.ToList());
        return Result;
    }

    /// <summary>보낸 파일 전부(묶음을 펼침).</summary>
    public List<string> AllSent
    {
        get { lock (_gate) return Sent.SelectMany(s => s).ToList(); }
    }
}

/// <summary>
/// MainViewModel 에 넣을 가짜 시스템 동작 묶음(W2-05 — 모양은 W2-04 보관본과 같게) — 셸·휴지통·설정 모두 가짜.
/// </summary>
public sealed class TestServices
{
    public FakeShellLauncher Shell { get; } = new();
    public FakeRecycleBin RecycleBin { get; } = new();
    public InMemorySettingsStore Settings { get; }

    /// <summary>가짜 파일 바뀜 알림(W2-06 — 시험이 <see cref="FakeFileChangeSource.Raise"/> 로 '바뀜'을 알림).</summary>
    public FakeFileChangeSource FileChanges { get; } = new();

    public TestServices(AppSettings? settings = null)
    {
        Settings = new InMemorySettingsStore(settings);
        Services = new AppServices
        {
            Shell = Shell,
            Settings = Settings,
            RecycleBin = RecycleBin,
            FileChanges = FileChanges,
        };
    }

    public AppServices Services { get; }

    /// <summary>설정 하나를 바꾼 묶음을 만드는 도우미.</summary>
    public static TestServices With(Action<ExtractSettings> change)
    {
        var s = new AppSettings();
        change(s.Extract);
        return new TestServices(s);
    }
}

/// <summary>
/// 가짜 파일 바뀜 알림(W2-06): 진짜 FileSystemWatcher·타이머를 쓰지 않는다. 감시를 건 경로를 기록하고, 시험이 <see cref="Raise"/> 로 알림을 보낸다.
/// </summary>
public sealed class FakeFileChangeSource : IFileChangeSource
{
    private readonly object _gate = new();
    private readonly List<(string Path, Action OnChanged, Subscription Sub)> _subs = new();

    /// <summary>감시를 건 경로(차례대로, 그만둔 것도 남음).</summary>
    public List<string> Watched { get; } = new();

    /// <summary>지금 감시 중인 경로 수.</summary>
    public int ActiveCount
    {
        get { lock (_gate) return _subs.Count(s => !s.Sub.Disposed); }
    }

    public IDisposable Watch(string filePath, Action onChanged)
    {
        var sub = new Subscription();
        lock (_gate)
        {
            Watched.Add(Path.GetFullPath(filePath));
            _subs.Add((Path.GetFullPath(filePath), onChanged, sub));
        }
        return sub;
    }

    /// <summary>그 파일이 바뀌었다고 알린다(감시 중인 것만). 알린 수를 돌려준다.</summary>
    public int Raise(string filePath)
    {
        string full = Path.GetFullPath(filePath);
        List<Action> targets;
        lock (_gate)
            targets = _subs.Where(s => !s.Sub.Disposed && string.Equals(s.Path, full, StringComparison.OrdinalIgnoreCase)).Select(s => s.OnChanged).ToList();
        foreach (var t in targets) t();
        return targets.Count;
    }

    private sealed class Subscription : IDisposable
    {
        public bool Disposed { get; private set; }

        public void Dispose() => Disposed = true;
    }
}
