using System.IO;
using Iljip.Models;

namespace Iljip.Services;

/// <summary>
/// 끌어낼 항목 하나(W2-04 — I1-4). <see cref="RelativePath"/> 는 탐색기가 놓은 곳 아래에 만들 상대 경로('\' 구분, FILEDESCRIPTORW 의 cFileName).
/// <see cref="EntryKey"/> 는 압축 안 경로('/' 구분 — 서비스에 넘겨 그 항목만 꺼냄). 폴더는 내용 없이 이름만.
/// </summary>
public sealed record DragItem(string RelativePath, string EntryKey, bool IsDirectory, long Size, DateTime? LastModified);

/// <summary>끌기 묶음: 끌어낼 항목(폴더가 그 안 항목보다 먼저)과, 경로가 너무 길어 뺀 것(압축 안 경로).</summary>
public sealed record DragPayload(IReadOnlyList<DragItem> Items, IReadOnlyList<string> TooLong);

/// <summary>
/// 목록에서 고른 것 → 끌기 서술자 목록(순수 논리 — 창·파일 없이 시험, W2-04 §3).
/// <list type="bullet">
/// <item>파일은 이름만, 폴더는 그 이름 + 안의 모든 하위 폴더·파일(재귀). 압축에 폴더 항목이 따로 없어도 파일 경로에서 폴더를 만든다.</item>
/// <item>이름 겹침(대소문자 무시 — Windows 는 같은 이름)은 '이름 (2).ext' 로 비켜 준다.</item>
/// <item>탐색기에 넘길 이름은 안전하게 다듬는다: '.'·'..'·빈 조각은 버리고 Windows 가 못 쓰는 글자는 '_'(악성 압축의 '../' 로 놓은 곳 밖에 쓰지 않게).</item>
/// <item>FILEDESCRIPTORW 의 이름 칸은 259글자까지 — 넘는 것(과 그 안 항목)은 빼고 <see cref="DragPayload.TooLong"/> 에 적는다.</item>
/// </list>
/// </summary>
public static class DragPayloadBuilder
{
    /// <summary>FILEDESCRIPTORW.cFileName 은 MAX_PATH(260) 글자(끝 0 포함).</summary>
    public const int MaxRelativePathLength = 259;

    /// <param name="selected">목록에서 고른 것(현재 폴더의 직속 — 폴더 행은 경로만 있는 항목이어도 됨).</param>
    /// <param name="allEntries">압축 전체 항목(폴더 안을 펼칠 때 씀).</param>
    public static DragPayload Build(IEnumerable<ArchiveEntry> selected, IReadOnlyCollection<ArchiveEntry> allEntries)
    {
        var items = new List<DragItem>();
        var tooLong = new List<string>();
        var usedTop = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var usedAll = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var seenKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // 고른 것(겹친 것 뺌)을 먼저 정리 — 폴더 안 항목은 아래에서 압축 전체를 한 번만 훑어 나눠 담는다
        // (IJ3-01: 고른 폴더마다 전체를 다시 훑으면 폴더 수 × 항목 수 — 수천 개를 고르면 느림)
        var picks = new List<(ArchiveEntry Sel, string Key)>();
        foreach (var sel in selected)
        {
            string key = NormalizeKey(sel.Path);
            if (key.Length == 0 || !seenKeys.Add(key)) continue;
            picks.Add((sel, key));
        }
        var folders = new Dictionary<string, FolderBucket>(StringComparer.OrdinalIgnoreCase);
        foreach (var (sel, key) in picks)
            if (sel.IsDirectory) folders[key] = new FolderBucket();
        if (folders.Count > 0) FillBuckets(folders, allEntries);

        foreach (var (sel, key) in picks)
        {
            string name = SafeSegment(LastSegment(key));
            string top = Unique(name, usedTop);
            usedAll.Add(top);

            if (!sel.IsDirectory)
            {
                Add(new DragItem(top, key, false, Math.Max(0, sel.Size), sel.LastModified));
                continue;
            }

            // 폴더: 자신 → 하위 폴더(얕은 것부터) → 파일
            var bucket = folders[key];
            DateTime? dirTime = bucket.HasOwnEntry ? bucket.OwnTime ?? sel.LastModified : sel.LastModified;
            if (!Add(new DragItem(top, key, true, 0, dirTime))) continue;

            string prefix = key + "/";
            // 너무 길어 뺀 폴더 — 그 안 항목도 뺀다(조상을 따라 올라가며 찾음 — 뺀 폴더 수와 상관없이 빠름)
            var skipped = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            bool UnderSkipped(string rel)
            {
                if (skipped.Count == 0) return false;
                for (string p = rel; p.Length > 0; p = ParentOf(p))
                    if (skipped.Contains(p)) return true;
                return false;
            }
            foreach (var d in bucket.SubDirs.Keys.OrderBy(k => k.Count(c => c == '/')).ThenBy(k => k, StringComparer.OrdinalIgnoreCase))
            {
                if (UnderSkipped(d)) continue;
                string relPath = top + "\\" + SafeRelative(d);
                // 대소문자만 다른 두 폴더(Windows 에선 같은 폴더)는 하나로 합친다 — 이름을 바꾸면 안 파일들이 엉뚱한 곳에 감
                if (!usedAll.Add(relPath)) continue;
                if (!Add(new DragItem(relPath, prefix + d, true, 0, bucket.SubDirs[d])))
                    skipped.Add(d);
            }
            foreach (var (rel, e) in bucket.Files.OrderBy(f => f.Rel.Count(c => c == '/')).ThenBy(f => f.Rel, StringComparer.OrdinalIgnoreCase))
            {
                if (UnderSkipped(rel)) continue;
                string relPath = top + "\\" + SafeRelative(rel);
                Add(new DragItem(UniqueAll(relPath, usedAll), prefix + rel, false, Math.Max(0, e.Size), e.LastModified));
            }
        }
        return new DragPayload(items, tooLong);

        bool Add(DragItem item)
        {
            if (item.RelativePath.Length > MaxRelativePathLength)
            {
                tooLong.Add(item.EntryKey);
                return false;
            }
            items.Add(item);
            return true;
        }
    }

