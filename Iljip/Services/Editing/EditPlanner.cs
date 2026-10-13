using System.IO;

namespace Iljip.Services.Editing;

/// <summary>
/// 고치기 계획(두 편집기 공용): 압축 안 항목 목록에 지우기·이름 바꾸기·새 폴더·넣기를 차례로 적용해 '무엇이 남고, 무엇이 어떤 이름이 되고,
/// 무엇이 새로 들어가는지'를 정한다. 파일은 건드리지 않는다(순수 계획 — 실제 쓰기는 편집기가).
/// 키는 '/' 구분, 끝 '/' 없음(<see cref="Normalize"/>). 같은 이름 비교는 대소문자를 가리지 않는다(Windows 에 풀면 한 파일이 되므로).
/// </summary>
internal sealed class EditPlanner<TSource>
{
    /// <summary>원래 압축의 항목 하나.</summary>
    internal sealed class Item
    {
        public required TSource Source { get; init; }
        public required string OriginalKey { get; init; }
        public required bool IsDirectory { get; init; }
        public long Size { get; init; }
        public DateTime? Modified { get; init; }

        /// <summary>지금 키(이름을 바꿨으면 새 키).</summary>
        public string Key { get; set; } = string.Empty;

        public bool Removed { get; set; }

        public bool Renamed => !string.Equals(Key, OriginalKey, StringComparison.Ordinal);
    }

    /// <summary>새로 넣을 파일(원본 경로 → 압축 안 키). <see cref="TempKey"/> 는 임시 압축(ZIP)에 들어간 이름.</summary>
    internal sealed class AddedFile
    {
        public required string SourcePath { get; init; }
        public required string TempKey { get; init; }
        public string Key { get; set; } = string.Empty;
        public long Size { get; init; }
        public DateTime? Modified { get; init; }
        public bool Removed { get; set; }
    }

    private readonly List<Item> _items;
    private readonly List<AddedFile> _added = new();
    private readonly List<string> _addedDirs = new();

    public EditPlanner(IEnumerable<Item> items)
    {
        _items = items.ToList();
        foreach (var it in _items)
            if (it.Key.Length == 0) it.Key = it.OriginalKey;
    }

    public IReadOnlyList<Item> Items => _items;
    public IEnumerable<Item> Kept => _items.Where(i => !i.Removed);
    public IEnumerable<AddedFile> AddedFiles => _added.Where(a => !a.Removed);
    public IReadOnlyList<string> AddedDirectories => _addedDirs;

    /// <summary>뭔가 바뀌는가(지움·이름 바꿈·넣음·새 폴더).</summary>
    public bool HasChanges =>
        _items.Any(i => i.Removed || i.Renamed) || _added.Any(a => !a.Removed) || _addedDirs.Count > 0;

    /// <summary>같은 이름 '건너뛰기'로 넣지 않은 파일 수.</summary>
    public int CollisionSkipped { get; private set; }

    /// <summary>키 정리: '\' → '/', 앞뒤 '/' 뗌.</summary>
    public static string Normalize(string key) => (key ?? string.Empty).Replace('\\', '/').Trim('/');

    /// <summary>두 키를 잇는다(앞이 비면 뒤만).</summary>
    public static string Join(string folder, string rel)
    {
        folder = Normalize(folder);
        rel = Normalize(rel);
        return folder.Length == 0 ? rel : rel.Length == 0 ? folder : folder + "/" + rel;
    }

    public static string ParentOf(string key)
    {
        int i = key.LastIndexOf('/');
        return i < 0 ? string.Empty : key[..i];
    }

    private static bool Under(string key, string folder) =>
        key.Length > folder.Length && key.StartsWith(folder, StringComparison.Ordinal) && key[folder.Length] == '/';

    private static bool UnderIgnoreCase(string key, string folder) =>
        key.Length > folder.Length && key.StartsWith(folder, StringComparison.OrdinalIgnoreCase) && key[folder.Length] == '/';

    // ───────────────────────────── 지우기 ─────────────────────────────

