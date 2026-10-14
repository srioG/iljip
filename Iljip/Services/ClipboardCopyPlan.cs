using System.IO;
using Iljip.Models;

namespace Iljip.Services;

/// <summary>
/// Ctrl+C(목록에서 고른 것 → 탐색기에 붙여넣기)의 정리 단계(IJ3-01 — 순수 논리, 창 없이 시험). 화면은 고른 것을 <c>temp\clip\guid\</c> 에 꺼낸 뒤
/// 이것으로 클립보드에 넣을 경로를 모은다.
/// <list type="bullet">
/// <item>꺼낸 자리는 <see cref="LazyEntryExtractor.LocateExtracted"/> 로 찾는다 — 풀기 엔진은 Windows 가 그대로 못 쓰는 이름을 바꿔 쓴다
///   ('회의 10:00.txt' → '회의 10_00.txt', '왜?.txt' → '왜_.txt', 'CON.txt' → '_CON.txt', '...' → '.._', 대소문자만 다른 두 파일의 뒤엣것 → 'report (1).txt').
///   폴더 목록은 폴더마다 한 번만 읽는다(수천 개를 골라도 고른 수 × 폴더 크기로 느려지지 않게 — W2-04 보관본은 고른 것마다 폴더를 다시 읽었다).</item>
/// <item>못 찾은 것이 하나라도 있으면 <see cref="Result.Missing"/> 에 — 화면은 말없이 빼지 않고 알린 뒤 아무것도 넣지 않는다.</item>
/// <item>디스크 이름이 원래 이름과 다른 것은 <see cref="Result.Renamed"/> 에 — 화면이 '이름을 바꿔 복사했어요'로 알린다(고른 것 자신, 그리고 고른 폴더 안에서
///   규칙으로 바뀌는 이름 — 폴더 안은 처음 바뀐 조각 하나만).</item>
/// <item>압축에 인터넷 표식이 있으면 꺼낸 파일마다 붙인다(<see cref="ZoneIdentifier"/>).</item>
/// <item>뒤 스레드에서 돌린다 — 진행 창에 단계 알림을 보내고, 항목마다 [취소]를 본다.</item>
/// </list>
/// </summary>
public static class ClipboardCopyPlan
{
    /// <summary>진행 창 단계 알림 문구.</summary>
    public const string StageCollecting = "복사할 파일을 정리하는 중...";

    /// <summary>고른 것 하나: 압축 안 경로('/' 구분, 앞뒤 '/' 없음)와 폴더인지.</summary>
    public sealed record Pick(string Key, bool IsDirectory);

    /// <summary>이름이 바뀐 것: 원래 이름(폴더 안이면 고른 폴더부터의 경로 — '/' 구분)과 디스크에 생긴 이름.</summary>
    public sealed record Renamed(string Original, string OnDisk);

    /// <summary>정리 결과: 클립보드에 넣을 경로(고른 것 자체 — 최상위), 못 찾은 열쇠, 이름이 바뀐 것.</summary>
    public sealed record Result(IReadOnlyList<string> Paths, IReadOnlyList<string> Missing, IReadOnlyList<Renamed> Renamed);

    /// <summary>
    /// 고른 것 → 꺼낼 열쇠. 같은 열쇠는 하나만(대소문자까지 같을 때만 — 리눅스에서 만든 압축의 'Report.txt'·'report.txt' 는 둘 다, 끌기와 같게).
    /// </summary>
    public static IReadOnlyList<Pick> Picks(IEnumerable<ArchiveEntry> selected) =>
        selected.Select(e => new Pick(DragPayloadBuilder.NormalizeKey(e.Path), e.IsDirectory))
            .Where(p => p.Key.Length > 0)
            .DistinctBy(p => p.Key, StringComparer.Ordinal)
            .ToList();

    /// <summary>
    /// 꺼낸 <paramref name="dest"/> 에서 고른 것의 자리를 찾고, 바뀐 이름을 모으고, 인터넷 표식을 붙인다(못 찾은 것이 있으면 표식은 붙이지 않음 — 화면이 사본을 지움).
    /// 취소면 <see cref="OperationCanceledException"/>.
    /// </summary>
    /// <param name="allEntries">압축 전체 항목(고른 폴더 안에서 이름이 바뀌는 것을 찾을 때).</param>
    /// <param name="zone">압축의 인터넷 표식 내용(없으면 null).</param>
    public static Result Collect(string dest, IReadOnlyList<Pick> picks, IReadOnlyCollection<ArchiveEntry> allEntries, byte[]? zone,
        IProgress<ArchiveProgress>? progress, CancellationToken ct)
    {
        Report(progress, 0, picks.Count);
        var claimed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var listings = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        var tops = new List<string>();
        var missing = new List<string>();
        var renamed = new List<Renamed>();
        for (int i = 0; i < picks.Count; i++)
        {
            ct.ThrowIfCancellationRequested();
            if (i > 0 && i % 250 == 0) Report(progress, i, picks.Count);
            var (key, isDir) = picks[i];
            // 대소문자만 다른 두 파일이 한 자리를 나눠 갖지 않게 이미 준 자리는 건너뜀(claimed). 폴더는 엔진이 합친다(대소문자만 다른 두 폴더 → 한 폴더).
            string? path = LazyEntryExtractor.LocateExtracted(dest, key, isDir, isDir ? null : claimed, listings)
                           ?? (picks.Count == 1 && !isDir ? LazyEntryExtractor.FindExtracted(dest, key) : null);
            if (path is null)
            {
                missing.Add(key);
                continue;
            }
            if (!claimed.Add(path)) continue;   // 같은 자리(합쳐진 폴더)는 한 번만
            tops.Add(path);
            string original = EntryNames.Nfc(LastSegment(key));
            string onDisk = Path.GetFileName(path);
            if (!string.Equals(original, onDisk, StringComparison.Ordinal)) renamed.Add(new Renamed(original, onDisk));
        }
        if (missing.Count == 0)
        {
            renamed.AddRange(NestedRenames(picks, allEntries, ct));
            if (zone is not null)
                foreach (var t in tops) ZoneIdentifier.Apply(zone, t, ct);
        }
        Report(progress, picks.Count, picks.Count);
        return new Result(tops, missing, renamed);
    }

