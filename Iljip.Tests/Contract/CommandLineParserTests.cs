using Iljip.Services;

namespace Iljip.Tests.Contract;

/// <summary>
/// [I0-1] 명령줄 해석(순수 함수 <see cref="CommandLineParser"/>). 파일이 있는지는 보지 않는다(화면 몫).
/// 키워드는 대소문자를 가리고('--OPEN' 은 경로 목록 — 기존 기록 동작), 인자가 모자라면 None.
/// '--extract-to-folder' 와 '--extract-to' 는 통째로 같은지만 비교하므로 섞이지 않는다.
/// </summary>
public class CommandLineParserTests
{
    [Theory]
    [InlineData("--open", CommandVerb.Open)]
    [InlineData("--extract", CommandVerb.Extract)]
    [InlineData("--extract-here", CommandVerb.ExtractHere)]
    [InlineData("--extract-to-folder", CommandVerb.ExtractToFolder)]
    public void 압축_하나를_받는_키워드(string keyword, CommandVerb verb)
    {
        var cmd = CommandLineParser.Parse(new[] { keyword, @"C:\받은 파일\보고서.zip" });
        Assert.Equal(verb, cmd.Verb);
        Assert.Equal(@"C:\받은 파일\보고서.zip", cmd.Archive);
        Assert.Null(cmd.Dest);
        Assert.Empty(cmd.Paths);

        // 뒤에 더 붙은 인자는 무시(예전 switch 와 같음) — '--extract-to-folder a b' 도 '--extract-to' 가 되지 않는다
        var extra = CommandLineParser.Parse(new[] { keyword, "a.zip", "b" });
        Assert.Equal(verb, extra.Verb);
        Assert.Equal("a.zip", extra.Archive);
        Assert.Null(extra.Dest);
    }

    [Fact]
    public void extract_to는_압축과_폴더_둘을_받는다()
    {
        var cmd = CommandLineParser.Parse(new[] { "--extract-to", "a.zip", @"D:\풀 곳" });
        Assert.Equal(CommandVerb.ExtractTo, cmd.Verb);
        Assert.Equal("a.zip", cmd.Archive);
        Assert.Equal(@"D:\풀 곳", cmd.Dest);
    }

    [Fact]
    public void compress는_경로를_모두_받는다()
    {
        var cmd = CommandLineParser.Parse(new[] { "--compress", "a.txt", @"C:\폴더", "b.doc" });
        Assert.Equal(CommandVerb.Compress, cmd.Verb);
        Assert.Equal(new[] { "a.txt", @"C:\폴더", "b.doc" }, cmd.Paths);
        Assert.Null(cmd.Archive);
    }

    /// <summary>인자가 모자라면 None(아무것도 안 함).</summary>
    [Theory]
    [InlineData]
    [InlineData("--open")]
    [InlineData("--extract")]
    [InlineData("--extract-here")]
    [InlineData("--extract-to-folder")]
    [InlineData("--extract-to")]
    [InlineData("--extract-to", "a.zip")]
    [InlineData("--extract-to", "a.zip", " ")]
    [InlineData("--compress")]
    [InlineData("--open", "")]
    public void 인자가_모자라면_None(params string[] args)
    {
        Assert.Equal(CommandVerb.None, CommandLineParser.Parse(args).Verb);
    }

    [Fact]
    public void null도_None()
    {
        Assert.Equal(CommandVerb.None, CommandLineParser.Parse(null).Verb);
    }

    /// <summary>키워드가 아니면 모두 경로(끌어 놓기와 같게). 대문자 키워드도 경로 — 기존 기록 동작('--OPEN' 은 결과적으로 열림).</summary>
    [Theory]
    [InlineData("--OPEN", "a.zip")]
    [InlineData("--Extract-Here", "a.zip")]
    [InlineData("--EXTRACT-TO-FOLDER", "a.zip")]
    [InlineData("a.zip")]
    [InlineData(@"C:\a.txt", @"C:\폴더")]
    [InlineData("--모르는-키워드", "a.zip")]
    public void 키워드가_아니면_모두_경로(params string[] args)
    {
        var cmd = CommandLineParser.Parse(args);
        Assert.Equal(CommandVerb.Paths, cmd.Verb);
        Assert.Equal(args, cmd.Paths);
    }

