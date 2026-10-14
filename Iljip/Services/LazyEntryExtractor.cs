using System.Collections.Concurrent;
using System.IO;
using System.Windows.Threading;

namespace Iljip.Services;

/// <summary>
/// 끌어낸 항목을 '놓은 순간에' 꺼내는 일꾼(W2-04 — I1-4 지연 렌더링). <see cref="VirtualFileDataObject"/> 가 탐색기의
/// GetData(FILECONTENTS, i) 를 받으면 <see cref="GetContent"/>(i) 를 부른다.
/// <list type="bullet">
/// <item>부르기 전에는 아무것도 꺼내지 않는다(폴더도 만들지 않음) — 끌다가 취소하면 디스크에 남는 것이 없다.</item>
/// <item>파일이 둘 이상이면 처음 불릴 때 이 끌기의 파일을 '한 번에' <c>temp\drag\&lt;guid&gt;\all\</c> 에 꺼내고(진행 창 하나), 뒤의 요청은 꺼낸 파일을 바로 준다.
///   항목마다 따로 꺼내면 솔리드 압축(7z 기본값·반디집/7-Zip 의 7z, 솔리드 RAR·EGG)은 한 파일마다 묶음 처음부터 다시 풀어야 해서
///   폴더를 끌면 걸리는 시간이 파일 수의 제곱으로 늘었다(100KB 파일 300개 솔리드 7z: 한 번에 2초, 하나씩 95초 — W2-04 2차 검증).
///   한 번에 꺼내기가 실패하면(손상 항목 하나 등) 그 뒤 요청은 예전처럼 항목마다 <c>…\i\</c> 에 따로 꺼낸다 — 멀쩡한 파일은 그대로 받는다.
///   경로 공격 항목이 섞인 폴더는 안전한 파일만 골라 한 번에 꺼낸다(<see cref="BatchSelectors"/>). 진행 창에서 [취소]하면 이 끌기의 남은 요청은
///   꺼내지 않고 모두 취소로 돌려준다(<see cref="IsCancelled"/> — IJ3-01).</item>
/// <item>같은 항목을 두 번 불러도 다시 꺼내지 않는다(<see cref="ExtractionCount"/> 로 시험).</item>
/// <item>압축 서비스 인스턴스를 따로 받는다 — 끄는 동안 화면이 다른 압축을 열거나 닫아도 영향이 없게(설계 위험 항목).</item>
/// <item>꺼낸 임시 파일에 인터넷 표식(Zone.Identifier)을 붙인다(<see cref="ZoneIdentifier"/>). 탐색기는 내용 스트림으로 새 파일을 만들어 이 표식을 옮기지 않으므로,
///   놓은 곳의 파일 표식은 끌기 데이터의 'ZoneIdentifier' 형식(<see cref="ZoneId"/>)으로 따로 알린다.</item>
/// <item>꺼내기는 따로 스레드에서 돌리고, 부른 스레드가 UI(디스패처) 스레드면 기다리는 동안 화면 메시지를 계속 돌린다 — 진행 창 [취소]가 먹고 화면이 굳지 않게.</item>
/// </list>
/// 끌기에서는 암호를 묻지 않는다 — 화면이 끌기를 시작하기 전에 확인한 암호(<paramref name="password"/>)만 쓴다.
/// </summary>
public sealed class LazyEntryExtractor : IDisposable
{
    private readonly IArchiveService _service;
    private readonly string _archivePath;
    private readonly string? _password;
    private readonly IReadOnlyList<DragItem> _items;

    /// <summary>한 번에 꺼내기를 쓰는가(파일 항목이 둘 이상 — 하나면 따로 꺼내기와 같아서).</summary>
    private readonly bool _usesBatch;

    private readonly Func<IReadOnlyList<DragItem>, IProgressSession?>? _progressFactory;
    private readonly Action<Task> _wait;
    private readonly ConcurrentDictionary<int, Lazy<Task<string>>> _tasks = new();
    private readonly List<IDisposable> _streams = new();
    private readonly object _gate = new();

