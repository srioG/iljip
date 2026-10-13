using Iljip.Models;
using Iljip.Services;
using Iljip.Tests.TestSupport;
using Iljip.ViewModels;

namespace Iljip.Tests.Contract;

/// <summary>
/// [W2-05 I2-6] 여러 압축을 한꺼번에 풀기(<see cref="BatchExtractor"/>): 진행 창 하나('2/5 · b.zip' + 전체 막대), 암호는 한 번 넣으면 다음 파일에 조용히 써 보고
/// 틀리면 그 파일만 '앞에서 쓴 암호가 맞지 않아요. b.zip 암호를 넣어 주세요.'로 묻는다(취소하면 그 파일만 건너뜀). 끝에 요약(성공 N·실패 M·이유, 날 문구는 '자세히').
/// 진행 창 [취소]·같은 이름 [취소]는 남은 압축을 손대지 않고 멈춘다. 들어오는 길: 탐색기에서 여러 개 골라 우클릭(파일마다 일집이 떠 파이프로 묶임),
/// 일집 열기 창에서 여러 개 고르기('모두 풀기'), 명령줄 '--extract a b …'. 화면은 가짜(<see cref="FakeUserInteraction"/>)·셸·휴지통도 가짜.
/// </summary>
public class BatchExtractTests
{
    private static Task Run(Func<Task> action, string what) => VmRunner.Run(action, what);

    /// <summary><paramref name="work"/> 에 픽스처를 <paramref name="name"/> 으로 복사.</summary>
    private static string Copy(string work, string fixtureId, string name)
    {
        string dst = Path.Combine(work, name);
        File.Copy(FixtureCatalog.Get(fixtureId).FullPath, dst);
        return dst;
    }

    private static string Garbage(string work, string name)
    {
        string p = Path.Combine(work, name);
        File.WriteAllBytes(p, Enumerable.Range(0, 5000).Select(i => (byte)(i * 31 + 7)).ToArray());
        return p;
    }

    /// <summary>같은 암호의 압축 셋을 우클릭 '"이름"\ 에 풀기'로: 암호는 처음 한 번만 묻고 모두 푼다. 진행 창 하나, 결과 폴더(같은 위)는 한 번만 열고 요약 창은 없음.</summary>
    [Fact]
    public async Task 같은_암호_압축_셋은_암호를_한번만_묻는다()
    {
        var fx = FixtureCatalog.Get("zip_aes256");
        using var tmp = new TempDir("bpw");
        string work = tmp.Dir("받은 파일");
        var arcs = new[] { "a.zip", "b.zip", "c.zip" }.Select(n => Copy(work, "zip_aes256", n)).ToArray();
        var ui = new FakeUserInteraction(fx.Password);
        var svc = new TestServices();
        var vm = new MainViewModel(ui, null, svc.Services);

        await Run(() => vm.ProcessShellCommandAsync(new[] { "--extract-to-folder" }.Concat(arcs).ToArray()), "여러 개 풀기");

        var p = Assert.Single(ui.PasswordPrompts);
        Assert.Equal(("a.zip", PasswordPromptReason.Required), (p.ArchiveName, p.Reason));
        foreach (var n in new[] { "a", "b", "c" })
            TreeAssert.ExtractedTreeEquals(Path.Combine(work, n), fx.Expected, context: n);
        Assert.Empty(ui.Errors);
        Assert.Empty(ui.Summaries);   // 모두 풀었고 폴더를 열었으면 요약 창 없음
        Assert.Equal(new[] { (work, false) }, svc.Shell.OpenedFolders);
        var session = Assert.Single(ui.ProgressSessions);
        Assert.Equal("압축 해제 완료: 3개", vm.StatusText);

        // 진행 창 하나에 '2/3 · b.zip' 과 전체 막대(앞서 끝난 수 + 지금 압축 진행)
        var reports = session.Reports.ToList();
        Assert.Contains(reports, r => r.BatchIndex == 2 && r.BatchCount == 3 && r.BatchName == "b.zip");
        Assert.All(reports.Where(r => r.BatchIndex == 3), r => Assert.InRange(r.OverallPercentage, 200.0 / 3 - 0.001, 100));
        Assert.All(reports.Where(r => r.BatchIndex == 1), r => Assert.InRange(r.OverallPercentage, 0, 100.0 / 3 + 0.001));
    }

