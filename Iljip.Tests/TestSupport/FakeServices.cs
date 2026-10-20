using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Iljip.Services;

namespace Iljip.Tests.TestSupport;

/// <summary>가짜 클립보드(IJ3-01 — W2-04 보관본에서): 넣은 파일 목록과 'Preferred DropEffect' 만 기록한다.</summary>
public sealed class FakeClipboard : IClipboardService
{
    private readonly object _gate = new();
    private readonly List<(IReadOnlyList<string> Paths, DragDropEffects Effect)> _calls = new();

    /// <summary>넣은 것(차례대로).</summary>
    public List<(IReadOnlyList<string> Paths, DragDropEffects Effect)> Calls
    {
        get { lock (_gate) return _calls.ToList(); }
    }

    /// <summary>넣은 스레드(화면 스레드에서 넣는지 보는 시험용).</summary>
    public List<int> Threads { get; } = new();

    /// <summary>있으면 넣을 때 이 예외(다른 프로그램이 클립보드를 잡음 흉내).</summary>
    public Exception? ThrowOnSet { get; set; }

    public void SetFileDrop(IReadOnlyList<string> paths, DragDropEffects effect)
    {
        lock (_gate)
        {
            Threads.Add(Environment.CurrentManagedThreadId);
            if (ThrowOnSet is { } ex) throw ex;
            _calls.Add((paths.ToList(), effect));
        }
    }
}

/// <summary>
/// 가짜 그림 읽기(IJ3-01 — W2-04 보관본에서): 파일을 실제로 그림으로 풀지 않고 이름·내용 바이트만 기록한다. 돌려주는 그림은 1×1 얼린 비트맵(화면 칸에 쓰지 않음),
/// 크기(<see cref="BytesPerImage"/>)는 미리 보기 캐시 상한 시험용.
/// </summary>
public sealed class FakeImageDecoder : IImageDecoder
{
    private readonly object _gate = new();

    public List<(string Name, byte[] Bytes)> Decoded { get; } = new();

    /// <summary>한 장이 메모리에서 차지한다고 볼 바이트.</summary>
    public long BytesPerImage { get; set; } = 4_000;

    /// <summary>이 이름이면 못 읽음(코덱 없음 흉내).</summary>
    public string? FailName { get; set; }

    /// <summary>이 이름이면 화소가 너무 많은 그림(<see cref="ImageTooLargeException"/> — 진짜 WIC 의 거절 흉내, IJ3-01).</summary>
    public string? TooLargeName { get; set; }

    /// <summary>있으면 읽기마다 이것이 열릴 때까지 기다린다(큰 그림을 읽는 도중 흉내 — 시작한 읽기는 멈추지 않음).</summary>
    public ManualResetEventSlim? Gate { get; set; }

    private int _started, _active, _maxActive;

    /// <summary>시작한 읽기 수.</summary>
    public int Started => Volatile.Read(ref _started);

    /// <summary>동시에 돈 읽기의 최댓값.</summary>
    public int MaxActive => Volatile.Read(ref _maxActive);

    /// <summary>읽기가 <paramref name="count"/> 번 시작될 때까지 기다린다(못 하면 false).</summary>
    public bool WaitStarted(int count, TimeSpan timeout) => SpinWait.SpinUntil(() => Started >= count, timeout);

    public int DecodeCount(string name)
    {
        lock (_gate) return Decoded.Count(d => d.Name == name);
    }

