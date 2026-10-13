using System.ComponentModel;
using Iljip.Models;
using Iljip.Services;
using Iljip.Tests.TestSupport;
using Iljip.ViewModels;

namespace Iljip.Tests.Contract;

/// <summary>W2-11 시험들이 같이 쓰는 도우미(픽스처 복사 — 나뉜 압축은 조각 전부).</summary>
internal static class W211
{
    /// <summary>픽스처를 <paramref name="dir"/> 에 복사하고 여는 파일 경로를 돌려준다(나뉜 압축은 조각 전부).</summary>
    public static string Copy(string dir, string fixtureId)
    {
        var fx = FixtureCatalog.Get(fixtureId);
        var files = fx.SplitVolumes.Count > 0 ? fx.SplitVolumes : new[] { fx.File };
        foreach (var rel in files)
            File.Copy(FixtureCatalog.PathOf(rel), Path.Combine(dir, Path.GetFileName(rel)));
        return Path.Combine(dir, Path.GetFileName(fx.FullPath));
    }

    public static ExtractTarget Into(string folder, bool makeFolder = false) =>
        new(folder, MakeFolder: makeFolder, DeleteArchiveAfter: false, OpenFolderAfter: false);

    /// <summary>폴더 안 파일 이름(숨김 임시 파일 포함 — 남은 것이 없는지 보려고).</summary>
    public static List<string> Names(string dir) =>
        Directory.EnumerateFileSystemEntries(dir).Select(Path.GetFileName).Select(n => n!).OrderBy(n => n, StringComparer.Ordinal).ToList();
}

/// <summary>
/// [W2-11 I3-15] 시작 화면(압축도 모으는 목록도 없을 때 목록 자리): 보이고 숨는 때, 큰 단추([압축하기][압축 풀기…]), 최근 파일 누르기·없는 파일·빼기·모두 지우기,
/// 새 창(☰ ▸ 새 창·Ctrl+N·최근 파일 '새 창으로 열기' — 가짜 <see cref="FakeProcessLauncher"/>, 진짜 프로세스는 띄우지 않음).
/// 화면(XAML)이 읽히는지는 MainWindowEditUiTests, 생김새는 MANUAL_CHECKLIST 17절.
/// </summary>
public class StartScreenTests
{
    private static Task Run(Func<Task> action, string what) => VmRunner.Run(action, what);

    private sealed class Ctx : IDisposable
    {
        public TempDir Tmp { get; }
        public string Work { get; }
        public TestServices Services { get; }
        public FakeUserInteraction Ui { get; } = new();
        public MainViewModel Vm { get; }
        public List<string> Changed { get; } = new();

        public Ctx(string tag, AppSettings? settings = null)
        {
            Tmp = new TempDir(tag);
            Work = Tmp.Dir("받은 파일");
            Services = new TestServices(settings);
            Vm = new MainViewModel(Ui, null, Services.Services);
            ((INotifyPropertyChanged)Vm).PropertyChanged += (_, e) =>
            {
                lock (Changed) Changed.Add(e.PropertyName ?? string.Empty);
            };
        }

        public void Dispose() => Tmp.Dispose();
    }

    private static AppSettings WithRecent(params string[] paths)
    {
        var s = new AppSettings();
        var t0 = new DateTime(2026, 10, 14, 0, 0, 0, DateTimeKind.Utc);
        for (int i = 0; i < paths.Length; i++)
            s.Recent.Files.Add(new RecentFile { Path = paths[i], LastOpenedUtc = t0.AddMinutes(-i) });
        return s;
    }

    /// <summary>빈 창이면 시작 화면 — 압축을 열면 숨고(알림), 닫으면 다시 보이고, 압축할 목록을 모으면 숨는다.</summary>
    [Fact]
    public async Task 빈_창이면_시작_화면이고_열면_숨고_닫으면_다시_보인다()
    {
        using var c = new Ctx("stvis");
        string archive = W211.Copy(c.Work, "zip_default");
        Assert.True(c.Vm.IsStartVisible);

        await Run(() => c.Vm.OpenArchiveAsync(archive), "열기");
        Assert.False(c.Vm.IsStartVisible);
        Assert.Contains(nameof(MainViewModel.IsStartVisible), c.Changed);

        c.Changed.Clear();
        await Run(() => c.Vm.ClearCommand.ExecuteAsync(null), "닫기");
        Assert.True(c.Vm.IsStartVisible);
        Assert.Contains(nameof(MainViewModel.IsStartVisible), c.Changed);

        string file = Path.Combine(c.Work, "보고서.txt");
        File.WriteAllText(file, "내용");
        await Run(() => c.Vm.HandleDroppedPathsAsync(new[] { file }), "끌어 놓기");
        Assert.True(c.Vm.IsStagingMode);
        Assert.False(c.Vm.IsStartVisible);
    }

    /// <summary>최근 파일을 누르면 그 압축을 연다(최근 목록 맨 앞으로).</summary>
    [Fact]
    public async Task 최근_파일을_누르면_연다()
    {
        using var t = new TempDir("stopen");
        string a = W211.Copy(t.Root, "zip_default");
        string b = W211.Copy(t.Root, "7z_default");
        using var c = new Ctx("stopen2", WithRecent(a, b));
        c.Vm.RefreshRecentFiles();
        Assert.Equal(new[] { a, b }, c.Vm.RecentFiles.Select(i => i.Path));
        Assert.All(c.Vm.RecentFiles, i => Assert.False(i.IsMissing));

        await Run(() => c.Vm.OpenRecentCommand.ExecuteAsync(c.Vm.RecentFiles[1]), "최근 파일 열기");
        Assert.Equal(b, c.Vm.CurrentArchivePath);
        Assert.False(c.Vm.IsStartVisible);
        Assert.Empty(c.Ui.ChoicePrompts);
        Assert.Equal(new[] { b, a }, c.Vm.RecentFiles.Select(i => i.Path));
    }

