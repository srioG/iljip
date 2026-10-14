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
        Iljip.Services.AppPaths.OverrideRootForTests(TestAppDataRoot, processWide: true);
    }

    /// <summary>시험 중 일집 자료 폴더(<see cref="Iljip.Services.AppPaths.Root"/>) — 프로세스마다 따로.</summary>
    internal static readonly string TestAppDataRoot =
        System.IO.Path.Combine(System.IO.Path.GetTempPath(), "IljipTests", $"appdata-{Environment.ProcessId}");
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