    public DecodedImage Decode(string path)
    {
        Interlocked.Increment(ref _started);
        int now = Interlocked.Increment(ref _active);
        for (int max = Volatile.Read(ref _maxActive); now > max; max = Volatile.Read(ref _maxActive))
            if (Interlocked.CompareExchange(ref _maxActive, now, max) == max) break;
        try
        {
            var bytes = File.ReadAllBytes(path);
            string name = Path.GetFileName(path);
            lock (_gate) Decoded.Add((name, bytes));
            Gate?.Wait(TimeSpan.FromSeconds(30));
            if (FailName == name) throw new NotSupportedException("이 PC 에 코덱이 없음(가짜)");
            if (TooLargeName == name) throw new ImageTooLargeException(30000, 30000);
            var bmp = BitmapSource.Create(1, 1, 96, 96, PixelFormats.Bgra32, null, new byte[4], 4);
            bmp.Freeze();
            return new DecodedImage(bmp, 10, 10, BytesPerImage);
        }
        finally
        {
            Interlocked.Decrement(ref _active);
        }
    }
}

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

    /// <summary>'기본 앱 설정 열기'를 부른 횟수(W2-12 — 진짜 ms-settings 는 열지 않음).</summary>
    public int DefaultAppsOpened { get; private set; }

    /// <summary>파일 연결이 바뀌었다고 알린 횟수(W2-12 — 진짜 SHChangeNotify 는 부르지 않음).</summary>
    public int AssociationsChangedNotified { get; private set; }

    public void OpenDefaultAppsSettings()
    {
        lock (_gate) DefaultAppsOpened++;
    }

    public void NotifyAssociationsChanged()
    {
        lock (_gate) AssociationsChangedNotified++;
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

    /// <summary>IsRecyclable 이 불릴 때마다(작업 스레드에서) 부른다 — 원본을 맞춰 보는 도중에 다른 일(진행 창 [취소] 등)을 끼워 넣는 시험(W2-07 검증 지적 2차).</summary>
    public Action<string>? OnIsRecyclable { get; set; }

    /// <summary>
    /// Send 가 불릴 때(결과를 돌려주기 전에) 부른다 — '일부만 옮겨짐'을 흉내 내는 시험이 그 시험의 임시 원본 하나를 직접 지워 '옮겨진 것'으로 만든다
    /// (W2-07 검증 지적 2차). 가짜 휴지통 자신은 여전히 아무것도 지우지 않는다.
    /// </summary>
    public Action<IReadOnlyList<string>>? OnSend { get; set; }

    public bool IsRecyclable(string path)
    {
        OnIsRecyclable?.Invoke(path);
        return !NotRecyclable.Any(p => Path.GetFullPath(path).StartsWith(Path.GetFullPath(p), StringComparison.OrdinalIgnoreCase));
    }

    public RecycleResult Send(IReadOnlyList<string> paths)
    {
        lock (_gate) Sent.Add(paths.ToList());
        OnSend?.Invoke(paths);
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

    /// <summary>가짜 시계(W2-07 — 이름에 날짜). 모양은 W2-04 보관본과 같게.</summary>
    public FakeClock Clock { get; } = new();

    /// <summary>
    /// 만든 압축 시험기(W2-07). 기본은 진짜 시험기에 맡기며 부른 기록만 남긴다(<see cref="FakeCompressTester.Pass"/>·<see cref="FakeCompressTester.Fail"/> 로 바꿈).
    /// </summary>
    public FakeCompressTester CompressTester { get; } = new();

    /// <summary>
    /// '잠깐 두는' 폴더(W2-09 검증 지적 — 풀기 창이 임시 폴더의 압축에는 바탕 화면을 권함). 기본은 시스템 임시 폴더·바탕 화면 없음
    /// (시험 픽스처가 진짜 %TEMP% 아래에 있어서 — 일집 임시 폴더는 시험용 자료 폴더 아래라 늘 '임시'). 시험이 가짜 임시 폴더·바탕 화면을 넣는다.
    /// </summary>
    public TempLocations TempLocations { get; }

    /// <summary>가짜 일집 띄우기(W2-11 — 새 창·관리자 권한으로 다시 시도): 부른 것만 기록한다(진짜 프로세스·UAC 를 절대 부르지 않음).</summary>
    public FakeProcessLauncher Processes { get; } = new();

    /// <summary>가짜 네트워크 드라이브 매핑(W2-11). 기본은 매핑 없음(모든 경로 그대로).</summary>
    public FakeMappedDriveResolver MappedDrives { get; } = new();

    /// <summary>가짜 레지스트리(W2-12 — 파일 연결). Software\Classes 만 있는 빈 HKCU 흉내 — 진짜 HKCU 는 절대 쓰지 않는다.</summary>
    public FakeRegistryRoot Registry { get; } = FakeRegistryRoot.WithDefaults();

    /// <summary>가짜 클립보드(IJ3-01 — Ctrl+C): 넣은 파일 목록만 기록한다(진짜 클립보드를 절대 쓰지 않음).</summary>
    public FakeClipboard Clipboard { get; } = new();

    /// <summary>가짜 그림 읽기(IJ3-01 — 미리 보기): 그림으로 풀지 않고 이름·바이트만 기록한다.</summary>
    public FakeImageDecoder Images { get; } = new();

    public TestServices(AppSettings? settings = null, TempLocations? tempLocations = null)
    {
        Settings = new InMemorySettingsStore(settings);
        TempLocations = tempLocations ?? new TempLocations(Array.Empty<string>(), desktop: null);
        Services = new AppServices
        {
            Shell = Shell,
            Settings = Settings,
            RecycleBin = RecycleBin,
            FileChanges = FileChanges,
            Clock = Clock,
            CompressTester = CompressTester,
            TempLocations = TempLocations,
            Processes = Processes,
            MappedDrives = MappedDrives,
            Registry = Registry,
            Clipboard = Clipboard,
            ImageDecoder = Images,
        };
    }

    public AppServices Services { get; }

    /// <summary>이 시험 흐름의 일집 자료 폴더(%LOCALAPPDATA%\Iljip 대신)를 <paramref name="tmp"/> 아래로 — Dispose 로 되돌림(IJ3-01 — 임시 폴더 open·drag·clip·preview 를 시험마다 따로).</summary>
    public static IDisposable UseAppRoot(TempDir tmp) => AppPaths.OverrideRootForTests(tmp.Dir("appdata"));

    /// <summary>설정 하나를 바꾼 묶음을 만드는 도우미.</summary>
    public static TestServices With(Action<ExtractSettings> change)
    {
        var s = new AppSettings();
        change(s.Extract);
        return new TestServices(s);
    }

    /// <summary>압축 설정(compress.*) 하나를 바꾼 묶음(W2-07).</summary>
    public static TestServices WithCompress(Action<CompressSettings> change)
    {
        var s = new AppSettings();
        change(s.Compress);
        return new TestServices(s);
    }
}

/// <summary>
/// 가짜 일집 띄우기(W2-11): 새 창·관리자 권한 다시 시작 호출(실행 파일, 인자)만 기록한다 — 진짜 Process.Start·runas(UAC 창)는 절대 부르지 않는다.
/// <see cref="ThrowOnElevate"/> 로 'UAC 창에서 [아니요]'(Win32Exception 1223) 등을 흉내 낸다.
/// </summary>
public sealed class FakeProcessLauncher : IProcessLauncher
{
    private readonly object _gate = new();

    /// <summary>지금 일집 실행 파일 경로(가짜 — 실제로 띄우지 않으므로 무엇이든 됨).</summary>
    public string? ExecutablePath { get; set; } = @"C:\Program Files\일집\Iljip.exe";

    /// <summary>이 일집이 이미 관리자 권한인가(기본 아님).</summary>
    public bool IsCurrentProcessElevated { get; set; }

    public List<(string Exe, IReadOnlyList<string> Args)> NewInstances { get; } = new();
    public List<(string Exe, IReadOnlyList<string> Args)> Elevated { get; } = new();

    /// <summary>관리자 권한 다시 시작에서 이 예외를 던진다(기록은 함).</summary>
    public Exception? ThrowOnElevate { get; set; }

    /// <summary>새 창에서 이 예외를 던진다(기록은 함).</summary>
    public Exception? ThrowOnNewInstance { get; set; }

    public void StartNewInstance(string exe, IReadOnlyList<string> args)
    {
        lock (_gate) NewInstances.Add((exe, args.ToList()));
        if (ThrowOnNewInstance is { } ex) throw ex;
    }

    public void StartElevated(string exe, IReadOnlyList<string> args)
    {
        lock (_gate) Elevated.Add((exe, args.ToList()));
        if (ThrowOnElevate is { } ex) throw ex;
    }
}

/// <summary>
/// 가짜 네트워크 드라이브 매핑(W2-11): <see cref="Map"/> 에 넣은 드라이브('Z:' → '\\서버\공유')만 네트워크 드라이브로 보고 UNC 로 바꾼다.
/// <see cref="Unresolvable"/> 의 드라이브는 '네트워크 드라이브인데 UNC 를 모름'(null).
/// </summary>
public sealed class FakeMappedDriveResolver : IMappedDriveResolver
{
    public Dictionary<string, string> Map { get; } = new(StringComparer.OrdinalIgnoreCase);

    public HashSet<string> Unresolvable { get; } = new(StringComparer.OrdinalIgnoreCase);

    public string? ToUncIfMapped(string fullPath)
    {
        if (fullPath.Length < 2 || fullPath[1] != ':') return fullPath;
        string drive = fullPath[..2];
        if (Unresolvable.Contains(drive)) return null;
        return Map.TryGetValue(drive, out var unc) ? unc.TrimEnd('\\') + fullPath[2..] : fullPath;
    }
}

/// <summary>가짜 시계(W2-07 — 모양은 W2-04 보관본과 같게). <see cref="SetLocal"/> 로 '오늘'을 정한다.</summary>
public sealed class FakeClock : IClock
{
    public DateTime UtcNow { get; set; } = DateTime.UtcNow;

    /// <summary>이 PC 시간대의 그 시각으로 맞춘다(이름의 날짜는 지역 시각 — 시간대를 바꿔 돌려도 같은 날이 나오게).</summary>
    public FakeClock SetLocal(int year, int month, int day, int hour = 12)
    {
        UtcNow = new DateTime(year, month, day, hour, 0, 0, DateTimeKind.Local).ToUniversalTime();
        return this;
    }
}

/// <summary>
/// 만든 압축 시험기(W2-07) 가짜: 부른 경로를 기록하고, <see cref="Mode"/> 대로 통과·실패·진짜 시험기에 맡기기.
/// <see cref="FailFor"/> 에 든 이름(파일 이름 앞부분)이면 그 압축만 실패.
/// </summary>
public sealed class FakeCompressTester : ICreatedArchiveTester
{
    public enum TestMode { Real, Pass, Fail }

    private readonly object _gate = new();

    public TestMode Mode { get; set; } = TestMode.Real;

    /// <summary>이 이름으로 시작하는 압축 파일만 실패(그 밖은 <see cref="Mode"/>).</summary>
    public List<string> FailFor { get; } = new();

    /// <summary>시험한 경로(차례대로).</summary>
    public List<string> Tested { get; } = new();

    public FakeCompressTester Pass()
    {
        Mode = TestMode.Pass;
        return this;
    }

    public FakeCompressTester Fail()
    {
        Mode = TestMode.Fail;
        return this;
    }

    public void Test(string archivePath, string? password, CancellationToken cancellationToken)
    {
        lock (_gate) Tested.Add(archivePath);
        cancellationToken.ThrowIfCancellationRequested();
        string name = Path.GetFileName(archivePath);
        if (FailFor.Any(f => name.StartsWith(f, StringComparison.OrdinalIgnoreCase)) || Mode == TestMode.Fail)
            throw new CorruptArchiveException();
        if (Mode == TestMode.Real) CreatedArchiveTester.Instance.Test(archivePath, password, cancellationToken);
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

    /// <summary>
    /// 그 파일이 바뀌었다고 알린다(감시 중인 것만). 알린 수를 돌려준다.
    /// I5-A: 진짜 감시(<see cref="FileChangeSource"/>)처럼 <b>같은 폴더</b>를 감시하는 것에 알린다 — 편집기가 이름을 바꾼 새 이름(같은 순번 폴더)으로 알려도 닿는다.
    /// </summary>
    public int Raise(string filePath)
    {
        string folder = Path.GetDirectoryName(Path.GetFullPath(filePath))!;
        List<Action> targets;
        lock (_gate)
            targets = _subs.Where(s => !s.Sub.Disposed && string.Equals(Path.GetDirectoryName(s.Path), folder, StringComparison.OrdinalIgnoreCase)).Select(s => s.OnChanged).ToList();
        foreach (var t in targets) t();
        return targets.Count;
    }

    private sealed class Subscription : IDisposable
    {
        public bool Disposed { get; private set; }

        public void Dispose() => Disposed = true;
    }
}
