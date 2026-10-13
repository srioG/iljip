using System.Text;
using Iljip.Models;
using Iljip.Services;
using Iljip.Tests.TestSupport;
using Iljip.ViewModels;

namespace Iljip.Tests.Contract;

/// <summary>
/// [W2-07] 한 화면 압축 창 뒤의 흐름(<see cref="CompressFlow"/>·MainViewModel.Compress.cs) — 창은 가짜(<see cref="FakeUserInteraction.CompressionRequestAnswer"/>),
/// 휴지통·시계·설정·시험기도 가짜(<see cref="TestServices"/> — 진짜 휴지통은 절대 부르지 않음).
/// I2-4 각각 따로(원본마다 압축 파일 하나, 같은 이름은 '이름 (2)', 요약 창), I3-5 이름에 날짜(세 모양·두 번 붙이지 않음),
/// I3-6 쓸모없는 파일 빼기(기본 목록·끄기·사용자 목록·직접 고른 것은 담음), I3-4 원본 지우기(시험 통과 + '옮기기'일 때만, 휴지통만, 지우지 않는 경우들),
/// I2-12 설정 기억(settings.json 왕복·깨진 파일·모르는 필드·암호 없음).
/// </summary>
public class CompressFlowTests
{
    // ───────────── 도우미 ─────────────

    private sealed class Ctx : IDisposable
    {
        public Ctx(string tag, TestServices? services)
        {
            Temp = new TempDir(tag);
            Services = services ?? new TestServices();
            Services.Clock.SetLocal(2026, 10, 13);
            Ui = new FakeUserInteraction();
            Vm = new MainViewModel(Ui, null, Services.Services);
            Src = Temp.Dir("원본");
            Out = Temp.Dir("만든곳");
        }

        public TempDir Temp { get; }
        public TestServices Services { get; }
        public FakeUserInteraction Ui { get; }
        public MainViewModel Vm { get; }

        /// <summary>원본을 두는 폴더.</summary>
        public string Src { get; }

        /// <summary>압축 파일을 만들 폴더.</summary>
        public string Out { get; }

        public string File(string relative, string content)
        {
            string p = Path.Combine(Src, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(p)!);
            System.IO.File.WriteAllText(p, content, new UTF8Encoding(false));
            return p;
        }

        public string Dir(string relative) => Temp.Dir("원본", relative);

        public void Dispose() => Temp.Dispose();
    }

    private static Ctx New(string tag, TestServices? services = null) => new(tag, services);

    /// <summary>창의 답(넘긴 것의 원본·설정 빼기 목록을 쓰고, 나머지는 인자대로).</summary>
    private static Func<CompressionDialogRequest, CompressionRequest?> Answer(
        string folder, CompressionFormat format = CompressionFormat.Zip, string? baseName = null, bool each = false,
        DateNameFormat date = DateNameFormat.None, bool exclude = true, IReadOnlyList<string>? patterns = null,
        bool test = false, bool delete = false, CompressionOptions? options = null, bool overwrite = false) =>
        r => new CompressionRequest
        {
            Sources = r.Sources,
            OutputFolder = folder,
            BaseName = baseName ?? r.SuggestedBaseName,
            Format = format,
            Options = options ?? CompressionOptions.Default,
            EachSeparately = each,
            DateInName = date,
            ExcludeEnabled = exclude,
            ExcludePatterns = patterns ?? r.Defaults.ExcludePatterns,
            TestAfter = test,
            DeleteOriginalsAfter = delete,
            OverwriteExisting = overwrite,
        };

    private static async Task Compress(Ctx c, params string[] sources)
    {
        c.Vm.AddToStaging(sources);
        await VmRunner.Run(() => c.Vm.CompressCommand.ExecuteAsync(null), "압축");
    }

    /// <summary>보던 압축(하나로 만들면 결과를 열어 보임)을 닫고 새로 담아 압축 — 두 번째 압축용.</summary>
    private static async Task CompressAgain(Ctx c, params string[] sources)
    {
        c.Vm.StartNewStaging(sources);
        await VmRunner.Run(() => c.Vm.CompressCommand.ExecuteAsync(null), "압축");
    }

    private static async Task<List<string>> FileNamesIn(string archive, string? password = null) =>
        (await Svc.For(archive).ListEntriesAsync(archive, password))
            .Where(e => !e.IsDirectory)
            .Select(e => TreeAssert.NormalizeEntryPath(e.Path))
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToList();

    private static async Task<SortedDictionary<string, string?>> ExtractSnapshot(TempDir t, string archive, string? password = null)
    {
        string dest = t.Dir("풀어봄_" + Guid.NewGuid().ToString("N")[..6]);
        await Svc.For(archive).ExtractAsync(archive, dest, password);
        return TreeAssert.Snapshot(dest);
    }

    private static CompressionFormat FormatOf(string ext) => CompressionFormats.FromExtension(ext)!.Value;

    // ───────────── I2-4 각각 따로 ─────────────

    /// <summary>
    /// 계약: 원본 셋(파일·폴더·한글 이름)이면 압축 파일 셋 — 파일은 확장자를 뗀 이름, 폴더는 폴더 이름. 풀면 원본과 같다(이름·내용 왕복).
    /// 이미 있는 같은 이름은 건드리지 않고 '이름 (2)'. 진행 창 하나에 '1/3'…'3/3', 끝에 요약 창 '3개를 모두 압축했어요.', 압축한 원본은 대기 목록에서 빠짐.
    /// </summary>
    [Theory]
    [InlineData(".zip")]
    [InlineData(".7z")]
    [InlineData(".tar")]
    public async Task 각각_따로_원본마다_압축_파일이_생기고_내용이_맞다(string ext)
    {
        using var c = New("each" + ext.TrimStart('.'));
        string report = c.File("보고서.xlsx", "엑셀 흉내 내용");
        string client = c.Dir("거래처A");
        c.File(@"거래처A\견적.txt", "견적 내용");
        c.File(@"거래처A\하위\메모 [중요].txt", "메모");
        string korean = c.File("견적서 최종.txt", "한글 이름 파일");
        string existing = Path.Combine(c.Out, "보고서" + ext);
        System.IO.File.WriteAllText(existing, "예전에 만든 것");
        string existingSha = TreeAssert.Sha256(existing);
        c.Ui.CompressionRequestAnswer = Answer(c.Out, FormatOf(ext), each: true);

        await Compress(c, report, client, korean);

        Assert.Empty(c.Ui.Errors);
        Assert.Empty(c.Ui.Warnings);
        Assert.Equal(existingSha, TreeAssert.Sha256(existing));   // 있던 것은 그대로
        var made = Directory.GetFiles(c.Out).Select(Path.GetFileName).OrderBy(n => n, StringComparer.Ordinal).ToList();
        Assert.Equal(new[] { "거래처A" + ext, "견적서 최종" + ext, "보고서 (2)" + ext, "보고서" + ext }.OrderBy(n => n, StringComparer.Ordinal), made);

        var one = await ExtractSnapshot(c.Temp, Path.Combine(c.Out, "보고서 (2)" + ext));
        Assert.Equal(new[] { "보고서.xlsx" }, one.Keys);
        Assert.Equal(TreeAssert.Sha256(report), one["보고서.xlsx"]);
        var folder = await ExtractSnapshot(c.Temp, Path.Combine(c.Out, "거래처A" + ext));
        Assert.Equal(TreeAssert.Snapshot(c.Src).Where(kv => kv.Key.StartsWith("거래처A", StringComparison.Ordinal)), folder);
        var k = await ExtractSnapshot(c.Temp, Path.Combine(c.Out, "견적서 최종" + ext));
        Assert.Equal(TreeAssert.Sha256(korean), Assert.Single(k).Value);

        var summary = Assert.Single(c.Ui.Summaries);
        Assert.Equal(SummaryKind.Compress, summary.Kind);
        Assert.Equal("3개를 모두 압축했어요.", summary.Headline);
        Assert.Empty(summary.Failures);
        Assert.Contains(summary.Notes, n => n.StartsWith("만든 파일: ", StringComparison.Ordinal) && n.Contains("보고서 (2)" + ext));
        var session = Assert.Single(c.Ui.ProgressSessions);
        Assert.Equal(new[] { 1, 2, 3 }, session.Reports.Where(r => r.BatchCount == 3).Select(r => r.BatchIndex).Distinct().OrderBy(i => i));
        Assert.True(c.Vm.IsEmptyState, "압축한 원본이 대기 목록에 남음");
        Assert.Equal("압축 완료: 3개", c.Vm.StatusText);
    }

