using System.Text;
using Iljip.Services;
using Iljip.Tests.TestSupport;

namespace Iljip.Tests.Contract;

/// <summary>
/// [4차 통합 점검(2026-10-18)] 4차 묶음 I4-1(압축 검사·오류 원인 문제 창·깨진 ZIP 고치기 — IJ3-02 다시)은 검증에서 되돌려
/// <c>stash@{0}</c> <c>iljip-wave4-I4-1-failed</c> 에 있다. main 에 들어온 것은 그 묶음의 '시험 로그 격리'(B) 하나 —
/// 계약 테스트와 시험이 띄우는 Iljip.exe 가 사용자 진짜 로그(<c>%LOCALAPPDATA%\Iljip\logs</c> — 재사용장치 지켜보기 명령이 보는 곳)에 쓰지 않게
/// (3차까지는 전체 시험 한 번이 그 로그에 수백 KB~수백 MB 를 남겼다 — 10/13 85MB·10/14 255MB). 코드는 보관분과 같은 글자 그대로 들여
/// 보관분을 다시 얹을 때 겹치지 않게 했다.
/// </summary>
public class Wave4IntegrationTests
{
    /// <summary>
    /// 계약: 시험 프로세스의 일집 자료 폴더·로그 폴더는 시험 전용 폴더(<c>%TEMP%\IljipTests\appdata-pid</c>)이고, 사용자 PC 의 자료 폴더는 예전 그대로
    /// <c>%LOCALAPPDATA%\Iljip</c>(로그는 그 아래 logs — MANUAL_CHECKLIST 8절 지켜보기 명령). 실제로 한 줄 써 보면 시험 폴더에 남는다.
    /// </summary>
    [Fact]
    public void 시험의_자료_폴더와_로그는_사용자_폴더가_아니다()
    {
        string user = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Iljip");
        Assert.Equal(user, AppPaths.DefaultRoot());
        Assert.Equal(ModuleInit.TestAppDataRoot, AppPaths.Root);
        Assert.Equal(Path.Combine(ModuleInit.TestAppDataRoot, "logs"), Logger.LogDirectory);
        Assert.False(Logger.LogDirectory.StartsWith(user + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase), Logger.LogDirectory);

        string marker = "4차 통합 점검 — 로그 격리 확인 " + Guid.NewGuid().ToString("N");
        Logger.LogError(marker, new InvalidOperationException("시험 기록"));

        bool found = false;
        foreach (int day in new[] { 0, -1, 1 })   // 자정 언저리
        {
            string file = Path.Combine(Logger.LogDirectory, $"error-{DateTime.Now.AddDays(day):yyyyMMdd}.log");
            if (!File.Exists(file)) continue;
            using var fs = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var reader = new StreamReader(fs, Encoding.UTF8);
            if (reader.ReadToEnd().Contains(marker, StringComparison.Ordinal)) { found = true; break; }
        }
        Assert.True(found, $"시험 로그 폴더({Logger.LogDirectory})에 방금 쓴 줄이 없음");
    }

    /// <summary>계약: 시험이 띄우는 Iljip.exe 에는 환경 변수로 시험 자료 폴더를 준다(exe 의 로그·설정·임시 폴더도 사용자 폴더가 아니게).</summary>
    [Fact]
    public void 시험이_띄우는_exe_에는_시험_자료_폴더를_넘긴다()
    {
        Assert.Equal("ILJIP_TEST_APPDATA", AppPaths.TestAppDataEnvVar);
        var psi = ModuleInit.ApplyTo(new System.Diagnostics.ProcessStartInfo("Iljip.exe"));
        Assert.Equal(ModuleInit.TestAppDataRoot, psi.Environment[AppPaths.TestAppDataEnvVar]);
        Assert.True(Path.IsPathFullyQualified(ModuleInit.TestAppDataRoot), ModuleInit.TestAppDataRoot);   // 본체는 절대 경로일 때만 받음
    }

    /// <summary>
    /// 계약(소스): 시험 코드에서 빌드된 Iljip.exe 를 띄우는 곳(<c>IljipExe.Path</c> 로 만든 ProcessStartInfo)은 모두 <c>ModuleInit.ApplyTo</c> 를 거친다 —
    /// 새 시험이 그냥 띄우면 그 exe 가 사용자 진짜 로그에 다시 쓰게 된다.
    /// </summary>
    [Fact]
    public void 시험이_Iljip_exe_를_띄우는_곳은_모두_시험_자료_폴더를_넘긴다()
    {
        string testsDir = RepoFiles.PathOf("Iljip.Tests");
        string needle = "new ProcessStartInfo(" + "IljipExe.Path";   // 이 파일 자신은 세지 않게 나눠 씀
        int launches = 0;
        foreach (var file in Directory.EnumerateFiles(testsDir, "*.cs", SearchOption.AllDirectories))
        {
            string rel = Path.GetRelativePath(testsDir, file);
            if (rel.StartsWith("bin" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
                || rel.StartsWith("obj" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) continue;
            string text = File.ReadAllText(file);
            int starts = Count(text, needle);
            if (starts == 0) continue;
            launches += starts;
            Assert.True(Count(text, "ModuleInit.ApplyTo(psi)") >= starts, $"{rel}: Iljip.exe 를 띄우는 {starts}곳 가운데 ModuleInit.ApplyTo 를 거치지 않는 곳이 있음");
        }
        Assert.True(launches >= 2, $"Iljip.exe 를 띄우는 곳을 {launches}개만 찾음(CommandLineTests·ReuseDeviceContractTests)");

        static int Count(string s, string part)
        {
            int n = 0;
            for (int i = s.IndexOf(part, StringComparison.Ordinal); i >= 0; i = s.IndexOf(part, i + part.Length, StringComparison.Ordinal)) n++;
            return n;
        }
    }
}
