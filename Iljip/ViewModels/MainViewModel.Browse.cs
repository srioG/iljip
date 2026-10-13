using System.Globalization;
using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Iljip.Models;
using Iljip.Services;

namespace Iljip.ViewModels;

// 둘러보기(W2-09 — I2-14): 이름으로 찾기(모든 폴더 — 200ms 기다렸다 찾음, Esc 로 지움), Backspace = 위 폴더(UpCommand),
// 상태 줄 '선택: 3개 · 12.4MB'(폴더는 그 아래 전부 — 크기를 모르는 항목이 있으면 '약'), 왼쪽 트리 너비 기억(ui.treeWidth — 창을 닫을 때 한 번 저장).
// 찾기는 열 때마다 만들지 않고 처음 찾을 때 이름 목록(NFC·소문자)을 한 번 만들어 둔다 — 항목 10만 개도 한 번 찾는 데 100ms 안(시험이 잰다).
public partial class MainViewModel
{
    // ───────────────────────────── 이름으로 찾기 ─────────────────────────────

    /// <summary>찾는 글자(찾기 칸). 바뀌면 <see cref="SearchDelay"/> 기다렸다 찾는다(치는 동안 매번 찾지 않게).</summary>
    [ObservableProperty]
    private string _searchText = string.Empty;

    /// <summary>찾기 칸에서 치기를 멈춘 뒤 찾기까지 기다리는 시간(기본 200ms — 시험은 줄여 씀).</summary>
    internal TimeSpan SearchDelay { get; set; } = TimeSpan.FromMilliseconds(200);

    /// <summary>지금 목록에 반영된 찾는 글자(NFC·소문자·앞뒤 공백 뗌). 빈 글자면 찾는 중이 아님.</summary>
    private string _appliedSearch = string.Empty;

    private CancellationTokenSource? _searchDelayCts;

    /// <summary>찾는 중인가(목록이 모든 폴더에서 찾은 결과 — '경로' 열이 보임).</summary>
    public bool IsSearching => _appliedSearch.Length > 0;

    /// <summary>찾기용 이름 목록(처음 찾을 때 만듦, 목록을 다시 읽으면 버림): 항목과 그 이름(NFC·소문자).</summary>
    private ArchiveEntry[]? _searchItems;
    private string[]? _searchKeys;

    /// <summary>찾기 칸을 조용히 비우는 중(<see cref="ResetSearch"/> — 기다리기를 걸지 않음).</summary>
    private bool _quietSearchText;

    partial void OnSearchTextChanged(string value)
    {
        if (!_quietSearchText) ScheduleSearch();
    }

    /// <summary>치기를 멈추면(<see cref="SearchDelay"/>) 찾는다. 앞서 기다리던 것은 버린다.</summary>
    private async void ScheduleSearch()
    {
        _searchDelayCts?.Cancel();
        var cts = _searchDelayCts = new CancellationTokenSource();
        try
        {
            await Task.Delay(SearchDelay, cts.Token);
        }
        catch (OperationCanceledException)
        {
            return;
        }
        try
        {
            if (!cts.IsCancellationRequested) ApplySearchNow();
        }
        catch (Exception ex)
        {
            Logger.LogError("이름으로 찾기 실패", ex);
        }
    }

    /// <summary>기다리지 않고 지금 찾는다(Enter·시험). 찾는 글자가 그대로면 아무것도 하지 않는다.</summary>
    public void ApplySearchNow()
    {
        _searchDelayCts?.Cancel();
        string q = NormalizeSearch(SearchText);
        if (string.Equals(q, _appliedSearch, StringComparison.Ordinal)) return;
        _appliedSearch = q;
        OnPropertyChanged(nameof(IsSearching));
        RefreshVisibleEntries();
        if (IsSearching && IsArchiveMode) StatusText = $"‘{SearchText.Trim()}’ 찾은 항목 {VisibleEntries.Count}개";
        else if (IsArchiveMode) StatusText = $"{Entries.Count}개 항목";
    }

    /// <summary>찾기를 지우고 보던 폴더 목록으로(Esc).</summary>
    [RelayCommand]
    public void ClearSearch()
    {
        _searchDelayCts?.Cancel();
        if (SearchText.Length > 0) SearchText = string.Empty;   // 이것이 다시 기다리기를 거는데 — 바로 아래에서 끝낸다
        ApplySearchNow();
    }

    /// <summary>다른 압축을 열거나 화면을 비울 때: 찾기를 조용히 지운다(목록은 부르는 쪽이 다시 그림).</summary>
    private void ResetSearch()
    {
        _searchDelayCts?.Cancel();
        _searchItems = null;
        _searchKeys = null;
        if (_appliedSearch.Length == 0 && SearchText.Length == 0) return;
        _appliedSearch = string.Empty;
        _quietSearchText = true;   // 기다리기를 걸지 않고 칸만 비운다
        try { SearchText = string.Empty; }
        finally { _quietSearchText = false; }
        OnPropertyChanged(nameof(IsSearching));
    }

