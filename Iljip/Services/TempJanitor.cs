using System.IO;

namespace Iljip.Services;

/// <summary>
/// 일집 임시 폴더 청소(W2-04 보관본 → IJ3-01, 설계 §3). 일반 시작 때 한 번 뒤에서 부른다(창 없이 풀기 <c>--extract-to</c> 에서는 부르지 않음 — 재사용장치 쪽은 빨리 끝나야 해서).
/// <list type="bullet">
/// <item><c>temp\open·drag</c>: 하위 폴더 중 <c>owner.lock</c> 이 잡혀 있지 않은 것(주인 일집이 꺼진 것)을 지운다.
///   다른 프로그램이 아직 잡은 파일은 남긴다(다음에 다시).</item>
/// <item>잠금 파일이 없는 곳은 나이로만 본다(다른 일집이 지금 쓰는 것을 지우지 않게 — IJ3-01): <c>temp\preview</c>(그림 읽기 — 읽자마자 지우므로 남은 것은 꺼진 일집의 것)는
///   1시간, <c>temp\list</c>(압축된 tar 목록 캐시 — 그 캐시도 하루 지난 것은 지움)·<c>temp\convert</c>(다른 이름으로 저장 — 강제로 끝난 일집이 남긴 것)·
///   <c>temp\args</c>(셸 확장이 넘긴 긴 목록 파일)는 24시간.</item>
/// <item><c>temp\clip</c>: 24시간 지난 것만 지운다 — Ctrl+C 한 뒤 붙여넣기가 늦게 일어날 수 있어서(일집을 꺼도 클립보드는 남음).</item>
/// <item>청소를 시작한 뒤에 생긴 폴더는 건드리지 않는다 — 청소가 뒤에서 도는 동안 이 일집이 막 만든 폴더(아직 잠그기 전)를 지우지 않게.</item>
/// <item>I5-A: 꺼진 일집이 '연 파일 목록'(<see cref="WorkspaceManifest"/> — <c>owner.lock</c> 안)을 남긴 <c>temp\open</c> 폴더는 목록의 파일마다 본다 —
///   다른 프로그램이 아직 잡고 있으면 그대로 두고(다음에 다시), 일집을 닫은 뒤(또는 일집이 갑자기 꺼진 뒤) 고쳐 저장해 기준 내용과 다르면
///   <b>지우지 않고</b> <see cref="LeftoverFile"/> 로 알린다(시작 화면이 [압축에 넣기][폴더 열기][버리기][나중에]를 물음 — X3-F1·I3-3-close).
///   이름을 바꿨으면(일집이 꺼진 동안 Light PDF 탭 '이름 바꾸기') 새 이름을 찾아 본다. 다 그대로면 예전처럼 지운다.</item>
/// </list>
/// 일집이 꺼내 둔 임시 사본만 지운다 — 그 사본을 사용자가 고쳐 저장했고 아직 압축에 넣지 않았으면(위 I5-A) 지우지 않는다.
/// </summary>
public static class TempJanitor
{
    /// <summary>클립보드용 사본을 두는 시간.</summary>
    public static readonly TimeSpan ClipKeep = TimeSpan.FromHours(24);

    /// <summary>잠금 파일로 '살아 있음'을 보는 하위 폴더.</summary>
    internal static readonly string[] LockedKinds = { "open", "drag" };

    /// <summary>잠금 파일이 없어 나이로만 보는 하위 폴더와 그 나이(clip 은 따로 — <see cref="ClipKeep"/>).</summary>
    internal static readonly (string Kind, TimeSpan Keep)[] AgedKinds =
    {
        ("preview", TimeSpan.FromHours(1)),
        ("list", TimeSpan.FromHours(24)),
        ("convert", TimeSpan.FromHours(24)),
        ("args", TimeSpan.FromHours(24)),
    };

    /// <summary>청소한다. 지운 폴더 수를 돌려준다(실패는 조용히 넘어감).</summary>
    /// <param name="clock">나이 판단(clip 24시간 등)에 쓰는 시각(시험은 가짜).</param>
    /// <param name="tempRoot">임시 폴더 뿌리(기본 <see cref="AppPaths.TempRoot"/>).</param>
    public static int CleanupStale(IClock? clock = null, string? tempRoot = null) => Clean(clock, tempRoot, null);

    /// <summary>
    /// 청소하고, 꺼진 일집이 남긴 '압축에 아직 넣지 않은 고친 파일'(지우지 않고 남긴 것)을 <paramref name="leftovers"/> 에 더한다(I5-A — 일반 시작 때 화면이 알림).
    /// 지운 폴더 수를 돌려준다.
    /// </summary>
    public static int CleanupStale(IClock? clock, List<LeftoverFile> leftovers, string? tempRoot = null) => Clean(clock, tempRoot, leftovers);

