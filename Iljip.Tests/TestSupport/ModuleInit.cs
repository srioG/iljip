using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;

namespace Iljip.Tests.TestSupport;

internal static class ModuleInit
{
    /// <summary>
    /// 본체는 App.OnStartup 에서 코드 페이지(CP949·CP437) 공급자를 등록한다.
    /// 테스트는 App 을 띄우지 않으므로 같은 등록을 여기서 먼저 해 둔다
    /// (안 하면 KoreanFileNameDecoder 정적 생성자가 CP949 를 못 찾아 터진다).
    /// </summary>
#pragma warning disable CA2255
    [ModuleInitializer]
#pragma warning restore CA2255
    internal static void Init()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        TestTimeZone.ApplyFromEnvironment();
        // W2-05: 일집 자료 폴더(설정 settings.json·임시 폴더)를 시험 전용 빈 폴더로 — 이 PC 사용자가 일집 설정에서 '풀고 나서 압축 파일 지우기'를
        // 켜 두었어도, 가짜 없이 만든 화면(new MainViewModel(ui))이 그 설정을 읽어 진짜 휴지통으로 픽스처를 보내는 일이 없게(설정은 늘 기본값).
        // I4-1: 로그(AppPaths.Root\logs)도 이 폴더로 — 시험이 사용자 진짜 로그(%LOCALAPPDATA%\Iljip\logs)에 쓰지 않게.
        // 시험이 띄우는 Iljip.exe 에는 환경 변수(ApplyTo)로 같은 폴더를 준다.
        Iljip.Services.AppPaths.OverrideRootForTests(TestAppDataRoot, processWide: true);
        SweepStaleAppData();
        AppDomain.CurrentDomain.ProcessExit += (_, _) => DeleteQuietly(TestAppDataRoot);
    }

    /// <summary>시험 중 일집 자료 폴더(<see cref="Iljip.Services.AppPaths.Root"/>) — 프로세스마다 따로.</summary>
    internal static readonly string TestAppDataRoot =
        System.IO.Path.Combine(System.IO.Path.GetTempPath(), "IljipTests", $"appdata-{Environment.ProcessId}");

    /// <summary>시험이 띄우는 Iljip.exe 가 이 시험 프로세스와 같은 자료 폴더(로그·설정)를 쓰게 한다(사용자 진짜 로그·settings.json 을 건드리지 않게).</summary>
    internal static System.Diagnostics.ProcessStartInfo ApplyTo(System.Diagnostics.ProcessStartInfo psi)
    {
        psi.Environment[Iljip.Services.AppPaths.TestAppDataEnvVar] = TestAppDataRoot;
        return psi;
    }

    /// <summary>
    /// 앞선 시험 실행이 남긴 appdata-&lt;pid&gt; 가운데 10분 넘게 지났고 그 pid 가 돌지 않는 것만 치운다(%TEMP%\IljipTests 안에서만 — 다른 곳은 보지 않음).
    /// testhost 는 끝날 때 ProcessExit 정리를 돌리지 않는 일이 있어(I4-1 검증 지적 — 전체 시험 뒤 22MB 폴더가 남음) 다음 시험이 곧바로 치운다(예전: 하루 지난 것만).
    /// </summary>
    private static void SweepStaleAppData()
    {
        try
        {
            string parent = System.IO.Path.GetDirectoryName(TestAppDataRoot)!;
            if (!System.IO.Directory.Exists(parent)) return;
            foreach (var dir in System.IO.Directory.EnumerateDirectories(parent, "appdata-*"))
            {
                string name = System.IO.Path.GetFileName(dir);
                if (!int.TryParse(name.AsSpan("appdata-".Length), out int pid) || pid == Environment.ProcessId) continue;
                if (System.IO.Directory.GetLastWriteTimeUtc(dir) > DateTime.UtcNow.AddMinutes(-10)) continue;
                if (IsRunning(pid)) continue;
                DeleteQuietly(dir);
            }
        }
        catch
        {
            // 청소 실패는 시험에 영향 없음
        }
    }

    /// <summary>
    /// 그 pid 로 시험 프로세스(testhost·dotnet)가 아직 도는가. pid 가 다른 프로그램(svchost 등)에 다시 쓰였으면 아니다 — 그렇지 않으면 오래된 폴더가 영영 남는다.
    /// </summary>
    private static bool IsRunning(int pid)
    {
        try
        {
            using var p = System.Diagnostics.Process.GetProcessById(pid);
            string name = p.ProcessName;
            if (!name.StartsWith("testhost", StringComparison.OrdinalIgnoreCase) && !name.Equals("dotnet", StringComparison.OrdinalIgnoreCase)) return false;
            return !p.HasExited;
        }
        catch (ArgumentException)
        {
            return false;   // 그런 프로세스 없음
        }
        catch
        {
            return true;    // 모르면 남겨 둔다
        }
    }

    /// <summary>TempDir 와 같은 방식 — 읽기 전용을 풀고 세 번까지(DLP 가 잠깐 잡고 있을 수 있음). 못 지우면 남긴다.</summary>
    private static void DeleteQuietly(string dir)
    {
        for (int attempt = 0; attempt < 3; attempt++)
        {
            try
            {
                if (!System.IO.Directory.Exists(dir)) return;
                foreach (var f in System.IO.Directory.EnumerateFiles(dir, "*", System.IO.SearchOption.AllDirectories))
                {
                    try { System.IO.File.SetAttributes(f, System.IO.FileAttributes.Normal); } catch { /* 무시 */ }
                }
                System.IO.Directory.Delete(dir, recursive: true);
                return;
            }
            catch (Exception e) when (e is System.IO.IOException or UnauthorizedAccessException)
            {
                Thread.Sleep(100 * (attempt + 1));
            }
        }
    }
}