    /// <summary>
    /// 없는 최근 파일은 '없음'(흐리게)으로 보이고, 누르면 '…찾을 수 없어요… 최근 파일 목록에서 뺄까요?'([목록에서 빼기][그대로 두기]).
    /// 빼면 바로 저장(한 번), 그대로 두면 아무것도 바꾸지 않는다. 열기 오류 창은 띄우지 않는다.
    /// </summary>
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public async Task 없는_최근_파일은_목록에서_뺄까요를_묻는다(int answer)
    {
        using var t = new TempDir("stmiss");
        string have = W211.Copy(t.Root, "zip_default");
        string gone = Path.Combine(t.Root, "지운 견적.zip");
        using var c = new Ctx("stmiss2", WithRecent(gone, have));
        c.Vm.RefreshRecentFiles();
        var missing = c.Vm.RecentFiles[0];
        Assert.True(missing.IsMissing);
        Assert.Equal("지운 견적.zip", missing.Name);
        Assert.False(c.Vm.RecentFiles[1].IsMissing);
        c.Ui.ChoiceAnswers.Enqueue(answer);

        await Run(() => c.Vm.OpenRecentCommand.ExecuteAsync(missing), "없는 최근 파일 누르기");

        var prompt = Assert.Single(c.Ui.ChoicePrompts);
        Assert.Equal(MainViewModel.RecentMissingQuestion("지운 견적.zip"), prompt.Message);
        Assert.Contains("목록에서 뺄까요?", prompt.Message);
        Assert.Equal(new[] { "목록에서 빼기", "그대로 두기" }, prompt.Choices);
        Assert.Empty(c.Ui.Errors);
        Assert.Null(c.Vm.CurrentArchivePath);
        if (answer == 0)
        {
            Assert.Equal(new[] { have }, c.Vm.RecentFiles.Select(i => i.Path));
            Assert.Equal(1, c.Services.Settings.SaveCount);
            Assert.Equal(new[] { have }, c.Services.Settings.Load().Recent.Files.Select(f => f.Path));
        }
        else
        {
            Assert.Equal(new[] { gone, have }, c.Vm.RecentFiles.Select(i => i.Path));
            Assert.Equal(0, c.Services.Settings.SaveCount);
        }
    }

    /// <summary>오른쪽 클릭 '목록에서 지우기'는 그것만(파일은 그대로), '모두 지우기'는 묻고(기본 [취소]) 비운다 — 둘 다 바로 저장.</summary>
    [Fact]
    public void 목록에서_지우기와_모두_지우기()
    {
        using var t = new TempDir("stdel");
        string a = W211.Copy(t.Root, "zip_default");
        string b = W211.Copy(t.Root, "7z_default");
        using var c = new Ctx("stdel2", WithRecent(a, b));
        c.Vm.RefreshRecentFiles();

        c.Vm.RemoveRecentCommand.Execute(c.Vm.RecentFiles[0]);
        Assert.Equal(new[] { b }, c.Vm.RecentFiles.Select(i => i.Path));
        Assert.True(File.Exists(a));   // 파일은 그대로
        Assert.Equal(1, c.Services.Settings.SaveCount);

        c.Vm.ClearRecentCommand.Execute(null);   // 창을 닫음(-1) = 취소
        var ask = Assert.Single(c.Ui.ChoicePrompts);
        Assert.Equal(new[] { "모두 지우기", "취소" }, ask.Choices);
        Assert.Equal(1, ask.DefaultIndex);
        Assert.Single(c.Vm.RecentFiles);
        Assert.Equal(1, c.Services.Settings.SaveCount);

        c.Ui.ChoiceAnswers.Enqueue(0);
        c.Vm.ClearRecentCommand.Execute(null);
        Assert.Empty(c.Vm.RecentFiles);
        Assert.False(c.Vm.HasRecentFiles);
        Assert.True(File.Exists(b));
        Assert.Equal(2, c.Services.Settings.SaveCount);
        Assert.Empty(c.Services.Settings.Load().Recent.Files);

        // 다른 창이 닫으며 합쳐도 이 실행에서 뺀 것이 되살아나지 않는다
        c.Vm.SaveOnExit(double.NaN);
        Assert.Empty(c.Services.Settings.Load().Recent.Files);
    }

    /// <summary>
    /// W2-11 검증 지적: '없는 파일 정리'(제목 옆 단추·오른쪽 클릭 메뉴)는 찾을 수 없는 줄(흐린 '없음')만 한꺼번에 묻고 뺀다 —
    /// [목록에서 빼기]면 있는 파일은 그대로 남고 바로 한 번 저장, [취소]면 아무것도 바꾸지 않는다. 파일 자체는 건드리지 않는다.
    /// </summary>
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void 없는_파일_정리는_찾을_수_없는_줄만_한꺼번에_뺀다(int answer)
    {
        using var t = new TempDir("stclean");
        string have = W211.Copy(t.Root, "zip_default");
        string gone1 = Path.Combine(t.Root, "지운 견적.zip");
        string gone2 = Path.Combine(t.Root, "옮긴 자료.7z");
        using var c = new Ctx("stclean2", WithRecent(gone1, have, gone2));
        c.Vm.RefreshRecentFiles();
        Assert.True(c.Vm.HasMissingRecentFiles);
        c.Ui.ChoiceAnswers.Enqueue(answer);

        c.Vm.RemoveMissingRecentCommand.Execute(null);

        var ask = Assert.Single(c.Ui.ChoicePrompts);
        Assert.Equal(MainViewModel.RemoveMissingQuestion(new[] { "지운 견적.zip", "옮긴 자료.7z" }), ask.Message);
        Assert.StartsWith("찾을 수 없는 파일 2개(‘지운 견적.zip’, ‘옮긴 자료.7z’)를 최근 파일 목록에서 뺄까요?", ask.Message);
        Assert.Equal(new[] { "목록에서 빼기", "취소" }, ask.Choices);
        Assert.Empty(c.Ui.Errors);
        if (answer == 0)
        {
            Assert.Equal(new[] { have }, c.Vm.RecentFiles.Select(i => i.Path));
            Assert.False(c.Vm.HasMissingRecentFiles);
            Assert.Equal(1, c.Services.Settings.SaveCount);
            Assert.Equal(new[] { have }, c.Services.Settings.Load().Recent.Files.Select(f => f.Path));
            Assert.True(File.Exists(have));
        }
        else
        {
            Assert.Equal(new[] { gone1, have, gone2 }, c.Vm.RecentFiles.Select(i => i.Path));
            Assert.Equal(0, c.Services.Settings.SaveCount);
        }
    }