    public void Delete(IEnumerable<string> keys)
    {
        foreach (var raw in keys)
        {
            string k = Normalize(raw);
            if (k.Length == 0) continue;
            bool any = false;
            foreach (var it in _items.Where(i => !i.Removed && (i.Key == k || Under(i.Key, k))))
            {
                it.Removed = true;
                any = true;
            }
            foreach (var a in _added.Where(a => !a.Removed && (a.Key == k || Under(a.Key, k))))
            {
                a.Removed = true;
                any = true;
            }
            if (_addedDirs.RemoveAll(d => d == k || Under(d, k)) > 0) any = true;
            if (!any) throw NotFound(k);
        }
    }

    // ───────────────────────────── 이름 바꾸기 ─────────────────────────────

    /// <summary>못 쓰는 글자(Windows 파일 이름 금지 글자 + 경로 구분자 + 제어 문자).</summary>
    private static readonly char[] Forbidden = Path.GetInvalidFileNameChars();

    /// <summary>새 이름이 쓸 수 있는가 — 못 쓰면 화면 문구, 쓸 수 있으면 null.</summary>
    public static string? ValidateName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return "이름을 넣어 주세요.";
        if (name is "." or "..") return "이 이름은 쓸 수 없어요.";
        if (name.IndexOfAny(Forbidden) >= 0) return "이름에 쓸 수 없는 글자가 있어요: \\ / : * ? \" < > |";
        if (name.EndsWith('.') || name.EndsWith(' ')) return "이름 끝에는 점(.)이나 빈칸을 쓸 수 없어요.";
        return null;
    }

    /// <summary>
    /// 압축에 이미 있던 이름(다시 넣기의 원래 이름 — 사용자가 고른 이름이 아님)을 그대로 쓸 수 있는가 — 못 쓰면 화면 문구, 쓸 수 있으면 null.
    /// 맥·리눅스에서 만든 압축의 ':' 든 이름·끝 점 이름처럼 Windows 파일 이름으로는 못 쓰는 이름도 압축 안에서는 그대로 둔다(W2-06 검증 지적 2차 —
    /// 고쳐 놓고 [넣기]를 고르면 바꾼 적 없는 이름 때문에 '쓸 수 없는 글자' 오류로 못 넣었다). 압축 안 경로를 깨는 것(빈 이름·'.'·'..'·'/'·'\'·NUL)만 막는다.
    /// </summary>
    public static string? ValidateExistingName(string? name)
    {
        if (string.IsNullOrEmpty(name)) return "이름을 넣어 주세요.";
        if (name is "." or ".." || name.IndexOfAny(PathBreakers) >= 0) return "이 이름은 쓸 수 없어요.";
        return null;
    }

    /// <summary>압축 안 경로를 깨는 글자(<see cref="ValidateExistingName"/>).</summary>
    private static readonly char[] PathBreakers = { '/', '\\', '\0' };

    public void Rename(string rawKey, string newName)
    {
        string k = Normalize(rawKey);
        if (ValidateName(newName) is { } problem) throw new ArchiveEditException(problem, entryPath: k);
        string parent = ParentOf(k);
        string newKey = parent.Length == 0 ? newName : parent + "/" + newName;
        if (newKey == k) return;

        var mine = _items.Where(i => !i.Removed && (i.Key == k || Under(i.Key, k))).ToList();
        var mineAdded = _added.Where(a => !a.Removed && (a.Key == k || Under(a.Key, k))).ToList();
        var mineDirs = _addedDirs.Where(d => d == k || Under(d, k)).ToList();
        if (mine.Count == 0 && mineAdded.Count == 0 && mineDirs.Count == 0) throw NotFound(k);

        // 같은 자리(대소문자 무시)에 다른 항목이 있으면 거절 — 자기 자신(대소문자만 바꾸기)은 빼고
        bool taken =
            _items.Any(i => !i.Removed && !mine.Contains(i) && (string.Equals(i.Key, newKey, StringComparison.OrdinalIgnoreCase) || UnderIgnoreCase(i.Key, newKey)))
            || _added.Any(a => !a.Removed && !mineAdded.Contains(a) && (string.Equals(a.Key, newKey, StringComparison.OrdinalIgnoreCase) || UnderIgnoreCase(a.Key, newKey)))
            || _addedDirs.Any(d => !mineDirs.Contains(d) && (string.Equals(d, newKey, StringComparison.OrdinalIgnoreCase) || UnderIgnoreCase(d, newKey)));
        if (taken) throw new ArchiveEditException($"‘{newName}’ 이름이 이미 있어요. 다른 이름을 골라 주세요.", entryPath: k);

        string Move(string key) => key == k ? newKey : newKey + key[k.Length..];
        foreach (var it in mine) it.Key = Move(it.Key);
        foreach (var a in mineAdded) a.Key = Move(a.Key);
        for (int i = 0; i < _addedDirs.Count; i++)
            if (mineDirs.Contains(_addedDirs[i])) _addedDirs[i] = Move(_addedDirs[i]);
    }

    // ───────────────────────────── 새 폴더 ─────────────────────────────

    public void NewFolder(string rawPath)
    {
        string k = Normalize(rawPath);
        if (k.Length == 0) return;
        foreach (var part in k.Split('/'))
            if (ValidateName(part) is { } problem) throw new ArchiveEditException(problem, entryPath: k);
        if (FileExists(k)) throw new ArchiveEditException($"‘{k.Split('/')[^1]}’ 이름의 파일이 이미 있어요. 다른 이름을 골라 주세요.", entryPath: k);
        if (DirectoryExists(k)) return;
        _addedDirs.Add(k);
    }

    // ───────────────────────────── 넣기 ─────────────────────────────

    /// <summary>
    /// 넣을 파일 하나의 자리를 정한다(같은 이름이면 처리기에 묻는다 — 범위가 없으면 '이름 (1)'). 넣지 않으면(건너뛰기) false.
    /// '덮어쓰기'면 원래 항목을 지운다. [취소]면 <see cref="CollisionCancelledException"/>.
    /// </summary>
    public bool AddFile(string sourcePath, string tempKey, string desiredKey, long size, DateTime? modified, int remaining, ICollisionResolver? resolver)
    {
        string key = Normalize(desiredKey);
        if (FindFile(key) is { } existing)
        {
            var question = new CollisionQuestion(key, existing.Size, existing.Modified ?? DateTime.MinValue, size, modified, Math.Max(0, remaining));
            var choice = resolver?.Resolve(question) ?? CollisionChoice.Rename;
            switch (choice)
            {
                case CollisionChoice.Cancel:
                    throw new CollisionCancelledException();
                case CollisionChoice.Skip:
                    CollisionSkipped++;
                    return false;
                case CollisionChoice.Overwrite:
                    existing.Remove();
                    break;
                default:
                    key = UniqueKey(key);
                    break;
            }
        }
        else if (DirectoryExists(key))
        {
            key = UniqueKey(key);   // 같은 이름의 폴더 자리에는 파일을 둘 수 없다 — 비켜 넣는다
        }
        _added.Add(new AddedFile { SourcePath = sourcePath, TempKey = tempKey, Key = key, Size = size, Modified = modified });
        return true;
    }

    /// <summary>넣는 원본의 빈 폴더. 이미 있으면(파일이든 폴더든) 넣지 않는다(같은 이름 파일은 <see cref="AvoidFiles"/> 가 먼저 비켜 둔다).</summary>
    public void AddEmptyDirectory(string desiredKey)
    {
        string k = Normalize(desiredKey);
        if (k.Length == 0 || DirectoryExists(k) || FileExists(k)) return;
        _addedDirs.Add(k);
    }

    /// <summary>
    /// 넣을 자리(<paramref name="floor"/> 폴더 + <paramref name="rel"/>)의 폴더 조각 가운데 같은 자리(대소문자 무시)에 '파일'이 이미 있는 것을 비켜 둔 키 —
    /// 폴더 '보고서' 를 넣는데 파일 '보고서' 가 있으면 '보고서 (1)/…'(같은 이름의 파일과 폴더가 함께 있으면 Windows 에 풀 수 없음 — W2-06 검증 지적.
    /// 파일을 같은 이름 폴더 자리에 넣을 때 비켜 넣는 것과 같은 규칙). <paramref name="floor"/>(넣는 자리 폴더)와 그 위는 보지 않는다.
    /// <paramref name="lastIsFolder"/> 면 마지막 조각(빈 폴더 자신)도 폴더로 본다. 한 번의 넣기 안에서 같은 폴더는 같은 이름으로 옮기려고
    /// <paramref name="moved"/>(원래 경로 → 옮긴 경로)를 같이 쓴다.
    /// </summary>
    public string AvoidFiles(string floor, string rel, bool lastIsFolder, Dictionary<string, string> moved)
    {
        floor = Normalize(floor);
        rel = Normalize(rel);
        if (rel.Length == 0) return floor;
        string original = floor;
        string current = floor;
        var parts = rel.Split('/');
        int folders = lastIsFolder ? parts.Length : parts.Length - 1;
        for (int i = 0; i < folders; i++)
        {
            original = Join(original, parts[i]);
            if (moved.TryGetValue(original, out var already))
            {
                current = already;
                continue;
            }
            string candidate = Join(current, parts[i]);
            if (FileExists(candidate))
            {
                string aside = UniqueFolderKey(candidate);
                moved[original] = aside;
                current = aside;
            }
            else
            {
                current = candidate;
            }
        }
        for (int i = folders; i < parts.Length; i++) current = Join(current, parts[i]);
        return current;
    }

    // ───────────────────────────── 찾기 ─────────────────────────────

    private sealed record Existing(long Size, DateTime? Modified, Action Remove);

    private Existing? FindFile(string key)
    {
        var item = _items.FirstOrDefault(i => !i.Removed && !i.IsDirectory && string.Equals(i.Key, key, StringComparison.OrdinalIgnoreCase));
        if (item is not null)
            return new Existing(item.Size, item.Modified, () =>
            {
                foreach (var same in _items.Where(i => !i.Removed && !i.IsDirectory && string.Equals(i.Key, key, StringComparison.OrdinalIgnoreCase)))
                    same.Removed = true;
            });
        var added = _added.FirstOrDefault(a => !a.Removed && string.Equals(a.Key, key, StringComparison.OrdinalIgnoreCase));
        return added is null ? null : new Existing(added.Size, added.Modified, () => added.Removed = true);
    }

    private bool FileExists(string key) => FindFile(key) is not null;

    private bool DirectoryExists(string key) =>
        _items.Any(i => !i.Removed && (i.IsDirectory && string.Equals(i.Key, key, StringComparison.OrdinalIgnoreCase) || UnderIgnoreCase(i.Key, key)))
        || _added.Any(a => !a.Removed && UnderIgnoreCase(a.Key, key))
        || _addedDirs.Any(d => string.Equals(d, key, StringComparison.OrdinalIgnoreCase) || UnderIgnoreCase(d, key));

    private bool Taken(string key) => FileExists(key) || DirectoryExists(key);

    /// <summary>'이름 (1).ext', '이름 (2).ext' … 중 비어 있는 첫 키.</summary>
    private string UniqueKey(string key)
    {
        string parent = ParentOf(key);
        string leaf = parent.Length == 0 ? key : key[(parent.Length + 1)..];
        string stem = Path.GetFileNameWithoutExtension(leaf);
        string ext = Path.GetExtension(leaf);
        if (stem.Length == 0) { stem = leaf; ext = string.Empty; }
        for (int n = 1; ; n++)
        {
            string cand = Join(parent, $"{stem} ({n}){ext}");
            if (!Taken(cand)) return cand;
        }
    }

    /// <summary>폴더용 '이름 (1)', '이름 (2)' … 중 비어 있는 첫 키(점 뒤를 확장자로 보지 않음).</summary>
    private string UniqueFolderKey(string key)
    {
        for (int n = 1; ; n++)
        {
            string cand = $"{key} ({n})";
            if (!Taken(cand)) return cand;
        }
    }

    private static ArchiveEditException NotFound(string key) =>
        new($"‘{key.Split('/')[^1]}’을(를) 압축 파일에서 찾지 못했어요. 목록을 새로 읽고 다시 해 주세요.", entryPath: key);
}
