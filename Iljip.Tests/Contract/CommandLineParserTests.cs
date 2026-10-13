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

    /// <summary>
    /// 알아보는 키워드 목록(셸 확장 계약 확인에 씀)은 각각 해석된다. W2-06 에서 '--add-to'(압축 파일에 넣기)·'--add-to-pick'(기존 압축을 골라 넣기 — 검증 지적)을 더해 8개.
    /// </summary>
    [Fact]
    public void 키워드_목록은_모두_해석된다()
    {
        Assert.Equal(8, CommandLineParser.Keywords.Count);
        Assert.Contains(CommandLineParser.AddToKeyword, CommandLineParser.Keywords);
        Assert.Contains(CommandLineParser.AddToPickKeyword, CommandLineParser.Keywords);
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
    [InlineData(false, "--add-to", "a.zip", "x.txt")]   // W2-06: 우클릭 'zip에 추가'도 메인 창 없이
    [InlineData(true, "--add-to", "a.zip")]             // 넣을 것이 없음 = None → 메인 창
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

    /// <summary>
    /// W2-06(I2-3): '--add-to 압축 경로…' — 첫 인자가 넣을 압축 파일, 나머지(빈 문자열 뺌)가 넣을 경로. 넣을 경로가 없거나 압축 파일이 비면 None.
    /// 키워드는 대소문자를 가린다('--ADD-TO' 는 경로 목록 — 다른 키워드와 같은 규칙).
    /// </summary>
    [Fact]
    public void add_to는_압축_하나와_넣을_경로들()
    {
        var cmd = CommandLineParser.Parse(new[] { "--add-to", @"C:\받은 파일\a.zip", @"C:\자료\x.txt", "", @"C:\자료\폴더 y" });
        Assert.Equal(CommandVerb.AddTo, cmd.Verb);
        Assert.Equal(@"C:\받은 파일\a.zip", cmd.Archive);
        Assert.Equal(new[] { @"C:\자료\x.txt", @"C:\자료\폴더 y" }, cmd.Paths);
        Assert.Null(cmd.Dest);
        Assert.False(cmd.ShowMainWindow);

        var simple = CommandLineParser.Parse(new[] { "--add-to", "a.zip", "x", "y" });
        Assert.Equal(CommandVerb.AddTo, simple.Verb);
        Assert.Equal("a.zip", simple.Archive);
        Assert.Equal(new[] { "x", "y" }, simple.Paths);

        Assert.Equal(CommandVerb.None, CommandLineParser.Parse(new[] { "--add-to", "a.zip" }).Verb);
        Assert.Equal(CommandVerb.None, CommandLineParser.Parse(new[] { "--add-to", "", "x" }).Verb);
        Assert.Equal(CommandVerb.None, CommandLineParser.Parse(new[] { "--add-to", "a.zip", "" }).Verb);
        Assert.Equal(CommandVerb.Paths, CommandLineParser.Parse(new[] { "--ADD-TO", "a.zip", "x" }).Verb);
    }

    /// <summary>
    /// W2-06 검증 지적(우클릭 'zip에 추가(기존 zip 고르기)'): '--add-to-pick 경로…' — 넣을 경로만 받고 압축 파일은 비워 둔다(일집이 고르는 창으로 묻는다).
    /// 같은 동사(AddTo)라 메인 창 없이. 넣을 경로가 없으면 None. '--add-to' 와 섞이지 않는다(통째로 같은지로만 비교).
    /// </summary>
    [Fact]
    public void add_to_pick은_넣을_경로만_받고_압축은_고르게_한다()
    {
        var cmd = CommandLineParser.Parse(new[] { "--add-to-pick", @"C:\자료\x.txt", "", @"C:\자료\폴더 y" });
        Assert.Equal(CommandVerb.AddTo, cmd.Verb);
        Assert.Null(cmd.Archive);
        Assert.Empty(cmd.Archives);
        Assert.Equal(new[] { @"C:\자료\x.txt", @"C:\자료\폴더 y" }, cmd.Paths);
        Assert.False(cmd.ShowMainWindow);

        Assert.Equal(CommandVerb.None, CommandLineParser.Parse(new[] { "--add-to-pick" }).Verb);
        Assert.Equal(CommandVerb.None, CommandLineParser.Parse(new[] { "--add-to-pick", "" }).Verb);
        Assert.Equal("a.zip", CommandLineParser.Parse(new[] { "--add-to", "a.zip", "x" }).Archive);
    }
}