    /// <summary>
    /// 암호가 다른 파일이 섞이면 그 파일에서만 '앞에서 쓴 암호가 맞지 않아요. b.zip 암호를 넣어 주세요.'로 묻는다.
    /// 그 뒤 원래 암호 파일(c.zip)은 앞에서 맞은 암호들을 조용히 써 보아 다시 묻지 않는다.
    /// </summary>
    [Fact]
    public async Task 암호가_다른_파일에서만_다시_묻는다()
    {
        var fx = FixtureCatalog.Get("zip_aes256");
        using var tmp = new TempDir("bpw2");
        string work = tmp.Dir("받은 파일");
        string a = Copy(work, "zip_aes256", "a.zip");
        const string other = "다른암호-5678";
        string src = SampleTree.Create(tmp.Dir("원본"));
        string b = Path.Combine(work, "b.zip");
        await new ZipArchiveService().CompressAsync(new[] { src }, b, new CompressionOptions { Password = other });
        string c = Copy(work, "zip_aes256", "c.zip");
        var ui = new FakeUserInteraction(fx.Password, other);
        var svc = new TestServices();

        await Run(() => new MainViewModel(ui, null, svc.Services).ProcessShellCommandAsync(new[] { "--extract-to-folder", a, b, c }), "여러 개 풀기");

        Assert.Equal(2, ui.PasswordPrompts.Count);
        Assert.Equal(("a.zip", PasswordPromptReason.Required), (ui.PasswordPrompts[0].ArchiveName, ui.PasswordPrompts[0].Reason));
        Assert.Equal(("b.zip", PasswordPromptReason.Wrong, BatchExtractor.ReusedPasswordWrongMessage("b.zip")), ui.PasswordPrompts[1]);
        Assert.Equal("앞에서 쓴 암호가 맞지 않아요. b.zip 암호를 넣어 주세요.", ui.PasswordPrompts[1].Message);
        TreeAssert.ExtractedTreeEquals(Path.Combine(work, "a"), fx.Expected, context: "a");
        TreeAssert.ExtractedTreeEquals(Path.Combine(work, "b"), SampleTree.ExpectedOf(src), context: "b");
        TreeAssert.ExtractedTreeEquals(Path.Combine(work, "c"), fx.Expected, context: "c");
        Assert.Empty(ui.Errors);
        Assert.Empty(ui.Summaries);
    }

    /// <summary>암호 창을 취소하면 그 파일만 건너뛰고(새 폴더 남기지 않음) 나머지는 푼다. 요약에 '암호를 넣지 않아 건너뛰었어요.'</summary>
    [Fact]
    public async Task 암호_창을_취소하면_그_파일만_건너뛴다()
    {
        var fx = FixtureCatalog.Get("zip_default");
        using var tmp = new TempDir("bpwc");
        string work = tmp.Dir("받은 파일");
        string a = Copy(work, "zip_default", "a.zip");
        string b = Copy(work, "zip_aes256", "b.zip");
        string c = Copy(work, "zip_default", "c.zip");
        var ui = new FakeUserInteraction();   // 암호 창 = 취소
        var svc = new TestServices();

        await Run(() => new MainViewModel(ui, null, svc.Services).ProcessShellCommandAsync(new[] { "--extract-to-folder", a, b, c }), "여러 개 풀기");

        Assert.Single(ui.PasswordPrompts);
        TreeAssert.ExtractedTreeEquals(Path.Combine(work, "a"), fx.Expected, context: "a");
        TreeAssert.ExtractedTreeEquals(Path.Combine(work, "c"), fx.Expected, context: "c");
        Assert.False(Directory.Exists(Path.Combine(work, "b")), "건너뛴 파일의 새 폴더가 남음");
        var s = Assert.Single(ui.Summaries);
        Assert.Equal((3, 2, false), (s.Total, s.Succeeded, s.Cancelled));
        var f = Assert.Single(s.Failures);
        Assert.Equal(("b.zip", BatchExtractor.PasswordSkippedReason), (f.Name, f.Reason));
        Assert.Null(f.Detail);
        Assert.Empty(ui.Errors);
    }