    /// <summary>찾을 수 없는 줄이 없으면 '없는 파일 정리'는 묻지 않고 알리기만 한다. 네트워크 경로는 보지 않으므로(끊긴 NAS 로 굳지 않게) '없음'이 아니다.</summary>
    [Fact]
    public void 없는_파일이_없으면_정리는_알리기만_한다()
    {
        using var t = new TempDir("stclean0");
        string have = W211.Copy(t.Root, "zip_default");
        using var c = new Ctx("stclean3", WithRecent(have, @"\\나스\공유\자료.zip"));
        c.Vm.RefreshRecentFiles();
        Assert.False(c.Vm.HasMissingRecentFiles);

        c.Vm.RemoveMissingRecentCommand.Execute(null);

        Assert.Empty(c.Ui.ChoicePrompts);
        Assert.Contains("찾을 수 없는 파일이 없어요", Assert.Single(c.Ui.Infos));
        Assert.Equal(2, c.Vm.RecentFiles.Count);
        Assert.Equal(0, c.Services.Settings.SaveCount);
    }

    /// <summary>오른쪽 클릭 '들어 있는 폴더 열기'는 탐색기에서 그 파일을 골라 둔 채(가짜 셸).</summary>
    [Fact]
    public void 들어_있는_폴더_열기()
    {
        using var c = new Ctx("stfold", WithRecent(@"C:\받은 파일\견적.zip"));
        c.Vm.RefreshRecentFiles();
        c.Vm.ShowRecentInFolderCommand.Execute(c.Vm.RecentFiles[0]);
        Assert.Equal((@"C:\받은 파일\견적.zip", true), Assert.Single(c.Services.Shell.OpenedFolders));
    }

    /// <summary>
    /// ☰ ▸ 새 창(Ctrl+N): 일집을 '--new-window' 로 하나 더(단일 인스턴스를 건너뜀 — StartupDecision 이 Independent 로 봄).
    /// 최근 파일 '새 창으로 열기'는 '--new-window --open 경로'. 관리자 권한으로 뜬 창이면 새 창도 '--elevated'.
    /// </summary>
    [Fact]
    public void 새_창은_new_window로_하나_더_띄운다()
    {
        using var c = new Ctx("stnew", WithRecent(@"C:\받은 파일\견적 (최종).zip"));
        c.Vm.NewWindowCommand.Execute(null);
        var (exe, args) = Assert.Single(c.Services.Processes.NewInstances);
        Assert.Equal(c.Services.Processes.ExecutablePath, exe);
        Assert.Equal(new[] { "--new-window" }, args);
        Assert.Equal(StartupKind.Independent, StartupDecision.Decide(args.ToArray(), mutexAcquired: false));
        Assert.Empty(c.Services.Processes.Elevated);

        c.Vm.RefreshRecentFiles();
        c.Vm.OpenRecentInNewWindowCommand.Execute(c.Vm.RecentFiles[0]);
        Assert.Equal(new[] { "--new-window", "--open", @"C:\받은 파일\견적 (최종).zip" }, c.Services.Processes.NewInstances[1].Args);
        Assert.Null(c.Vm.CurrentArchivePath);   // 이 창은 그대로

        c.Vm.IsElevatedInstance = true;
        c.Vm.NewWindowCommand.Execute(null);
        Assert.Equal(new[] { "--elevated", "--new-window" }, c.Services.Processes.NewInstances[2].Args);
        Assert.Empty(c.Ui.Errors);
    }

    /// <summary>새 창을 못 띄우면 쉬운 한국어 오류 창(실행 파일을 못 찾음 포함).</summary>
    [Fact]
    public void 새_창을_못_띄우면_알린다()
    {
        using var c = new Ctx("stnewx");
        c.Services.Processes.ThrowOnNewInstance = new Win32Exception(2);
        c.Vm.NewWindowCommand.Execute(null);
        Assert.Contains("새 창을 열지 못했어요", Assert.Single(c.Ui.Errors));

        c.Services.Processes.ExecutablePath = null;
        c.Vm.NewWindowCommand.Execute(null);
        Assert.Equal(2, c.Ui.Errors.Count);
        Assert.Contains("새 창을 열 수 없어요", c.Ui.Errors[1]);
        Assert.Single(c.Services.Processes.NewInstances);
    }