    /// <summary>찾는 글자 다듬기: 앞뒤 공백을 떼고 NFC·소문자(대소문자 무시, 맥 NFD 이름도 같게).</summary>
    internal static string NormalizeSearch(string? text) =>
        EntryNames.Nfc((text ?? string.Empty).Trim()).ToLower(CultureInfo.InvariantCulture);

    /// <summary>
    /// 이름에 <paramref name="query"/>(다듬은 글자)가 든 항목 — 모든 폴더의 파일과 폴더(폴더는 트리의 폴더 모양: 크기 칸 비움).
    /// 차례는 압축 안 차례(정렬은 목록이 함).
    /// </summary>
    internal List<ArchiveEntry> FindByName(string query)
    {
        EnsureSearchIndex();
        var items = _searchItems!;
        var keys = _searchKeys!;
        var found = new List<ArchiveEntry>();
        if (query.Length == 0) return found;
        for (int i = 0; i < keys.Length; i++)
            if (keys[i].Contains(query, StringComparison.Ordinal)) found.Add(items[i]);
        return found;
    }

    /// <summary>찾기용 이름 목록을 만든다(없을 때만): 트리의 폴더(뿌리 빼고) + 모든 파일.</summary>
    private void EnsureSearchIndex()
    {
        if (_searchItems is not null && _searchKeys is not null) return;
        var items = new List<ArchiveEntry>(Entries.Count + 16);
        foreach (var root in FolderTree)
            CollectFolders(root, items, isRoot: true);
        foreach (var e in Entries)
            if (!e.IsDirectory) items.Add(e);
        var keys = new string[items.Count];
        for (int i = 0; i < keys.Length; i++)
            keys[i] = NormalizeSearch(items[i].Name);
        _searchItems = items.ToArray();
        _searchKeys = keys;
    }

    private static void CollectFolders(ArchiveTreeNode node, List<ArchiveEntry> into, bool isRoot)
    {
        if (!isRoot) into.Add(FolderEntry(node));
        foreach (var child in node.Children) CollectFolders(child, into, isRoot: false);
    }

    /// <summary>트리 폴더를 목록 한 줄 모양으로(크기 칸 비움 — 상태 줄 합계는 <see cref="FolderSize"/>).</summary>
    private static ArchiveEntry FolderEntry(ArchiveTreeNode node) => new()
    {
        Path = node.FullPath,
        IsDirectory = true,
        Size = -1,
        CompressedSize = -1,
    };

    /// <summary>목록을 다시 읽었을 때(트리를 새로 만듦): 찾기용 이름 목록·폴더 크기 계산을 버린다(찾는 글자는 그대로 — 새 목록에서 다시 찾음).</summary>
    private void InvalidateBrowseCaches()
    {
        _searchItems = null;
        _searchKeys = null;
        _folderSizes = null;
    }

    // ───────────────────────────── 위 폴더(Backspace) ─────────────────────────────

    /// <summary>
    /// 위 폴더로(Backspace — 목록에 포커스가 있고 이름을 고치는 중이 아닐 때). 찾는 중이면 찾기를 지우고 보던 폴더로. 맨 위(압축 파일)면 그대로.
    /// </summary>
    [RelayCommand]
    private void Up()
    {
        if (!IsArchiveMode) return;
        if (IsSearching)
        {
            ClearSearch();
            return;
        }
        if (SelectedFolder is not { } cur || cur.FullPath.Length == 0) return;
        int i = cur.FullPath.LastIndexOf('/');
        string parent = i < 0 ? string.Empty : cur.FullPath[..i];
        if (FindNode(FolderTree, parent) is { } node) SelectedFolder = node;
    }

    /// <summary>사용자가 폴더로 들어갈 때(트리에서 고름·목록에서 폴더 두 번 누르기): 찾는 중이면 찾기를 지우고 그 폴더로.</summary>
    public void GoToFolder(ArchiveTreeNode node)
    {
        bool wasSearching = IsSearching;
        if (wasSearching) ResetSearchAndKeepView();
        if (!ReferenceEquals(SelectedFolder, node)) SelectedFolder = node;   // → RefreshVisibleEntries
        else if (wasSearching) RefreshVisibleEntries();
    }

    /// <inheritdoc cref="GoToFolder(ArchiveTreeNode)"/>
    public void GoToFolder(string folderFullPath)
    {
        if (FindNode(FolderTree, folderFullPath.TrimEnd('/')) is { } node) GoToFolder(node);
    }

    private void ResetSearchAndKeepView()
    {
        var items = _searchItems;
        var keys = _searchKeys;
        ResetSearch();
        _searchItems = items;   // 같은 목록이면 다음 찾기에 다시 쓴다
        _searchKeys = keys;
    }

    // ───────────────────────────── 선택 합계(상태 줄) ─────────────────────────────

    /// <summary>상태 줄 오른쪽: '선택: 3개 · 12.4MB'(크기를 모르는 항목이 있으면 '약 12.4MB'). 고른 것이 없으면 빈 글자.</summary>
    [ObservableProperty]
    private string _selectionText = string.Empty;

    /// <summary>폴더 크기(그 아래 전부) 계산 — 트리 폴더 경로 → (합, 모르는 크기 있음). 처음 고를 때 한 번 만든다.</summary>
    private Dictionary<string, (long Size, bool Unknown)>? _folderSizes;

