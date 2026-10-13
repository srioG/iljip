using System.IO;

namespace Iljip.Services;

/// <summary>
/// 시작 화면의 '최근 파일' 한 줄(W2-11, I3-15). <see cref="Exists"/> 가 false 면 화면이 흐리게 '없음'으로 보이고, 누르면 '목록에서 뺄까요?'를 묻는다.
/// </summary>
public sealed record RecentFileItem(string Path, DateTime LastOpenedUtc, bool Exists)
{
    public string Name => System.IO.Path.GetFileName(Path);

    public string Folder => System.IO.Path.GetDirectoryName(Path) ?? string.Empty;

    public bool IsMissing => !Exists;

    /// <summary>마지막으로 연 때(이 PC 시각, '2026-10-14 09:30').</summary>
    public string LastOpenedText => LastOpenedUtc == default ? string.Empty : LastOpenedUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm");
}

/// <summary>
/// 최근 연 압축 파일(W2-11, I3-15 — 설정 recent.files). 압축 파일을 열 때마다 맨 앞에 넣고(같은 파일 — 대소문자 무시 — 은 하나로), 최대 <see cref="MaxFiles"/>개.
/// 임시 폴더(일집 temp\open 등·Windows 임시 폴더·Outlook 첨부 임시 폴더) 안의 압축은 넣지 않는다 — 곧 사라져 목록에 '없음'만 쌓인다.
/// 저장은 하지 않는다(부르는 쪽이 — 열 때마다 쓰지 않고 창을 닫을 때 한 번, 사용자가 목록을 지울 때는 바로 — DLP 연속 재기록 경합 교훈).
/// 저장하지 않은 동안 이 창에서 넣은 경로·뺀 경로를 기억해(<see cref="AddedThisSession"/>·<see cref="RemovedThisSession"/>) 저장할 때
/// 저장된 목록(다른 창이 넣거나 뺐을 수 있음)에 그것만 얹는다(<see cref="Merge"/> — W2-11 검증 지적: 창 두 개가 서로의 것을 지우거나 되살리지 않게).
/// </summary>
public sealed class RecentFilesService
{
    /// <summary>기억하는 수.</summary>
    public const int MaxFiles = 10;

    private readonly AppSettings _settings;
    private readonly Func<string, bool> _excluded;
    private readonly IClock _clock;
    private readonly HashSet<string> _removed = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _added = new(StringComparer.OrdinalIgnoreCase);

    /// <param name="settings">설정(recent.files 를 바로 고침).</param>
    /// <param name="isExcluded">넣지 않을 경로(임시 폴더 등). null 이면 일집 임시 폴더만.</param>
    /// <param name="clock">지금 시각(시험은 가짜).</param>
    public RecentFilesService(AppSettings settings, Func<string, bool>? isExcluded = null, IClock? clock = null)
    {
        _settings = settings.Normalize();
        _excluded = isExcluded ?? TempLocations.IsIljipTemp;
        _clock = clock ?? SystemClock.Instance;
    }

    /// <summary>기억한 목록(최근 것이 맨 앞).</summary>
    public IReadOnlyList<RecentFile> Files => _settings.Recent.Files;

    /// <summary>저장하지 않은 동안 이 창에서 목록에서 뺀 경로(저장할 때 저장된 목록에서도 뺌).</summary>
    public IReadOnlyCollection<string> RemovedThisSession => _removed;

    /// <summary>저장하지 않은 동안 이 창에서 연 압축(최근 것이 맨 앞 — 저장할 때 저장된 목록에 얹음). 사본.</summary>
    public IReadOnlyList<RecentFile> AddedThisSession =>
        _settings.Recent.Files.Where(f => f is not null && _added.Contains(f.Path))
            .Select(f => new RecentFile { Path = f.Path, LastOpenedUtc = f.LastOpenedUtc }).ToList();

    /// <summary>저장하지 않은 바뀜(넣거나 뺀 것)이 있는가.</summary>
    public bool HasUnsavedChanges => _added.Count > 0 || _removed.Count > 0;

    /// <summary>저장했다 — 넣고 뺀 것은 이제 저장된 목록에 있으므로 잊는다(다음 저장에서 다른 창이 그 뒤에 바꾼 것을 되돌리지 않게).</summary>
    public void MarkSaved()
    {
        _added.Clear();
        _removed.Clear();
    }

    /// <summary>연 압축 파일을 맨 앞에 넣는다. 넣었으면 true(임시 폴더 안이면 넣지 않고 false).</summary>
    public bool Add(string path)
    {
        string full = Full(path);
        if (full.Length == 0) return false;
        bool excluded;
        try { excluded = _excluded(full); }
        catch { excluded = false; }
        if (excluded) return false;
        var list = _settings.Recent.Files;
        list.RemoveAll(f => Same(f.Path, full));
        list.Insert(0, new RecentFile { Path = full, LastOpenedUtc = _clock.UtcNow });
        Trim(list);
        _removed.Remove(full);
        _added.Add(full);
        return true;
    }