    /// <summary>한 번에 꺼내기(파일이 둘 이상일 때, 처음 불릴 때 시작) — 끝나면 (항목 번호 → 꺼낸 파일).</summary>
    private Lazy<Task<IReadOnlyDictionary<int, string>>>? _batch;

    private FileStream? _lock;
    private byte[]? _zone;
    private bool _zoneRead;
    private int _extractions;
    private bool _disposed;

    /// <param name="service">이 끌기 전용 압축 서비스.</param>
    /// <param name="archivePath">압축 파일.</param>
    /// <param name="password">끌기 전에 확인한 암호(없으면 null).</param>
    /// <param name="items">끌기 서술자(<see cref="DragPayloadBuilder"/>).</param>
    /// <param name="workRoot">이 끌기의 임시 폴더(<c>temp\drag\&lt;guid&gt;</c> — 첫 꺼내기 때 만든다).</param>
    /// <param name="progressFactory">
    /// 꺼낼 때 진행 창 — 이번에 꺼낼 파일 항목들(한 번에 꺼내기면 이 끌기의 파일 전부, 따로 꺼내기면 그 하나)을 받아, 띄울 만큼 크면 창을 연다(null 이면 창 없이).
    /// </param>
    /// <param name="wait">꺼내기를 기다리는 법(기본: UI 스레드면 메시지를 돌리며, 아니면 그냥 기다림).</param>
    public LazyEntryExtractor(IArchiveService service, string archivePath, string? password, IReadOnlyList<DragItem> items, string workRoot,
        Func<IReadOnlyList<DragItem>, IProgressSession?>? progressFactory = null, Action<Task>? wait = null)
    {
        _service = service;
        _archivePath = archivePath;
        _password = string.IsNullOrEmpty(password) ? null : password;
        _items = items;
        _usesBatch = items.Count(i => !i.IsDirectory) >= 2;
        WorkRoot = workRoot;
        _progressFactory = progressFactory;
        _wait = wait ?? WaitPumping;
    }

    public string WorkRoot { get; }

    public IReadOnlyList<DragItem> Items => _items;

    /// <summary>압축 서비스에 꺼내기를 맡긴 횟수(한 번에 꺼내기 = 1, 같은 항목을 두 번 불러도 다시 꺼내지 않음).</summary>
    public int ExtractionCount => Volatile.Read(ref _extractions);

    /// <summary>한 번에 꺼낼 때의 하위 폴더 이름(항목마다 따로 꺼내는 폴더는 번호라 겹치지 않음).</summary>
    internal const string BatchFolderName = "all";

    /// <summary>꺼내다 실패하면(탐색기에는 실패로 돌려줌) 화면이 알릴 수 있게. 한 번에 꺼내기의 실패는 알리지 않는다(항목마다 다시 꺼내 그 결과를 알림).</summary>
    public event Action<DragItem, Exception>? ExtractionFailed;

    /// <summary>
    /// i번 항목의 꺼낸 파일 경로(처음 부를 때 꺼냄 — 파일이 둘 이상이면 이 끌기의 파일을 한 번에). 폴더 항목·범위 밖이면 <see cref="ArgumentOutOfRangeException"/>.
    /// 취소(진행 창)면 <see cref="OperationCanceledException"/>, 꺼내기 실패면 그 예외.
    /// </summary>
    public string GetContent(int index)
    {
        if (index < 0 || index >= _items.Count || _items[index].IsDirectory)
            throw new ArgumentOutOfRangeException(nameof(index));
        ObjectDisposedException.ThrowIf(_disposed, this);
        ThrowIfCancelled();
        if (_usesBatch && FromBatch(index) is { } batched) return batched;
        var lazy = _tasks.GetOrAdd(index, i => new Lazy<Task<string>>(() => Start(i), LazyThreadSafetyMode.ExecutionAndPublication));
        var task = lazy.Value;
        try
        {
            _wait(task);
            return task.GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            // 실패한 항목은 잊는다(같은 끌기에서 탐색기가 [다시 시도]하면 다시 꺼냄)
            _tasks.TryRemove(new KeyValuePair<int, Lazy<Task<string>>>(index, lazy));
            if (ex is OperationCanceledException)
            {
                _cancelled = true;
            }
            else
            {
                Logger.LogError($"끌어 꺼내기 실패: {_archivePath} → {_items[index].EntryKey}", ex);
                ExtractionFailed?.Invoke(_items[index], ex);
            }
            throw;
        }
    }

