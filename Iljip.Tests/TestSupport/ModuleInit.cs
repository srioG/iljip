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

    public static void ApplyFromEnvironment()
    {
        string? id = Environment.GetEnvironmentVariable(EnvVar);
        if (string.IsNullOrWhiteSpace(id)) return;

        var tz = TimeZoneInfo.FindSystemTimeZoneById(id.Trim());
        TimeZoneInfo.ClearCachedData();
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