    /// <summary>계약: 각각 따로에서 하나를 못 만들면(통째로 잠긴 파일 — 담을 것이 없음) 그것만 요약의 '압축하지 못한 것'에, 나머지는 만든다. 못 만든 것은 대기 목록에 남는다.</summary>
    [Fact]
    public async Task 각각_따로_하나가_실패하면_그것만_요약에_나오고_나머지는_만든다()
    {
        using var c = New("eachfail");
        string ok = c.File("멀쩡.txt", "내용");
        string locked = c.File("잠김.pst", "잠긴 내용");
        c.Ui.CompressionRequestAnswer = Answer(c.Out, each: true);

        using (new FileStream(locked, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            await Compress(c, ok, locked);

        Assert.True(System.IO.File.Exists(Path.Combine(c.Out, "멀쩡.zip")));
        Assert.False(System.IO.File.Exists(Path.Combine(c.Out, "잠김.zip")), "못 만든 압축이 남음");
        var summary = Assert.Single(c.Ui.Summaries);
        Assert.Equal("2개 중 1개를 압축했어요.", summary.Headline);
        var failure = Assert.Single(summary.Failures);
        Assert.Equal("잠김.pst", failure.Name);
        Assert.Equal(NothingToCompressException.DefaultMessage, failure.Reason);
        Assert.Contains("압축하지 못한 것:", summary.ToDisplayText());
        Assert.Equal(locked, Assert.Single(c.Vm.Entries).SourcePath);   // 못 만든 것은 다시 할 수 있게 남김
        Assert.Equal("오류 발생", c.Vm.StatusText);
    }

    /// <summary>계약: 원본이 하나뿐이면 '각각 따로'여도 이름은 원본에서(창의 이름 칸은 쓰지 않음).</summary>
    [Fact]
    public async Task 각각_따로_원본_하나는_원본_이름으로()
    {
        using var c = New("eachone");
        string f = c.File("단독 보고서.docx", "x");
        c.Ui.CompressionRequestAnswer = Answer(c.Out, baseName: "무시될 이름", each: true);
        await Compress(c, f);
        Assert.True(System.IO.File.Exists(Path.Combine(c.Out, "단독 보고서.zip")));
    }

    // ───────────── I3-5 이름에 날짜 ─────────────

    [Theory]
    [InlineData(DateNameFormat.YearMonthDay, "보고서_20261013.zip")]
    [InlineData(DateNameFormat.YearMonthDayDashed, "보고서_2026-10-13.zip")]
    [InlineData(DateNameFormat.ShortYearMonthDay, "보고서_261013.zip")]
    public async Task 이름에_날짜를_붙인다(DateNameFormat date, string expected)
    {
        using var c = New("date");
        string f = c.File("보고서.xlsx", "x");
        c.Ui.CompressionRequestAnswer = Answer(c.Out, baseName: "보고서", date: date);
        await Compress(c, f);

        string path = Path.Combine(c.Out, expected);
        Assert.True(System.IO.File.Exists(path), string.Join(", ", Directory.GetFiles(c.Out).Select(Path.GetFileName)));
        Assert.Equal(path, c.Vm.CurrentArchivePath);   // 하나로 만들면 예전처럼 결과를 열어 보임
        Assert.Equal(new[] { "보고서.xlsx" }, await FileNamesIn(path));
    }

    /// <summary>계약: 이미 이름에 같은 날짜가 있으면(어느 모양이든, 숫자에 붙지 않은 채로) 두 번 붙이지 않는다.</summary>
    [Fact]
    public async Task 날짜는_두_번_붙이지_않는다()
    {
        using var c = New("date2");
        string f = c.File("보고서.xlsx", "x");
        c.Ui.CompressionRequestAnswer = Answer(c.Out, baseName: "보고서_20261013", date: DateNameFormat.YearMonthDay);
        await Compress(c, f);
        Assert.Equal(new[] { "보고서_20261013.zip" }, Directory.GetFiles(c.Out).Select(Path.GetFileName));

        var day = new DateTime(2026, 10, 13, 9, 0, 0, DateTimeKind.Local);
        Assert.Equal("보고서_2026-10-13", CompressNaming.WithDate("보고서_2026-10-13", DateNameFormat.YearMonthDay, day));
        Assert.Equal("261013 회의록", CompressNaming.WithDate("261013 회의록", DateNameFormat.YearMonthDayDashed, day));
        Assert.Equal("보고서_20261012_20261013", CompressNaming.WithDate("보고서_20261012", DateNameFormat.YearMonthDay, day));   // 다른 날
        Assert.Equal("번호2026101399_20261013", CompressNaming.WithDate("번호2026101399", DateNameFormat.YearMonthDay, day));     // 숫자에 붙은 것은 날짜가 아님
        Assert.Equal("보고서", CompressNaming.WithDate("보고서", DateNameFormat.None, day));
    }

    /// <summary>계약: 각각 따로일 때도 원본마다 날짜가 붙는다.</summary>
    [Fact]
    public async Task 각각_따로에도_원본마다_날짜가_붙는다()
    {
        using var c = New("date3");
        string a = c.File("가.txt", "a");
        string b = c.Dir("나 폴더");
        c.File(@"나 폴더\b.txt", "b");
        c.Ui.CompressionRequestAnswer = Answer(c.Out, each: true, date: DateNameFormat.YearMonthDayDashed);
        await Compress(c, a, b);
        Assert.Equal(new[] { "가_2026-10-13.zip", "나 폴더_2026-10-13.zip" },
            Directory.GetFiles(c.Out).Select(Path.GetFileName).OrderBy(n => n, StringComparer.Ordinal));
    }

    // ───────────── I3-6 쓸모없는 파일 빼기 ─────────────

    /// <summary>쓸모없는 파일이 섞인 폴더(뺄 것 5개: Thumbs.db·~$ 잠금 파일·.DS_Store·__MACOSX 폴더·하위의 desktop.ini).</summary>
    private static string JunkFolder(Ctx c)
    {
        string root = c.Dir("자료");
        c.File(@"자료\보고서.txt", "본문");
        c.File(@"자료\Thumbs.db", "미리 보기 캐시");
        c.File(@"자료\~$보고서.xlsx", "오피스 잠금");
        c.File(@"자료\.DS_Store", "맥");
        c.File(@"자료\__MACOSX\._보고서.txt", "맥 꾸러미");
        c.File(@"자료\하위\desktop.ini", "[.ShellClassInfo]");
        c.File(@"자료\하위\메모.txt", "메모");
        c.File(@"자료\보고서.bak", "백업");
        return root;
    }

    /// <summary>계약(서비스): 기본 빼기 목록이면 Thumbs.db·~$*·.DS_Store·__MACOSX/·desktop.ini 가 압축에 없고 뺀 수(5 — 폴더는 하나로)가 맞다. ZIP·7Z·TAR 같음.</summary>
    [Theory]
    [InlineData(".zip")]
    [InlineData(".7z")]
    [InlineData(".tar")]
    public async Task 기본_빼기_목록의_파일은_압축에_없다(string ext)
    {
        using var c = New("junk" + ext.TrimStart('.'));
        string root = JunkFolder(c);
        string arc = Path.Combine(c.Out, "자료" + ext);
        var options = new CompressionOptions { ExcludeFilter = new PatternFileFilter(PatternFileFilter.DefaultPatterns) };

        var result = await Svc.For(arc).CompressAsync(new[] { root }, arc, options);

        Assert.Equal(new[] { "자료/보고서.bak", "자료/보고서.txt", "자료/하위/메모.txt" }, await FileNamesIn(arc));
        Assert.Equal(5, result.Excluded);
        Assert.Equal(
            new[] { "Thumbs.db", "~$보고서.xlsx", ".DS_Store", "__MACOSX", "desktop.ini" }.OrderBy(n => n, StringComparer.Ordinal),
            result.ExcludedPaths.Select(Path.GetFileName).OrderBy(n => n, StringComparer.Ordinal));
        Assert.Empty(result.Skipped);
    }

    /// <summary>계약(화면): 빼기를 켠 압축의 완료 안내는 '쓸모없는 파일 5개는 뺐어요' — 결과는 열어 보임.</summary>
    [Fact]
    public async Task 화면은_뺀_수를_알린다()
    {
        using var c = New("junkvm");
        string root = JunkFolder(c);
        c.Ui.CompressionRequestAnswer = Answer(c.Out);
        await Compress(c, root);

        var info = Assert.Single(c.Ui.Infos);
        Assert.StartsWith("쓸모없는 파일 5개는 뺐어요", info);
        Assert.Contains("__MACOSX 폴더", info);
        Assert.Equal(new[] { "자료/보고서.bak", "자료/보고서.txt", "자료/하위/메모.txt" }, await FileNamesIn(Path.Combine(c.Out, "자료.zip")));
    }

    /// <summary>계약: '쓸모없는 파일 빼기'를 끄면 아무것도 빼지 않는다(안내도 없음).</summary>
    [Fact]
    public async Task 빼기를_끄면_다_들어간다()
    {
        using var c = New("junkoff");
        string root = JunkFolder(c);
        c.Ui.CompressionRequestAnswer = Answer(c.Out, exclude: false);
        await Compress(c, root);

        Assert.Equal(8, (await FileNamesIn(Path.Combine(c.Out, "자료.zip"))).Count);
        Assert.Empty(c.Ui.Infos);
    }

    /// <summary>계약: 사용자 목록('*.bak', 폴더 '하위/')도 맞는다 — 목록을 바꾸면 기본 목록은 쓰지 않는다(Thumbs.db 는 들어감). 대소문자는 가리지 않는다.</summary>
    [Fact]
    public async Task 사용자_빼기_목록도_맞는다()
    {
        using var c = New("junkuser");
        string root = JunkFolder(c);
        c.Ui.CompressionRequestAnswer = Answer(c.Out, patterns: new[] { "*.BAK", "하위/", "" });
        await Compress(c, root);

        var names = await FileNamesIn(Path.Combine(c.Out, "자료.zip"));
        Assert.DoesNotContain("자료/보고서.bak", names);
        Assert.DoesNotContain(names, n => n.StartsWith("자료/하위/", StringComparison.Ordinal));
        Assert.Contains("자료/Thumbs.db", names);
        Assert.StartsWith("쓸모없는 파일 2개는 뺐어요", Assert.Single(c.Ui.Infos));
    }

    /// <summary>
    /// 계약(결정): 사용자가 직접 고른 파일·폴더는 빼기 목록에 걸려도 담는다(일부러 고른 desktop.ini 등). 안의 것이 모두 빠진 폴더는 빈 폴더로 남는다.
    /// </summary>
    [Fact]
    public async Task 직접_고른_것은_담고_다_빠진_폴더는_빈_폴더로()
    {
        using var c = New("junkpick");
        string ini = c.File("desktop.ini", "[.ShellClassInfo]");
        string photos = c.Dir("사진");
        c.File(@"사진\Thumbs.db", "캐시");
        string arc = Path.Combine(c.Out, "골라.zip");

        var result = await Svc.For(arc).CompressAsync(new[] { ini, photos }, arc,
            new CompressionOptions { ExcludeFilter = new PatternFileFilter(PatternFileFilter.DefaultPatterns) });

        var entries = await Svc.For(arc).ListEntriesAsync(arc);
        Assert.Contains(entries, e => !e.IsDirectory && e.Path == "desktop.ini");
        Assert.Contains(entries, e => e.IsDirectory && TreeAssert.NormalizeEntryPath(e.Path) == "사진");
        Assert.DoesNotContain(entries, e => e.Path.Contains("Thumbs.db", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(1, result.Excluded);
    }

    /// <summary>계약: 빼기 목록 규칙 — 이름 단위·대소문자 무시·'*' '?'·끝 '/' 는 폴더만(파일에는 안 걸림)·빈 줄은 무시.</summary>
    [Fact]
    public void 빼기_목록_규칙()
    {
        var f = new PatternFileFilter(new[] { "thumbs.DB", "~$*", "._*", "__MACOSX/", "*.~tmp", "보고서?.txt", "  ", "a/b.txt" });
        Assert.True(f.IsExcluded("Thumbs.db", false));
        Assert.False(f.IsExcluded("Thumbs.db", true));         // 파일 규칙은 폴더에 안 걸림
        Assert.True(f.IsExcluded("~$견적.xlsx", false));
        Assert.True(f.IsExcluded("._사진.jpg", false));
        Assert.True(f.IsExcluded("__macosx", true));
        Assert.False(f.IsExcluded("__MACOSX", false));          // 폴더 규칙은 파일에 안 걸림
        Assert.True(f.IsExcluded("작업.~TMP", false));
        Assert.True(f.IsExcluded("보고서1.txt", false));
        Assert.False(f.IsExcluded("보고서12.txt", false));
        Assert.False(f.IsExcluded("b.txt", false));             // 경로 규칙은 쓰지 않음
        Assert.False(f.IsExcluded("보고서.txt", false));
        Assert.Equal(7, f.Patterns.Count);                       // 빈 줄 뺌
        Assert.Equal(new[] { "Thumbs.db", "desktop.ini", ".DS_Store", "._*", "__MACOSX/", "~$*", "*.~tmp" }, PatternFileFilter.DefaultPatterns);
        Assert.Equal(new[] { "a", "b" }, PatternFileFilter.ParseLines("a\r\n\r\n  b  \nA\n"));
    }

    // ───────────── I3-4 원본 지우기 ─────────────

    /// <summary>
    /// 계약: 시험 통과 + '옮기기'면 압축에 들어간 원본(고른 파일·폴더 그대로)만 휴지통(가짜)으로. 묻는 문구 '원본 2개(…)를 휴지통으로 옮길까요?',
    /// 버튼 [옮기기][그대로 두기], 기본 버튼은 '그대로 두기'. 시험기는 만든 압축을 받았다. 진짜 휴지통·영구 삭제는 없다(원본 파일은 그대로 있음 — 가짜).
    /// </summary>
    [Fact]
    public async Task 시험을_통과하고_옮기기를_고르면_원본만_휴지통으로()
    {
        using var c = New("del");
        c.Services.CompressTester.Pass();
        string file = c.File("보고서.xlsx", "엑셀");
        string folder = c.Dir("거래처A");
        c.File(@"거래처A\견적.txt", "견적");
        c.Ui.ChoiceAnswers.Enqueue(0);
        c.Ui.CompressionRequestAnswer = Answer(c.Out, baseName: "묶음", delete: true);

        await Compress(c, file, folder);

        string arc = Path.Combine(c.Out, "묶음.zip");
        Assert.Equal(new[] { arc }, c.Services.CompressTester.Tested);
        var prompt = Assert.Single(c.Ui.ChoicePrompts);
        Assert.StartsWith("원본 2개(", prompt.Message);
        Assert.Contains("를 휴지통으로 옮길까요?", prompt.Message);
        Assert.Contains("- 거래처A 폴더", prompt.Message);
        Assert.Equal(new[] { "옮기기", "그대로 두기" }, prompt.Choices);
        Assert.Equal(1, prompt.DefaultIndex);
        Assert.Equal(new[] { file, folder }, c.Services.RecycleBin.AllSent);
        Assert.Contains(c.Ui.Infos, i => i.Contains("원본 2개를 휴지통으로 옮겼어요."));
        Assert.True(System.IO.File.Exists(file), "가짜 휴지통인데 파일이 사라짐");
    }

    /// <summary>계약: 진짜 시험기로도 통과(ZIP·7Z·TAR·나눈 ZIP·나눈 7Z·암호 ZIP·파일 이름까지 숨긴 7z) — 통과하면 묻고 옮긴다.</summary>
    [Theory]
    [InlineData(".zip", 0L, null, false)]
    [InlineData(".7z", 0L, null, false)]
    [InlineData(".tar", 0L, null, false)]
    [InlineData(".zip", 20_000L, null, false)]
    [InlineData(".7z", 20_000L, null, false)]
    [InlineData(".zip", 0L, FixtureCatalog.KoreanPassword, false)]
    [InlineData(".7z", 0L, FixtureCatalog.KoreanPassword, true)]
    public async Task 진짜_시험기로도_통과하면_옮긴다(string ext, long split, string? password, bool encryptNames)
    {
        using var c = New("delreal" + ext.TrimStart('.'));
        string root = SampleTree.Create(c.Src);
        var rnd = new Random(7);
        var bin = new byte[60_000];
        rnd.NextBytes(bin);
        System.IO.File.WriteAllBytes(Path.Combine(root, "무작위.bin"), bin);
        c.Ui.ChoiceAnswers.Enqueue(0);
        c.Ui.CompressionRequestAnswer = Answer(c.Out, FormatOf(ext), delete: true,
            options: new CompressionOptions { SplitBytes = split > 0 ? split : null, Password = password, EncryptFileNames = encryptNames });

        await Compress(c, root);

        Assert.Empty(c.Ui.Errors);
        Assert.Empty(c.Ui.Warnings);
        Assert.Single(c.Services.CompressTester.Tested);
        Assert.Equal(new[] { root }, c.Services.RecycleBin.AllSent);
        if (split > 0) Assert.True(Directory.GetFiles(c.Out).Length > 1, "나눠 만들지 않음");
    }

    /// <summary>계약: 시험에서 문제가 있으면 묻지도 않고 지우지 않는다 — '시험에서 문제가 있어 원본을 지우지 않았어요'. 만든 압축은 남는다.</summary>
    [Fact]
    public async Task 시험에_실패하면_지우지_않는다()
    {
        using var c = New("delfail");
        c.Services.CompressTester.Fail();
        string file = c.File("보고서.xlsx", "엑셀");
        c.Ui.ChoiceAnswers.Enqueue(0);
        c.Ui.CompressionRequestAnswer = Answer(c.Out, delete: true);

        await Compress(c, file);

        Assert.Empty(c.Services.RecycleBin.Sent);
        Assert.Empty(c.Ui.ChoicePrompts);
        Assert.Contains(CompressFlow.TestFailedKeptMessage, Assert.Single(c.Ui.Warnings));
        Assert.True(System.IO.File.Exists(Path.Combine(c.Out, "보고서.zip")));
    }

    /// <summary>계약: '그대로 두기'·창 닫기면 지우지 않는다.</summary>
    [Theory]
    [InlineData(1)]
    [InlineData(-1)]
    public async Task 그대로_두기면_지우지_않는다(int answer)
    {
        using var c = New("delkeep");
        c.Services.CompressTester.Pass();
        string file = c.File("보고서.xlsx", "엑셀");
        c.Ui.ChoiceAnswers.Enqueue(answer);
        c.Ui.CompressionRequestAnswer = Answer(c.Out, delete: true);

        await Compress(c, file);

        Assert.Single(c.Ui.ChoicePrompts);
        Assert.Empty(c.Services.RecycleBin.Sent);
    }

    /// <summary>
    /// 계약: 원본 지우기는 기본 꺼짐 — 설정 기본값·창의 처음 값 모두 꺼짐이고, 꺼져 있으면 시험도 묻기도 휴지통도 없다(예전 저장 창·옵션 창 흐름 그대로).
    /// </summary>
    [Fact]
    public async Task 원본_지우기는_기본_꺼짐()
    {
        Assert.False(new CompressSettings().DeleteOriginalsAfter);
        Assert.False(new AppSettings().Compress.DeleteOriginalsAfter);
        using var c = New("deldefault");
        string file = c.File("보고서.xlsx", "엑셀");
        c.Ui.SavePath = Path.Combine(c.Out, "보고서.zip");   // 예전 관례(저장 창 + 옵션 창)의 답
        c.Ui.CompressionOptions = CompressionOptions.Default;

        await Compress(c, file);

        Assert.False(Assert.Single(c.Ui.CompressionPrompts).Defaults.DeleteOriginalsAfter);
        Assert.True(System.IO.File.Exists(Path.Combine(c.Out, "보고서.zip")));
        Assert.Empty(c.Services.CompressTester.Tested);
        Assert.Empty(c.Ui.ChoicePrompts);
        Assert.Empty(c.Services.RecycleBin.Sent);
    }

    /// <summary>계약: 휴지통이 없는 곳(네트워크 드라이브·USB 흉내)의 원본은 묻지도 않고 지우지 않으며 까닭을 알린다.</summary>
    [Fact]
    public async Task 휴지통이_없는_곳의_원본은_지우지_않는다()
    {
        using var c = New("delnet");
        c.Services.CompressTester.Pass();
        string file = c.File("보고서.xlsx", "엑셀");
        c.Services.RecycleBin.NotRecyclable.Add(c.Src);
        c.Ui.ChoiceAnswers.Enqueue(0);
        c.Ui.CompressionRequestAnswer = Answer(c.Out, delete: true);

        await Compress(c, file);

        Assert.Empty(c.Ui.ChoicePrompts);
        Assert.Empty(c.Services.RecycleBin.Sent);
        Assert.Contains("네트워크 드라이브나 USB", Assert.Single(c.Ui.Warnings));
        Assert.True(CompressFlow.IsOnDriveWithoutRecycleBin(@"\\서버\공유\보고서.xlsx"));   // 공유 폴더는 늘 휴지통 없음으로
        Assert.False(CompressFlow.IsOnDriveWithoutRecycleBin(file));
    }

    /// <summary>계약: 각각 따로에서 한 압축만 시험에 실패하면 그 원본만 남고 나머지는 묻고 옮긴다.</summary>
    [Fact]
    public async Task 각각_따로에서_시험에_실패한_원본만_남는다()
    {
        using var c = New("deleach");
        c.Services.CompressTester.Pass();
        c.Services.CompressTester.FailFor.Add("나");
        string a = c.File("가.txt", "a");
        string b = c.File("나.txt", "b");
        string d = c.Dir("다 폴더");
        c.File(@"다 폴더\c.txt", "c");
        c.Ui.ChoiceAnswers.Enqueue(0);
        c.Ui.CompressionRequestAnswer = Answer(c.Out, each: true, delete: true);

        await Compress(c, a, b, d);

        Assert.Equal(3, c.Services.CompressTester.Tested.Count);
        Assert.StartsWith("원본 2개(", Assert.Single(c.Ui.ChoicePrompts).Message);
        Assert.Equal(new[] { a, d }, c.Services.RecycleBin.AllSent);
        var summary = Assert.Single(c.Ui.Summaries);
        Assert.Contains(summary.Notes, n => n.Contains("나.zip") && n.Contains(CompressFlow.TestFailedKeptMessage));
        Assert.Contains(summary.Notes, n => n.Contains("원본 2개를 휴지통으로 옮겼어요."));
    }

    /// <summary>계약: 뺀 파일(쓸모없는 파일)이 든 폴더는 지우지 않는다(지우면 압축에 없는 파일까지 사라짐) — 같이 고른 다른 원본은 옮긴다.</summary>
    [Fact]
    public async Task 뺀_파일이_든_폴더는_지우지_않는다()
    {
        using var c = New("deljunk");
        c.Services.CompressTester.Pass();
        string root = JunkFolder(c);
        string file = c.File("보고서.hwp", "한글");
        c.Ui.ChoiceAnswers.Enqueue(0);
        c.Ui.CompressionRequestAnswer = Answer(c.Out, baseName: "묶음", delete: true);

        await Compress(c, root, file);

        Assert.Equal(new[] { file }, c.Services.RecycleBin.AllSent);
        Assert.Contains(c.Ui.Warnings, w => w.Contains("‘자료’ 폴더 안에 뺀 파일") && w.Contains("5개"));
    }

    /// <summary>계약: 압축 파일을 원본 폴더 안에 만들었으면 그 폴더는 지우지 않는다(지우면 압축 파일까지 사라짐).</summary>
    [Fact]
    public async Task 압축_파일이_원본_폴더_안이면_그_폴더는_지우지_않는다()
    {
        using var c = New("delinside");
        c.Services.CompressTester.Pass();
        string folder = c.Dir("거래처A");
        c.File(@"거래처A\견적.txt", "견적");
        c.Ui.ChoiceAnswers.Enqueue(0);
        c.Ui.CompressionRequestAnswer = Answer(folder, delete: true);

        await Compress(c, folder);

        Assert.True(System.IO.File.Exists(Path.Combine(folder, "거래처A.zip")));
        Assert.Empty(c.Ui.ChoicePrompts);
        Assert.Empty(c.Services.RecycleBin.Sent);
        Assert.Contains("폴더 안에 압축 파일을 만들어서", Assert.Single(c.Ui.Warnings));
    }

    /// <summary>계약: 묻는 동안 원본이 바뀌면(크기가 압축과 다름) 옮기기 직전에 다시 맞춰 보고 지우지 않는다.</summary>
    [Fact]
    public async Task 묻는_동안_원본이_바뀌면_지우지_않는다()
    {
        using var c = New("delchanged");
        c.Services.CompressTester.Pass();
        string file = c.File("보고서.txt", "처음 내용");
        c.Ui.ChoiceAnswers.Enqueue(0);
        c.Ui.OnChoicePrompt = _ => System.IO.File.AppendAllText(file, " 더 씀");
        c.Ui.CompressionRequestAnswer = Answer(c.Out, delete: true);

        await Compress(c, file);

        Assert.Single(c.Ui.ChoicePrompts);
        Assert.Empty(c.Services.RecycleBin.Sent);
        Assert.Contains("압축 파일 내용과 맞지 않아", Assert.Single(c.Ui.Warnings));
    }

    /// <summary>
    /// 계약(W2-07 검증 지적): 크기는 같고 내용만 바뀐 원본도 지우지 않는다 — 압축하기 직전의 크기·수정 시각과 맞춰 본다.
    /// 묻는 동안 'AAAA' → 'BBBB'(같은 4바이트)로 고쳐 저장해도, 압축하는 동안 고쳐 저장해도 휴지통으로 보내지 않고 까닭을 알린다(압축에는 옛 내용만 있음).
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task 크기가_같아도_내용이_바뀐_원본은_지우지_않는다(bool duringCompression)
    {
        using var c = New("delsamesize");
        c.Services.CompressTester.Pass();
        string file = c.File("보고서.txt", "AAAA");
        // 처음 파일의 수정 시각을 예전 날로 — 고쳐 저장하면 '지금'이 되어 시계 눈금과 상관없이 늘 달라짐(저장 프로그램이 하는 것과 같음)
        System.IO.File.SetLastWriteTimeUtc(file, new DateTime(2025, 3, 4, 5, 6, 8, DateTimeKind.Utc));
        bool changed = false;
        void Change()
        {
            if (changed) return;
            changed = true;
            System.IO.File.WriteAllText(file, "BBBB");
        }
        if (duringCompression)
            c.Ui.OnProgressReport = p => { if (p.BytesProcessed > 0 && !p.IsStage) Change(); };
        else
            c.Ui.OnChoicePrompt = _ => Change();
        c.Ui.ChoiceAnswers.Enqueue(0);
        c.Ui.CompressionRequestAnswer = Answer(c.Out, delete: true);

        await Compress(c, file);

        Assert.True(changed, "원본을 고치지 못함(시험 준비 실패)");
        Assert.Equal(4, new FileInfo(file).Length);
        Assert.Empty(c.Services.RecycleBin.Sent);
        Assert.Contains("압축 파일 내용과 맞지 않아", Assert.Single(c.Ui.Warnings));
        if (duringCompression) Assert.Empty(c.Ui.ChoicePrompts);   // 묻기 전에 걸러짐
    }

    /// <summary>
    /// 계약(W2-07 검증 지적): 폴더와 그 안의 파일을 같이 골라도(대기 목록이 막지 않음) 휴지통으로는 폴더만 — 묻는 문구의 수·크기를 두 번 세지 않고,
    /// 보내는 목록에 부모와 자식이 같이 들어가지 않는다(하나로·각각 따로 모두).
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task 폴더와_그_안의_파일을_같이_골라도_폴더만_옮긴다(bool each)
    {
        using var c = New("delnested" + (each ? "each" : "one"));
        c.Services.CompressTester.Pass();
        string folder = c.Dir("A");
        string inner = c.File(@"A\b.txt", "b");
        c.Ui.ChoiceAnswers.Enqueue(0);
        c.Ui.CompressionRequestAnswer = Answer(c.Out, baseName: "묶음", each: each, delete: true);

        await Compress(c, folder, inner);

        var prompt = Assert.Single(c.Ui.ChoicePrompts);
        Assert.StartsWith(CompressFlow.RecycleQuestion(1, 1), prompt.Message);   // '원본 1개(1바이트)를 …' — 두 번 세지 않음
        Assert.Contains("- A 폴더", prompt.Message);
        Assert.DoesNotContain("- b.txt", prompt.Message);
        Assert.Equal(new[] { folder }, c.Services.RecycleBin.AllSent);
    }

    /// <summary>계약: 휴지통으로 못 보내면 경고만 하고 그대로 둔다(영구 삭제로 바꾸지 않음).</summary>
    [Fact]
    public async Task 휴지통_실패는_경고만()
    {
        using var c = New("delrecyclefail");
        c.Services.CompressTester.Pass();
        c.Services.RecycleBin.Result = new RecycleResult(false, "휴지통으로 옮기지 못했어요(흉내).");
        string file = c.File("보고서.txt", "내용");
        c.Ui.ChoiceAnswers.Enqueue(0);
        c.Ui.CompressionRequestAnswer = Answer(c.Out, delete: true);

        await Compress(c, file);

        Assert.Single(c.Services.RecycleBin.Sent);
        Assert.Contains("원본을 휴지통으로 옮기지 못해 그대로 두었어요.", Assert.Single(c.Ui.Warnings));
        Assert.True(System.IO.File.Exists(file));
        Assert.Empty(c.Ui.Errors);
    }

    /// <summary>계약: GZ·BZ2 는 원본을 지우지 않는다(목록에 크기가 없어 원본과 맞춰 볼 수 없음) — 묻지 않고 까닭만.</summary>
    [Fact]
    public async Task GZ는_원본을_지우지_않는다()
    {
        using var c = New("delgz");
        c.Services.CompressTester.Pass();
        string file = c.File("보고서.xlsx", "엑셀");
        c.Ui.ChoiceAnswers.Enqueue(0);
        c.Ui.CompressionRequestAnswer = Answer(c.Out, CompressionFormat.GZip, baseName: "보고서.xlsx", delete: true);

        await Compress(c, file);

        Assert.True(System.IO.File.Exists(Path.Combine(c.Out, "보고서.xlsx.gz")));
        Assert.Empty(c.Ui.ChoicePrompts);
        Assert.Empty(c.Services.RecycleBin.Sent);
        Assert.Contains("GZ·BZ2 는 원본을 지우지 않아요", Assert.Single(c.Ui.Warnings));
    }

    /// <summary>계약: '다 만들고 시험하기'만 켜면 시험하고 '문제없어요'를 알린다(원본은 묻지 않음). 시험이 틀리면 경고.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task 다_만들고_시험하기(bool fail)
    {
        using var c = New("testafter");
        if (fail) c.Services.CompressTester.Fail();
        string file = c.File("보고서.txt", "내용");
        c.Ui.CompressionRequestAnswer = Answer(c.Out, test: true);

        await Compress(c, file);

        Assert.Single(c.Services.CompressTester.Tested);
        Assert.Empty(c.Ui.ChoicePrompts);
        if (fail)
            Assert.Contains("시험했더니 문제가 있어요", Assert.Single(c.Ui.Warnings));
        else
            Assert.Contains("만든 압축 파일을 시험해 봤어요 — 문제없어요.", Assert.Single(c.Ui.Infos));
    }

    // ───────────── 시험기(진짜) ─────────────

    /// <summary>계약: 진짜 시험기는 멀쩡한 압축은 통과시키고, 가운데가 망가진 압축(ZIP·7Z·GZ·BZ2)은 오류로 잡는다(디스크에 아무것도 남기지 않음).</summary>
    [Theory]
    [InlineData(".zip")]
    [InlineData(".7z")]
    [InlineData(".gz")]
    [InlineData(".bz2")]
    public async Task 진짜_시험기는_망가진_압축을_잡는다(string ext)
    {
        using var t = new TempDir("tester" + ext.TrimStart('.'));
        string src = Path.Combine(t.Dir("원본"), "보고서.txt");
        System.IO.File.WriteAllText(src, string.Concat(Enumerable.Range(0, 4000).Select(i => $"줄 {i} 내용 {i * 7919 % 1000}\n")));
        string arc = t.P("만든" + ext);
        await Svc.For(arc).CompressAsync(new[] { src }, arc);

        CreatedArchiveTester.Instance.Test(arc, null, CancellationToken.None);   // 멀쩡하면 통과

        var bytes = System.IO.File.ReadAllBytes(arc);
        int mid = bytes.Length / 2;
        for (int i = mid; i < mid + 8 && i < bytes.Length; i++) bytes[i] ^= 0x5A;
        System.IO.File.WriteAllBytes(arc, bytes);
        var before = Directory.GetFileSystemEntries(t.Root, "*", SearchOption.AllDirectories).Length;
        Assert.ThrowsAny<Exception>(() => CreatedArchiveTester.Instance.Test(arc, null, CancellationToken.None));
        Assert.Equal(before, Directory.GetFileSystemEntries(t.Root, "*", SearchOption.AllDirectories).Length);
    }

    // ───────────── I2-12 설정 기억 ─────────────

    /// <summary>계약: settings.json 에 쓰고 다시 읽으면 compress.* 값이 같다. 키는 camelCase('compress' 묶음), 'password' 는 없다.</summary>
    [Fact]
    public void 압축_설정을_저장하고_다시_읽으면_같다()
    {
        using var t = new TempDir("cset");
        var store = new JsonSettingsStore(t.P("settings.json"));
        var s = new AppSettings();
        var cs = s.Compress;
        cs.LastFormat = CompressionFormat.SevenZip;
        cs.Level = CompressionLevelOption.Maximum;
        cs.Solid = false;
        cs.EncryptFileNames = true;
        cs.ZipEncryption = ZipEncryption.ZipCrypto;
        cs.SplitBytes = 20 * 1024 * 1024;
        cs.EachSeparately = true;
        cs.DateInName = DateNameFormat.YearMonthDayDashed;
        cs.ExcludeEnabled = false;
        cs.ExcludePatterns = new List<string> { "*.bak", "임시/" };
        cs.TestAfter = true;
        cs.DeleteOriginalsAfter = true;
        cs.LastFolder = @"C:\보낼 것";
        store.Save(s);

        var back = store.Load().Compress;
        Assert.Equal(CompressionFormat.SevenZip, back.LastFormat);
        Assert.Equal(CompressionLevelOption.Maximum, back.Level);
        Assert.False(back.Solid);
        Assert.True(back.EncryptFileNames);
        Assert.Equal(ZipEncryption.ZipCrypto, back.ZipEncryption);
        Assert.Equal(20 * 1024 * 1024, back.SplitBytes);
        Assert.True(back.EachSeparately);
        Assert.Equal(DateNameFormat.YearMonthDayDashed, back.DateInName);
        Assert.False(back.ExcludeEnabled);
        Assert.Equal(new[] { "*.bak", "임시/" }, back.ExcludePatterns);
        Assert.True(back.TestAfter);
        Assert.True(back.DeleteOriginalsAfter);
        Assert.Equal(@"C:\보낼 것", back.LastFolder);

        string json = System.IO.File.ReadAllText(t.P("settings.json"));
        Assert.Contains("\"compress\"", json);
        Assert.Contains("\"lastFormat\": \"SevenZip\"", json);
        Assert.Contains("\"dateInName\": \"YearMonthDayDashed\"", json);
        Assert.Contains("\"zipEncryption\": \"ZipCrypto\"", json);
        Assert.DoesNotContain("password", json, StringComparison.OrdinalIgnoreCase);

        var fresh = new AppSettings().Compress;   // 기본값(설계 §4)
        Assert.Equal(CompressionFormat.Zip, fresh.LastFormat);
        Assert.Equal(CompressionLevelOption.Normal, fresh.Level);
        Assert.True(fresh.Solid);
        Assert.False(fresh.EncryptFileNames);
        Assert.Equal(ZipEncryption.Aes256, fresh.ZipEncryption);
        Assert.Null(fresh.SplitBytes);
        Assert.False(fresh.EachSeparately);
        Assert.Equal(DateNameFormat.None, fresh.DateInName);
        Assert.True(fresh.ExcludeEnabled);
        Assert.Equal(PatternFileFilter.DefaultPatterns, fresh.ExcludePatterns);
        Assert.False(fresh.TestAfter);
        Assert.False(fresh.DeleteOriginalsAfter);
    }

    /// <summary>계약: 깨진 settings.json 이면 기본값으로 시작하고 깨진 파일은 settings.bad-시각.json 으로 옮겨 둔다.</summary>
    [Fact]
    public void 깨진_설정이면_기본값과_bad_백업()
    {
        using var t = new TempDir("cbad");
        string path = t.P("settings.json");
        System.IO.File.WriteAllText(path, "{ \"compress\": { \"level\": ");
        var loaded = new JsonSettingsStore(path).Load();

        Assert.Equal(CompressionLevelOption.Normal, loaded.Compress.Level);
        Assert.True(loaded.Compress.ExcludeEnabled);
        Assert.Equal(PatternFileFilter.DefaultPatterns, loaded.Compress.ExcludePatterns);
        Assert.False(System.IO.File.Exists(path));
        Assert.Single(Directory.GetFiles(t.Root, "settings.bad-*.json"));
    }

    /// <summary>계약: 모르는 필드(뒤 판이 더한 것 — 맨 위·compress 안 모두)는 읽고 다시 써도 남는다. compress 가 빠진 옛 설정은 기본값으로 채운다.</summary>
    [Fact]
    public void 모르는_필드는_보존된다()
    {
        using var t = new TempDir("cunk");
        string path = t.P("settings.json");
        System.IO.File.WriteAllText(path,
            "{ \"schemaVersion\": 1, \"future\": { \"x\": 1 }, \"compress\": { \"level\": \"Maximum\", \"futureKey\": 7, \"excludePatterns\": null } }");
        var store = new JsonSettingsStore(path);
        var s = store.Load();
        Assert.Equal(CompressionLevelOption.Maximum, s.Compress.Level);
        Assert.Equal(PatternFileFilter.DefaultPatterns, s.Compress.ExcludePatterns);   // null 이면 기본 목록
        store.Save(s);
        string json = System.IO.File.ReadAllText(path);
        Assert.Contains("\"future\"", json);
        Assert.Contains("\"futureKey\": 7", json);

        System.IO.File.WriteAllText(path, "{ \"schemaVersion\": 1, \"extract\": { \"openFolderAfter\": false } }");
        var old = store.Load();
        Assert.False(old.Extract.OpenFolderAfter);
        Assert.Equal(CompressionFormat.Zip, old.Compress.LastFormat);
        Assert.Equal(PatternFileFilter.DefaultPatterns, old.Compress.ExcludePatterns);
    }

    /// <summary>
    /// 계약(I2-12): 압축 창을 [확인]으로 닫으면 그 값(형식·수준·7z 칸·나누기·각각 따로·날짜·빼기·시험·만든 곳)을 설정에 기억해 한 번 저장한다 — 암호는 저장하지 않는다.
    /// 다음 압축 창의 처음 값이 그 값이다.
    /// </summary>
    [Fact]
    public async Task 압축_창의_값을_기억하고_암호는_저장하지_않는다()
    {
        using var c = New("remember");
        string a = c.File("가.txt", "a");
        string b = c.File("나.txt", "b");
        const string Secret = "비밀번호-1234-가나";
        c.Ui.CompressionRequestAnswer = Answer(c.Out, CompressionFormat.SevenZip, each: true, date: DateNameFormat.ShortYearMonthDay,
            patterns: new[] { "*.bak" }, test: true,
            options: new CompressionOptions { Level = CompressionLevelOption.Maximum, Solid = false, Password = Secret, EncryptFileNames = true });

        await Compress(c, a, b);

        Assert.Equal(1, c.Services.Settings.SaveCount);
        string json = c.Services.Settings.Json!;
        Assert.DoesNotContain(Secret, json);
        Assert.DoesNotContain("password", json, StringComparison.OrdinalIgnoreCase);
        var saved = c.Services.Settings.Load().Compress;
        Assert.Equal(CompressionFormat.SevenZip, saved.LastFormat);
        Assert.Equal(CompressionLevelOption.Maximum, saved.Level);
        Assert.False(saved.Solid);
        Assert.True(saved.EncryptFileNames);
        Assert.True(saved.EachSeparately);
        Assert.Equal(DateNameFormat.ShortYearMonthDay, saved.DateInName);
        Assert.Equal(new[] { "*.bak" }, saved.ExcludePatterns);
        Assert.True(saved.TestAfter);
        Assert.Equal(c.Out, saved.LastFolder);

        // 다음 압축 창의 처음 값
        c.Ui.CompressionRequestAnswer = _ => null;
        await Compress(c, c.File("다.txt", "c"));
        var next = c.Ui.CompressionPrompts[^1].Defaults;
        Assert.Equal(CompressionFormat.SevenZip, next.LastFormat);
        Assert.Equal(CompressionLevelOption.Maximum, next.Level);
        Assert.Equal(DateNameFormat.ShortYearMonthDay, next.DateInName);
        Assert.Equal(1, c.Services.Settings.SaveCount);   // 취소하면 저장하지 않음
    }

    /// <summary>계약: 하나로 만들 때 같은 이름이 있으면 창이 [바꾸기]를 골랐을 때만 바꾸고(예전 저장 창과 같음), 아니면 '이름 (2)' — 있던 파일은 그대로.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task 같은_이름이_있으면_바꾸기를_골랐을_때만_바꾼다(bool overwrite)
    {
        using var c = New("single-exists");
        string file = c.File("보고서.txt", "새 내용");
        string existing = Path.Combine(c.Out, "보고서.zip");
        System.IO.File.WriteAllText(existing, "예전 압축");
        c.Ui.CompressionRequestAnswer = Answer(c.Out, overwrite: overwrite);

        await Compress(c, file);

        if (overwrite)
        {
            Assert.Equal(new[] { "보고서.txt" }, await FileNamesIn(existing));
            Assert.False(System.IO.File.Exists(Path.Combine(c.Out, "보고서 (2).zip")));
        }
        else
        {
            Assert.Equal("예전 압축", System.IO.File.ReadAllText(existing));
            Assert.Equal(new[] { "보고서.txt" }, await FileNamesIn(Path.Combine(c.Out, "보고서 (2).zip")));
            Assert.Equal(Path.Combine(c.Out, "보고서 (2).zip"), c.Vm.CurrentArchivePath);
        }
    }

    /// <summary>
    /// 계약(W2-07 검증 지적 — 예전 저장 창 흐름부터 있던 일): 원본이 압축 파일 하나('a.zip')이고 같은 곳에 같은 이름으로 만들면, [바꾸기]를 골랐다고 넘겨도
    /// 원본을 덮지 않고 'a (2).zip' 으로 비켜 만든다 — 예전엔 쓰는 쪽이 자기 자신을 원본에서 빼고 덮어써 빈 압축(ZIP 22바이트·TAR 1024바이트)을 '압축 완료'로 알렸다.
    /// </summary>
    [Theory]
    [InlineData(".zip")]
    [InlineData(".7z")]
    [InlineData(".tar")]
    public async Task 원본_그_자체는_바꾸기를_골라도_덮지_않는다(string ext)
    {
        using var c = New("selftarget" + ext.TrimStart('.'));
        string self = c.File("a" + ext, "원래 압축 — 사라지면 안 됨");
        string sha = TreeAssert.Sha256(self);
        c.Ui.CompressionRequestAnswer = Answer(c.Src, FormatOf(ext), baseName: "a", overwrite: true);

        await Compress(c, self);

        Assert.Empty(c.Ui.Errors);
        Assert.Equal(sha, TreeAssert.Sha256(self));   // 원본 그대로
        string made = Path.Combine(c.Src, "a (2)" + ext);
        Assert.Equal(new[] { "a" + ext }, await FileNamesIn(made));
        Assert.Equal(made, c.Vm.CurrentArchivePath);
    }

    /// <summary>계약(W2-07 검증 지적): 각각 따로에서 같은 이름 처리기가 '덮어쓰기'여도 원본 그 자체는 덮지 않는다(다른 원본은 처리기대로).</summary>
    [Fact]
    public async Task 각각_따로에서도_원본_그_자체는_덮지_않는다()
    {
        using var t = new TempDir("selftargeteach");
        string folder = t.Dir("원본");
        string self = Path.Combine(folder, "a.zip");
        File.WriteAllText(self, "원래 압축");
        string other = Path.Combine(folder, "b.txt");
        File.WriteAllText(other, "b 새 내용");
        string oldB = Path.Combine(folder, "b.zip");
        File.WriteAllText(oldB, "예전 b 압축");
        string sha = TreeAssert.Sha256(self);
        var ui = new FakeUserInteraction();
        var flow = new CompressFlow(new ArchiveServiceLocator(), ui, new FakeRecycleBin(), new FakeCompressTester(), new FakeClock().SetLocal(2026, 10, 13),
            new FixedResolver(CollisionChoice.Overwrite));

        var result = await flow.RunAsync(new CompressionRequest
        {
            Sources = new[] { self, other },
            OutputFolder = folder,
            Format = CompressionFormat.Zip,
            EachSeparately = true,
        }, ui.ShowProgress("압축 중..."));

        Assert.Equal(2, result.SucceededCount);
        Assert.Equal(sha, TreeAssert.Sha256(self));
        Assert.Equal(new[] { "a.zip" }, await FileNamesIn(Path.Combine(folder, "a (2).zip")));
        Assert.Equal(new[] { "b.txt" }, await FileNamesIn(oldB));   // 다른 원본은 처리기대로 덮어씀
    }

    // ───────────── GZ·BZ2 이름의 날짜 ─────────────

    /// <summary>
    /// 계약(W2-07 검증 지적): GZ·BZ2 에 날짜를 붙이면 원래 확장자 앞에 — '보고서_20261013.xlsx.gz', 풀면 '보고서_20261013.xlsx'(엑셀로 바로 열림).
    /// 각각 따로 + BZ2 도 같다. 같은 이름이 있어 비킬 때도 번호를 확장자 앞에('보고서_20261013 (2).xlsx.gz').
    /// </summary>
    [Theory]
    [InlineData(".gz", DateNameFormat.YearMonthDay, "보고서_20261013.xlsx")]
    [InlineData(".bz2", DateNameFormat.YearMonthDayDashed, "보고서_2026-10-13.xlsx")]
    public async Task GZ_BZ2의_날짜는_원래_확장자_앞에_붙는다(string ext, DateNameFormat date, string inner)
    {
        using var c = New("gzdate" + ext.TrimStart('.'));
        string f = c.File("보고서.xlsx", "엑셀 흉내");
        c.Ui.CompressionRequestAnswer = Answer(c.Out, FormatOf(ext), baseName: "보고서.xlsx", date: date);

        await Compress(c, f);
        string path = Path.Combine(c.Out, inner + ext);
        Assert.True(System.IO.File.Exists(path), string.Join(", ", Directory.GetFiles(c.Out).Select(Path.GetFileName)));
        Assert.Equal(new[] { inner }, (await ExtractSnapshot(c.Temp, path)).Keys);

        // 한 번 더 — 같은 이름이 있으니 번호도 원래 확장자 앞에
        await CompressAgain(c, f);
        string second = Path.Combine(c.Out, Path.GetFileNameWithoutExtension(inner) + " (2).xlsx" + ext);
        Assert.True(System.IO.File.Exists(second), string.Join(", ", Directory.GetFiles(c.Out).Select(Path.GetFileName)));
        Assert.Equal(new[] { Path.GetFileNameWithoutExtension(inner) + " (2).xlsx" }, (await ExtractSnapshot(c.Temp, second)).Keys);
    }

    /// <summary>계약(W2-07 검증 지적): 각각 따로 + BZ2 + 날짜 — 원본마다 '이름_날짜.원래확장자.bz2'. 확장자로 보지 않는 것(공백·한글)은 끝에 붙인다.</summary>
    [Fact]
    public async Task 각각_따로_BZ2에도_날짜는_원래_확장자_앞에()
    {
        using var c = New("bz2each");
        string a = c.File("가.txt", "a");
        string b = c.File("나 자료.csv", "b");
        c.Ui.CompressionRequestAnswer = Answer(c.Out, CompressionFormat.BZip2, each: true, date: DateNameFormat.ShortYearMonthDay);
        await Compress(c, a, b);
        Assert.Equal(new[] { "가_261013.txt.bz2", "나 자료_261013.csv.bz2" },
            Directory.GetFiles(c.Out).Select(Path.GetFileName).OrderBy(n => n, StringComparer.Ordinal));

        var day = new DateTime(2026, 10, 13, 9, 0, 0, DateTimeKind.Local);
        Assert.Equal("backup_20261013.tar", CompressNaming.WithDate("backup.tar", DateNameFormat.YearMonthDay, day, CompressionFormat.GZip));
        Assert.Equal("보고서 v1.2 최종_20261013", CompressNaming.WithDate("보고서 v1.2 최종", DateNameFormat.YearMonthDay, day, CompressionFormat.GZip));
        Assert.Equal(".bashrc_20261013", CompressNaming.WithDate(".bashrc", DateNameFormat.YearMonthDay, day, CompressionFormat.BZip2));
        Assert.Equal("보고서_20261013.xlsx", CompressNaming.WithDate("보고서_20261013.xlsx", DateNameFormat.YearMonthDay, day, CompressionFormat.GZip));
        Assert.Equal("자료.v2_20261013", CompressNaming.WithDate("자료.v2", DateNameFormat.YearMonthDay, day, CompressionFormat.Zip));   // ZIP 은 끝에
    }

    // ───────────── 진행 창의 시험 단계 알림 ─────────────

    /// <summary>
    /// 계약(W2-07 검증 지적): '만든 압축 파일을 시험하는 중' 단계 알림은 진짜 진행 창 앞의 솎아내기(<see cref="ThrottledArchiveProgress"/>)를 지나 창까지 간다 —
    /// 압축이 끝나자마자 오는 한 번뿐인 알림이라 예전엔 40ms 안의 보고로 버려져 창이 '압축 100%'에 멈춘 것처럼 보였다. 맞춰 보기 단계 알림도 같다.
    /// </summary>
    [Fact]
    public async Task 시험_단계_알림은_솎아내기를_지나_진행_창에_닿는다()
    {
        using var t = new TempDir("stagethrottle");
        string src = t.Dir("원본");
        File.WriteAllText(Path.Combine(src, "보고서.txt"), string.Concat(Enumerable.Range(0, 2000).Select(i => $"줄 {i}\n")));
        string outDir = t.Dir("만든곳");

        var seen = new List<ArchiveProgress>();
        var session = ThrottledSession.Create(seen, intervalMs: 60_000);   // 간격을 아주 크게 — 첫 중간 보고 뒤로는 단계·완료 보고만 지나감
        var ui = new FakeUserInteraction();
        var flow = new CompressFlow(new ArchiveServiceLocator(), ui, new FakeRecycleBin(), new FakeCompressTester().Pass(), new FakeClock().SetLocal(2026, 10, 13));

        await flow.RunAsync(new CompressionRequest
        {
            Sources = new[] { src },
            OutputFolder = outDir,
            BaseName = "원본",
            Format = CompressionFormat.Zip,
            TestAfter = true,
            DeleteOriginalsAfter = true,
        }, session);

        List<ArchiveProgress> shown;
        lock (seen) shown = seen.ToList();
        var testing = Assert.Single(shown, p => p.CurrentFile.StartsWith(CompressFlow.StageTesting, StringComparison.Ordinal));
        Assert.True(testing.IsStage);
        Assert.Equal(0, testing.TotalBytes);   // 크기를 모름 — 진행 창은 움직이는 막대
        Assert.Contains(shown, p => p.CurrentFile == CompressFlow.StageChecking && p.IsStage);
        Assert.True(shown.FindIndex(p => p.CurrentFile.StartsWith(CompressFlow.StageTesting, StringComparison.Ordinal))
                    < shown.FindIndex(p => p.CurrentFile == CompressFlow.StageChecking));
    }

    /// <summary>솎아내기(<see cref="ThrottledArchiveProgress"/> — 컨텍스트 없이 그 자리에서)를 진행률로 쓰는 진행 창 흉내.</summary>
    private sealed class ThrottledSession : IProgressSession
    {
        private ThrottledSession(IProgress<ArchiveProgress> progress) => Progress = progress;

        public static ThrottledSession Create(List<ArchiveProgress> seen, long intervalMs)
        {
            var prev = SynchronizationContext.Current;
            SynchronizationContext.SetSynchronizationContext(null);
            try
            {
                return new ThrottledSession(new ThrottledArchiveProgress(p => { lock (seen) seen.Add(p); }, intervalMs));
            }
            finally
            {
                SynchronizationContext.SetSynchronizationContext(prev);
            }
        }

        public IProgress<ArchiveProgress> Progress { get; }
        public CancellationToken CancellationToken => CancellationToken.None;
        public void Close() { }
    }

    /// <summary>
    /// 계약(W2-07 검증 지적): 압축 창에서 켠 '원본을 휴지통으로'는 기억하지 않는다 — 다음 창은 설정(☰ ▸ 설정의 처음 값, 기본 꺼짐)대로.
    /// 설정 창에서 켜 둔 것은 압축 창이 끄고 만들어도 그대로(압축 창은 이 설정을 바꾸지 않음).
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task 원본_지우기는_압축_창에서_기억하지_않는다(bool setInSettings)
    {
        using var c = New("delnotremember", TestServices.WithCompress(s => s.DeleteOriginalsAfter = setInSettings));
        c.Services.CompressTester.Pass();
        string f = c.File("보고서.txt", "내용");
        c.Ui.ChoiceAnswers.Enqueue(1);   // 묻으면 '그대로 두기'
        c.Ui.CompressionRequestAnswer = Answer(c.Out, delete: !setInSettings);

        await Compress(c, f);

        Assert.Equal(1, c.Services.Settings.SaveCount);
        Assert.Equal(setInSettings, c.Services.Settings.Load().Compress.DeleteOriginalsAfter);
        c.Ui.CompressionRequestAnswer = _ => null;
        await CompressAgain(c, c.File("다.txt", "c"));
        Assert.Equal(2, c.Ui.CompressionPrompts.Count);
        Assert.Equal(setInSettings, c.Ui.CompressionPrompts[^1].Defaults.DeleteOriginalsAfter);
    }

    /// <summary>계약(I2-12 — W2-07 검증 지적): 'folderChoice'·'fixedFolder' 도 settings.json 에 쓰고 다시 읽으면 같다(기본은 원본이 있는 폴더).</summary>
    [Fact]
    public void 만들_곳_설정을_저장하고_다시_읽으면_같다()
    {
        using var t = new TempDir("csetfolder");
        var store = new JsonSettingsStore(t.P("settings.json"));
        var s = new AppSettings();
        s.Compress.FolderChoice = CompressFolderChoice.FixedFolder;
        s.Compress.FixedFolder = @"C:\늘 보내는 곳";
        store.Save(s);

        var back = store.Load().Compress;
        Assert.Equal(CompressFolderChoice.FixedFolder, back.FolderChoice);
        Assert.Equal(@"C:\늘 보내는 곳", back.FixedFolder);
        string json = System.IO.File.ReadAllText(t.P("settings.json"));
        Assert.Contains("\"folderChoice\": \"FixedFolder\"", json);
        Assert.Contains("\"fixedFolder\": \"C:\\\\늘 보내는 곳\"", json);
        Assert.Equal(CompressFolderChoice.SourceFolder, new AppSettings().Compress.FolderChoice);
        Assert.Null(new AppSettings().Compress.FixedFolder);
    }

    /// <summary>계약: 압축 창의 처음 값 — 원본 하나면 그 이름(확장자 뗌)·그 폴더, 여럿이면 그 폴더 이름. 7Z 는 7z.dll 을 쓸 수 있을 때만 형식에 있다.</summary>
    [Fact]
    public void 압축_창에_넘기는_것()
    {
        using var c = New("dialogreq");
        string a = c.File("보고서.xlsx", "a");
        string b = c.File("견적.txt", "b");

        var one = c.Vm.BuildCompressionDialogRequest(new[] { a });
        Assert.Equal("보고서", one.SuggestedBaseName);
        Assert.Equal(c.Src, one.SuggestedFolder);
        Assert.True(one.AllSourcesAreFiles);
        Assert.Contains(CompressionFormat.SevenZip, one.AvailableFormats);
        Assert.Equal(new[] { CompressionFormat.Zip, CompressionFormat.SevenZip, CompressionFormat.Tar, CompressionFormat.GZip, CompressionFormat.BZip2 },
            one.AvailableFormats);

        var two = c.Vm.BuildCompressionDialogRequest(new[] { a, c.Dir("폴더") });
        Assert.Equal("원본", two.SuggestedBaseName);
        Assert.False(two.AllSourcesAreFiles);

        using (SevenZipLibrary.SimulateUnavailableForTests())
            Assert.DoesNotContain(CompressionFormat.SevenZip, c.Vm.BuildCompressionDialogRequest(new[] { b }).AvailableFormats);
    }

    // ───────────── W2-07 검증 지적 수정(2차) ─────────────

    /// <summary>
    /// 계약(W2-07 검증 지적 2차 — 회귀): TAR 파일('backup.tar')을 GZ·BZ2 로 압축하면 'backup.tar.gz'·'backup.tar.bz2'(날짜를 붙이면 'backup_20261013.tar.gz')가
    /// 만들어지고, 풀면 원래 TAR 파일과 같다. 예전엔 만들 파일 이름의 가장 긴 확장자('.tar.gz' — 풀기만 되는 압축된 TAR 처리기)로 처리기를 골라
    /// '압축된 TAR 만들기는 아직 지원하지 않아요'로 실패했다(예전 저장 창은 'backup.gz' 를 제안해 없던 일). 각각 따로도 같다.
    /// 다 만들고 시험하기도 통과한다 — 이름이 '.tar' 이지만 속은 TAR 가 아닌 파일도(시험기도 이름의 가장 긴 확장자가 아니라 만든 형식으로 시험).
    /// </summary>
    [Theory]
    [InlineData(CompressionFormat.GZip, false, DateNameFormat.None, true, "backup.tar.gz")]
    [InlineData(CompressionFormat.BZip2, false, DateNameFormat.None, true, "backup.tar.bz2")]
    [InlineData(CompressionFormat.GZip, false, DateNameFormat.YearMonthDay, true, "backup_20261013.tar.gz")]
    [InlineData(CompressionFormat.GZip, true, DateNameFormat.None, true, "backup.tar.gz")]
    [InlineData(CompressionFormat.BZip2, true, DateNameFormat.None, false, "backup.tar.bz2")]
    [InlineData(CompressionFormat.GZip, true, DateNameFormat.YearMonthDayDashed, false, "backup_2026-10-13.tar.gz")]
    public async Task TAR_파일을_GZ_BZ2로_압축할_수_있다(CompressionFormat format, bool each, DateNameFormat date, bool realTar, string expected)
    {
        using var c = New("targz" + (int)format + (each ? "e" : "s") + (realTar ? "t" : "f"));
        string tar = Path.Combine(c.Src, "backup.tar");
        if (realTar)
        {
            string folder = c.Dir("백업할 것");
            c.File(@"백업할 것\장부.txt", string.Concat(Enumerable.Range(0, 500).Select(i => $"줄 {i}\n")));
            await new TarArchiveService().CompressAsync(new[] { folder }, tar);
        }
        else
        {
            System.IO.File.WriteAllText(tar, string.Concat(Enumerable.Range(0, 300).Select(i => $"이름만 tar — 속은 그냥 글자 {i}\n")));
        }
        string tarSha = TreeAssert.Sha256(tar);
        var sources = new List<string> { tar };
        if (each) sources.Add(c.File("a.txt", "a"));
        c.Ui.CompressionRequestAnswer = Answer(c.Out, format, baseName: "backup.tar", each: each, date: date, test: true);

        await Compress(c, sources.ToArray());

        Assert.Empty(c.Ui.Errors);
        Assert.Empty(c.Ui.Warnings);
        string made = Path.Combine(c.Out, expected);
        Assert.True(System.IO.File.Exists(made), string.Join(", ", Directory.GetFiles(c.Out).Select(Path.GetFileName)));
        Assert.Equal(sources.Count, c.Services.CompressTester.Tested.Count);
        // 단일 스트림 처리기로 풀면 원래 TAR 파일 그대로
        IArchiveService single = format == CompressionFormat.GZip ? new GZipArchiveService() : new BZip2ArchiveService();
        string dest = c.Temp.Dir("풀어봄");
        await single.ExtractAsync(made, dest);
        var back = TreeAssert.Snapshot(dest);
        Assert.Equal(tarSha, Assert.Single(back).Value);
        Assert.EndsWith(".tar", Assert.Single(back).Key, StringComparison.Ordinal);
        if (each)
        {
            Assert.Equal("2개를 모두 압축했어요.", Assert.Single(c.Ui.Summaries).Headline);
            string other = CompressNaming.FileNameFor("a.txt", format, date, new DateTime(2026, 10, 13, 12, 0, 0, DateTimeKind.Local));
            Assert.True(System.IO.File.Exists(Path.Combine(c.Out, other)), other);
        }
        else
        {
            Assert.Contains(c.Ui.Infos, i => i.Contains("문제없어요", StringComparison.Ordinal));
        }
    }

    /// <summary>
    /// 계약(W2-07 검증 지적 2차): 만들기·원본 맞춰 보기·만든 압축 시험은 이름의 가장 긴 확장자가 아니라 만든 형식의 처리기로 — 'backup.tar.gz'(TAR 파일을 GZ 로)는 GZ 처리기.
    /// 열기·풀기(<see cref="ArchiveServiceLocator.ResolveByExtension"/>)는 예전 그대로 압축된 TAR 처리기(안의 내용을 바로 펼침).
    /// </summary>
    [Fact]
    public void 만든_압축은_만든_형식의_처리기로_시험한다()
    {
        var t = CreatedArchiveTester.Instance;
        Assert.IsType<GZipArchiveService>(t.ServiceFor(@"C:\x\backup.tar.gz"));
        Assert.IsType<BZip2ArchiveService>(t.ServiceFor(@"C:\x\backup_20261013.tar.bz2"));
        Assert.IsType<TarArchiveService>(t.ServiceFor(@"C:\x\a.tar"));
        Assert.IsType<ZipArchiveService>(t.ServiceFor(@"C:\x\자료.7z.zip"));
        Assert.IsType<SevenZipArchiveService>(t.ServiceFor(@"C:\x\a.7z"));
        Assert.IsType<SevenZipArchiveService>(t.ServiceFor(@"C:\x\a.7z.001"));   // 나눈 7z 의 여는 조각

        var locator = new ArchiveServiceLocator();
        Assert.IsType<CompressedTarArchiveService>(locator.ResolveByExtension(@"C:\x\backup.tar.gz"));
        Assert.IsType<GZipArchiveService>(locator.ForExtension(".gz"));
        Assert.IsType<BZip2ArchiveService>(locator.ForExtension(".BZ2"));
        Assert.Null(locator.ForExtension(".001"));
        Assert.Null(locator.ForExtension(null));
    }

    /// <summary>
    /// 계약(W2-07 검증 지적 2차): 각각 따로 + 시험이면 시험 단계 알림은 '만든 압축 파일을 시험하는 중 (2/3) — 이름' 이고 진행 창 막대는 움직이는 막대 —
    /// 압축이 끝나 100% 이던 막대가 시험하는 동안 0%·33%·66% 로 되돌아가지 않는다(진행 창의 막대 규칙 <see cref="ProgressDialog.BarValue"/> 로 확인).
    /// 원본 지우기를 켰을 때(원본을 살펴보는 중·맞춰 보는 중)도 같다.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task 각각_따로_시험_중에_진행_막대가_뒤로_가지_않는다(bool delete)
    {
        using var c = New("eachbar" + (delete ? "d" : ""));
        c.Services.CompressTester.Pass();
        var sources = new[] { "가", "나", "다" }
            .Select(n => c.File(n + ".txt", string.Concat(Enumerable.Range(0, 3000).Select(i => $"{n} 줄 {i}\n"))))
            .ToArray();
        c.Ui.ChoiceAnswers.Enqueue(1);   // 묻으면 '그대로 두기'
        c.Ui.CompressionRequestAnswer = Answer(c.Out, each: true, test: true, delete: delete);

        await Compress(c, sources);

        var reports = c.Ui.ProgressSessions[0].Reports.ToList();
        var testing = reports.Where(r => r.CurrentFile.StartsWith(CompressFlow.StageTesting, StringComparison.Ordinal)).ToList();
        Assert.Equal(new[] { "가.zip", "나.zip", "다.zip" }.Select((n, i) => $"{CompressFlow.StageTesting} ({i + 1}/3) — {n}"),
            testing.Select(r => r.CurrentFile));
        Assert.All(testing, r => Assert.Null(Iljip.Views.ProgressDialog.BarValue(r)));   // 움직이는 막대
        var bars = reports.Select(Iljip.Views.ProgressDialog.BarValue).Where(v => v is not null).Select(v => v!.Value).ToList();
        for (int i = 1; i < bars.Count; i++)
            Assert.True(bars[i] >= bars[i - 1] - 0.0001, $"막대가 뒤로 감: {bars[i - 1]:0.#}% → {bars[i]:0.#}% ({i}번째)");
        Assert.True(bars[^1] > 99.9, $"마지막 막대 {bars[^1]:0.#}%");
    }

    /// <summary>
    /// 계약(W2-07 검증 지적 2차): 원본을 훑는 일(압축하기 전 원본 재기·지우기 전 맞춰 보기·엔진의 원본 모으기)은 [취소]를 본다 —
    /// 폴더가 아주 커도 다 훑을 때까지 기다리지 않는다. 취소는 삼키지 않고 <see cref="OperationCanceledException"/>(원본 재기는 '못 잼'(null)으로 바꾸지 않음).
    /// </summary>
    [Fact]
    public void 원본_훑기는_취소를_본다()
    {
        using var t = new TempDir("scancancel");
        string folder = t.Dir("큰 폴더");
        for (int i = 0; i < 20; i++)
            File.WriteAllText(Path.Combine(t.Dir("큰 폴더", $"하위{i}"), $"{i}.txt"), "x");
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        Assert.ThrowsAny<OperationCanceledException>(() => SourceCollector.Collect(new[] { folder }, null, cts.Token));
        Assert.ThrowsAny<OperationCanceledException>(() => CompressFlow.TakeStamps(new[] { folder }, null, cts.Token));
        Assert.Equal(20, SourceCollector.Collect(new[] { folder }, null, CancellationToken.None).Files.Count);   // 취소가 없으면 그대로
        Assert.Equal(20, CompressFlow.TakeStamps(new[] { folder }, null, CancellationToken.None)!.Count);
    }

    /// <summary>
    /// 계약(W2-07 검증 지적 2차): 지우기 전 원본을 맞춰 보는 도중 [취소]하면 바로 멈추고(그 원본 폴더를 끝까지 훑지 않음) 묻지도 옮기지도 않는다 —
    /// '취소해서 원본은 지우지 않았어요.'. 예전엔 훑기가 취소를 보지 않아 맞춰 보기가 끝까지 돌고, 취소했는데도 '옮길까요?'를 물었다.
    /// </summary>
    [Fact]
    public async Task 맞춰_보는_도중_취소하면_묻지도_옮기지도_않는다()
    {
        using var t = new TempDir("checkcancel");
        string folder = t.Dir("원본", "거래처A");
        File.WriteAllText(Path.Combine(folder, "견적.txt"), "견적");
        File.WriteAllText(Path.Combine(t.Dir("원본", "거래처A", "하위"), "메모.txt"), "메모");
        string outDir = t.Dir("만든곳");
        var ui = new FakeUserInteraction();
        ui.ChoiceAnswers.Enqueue(0);
        var bin = new FakeRecycleBin();
        var session = new CancellableSession();
        bin.OnIsRecyclable = _ => session.Cts.Cancel();   // 맞춰 보기를 막 시작했을 때 [취소]
        var flow = new CompressFlow(new ArchiveServiceLocator(), ui, bin, new FakeCompressTester().Pass(), new FakeClock().SetLocal(2026, 10, 13));

        var result = await flow.RunAsync(new CompressionRequest
        {
            Sources = new[] { folder },
            OutputFolder = outDir,
            BaseName = "거래처A",
            Format = CompressionFormat.Zip,
            DeleteOriginalsAfter = true,
        }, session);

        Assert.Equal(1, result.SucceededCount);
        Assert.Empty(ui.ChoicePrompts);
        Assert.Empty(bin.Sent);
        Assert.Contains(CompressFlow.RecycleCancelledMessage, result.Warnings);
        Assert.True(File.Exists(Path.Combine(outDir, "거래처A.zip")));
    }

    /// <summary>
    /// 계약(W2-07 검증 지적 2차): 한 번에 보낸 원본 중 일부만 휴지통으로 옮겨지면(Windows 가 하나에서 실패) 옮겨진 것은 '원본 1개를 휴지통으로 옮겼어요.',
    /// 남은 것만 '원본 1개는 휴지통으로 옮기지 못해 그대로 두었어요: b.txt' — 예전엔 일부를 옮겼어도 '옮기지 못해 그대로 두었어요'만 알렸다.
    /// 하나도 못 옮겼으면 예전 문구 그대로.
    /// </summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task 휴지통으로_일부만_옮겨지면_옮긴_것과_남은_것을_따로_알린다(bool partly)
    {
        using var t = new TempDir("recyclepartial");
        string src = t.Dir("원본");
        string a = Path.Combine(src, "a.txt");
        string b = Path.Combine(src, "b.txt");
        File.WriteAllText(a, "a");
        File.WriteAllText(b, "b");
        var ui = new FakeUserInteraction();
        ui.ChoiceAnswers.Enqueue(0);
        var bin = new FakeRecycleBin { Result = new RecycleResult(false, "'b.txt' 을(를) 휴지통으로 옮기지 못했어요.") };
        if (partly) bin.OnSend = _ => File.Delete(a);   // a 만 옮겨진 것처럼(이 시험이 만든 임시 파일)
        var flow = new CompressFlow(new ArchiveServiceLocator(), ui, bin, new FakeCompressTester().Pass(), new FakeClock().SetLocal(2026, 10, 13));

        var result = await flow.RunAsync(new CompressionRequest
        {
            Sources = new[] { a, b },
            OutputFolder = t.Dir("만든곳"),
            BaseName = "묶음",
            Format = CompressionFormat.Zip,
            DeleteOriginalsAfter = true,
        }, ui.ShowProgress("압축 중..."));

        Assert.Equal(new[] { a, b }, bin.AllSent);
        if (partly)
        {
            Assert.Equal(new[] { a }, result.Recycled);
            Assert.Contains("원본 1개를 휴지통으로 옮겼어요.", result.Notes);
            var w = Assert.Single(result.Warnings);
            Assert.StartsWith("원본 1개는 휴지통으로 옮기지 못해 그대로 두었어요: b.txt", w);
            Assert.DoesNotContain("a.txt", w);
        }
        else
        {
            Assert.Empty(result.Recycled);
            Assert.DoesNotContain(result.Notes, n => n.Contains("휴지통으로 옮겼어요", StringComparison.Ordinal));
            Assert.StartsWith("원본을 휴지통으로 옮기지 못해 그대로 두었어요.", Assert.Single(result.Warnings));
        }
        Assert.True(File.Exists(b));
    }

    /// <summary>
    /// 계약(W2-07 검증 지적 2차): '다 만들고 시험하기'만 켠 채 시험 도중 [취소]하면 알린다 — '시험을 취소했어요(3개 중 1개만 시험했어요). 만든 압축 파일은 그대로 있어요.'
    /// ('모두 문제없어요'는 하지 않음). 각각 따로의 상태 줄은 '압축 완료: 3개 (시험은 취소)'. 하나로 만들 때도 알린다. 예전엔 아무 알림이 없었다.
    /// </summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task 시험만_켜고_시험을_취소하면_알린다(bool each)
    {
        using var c = New("testcancel" + (each ? "e" : "s"));
        c.Services.CompressTester.Pass();
        var sources = each
            ? new[] { c.File("가.txt", "a"), c.File("나.txt", "b"), c.File("다.txt", "c") }
            : new[] { c.File("보고서.txt", "내용") };
        c.Ui.CancelWhen = p => p.CurrentFile.StartsWith(each ? $"{CompressFlow.StageTesting} (2/3)" : CompressFlow.StageTesting, StringComparison.Ordinal);
        c.Ui.CompressionRequestAnswer = Answer(c.Out, each: each, test: true);

        await Compress(c, sources);

        Assert.Equal(sources.Length, Directory.GetFiles(c.Out, "*.zip").Length);   // 만든 압축은 그대로
        Assert.Empty(c.Ui.Errors);
        if (each)
        {
            Assert.Single(c.Services.CompressTester.Tested);
            var summary = Assert.Single(c.Ui.Summaries);
            Assert.Equal("3개를 모두 압축했어요.", summary.Headline);
            Assert.Contains(CompressFlow.TestCancelledMessage(3, 1), summary.Notes);
            Assert.Equal("시험을 취소했어요(3개 중 1개만 시험했어요). 만든 압축 파일은 그대로 있어요.", CompressFlow.TestCancelledMessage(3, 1));
            Assert.DoesNotContain(summary.Notes, n => n.Contains("문제없어요", StringComparison.Ordinal));
            Assert.Equal("압축 완료: 3개 (시험은 취소)", c.Vm.StatusText);
        }
        else
        {
            Assert.Empty(c.Services.CompressTester.Tested);
            var info = Assert.Single(c.Ui.Infos);
            Assert.Contains("시험을 취소했어요. 만든 압축 파일은 그대로 있어요.", info);
            Assert.DoesNotContain("문제없어요", info);
            Assert.Equal(Path.Combine(c.Out, "보고서.zip"), c.Vm.CurrentArchivePath);   // 예전처럼 결과를 열어 보임
        }
    }

    /// <summary>[취소]를 시험이 직접 누르는 진행 창 흉내.</summary>
    private sealed class CancellableSession : IProgressSession
    {
        public CancellationTokenSource Cts { get; } = new();
        public ProgressRecorder Recorder { get; } = new();
        public IProgress<ArchiveProgress> Progress => Recorder;
        public CancellationToken CancellationToken => Cts.Token;
        public void Close() { }
    }
}