    /// <summary>
    /// 사용자가 진행 창에서 [취소]했는가. 취소한 뒤에는 이 끌기의 다른 요청도 꺼내지 않고 바로 취소로 돌려준다(IJ3-01) —
    /// 탐색기가 남은 파일을 계속 달라고 하면 예전엔 하나씩 따로 꺼내(솔리드 압축은 매번 묶음 처음부터) 오래 걸리고 진행 창이 또 떴다.
    /// 다시 받으려면 다시 끌면 된다(끌기마다 새 일꾼).
    /// </summary>
    public bool IsCancelled => _cancelled;

    private volatile bool _cancelled;

    private void ThrowIfCancelled()
    {
        if (_cancelled) throw new OperationCanceledException("끌어 꺼내기를 취소했어요.");
    }

    /// <summary>탐색기에 넘긴 스트림을 적어 둔다 — 끝날 때 닫고 폴더를 지운다.</summary>
    internal void Track(IDisposable stream)
    {
        lock (_gate)
        {
            if (_disposed) { stream.Dispose(); return; }
            _streams.Add(stream);
        }
    }

    /// <summary>
    /// 꺼내기 시작(처음 부른 스레드에서): 진행 창은 여기서 — 부른 스레드가 UI 면 그 자리에서, 아니면 UI 로 넘겨 연다
    /// (꺼내는 일꾼 스레드에서 UI 를 기다리면, UI 가 꺼내기를 기다리는 동안 서로 멈출 수 있어서). 꺼내기는 따로 스레드에서.
    /// </summary>
    private Task<string> Start(int index)
    {
        var session = OpenProgress(new[] { _items[index] });
        return Task.Run(() => ExtractOne(index, session));
    }

    private IProgressSession? OpenProgress(IReadOnlyList<DragItem> items)
    {
        try
        {
            return _progressFactory?.Invoke(items);
        }
        catch (Exception ex)
        {
            Logger.LogError("끌어 꺼내기 진행 창을 열지 못함(창 없이 계속)", ex);
            return null;
        }
    }