/// <summary>
/// 시간대 독립 확인용 스위치: 환경 변수 ILJIP_TEST_TZ 에 Windows 시간대 ID(예: "UTC", "Pacific Standard Time")를 주면
/// 이 테스트 프로세스 안에서만 TimeZoneInfo.Local 을 그 시간대로 바꾼다(시스템 설정은 건드리지 않음).
/// <code>
///   $env:ILJIP_TEST_TZ = "UTC"; dotnet test D:\Python\일집\Iljip.Tests\Iljip.Tests.csproj; Remove-Item Env:ILJIP_TEST_TZ
/// </code>
/// .NET 내부 필드(TimeZoneInfo.s_cachedData._localTimeZone)를 리플렉션으로 바꾸므로, .NET 버전이 바뀌어 필드가 없으면
/// 조용히 넘어가지 않고 바로 예외를 낸다(설정했는데 안 먹은 채로 '통과'하는 일이 없게). 변수가 없으면 아무것도 안 한다.
/// </summary>
internal static class TestTimeZone
{
    public const string EnvVar = "ILJIP_TEST_TZ";

    private static TimeZoneInfo? s_osLocal;

    /// <summary>
    /// 이 PC(OS)의 진짜 현지 시간대. 흉내(<see cref="EnvVar"/>)를 켜도 이 테스트가 띄운 다른 프로세스(Iljip.exe·자동 풀림 스텁)와
    /// 7z.dll 같은 네이티브 코드는 이 시간대로 벽시계(DOS 시각)를 읽고 쓴다. 흉내가 없으면 <see cref="TimeZoneInfo.Local"/> 그대로.
    /// </summary>
    public static TimeZoneInfo OsLocal => s_osLocal ?? TimeZoneInfo.Local;

    public static void ApplyFromEnvironment()
    {
        string? id = Environment.GetEnvironmentVariable(EnvVar);
        if (string.IsNullOrWhiteSpace(id)) return;

        var tz = TimeZoneInfo.FindSystemTimeZoneById(id.Trim());
        TimeZoneInfo.ClearCachedData();
        s_osLocal = TimeZoneInfo.Local;   // 바꾸기 전 — OS 의 시간대
        var cachedField = typeof(TimeZoneInfo).GetField("s_cachedData", BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException($"{EnvVar}: TimeZoneInfo.s_cachedData 가 없음(.NET 내부 구조 변경) — 이 스위치를 고칠 것");
        object cached = cachedField.GetValue(null)
            ?? throw new InvalidOperationException($"{EnvVar}: s_cachedData 가 null");
        var localField = cached.GetType().GetField("_localTimeZone", BindingFlags.NonPublic | BindingFlags.Instance)
            ?? throw new InvalidOperationException($"{EnvVar}: CachedData._localTimeZone 이 없음(.NET 내부 구조 변경) — 이 스위치를 고칠 것");
        localField.SetValue(cached, tz);
        if (TimeZoneInfo.Local.Id != tz.Id)
            throw new InvalidOperationException($"{EnvVar}={id} 를 적용하지 못함(현재 {TimeZoneInfo.Local.Id})");
    }
}
