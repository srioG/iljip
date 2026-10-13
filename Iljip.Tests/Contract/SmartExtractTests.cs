using Iljip.Models;
using Iljip.Services;
using Iljip.Tests.TestSupport;
using Iljip.ViewModels;

namespace Iljip.Tests.Contract;

/// <summary>
/// [W2-12 I1-2] 알아서 풀기(탐색기 우클릭 '알아서 풀기'·'각각 알아서 풀기' = '--extract-smart'): 압축 안의 맨 위가 하나(폴더 하나·파일 하나)면
/// 압축 파일이 있는 폴더에 바로(폴더가 두 겹이 되지 않게), 여럿이면 '이름' 폴더(있으면 '이름 (2)'). 같은 이름은 W2-05 처리기, 암호는 여러 개 풀기와 같게.
/// 메인 창 없이 진행 창만, 끝나면(설정 extract.openFolderAfter) 맨 위 폴더 또는 '이름' 폴더를 연다 — 바로 푼 파일 하나는 이미 보던 폴더라 열지 않음.
/// 화면·셸은 가짜(<see cref="FakeUserInteraction"/>·<see cref="FakeShellLauncher"/>).
/// </summary>
public class SmartExtractTests
{
    private static Task Run(Func<Task> action, string what) => VmRunner.Run(action, what);

    private static string Copy(string work, string fixtureId, string name)
    {
        string dst = Path.Combine(work, name);
        File.Copy(FixtureCatalog.Get(fixtureId).FullPath, dst);
        return dst;
    }

    /// <summary>기대 목록 + 그 자리에 그대로 있어야 할 압축 파일 자신.</summary>
    private static IEnumerable<ExpectedEntry> WithArchive(IEnumerable<ExpectedEntry> expected, params string[] archives) =>
        expected.Concat(archives.Select(a => new ExpectedEntry(Path.GetFileName(a), new FileInfo(a).Length, TreeAssert.Sha256(a), false)));

    private static IReadOnlyList<ArchiveEntry> Entries(params string[] paths) =>
        paths.Select(p => new ArchiveEntry { Path = p.TrimEnd('/'), IsDirectory = p.EndsWith('/') }).ToList();

    // ───────────── 계획(순수 함수) ─────────────

    [Fact]
    public void 맨_위가_하나면_바로_여럿이면_이름_폴더()
    {
        const string arc = @"C:\받은 파일\보고서.zip";
        Assert.Equal(new SmartExtractPlan(SmartPlanKind.Here, "보고서", true), SmartExtractPlanner.Plan(Entries("보고서/", "보고서/a.txt", "보고서/하위/b.txt"), arc));
        Assert.Equal(new SmartExtractPlan(SmartPlanKind.Here, "자료", true), SmartExtractPlanner.Plan(Entries("자료/a.txt"), arc));   // 폴더 항목 없이도 폴더
        Assert.Equal(new SmartExtractPlan(SmartPlanKind.Here, "견적서.xlsx", false), SmartExtractPlanner.Plan(Entries("견적서.xlsx"), arc));
        Assert.Equal(new SmartExtractPlan(SmartPlanKind.Here, "A", true), SmartExtractPlanner.Plan(Entries("A/x.txt", "a/y.txt", "./A/z.txt"), arc));   // 대소문자·'./' 무시
        Assert.Equal(new SmartExtractPlan(SmartPlanKind.Here, "빈폴더", true), SmartExtractPlanner.Plan(Entries("빈폴더/"), arc));
        Assert.Equal(new SmartExtractPlan(SmartPlanKind.Here, "폴더", true), SmartExtractPlanner.Plan(Entries(@"폴더\윈도.txt"), arc));   // 역슬래시 구분도

        Assert.Equal(SmartPlanKind.NamedFolder, SmartExtractPlanner.Plan(Entries("a.txt", "b.txt"), arc).Kind);
        Assert.Equal(SmartPlanKind.NamedFolder, SmartExtractPlanner.Plan(Entries("보고서/a.txt", "readme.txt"), arc).Kind);
        Assert.Equal(SmartPlanKind.NamedFolder, SmartExtractPlanner.Plan(Entries(), arc).Kind);                           // 빈 압축
        Assert.Equal(SmartPlanKind.NamedFolder, SmartExtractPlanner.Plan(Entries("보고서.zip"), arc).Kind);               // 압축 파일 자신을 덮게 됨
        Assert.Equal(SmartPlanKind.NamedFolder, SmartExtractPlanner.Plan(Entries("../밖.txt"), arc).Kind);                // '..' 는 바로 풀지 않음
    }