    /// <summary>목록에서 뺀다(파일은 그대로). 뺐으면 true.</summary>
    public bool Remove(string path)
    {
        string full = Full(path);
        int n = _settings.Recent.Files.RemoveAll(f => Same(f.Path, full) || Same(f.Path, path));
        string key = full.Length > 0 ? full : path;
        if (n > 0 || full.Length > 0) _removed.Add(key);
        _added.Remove(key);
        return n > 0;
    }

    /// <summary>목록을 모두 비운다(파일은 그대로). 비웠으면 true.</summary>
    public bool Clear()
    {
        var list = _settings.Recent.Files;
        if (list.Count == 0) return false;
        foreach (var f in list) _removed.Add(Full(f.Path) is { Length: > 0 } p ? p : f.Path);
        _added.Clear();
        list.Clear();
        return true;
    }

    /// <summary>
    /// 화면에 보일 줄(최근 순, 같은 파일은 하나, 최대 <see cref="MaxFiles"/>개). 있는지는 <paramref name="exists"/>(기본: 로컬은 바로 보고,
    /// 네트워크(NAS·공유 폴더·네트워크 드라이브)는 보지 않고 '있음' — 끊긴 NAS 를 보다가 창이 굳지 않게, 누를 때 확인).
    /// </summary>
    public IReadOnlyList<RecentFileItem> Items(Func<string, bool>? exists = null)
    {
        exists ??= DefaultExists;
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var result = new List<RecentFileItem>();
        foreach (var f in _settings.Recent.Files)
        {
            if (f is null || string.IsNullOrWhiteSpace(f.Path) || !seen.Add(f.Path)) continue;
            bool ok;
            try { ok = exists(f.Path); }
            catch { ok = false; }
            result.Add(new RecentFileItem(f.Path, f.LastOpenedUtc, ok));
            if (result.Count == MaxFiles) break;
        }
        return result;
    }

    /// <summary>
    /// 저장할 때 합치기: 이 창이 넣은 것(<paramref name="mine"/> — 보통 <see cref="AddedThisSession"/>)과 지금 저장된 목록(<paramref name="latest"/> — 다른 창이 넣었을 수 있음)을
    /// 마지막으로 연 시각 순으로 합치고, 이 창에서 뺀 것(<paramref name="removed"/>)은 저장된 목록에서 빼고, 최대 <see cref="MaxFiles"/>개.
    /// </summary>
    public static List<RecentFile> Merge(IEnumerable<RecentFile> mine, IEnumerable<RecentFile> latest, IReadOnlyCollection<string> removed)
    {
        var removedSet = new HashSet<string>(removed, StringComparer.OrdinalIgnoreCase);
        var byPath = new Dictionary<string, RecentFile>(StringComparer.OrdinalIgnoreCase);
        var order = new List<string>();
        void Take(RecentFile f, bool fromMine)
        {
            if (f is null || string.IsNullOrWhiteSpace(f.Path)) return;
            if (!fromMine && removedSet.Contains(f.Path)) return;
            if (byPath.TryGetValue(f.Path, out var had))
            {
                if (f.LastOpenedUtc > had.LastOpenedUtc) byPath[f.Path] = f;
                return;
            }
            byPath[f.Path] = f;
            order.Add(f.Path);
        }
        foreach (var f in mine) Take(f, fromMine: true);
        foreach (var f in latest) Take(f, fromMine: false);
        // 시각이 같으면 이 창의 차례를 먼저(안정 정렬)
        var merged = order.Select(p => byPath[p]).OrderByDescending(f => f.LastOpenedUtc).Take(MaxFiles).ToList();
        return merged.Select(f => new RecentFile { Path = f.Path, LastOpenedUtc = f.LastOpenedUtc }).ToList();
    }

    private static void Trim(List<RecentFile> list)
    {
        // 같은 파일이 여럿 들어 있으면(손으로 고친 설정 등) 앞의 것만
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        list.RemoveAll(f => f is null || string.IsNullOrWhiteSpace(f.Path) || !seen.Add(f.Path));
        if (list.Count > MaxFiles) list.RemoveRange(MaxFiles, list.Count - MaxFiles);
    }

    private static bool DefaultExists(string path) => FolderProbe.IsNetwork(path) || File.Exists(path);

    private static string Full(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return string.Empty;
        try
        {
            return Path.GetFullPath(path.Trim());
        }
        catch
        {
            return path.Trim();
        }
    }

    private static bool Same(string? a, string? b) =>
        !string.IsNullOrEmpty(a) && !string.IsNullOrEmpty(b) && string.Equals(Full(a), Full(b), StringComparison.OrdinalIgnoreCase);
}
