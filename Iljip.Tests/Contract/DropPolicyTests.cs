using System.Text.RegularExpressions;
using System.Windows.Controls;
using Iljip.Services;
using Iljip.Tests.TestSupport;
using Iljip.ViewModels;

namespace Iljip.Tests.Contract;

/// <summary>
/// [I0-4] 창에 파일을 끌어 놓을 때 보던 압축이 말없이 닫히지 않는다. 규칙은 <see cref="DropPolicy"/>(순수 함수),
/// 화면 흐름은 가짜 선택 창(<see cref="FakeUserInteraction"/>)으로 시험한다. 압축 파일인지는 확장자로만 본다.
/// </summary>
public class DropPolicyTests
{
    private static bool IsArchive(string p) => p.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) || p.EndsWith(".7z", StringComparison.OrdinalIgnoreCase);

    private static DropDecision Decide(ViewState state, params string[] paths) => DropPolicy.Decide(state, paths, IsArchive);

    [Fact]
    public void 빈_창에_압축_파일_하나는_바로_연다()
    {
        Assert.Equal(DropDecisionKind.OpenDirect, Decide(ViewState.Empty, "a.zip").Kind);
    }

    [Theory]
    [InlineData("a.txt")]
    [InlineData("a.zip", "b.zip")]
    [InlineData("a.zip", "b.txt")]
    [InlineData("보고서.docx")]   // 속은 ZIP 이어도 확장자로는 압축 파일이 아님 → 압축할 파일
    public void 빈_창에_그_밖은_바로_압축할_목록에(params string[] paths)
    {
        Assert.Equal(DropDecisionKind.StageDirect, Decide(ViewState.Empty, paths).Kind);
    }

    [Theory]
    [InlineData("a.txt")]
    [InlineData("a.txt", "b.txt")]
    [InlineData("a.zip", "b.zip")]
    public void 목록을_모으는_중에_일반_파일은_바로_더한다(params string[] paths)
    {
        Assert.Equal(DropDecisionKind.StageDirect, Decide(ViewState.Staging, paths).Kind);
    }

    [Fact]
    public void 목록을_모으는_중에_압축_파일_하나는_묻는다()
    {
        var d = Decide(ViewState.Staging, "a.7z");
        Assert.Equal(DropDecisionKind.Ask, d.Kind);
        Assert.Equal(DropPolicy.AskWhileStaging, d.Message);
        Assert.Equal(new[] { DropChoice.OpenArchive, DropChoice.AddToStaging, DropChoice.Cancel }, d.Options);
    }

    [Theory]
    [InlineData("a.txt")]
    [InlineData("a.txt", "b.txt")]
    [InlineData("a.zip", "b.zip")]
    public void 압축을_보는_중에는_늘_묻는다(params string[] paths)
    {
        var d = Decide(ViewState.Archive, paths);
        Assert.Equal(DropDecisionKind.Ask, d.Kind);
        Assert.Equal(DropPolicy.AskWhileArchive, d.Message);
        Assert.Equal(new[] { DropChoice.NewStaging, DropChoice.Cancel }, d.Options);
    }

    [Fact]
    public void 압축을_보는_중에_압축_파일_하나면_열기를_맨_앞에()
    {
        var d = Decide(ViewState.Archive, "b.zip");
        Assert.Equal(new[] { DropChoice.OpenArchive, DropChoice.NewStaging, DropChoice.Cancel }, d.Options);
    }

    /// <summary>선택 창의 기본 버튼(Enter·처음 포커스)은 늘 [취소] — Enter 한 번에 보던 압축이 닫히거나 모으던 목록이 비워지지 않게(W1-D 1차 검증).</summary>
    [Theory]
    [InlineData(ViewState.Staging, "a.zip")]
    [InlineData(ViewState.Archive, "a.zip")]
    [InlineData(ViewState.Archive, "a.txt")]
    [InlineData(ViewState.Archive, "a.txt", "b.zip")]
    public void 묻는_창의_기본_버튼은_늘_취소(ViewState state, params string[] paths)
    {
        var d = Decide(state, paths);
        Assert.Equal(DropDecisionKind.Ask, d.Kind);
        Assert.Equal(DropChoice.Cancel, d.Options[d.DefaultIndex]);
    }

    // ───────────── W2-06(I2-3): 고칠 수 있는 압축(ZIP·7z)을 보는 중이면 [이 압축 파일에 넣기] ─────────────

    private static DropDecision DecideEditable(ViewState state, params string[] paths) =>
        DropPolicy.Decide(state, paths, IsArchive, archiveEditable: true);

    [Theory]
    [InlineData("a.txt")]
    [InlineData("a.txt", "b.txt")]
    [InlineData("a.zip", "b.zip")]
    public void 고칠_수_있는_압축을_보는_중에는_넣을지_묻는다(params string[] paths)
    {
        var d = DecideEditable(ViewState.Archive, paths);
        Assert.Equal(DropDecisionKind.Ask, d.Kind);
        Assert.Equal(DropPolicy.AskWhileEditableArchive, d.Message);
        Assert.Contains("이 압축 파일에 넣을까요?", d.Message);
        Assert.Equal(new[] { DropChoice.AddToArchive, DropChoice.NewStaging, DropChoice.Cancel }, d.Options);
        Assert.Equal(DropChoice.Cancel, d.Options[d.DefaultIndex]);   // 기본 버튼은 여전히 [취소]
    }

    [Fact]
    public void 고칠_수_있는_압축을_보는_중에_압축_파일_하나면_넣기_다음에_열기()
    {
        var d = DecideEditable(ViewState.Archive, "b.zip");
        Assert.Equal(new[] { DropChoice.AddToArchive, DropChoice.OpenArchive, DropChoice.NewStaging, DropChoice.Cancel }, d.Options);
        Assert.Equal(DropChoice.Cancel, d.Options[d.DefaultIndex]);
    }

    /// <summary>고칠 수 있는지는 압축을 보는 중에만 따진다 — 빈 창·목록을 모으는 중은 예전 규칙 그대로.</summary>
    [Theory]
    [InlineData(ViewState.Empty, "a.zip")]
    [InlineData(ViewState.Empty, "a.txt")]
    [InlineData(ViewState.Staging, "a.zip")]
    [InlineData(ViewState.Staging, "a.txt")]
    public void 넣기_선택은_압축을_보는_중에만(ViewState state, params string[] paths)
    {
        Assert.Equal(Decide(state, paths), DecideEditable(state, paths), new DecisionComparer());
        Assert.DoesNotContain(DropChoice.AddToArchive, DecideEditable(state, paths).Options);
    }

    [Fact]
    public void 넣기_버튼_문구()
    {
        Assert.Equal("이 압축 파일에 넣기", DropPolicy.Label(DropChoice.AddToArchive, ViewState.Archive));
    }

    private sealed class DecisionComparer : IEqualityComparer<DropDecision>
    {
        public bool Equals(DropDecision? x, DropDecision? y) =>
            x is not null && y is not null && x.Kind == y.Kind && x.Message == y.Message && x.Options.SequenceEqual(y.Options);

        public int GetHashCode(DropDecision obj) => obj.Kind.GetHashCode();
    }

    [Fact]
    public void 묻지_않으면_기본_버튼도_없다()
    {
        Assert.Equal(-1, DropDecision.OpenDirect.DefaultIndex);
        Assert.Equal(-1, DropDecision.StageDirect.DefaultIndex);
    }

    [Fact]
    public void 버튼_문구는_무엇이_닫히는지_알린다()
    {
        Assert.Equal("이 압축 파일 열기 (압축할 목록은 비워져요)", DropPolicy.Label(DropChoice.OpenArchive, ViewState.Staging));
        Assert.Equal("이 압축 파일 열기 (보던 압축은 닫혀요)", DropPolicy.Label(DropChoice.OpenArchive, ViewState.Archive));
        Assert.Equal("압축할 목록에 넣기", DropPolicy.Label(DropChoice.AddToStaging, ViewState.Staging));
        Assert.Equal("새로 압축할 목록 만들기 (보던 압축은 닫혀요)", DropPolicy.Label(DropChoice.NewStaging, ViewState.Archive));
        Assert.Equal("취소", DropPolicy.Label(DropChoice.Cancel, ViewState.Archive));
    }

    // ───────────── 화면(VM) — 가짜 선택 창 ─────────────

    private static Task Drop(MainViewModel vm, params string[] paths) =>
        VmRunner.Run(() => vm.HandleDroppedPathsAsync(paths), "끌어 놓기");

    private static async Task<(MainViewModel Vm, FakeUserInteraction Ui, string Zip)> OpenedZip()
    {
        var ui = new FakeUserInteraction();
        var vm = new MainViewModel(ui);
        string zip = FixtureCatalog.Get("zip_default").FullPath;
        await VmRunner.Run(() => vm.OpenArchiveAsync(zip), "열기");
        Assert.Equal(zip, vm.CurrentArchivePath);
        return (vm, ui, zip);
    }

    private static string NewFile(TempDir tmp, string name)
    {
        string p = tmp.P(name);
        File.WriteAllText(p, name);
        return p;
    }

    [Theory]
    [InlineData("취소")]
    [InlineData(null)]   // 창을 닫음(-1)
    public async Task 압축을_보는_중_끌어_놓고_취소하면_그대로(string? choose)
    {
        using var tmp = new TempDir("drop1");
        var (vm, ui, zip) = await OpenedZip();
        ui.ChooseByLabel = choose;

        await Drop(vm, NewFile(tmp, "a.txt"));

        var prompt = Assert.Single(ui.ChoicePrompts);
        // W2-06: 보는 압축이 ZIP(고칠 수 있음)이라 [이 압축 파일에 넣기]가 맨 앞에 더해졌다(고칠 수 없는 압축은 아래 '고칠_수_없는_압축' 시험 — 예전 그대로)
        Assert.Equal(DropPolicy.AskWhileEditableArchive, prompt.Message);
        Assert.Equal(new[] { "이 압축 파일에 넣기", "새로 압축할 목록 만들기 (보던 압축은 닫혀요)", "취소" }, prompt.Choices);
        Assert.Equal("취소", prompt.Choices[prompt.DefaultIndex]);   // 화면에도 [취소]를 기본 버튼으로 넘긴다
        Assert.Equal(zip, vm.CurrentArchivePath);
        Assert.Equal(15, vm.Entries.Count);
        Assert.True(vm.IsArchiveMode);
    }

    [Fact]
    public async Task 압축을_보는_중_새로_압축할_목록을_고르면_목록이_된다()
    {
        using var tmp = new TempDir("drop2");
        var (vm, ui, _) = await OpenedZip();
        ui.ChooseByLabel = "새로 압축할 목록";

        await Drop(vm, NewFile(tmp, "a.txt"), NewFile(tmp, "b.txt"));

        Assert.True(vm.IsStagingMode);
        Assert.Null(vm.CurrentArchivePath);
        Assert.Equal(new[] { "a.txt", "b.txt" }, vm.Entries.Select(e => e.Path).ToArray());
        Assert.Empty(vm.FolderTree);
    }

    [Fact]
    public async Task 압축을_보는_중_다른_압축을_끌어_와_열기를_고르면_그것이_열린다()
    {
        var (vm, ui, _) = await OpenedZip();
        ui.ChooseByLabel = "이 압축 파일 열기";
        string tar = FixtureCatalog.Get("tar_default").FullPath;

        await Drop(vm, tar);

        Assert.Equal(new[] { "이 압축 파일에 넣기", "이 압축 파일 열기 (보던 압축은 닫혀요)", "새로 압축할 목록 만들기 (보던 압축은 닫혀요)", "취소" },
            Assert.Single(ui.ChoicePrompts).Choices);
        Assert.Equal(tar, vm.CurrentArchivePath);
    }

    /// <summary>W2-06: 고칠 수 없는 압축(TAR·RAR·EGG …)을 보는 중이면 예전 그대로 — [이 압축 파일에 넣기] 없음.</summary>
    [Fact]
    public async Task 고칠_수_없는_압축을_보는_중에는_넣기가_없다()
    {
        using var tmp = new TempDir("drop8");
        var ui = new FakeUserInteraction { ChooseByLabel = "취소" };
        var vm = new MainViewModel(ui);
        string tar = FixtureCatalog.Get("tar_default").FullPath;
        await VmRunner.Run(() => vm.OpenArchiveAsync(tar), "열기");
        Assert.False(vm.CanEditArchive);

        await Drop(vm, NewFile(tmp, "a.txt"));

        var prompt = Assert.Single(ui.ChoicePrompts);
        Assert.Equal(DropPolicy.AskWhileArchive, prompt.Message);
        Assert.Equal(new[] { "새로 압축할 목록 만들기 (보던 압축은 닫혀요)", "취소" }, prompt.Choices);
        Assert.Equal(tar, vm.CurrentArchivePath);
    }

    /// <summary>W2-06(I2-3): 열린 ZIP 에 파일을 끌어 놓고 [이 압축 파일에 넣기] → 질문은 한 번, 지금 보는 폴더에 들어가고 목록이 다시 읽힌다.</summary>
    [Fact]
    public async Task 열린_압축에_끌어_놓고_넣기를_고르면_들어간다()
    {
        using var tmp = new TempDir("drop9");
        string zip = tmp.P("열린.zip");
        File.Copy(FixtureCatalog.Get("zip_default").FullPath, zip);
        var ui = new FakeUserInteraction { ChooseByLabel = "이 압축 파일에 넣기" };
        var vm = new MainViewModel(ui);
        await VmRunner.Run(() => vm.OpenArchiveAsync(zip), "열기");
        int before = vm.Entries.Count;
        string file = NewFile(tmp, "끌어 온 파일.txt");

        await Drop(vm, file);

        Assert.Single(ui.ChoicePrompts);
        Assert.Empty(ui.Errors);
        Assert.Equal(zip, vm.CurrentArchivePath);
        Assert.Equal(before + 1, vm.Entries.Count);
        Assert.Contains(vm.Entries, e => e.Path == "끌어 온 파일.txt");
        var list = await Svc.For(zip).ListEntriesAsync(zip);
        Assert.Contains(list, e => e.Path == "끌어 온 파일.txt");
    }

    [Fact]
    public async Task 목록을_모으는_중_압축_파일을_끌어_와_열기를_고르면_압축을_연다()
    {
        using var tmp = new TempDir("drop3");
        var ui = new FakeUserInteraction { ChooseByLabel = "이 압축 파일 열기" };
        var vm = new MainViewModel(ui);
        vm.AddToStaging(new[] { NewFile(tmp, "a.txt") });
        string zip = FixtureCatalog.Get("zip_default").FullPath;

        await Drop(vm, zip);

        var prompt = Assert.Single(ui.ChoicePrompts);
        Assert.Equal(DropPolicy.AskWhileStaging, prompt.Message);
        Assert.Equal("취소", prompt.Choices[prompt.DefaultIndex]);
        Assert.True(vm.IsArchiveMode);
        Assert.Equal(zip, vm.CurrentArchivePath);
    }

    [Fact]
    public async Task 목록을_모으는_중_압축_파일을_목록에_넣기를_고르면_더해진다()
    {
        using var tmp = new TempDir("drop4");
        var ui = new FakeUserInteraction { ChooseByLabel = "압축할 목록에 넣기" };
        var vm = new MainViewModel(ui);
        vm.AddToStaging(new[] { NewFile(tmp, "a.txt") });
        string zip = FixtureCatalog.Get("zip_default").FullPath;

        await Drop(vm, zip);

        Assert.True(vm.IsStagingMode);
        Assert.Equal(new[] { "a.txt", "zip_default.zip" }, vm.Entries.Select(e => e.Path).ToArray());
    }

    [Fact]
    public async Task 목록을_모으는_중_일반_파일은_묻지_않고_더한다()
    {
        using var tmp = new TempDir("drop5");
        var ui = new FakeUserInteraction();
        var vm = new MainViewModel(ui);
        vm.AddToStaging(new[] { NewFile(tmp, "a.txt") });

        await Drop(vm, NewFile(tmp, "b.txt"));

        Assert.Empty(ui.ChoicePrompts);
        Assert.Equal(2, vm.Entries.Count);
    }

    [Fact]
    public async Task 빈_창에는_묻지_않는다()
    {
        using var tmp = new TempDir("drop6");
        var ui = new FakeUserInteraction();
        var vm = new MainViewModel(ui);
        string zip = FixtureCatalog.Get("zip_default").FullPath;

        await Drop(vm, zip);
        Assert.Equal(zip, vm.CurrentArchivePath);

        var ui2 = new FakeUserInteraction();
        var vm2 = new MainViewModel(ui2);
        await Drop(vm2, NewFile(tmp, "보고서.docx"));
        Assert.True(vm2.IsStagingMode);

        Assert.Empty(ui.ChoicePrompts);
        Assert.Empty(ui2.ChoicePrompts);
    }

    // ───────────── 선택 창·끌어 놓기 이벤트(창은 띄우지 않음) ─────────────

    /// <summary>
    /// 선택 창(<see cref="Iljip.Views.ChoiceDialog"/>)은 정해 준 버튼 하나만 기본(IsDefault — Enter)으로 둔다. 범위 밖이면 첫 버튼(예전 동작).
    /// 창은 만들기만 하고 띄우지 않는다(STA 스레드).
    /// </summary>
    [Theory]
    [InlineData(2, 2)]
    [InlineData(0, 0)]
    [InlineData(-1, 0)]
    [InlineData(9, 0)]
    public void 선택_창은_정해_준_버튼_하나만_기본으로(int defaultIndex, int expected)
    {
        Sta.Run(() =>
        {
            var dlg = new Iljip.Views.ChoiceDialog("문구", new[] { "이 압축 파일 열기", "새로 압축할 목록 만들기", "취소" }, defaultIndex);
            var buttons = dlg.ButtonsPanel.Children.OfType<Button>().ToList();
            Assert.Equal(3, buttons.Count);
            Assert.Equal(Enumerable.Range(0, 3).Select(i => i == expected).ToArray(), buttons.Select(b => b.IsDefault).ToArray());
        });
    }

    /// <summary>
    /// 끌어 놓기는 경로만 꺼내 두고 Drop 이벤트를 끝낸 뒤(Dispatcher.Yield) 처리한다 — OLE 끌어 놓기는 Drop 이 끝날 때까지 끌기를 시작한
    /// 탐색기 창이 기다리므로, 그 안에서 선택 창을 띄우면 그 탐색기 창이 멈춘다(W1-D 1차 검증). 끌기 데이터(e.Data)는 넘긴 뒤에 쓰지 않는다. (소스 확인)
    /// </summary>
    [Fact]
    public void 끌어_놓기는_Drop_이벤트를_끝낸_뒤에_처리한다()
    {
        string code = RepoFiles.Read("Iljip", "MainWindow.xaml.cs");
        var m = Regex.Match(code, @"void Window_Drop\(.*?\n    \}", RegexOptions.Singleline);
        Assert.True(m.Success, "Window_Drop 을 못 찾음");
        string body = m.Value;
        int yield = body.IndexOf("Dispatcher.Yield(", StringComparison.Ordinal);
        int handle = body.IndexOf("HandleDroppedPathsAsync(", StringComparison.Ordinal);
        Assert.True(yield >= 0, "Drop 안에서 바로 처리함(Dispatcher.Yield 없음)");
        Assert.True(handle > yield, "Dispatcher.Yield 전에 처리를 시작함");
        Assert.Matches(new Regex(@"await\s+[\w.]*Dispatcher\.Yield\("), body);   // 기다려야 Drop 이 먼저 끝난다
        Assert.DoesNotContain("e.Data", body[yield..]);
    }

    /// <summary>압축을 보는 중에 AddToStaging 을 바로 불러도 보던 압축을 닫지 않는다(닫는 것은 고른 경우에만 — StartNewStaging).</summary>
    [Fact]
    public async Task 압축을_보는_중_AddToStaging은_아무것도_안_한다()
    {
        using var tmp = new TempDir("drop7");
        var (vm, _, zip) = await OpenedZip();
        vm.AddToStaging(new[] { NewFile(tmp, "a.txt") });
        Assert.Equal(zip, vm.CurrentArchivePath);
        Assert.Equal(15, vm.Entries.Count);
    }
}