    // ───────────── 우클릭 흐름(진짜 압축·가짜 화면) ─────────────

    /// <summary>
    /// 맨 위가 폴더 하나('일집_샘플\…')인 zip: '받은 파일\일집_샘플\…' 로 바로(‘받은 파일\zip_default\일집_샘플\…’ 두 겹이 아님).
    /// 진행 창 하나, 오류·요약 없음, 그 맨 위 폴더를 한 번 연다. 메인 창 없이(ShowMainWindow false).
    /// </summary>
    [Fact]
    public async Task 맨_위가_폴더_하나면_두_겹이_되지_않는다()
    {
        var fx = FixtureCatalog.Get("zip_default");
        using var tmp = new TempDir("smart1");
        string work = tmp.Dir("받은 파일");
        string arc = Copy(work, "zip_default", "zip_default.zip");
        var ui = new FakeUserInteraction();
        var svc = new TestServices();
        var vm = new MainViewModel(ui, null, svc.Services);
        Assert.False(CommandLineParser.Parse(new[] { "--extract-smart", arc }).ShowMainWindow);

        await Run(() => vm.ProcessShellCommandAsync(new[] { "--extract-smart", arc }), "알아서 풀기");

        TreeAssert.ExtractedTreeEquals(work, WithArchive(fx.Expected, arc), context: "알아서 풀기");
        Assert.False(Directory.Exists(Path.Combine(work, "zip_default")));
        Assert.Equal(new[] { (Path.Combine(work, "일집_샘플"), false) }, svc.Shell.OpenedFolders);
        Assert.Single(ui.ProgressSessions);
        Assert.Empty(ui.Errors);
        Assert.Empty(ui.Summaries);
        Assert.Empty(ui.CollisionPrompts);
        Assert.Equal($"압축 해제 완료: {work}", vm.StatusText);
    }

    /// <summary>맨 위가 여럿인 zip: '이름' 폴더에(있으면 '이름 (2)' — 기존 폴더와 섞지 않음), 그 폴더를 연다.</summary>
    [Fact]
    public async Task 맨_위가_여럿이면_이름_폴더에_푼다()
    {
        var fx = FixtureCatalog.Get("zip_storeroot_no");
        using var tmp = new TempDir("smart2");
        string work = tmp.Dir("받은 파일");
        string arc = Copy(work, "zip_storeroot_no", "자료.zip");
        Directory.CreateDirectory(Path.Combine(work, "자료"));
        File.WriteAllText(Path.Combine(work, "자료", "원래 있던 것.txt"), "그대로");
        var ui = new FakeUserInteraction();
        var svc = new TestServices();

        await Run(() => new MainViewModel(ui, null, svc.Services).ProcessShellCommandAsync(new[] { "--extract-smart", arc }), "알아서 풀기");

        TreeAssert.ExtractedTreeEquals(Path.Combine(work, "자료 (2)"), fx.Expected, context: "자료 (2)");
        Assert.Equal(new[] { "원래 있던 것.txt" }, TreeAssert.Files(Path.Combine(work, "자료")));
        Assert.Equal(new[] { (Path.Combine(work, "자료 (2)"), false) }, svc.Shell.OpenedFolders);
        Assert.Empty(ui.Errors);
        Assert.Empty(ui.CollisionPrompts);
    }