    /// <summary>고른 폴더 하나에 담길 것: 폴더 자신의 항목(시각), 안의 하위 폴더(상대 경로 → 시각), 안의 파일.</summary>
    private sealed class FolderBucket
    {
        public bool HasOwnEntry;
        public DateTime? OwnTime;
        public readonly Dictionary<string, DateTime?> SubDirs = new(StringComparer.OrdinalIgnoreCase);
        public readonly List<(string Rel, ArchiveEntry Entry)> Files = new();
    }

    /// <summary>
    /// 압축 전체를 한 번 훑어 고른 폴더마다 담는다. 항목마다 조상 경로를 따라 올라가며 고른 폴더인지 본다(깊이만큼 — 고른 폴더 수와 상관없음).
    /// 검색 결과처럼 고른 폴더가 서로 안에 들어 있으면 양쪽에 다 담는다(전과 같음).
    /// </summary>
    private static void FillBuckets(Dictionary<string, FolderBucket> folders, IReadOnlyCollection<ArchiveEntry> allEntries)
    {
        foreach (var e in allEntries)
        {
            string k = NormalizeKey(e.Path);
            if (k.Length == 0) continue;
            if (e.IsDirectory && folders.TryGetValue(k, out var own) && !own.HasOwnEntry)
            {
                own.HasOwnEntry = true;   // 같은 폴더 항목이 여럿이면 처음 것(전과 같음)
                own.OwnTime = e.LastModified;
            }
            for (int slash = k.LastIndexOf('/'); slash > 0; slash = k.LastIndexOf('/', slash - 1))
            {
                if (!folders.TryGetValue(k[..slash], out var bucket)) continue;
                string rel = k[(slash + 1)..];
                if (rel.Length == 0) continue;
                // 파일 경로의 부모 폴더들도 폴더로(압축에 폴더 항목이 없을 수 있음)
                string parent = e.IsDirectory ? rel : ParentOf(rel);
                for (string p = parent; p.Length > 0; p = ParentOf(p))
                {
                    if (!bucket.SubDirs.ContainsKey(p)) bucket.SubDirs[p] = null;
                    if (p == rel && e.IsDirectory) bucket.SubDirs[p] = e.LastModified;
                }
                if (!e.IsDirectory) bucket.Files.Add((rel, e));
            }
        }
    }

    /// <summary>'a/b/c' → 'a/b', 'a' → ''.</summary>
    private static string ParentOf(string rel)
    {
        int i = rel.LastIndexOf('/');
        return i < 0 ? string.Empty : rel[..i];
    }

    /// <summary>압축 안 경로를 '/' 구분·앞뒤 '/' 없음으로.</summary>
    internal static string NormalizeKey(string path) => (path ?? string.Empty).Replace('\\', '/').Trim('/');

    private static string LastSegment(string key)
    {
        int i = key.LastIndexOf('/');
        return i < 0 ? key : key[(i + 1)..];
    }

    /// <summary>'/' 구분 상대 경로 → 안전한 조각들을 '\' 로 이음('.'·'..'·빈 조각은 버림).</summary>
    private static string SafeRelative(string rel) =>
        string.Join("\\", rel.Split('/').Where(s => s.Length > 0 && s != "." && s != "..").Select(SafeSegment));

    private static readonly char[] Invalid = Path.GetInvalidFileNameChars();

    /// <summary>Windows 가 못 쓰는 글자는 '_', 끝의 점·공백은 뗌, 비면 '_'.</summary>
    internal static string SafeSegment(string name)
    {
        if (name is "." or "..") return "_";
        var chars = name.Select(c => Array.IndexOf(Invalid, c) >= 0 ? '_' : c).ToArray();
        string s = new string(chars).TrimEnd('.', ' ');
        return s.Length == 0 ? "_" : s;
    }

    /// <summary>최상위 이름 겹침 → '이름 (2).ext'.</summary>
    private static string Unique(string name, HashSet<string> used)
    {
        if (used.Add(name)) return name;
        string stem = Path.GetFileNameWithoutExtension(name), ext = Path.GetExtension(name);
        for (int i = 2; ; i++)
        {
            string cand = $"{stem} ({i}){ext}";
            if (used.Add(cand)) return cand;
        }
    }

    /// <summary>전체 상대 경로 겹침(같은 폴더에 대소문자만 다른 두 항목 등) → 마지막 조각을 '이름 (2).ext'.</summary>
    private static string UniqueAll(string relPath, HashSet<string> used)
    {
        if (used.Add(relPath)) return relPath;
        int slash = relPath.LastIndexOf('\\');
        string dir = slash < 0 ? string.Empty : relPath[..(slash + 1)];
        string name = slash < 0 ? relPath : relPath[(slash + 1)..];
        string stem = Path.GetFileNameWithoutExtension(name), ext = Path.GetExtension(name);
        for (int i = 2; ; i++)
        {
            string cand = $"{dir}{stem} ({i}){ext}";
            if (used.Add(cand)) return cand;
        }
    }
}