    /// <summary>
    /// 다섯 개 중 하나가 손상이면: 나머지 넷은 풀고, 끝에 요약 하나 '5개 중 4개를 풀었어요.' + 못 푼 이름·쉬운 한국어 이유.
    /// 날 엔진 문구(영어)는 첫 화면에 없고 '자세히'(Detail)에만. 오류 창을 따로 띄우지 않는다.
    /// </summary>
    [Fact]
    public async Task 다섯_개_중_하나가_손상이면_요약에_이름과_이유가_나온다()
    {
        var fx = FixtureCatalog.Get("zip_default");
        using var tmp = new TempDir("bbad");
        string work = tmp.Dir("받은 파일");
        var arcs = new List<string>
        {
            Copy(work, "zip_default", "1.zip"),
            Copy(work, "zip_default", "2.zip"),
            Garbage(work, "망가진.zip"),
            Copy(work, "zip_default", "4.zip"),
            Copy(work, "zip_default", "5.zip"),
        };
        var ui = new FakeUserInteraction();
        var svc = new TestServices();
        var vm = new MainViewModel(ui, null, svc.Services);

        await Run(() => vm.ProcessShellCommandAsync(new[] { "--extract-to-folder" }.Concat(arcs).ToArray()), "여러 개 풀기");

        foreach (var n in new[] { "1", "2", "4", "5" })
            TreeAssert.ExtractedTreeEquals(Path.Combine(work, n), fx.Expected, context: n);
        Assert.False(Directory.Exists(Path.Combine(work, "망가진")));
        Assert.Empty(ui.Errors);
        var s = Assert.Single(ui.Summaries);
        Assert.Equal((5, 4, false), (s.Total, s.Succeeded, s.Cancelled));
        Assert.Equal("5개 중 4개를 풀었어요.", s.Headline);
        var f = Assert.Single(s.Failures);
        Assert.Equal("망가진.zip", f.Name);
        Assert.Matches("[가-힣]", f.Reason);
        Assert.DoesNotMatch("[A-Za-z]{3,}", f.Reason);   // 이유에 영어 엔진 문구 없음
        Assert.False(string.IsNullOrEmpty(f.Detail), "날 오류가 '자세히'에 없음");
        string display = s.ToDisplayText();
        Assert.Contains("망가진.zip", display);
        Assert.DoesNotContain(f.Detail!, display);
        Assert.Contains("[망가진.zip]", s.DetailText());
        Assert.Equal("오류 발생", vm.StatusText);
        Assert.Equal(new[] { (work, false) }, svc.Shell.OpenedFolders);   // 푼 것은 같은 위 폴더 — 한 번 연다
    }

    /// <summary>
    /// 도중에 취소(두 번째 압축의 같은 이름 창 [취소])하면 남은 압축은 손대지 않고 멈춘다: 첫 압축은 그대로, 두 번째가 만든 것은 되돌리고,
    /// 3~5번은 시작하지 않는다(요약에 '취소해서 손대지 않은 파일 3개'). 취소했으면 '풀고 나서 지우기'가 켜져 있어도 하나도 지우지 않는다.
    /// </summary>
    [Fact]
    public async Task 도중에_취소하면_남은_압축은_손대지_않는다()
    {
        var fx = FixtureCatalog.Get("zip_default");
        using var tmp = new TempDir("bcan");
        string work = tmp.Dir("받은 파일");
        var arcs = Enumerable.Range(1, 5).Select(i => Copy(work, "zip_default", $"{i}.zip")).ToList();
        var ui = new FakeUserInteraction();
        ui.CollisionAnswers.Enqueue(new(CollisionChoice.Cancel, false));
        var svc = TestServices.With(e => e.DeleteArchiveAfter = true);
        var vm = new MainViewModel(ui, null, svc.Services);

        await Run(() => vm.ProcessShellCommandAsync(new[] { "--extract-here" }.Concat(arcs).ToArray()), "여러 개 풀기");

        Assert.Single(ui.CollisionPrompts);
        // 첫 압축이 푼 것만 그대로(두 번째가 만든 '(1)' 등은 없음) + 압축 파일 다섯
        var expectedFiles = fx.Expected.Where(e => !e.IsDir).Select(e => e.Path)
            .Concat(arcs.Select(a => Path.GetFileName(a))).OrderBy(x => x, StringComparer.Ordinal).ToList();
        Assert.Equal(expectedFiles, TreeAssert.Files(work).OrderBy(x => x, StringComparer.Ordinal).ToList());
        var s = Assert.Single(ui.Summaries);
        Assert.True(s.Cancelled);
        Assert.Equal((5, 1), (s.Total, s.Succeeded));
        Assert.Equal(new[] { "3.zip", "4.zip", "5.zip" }, s.NotStarted);
        Assert.Equal("5개 중 1개를 풀고 멈췄어요.", s.Headline);
        Assert.Contains("취소해서 손대지 않은 파일 3개", s.ToDisplayText());
        Assert.Empty(svc.RecycleBin.Sent);   // 취소한 묶음은 하나도 지우지 않음
        Assert.Empty(ui.Errors);
        Assert.Equal("압축 해제 취소됨", vm.StatusText);
    }

