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

    /// <summary>
    /// 같은 압축 파일을 같은 폴더에 '동시에' 여러 번 풀어도 모두 성공하고, 서로 다른 이름(원래 이름, 이름 (1) …)으로 하나씩 생긴다.
    /// 예전엔 '있는지 확인 → 만들기' 사이에 다른 해제가 끼어들어 진 쪽이 '다른 프로세스가 사용 중' 오류로 실패했다
    /// (6개 동시에 2개만 성공 — W1-A 1차 검증). 지금은 만들기를 '없을 때만 만들기'로 한 번에 한다(OutputFiles).
    /// SharpCompress 계열(gz·zip)과 EGG 서비스가 각자 해제 코드를 가지므로 둘 다 본다.
    /// </summary>
    [Theory]
    [InlineData("gz")]
    [InlineData("zip")]
    [InlineData("egg")]
    public async Task 같은_폴더에_동시에_여러번_풀어도_모두_다른_이름으로_풀린다(string kind)
    {
        const int n = 8;
        using var tmp = new TempDir("colpar");
        var payload = new byte[300_000];
        new Random(8).NextBytes(payload);
        string arc = tmp.P("src." + kind);
        switch (kind)
        {
            case "gz": RawGz.Write(tmp.P("src.txt.gz"), null, payload); arc = tmp.P("src.txt.gz"); break;
            case "zip": RawZip.Write(arc, new RawZip.Item(System.Text.Encoding.UTF8.GetBytes("src.txt"), payload)); break;
            default: RawEgg.Write(arc, new[] { new RawEgg.Item("src.txt", payload) }); break;
        }
        string dest = tmp.Dir("out");
        var svc = Svc.For(arc);
        await Task.WhenAll(Enumerable.Range(0, n).Select(_ => Task.Run(() => svc.ExtractAsync(arc, dest))));

        var want = new[] { "src.txt" }.Concat(Enumerable.Range(1, n - 1).Select(i => $"src ({i}).txt")).OrderBy(x => x, StringComparer.Ordinal);
        Assert.Equal(want, TreeAssert.Files(dest));
        string sha = TreeAssert.Sha256(payload);
        Assert.All(TreeAssert.Files(dest), f => Assert.Equal(sha, TreeAssert.Sha256(Path.Combine(dest, f))));
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
