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

    public TestServices(AppSettings? settings = null)
    {
        Settings = new InMemorySettingsStore(settings);
        Services = new AppServices
        {
            Shell = Shell,
            Settings = Settings,
            RecycleBin = RecycleBin,
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