    /// <summary>진행 창 [취소]를 누르면(풀던 압축은 서비스 취소 규칙대로) 남은 압축은 시작하지 않는다.</summary>
    [Fact]
    public async Task 진행_창_취소면_남은_압축을_시작하지_않는다()
    {
        using var tmp = new TempDir("bcan2");
        string work = tmp.Dir("받은 파일");
        var arcs = Enumerable.Range(1, 3).Select(i => Copy(work, "zip_default", $"{i}.zip")).ToList();
        var ui = new FakeUserInteraction { CancelOnFirstBytes = true };
        var svc = new TestServices();

        await Run(() => new MainViewModel(ui, null, svc.Services).ProcessShellCommandAsync(new[] { "--extract-to-folder" }.Concat(arcs).ToArray()), "여러 개 풀기");

        var s = Assert.Single(ui.Summaries);
        Assert.True(s.Cancelled);
        Assert.Equal(new[] { "2.zip", "3.zip" }, s.NotStarted);
        Assert.False(Directory.Exists(Path.Combine(work, "2")));
        Assert.False(Directory.Exists(Path.Combine(work, "3")));
        Assert.Empty(svc.Shell.OpenedFolders);   // 취소했으면 폴더를 열지 않음
        Assert.Empty(ui.Errors);
    }

    /// <summary>
    /// 탐색기에서 여러 개를 골라 우클릭하면 파일마다 일집이 떠 명령이 따로 들어온다 — 돌던 같은 동사 묶음에 붙어 진행 창 하나로 끝난다.
    /// (같은 이름 창이 떠 있는 동안 두 번째 명령이 들어오는 것으로 흉내 — 들어온 명령은 바로 돌아가고, 묶음이 이어서 푼다)
    /// </summary>
    [Fact]
    public async Task 따로_들어온_우클릭_명령이_돌던_묶음에_붙는다()
    {
        using var tmp = new TempDir("bjoin");
        string work = tmp.Dir("받은 파일");
        string a = Copy(work, "zip_default", "a.zip");
        var egg = SyntheticArchives.Build("egg", tmp.Dir("만든 것"));
        string b = Path.Combine(work, "b.egg");
        File.Copy(egg.Path, b);
        string mine = Path.Combine(work, "일집_샘플", "견적서_2026.txt");
        Directory.CreateDirectory(Path.GetDirectoryName(mine)!);
        File.WriteAllText(mine, "사용자 파일");

        var ui = new FakeUserInteraction();
        ui.CollisionAnswers.Enqueue(new(CollisionChoice.Skip, true));
        var svc = new TestServices();
        var vm = new MainViewModel(ui, null, svc.Services);
        Task? joined = null;
        ui.OnCollisionPrompt = _ => joined = vm.ProcessShellCommandAsync(new[] { "--extract-here", b });

        await Run(() => vm.ProcessShellCommandAsync(new[] { "--extract-here", a }), "여러 개 풀기");

        Assert.NotNull(joined);
        Assert.True(joined!.IsCompletedSuccessfully, "붙은 명령이 따로 돌았음(묶음에 붙지 않음)");
        Assert.Single(ui.ProgressSessions);
        Assert.Equal("사용자 파일", File.ReadAllText(mine));
        foreach (var e in egg.Expected.Where(e => !e.IsDir))   // 붙은 b.egg 도 같은 진행 창에서 풀림
            Assert.Equal(e.Sha256, TreeAssert.Sha256(Path.Combine(work, e.Path)));
        var s = Assert.Single(ui.Summaries);   // '여기에 풀기'는 폴더를 열지 않아 요약으로 알림
        Assert.Equal((2, 2), (s.Total, s.Succeeded));
        Assert.Equal("2개를 모두 풀었어요.", s.Headline);
    }

