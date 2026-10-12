using Iljip.Tests.TestSupport;

namespace Iljip.Tests.Contract;

/// <summary>
/// 해제 대상에 같은 이름이 이미 있을 때: 덮어쓰지 않고 '이름 (1).확장자'로 비켜 쓴다(원본 비파괴).
/// 엔진을 7z.dll 등으로 바꾸면 기본값이 '덮어쓰기'인 경우가 많아 꼭 지켜야 할 계약.
/// </summary>
public class CollisionTests
{
    [Fact]
    public async Task 같은_곳에_두번_풀면_기존_파일을_덮지_않고_이름1로_비켜쓴다()
    {
        var fx = FixtureCatalog.Get("zip_default");
        using var tmp = new TempDir("col");
        string dest = tmp.P("out");
        var svc = Svc.For(fx.FullPath);
        await svc.ExtractAsync(fx.FullPath, dest);

        // 사용자가 풀린 파일을 고쳐 둔 상황
        string mine = Path.Combine(dest, "일집_샘플", "readme.txt");
        File.WriteAllText(mine, "사용자가 고친 내용");
        string mineSha = TreeAssert.Sha256(mine);

        await svc.ExtractAsync(fx.FullPath, dest);

        Assert.Equal(mineSha, TreeAssert.Sha256(mine));   // 덮어쓰지 않음
        string renamed = Path.Combine(dest, "일집_샘플", "readme (1).txt");
        Assert.True(File.Exists(renamed));
        Assert.Equal(fx.Expected.Single(e => e.Path == "일집_샘플/readme.txt").Sha256, TreeAssert.Sha256(renamed));
        Assert.True(File.Exists(Path.Combine(dest, "일집_샘플", "하위폴더", "2단계", "3단계", "깊은 파일 [끝] (1).txt")));
        Assert.True(File.Exists(Path.Combine(dest, "일집_샘플", "빈파일 (1).txt")));
    }

    [Fact]
    public async Task 세번째는_이름2()
    {
        var fx = FixtureCatalog.Get("tar_default");
        using var tmp = new TempDir("col3");
        string dest = tmp.P("out");
        var svc = Svc.For(fx.FullPath);
        for (int i = 0; i < 3; i++)
            await svc.ExtractAsync(fx.FullPath, dest);
        Assert.True(File.Exists(Path.Combine(dest, "일집_샘플", "견적서_2026 (2).txt")));
        Assert.False(File.Exists(Path.Combine(dest, "일집_샘플", "견적서_2026 (3).txt")));
    }

    /// <summary>파일 이름과 같은 '폴더'가 이미 있어도 멈추지 않고 비켜 쓴다.</summary>
    [Fact]
    public async Task 같은_이름의_폴더가_있어도_비켜쓴다()
    {
        var fx = FixtureCatalog.Get("zip_default");
        using var tmp = new TempDir("cold");
        string dest = tmp.P("out");
        Directory.CreateDirectory(Path.Combine(dest, "일집_샘플", "견적서_2026.txt"));
        await Svc.For(fx.FullPath).ExtractAsync(fx.FullPath, dest);
        Assert.True(Directory.Exists(Path.Combine(dest, "일집_샘플", "견적서_2026.txt")));
        Assert.True(File.Exists(Path.Combine(dest, "일집_샘플", "견적서_2026 (1).txt")));
    }

    /// <summary>bz2 단일 스트림 경로도 같은 규칙.</summary>
    [Fact]
    public async Task bz2도_덮어쓰지_않는다()
    {
        var fx = FixtureCatalog.Get("bz2_single_python");
        using var tmp = new TempDir("colb");
        string dest = tmp.P("out");
        var svc = Svc.For(fx.FullPath);
        await svc.ExtractAsync(fx.FullPath, dest);
        await svc.ExtractAsync(fx.FullPath, dest);
        Assert.Equal(new[] { "견적서_2026 (1).txt", "견적서_2026.txt" }, TreeAssert.Files(dest));
    }
}