    /// <summary>시작 화면 [압축하기]: 파일을 고르면 모으는 목록에 담고 한 화면 압축 창을 띄운다(창에서 [취소]면 아무것도 만들지 않음).</summary>
    [Fact]
    public async Task 시작_화면_압축하기는_고른_파일로_압축_창을_띄운다()
    {
        using var c = new Ctx("stcmp");
        string file = Path.Combine(c.Work, "보고서.txt");
        File.WriteAllText(file, "내용");
        c.Ui.FilesToPick = new[] { file };
        c.Ui.CompressionRequestAnswer = _ => null;

        await Run(() => c.Vm.StartCompressCommand.ExecuteAsync(null), "시작 화면 압축하기");

        var request = Assert.Single(c.Ui.CompressionPrompts);
        Assert.Equal(new[] { file }, request.Sources);
        Assert.Equal(new[] { "보고서.txt" }, W211.Names(c.Work));

        // 고르지 않으면(창 닫기) 아무 일 없음
        using var d = new Ctx("stcmp0");
        await Run(() => d.Vm.StartCompressCommand.ExecuteAsync(null), "고르지 않음");
        Assert.Empty(d.Ui.CompressionPrompts);
        Assert.True(d.Vm.IsStartVisible);
    }

    /// <summary>시작 화면 [압축 풀기…]: 하나면 열어 보이며 풀기 창으로 풀고, 여럿이면 풀기 창 한 번으로 모두 푼다.</summary>
    [Fact]
    public async Task 시작_화면_압축_풀기는_하나든_여럿이든_푼다()
    {
        using var c = new Ctx("stext");
        string a = W211.Copy(c.Work, "zip_default");
        string b = W211.Copy(c.Work, "7z_default");
        string one = c.Tmp.Dir("하나");
        c.Ui.FilesToPick = new[] { a };
        c.Ui.ExtractTargetAnswer = _ => W211.Into(one);

        await Run(() => c.Vm.StartExtractCommand.ExecuteAsync(null), "하나 풀기");
        Assert.Equal(a, c.Vm.CurrentArchivePath);
        TreeAssert.ExtractedTreeEquals(one, FixtureCatalog.Get("zip_default").Expected, nfc: true, context: "하나");
        Assert.Single(c.Ui.ExtractTargetPrompts);

        using var d = new Ctx("stext2");
        string many = c.Tmp.Dir("여럿");
        d.Ui.FilesToPick = new[] { a, b };
        d.Ui.ExtractTargetAnswer = _ => W211.Into(many, makeFolder: true);
        await Run(() => d.Vm.StartExtractCommand.ExecuteAsync(null), "여럿 풀기");
        Assert.Single(d.Ui.ExtractTargetPrompts);
        Assert.Equal(new[] { "7z_default", "zip_default" }, W211.Names(many));
        Assert.Empty(d.Ui.Errors);
    }
}

/// <summary>
/// [W2-11 I3-15] 연 압축의 '다른 이름으로 저장': 같은 형식은 복사(나뉜 압축은 조각 전부 새 이름 규칙으로 — 바이트가 같다),
/// ZIP↔7Z 는 일집 임시 폴더(temp\convert\guid)에 다 풀고 다시 압축(원래 암호가 있었으면 '같은 암호를 걸까요?'), 끝나면 임시 폴더가 남지 않는다. 다 되면 새 파일을 연다.
/// </summary>
public class SaveAsTests
{
    private static Task Run(Func<Task> action, string what) => VmRunner.Run(action, what);

    private static string ConvertRoot => Path.Combine(AppPaths.TempRoot, MainViewModel.ConvertFolderName);

    private static void NoConvertLeftovers() =>
        Assert.True(!Directory.Exists(ConvertRoot) || !Directory.EnumerateFileSystemEntries(ConvertRoot).Any(),
            "temp\\convert 에 풀어 둔 것이 남음: " + (Directory.Exists(ConvertRoot) ? string.Join(", ", Directory.EnumerateFileSystemEntries(ConvertRoot)) : ""));

    private sealed class Ctx : IDisposable
    {
        public TempDir Tmp { get; }
        public string Work { get; }
        public string Out { get; }
        public TestServices Services { get; } = new();
        public FakeUserInteraction Ui { get; }
        public MainViewModel Vm { get; }

        public Ctx(string tag, params string?[] passwords)
        {
            Tmp = new TempDir(tag);
            Work = Tmp.Dir("받은 파일");
            Out = Tmp.Dir("저장할 곳");
            Ui = new FakeUserInteraction(passwords);
            Vm = new MainViewModel(Ui, null, Services.Services);
        }

        public void Dispose() => Tmp.Dispose();
    }

    [Theory]
    [InlineData("견적.zip", ".zip", "견적 - 복사본.zip")]
    [InlineData("견적.7z.001", ".7z.001", "견적 - 복사본.7z.001")]
    [InlineData("자료.tar.gz", ".tar.gz", "자료 - 복사본.tar.gz")]
    [InlineData("자료.part1.rar", ".part1.rar", "자료 - 복사본.part1.rar")]
    public void 처음_이름은_이름_복사본_같은_꼬리(string name, string suffix, string suggested)
    {
        Assert.Equal(suffix, ArchiveSaveAs.SuffixOf(name));
        Assert.Equal(suggested, ArchiveSaveAs.SuggestName(Path.Combine(@"C:\받은 파일", name)));
        string filter = ArchiveSaveAs.Filter(name, sevenZipAvailable: true);
        Assert.StartsWith($"같은 형식 (*{suffix})|*{suffix}", filter);
        Assert.EndsWith("모든 파일 (*.*)|*.*", filter);
    }

