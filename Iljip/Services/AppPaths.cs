using System.IO;

namespace Iljip.Services;

/// <summary>
/// 일집이 자기 것을 두는 곳(설계 §3): <c>%LOCALAPPDATA%\Iljip\</c> — 로그(logs\)와 같은 곳이고 OneDrive·로밍과 무관하다.
/// W2-01 에서는 압축된 tar 목록 캐시(<c>temp\list\&lt;guid&gt;\inner.tar</c>)만 쓴다. 나머지 temp 하위 폴더·설정 파일은 뒤 묶음이 더한다.
/// 계약 테스트는 <see cref="OverrideRootForTests"/> 로 임시 폴더를 쓴다(실제 사용자 폴더를 건드리지 않게).
/// </summary>
public static class AppPaths
{
    private static readonly AsyncLocal<string?> TestRoot = new();
    private static string? _globalTestRoot;

    /// <summary>일집 자료 폴더(없으면 만들지 않는다 — 쓰는 쪽이 만든다).</summary>
    public static string Root
    {
        get
        {
            if (TestRoot.Value is { } t) return t;
            if (_globalTestRoot is { } g) return g;
            try
            {
                string baseDir = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                if (!string.IsNullOrEmpty(baseDir)) return Path.Combine(baseDir, "Iljip");
            }
            catch
            {
                // 아래로
            }
            return Path.Combine(Path.GetTempPath(), "Iljip");
        }
    }

    /// <summary>일집 임시 폴더(<c>Root\temp</c>).</summary>
    public static string TempRoot => Path.Combine(Root, "temp");

    /// <summary>압축된 tar 목록 캐시를 두는 곳(<c>Root\temp\list</c> — W2-01).</summary>
    public static string ListCacheRoot => Path.Combine(TempRoot, "list");

    /// <summary>설정 파일(<c>Root\settings.json</c> — 설계 §4, W2-05 에서 처음 씀). 로그와 같은 곳이라 OneDrive·로밍과 무관하다.</summary>
    public static string SettingsFile => Path.Combine(Root, "settings.json");

    /// <summary>
    /// <paramref name="path"/> 가 일집 임시 폴더(<see cref="TempRoot"/>) 안인가 — 그 안의 압축(압축 안의 압축을 꺼내 연 것 등)은
    /// '풀고 나서 압축 파일 지우기'(I2-5) 대상이 아니다(일집이 알아서 치움).
    /// </summary>
    public static bool IsUnderTemp(string path)
    {
        try
        {
            string root = Path.GetFullPath(TempRoot).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            return Path.GetFullPath(path).StartsWith(root, StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// 테스트 전용: Dispose 할 때까지 <see cref="Root"/> 를 <paramref name="root"/> 로 바꾼다.
    /// <paramref name="processWide"/> 면 이 프로세스 전체(다른 테스트 흐름 포함), 아니면 이 비동기 흐름에서만.
    /// </summary>
    internal static IDisposable OverrideRootForTests(string root, bool processWide = false)
    {
        if (processWide) _globalTestRoot = root;
        else TestRoot.Value = root;
        return new Restore(processWide);
    }

    private sealed class Restore(bool processWide) : IDisposable
    {
        public void Dispose()
        {
            if (processWide) _globalTestRoot = null;
            else TestRoot.Value = null;
        }
    }
}