    /// <summary>
    /// 고른 폴더 안에서 디스크 이름이 바뀌는 항목(풀기 규칙 <see cref="EntryPaths.SafeComponent"/>, 끝의 점·공백은 Windows 가 뗌) — 항목마다 처음 바뀐 조각 하나만,
    /// 같은 조각은 한 번만('자료:모음' 폴더 안 파일 100개 → '자료:모음' 하나). 대소문자만 다른 두 항목의 '이름 (1)' 은 폴더 안에서는 세지 않는다(디스크를 다 읽어야 해서).
    /// </summary>
    internal static List<Renamed> NestedRenames(IReadOnlyList<Pick> picks, IReadOnlyCollection<ArchiveEntry> allEntries, CancellationToken ct)
    {
        var result = new List<Renamed>();
        var folders = new HashSet<string>(picks.Where(p => p.IsDirectory).Select(p => p.Key), StringComparer.OrdinalIgnoreCase);
        if (folders.Count == 0) return result;
        var seen = new HashSet<string>(StringComparer.Ordinal);
        int n = 0;
        foreach (var e in allEntries)
        {
            if ((++n & 1023) == 0) ct.ThrowIfCancellationRequested();
            string k = DragPayloadBuilder.NormalizeKey(e.Path);
            // 가장 얕은 고른 폴더(그 이름부터 보인다)
            int start = -1;
            for (int s = k.IndexOf('/'); s > 0; s = k.IndexOf('/', s + 1))
            {
                if (!folders.Contains(k[..s])) continue;
                start = k.LastIndexOf('/', s - 1) + 1;   // 고른 폴더 이름의 첫 글자
                break;
            }
            if (start < 0) continue;
            var parts = k[start..].Split('/');
            for (int j = 1; j < parts.Length; j++)   // 0번은 고른 폴더 자신(그 이름은 위에서 봄)
            {
                string seg = parts[j];
                if (seg.Length == 0 || seg is "." or "..") continue;
                string original = EntryNames.Nfc(seg);
                string safe = DiskName(original);
                if (string.Equals(original, safe, StringComparison.Ordinal)) continue;
                string shown = EntryNames.Nfc(string.Join("/", parts, 0, j + 1));
                if (seen.Add(shown)) result.Add(new Renamed(shown, safe));
                break;
            }
        }
        return result;
    }

    /// <summary>조각 하나가 디스크에 생기는 이름(풀기 규칙 + Windows 가 끝의 점·공백을 뗌).</summary>
    internal static string DiskName(string segment)
    {
        string safe = EntryPaths.SafeComponent(segment).TrimEnd('.', ' ');
        return safe.Length == 0 ? EntryPaths.SafeComponent(segment) : safe;
    }

    /// <summary>
    /// 화면에 보일 '이름을 바꿔 복사했어요' 안내(최대 5개 + '외 N개'). 바뀐 것이 없으면 null.
    /// </summary>
    public static string? RenamedNotice(IReadOnlyList<Renamed> renamed)
    {
        if (renamed.Count == 0) return null;
        var lines = renamed.Take(5).Select(r => $"- {r.Original} → {r.OnDisk}").ToList();
        if (renamed.Count > 5) lines.Add($"외 {renamed.Count - 5}개");
        return "복사했어요. 다만 아래 이름은 Windows 에서 그대로 쓸 수 없어서(쓸 수 없는 글자·이름이거나 같은 이름이 있어서) 바꿔서 복사했어요:\n"
               + string.Join("\n", lines);
    }

    private static void Report(IProgress<ArchiveProgress>? progress, int done, int total) =>
        progress?.Report(new ArchiveProgress { CurrentFile = StageCollecting, IsStage = true, FilesProcessed = done, TotalFiles = total });

    private static string LastSegment(string key)
    {
        int i = key.LastIndexOf('/');
        return i < 0 ? key : key[(i + 1)..];
    }
}