    [Fact]
    public void 계획_같은_파일_모르는_형식은_하지_않는다()
    {
        using var t = new TempDir("sapl");
        string archive = W211.Copy(t.Root, "zip_default");
        Assert.Equal(SaveAsKind.Refuse, ArchiveSaveAs.Plan(archive, archive.ToUpperInvariant(), true).Kind);
        var rar = ArchiveSaveAs.Plan(archive, Path.Combine(t.Root, "새.rar"), true);
        Assert.Equal(SaveAsKind.Refuse, rar.Kind);
        Assert.Contains("‘.rar’ 형식으로는 저장할 수 없어요", rar.Problem);
        Assert.Equal(SaveAsKind.Convert, ArchiveSaveAs.Plan(archive, Path.Combine(t.Root, "새.7z"), true).Kind);
        Assert.Equal(SaveAsKind.Refuse, ArchiveSaveAs.Plan(archive, Path.Combine(t.Root, "새.7z"), sevenZipAvailable: false).Kind);
        var copy = ArchiveSaveAs.Plan(archive, Path.Combine(t.Root, "새.ZIP"), true);
        Assert.Equal(SaveAsKind.Copy, copy.Kind);
        Assert.Single(copy.Pairs);
    }

    /// <summary>같은 형식: 바이트가 같은 복사본(수정 시각도), 새 파일을 열고 최근 목록 맨 앞. 숨김 임시 파일이 남지 않는다.</summary>
    [Theory]
    [InlineData("zip_default")]
    [InlineData("7z_default")]
    public async Task 같은_형식은_바이트가_같은_복사본이고_새_파일을_연다(string id)
    {
        using var c = new Ctx("sacp");
        string archive = W211.Copy(c.Work, id);
        string target = Path.Combine(c.Out, ArchiveSaveAs.SuggestName(archive));
        c.Ui.SavePath = target;

        await Run(() => c.Vm.OpenArchiveAsync(archive), "열기");
        await Run(() => c.Vm.SaveArchiveAsCommand.ExecuteAsync(null), "다른 이름으로 저장");

        Assert.Equal(new[] { ArchiveSaveAs.SuggestName(archive) }, c.Ui.SaveDefaultNames);
        Assert.Empty(c.Ui.Errors);
        Assert.Empty(c.Ui.Warnings);
        Assert.Equal(File.ReadAllBytes(archive), File.ReadAllBytes(target));
        Assert.Equal(File.GetLastWriteTimeUtc(archive), File.GetLastWriteTimeUtc(target));
        Assert.False(File.GetAttributes(target).HasFlag(FileAttributes.Hidden));
        Assert.Equal(new[] { Path.GetFileName(target) }, W211.Names(c.Out));
        Assert.Equal(target, c.Vm.CurrentArchivePath);
        Assert.Equal(target, c.Vm.RecentFiles[0].Path);
        Assert.True(File.Exists(archive));   // 원래 것은 그대로
        Assert.Empty(c.Ui.ChoicePrompts);
    }

    /// <summary>나뉜 압축: 조각 전부가 새 이름 규칙('새 이름' + 원래 꼬리)으로 복사되고 바이트가 같다, 연 조각의 새 이름을 연다.</summary>
    [Theory]
    [InlineData("7z_split", ".7z.001", new[] { ".7z.001", ".7z.002", ".7z.003" })]
    [InlineData("zip_split", ".zip", new[] { ".z01", ".z02", ".zip" })]
    public async Task 나뉜_압축은_조각_전부를_새_이름으로_복사한다(string id, string suffix, string[] partSuffixes)
    {
        using var c = new Ctx("saspl");
        string archive = W211.Copy(c.Work, id);
        Assert.Equal(id + " - 복사본" + suffix, ArchiveSaveAs.SuggestName(archive));
        string target = Path.Combine(c.Out, "새 이름" + suffix);
        c.Ui.SavePath = target;

        await Run(() => c.Vm.OpenArchiveAsync(archive), "열기");
        await Run(() => c.Vm.SaveArchiveAsCommand.ExecuteAsync(null), "다른 이름으로 저장");

        Assert.Empty(c.Ui.Errors);
        Assert.Empty(c.Ui.Warnings);
        Assert.Equal(partSuffixes.Select(s => "새 이름" + s).OrderBy(n => n, StringComparer.Ordinal), W211.Names(c.Out));
        foreach (var s in partSuffixes)
            Assert.Equal(File.ReadAllBytes(Path.Combine(c.Work, id + s)), File.ReadAllBytes(Path.Combine(c.Out, "새 이름" + s)));
        Assert.Equal(target, c.Vm.CurrentArchivePath);
        TreeAssert.ListFilesEqual(c.Vm.Entries.ToList(), FixtureCatalog.Get(id).Expected, "새 이름으로 연 목록");
    }

    /// <summary>
    /// 나뉜 압축은 조각이 섞이지 않게 덮어쓰지 않는다: 새 이름의 조각이 하나라도 있으면(숫자 조각은 그 다음 번호도) 하지 않고 알린다 — 있던 파일·연 압축은 그대로.
    /// </summary>
    [Theory]
    [InlineData("새 이름.7z.002")]
    [InlineData("새 이름.7z.004")]
    public async Task 나뉜_압축의_조각이_이미_있으면_하지_않는다(string existing)
    {
        using var c = new Ctx("saexi");
        string archive = W211.Copy(c.Work, "7z_split");
        string there = Path.Combine(c.Out, existing);
        File.WriteAllText(there, "원래 있던 것");
        c.Ui.SavePath = Path.Combine(c.Out, "새 이름.7z.001");

        await Run(() => c.Vm.OpenArchiveAsync(archive), "열기");
        await Run(() => c.Vm.SaveArchiveAsCommand.ExecuteAsync(null), "다른 이름으로 저장");

        Assert.Contains("이미 있어요", Assert.Single(c.Ui.Warnings));
        Assert.Equal(new[] { existing }, W211.Names(c.Out));
        Assert.Equal("원래 있던 것", File.ReadAllText(there));
        Assert.Equal(archive, c.Vm.CurrentArchivePath);
    }

