using System.IO;

namespace Iljip.Services;

/// <summary>
/// 일집이 자기 것을 두는 곳(설계 §3): <c>%LOCALAPPDATA%\Iljip\</c> — 로그(logs\)와 같은 곳이고 OneDrive·로밍과 무관하다.
/// W2-01 에서는 압축된 tar 목록 캐시(<c>temp\list\&lt;guid&gt;\inner.tar</c>)만 쓴다. 나머지 temp 하위 폴더·설정 파일은 뒤 묶음이 더한다
/// (W2-04: open·drag·clip·preview 와 settings.json).
/// 계약 테스트는 <see cref="OverrideRootForTests"/> 로 임시 폴더를 쓴다(실제 사용자 폴더를 건드리지 않게). 시험이 띄운 exe 는
/// 환경 변수 <see cref="TestAppDataEnvVar"/> 로 같은 폴더를 받는다(I4-1 — 로그 <c>logs\</c> 도 이 아래).
/// </summary>
public static class AppPaths
{
    private static readonly AsyncLocal<string?> TestRoot = new();
    private static string? _globalTestRoot;

    /// <summary>
    /// 시험이 띄운 Iljip.exe(따로 도는 프로세스)에게 자료 폴더를 알려 주는 환경 변수 이름(I4-1 — 시험이 사용자 진짜 로그·설정을 쓰거나 읽지 않게).
    /// 비어 있지 않은 절대 경로일 때만 쓴다. 사용자 PC 에는 없다.
    /// </summary>
    public const string TestAppDataEnvVar = "ILJIP_TEST_APPDATA";

    /// <summary>
    /// 일집 자료 폴더(없으면 만들지 않는다 — 쓰는 쪽이 만든다). 찾는 순서: 시험 흐름(AsyncLocal) → 시험 프로세스 전체 →
    /// 환경 변수 <see cref="TestAppDataEnvVar"/>(시험이 띄운 exe) → <c>%LOCALAPPDATA%\Iljip</c> → <c>%TEMP%\Iljip</c>.
    /// </summary>
    public static string Root
    {
        get
        {
            if (TestRoot.Value is { } t) return t;
            if (_globalTestRoot is { } g) return g;
            if (FromEnvironment() is { } e) return e;
            return DefaultRoot();
        }
    }

    /// <summary>환경 변수가 없을 때의 자료 폴더 — 순수 함수(환경 변수·시험 덮어쓰기를 보지 않음).</summary>
    public static string DefaultRoot()
    {
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

    private static string? FromEnvironment()
    {
        try
        {
            string? v = Environment.GetEnvironmentVariable(TestAppDataEnvVar);
            if (string.IsNullOrWhiteSpace(v)) return null;
            v = v.Trim();
            return Path.IsPathFullyQualified(v) ? v : null;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>일집 임시 폴더(<c>Root\temp</c>).</summary>
    public static string TempRoot => Path.Combine(Root, "temp");

    /// <summary>압축된 tar 목록 캐시를 두는 곳(<c>Root\temp\list</c> — W2-01).</summary>
    public static string ListCacheRoot => Path.Combine(TempRoot, "list");

    // ───── W2-06(I3-3 고친 파일 다시 넣기)·IJ3-01(W2-04 다시 — 끌어내기·Ctrl+C·미리 보기)에서 더한 것 ─────

    /// <summary>압축 안 파일을 열 때 꺼내 두는 곳(<c>temp\open\&lt;key16&gt;\&lt;순번&gt;\</c> — <see cref="ArchiveWorkspace"/>).</summary>
    public static string OpenRoot => Path.Combine(TempRoot, "open");

    /// <summary>목록에서 끌어낼 때 놓은 순간에 꺼내는 곳(<c>temp\drag\&lt;guid&gt;\</c> — <see cref="LazyEntryExtractor"/>).</summary>
    public static string DragRoot => Path.Combine(TempRoot, "drag");

    /// <summary>
    /// Ctrl+C 로 복사할 때 꺼내 두는 곳(<c>temp\clip\&lt;guid&gt;\</c>). 앱을 켜 둔 동안은 최근 2개, 닫을 때 마지막 1개만 남기고(클립보드에서 밀려난 것은 지움),
    /// 남긴 것은 붙여넣기가 늦을 수 있어 24시간 둔다(<see cref="TempJanitor"/>).
    /// </summary>
    public static string ClipRoot => Path.Combine(TempRoot, "clip");

    /// <summary>그림 미리 보기용으로 잠깐 꺼내는 곳(<c>temp\preview\&lt;guid&gt;\</c>). 읽은 뒤 바로 지운다.</summary>
    public static string PreviewRoot => Path.Combine(TempRoot, "preview");

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