    /// <summary>목록에서 고른 것이 바뀌었을 때(화면이 부름).</summary>
    public void UpdateSelection(IReadOnlyList<ArchiveEntry> selected) => SelectionText = SelectionSummary(selected);

    /// <summary>
    /// '선택: N개 · 크기' 글자(고른 것이 없으면 빈 글자). 압축할 목록(모으는 중)의 폴더는 디스크를 다 읽어야 크기를 알 수 있어 세지 않는다 —
    /// 폴더만 골랐으면 크기 없이 '선택: 1개', 파일과 섞였으면 파일만 더해 '선택: 3개 · 파일 12.4MB'(W2-09 검증 지적 — 예전엔 '약 0B').
    /// </summary>
    internal string SelectionSummary(IReadOnlyList<ArchiveEntry> selected)
    {
        if (selected.Count == 0) return string.Empty;
        long total = 0;
        bool unknown = false;
        int counted = 0;
        foreach (var e in selected)
        {
            if (e.IsDirectory && !IsArchiveMode) continue;   // 압축할 목록의 폴더 — 크기에서 뺀다
            var (size, unk) = SizeOf(e);
            total += size;
            unknown |= unk;
            counted++;
        }
        if (counted == 0) return $"선택: {selected.Count}개";
        string sizeText = (unknown ? "약 " : string.Empty) + FormatSize(total);
        return counted < selected.Count ? $"선택: {selected.Count}개 · 파일 {sizeText}" : $"선택: {selected.Count}개 · {sizeText}";
    }

    /// <summary>항목 하나의 크기와 '모름' 여부. 폴더는 그 아래 전부(압축을 보는 중일 때만 부름).</summary>
    private (long Size, bool Unknown) SizeOf(ArchiveEntry e)
    {
        if (!e.IsDirectory) return IsSizeUnknown(e) ? (Math.Max(0, e.Size), true) : (e.Size, false);
        return FolderSize(e.Path);
    }

    /// <summary>
    /// 크기를 모르는 항목인가: 크기가 음수이거나, 크기 0 인데 압축 크기가 큰 것(순수 .bz2 처럼 원래 크기를 적어 두지 않는 형식 — 빈 파일은 압축해도 몇십 바이트를 넘지 않음).
    /// </summary>
    internal static bool IsSizeUnknown(ArchiveEntry e) => e.Size < 0 || (e.Size == 0 && e.CompressedSize > 64);

    /// <summary>트리 폴더 하나의 크기(그 아래 파일 전부). 모르는 폴더면 (0, 모름).</summary>
    internal (long Size, bool Unknown) FolderSize(string folderPath)
    {
        _folderSizes ??= ComputeFolderSizes();
        return _folderSizes.TryGetValue(folderPath.Trim('/'), out var v) ? v : (0, true);
    }

    private Dictionary<string, (long, bool)> ComputeFolderSizes()
    {
        var map = new Dictionary<string, (long, bool)>(StringComparer.OrdinalIgnoreCase);
        foreach (var root in FolderTree) Sum(root);
        return map;

        (long, bool) Sum(ArchiveTreeNode node)
        {
            long size = 0;
            bool unknown = false;
            foreach (var f in node.Files)
            {
                if (IsSizeUnknown(f)) unknown = true;
                size += Math.Max(0, f.Size);
            }
            foreach (var c in node.Children)
            {
                var (s, u) = Sum(c);
                size += s;
                unknown |= u;
            }
            map[node.FullPath] = (size, unknown);
            return (size, unknown);
        }
    }

    /// <summary>크기 글자: 512B · 3.5KB · 12.4MB · 1.25GB(상태 줄용 — 짧게).</summary>
    internal static string FormatSize(long bytes)
    {
        const double KB = 1024, MB = KB * 1024, GB = MB * 1024;
        var c = CultureInfo.InvariantCulture;
        if (bytes >= GB) return (bytes / GB).ToString("0.##", c) + "GB";
        if (bytes >= MB) return (bytes / MB).ToString("0.#", c) + "MB";
        if (bytes >= KB) return (bytes / KB).ToString("0.#", c) + "KB";
        return bytes.ToString(c) + "B";
    }

    // ───────────────────────────── 트리 너비(ui.treeWidth) ─────────────────────────────

    /// <summary>왼쪽 폴더 트리 너비(설정 — 기본 260, 쓸 수 없는 값이면 기본값).</summary>
    public double TreeWidth => UiSettings.ClampTreeWidth(Settings.Ui.TreeWidth);

    /// <summary>창을 닫을 때 한 번: 가르는 막대로 바꾼 트리 너비를 기억한다(바뀌었을 때만 저장).</summary>
    public void RememberTreeWidth(double width)
    {
        if (!double.IsFinite(width) || width <= 0) return;
        double w = UiSettings.ClampTreeWidth(Math.Round(width));
        if (Math.Abs(Settings.Ui.TreeWidth - w) < 0.5) return;
        Settings.Ui.TreeWidth = w;
        _services.Settings.Save(Settings);
    }
}
