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
/// </list>
/// 사용자 파일은 지우지 않는다 — 여기 있는 것은 모두 일집이 꺼내 둔 임시 사본이다.
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
    public static int CleanupStale(IClock? clock = null, string? tempRoot = null)
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