    /// <summary>파일 하나뿐인 압축(.gz): 압축 파일 옆에 그 파일 하나 — 이미 보던 폴더라 탐색기를 열지 않는다('여기에 풀기'와 같음).</summary>
    [Fact]
    public async Task 파일_하나면_옆에_풀고_폴더는_열지_않는다()
    {
        var fx = FixtureCatalog.Get("gz_single");
        using var tmp = new TempDir("smart3");
        string work = tmp.Dir("받은 파일");
        string arc = Copy(work, "gz_single", "견적서_2026.txt.gz");
        var ui = new FakeUserInteraction();
        var svc = new TestServices();

        await Run(() => new MainViewModel(ui, null, svc.Services).ProcessShellCommandAsync(new[] { "--extract-smart", arc }), "알아서 풀기");

        TreeAssert.ExtractedTreeEquals(work, WithArchive(fx.Expected, arc), context: "gz");
        Assert.Empty(svc.Shell.OpenedFolders);
        Assert.Empty(ui.Errors);
        Assert.Empty(ui.Summaries);
    }

    /// <summary>
    /// 바로 풀 때 맨 위 폴더가 이미 있으면: 폴더는 합치고 겹치는 파일만 같은 이름 창으로 묻는다(W2-05 처리기 — [건너뛰기]면 있던 파일 그대로).
    /// </summary>
    [Fact]
    public async Task 바로_풀_때_같은_이름은_묻는다()
    {
        var fx = FixtureCatalog.Get("zip_default");
        var first = fx.ExpectedFiles.OrderBy(e => e.Path, StringComparer.Ordinal).First();
        using var tmp = new TempDir("smart4");
        string work = tmp.Dir("받은 파일");
        string arc = Copy(work, "zip_default", "zip_default.zip");
        string existing = Path.Combine(work, first.Path.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(existing)!);
        File.WriteAllText(existing, "원래 있던 내용");
        var ui = new FakeUserInteraction();
        ui.CollisionAnswers.Enqueue(new(CollisionChoice.Skip, false));
        var svc = new TestServices();

        await Run(() => new MainViewModel(ui, null, svc.Services).ProcessShellCommandAsync(new[] { "--extract-smart", arc }), "알아서 풀기");

        var q = Assert.Single(ui.CollisionPrompts);
        Assert.Equal(existing, q.TargetPath);
        Assert.Equal("원래 있던 내용", File.ReadAllText(existing));
        foreach (var e in fx.ExpectedFiles.Where(e => e.Path != first.Path))
            Assert.Equal(e.Sha256, TreeAssert.Sha256(Path.Combine(work, e.Path.Replace('/', Path.DirectorySeparatorChar))));
        Assert.False(Directory.Exists(Path.Combine(work, "zip_default")));
        Assert.Empty(ui.Errors);
    }

    /// <summary>
    /// 목록까지 잠긴 7z(헤더 암호): 목록을 보려면 암호가 필요해 먼저 한 번 묻고(목록 잠김 이유), 그 암호로 맨 위를 보고 바로 푼다. 다시 묻지 않는다.
    /// </summary>
    [Fact]
    public async Task 목록이_잠긴_7z는_한_번_묻고_바로_푼다()
    {
        var fx = FixtureCatalog.Get("7z_header_encrypted");
        using var tmp = new TempDir("smart5");
        string work = tmp.Dir("받은 파일");
        string arc = Copy(work, "7z_header_encrypted", "잠긴.7z");
        var ui = new FakeUserInteraction(fx.Password);
        var svc = new TestServices();

        await Run(() => new MainViewModel(ui, null, svc.Services).ProcessShellCommandAsync(new[] { "--extract-smart", arc }), "알아서 풀기");

        var p = Assert.Single(ui.PasswordPrompts);
        Assert.Equal(("잠긴.7z", PasswordPromptReason.ListLocked), (p.ArchiveName, p.Reason));
        TreeAssert.ExtractedTreeEquals(work, WithArchive(fx.Expected, arc), context: "헤더 암호 7z");
        Assert.Equal(new[] { (Path.Combine(work, "일집_샘플"), false) }, svc.Shell.OpenedFolders);
        Assert.Empty(ui.Errors);
    }