    private static int Clean(IClock? clock, string? tempRoot, List<LeftoverFile>? leftovers)
    {
        clock ??= SystemClock.Instance;
        string root = tempRoot ?? AppPaths.TempRoot;
        DateTime startedUtc = DateTime.UtcNow;
        int deleted = 0;
        try
        {
            if (!Directory.Exists(root)) return 0;
            foreach (var kind in LockedKinds)
            {
                string dir = Path.Combine(root, kind);
                if (!Directory.Exists(dir)) continue;
                foreach (var d in SafeDirs(dir))
                {
                    if (CreatedAfter(d, startedUtc) || IsAlive(d)) continue;
                    // I5-A: 연 파일 목록이 있으면 파일마다 본다 — 아직 열려 있거나 닫은 뒤 고쳐 저장했으면 지우지 않는다
                    if (WorkspaceManifest.TryRead(Path.Combine(d, ArchiveWorkspace.LockFileName)) is { Entries.Count: > 0 } manifest
                        && InspectLeftover(d, manifest, leftovers))
                        continue;
                    if (TempFolders.DeleteBestEffort(d)) deleted++;
                }
                foreach (var f in SafeFiles(dir))
                {
                    if (CreatedAfter(f, startedUtc)) continue;
                    try { File.Delete(f); } catch { /* 남김 */ }
                }
            }

            DateTime now = clock.UtcNow;
            foreach (var (kind, keep) in AgedKinds.Append(("clip", ClipKeep)))
            {
                string dir = Path.Combine(root, kind);
                if (!Directory.Exists(dir)) continue;
                foreach (var d in SafeDirs(dir))
                {
                    if (CreatedAfter(d, startedUtc) || IsAlive(d) || !OlderThan(d, now, keep)) continue;
                    if (TempFolders.DeleteBestEffort(d)) deleted++;
                }
                foreach (var f in SafeFiles(dir))
                {
                    if (CreatedAfter(f, startedUtc) || !OlderThan(f, now, keep)) continue;
                    try { File.Delete(f); } catch { /* 남김 */ }
                }
            }
        }
        catch (Exception ex)
        {
            Logger.LogError("임시 폴더 청소 실패(다음 시작 때 다시)", ex);
        }
        return deleted;
    }

    /// <summary>
    /// 꺼진 일집의 작업 폴더를 남길지(I5-A): 목록의 파일 중 다른 프로그램이 아직 잡고 있는 것이 있거나, 기준 내용(일집이 마지막으로 넣었거나 넘긴 내용)과 달라진 것이 있으면 true.
    /// 달라진 것(잡혀 있지 않은 것)은 <paramref name="leftovers"/> 에 더한다. 다 그대로이거나 없으면 false(지움).
    /// </summary>
    private static bool InspectLeftover(string dir, WorkspaceManifest.Data manifest, List<LeftoverFile>? leftovers)
    {
        bool keep = false;
        foreach (var e in manifest.Entries)
        {
            string? file = WorkspaceManifest.ResolveFile(dir, e);
            if (file is null) continue;
            if (FileInUse.IsHeld(file))
            {
                keep = true;   // 아직 편집기에 열려 있음 — 더 고칠 수 있으니 이번엔 알리지 않고 다음에 다시
                continue;
            }
            if (!ChangedSince(file, e)) continue;
            keep = true;
            leftovers?.Add(new LeftoverFile(dir, manifest.Archive, e.Key, file, Path.GetFileName(e.Extracted)));
        }
        return keep;
    }

    /// <summary>기준(크기·시각이 같으면 그대로, 다르면 SHA-256 비교 — 못 읽으면 '바뀜'으로 — 지우지 않는 쪽)과 다른가.</summary>
    private static bool ChangedSince(string file, WorkspaceManifest.Entry e)
    {
        try
        {
            var fi = new FileInfo(file);
            if (!fi.Exists) return false;
            if (fi.Length == e.Size && fi.LastWriteTimeUtc.Ticks == e.WriteTicks) return false;
            string? sha = OpenedEntry.TrySha(file);
            return sha is null || e.Sha is null || !string.Equals(sha, e.Sha, StringComparison.Ordinal);
        }
        catch
        {
            return true;
        }
    }

    /// <summary>
    /// 폴더의 <c>owner.lock</c> 을 다른 누가(살아 있는 일집) 혼자 잡고 있는가. 잠금 파일이 없으면 아니오.
    /// 확인하려고 잠깐 열어 본 잠금은 바로 놓는다.
    /// </summary>
    public static bool IsAlive(string dir)
    {
        string lockPath = Path.Combine(dir, ArchiveWorkspace.LockFileName);
        if (!File.Exists(lockPath)) return false;
        try
        {
            using var fs = new FileStream(lockPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
            return false;
        }
        catch (FileNotFoundException)
        {
            return false;
        }
        catch (DirectoryNotFoundException)
        {
            return false;
        }
        catch (Exception)
        {
            return true;   // 잠김(공유 위반)·권한 — 지우지 않는 쪽으로
        }
    }

    private static bool CreatedAfter(string path, DateTime startedUtc)
    {
        try { return File.GetCreationTimeUtc(path) > startedUtc; }
        catch { return true; }
    }

    /// <summary>만든 지 <paramref name="keep"/> 보다 오래됐는가(못 읽으면 아니오 — 지우지 않는 쪽으로).</summary>
    private static bool OlderThan(string path, DateTime nowUtc, TimeSpan keep)
    {
        try { return nowUtc - File.GetCreationTimeUtc(path) >= keep; }
        catch { return false; }
    }

    private static List<string> SafeDirs(string dir)
    {
        try { return Directory.EnumerateDirectories(dir).ToList(); }
        catch { return new List<string>(); }
    }

    private static List<string> SafeFiles(string dir)
    {
        try { return Directory.EnumerateFiles(dir).ToList(); }
        catch { return new List<string>(); }
    }
}