    /// <summary>지금 연 파일 자신을 고르면 하지 않는다. 고르지 않으면(창 닫기) 아무 일 없음.</summary>
    [Fact]
    public async Task 같은_파일이나_취소면_하지_않는다()
    {
        using var c = new Ctx("sasame");
        string archive = W211.Copy(c.Work, "zip_default");
        byte[] before = File.ReadAllBytes(archive);
        c.Ui.SavePath = archive;
        await Run(() => c.Vm.OpenArchiveAsync(archive), "열기");
        await Run(() => c.Vm.SaveArchiveAsCommand.ExecuteAsync(null), "같은 파일로 저장");
        Assert.Contains("지금 열린 파일과 같은", Assert.Single(c.Ui.Warnings));
        Assert.Equal(before, File.ReadAllBytes(archive));

        c.Ui.SavePath = null;
        await Run(() => c.Vm.SaveArchiveAsCommand.ExecuteAsync(null), "취소");
        Assert.Single(c.Ui.Warnings);
        Assert.Empty(W211.Names(c.Out));
        Assert.Equal(new[] { "zip_default.zip" }, W211.Names(c.Work));
    }

    /// <summary>
    /// ZIP→7Z·7Z→ZIP: 새 형식으로 다시 압축 — 내용 왕복이 같다(파일·폴더·sha256), 암호가 없으면 묻지 않는다,
    /// temp\convert·숨김 임시 파일이 남지 않는다, 새 파일을 연다.
    /// </summary>
    [Theory]
    [InlineData("zip_default", "바꾼 것.7z")]
    [InlineData("7z_default", "바꾼 것.zip")]
    public async Task 다른_형식으로_바꾸면_내용이_같고_임시_폴더가_남지_않는다(string id, string newName)
    {
        using var c = new Ctx("sacv");
        string archive = W211.Copy(c.Work, id);
        string target = Path.Combine(c.Out, newName);
        c.Ui.SavePath = target;

        await Run(() => c.Vm.OpenArchiveAsync(archive), "열기");
        await Run(() => c.Vm.SaveArchiveAsCommand.ExecuteAsync(null), "다른 형식으로 저장");

        Assert.Empty(c.Ui.Errors);
        Assert.Empty(c.Ui.Warnings);
        Assert.Empty(c.Ui.ChoicePrompts);
        Assert.Equal(new[] { newName }, W211.Names(c.Out));
        Assert.Equal(target, c.Vm.CurrentArchivePath);
        var service = Svc.For(target);
        Assert.Contains(Path.GetExtension(newName), service.SupportedExtensions, StringComparer.OrdinalIgnoreCase);
        string dest = c.Tmp.Dir("확인");
        await service.ExtractAsync(target, dest);
        TreeAssert.ExtractedTreeEquals(dest, FixtureCatalog.Get(id).Expected, nfc: true, context: $"{id} → {newName}");
        NoConvertLeftovers();
    }

    /// <summary>
    /// 원래 압축에 암호가 있었으면 '같은 암호를 걸까요?'([같은 암호 걸기][암호 없이 저장][취소]): 걸면 새 파일도 그 암호(암호 없이는 못 풂),
    /// 없이 저장하면 암호 없이 풀린다, 취소면 새 파일이 없다. 어느 쪽이든 temp\convert 가 남지 않는다.
    /// </summary>
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public async Task 암호가_있던_압축은_같은_암호를_걸지_묻는다(int answer)
    {
        var fx = FixtureCatalog.Get("zip_aes256");
        using var c = new Ctx("sapw", fx.Password);
        string archive = W211.Copy(c.Work, "zip_aes256");
        string target = Path.Combine(c.Out, "암호 바꾼 것.7z");
        c.Ui.SavePath = target;
        c.Ui.ChoiceAnswers.Enqueue(answer);

        await Run(() => c.Vm.OpenArchiveAsync(archive, fx.Password), "열기");
        await Run(() => c.Vm.SaveArchiveAsCommand.ExecuteAsync(null), "다른 형식으로 저장");

        var ask = Assert.Single(c.Ui.ChoicePrompts);
        Assert.Equal(MainViewModel.SamePasswordQuestion, ask.Message);
        Assert.Equal(MainViewModel.SamePasswordChoices, ask.Choices);
        Assert.Empty(c.Ui.Errors);
        NoConvertLeftovers();
        if (answer == 2)
        {
            Assert.Empty(W211.Names(c.Out));
            Assert.Equal(archive, c.Vm.CurrentArchivePath);
            return;
        }

        Assert.Equal(new[] { "암호 바꾼 것.7z" }, W211.Names(c.Out));
        Assert.Equal(target, c.Vm.CurrentArchivePath);
        var service = Svc.For(target);
        string plain = c.Tmp.Dir("암호 없이");
        var noPassword = await Svc.Catch(() => service.ExtractAsync(target, plain));
        string dest = c.Tmp.Dir("확인");
        if (answer == 0)
        {
            Assert.NotNull(noPassword);
            Assert.True(Internals.IsPasswordRelated(noPassword!), noPassword!.ToString());
            TreeAssert.NoFiles(plain, "암호 없이 푼 곳");
            await service.ExtractAsync(target, dest, fx.Password);
        }
        else
        {
            Assert.Null(noPassword);
            await service.ExtractAsync(target, dest);
        }
        TreeAssert.ExtractedTreeEquals(dest, fx.Expected, nfc: true, context: $"암호 답 {answer}");
    }