    /// <summary>암호 창을 취소하면 아무것도 만들지 않는다(풀 곳을 정하기 전 — 빈 '이름' 폴더도 남지 않음).</summary>
    [Fact]
    public async Task 암호_창을_취소하면_아무것도_남지_않는다()
    {
        using var tmp = new TempDir("smart6");
        string work = tmp.Dir("받은 파일");
        string arc = Copy(work, "7z_header_encrypted", "잠긴.7z");
        var ui = new FakeUserInteraction();   // 암호 창 = 취소
        var svc = new TestServices();

        await Run(() => new MainViewModel(ui, null, svc.Services).ProcessShellCommandAsync(new[] { "--extract-smart", arc }), "알아서 풀기");

        Assert.Single(ui.PasswordPrompts);
        Assert.Equal(new[] { "잠긴.7z" }, TreeAssert.Files(work));
        Assert.Equal(new[] { "잠긴.7z" }, Directory.EnumerateFileSystemEntries(work).Select(Path.GetFileName).ToArray());
        Assert.Empty(svc.Shell.OpenedFolders);
        Assert.Empty(ui.Errors);
    }

    /// <summary>
    /// 여러 개를 골라 '각각 알아서 풀기': 압축마다 따로 정한다(폴더 하나 → 바로, 여럿 → '이름' 폴더, 파일 하나 → 바로). 진행 창 하나.
    /// 연 폴더는 같은 위라 한 번(그 위 폴더 — 여러 개 풀기와 같은 규칙).
    /// </summary>
    [Fact]
    public async Task 여러_개는_압축마다_따로_정한다()
    {
        using var tmp = new TempDir("smart7");
        string work = tmp.Dir("받은 파일");
        string one = Copy(work, "zip_default", "하나.zip");
        string many = Copy(work, "zip_storeroot_no", "여럿.zip");
        string single = Copy(work, "gz_single", "견적서_2026.txt.gz");
        var ui = new FakeUserInteraction();
        var svc = new TestServices();
        var vm = new MainViewModel(ui, null, svc.Services);

        await Run(() => vm.ProcessShellCommandAsync(new[] { "--extract-smart", one, many, single }), "각각 알아서 풀기");

        TreeAssert.ExtractedTreeEquals(Path.Combine(work, "일집_샘플"), FixtureCatalog.Get("zip_default").Expected
            .Where(e => e.Path.StartsWith("일집_샘플/", StringComparison.Ordinal))
            .Select(e => e with { Path = e.Path["일집_샘플/".Length..] }), context: "하나.zip");
        TreeAssert.ExtractedTreeEquals(Path.Combine(work, "여럿"), FixtureCatalog.Get("zip_storeroot_no").Expected, context: "여럿.zip");
        Assert.True(File.Exists(Path.Combine(work, "견적서_2026.txt")));
        Assert.False(Directory.Exists(Path.Combine(work, "하나")));
        Assert.Single(ui.ProgressSessions);
        Assert.Equal(new[] { (work, false) }, svc.Shell.OpenedFolders);
        Assert.Empty(ui.Errors);
        Assert.Empty(ui.Summaries);   // 모두 풀었고 폴더를 열었음
        Assert.Equal("압축 해제 완료: 3개", vm.StatusText);
    }

    /// <summary>관리자 권한으로 다시 시도·새 창이 같은 명령을 만들 수 있게: 알아서 풀기 규칙 → '--extract-smart 압축…'. 해석 왕복도 같다.</summary>
    [Fact]
    public void 다시_할_명령은_알아서_풀기다()
    {
        var archives = new[] { @"C:\a\x.zip", @"C:\a\y.7z" };
        var retry = MainViewModel.ElevatedRetryFor(ExtractTargetRule.Smart, archives)!;
        Assert.Equal(CommandVerb.ExtractSmart, retry.Verb);
        Assert.Equal(archives, retry.Archives);
        Assert.Equal(new[] { "--extract-smart", @"C:\a\x.zip", @"C:\a\y.7z" }, CommandLineParser.ToArgs(retry));
        Assert.Equal(archives, CommandLineParser.Parse(CommandLineParser.ToArgs(retry).ToArray()).Archives);
        Assert.Equal(@"C:\a", ExtractTargetRule.Smart.DestinationFor(@"C:\a\x.zip"));
    }
}
