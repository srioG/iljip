using System.IO;

namespace Iljip.Services;

/// <summary>
/// 풀 곳으로 권하지 않는 '잠깐 두는' 폴더(W2-09 검증 지적 — 반디집의 '임시 폴더의 압축 파일은 바탕 화면에 풀기'와 같은 생각):
/// <list type="bullet">
/// <item>일집 임시 폴더(<see cref="AppPaths.TempRoot"/>) — 압축 안의 압축을 열면 여기 꺼내져 있고, 일집을 닫으면 통째로 지워진다(풀어 둔 파일도 함께 사라짐).</item>
/// <item>Windows 임시 폴더(%TEMP% — 탐색기·다른 압축 프로그램이 압축 안의 압축을 꺼내 여는 곳)·인터넷 임시 파일(INetCache — Outlook 첨부를 바로 열면
///   <c>Content.Outlook\…</c>, 숨김 폴더라 사용자가 찾아갈 수 없고 Outlook 이 지움).</item>
/// </list>
/// 그런 곳의 압축을 풀 때 풀기 창의 처음 풀 곳은 바탕 화면(없으면 최근에 푼 곳)이고, 그런 곳은 최근 푼 곳으로 기억하지 않는다.
/// 일집 임시 폴더에는 풀기 창에서 골라도 풀지 않는다(<see cref="IsIljipTemp"/> — 풀기 창이 막음).
/// 시험은 가짜 시스템 임시 폴더·바탕 화면으로 만든다(진짜 %TEMP% 아래에서 도는 시험 픽스처가 '임시'로 보이지 않게).
/// </summary>
public sealed class TempLocations
{
    private readonly string[] _systemRoots;

    /// <param name="systemRoots">Windows 임시 폴더처럼 '잠깐 두는' 곳(그 아래 전부). 일집 임시 폴더는 늘 들어간다.</param>
    /// <param name="desktop">처음 풀 곳으로 권할 바탕 화면(없으면 null).</param>
    public TempLocations(IEnumerable<string> systemRoots, string? desktop)
    {
        _systemRoots = systemRoots.Select(Clean).Where(r => r.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        Desktop = string.IsNullOrWhiteSpace(desktop) ? null : desktop;
    }

    /// <summary>바탕 화면(처음 풀 곳으로 권함).</summary>
    public string? Desktop { get; }

    /// <summary>이 PC 의 진짜 위치: %TEMP%(짧은 이름일 수 있어 LOCALAPPDATA\Temp 도)·Windows\Temp·인터넷 임시 파일, 바탕 화면.</summary>
    public static TempLocations System { get; } = CreateSystem();

    private static TempLocations CreateSystem()
    {
        var roots = new List<string>();
        void Add(Func<string?> get)
        {
            try
            {
                if (get() is { Length: > 0 } p) roots.Add(p);
            }
            catch
            {
                // 못 구하면 그 곳은 빼고
            }
        }
        string Local() => Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        Add(Path.GetTempPath);
        Add(() => Local() is { Length: > 0 } l ? Path.Combine(l, "Temp") : null);
        Add(() => Local() is { Length: > 0 } l ? Path.Combine(l, "Microsoft", "Windows", "INetCache") : null);
        Add(() => Local() is { Length: > 0 } l ? Path.Combine(l, "Microsoft", "Windows", "Temporary Internet Files") : null);
        Add(() => Environment.GetFolderPath(Environment.SpecialFolder.InternetCache));
        Add(() => Environment.GetFolderPath(Environment.SpecialFolder.Windows) is { Length: > 0 } w ? Path.Combine(w, "Temp") : null);
        string? desktop = null;
        try { desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory); } catch { /* 없으면 null */ }
        return new TempLocations(roots, desktop);
    }

    /// <summary>일집 임시 폴더(<see cref="AppPaths.TempRoot"/>) 자신이나 그 안인가 — 일집을 닫으면 지워지는 곳.</summary>
    public static bool IsIljipTemp(string? path) => IsUnderOrSame(path, AppPaths.TempRoot);

    /// <summary>'잠깐 두는' 곳(일집 임시 폴더·Windows 임시 폴더·인터넷 임시 파일) 자신이나 그 안인가.</summary>
    public bool IsTemporary(string? path) => IsIljipTemp(path) || _systemRoots.Any(r => IsUnderOrSame(path, r));

    /// <summary>
    /// 처음 풀 곳으로 권할 곳: 바탕 화면이 있으면 그것, 없으면 null(부르는 쪽이 최근에 푼 곳으로). 회사 PC 에서 바탕 화면이 네트워크로 돌려져 있으면
    /// 있는지 보지 않고 권한다(끊겨 있어도 창이 굳지 않게 — [풀기] 때 확인, <see cref="FolderProbe"/>).
    /// </summary>
    public string? SuggestedFolder()
    {
        try
        {
            return Desktop is { } d && FolderProbe.ExistsOrNetwork(d) && !IsTemporary(d) ? d : null;
        }
        catch
        {
            return null;
        }
    }

    private static bool IsUnderOrSame(string? path, string root)
    {
        string p = Clean(path);
        string r = Clean(root);
        if (p.Length == 0 || r.Length == 0) return false;
        return string.Equals(p, r, StringComparison.OrdinalIgnoreCase)
               || p.StartsWith(r + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>전체 경로로, 끝 '\' 뗌(드라이브 뿌리 'C:\' 는 'C:'). 경로가 아니면 빈 글자.</summary>
    private static string Clean(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return string.Empty;
        try
        {
            if (!Path.IsPathFullyQualified(path.Trim())) return string.Empty;
            return Path.GetFullPath(path.Trim()).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }
        catch
        {
            return string.Empty;
        }
    }
}