    /// <summary>일집 열기 창에서 압축을 여러 개 고르면 '모두 풀까요?' → 풀 폴더 → 그 안의 '이름' 폴더마다. 메인 창 흐름이라 폴더는 열지 않고 요약으로 알린다.</summary>
    [Fact]
    public async Task 열기_창에서_여러_개를_고르면_모두_풀기()
    {
        var zip = FixtureCatalog.Get("zip_default");
        var sz = FixtureCatalog.Get("7z_default");
        using var tmp = new TempDir("bpick");
        string work = tmp.Dir("받은 파일");
        string a = Copy(work, "zip_default", "가.zip");
        string b = Copy(work, "7z_default", "나.7z");
        string outDir = tmp.Dir("풀 곳");
        var ui = new FakeUserInteraction { FilesToPick = new[] { a, b }, ChooseByLabel = "모두 풀기", FolderToPick = outDir };
        var svc = new TestServices();
        var vm = new MainViewModel(ui, null, svc.Services);

        await Run(() => vm.OpenCommand.ExecuteAsync(null), "열기 → 모두 풀기");

        var c = Assert.Single(ui.ChoicePrompts);
        Assert.Equal(new[] { "모두 풀기", "취소" }, c.Choices);
        TreeAssert.ExtractedTreeEquals(Path.Combine(outDir, "가"), zip.Expected, context: "가.zip");
        TreeAssert.ExtractedTreeEquals(Path.Combine(outDir, "나"), sz.Expected, context: "나.7z");
        Assert.Empty(svc.Shell.OpenedFolders);
        var s = Assert.Single(ui.Summaries);
        Assert.Equal("2개를 모두 풀었어요.", s.Headline);
        Assert.Null(vm.CurrentArchivePath);
    }

    /// <summary>'모두 풀까요?'에서 [취소]면 아무것도 하지 않는다.</summary>
    [Fact]
    public async Task 모두_풀기를_취소하면_아무것도_안_한다()
    {
        using var tmp = new TempDir("bpick0");
        string work = tmp.Dir("받은 파일");
        string a = Copy(work, "zip_default", "가.zip");
        string b = Copy(work, "7z_default", "나.7z");
        string outDir = tmp.Dir("풀 곳");
        var ui = new FakeUserInteraction { FilesToPick = new[] { a, b }, FolderToPick = outDir };
        ui.ChoiceAnswers.Enqueue(1);

        await Run(() => new MainViewModel(ui, null, new TestServices().Services).OpenCommand.ExecuteAsync(null), "열기 → 취소");

        Assert.Empty(Directory.EnumerateFileSystemEntries(outDir));
        Assert.Empty(ui.ProgressSessions);
        Assert.Empty(ui.Summaries);
    }

    /// <summary>명령줄 '--extract a b'(셸 '압축 풀기...'를 여러 개에): 폴더를 한 번 고르고 그 안의 '이름' 폴더마다 푼다.</summary>
    [Fact]
    public async Task 명령줄_extract_여러_개는_폴더를_한번_고른다()
    {
        var zip = FixtureCatalog.Get("zip_default");
        using var tmp = new TempDir("bcli");
        string work = tmp.Dir("받은 파일");
        string a = Copy(work, "zip_default", "a.zip");
        string b = Copy(work, "zip_default", "b.zip");
        string outDir = tmp.Dir("풀 곳");
        var ui = new FakeUserInteraction { FolderToPick = outDir };

        await Run(() => new MainViewModel(ui, null, new TestServices().Services).ProcessCommandLineAsync(new[] { "--extract", a, b }), "--extract a b");

        TreeAssert.ExtractedTreeEquals(Path.Combine(outDir, "a"), zip.Expected, context: "a");
        TreeAssert.ExtractedTreeEquals(Path.Combine(outDir, "b"), zip.Expected, context: "b");
        Assert.Single(ui.ProgressSessions);
        Assert.Empty(ui.Errors);
    }

    /// <summary>전체 막대 값: 여러 개면 (앞서 끝난 수 + 지금 진행) / 전체, 하나면 그 압축의 진행률 그대로.</summary>
    [Fact]
    public void 전체_막대는_앞서_끝난_수와_지금_진행으로_정한다()
    {
        Assert.Equal(50, new ArchiveProgress { BytesProcessed = 5, TotalBytes = 10 }.OverallPercentage, 3);
        Assert.Equal(50, new ArchiveProgress { BytesProcessed = 5, TotalBytes = 10, BatchIndex = 1, BatchCount = 1 }.OverallPercentage, 3);
        Assert.Equal(30, new ArchiveProgress { BytesProcessed = 5, TotalBytes = 10, BatchIndex = 2, BatchCount = 5 }.OverallPercentage, 3);
        Assert.Equal(100, new ArchiveProgress { BytesProcessed = 10, TotalBytes = 10, BatchIndex = 5, BatchCount = 5 }.OverallPercentage, 3);
        Assert.Equal(40, new ArchiveProgress { TotalBytes = 0, BatchIndex = 3, BatchCount = 5 }.OverallPercentage, 3);
    }
}
