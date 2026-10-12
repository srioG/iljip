using Iljip.Tests.TestSupport;

namespace Iljip.Tests.Contract;

/// <summary>
/// [W2-01] 빌드 출력 위치 약속: 실행 파일은 <c>Directory.Build.props</c> 대로 저장소 밖(<c>D:\Python\일집-build\dotnet\…</c>)에만 생긴다.
/// 재사용장치(<c>_find_iljip</c>)는 <c>Iljip\bin\Release</c> → <c>Iljip\bin\Debug</c> → <c>D:\Python\일집-dist</c> 순으로 Iljip.exe 를 찾으므로,
/// 저장소 안 bin 에 exe 가 생기면 재배포 없이도 매일 egg 해제가 그 판으로 바뀐다(HANDOFF '일집-dist 재배포').
/// </summary>
public class BuildOutputTests
{
    [Fact]
    public void 저장소_안_bin에는_Iljip_exe가_없다()
    {
        foreach (var config in new[] { "Release", "Debug" })
        {
            string bin = RepoFiles.PathOf("Iljip", "bin", config);
            var exes = Directory.Exists(bin)
                ? Directory.EnumerateFiles(bin, "Iljip.exe", SearchOption.AllDirectories).ToList()
                : new List<string>();
            Assert.True(exes.Count == 0, "저장소 안에 Iljip.exe 가 생김(재사용장치가 먼저 집어 감): " + string.Join(", ", exes));
        }
        // 테스트가 쓰는 본체(7z.dll 포함)는 저장소 밖 출력 폴더에서 온다
        Assert.False(AppContext.BaseDirectory.StartsWith(RepoFiles.Root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase),
            "테스트 출력이 저장소 안에 있음: " + AppContext.BaseDirectory);
    }
}
