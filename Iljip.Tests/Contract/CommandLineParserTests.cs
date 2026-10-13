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
    /// 알아보는 키워드 목록(셸 확장 계약 확인에 씀)은 각각 해석된다. W2-06 에서 '--add-to'(압축 파일에 넣기)·'--add-to-pick'(기존 압축을 골라 넣기 — 검증 지적)을 더해 8개,
    /// W2-11 에서 '--compress-to'(만들 곳을 정한 압축)·'--extract-to-folder-in'(그 폴더 안 '이름' 폴더에 풀기)을 더해 10개,
    /// W2-11 검증 지적에서 '--extract-into'(여럿을 그 폴더에 바로 풀기 — 관리자 권한으로 다시 시도)를 더해 11개.
    /// W2-12 에서 '--extract-smart'(알아서 풀기)·'--compress-zip'·'--compress-7z'(창 없이 바로 압축)·'--register-associations'·'--unregister-associations'(파일 연결)를 더해 16개.
    /// 동사 앞 표시('--new-window'·'--elevated')는 키워드가 아니라 <see cref="CommandLineParser.Flags"/>(W2-11 시험은 아래 표시 시험들).
    /// '--list-file'(긴 명령줄 — 해석 전에 펼침)·'--drop-handler'(파일 연결 넣기의 선택 표시)도 동사가 아니다(W2-12).
    /// </summary>
    [Fact]
    public void 키워드_목록은_모두_해석된다()
    {
        Assert.Equal(16, CommandLineParser.Keywords.Count);
        Assert.Equal(16, CommandLineParser.Keywords.Distinct().Count());
        Assert.Contains(CommandLineParser.AddToKeyword, CommandLineParser.Keywords);
        Assert.Contains(CommandLineParser.AddToPickKeyword, CommandLineParser.Keywords);
        Assert.Contains(CommandLineParser.CompressToKeyword, CommandLineParser.Keywords);
        Assert.Contains(CommandLineParser.ExtractToFolderInKeyword, CommandLineParser.Keywords);
        Assert.Contains(CommandLineParser.ExtractIntoKeyword, CommandLineParser.Keywords);
        Assert.Contains("--extract-smart", CommandLineParser.Keywords);
        Assert.Contains("--compress-zip", CommandLineParser.Keywords);
        Assert.Contains("--compress-7z", CommandLineParser.Keywords);
        Assert.Contains("--register-associations", CommandLineParser.Keywords);
        Assert.Contains("--unregister-associations", CommandLineParser.Keywords);
        Assert.DoesNotContain("--list-file", CommandLineParser.Keywords);
        Assert.DoesNotContain("--drop-handler", CommandLineParser.Keywords);
        Assert.Empty(CommandLineParser.Flags.Intersect(CommandLineParser.Keywords));
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

    // ───────────────────────────── W2-11: 새 창·관리자 권한 표시, 새 동사 ─────────────────────────────

    /// <summary>
    /// W2-11(I3-15 새 창): '--new-window' 만이면 빈 새 창(None + 표시), '--new-window a.zip' 이면 표시 + 경로(끌어 놓기와 같은 처리),
    /// '--new-window --open a.zip' 이면 표시 + 열기. 표시는 메인 창 여부를 바꾸지 않는다.
    /// </summary>
    [Fact]
    public void new_window_표시()
    {
        var empty = CommandLineParser.Parse(new[] { "--new-window" });
        Assert.Equal(CommandVerb.None, empty.Verb);
        Assert.True(empty.NewWindow);
        Assert.False(empty.Elevated);
        Assert.True(empty.ShowMainWindow);

        var path = CommandLineParser.Parse(new[] { "--new-window", "a.zip" });
        Assert.Equal(CommandVerb.Paths, path.Verb);
        Assert.Equal(new[] { "a.zip" }, path.Paths);
        Assert.True(path.NewWindow);

        var open = CommandLineParser.Parse(new[] { "--new-window", "--open", @"C:\받은 파일\보고서.zip" });
        Assert.Equal(CommandVerb.Open, open.Verb);
        Assert.Equal(@"C:\받은 파일\보고서.zip", open.Archive);
        Assert.True(open.NewWindow);

        // 표시가 없으면 예전 그대로(둘 다 false), 기존 None 은 그대로
        var plain = CommandLineParser.Parse(new[] { "--open", "a.zip" });
        Assert.False(plain.NewWindow);
        Assert.False(plain.Elevated);
        Assert.False(ParsedCommand.None.NewWindow);
    }

    /// <summary>
    /// W2-11(I3-16): '--elevated' 는 맨 앞에 붙는 표시 — 뒤의 동사는 예전대로. '--elevated --extract-to a b' 도 대화형 ExtractTo(메인 창 없이 진행 창만).
    /// 표시는 맨 앞에 연달아 올 때만(경로 자리의 '--elevated' 는 경로) — 대소문자는 키워드처럼 가린다.
    /// </summary>
    [Fact]
    public void elevated_표시()
    {
        var to = CommandLineParser.Parse(new[] { "--elevated", "--extract-to", @"C:\a.zip", @"C:\Program Files\풀 곳" });
        Assert.Equal(CommandVerb.ExtractTo, to.Verb);
        Assert.True(to.Elevated);
        Assert.Equal(@"C:\a.zip", to.Archive);
        Assert.Equal(@"C:\Program Files\풀 곳", to.Dest);
        Assert.False(to.ShowMainWindow);

        var both = CommandLineParser.Parse(new[] { "--elevated", "--new-window", "--compress", "x.txt" });
        Assert.Equal(CommandVerb.Compress, both.Verb);
        Assert.True(both.Elevated);
        Assert.True(both.NewWindow);

        Assert.True(CommandLineParser.Parse(new[] { "--elevated" }).Elevated);
        var tail = CommandLineParser.Parse(new[] { "--compress", "x.txt", "--elevated" });
        Assert.False(tail.Elevated);
        Assert.Equal(new[] { "x.txt", "--elevated" }, tail.Paths);
        var upper = CommandLineParser.Parse(new[] { "--ELEVATED", "a.zip" });
        Assert.False(upper.Elevated);
        Assert.Equal(CommandVerb.Paths, upper.Verb);
    }

    /// <summary>
    /// W2-11: '--compress-to 만들폴더 원본…'(만들 곳을 정한 압축 — 메인 창), '--extract-to-folder-in 폴더 압축…'(그 폴더 안 '이름' 폴더 — 메인 창 없이).
    /// 모자라면 None.
    /// </summary>
    [Fact]
    public void compress_to와_extract_to_folder_in()
    {
        var c = CommandLineParser.Parse(new[] { "--compress-to", @"C:\Program Files\만들 곳", @"C:\자료\a.txt", "", @"C:\자료\폴더 b" });
        Assert.Equal(CommandVerb.CompressTo, c.Verb);
        Assert.Equal(@"C:\Program Files\만들 곳", c.Dest);
        Assert.Equal(new[] { @"C:\자료\a.txt", @"C:\자료\폴더 b" }, c.Paths);
        Assert.Null(c.Archive);
        Assert.True(c.ShowMainWindow);

        var x = CommandLineParser.Parse(new[] { "--extract-to-folder-in", @"C:\Program Files\풀 곳", @"C:\a.zip", @"C:\b.7z" });
        Assert.Equal(CommandVerb.ExtractToFolderIn, x.Verb);
        Assert.Equal(@"C:\Program Files\풀 곳", x.Dest);
        Assert.Equal(@"C:\a.zip", x.Archive);
        Assert.Equal(new[] { @"C:\a.zip", @"C:\b.7z" }, x.Archives);
        Assert.False(x.ShowMainWindow);

        Assert.Equal(CommandVerb.None, CommandLineParser.Parse(new[] { "--compress-to", @"C:\곳" }).Verb);
        Assert.Equal(CommandVerb.None, CommandLineParser.Parse(new[] { "--compress-to", " ", "a.txt" }).Verb);
        Assert.Equal(CommandVerb.None, CommandLineParser.Parse(new[] { "--extract-to-folder-in", @"C:\곳" }).Verb);
        Assert.Equal(CommandVerb.None, CommandLineParser.Parse(new[] { "--extract-to-folder-in", "", "a.zip" }).Verb);
        // '--extract-to-folder' 와 섞이지 않는다
        Assert.Equal(CommandVerb.ExtractToFolder, CommandLineParser.Parse(new[] { "--extract-to-folder", "a.zip" }).Verb);
    }

    /// <summary>
    /// W2-11 검증 지적: '--extract-into 폴더 압축…'(여럿을 그 폴더에 바로 — 메인 창 없이). 모자라면 None.
    /// ★ 창 없이 풀기('--extract-to' 가 첫 인자 — 재사용장치 계약)와 섞이지 않는다: '--extract-to' 는 예전처럼 압축 하나·폴더.
    /// </summary>
    [Fact]
    public void extract_into는_여럿을_그_폴더에_바로()
    {
        var x = CommandLineParser.Parse(new[] { "--extract-into", @"C:\Program Files\풀 곳", @"C:\가.zip", "", @"C:\나.7z" });
        Assert.Equal(CommandVerb.ExtractInto, x.Verb);
        Assert.Equal(@"C:\Program Files\풀 곳", x.Dest);
        Assert.Equal(@"C:\가.zip", x.Archive);
        Assert.Equal(new[] { @"C:\가.zip", @"C:\나.7z" }, x.Archives);
        Assert.False(x.ShowMainWindow);

        Assert.Equal(CommandVerb.None, CommandLineParser.Parse(new[] { "--extract-into", @"C:\곳" }).Verb);
        Assert.Equal(CommandVerb.None, CommandLineParser.Parse(new[] { "--extract-into", " ", "a.zip" }).Verb);
        var to = CommandLineParser.Parse(new[] { "--extract-to", "a.zip", @"C:\곳" });
        Assert.Equal(CommandVerb.ExtractTo, to.Verb);
        Assert.Equal("a.zip", to.Archive);
        Assert.False(StartupDecision.IsHeadlessExtract(new[] { "--extract-into", "a.zip", @"C:\곳" }, _ => true));
        Assert.True(StartupDecision.IsHeadlessExtract(new[] { "--extract-to", "a.zip", @"C:\곳" }, _ => true));
    }

    /// <summary>
    /// W2-11: <see cref="CommandLineParser.ToArgs"/> 는 <see cref="CommandLineParser.Parse"/> 의 반대 — 모든 동사·표시를 다시 해석하면 같은 명령.
    /// '--elevated' 는 늘 맨 앞(창 없이 풀기 판정 'args[0] == --extract-to' 에 걸리지 않게).
    /// </summary>
    [Fact]
    public void ToArgs는_다시_해석하면_같은_명령()
    {
        var none = Array.Empty<string>();
        var commands = new[]
        {
            new ParsedCommand(CommandVerb.Open, @"C:\a b\보고서.zip", null, none),
            new ParsedCommand(CommandVerb.Extract, "a.zip", null, none) { Archives = new[] { "a.zip", "b.7z" } },
            new ParsedCommand(CommandVerb.ExtractHere, "a.zip", null, none) { Archives = new[] { "a.zip" } },
            new ParsedCommand(CommandVerb.ExtractToFolder, "a.zip", null, none) { Archives = new[] { "a.zip", "c.rar" } },
            new ParsedCommand(CommandVerb.ExtractTo, @"C:\a.zip", @"C:\풀 곳\", none),
            new ParsedCommand(CommandVerb.ExtractToFolderIn, "a.zip", @"D:\곳", none) { Archives = new[] { "a.zip", "b.zip" } },
            new ParsedCommand(CommandVerb.ExtractInto, "a.zip", @"D:\풀 곳", none) { Archives = new[] { "a.zip", "b.7z" } },
            new ParsedCommand(CommandVerb.Compress, null, null, new[] { "x.txt", @"C:\폴더 y" }),
            new ParsedCommand(CommandVerb.CompressTo, null, @"C:\Program Files\곳", new[] { "x.txt" }),
            new ParsedCommand(CommandVerb.AddTo, "a.zip", null, new[] { "x.txt", "y" }),
            new ParsedCommand(CommandVerb.AddTo, null, null, new[] { "x.txt" }),
            new ParsedCommand(CommandVerb.Paths, null, null, new[] { "a.zip", "b.txt" }),
            // W2-12
            new ParsedCommand(CommandVerb.ExtractSmart, "a.zip", null, none) { Archives = new[] { "a.zip", @"C:\b c\d.7z" } },
            new ParsedCommand(CommandVerb.CompressZip, null, null, new[] { @"C:\폴더 y", "x.txt" }),
            new ParsedCommand(CommandVerb.Compress7z, null, null, new[] { "x.txt" }),
            new ParsedCommand(CommandVerb.RegisterAssociations, null, null, new[] { "--drop-handler" }),
            new ParsedCommand(CommandVerb.RegisterAssociations, null, null, none),
            new ParsedCommand(CommandVerb.UnregisterAssociations, null, null, none),
        };
        foreach (var cmd in commands)
            foreach (var (nw, el) in new[] { (false, false), (true, false), (false, true), (true, true) })
            {
                var flagged = cmd with { NewWindow = nw, Elevated = el };
                var args = CommandLineParser.ToArgs(flagged);
                if (el) Assert.Equal("--elevated", args[0]);
                var back = CommandLineParser.Parse(args.ToArray());
                string what = string.Join(" | ", args);
                Assert.True(cmd.Verb == back.Verb, what);
                Assert.True(cmd.Archive == back.Archive, what);
                Assert.True(cmd.Dest == back.Dest, what);
                Assert.True(cmd.Archives.SequenceEqual(back.Archives), what);
                Assert.True(cmd.Paths.SequenceEqual(back.Paths), what);
                Assert.True(nw == back.NewWindow && el == back.Elevated, what);
            }
        Assert.Empty(CommandLineParser.ToArgs(ParsedCommand.None));
        Assert.Equal(new[] { "--new-window" }, CommandLineParser.ToArgs(ParsedCommand.None with { NewWindow = true }));
    }

    /// <summary>W2-11: 새 동사의 메인 창 여부(그 폴더 안 '이름' 폴더에 풀기는 우클릭 풀기처럼 진행 창만, 만들 곳을 정한 압축은 압축 창 — 메인 창).</summary>
    [Theory]
    [InlineData(false, "--extract-to-folder-in", @"C:\곳", "a.zip")]
    [InlineData(false, "--extract-into", @"C:\곳", "a.zip", "b.7z")]
    [InlineData(true, "--compress-to", @"C:\곳", "a.txt")]
    [InlineData(false, "--elevated", "--extract-to", "a.zip", @"C:\곳")]
    [InlineData(false, "--elevated", "--extract-here", "a.zip")]
    [InlineData(true, "--elevated", "--open", "a.zip")]
    [InlineData(true, "--new-window")]
    public void W2_11_메인_창_여부(bool showMainWindow, params string[] args)
    {
        Assert.Equal(showMainWindow, CommandLineParser.Parse(args).ShowMainWindow);
    }

    /// <summary>
    /// W2-12: 알아서 풀기·창 없이 바로 압축은 우클릭 풀기처럼 진행 창만(메인 창 없이), 파일 연결 넣기·빼기는 App 이 자체 프로세스로 맡아 메인 창 없음.
    /// 인자가 모자라면 None(메인 창). '--extract-smart' 는 뒤의 압축을 모두 받는다.
    /// </summary>
    [Theory]
    [InlineData(false, "--extract-smart", "a.zip")]
    [InlineData(false, "--extract-smart", "a.zip", "b.7z")]
    [InlineData(false, "--compress-zip", "폴더")]
    [InlineData(false, "--compress-7z", "a.txt", "b.txt")]
    [InlineData(false, "--register-associations")]
    [InlineData(false, "--register-associations", "--drop-handler")]
    [InlineData(false, "--unregister-associations")]
    [InlineData(true, "--extract-smart")]
    [InlineData(true, "--compress-zip")]
    [InlineData(true, "--compress-7z", "")]
    [InlineData(true, "--EXTRACT-SMART", "a.zip")]   // 키워드는 대소문자를 가림 — 경로 목록
    public void W2_12_메인_창_여부(bool showMainWindow, params string[] args)
    {
        Assert.Equal(showMainWindow, CommandLineParser.Parse(args).ShowMainWindow);
    }

    /// <summary>W2-12: '--extract-smart' 는 압축 여럿(빈 문자열 뺌), '--register-associations' 뒤의 표시는 Paths 에 그대로, '--unregister-associations' 는 뒤를 무시.</summary>
    [Fact]
    public void W2_12_새_동사_해석()
    {
        var smart = CommandLineParser.Parse(new[] { "--extract-smart", @"C:\받은 파일\a.zip", "", @"C:\b.egg" });
        Assert.Equal(CommandVerb.ExtractSmart, smart.Verb);
        Assert.Equal(@"C:\받은 파일\a.zip", smart.Archive);
        Assert.Equal(new[] { @"C:\받은 파일\a.zip", @"C:\b.egg" }, smart.Archives);
        Assert.Empty(smart.Paths);

        var reg = CommandLineParser.Parse(new[] { "--register-associations", "--drop-handler" });
        Assert.Equal(CommandVerb.RegisterAssociations, reg.Verb);
        Assert.Equal(new[] { CommandLineParser.DropHandlerOption }, reg.Paths);
        Assert.Empty(CommandLineParser.Parse(new[] { "--register-associations" }).Paths);

        var unreg = CommandLineParser.Parse(new[] { "--unregister-associations", "x" });
        Assert.Equal(CommandVerb.UnregisterAssociations, unreg.Verb);
        Assert.Empty(unreg.Paths);
        Assert.True(StartupDecision.IsAssociationCommand(new[] { "--register-associations" }));
        Assert.True(StartupDecision.IsAssociationCommand(new[] { "--unregister-associations" }));
        Assert.False(StartupDecision.IsAssociationCommand(new[] { "--extract-smart", "a.zip" }));
        Assert.False(StartupDecision.IsAssociationCommand(Array.Empty<string>()));
    }
}
