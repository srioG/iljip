using Iljip.Tests.TestSupport;

namespace Iljip.Tests.Contract;

/// <summary>
/// [범위 7] ExtractEntriesAsync(선택 해제): 파일 하나, 폴더(하위 전체 포함), 선택자 표기 차이.
/// 현재 동작: 고른 항목은 '압축 안 경로 그대로'(상위 폴더 포함) 대상 폴더 아래에 풀린다.
/// </summary>
public class ExtractEntriesTests
{
    private static readonly FixtureInfo Zip = FixtureCatalog.Get("zip_default");

    private static async Task<SortedDictionary<string, string?>> Run(FixtureInfo fx, params string[] selectors)
    {
        using var tmp = new TempDir("sel");
        string dest = tmp.P("out");
        await Svc.For(fx.FullPath).ExtractEntriesAsync(fx.FullPath, dest, selectors, fx.Password);
        return TreeAssert.Snapshot(dest);
    }

    private static string Sha(FixtureInfo fx, string path) => fx.Expected.Single(e => e.Path == path).Sha256!;

    [Fact]
    public async Task 파일_하나만_고르면_그_파일만_경로째_풀린다()
    {
        var snap = await Run(Zip, "일집_샘플/견적서_2026.txt");
        Assert.Equal(new[] { "일집_샘플", "일집_샘플/견적서_2026.txt" }, snap.Keys);
        Assert.Equal(Sha(Zip, "일집_샘플/견적서_2026.txt"), snap["일집_샘플/견적서_2026.txt"]);
    }

    [Fact]
    public async Task 폴더를_고르면_하위_전체가_풀린다()
    {
        var snap = await Run(Zip, "일집_샘플/하위폴더");
        Assert.Equal(new[]
        {
            "일집_샘플",
            "일집_샘플/하위폴더",
            "일집_샘플/하위폴더/2단계",
            "일집_샘플/하위폴더/2단계/3단계",
            "일집_샘플/하위폴더/2단계/3단계/깊은 파일 [끝].txt",
            "일집_샘플/하위폴더/보고서 최종.txt",
        }, snap.Keys);
        Assert.Equal(Sha(Zip, "일집_샘플/하위폴더/2단계/3단계/깊은 파일 [끝].txt"), snap["일집_샘플/하위폴더/2단계/3단계/깊은 파일 [끝].txt"]);
    }

    /// <summary>선택자 표기: 끝 '/', 역슬래시, 대소문자 차이는 같은 것으로 본다(현재 동작).</summary>
    [Theory]
    [InlineData("일집_샘플/하위폴더/")]
    [InlineData("일집_샘플\\하위폴더\\")]
    [InlineData("/일집_샘플/하위폴더")]
    public async Task 폴더_선택자_표기차이를_허용한다(string selector)
    {
        var snap = await Run(Zip, selector);
        Assert.Contains("일집_샘플/하위폴더/보고서 최종.txt", snap.Keys);
        Assert.Contains("일집_샘플/하위폴더/2단계/3단계/깊은 파일 [끝].txt", snap.Keys);
        Assert.DoesNotContain("일집_샘플/readme.txt", snap.Keys);
    }

    [Fact]
    public async Task 대소문자가_달라도_고른다()
    {
        var snap = await Run(Zip, "일집_샘플/DATA/SAMPLE.CSV");
        Assert.Equal(Sha(Zip, "일집_샘플/data/sample.csv"), snap["일집_샘플/data/sample.csv"]);
    }

    /// <summary>폴더 이름의 '앞부분'만 같은 선택자('하위')는 '하위폴더'를 고르지 않는다.</summary>
    [Fact]
    public async Task 이름_앞부분만_같으면_고르지_않는다()
    {
        var snap = await Run(Zip, "일집_샘플/하위");
        Assert.DoesNotContain(snap.Values, v => v is not null);
    }

    /// <summary>없는 항목을 고르면 예외 없이 아무 파일도 안 풀린다.</summary>
    [Fact]
    public async Task 없는_항목을_고르면_아무것도_안_풀린다()
    {
        using var tmp = new TempDir("sel0");
        string dest = tmp.P("out");
        await Svc.For(Zip.FullPath).ExtractEntriesAsync(Zip.FullPath, dest, new[] { "없는/파일.txt" });
        Assert.Empty(TreeAssert.Snapshot(dest));
    }

    /// <summary>현재 동작 기록: 없는 항목만 골라도 대상 폴더는 만들어진다(빈 폴더로 남음).</summary>
    [Fact, Trait(Kinds.Key, Kinds.Record)]
    public async Task 기록_없는_항목만_골라도_대상폴더는_생긴다()
    {
        using var tmp = new TempDir("sel0r");
        string dest = tmp.P("out");
        await Svc.For(Zip.FullPath).ExtractEntriesAsync(Zip.FullPath, dest, new[] { "없는/파일.txt" });
        Assert.True(Directory.Exists(dest));
    }

    /// <summary>여러 개를 고르면 각각 풀린다(파일 + 폴더 섞어서).</summary>
    [Fact]
    public async Task 여러_항목을_섞어_고를_수_있다()
    {
        var snap = await Run(Zip, "일집_샘플/readme.txt", "일집_샘플/data");
        var files = snap.Where(kv => kv.Value is not null).Select(kv => kv.Key).ToList();
        Assert.Equal(new[] { "일집_샘플/data/sample.csv", "일집_샘플/readme.txt" }, files);
    }

    /// <summary>TAR·7z(이름 보이는 암호)도 같은 규칙.</summary>
    [Theory]
    [InlineData("tar_default")]
    [InlineData("py7z_pw_names_visible")]
    [InlineData("zip_aes256")]
    public async Task 다른_형식도_폴더_선택이_같다(string id)
    {
        var fx = FixtureCatalog.Get(id);
        var snap = await Run(fx, "일집_샘플/하위폴더");
        var files = snap.Where(kv => kv.Value is not null).ToDictionary(kv => kv.Key, kv => kv.Value);
        var want = fx.ExpectedFiles.Where(e => e.Path.StartsWith("일집_샘플/하위폴더/", StringComparison.Ordinal))
                     .ToDictionary(e => e.Path, e => (string?)e.Sha256);
        Assert.Equal(2, want.Count);
        Assert.Equal(want.OrderBy(kv => kv.Key, StringComparer.Ordinal), files.OrderBy(kv => kv.Key, StringComparer.Ordinal));
    }

    /// <summary>bz2(단일 스트림)는 추정 이름과 같을 때만 풀린다.</summary>
    [Fact]
    public async Task bz2_선택해제는_추정이름이_맞을때만()
    {
        var fx = FixtureCatalog.Get("bz2_single_python");
        Assert.Single((await Run(fx, "견적서_2026.txt")).Keys);
        Assert.Empty(await Run(fx, "다른이름.txt"));
    }
}