    /// <summary>
    /// 한 번에 꺼낸 것에서 i번 파일을 찾는다(처음 부르면 이 끌기의 파일을 모두 꺼냄 — 다른 요청은 그 꺼내기를 같이 기다림).
    /// 꺼내기가 취소되면 <see cref="OperationCanceledException"/>(탐색기에 E_ABORT — 그 뒤 요청도 모두, <see cref="IsCancelled"/>).
    /// 꺼내기가 실패했거나(손상 항목 하나 등) 그 파일을 못 찾았으면 null — 부른 쪽이 항목만 따로 꺼낸다(예전 방식).
    /// </summary>
    private string? FromBatch(int index)
    {
        Lazy<Task<IReadOnlyDictionary<int, string>>> lazy;
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            lazy = _batch ??= new Lazy<Task<IReadOnlyDictionary<int, string>>>(StartBatch, LazyThreadSafetyMode.ExecutionAndPublication);
        }
        try
        {
            var task = lazy.Value;   // (꺼내기를 시작하는 도중 같은 스레드에서 다시 불리면 예외 → 아래에서 따로 꺼냄)
            _wait(task);
            var map = task.GetAwaiter().GetResult();
            return map.TryGetValue(index, out var path) && File.Exists(path) ? path : null;
        }
        catch (OperationCanceledException)
        {
            // 이 요청 중에(또는 같이 기다린 요청에서) 진행 창에서 취소함 — 이 끌기는 끝(다른 요청도 꺼내지 않음)
            _cancelled = true;
            throw;
        }
        catch
        {
            return null;   // 실패(로그는 ExtractAll 이 남김)·앞서 취소됨 → 항목마다 따로
        }
    }

    private Task<IReadOnlyDictionary<int, string>> StartBatch()
    {
        var files = _items.Where(i => !i.IsDirectory).ToList();
        var session = OpenProgress(files);
        return Task.Run(() => ExtractAll(session));
    }

    /// <summary>
    /// 이 끌기의 파일을 한 번에 <c>WorkRoot\all\</c> 에 꺼내고 (항목 번호 → 꺼낸 파일)을 돌려준다. 실패·취소면 그 폴더째 지우고 예외(반쪽 파일을 남기지 않음).
    /// 꺼낸 자리는 <see cref="LocateExtracted"/> 로 찾는다 — 대소문자만 다른 두 파일('Report.txt'·'report.txt')이 한 자리를 나눠 갖지 않게 이미 준 자리는 건너뜀.
    /// </summary>
    private async Task<IReadOnlyDictionary<int, string>> ExtractAll(IProgressSession? session)
    {
        string dest = Path.Combine(WorkRoot, BatchFolderName);
        try
        {
            EnsureRoot();
            Directory.CreateDirectory(dest);
            Interlocked.Increment(ref _extractions);
            await _service.ExtractEntriesAsync(_archivePath, dest, BatchSelectors(_items), _password,
                session?.Progress, session?.CancellationToken ?? CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            TempFolders.DeleteBestEffort(dest);
            if (ex is not OperationCanceledException)
                Logger.LogError($"끌어 꺼내기: 한 번에 꺼내지 못해 항목마다 따로 꺼냄: {_archivePath}", ex);
            throw;
        }
        finally
        {
            session?.Close();
        }

        var map = new Dictionary<int, string>();
        var claimed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var listings = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);   // 폴더 목록은 폴더마다 한 번만 읽음
        for (int i = 0; i < _items.Count; i++)
        {
            if (_items[i].IsDirectory) continue;
            if (LocateExtracted(dest, _items[i].EntryKey, isDirectory: false, claimed, listings) is not { } path) continue;   // 못 찾은 것은 불릴 때 따로 꺼냄
            claimed.Add(path);
            map[i] = path;
        }
        if (ArchiveZone is { } zone)
            foreach (var path in map.Values) ZoneIdentifier.Apply(zone, path);
        return map;
    }

    /// <summary>
    /// 한 번에 꺼낼 때 서비스에 넘길 열쇠: 맨 위 항목(목록에서 고른 것 — 폴더면 엔진이 그 안을 다 꺼냄) + 그 밑에 들지 않는 파일 항목.
    /// 열쇠가 적을수록 엔진이 고르기 쉬워 고른 것만 넘긴다.
    /// 단, 끌 항목 중에 대상 폴더 밖을 가리키는 경로 공격 항목('상자/../../evil.txt')이 있으면, 폴더 열쇠는 그 항목까지 골라 엔진이 꺼내기 전체를
    /// 거부한다 — 그때는 안전한 파일 항목의 열쇠만 하나하나 넘겨 멀쩡한 파일은 한 번에 꺼내고, 공격 항목만 불릴 때 따로 실패하게 한다(IJ3-01:
    /// 예전엔 한 번에 꺼내기가 통째로 실패해 모든 파일을 하나씩 꺼냈다 — 솔리드 압축이면 파일 수의 제곱).
    /// </summary>
    internal static IReadOnlyList<string> BatchSelectors(IReadOnlyList<DragItem> items)
    {
        if (items.Any(i => IsUnsafeKey(i.EntryKey)))
            return items.Where(i => !i.IsDirectory && !IsUnsafeKey(i.EntryKey)).Select(i => i.EntryKey).ToList();

        var keys = new List<string>();
        var folders = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var it in items.Where(i => !i.RelativePath.Contains('\\')))
        {
            keys.Add(it.EntryKey);
            if (it.IsDirectory) folders.Add(DragPayloadBuilder.NormalizeKey(it.EntryKey));
        }
        var top = new HashSet<string>(keys, StringComparer.OrdinalIgnoreCase);
        foreach (var it in items)
        {
            if (it.IsDirectory || top.Contains(it.EntryKey) || UnderAny(folders, DragPayloadBuilder.NormalizeKey(it.EntryKey))) continue;
            keys.Add(it.EntryKey);
        }
        return keys;
    }

    /// <summary>'/' 구분 열쇠의 조상 중 하나가 <paramref name="folders"/> 에 있는가(깊이만큼만 봄 — 고른 폴더 수와 상관없이 빠름).</summary>
    private static bool UnderAny(HashSet<string> folders, string key)
    {
        if (folders.Count == 0) return false;
        for (int slash = key.LastIndexOf('/'); slash > 0; slash = key.LastIndexOf('/', slash - 1))
            if (folders.Contains(key[..slash])) return true;
        return false;
    }

    /// <summary>해제 엔진이 '안전하지 않은 경로'로 거부할 열쇠인가(대상 폴더 밖 — <see cref="EntryPaths.ResolveOutput"/> 와 같은 판정, 디스크는 보지 않음).</summary>
    internal static bool IsUnsafeKey(string entryKey)
    {
        try
        {
            EntryPaths.ResolveOutput(ProbeRoot, entryKey);
            return false;
        }
        catch
        {
            return true;
        }
    }

    private static readonly string ProbeRoot = Path.Combine(Path.GetTempPath(), "iljip-probe", "dest");

    private async Task<string> ExtractOne(int index, IProgressSession? session)
    {
        var item = _items[index];
        string dest = Path.Combine(WorkRoot, index.ToString(System.Globalization.CultureInfo.InvariantCulture));
        try
        {
            EnsureRoot();
            Directory.CreateDirectory(dest);
            Interlocked.Increment(ref _extractions);
            await _service.ExtractEntriesAsync(_archivePath, dest, new[] { item.EntryKey }, _password,
                session?.Progress, session?.CancellationToken ?? CancellationToken.None).ConfigureAwait(false);
        }
        catch
        {
            TempFolders.DeleteBestEffort(dest);   // 취소·실패면 그 항목 폴더째 — 반쪽 파일을 남기지 않는다
            throw;
        }
        finally
        {
            session?.Close();
        }
        string path = FindExtracted(dest, item.EntryKey)
                      ?? throw new FileNotFoundException("꺼낸 파일을 찾지 못했어요: " + item.EntryKey);
        if (ArchiveZone is { } zone) ZoneIdentifier.Apply(zone, path);
        return path;
    }

    /// <summary>압축의 인터넷 표식 내용(없으면 null) — 처음 물을 때 한 번만 읽는다(꺼내지 않음).</summary>
    private byte[]? ArchiveZone
    {
        get
        {
            lock (_gate)
            {
                if (!_zoneRead)
                {
                    _zone = ZoneIdentifier.Read(_archivePath);
                    _zoneRead = true;
                }
                return _zone;
            }
        }
    }

    /// <summary>
    /// 압축의 인터넷 표식 영역(ZoneId — 3 = 인터넷, 표식이 없거나 못 읽으면 null). 끌기 데이터가 탐색기에 'ZoneIdentifier' 형식으로 알려,
    /// 탐색기가 놓은 곳에 새로 만드는 파일에도 표식을 붙이게 한다(<see cref="VirtualFileDataObject"/> — 임시 파일의 표식은 내용 스트림으로는 옮겨지지 않아서).
    /// </summary>
    public int? ZoneId => ArchiveZone is { } z ? ZoneIdentifier.ParseZoneId(z) : null;

    /// <summary>
    /// 서비스가 꺼낸 파일 하나 찾기(<see cref="LocateExtracted"/>). 그래도 못 찾으면 그 폴더에 하나뿐인 파일(항목 하나만 꺼냈을 때의 마지막 대비).
    /// </summary>
    internal static string? FindExtracted(string dest, string entryKey)
    {
        try
        {
            if (LocateExtracted(dest, entryKey, isDirectory: false) is { } found) return found;
            var files = Directory.EnumerateFiles(dest, "*", SearchOption.AllDirectories).Take(2).ToList();
            return files.Count == 1 ? files[0] : null;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// 꺼낸 항목(파일 또는 폴더)이 디스크에 생긴 자리. 풀기 엔진은 이름을 <see cref="EntryPaths.ToSafeRelative"/> 로 바꾸고('회의록 10:00.txt' →
    /// '회의록 10_00.txt', 'CON.txt' → '_CON.txt', 끝의 점은 Windows 가 뗌), 같은 이름이 이미 있으면 '이름 (1).ext' 로 비켜 쓴다 — 대소문자만 다른 두 항목
    /// ('Report.txt'·'report.txt')은 고른 열쇠가 대소문자를 무시해 하나만 골라도 둘 다 꺼내진다. 그래서 같은 폴더에서
    /// ① 이름이 대소문자까지 같은 것 → ② 엔진이 비켜 쓴 '이름 (n).ext'(대소문자까지, 없으면 대소문자 무시) → ③ 대소문자 무시 순서로 찾는다(W2-04 1차 검증).
    /// ②를 대소문자 무시로도 보는 까닭: 대상 경로에 '~' 가 있으면 .NET 이 짧은 이름을 풀면서 이미 있는 파일의 실제 대소문자를 돌려줘, 엔진이 뒤엣것을
    /// 'Report (1).txt' 처럼 앞엣것 대소문자로 비켜 쓴다 — 그때 ③으로 가면 앞엣것('Report.txt')을 잘못 준다(W2-04 2차).
    /// <paramref name="claimed"/> 에 든 경로는 건너뛴다(여러 항목을 한꺼번에 찾을 때 한 자리를 두 번 주지 않게). 대상 폴더 밖이거나 없으면 null.
    /// <paramref name="listings"/> 를 주면 폴더 목록을 한 번만 읽어 같이 쓴다(한 번에 꺼낸 파일 수천 개를 짝지을 때 폴더를 파일마다 다시 읽지 않게).
    /// </summary>
    internal static string? LocateExtracted(string dest, string entryKey, bool isDirectory, IReadOnlySet<string>? claimed = null,
        Dictionary<string, HashSet<string>>? listings = null)
    {
        try
        {
            string root = Path.GetFullPath(dest + Path.DirectorySeparatorChar);
            string expected = Path.GetFullPath(Path.Combine(dest, EntryPaths.ToSafeRelative(entryKey)));
            if (!expected.StartsWith(root, StringComparison.OrdinalIgnoreCase) || expected.Length <= root.Length) return null;
            string? parent = Path.GetDirectoryName(expected);
            if (parent is null || !Directory.Exists(parent)) return null;

            string listingKey = (isDirectory ? "D|" : "F|") + parent.ToUpperInvariant();
            if (listings is null || !listings.TryGetValue(listingKey, out var names))
            {
                names = (isDirectory ? Directory.EnumerateDirectories(parent) : Directory.EnumerateFiles(parent))
                    .Select(Path.GetFileName).OfType<string>().ToHashSet(StringComparer.Ordinal);
                if (listings is not null) listings[listingKey] = names;
            }
            bool Free(string name, out string full)
            {
                full = Path.Combine(parent, name);
                return names.Contains(name) && claimed?.Contains(full) != true;
            }

            // 찾을 이름은 열쇠에서(끝의 점·공백은 Windows 처럼 뗌) — GetFullPath 는 경로에 '~' 가 있으면 이미 있는 파일의 실제 대소문자로 바꿔
            // 'report.txt' 를 'Report.txt' 로 돌려준다. 열쇠 끝이 '.'·'..' 처럼 이름이 없으면 GetFullPath 결과.
            string last = Path.GetFileName(EntryPaths.ToSafeRelative(entryKey)).TrimEnd('.', ' ');
            string want = last.Length > 0 ? last : Path.GetFileName(expected);
            if (Free(want, out var hit)) return hit;
            string stem = Path.GetFileNameWithoutExtension(want), ext = Path.GetExtension(want);
            for (int i = 1; i <= names.Count; i++)
                if (Free($"{stem} ({i}){ext}", out hit)) return hit;
            var sorted = names.OrderBy(n => n, StringComparer.Ordinal).ToList();
            for (int i = 1; i <= names.Count; i++)
            {
                string renamed = $"{stem} ({i}){ext}";
                foreach (var name in sorted)
                    if (string.Equals(name, renamed, StringComparison.OrdinalIgnoreCase) && Free(name, out hit)) return hit;
            }
            foreach (var name in sorted)
                if (string.Equals(name, want, StringComparison.OrdinalIgnoreCase) && Free(name, out hit)) return hit;
            return null;
        }
        catch
        {
            return null;
        }
    }

    private void EnsureRoot()
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_lock is not null) return;
            Directory.CreateDirectory(WorkRoot);
            // 다른 일집의 청소가 지우지 않게 '살아 있음' 표시(TempJanitor)
            _lock = new FileStream(Path.Combine(WorkRoot, ArchiveWorkspace.LockFileName), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        }
    }

    /// <summary>넘긴 스트림을 닫고 임시 폴더를 지운다(잡힌 파일은 남김). 여러 번 불러도 된다.</summary>
    public void Dispose()
    {
        List<IDisposable> streams;
        FileStream? lockStream;
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            streams = _streams.ToList();
            _streams.Clear();
            lockStream = _lock;
            _lock = null;
        }
        foreach (var s in streams)
        {
            try { s.Dispose(); } catch { /* 무시 */ }
        }
        try { lockStream?.Dispose(); } catch { /* 무시 */ }
        TempFolders.DeleteBestEffort(WorkRoot);
    }

    /// <summary>
    /// 꺼내기를 기다린다. 부른 스레드가 WPF 디스패처 스레드(끌기 중 탐색기의 요청이 UI 스레드로 들어온 경우)면 끝날 때까지 메시지를 돌린다 —
    /// 진행 창이 그려지고 [취소]가 먹는다. 아니면 그냥 기다린다.
    /// </summary>
    internal static void WaitPumping(Task task)
    {
        if (task.IsCompleted) return;
        var dispatcher = Dispatcher.FromThread(Thread.CurrentThread);
        if (dispatcher is null)
        {
            ((IAsyncResult)task).AsyncWaitHandle.WaitOne();
            return;
        }
        var frame = new DispatcherFrame();
        task.ContinueWith(_ => dispatcher.BeginInvoke(DispatcherPriority.Send, new Action(() => frame.Continue = false)), TaskScheduler.Default);
        try
        {
            Dispatcher.PushFrame(frame);
        }
        catch (InvalidOperationException)
        {
            // 디스패처 처리가 잠시 막힌 때(레이아웃 중 등) — 그냥 기다린다
            ((IAsyncResult)task).AsyncWaitHandle.WaitOne();
        }
    }
}
