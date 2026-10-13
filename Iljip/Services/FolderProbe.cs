using System.IO;

namespace Iljip.Services;

/// <summary>
/// 화면 스레드에서 폴더가 있는지 볼 때(W2-09 검증 지적): 끊긴 NAS·공유 폴더(UNC '\\서버\공유'·네트워크 드라이브 'Z:')는 Directory.Exists 가
/// SMB 시간 초과까지 몇 초~수십 초 멈춘다 — 풀기 창이 열릴 때(최근 푼 곳)·풀 곳 칸에 한 글자 칠 때마다(미리 보기) 창이 굳었다.
/// 그래서 네트워크 폴더는 창이 열릴 때·칠 때는 디스크를 보지 않고, [풀기]를 누를 때 한 번만 정해진 시간까지 기다려 본다.
/// 드라이브 종류(네트워크인지)는 네트워크를 거치지 않고 바로 안다(GetDriveType).
/// </summary>
internal static class FolderProbe
{
    /// <summary>[풀기] 때 네트워크 폴더를 기다리는 시간.</summary>
    internal static TimeSpan NetworkTimeout { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>네트워크 경로인가 — UNC('\\서버\공유', '\\?\UNC\…') 또는 네트워크 드라이브('Z:\…'). 모르면 false.</summary>
    public static bool IsNetwork(string? path)
    {
        string p = (path ?? string.Empty).Trim();
        if (p.Length == 0) return false;
        if (p.StartsWith(@"\\?\UNC\", StringComparison.OrdinalIgnoreCase)) return true;
        if (p.StartsWith(@"\\?\", StringComparison.Ordinal) || p.StartsWith(@"\\.\", StringComparison.Ordinal)) p = p[4..];
        else if (p.StartsWith(@"\\", StringComparison.Ordinal) || p.StartsWith("//", StringComparison.Ordinal)) return true;
        if (p.Length < 2 || p[1] != ':' || !char.IsAsciiLetter(p[0])) return false;
        try
        {
            return new DriveInfo(p[..1]).DriveType == DriveType.Network;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// 폴더가 있는가. 로컬은 바로 보고, 네트워크는 일을 뒤로 보내 <paramref name="networkTimeout"/>(기본 <see cref="NetworkTimeout"/>)까지만 기다린다 —
    /// 그 안에 답이 없으면 null(모름 — 네트워크가 끊겼거나 느림).
    /// </summary>
    public static bool? Exists(string path, TimeSpan? networkTimeout = null)
    {
        if (!IsNetwork(path)) return SafeExists(path);
        var probe = Task.Run(() => SafeExists(path));
        return probe.Wait(networkTimeout ?? NetworkTimeout) ? probe.Result : null;
    }

    /// <summary>창이 열릴 때·칠 때 쓰는 빠른 확인: 네트워크 폴더는 보지 않고 '있음'으로 친다([풀기] 때 <see cref="Exists"/> 로 다시 봄).</summary>
    public static bool ExistsOrNetwork(string path) => IsNetwork(path) || SafeExists(path);

    private static bool SafeExists(string path)
    {
        try
        {
            return Directory.Exists(path);
        }
        catch
        {
            return false;
        }
    }
}