    /// <summary>
    /// 새 형식으로 만들다 실패하면(저장할 곳에 쓸 권한 없음) 반쪽 파일·temp\convert 를 남기지 않고, 연 압축은 그대로.
    /// 권한 오류라 '관리자 권한으로 일집 다시 열기'를 묻는다(명령줄로 나타낼 수 없는 작업 — 원래 압축을 연다).
    /// </summary>
    [Fact]
    public async Task 바꾸다_실패하면_반쪽_파일이_없다()
    {
        using var c = new Ctx("sacvx");
        string archive = W211.Copy(c.Work, "zip_default");
        string target = Path.Combine(c.Out, "바꾼 것.7z");
        c.Ui.SavePath = target;
        c.Ui.ChoiceAnswers.Enqueue(1);   // 닫기
        await Run(() => c.Vm.OpenArchiveAsync(archive), "열기");

        using (ElevationTests.DenyCreate(c.Out))
            await Run(() => c.Vm.SaveArchiveAsCommand.ExecuteAsync(null), "다른 형식으로 저장");

        var ask = Assert.Single(c.Ui.ChoicePrompts);
        Assert.StartsWith("다른 이름으로 저장하지 못했어요. 원래 압축 파일은 그대로예요.", ask.Message);
        Assert.Equal(new[] { ElevationService.ReopenLabel, "닫기" }, ask.Choices);
        Assert.Empty(c.Services.Processes.Elevated);
        Assert.Empty(W211.Names(c.Out));
        Assert.Equal(archive, c.Vm.CurrentArchivePath);
        NoConvertLeftovers();
    }
}

/// <summary>
/// [W2-11 I3-15] ☰ ▸ '이 압축 파일 지우기': 묻고(‘a.zip’을 휴지통으로 옮길까요? — 기본 [취소]), 먼저 보기를 닫은 뒤 조각 전부를 휴지통으로
/// (가짜 <see cref="FakeRecycleBin"/> — 진짜로는 지우지 않음), 옮겼으면 작업 폴더·연 파일 감시를 놓고 시작 화면으로, 최근 목록에서 뺀다(못 옮기면 작업 폴더는 그대로 — 검증 지적).
/// </summary>
public class DeleteArchiveTests
{
    private static Task Run(Func<Task> action, string what) => VmRunner.Run(action, what);

    private sealed class Ctx : IDisposable
    {
        public TempDir Tmp { get; } = new("dlarc");
        public TestServices Services { get; } = new();
        public FakeUserInteraction Ui { get; } = new();
        public MainViewModel Vm { get; }

        public Ctx() => Vm = new MainViewModel(Ui, null, Services.Services);

        public void Dispose() => Tmp.Dispose();
    }