    /// <summary>알아보는 키워드 목록(셸 확장 계약 확인에 씀)은 각각 해석된다.</summary>
    [Fact]
    public void 키워드_목록은_모두_해석된다()
    {
        Assert.Equal(6, CommandLineParser.Keywords.Count);
        foreach (var k in CommandLineParser.Keywords)
        {
            var cmd = CommandLineParser.Parse(new[] { k, "a.zip", "dest" });
            Assert.NotEqual(CommandVerb.None, cmd.Verb);
            Assert.NotEqual(CommandVerb.Paths, cmd.Verb);
        }
    }

    /// <summary>
    /// W2-05(I2-6): 풀기 동사(--extract·--extract-here·--extract-to-folder)는 뒤의 경로를 모두 압축 파일로 받는다(여러 개 풀기).
    /// 첫 번째는 예전처럼 <see cref="ParsedCommand.Archive"/>, 전부는 <see cref="ParsedCommand.Archives"/>(빈 문자열은 뺌). '--open' 은 하나만.
    /// </summary>
    [Theory]
    [InlineData("--extract", CommandVerb.Extract)]
    [InlineData("--extract-here", CommandVerb.ExtractHere)]
    [InlineData("--extract-to-folder", CommandVerb.ExtractToFolder)]
    public void 풀기_동사는_압축_여러_개를_받는다(string keyword, CommandVerb verb)
    {
        var cmd = CommandLineParser.Parse(new[] { keyword, @"C:\받은 파일\a.zip", "", @"C:\받은 파일\b 사본.7z", @"D:\c.egg" });
        Assert.Equal(verb, cmd.Verb);
        Assert.Equal(@"C:\받은 파일\a.zip", cmd.Archive);
        Assert.Equal(new[] { @"C:\받은 파일\a.zip", @"C:\받은 파일\b 사본.7z", @"D:\c.egg" }, cmd.Archives);
        Assert.Null(cmd.Dest);
        Assert.Empty(cmd.Paths);

        Assert.Equal(new[] { "a.zip" }, CommandLineParser.Parse(new[] { keyword, "a.zip" }).Archives);
    }

    [Fact]
    public void open과_extract_to는_압축_하나()
    {
        Assert.Equal(new[] { "a.zip" }, CommandLineParser.Parse(new[] { "--open", "a.zip", "b.zip" }).Archives);
        var to = CommandLineParser.Parse(new[] { "--extract-to", "a.zip", @"C:\풀 곳" });
        Assert.Equal(new[] { "a.zip" }, to.Archives);
        Assert.Equal(@"C:\풀 곳", to.Dest);
        Assert.Empty(CommandLineParser.Parse(new[] { "--compress", "a.txt" }).Archives);
        Assert.Empty(ParsedCommand.None.Archives);
    }

    /// <summary>W2-05(I1-6): 탐색기 우클릭 풀기 동사만 메인 창을 띄우지 않는다(진행 창만). 열기·풀기 창·압축·경로·인자 없음은 메인 창.</summary>
    [Theory]
    [InlineData(false, "--extract-here", "a.zip")]
    [InlineData(false, "--extract-to-folder", "a.zip", "b.zip")]
    [InlineData(false, "--extract-to", "a.zip", @"C:\풀 곳")]
    [InlineData(true, "--open", "a.zip")]
    [InlineData(true, "--extract", "a.zip")]
    [InlineData(true, "--compress", "a.txt")]
    [InlineData(true, "a.zip")]
    [InlineData(true, "--extract-here")]          // 인자 모자람 = None → 메인 창
    [InlineData(true)]
    public void 우클릭_풀기만_메인_창_없이(bool showMainWindow, params string[] args)
    {
        Assert.Equal(showMainWindow, CommandLineParser.Parse(args).ShowMainWindow);
    }
}