    /// <summary>
    /// [휴지통으로 옮기기]: 휴지통에 보내기 전에 압축이 닫혀 있다(일집이 잡은 것이 없어 FileShare.None 으로 열림), 나뉜 조각 전부를 한 번에,
    /// 시작 화면이 다시 보이고 최근 목록에서 빠진다(바로 저장).
    /// </summary>
    [Theory]
    [InlineData("zip_split", 3)]
    [InlineData("7z_split", 3)]
    [InlineData("zip_default", 1)]
    public async Task 지우기를_고르면_닫고_조각_전부를_휴지통으로(string id, int parts)
    {
        using var c = new Ctx();
        string archive = W211.Copy(c.Tmp.Root, id);
        var expectedParts = ArchiveVolumes.AllParts(archive);
        Assert.Equal(parts, expectedParts.Count);
        bool? closedBeforeSend = null;
        c.Services.RecycleBin.OnSend = paths =>
        {
            closedBeforeSend = c.Vm.CurrentArchivePath is null;
            foreach (var p in paths)
            {
                using var fs = new FileStream(p, FileMode.Open, FileAccess.ReadWrite, FileShare.None);   // 일집이 잡고 있으면 여기서 실패
            }
        };
        c.Ui.ChoiceAnswers.Enqueue(0);

        await Run(() => c.Vm.OpenArchiveAsync(archive), "열기");
        Assert.Equal(archive, c.Vm.RecentFiles[0].Path);
        await Run(() => c.Vm.DeleteArchiveFileCommand.ExecuteAsync(null), "이 압축 파일 지우기");

        var ask = Assert.Single(c.Ui.ChoicePrompts);
        string name = Path.GetFileName(archive);
        Assert.Equal(MainViewModel.DeleteArchiveQuestion(name, parts, Array.Empty<string>()), ask.Message);
        Assert.StartsWith($"‘{name}’", ask.Message);
        Assert.Contains("휴지통으로 옮길까요?", ask.Message);
        Assert.Equal(new[] { "휴지통으로 옮기기", "취소" }, ask.Choices);
        Assert.Equal(1, ask.DefaultIndex);
        Assert.True(closedBeforeSend);
        Assert.Equal(expectedParts.OrderBy(p => p), Assert.Single(c.Services.RecycleBin.Sent).OrderBy(p => p));
        Assert.Empty(c.Ui.Errors);
        Assert.True(c.Vm.IsStartVisible);
        Assert.Null(c.Vm.CurrentArchivePath);
        Assert.DoesNotContain(c.Vm.RecentFiles, i => string.Equals(i.Path, archive, StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(c.Services.Settings.Load().Recent.Files, f => string.Equals(f.Path, archive, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>[취소]·창 닫기면 휴지통에 0번, 압축은 그대로 열려 있다.</summary>
    [Theory]
    [InlineData(1)]
    [InlineData(-1)]
    public async Task 취소면_보내지_않는다(int answer)
    {
        using var c = new Ctx();
        string archive = W211.Copy(c.Tmp.Root, "zip_default");
        c.Ui.ChoiceAnswers.Enqueue(answer);
        await Run(() => c.Vm.OpenArchiveAsync(archive), "열기");
        await Run(() => c.Vm.DeleteArchiveFileCommand.ExecuteAsync(null), "지우기");

        Assert.Single(c.Ui.ChoicePrompts);
        Assert.Empty(c.Services.RecycleBin.Sent);
        Assert.Equal(archive, c.Vm.CurrentArchivePath);
        Assert.Equal("지우기를 취소했어요", c.Vm.StatusText);
        Assert.Equal(archive, c.Vm.RecentFiles[0].Path);
    }

    /// <summary>휴지통이 없는 곳(네트워크·USB)이면 묻지도 지우지도 않고 알린다(영구 삭제 금지).</summary>
    [Fact]
    public async Task 휴지통이_없는_곳이면_지우지_않는다()
    {
        using var c = new Ctx();
        string archive = W211.Copy(c.Tmp.Root, "zip_default");
        c.Services.RecycleBin.NotRecyclable.Add(c.Tmp.Root);
        await Run(() => c.Vm.OpenArchiveAsync(archive), "열기");
        await Run(() => c.Vm.DeleteArchiveFileCommand.ExecuteAsync(null), "지우기");

        Assert.Contains("휴지통이 없는 곳", Assert.Single(c.Ui.Warnings));
        Assert.Empty(c.Ui.ChoicePrompts);
        Assert.Empty(c.Services.RecycleBin.Sent);
        Assert.Equal(archive, c.Vm.CurrentArchivePath);
    }

    /// <summary>휴지통으로 못 옮기면 알리고 보던 압축을 다시 연다(최근 목록도 그대로).</summary>
    [Fact]
    public async Task 못_옮기면_알리고_다시_연다()
    {
        using var c = new Ctx();
        string archive = W211.Copy(c.Tmp.Root, "zip_default");
        c.Services.RecycleBin.Result = new RecycleResult(false, "다른 프로그램이 쓰고 있어요.");
        c.Ui.ChoiceAnswers.Enqueue(0);
        await Run(() => c.Vm.OpenArchiveAsync(archive), "열기");
        await Run(() => c.Vm.DeleteArchiveFileCommand.ExecuteAsync(null), "지우기");

        Assert.Single(c.Services.RecycleBin.Sent);
        var error = Assert.Single(c.Ui.Errors);
        Assert.Contains("휴지통으로 옮기지 못했어요", error);
        Assert.Contains("다른 프로그램이 쓰고 있어요.", error);
        Assert.Equal(archive, c.Vm.CurrentArchivePath);
        Assert.Equal(archive, c.Vm.RecentFiles[0].Path);
    }

    /// <summary>압축 속 파일을 열어 둔 채 지우면: 휴지통으로 옮긴 뒤 그 작업 폴더와 연 파일 감시도 놓는다(감시 0, 꺼내 둔 파일 없음).</summary>
    [Fact]
    public async Task 지우면_열어_둔_파일의_작업_폴더와_감시도_놓는다()
    {
        using var c = new Ctx();
        string archive = W211.Copy(c.Tmp.Root, "zip_default");
        c.Ui.ChoiceAnswers.Enqueue(0);
        await Run(() => c.Vm.OpenArchiveAsync(archive), "열기");
        var entry = c.Vm.Entries.First(e => !e.IsDirectory);
        bool opened = false;
        await Run(async () => opened = await c.Vm.OpenEntryAsync(entry), "안의 파일 열기");
        Assert.True(opened);
        string openedPath = Assert.Single(c.Services.Shell.OpenedFiles);
        Assert.True(File.Exists(openedPath));
        Assert.Equal(1, c.Services.FileChanges.ActiveCount);

        await Run(() => c.Vm.DeleteArchiveFileCommand.ExecuteAsync(null), "지우기");

        Assert.Single(c.Services.RecycleBin.Sent);
        Assert.Equal(0, c.Services.FileChanges.ActiveCount);
        Assert.False(File.Exists(openedPath));
        Assert.True(c.Vm.IsStartVisible);
    }

    /// <summary>
    /// W2-11 검증 지적: 휴지통으로 못 옮기면 압축 파일이 그대로 남으므로, 그 안에서 열어 고쳐 놓은(아직 넣지 않은) 파일·작업 폴더·연 파일 감시도 그대로 둔다
    /// (예전엔 옮기기 전에 작업 폴더를 지워 실패해도 고친 내용이 사라졌다). 다시 연 압축에서 이어서 '다시 넣기'를 할 수 있다.
    /// </summary>
    [Fact]
    public async Task 못_옮기면_열어_고친_파일과_감시를_그대로_둔다()
    {
        using var c = new Ctx();
        string archive = W211.Copy(c.Tmp.Root, "zip_default");
        c.Services.RecycleBin.Result = new RecycleResult(false, "다른 프로그램이 쓰고 있어요.");
        await Run(() => c.Vm.OpenArchiveAsync(archive), "열기");
        var entry = c.Vm.Entries.First(e => !e.IsDirectory);
        bool opened = false;
        await Run(async () => opened = await c.Vm.OpenEntryAsync(entry), "안의 파일 열기");
        Assert.True(opened);
        string openedPath = Assert.Single(c.Services.Shell.OpenedFiles);
        File.AppendAllText(openedPath, "\n고친 내용");   // 고쳐 놓고 아직 넣지 않음
        c.Ui.ChoiceAnswers.Enqueue(0);

        await Run(() => c.Vm.DeleteArchiveFileCommand.ExecuteAsync(null), "지우기(못 옮김)");

        Assert.Contains("아직 압축 파일에 넣지 않았어요", Assert.Single(c.Ui.ChoicePrompts).Message);
        Assert.Contains("휴지통으로 옮기지 못했어요", Assert.Single(c.Ui.Errors));
        Assert.Single(c.Services.RecycleBin.Sent);
        Assert.True(File.Exists(openedPath));
        Assert.EndsWith("고친 내용", File.ReadAllText(openedPath));
        Assert.Equal(1, c.Services.FileChanges.ActiveCount);
        Assert.True(c.Vm.HasOpenedEntries);
        Assert.Equal(archive, c.Vm.CurrentArchivePath);
        Assert.Equal(archive, c.Vm.RecentFiles[0].Path);
    }
}
