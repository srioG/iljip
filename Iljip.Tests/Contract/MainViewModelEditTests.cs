using Iljip.Models;
using Iljip.Services;
using Iljip.Services.Editing;
using Iljip.Tests.TestSupport;
using Iljip.ViewModels;

namespace Iljip.Tests.Contract;

/// <summary>
/// [W2-06, I2-3] 압축 고치기의 화면 흐름(가짜 창 — 실제 창·탐색기는 띄우지 않음): 지우기는 먼저 묻고(기본 [취소]), 이름 바꾸기·새 폴더·넣기는
/// 진행 창 하나로 하고 목록을 다시 읽는다. 암호 압축은 암호를 묻고, 고칠 수 없는 형식은 '이 형식은 고칠 수 없어요'.
/// 탐색기 우클릭 'zip에 추가'(--add-to)는 메인 창 없이 진행 창 + 짧은 안내.
/// </summary>
public class MainViewModelEditTests
{
    private static Task Run(Func<Task> action, string what) => VmRunner.Run(action, what);

    /// <summary>자료/{a.txt, b.txt, 하위/c.txt} 를 일집 ZIP 으로(암호가 있으면 ZipCrypto — 반디집과 같은 CP949 바이트).</summary>
    private static async Task<string> MakeZip(TempDir t, string? password = null)
    {
        string root = t.Dir("원본", "자료");
        Directory.CreateDirectory(Path.Combine(root, "하위"));
        File.WriteAllText(Path.Combine(root, "a.txt"), "에이");
        File.WriteAllText(Path.Combine(root, "b.txt"), "비");
        File.WriteAllText(Path.Combine(root, "하위", "c.txt"), "씨");
        string zip = t.P("고칠.zip");
        await new ZipArchiveService().CompressAsync(new[] { root }, zip,
            password is null ? null : new CompressionOptions { Password = password, Encryption = ZipEncryption.ZipCrypto });
        return zip;
    }

    private static async Task<(MainViewModel Vm, FakeUserInteraction Ui)> Open(string archive, params string?[] passwords)
    {
        var ui = new FakeUserInteraction(passwords);
        var vm = new MainViewModel(ui, null, new TestServices().Services);
        bool opened = false;
        await Run(async () => opened = await vm.OpenArchiveAsync(archive), "열기");
        Assert.True(opened);
        return (vm, ui);
    }

    private static ArchiveEntry EntryAt(MainViewModel vm, string path) =>
        vm.Entries.Single(e => TreeAssert.NormalizeEntryPath(e.Path) == path);

    private static async Task<List<string>> Files(string archive, string? password = null) =>
        (await Svc.For(archive).ListEntriesAsync(archive, password)).Where(e => !e.IsDirectory)
            .Select(e => TreeAssert.NormalizeEntryPath(e.Path)).OrderBy(p => p, StringComparer.Ordinal).ToList();

    // ───────────────────────────── 지우기 ─────────────────────────────

    /// <summary>계약: 지우기는 먼저 ‘N개를 압축 파일에서 지울까요?’(기본 [취소]) — 취소(또는 창 닫기)면 압축 파일은 바이트 그대로.</summary>
    [Theory]
    [InlineData("취소")]
    [InlineData(null)]
    public async Task 지우기를_취소하면_그대로다(string? answer)
    {
        using var t = new TempDir("vdel0");
        string zip = await MakeZip(t);
        string sha = TreeAssert.Sha256(zip);
        var (vm, ui) = await Open(zip);
        ui.ChooseByLabel = answer;

        bool done = true;
        await Run(async () => done = await vm.DeleteEntriesAsync(new[] { EntryAt(vm, "자료/a.txt") }), "지우기");

        Assert.False(done);
        var prompt = Assert.Single(ui.ChoicePrompts);
        Assert.StartsWith("1개를 압축 파일에서 지울까요?", prompt.Message);
        Assert.Equal(new[] { "지우기", "취소" }, prompt.Choices);
        Assert.Equal("취소", prompt.Choices[prompt.DefaultIndex]);
        Assert.Empty(ui.ProgressSessions);
        Assert.Equal(sha, TreeAssert.Sha256(zip));
        Assert.Equal(3, vm.Entries.Count);
    }

    [Fact]
    public async Task 지우기를_고르면_지우고_목록을_다시_읽는다()
    {
        using var t = new TempDir("vdel1");
        string zip = await MakeZip(t);
        var (vm, ui) = await Open(zip);
        ui.ChooseByLabel = "지우기";
        vm.NavigateToFolder("자료");
        var picked = vm.VisibleEntries.Where(e => e.Name is "a.txt" or "하위").ToList();
        Assert.Equal(2, picked.Count);

        await Run(() => vm.DeleteEntriesAsync(picked), "지우기");

        Assert.StartsWith("2개를 압축 파일에서 지울까요?", Assert.Single(ui.ChoicePrompts).Message);
        Assert.Empty(ui.Errors);
        Assert.Equal(new[] { "자료/b.txt" }, await Files(zip));
        Assert.Equal(new[] { "자료/b.txt" }, vm.Entries.Select(e => TreeAssert.NormalizeEntryPath(e.Path)).ToArray());
        Assert.Equal("자료", vm.SelectedFolder?.FullPath);   // 보던 폴더 그대로
        Assert.Single(ui.ProgressSessions);
    }

    /// <summary>
    /// [W2-09 검증 지적] 겉보기 이름이 같은 두 줄(맥 NFD·NFC '보고서.txt'): 한 줄만 골라 지우면 둘 다 지우지 않고 쉬운 말로 알리며 압축 파일은 그대로,
    /// 두 줄을 다 고르면 둘 다 지운다. 찾은 결과에서 폴더와 그 안 파일을 함께 골라도 지워진다.
    /// </summary>
    [Fact]
    public async Task 겉보기_이름이_같은_두_줄은_하나만_골라_지우지_않는다()
    {
        using var t = new TempDir("vdeldup");
        string zip = t.P("dup.zip");
        RawZip.Write(zip,
            new RawZip.Item(System.Text.Encoding.UTF8.GetBytes("하위/보고서.txt"), new byte[] { 1 }, true),
            new RawZip.Item(System.Text.Encoding.UTF8.GetBytes("하위/보고서.txt".Normalize(System.Text.NormalizationForm.FormD)), new byte[] { 2 }, true),
            new RawZip.Item(System.Text.Encoding.UTF8.GetBytes("하위/기타.txt"), new byte[] { 3 }, true),
            new RawZip.Item(System.Text.Encoding.UTF8.GetBytes("남김.txt"), new byte[] { 4 }, true));
        string sha = TreeAssert.Sha256(zip);
        var (vm, ui) = await Open(zip);
        ui.ChooseByLabel = "지우기";
        var twins = vm.Entries.Where(e => e.Path == "하위/보고서.txt").ToList();
        Assert.Equal(2, twins.Count);

        await Run(() => vm.DeleteEntriesAsync(new[] { twins[1] }), "한 줄만 지우기");
        Assert.Contains("2개 있어 어느 것을 지울지 알 수 없어요", Assert.Single(ui.Errors));
        Assert.Equal(sha, TreeAssert.Sha256(zip));

        await Run(() => vm.DeleteEntriesAsync(vm.Entries.Where(e => e.Path == "하위/보고서.txt").ToList()), "두 줄 지우기");
        Assert.Single(ui.Errors);
        Assert.Equal(new[] { "남김.txt", "하위/기타.txt" }, await Files(zip));

        // 찾은 결과: 폴더 '하위' 와 그 안 '기타.txt' 를 함께 골라 지우기
        vm.SearchText = "하위";
        vm.ApplySearchNow();
        var folder = Assert.Single(vm.VisibleEntries);
        vm.SearchText = "기타";
        vm.ApplySearchNow();
        var inner = Assert.Single(vm.VisibleEntries);
        await Run(() => vm.DeleteEntriesAsync(new[] { folder, inner }), "폴더와 안 파일 지우기");
        Assert.Single(ui.Errors);
        Assert.Equal(new[] { "남김.txt" }, await Files(zip));
    }

    // ───────────────────────────── 이름 바꾸기·새 폴더·넣기 ─────────────────────────────

    [Fact]
    public async Task 이름_바꾸기()
    {
        using var t = new TempDir("vren");
        string zip = await MakeZip(t);
        var (vm, ui) = await Open(zip);

        bool ok = false;
        await Run(async () => ok = await vm.RenameEntryAsync(EntryAt(vm, "자료/a.txt"), "에이 (고침).txt"), "이름 바꾸기");

        Assert.True(ok);
        Assert.Empty(ui.Errors);
        Assert.Equal(new[] { "자료/b.txt", "자료/에이 (고침).txt", "자료/하위/c.txt" }, await Files(zip));
        Assert.Contains(vm.Entries, e => e.Path == "자료/에이 (고침).txt");
    }

    /// <summary>못 쓰는 이름은 고치지 않고 쉬운 문구로 알린다(진행 창도 없음).</summary>
    [Fact]
    public async Task 이름_바꾸기_못_쓰는_이름은_알린다()
    {
        using var t = new TempDir("vren2");
        string zip = await MakeZip(t);
        string sha = TreeAssert.Sha256(zip);
        var (vm, ui) = await Open(zip);

        await Run(() => vm.RenameEntryAsync(EntryAt(vm, "자료/a.txt"), "물음?표.txt"), "이름 바꾸기");

        Assert.Equal("이름에 쓸 수 없는 글자가 있어요: \\ / : * ? \" < > |", Assert.Single(ui.Warnings));
        Assert.Empty(ui.ProgressSessions);
        Assert.Equal(sha, TreeAssert.Sha256(zip));
    }

    /// <summary>같은 이름이 이미 있으면 편집기의 문구를 오류 창으로(원본 그대로).</summary>
    [Fact]
    public async Task 이름_바꾸기_이미_있는_이름은_알린다()
    {
        using var t = new TempDir("vren3");
        string zip = await MakeZip(t);
        string sha = TreeAssert.Sha256(zip);
        var (vm, ui) = await Open(zip);

        await Run(() => vm.RenameEntryAsync(EntryAt(vm, "자료/a.txt"), "B.txt"), "이름 바꾸기");

        Assert.Contains("‘B.txt’ 이름이 이미 있어요", Assert.Single(ui.Errors));
        Assert.Equal(sha, TreeAssert.Sha256(zip));
    }

    [Fact]
    public async Task 새_폴더는_이름을_묻고_지금_폴더_안에_만든다()
    {
        using var t = new TempDir("vnew");
        string zip = await MakeZip(t);
        var (vm, ui) = await Open(zip);
        vm.NavigateToFolder("자료");
        ui.TextAnswers.Enqueue("보고서 모음");

        await Run(() => vm.NewFolderInArchiveCommand.ExecuteAsync(null), "새 폴더");

        var prompt = Assert.Single(ui.TextPrompts);
        Assert.Equal("새 폴더", prompt.Initial);
        Assert.Empty(ui.Errors);
        var list = await Svc.For(zip).ListEntriesAsync(zip);
        Assert.Contains(list, e => e.IsDirectory && TreeAssert.NormalizeEntryPath(e.Path) == "자료/보고서 모음");
        Assert.Contains(vm.VisibleEntries, e => e.IsDirectory && e.Name == "보고서 모음");
    }

    [Fact]
    public async Task 새_폴더_이름_창을_취소하면_그대로다()
    {
        using var t = new TempDir("vnew2");
        string zip = await MakeZip(t);
        string sha = TreeAssert.Sha256(zip);
        var (vm, ui) = await Open(zip);

        await Run(() => vm.NewFolderInArchiveCommand.ExecuteAsync(null), "새 폴더");

        Assert.Single(ui.TextPrompts);
        Assert.Empty(ui.ProgressSessions);
        Assert.Equal(sha, TreeAssert.Sha256(zip));
    }

    [Fact]
    public async Task 파일_넣기는_지금_폴더에_넣는다()
    {
        using var t = new TempDir("vadd");
        string zip = await MakeZip(t);
        var (vm, ui) = await Open(zip);
        vm.NavigateToFolder("자료/하위");
        string src = t.Dir("넣을 것");
        File.WriteAllText(Path.Combine(src, "새것.txt"), "새것");
        ui.FilesToPick = new[] { Path.Combine(src, "새것.txt") };

        await Run(() => vm.AddFilesToArchiveCommand.ExecuteAsync(null), "파일 넣기");

        Assert.Empty(ui.Errors);
        Assert.Contains("자료/하위/새것.txt", await Files(zip));
        Assert.Contains(vm.VisibleEntries, e => e.Name == "새것.txt");
        Assert.Equal("1개를 넣었어요", vm.StatusText);
    }

    /// <summary>같은 이름이 있으면 같은 이름 창으로 묻는다(압축 안에서는 설정과 상관없이 늘 묻는다) — [덮어쓰기]면 바꿔 넣는다.</summary>
    [Fact]
    public async Task 같은_이름은_묻고_덮어쓴다()
    {
        using var t = new TempDir("vadd2");
        string zip = await MakeZip(t);
        var (vm, ui) = await Open(zip);
        vm.NavigateToFolder("자료");
        string src = t.Dir("넣을 것");
        File.WriteAllText(Path.Combine(src, "a.txt"), "새 에이");
        ui.CollisionAnswers.Enqueue(new CollisionAnswer(CollisionChoice.Overwrite, false));

        await Run(() => vm.AddPathsToArchiveAsync(new[] { Path.Combine(src, "a.txt") }), "넣기");

        var q = Assert.Single(ui.CollisionPrompts);
        Assert.Equal("자료/a.txt", q.TargetPath);
        Assert.Equal(new[] { "자료/a.txt", "자료/b.txt", "자료/하위/c.txt" }, await Files(zip));
        string dest = t.Dir("풀기");
        await Svc.For(zip).ExtractAsync(zip, dest);
        Assert.Equal("새 에이", File.ReadAllText(Path.Combine(dest, "자료", "a.txt")));
    }

    // ───────────────────────────── 암호·고칠 수 없는 형식 ─────────────────────────────

    /// <summary>암호 ZIP 에 넣으면 암호를 묻는다(틀리면 다시) — 넣은 파일도 같은 암호로 잠긴다.</summary>
    [Fact]
    public async Task 암호_ZIP에_넣으면_암호를_묻는다()
    {
        using var t = new TempDir("vpw");
        string zip = await MakeZip(t, FixtureCatalog.KoreanPassword);
        var (vm, ui) = await Open(zip, "틀린암호", FixtureCatalog.KoreanPassword);
        string src = t.Dir("넣을 것");
        File.WriteAllText(Path.Combine(src, "새것.txt"), "비밀");

        await Run(() => vm.AddPathsToArchiveAsync(new[] { Path.Combine(src, "새것.txt") }), "넣기");

        Assert.Equal(new[] { PasswordPromptReason.Required, PasswordPromptReason.Wrong }, ui.PasswordPrompts.Select(p => p.Reason).ToArray());
        Assert.Empty(ui.Errors);
        Assert.Contains("새것.txt", await Files(zip));
        var ex = await Svc.Catch(() => Svc.For(zip).ExtractAsync(zip, t.P("암호없이"), null));
        Assert.IsAssignableFrom<ArchivePasswordException>(ex);
        string dest = t.Dir("풀기");
        await Svc.For(zip).ExtractAsync(zip, dest, FixtureCatalog.KoreanPassword);
        Assert.Equal("비밀", File.ReadAllText(Path.Combine(dest, "새것.txt")));
    }

    [Fact]
    public async Task 암호_창을_취소하면_그대로다()
    {
        using var t = new TempDir("vpw2");
        string zip = await MakeZip(t, FixtureCatalog.DefaultPassword);
        string sha = TreeAssert.Sha256(zip);
        var (vm, ui) = await Open(zip);
        string src = t.Dir("넣을 것");
        File.WriteAllText(Path.Combine(src, "새것.txt"), "x");

        bool ok = true;
        await Run(async () => ok = await vm.AddPathsToArchiveAsync(new[] { Path.Combine(src, "새것.txt") }), "넣기");

        Assert.False(ok);
        Assert.Single(ui.PasswordPrompts);
        Assert.Empty(ui.Errors);
        Assert.Equal(sha, TreeAssert.Sha256(zip));
    }

    /// <summary>계약: 고칠 수 없는 형식(TAR·RAR·EGG …)을 보는 중이면 넣기·새 폴더·지우기·이름 바꾸기 모두 '이 형식은 고칠 수 없어요' — 아무것도 바뀌지 않는다.</summary>
    [Fact]
    public async Task 고칠_수_없는_형식은_안내만_한다()
    {
        using var t = new TempDir("vtar");
        string root = t.Dir("원본", "자료");
        File.WriteAllText(Path.Combine(root, "a.txt"), "a");
        string tar = t.P("묶음.tar");
        await new TarArchiveService().CompressAsync(new[] { root }, tar);
        string sha = TreeAssert.Sha256(tar);
        var (vm, ui) = await Open(tar);
        Assert.False(vm.CanEditArchive);
        ui.ChooseByLabel = "지우기";
        ui.FilesToPick = new[] { Path.Combine(root, "a.txt") };
        ui.TextAnswers.Enqueue("새 폴더");

        await Run(() => vm.AddFilesToArchiveCommand.ExecuteAsync(null), "넣기");
        await Run(() => vm.NewFolderInArchiveCommand.ExecuteAsync(null), "새 폴더");
        await Run(() => vm.DeleteEntriesAsync(new[] { EntryAt(vm, "자료/a.txt") }), "지우기");
        await Run(() => vm.RenameEntryAsync(EntryAt(vm, "자료/a.txt"), "b.txt"), "이름 바꾸기");
        await Run(() => vm.AddPathsToArchiveAsync(new[] { Path.Combine(root, "a.txt") }), "끌어 놓기 넣기");

        Assert.Equal(5, ui.Infos.Count);
        Assert.All(ui.Infos, m => Assert.Equal(ArchiveNotEditableException.DefaultMessage, m));
        Assert.StartsWith("이 형식은 고칠 수 없어요", ArchiveNotEditableException.DefaultMessage);
        Assert.Empty(ui.ChoicePrompts);
        Assert.Empty(ui.TextPrompts);
        Assert.Empty(ui.ProgressSessions);
        Assert.Equal(sha, TreeAssert.Sha256(tar));
    }

    // ───────────────────────────── 탐색기 우클릭 'zip에 추가'(--add-to) ─────────────────────────────

    /// <summary>계약: '--add-to 압축 경로…' 는 메인 창 없이(보던 것 그대로) 진행 창 하나로 넣고 ‘N개를 넣었어요’ 한 줄로 알린다.</summary>
    [Fact]
    public async Task 우클릭_넣기는_메인_창_없이_넣고_짧게_알린다()
    {
        using var t = new TempDir("vaddto");
        string zip = await MakeZip(t);
        string src = t.Dir("넣을 것");
        File.WriteAllText(Path.Combine(src, "x.txt"), "x");
        Directory.CreateDirectory(Path.Combine(src, "폴더 y"));
        File.WriteAllText(Path.Combine(src, "폴더 y", "y1.txt"), "y1");
        File.WriteAllText(Path.Combine(src, "폴더 y", "y2.txt"), "y2");
        var ui = new FakeUserInteraction();
        var vm = new MainViewModel(ui, null, new TestServices().Services);

        await Run(() => vm.ProcessShellCommandAsync(new[] { "--add-to", zip, Path.Combine(src, "x.txt"), Path.Combine(src, "폴더 y") }), "우클릭 넣기");

        Assert.Empty(ui.Errors);
        Assert.Equal("3개를 넣었어요.\n(고칠.zip)", Assert.Single(ui.Infos));
        Assert.Single(ui.ProgressSessions);
        Assert.False(vm.HasAnyContent);   // 메인 창(목록)은 그대로 빈 채
        Assert.Equal(new[] { "x.txt", "자료/a.txt", "자료/b.txt", "자료/하위/c.txt", "폴더 y/y1.txt", "폴더 y/y2.txt" }, await Files(zip));
    }

    /// <summary>'--add-to' 로 암호 ZIP 에 넣으면 진행 창에서 암호를 묻는다.</summary>
    [Fact]
    public async Task 우클릭_넣기도_암호를_묻는다()
    {
        using var t = new TempDir("vaddto2");
        string zip = await MakeZip(t, FixtureCatalog.DefaultPassword);
        string src = t.Dir("넣을 것");
        File.WriteAllText(Path.Combine(src, "x.txt"), "x");
        var ui = new FakeUserInteraction(FixtureCatalog.DefaultPassword);
        var vm = new MainViewModel(ui, null, new TestServices().Services);

        await Run(() => vm.ProcessShellCommandAsync(new[] { "--add-to", zip, Path.Combine(src, "x.txt") }), "우클릭 넣기");

        Assert.Single(ui.PasswordPrompts);
        Assert.Empty(ui.Errors);
        Assert.Equal("1개를 넣었어요.\n(고칠.zip)", Assert.Single(ui.Infos));
        Assert.Contains("x.txt", await Files(zip));
    }

    [Theory]
    [InlineData("tar")]
    [InlineData("missing")]
    [InlineData("nofiles")]
    public async Task 우클릭_넣기_실패는_쉬운_오류_한_번(string kind)
    {
        using var t = new TempDir("vaddto3");
        string src = t.Dir("넣을 것");
        File.WriteAllText(Path.Combine(src, "x.txt"), "x");
        string target = kind switch
        {
            "tar" => t.P("묶음.tar"),
            "missing" => t.P("없는.zip"),
            _ => await MakeZip(t),
        };
        if (kind == "tar") await new TarArchiveService().CompressAsync(new[] { src }, target);
        string? sha = File.Exists(target) ? TreeAssert.Sha256(target) : null;
        string item = kind == "nofiles" ? t.P("없는 파일.txt") : Path.Combine(src, "x.txt");
        var ui = new FakeUserInteraction();
        var vm = new MainViewModel(ui, null, new TestServices().Services);

        await Run(() => vm.ProcessShellCommandAsync(new[] { "--add-to", target, item }), "우클릭 넣기");

        string error = Assert.Single(ui.Errors);
        Assert.Contains(kind == "tar" ? "고칠 수 없어요" : "찾을 수 없어요", error);
        Assert.Empty(ui.Infos);
        Assert.Empty(ui.ProgressSessions);
        if (sha is not null) Assert.Equal(sha, TreeAssert.Sha256(target));
        foreach (string bad in new[] { "압축 파일이 아닌", "압축파일이 아닌", "not archive", "not an archive", "unsupported archive" })
            Assert.DoesNotContain(bad, error, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// 계약(W2-06 검증 지적 — 우클릭 'zip에 추가(기존 zip 고르기)'): '--add-to-pick 경로…' 는 먼저 넣을 압축 파일을 고르는 창(ZIP·7Z 만)을 띄우고,
    /// 고른 압축에 넣고 ‘N개를 넣었어요’ 한 줄. 고르지 않으면(창 닫기) 조용히 그만 — 오류·진행 창 없음. 고칠 수 없는 형식을 고르면 쉬운 오류 한 번.
    /// </summary>
    [Fact]
    public async Task 기존_압축_고르기로_넣으면_고른_압축에_넣는다()
    {
        using var t = new TempDir("vaddpick");
        string zip = await MakeZip(t);
        string src = t.Dir("넣을 것");
        File.WriteAllText(Path.Combine(src, "x.txt"), "x");
        string[] args = { "--add-to-pick", Path.Combine(src, "x.txt") };

        // 고르지 않음 — 조용히 그만
        string sha = TreeAssert.Sha256(zip);
        var cancelUi = new FakeUserInteraction();
        var cancelVm = new MainViewModel(cancelUi, null, new TestServices().Services);
        await Run(() => cancelVm.ProcessShellCommandAsync(args), "고르기 취소");
        Assert.Equal(MainViewModel.AddToPickFilter, Assert.Single(cancelUi.PickFilesFilters));
        Assert.False(cancelUi.AnyErrorOrWarning);
        Assert.Empty(cancelUi.Infos);
        Assert.Empty(cancelUi.ProgressSessions);
        Assert.Equal(sha, TreeAssert.Sha256(zip));

        // 고름 — 그 압축에 넣는다(메인 창은 그대로 빈 채)
        var ui = new FakeUserInteraction { FilesToPick = new[] { zip } };
        var vm = new MainViewModel(ui, null, new TestServices().Services);
        await Run(() => vm.ProcessShellCommandAsync(args), "고른 압축에 넣기");
        Assert.Empty(ui.Errors);
        Assert.Equal("1개를 넣었어요.\n(고칠.zip)", Assert.Single(ui.Infos));
        Assert.Single(ui.ProgressSessions);
        Assert.False(vm.HasAnyContent);
        Assert.Equal(new[] { "x.txt", "자료/a.txt", "자료/b.txt", "자료/하위/c.txt" }, await Files(zip));

        // 고칠 수 없는 형식을 고름 — 쉬운 오류 한 번, 바뀌지 않음
        string tar = t.P("묶음.tar");
        await new TarArchiveService().CompressAsync(new[] { src }, tar);
        string tarSha = TreeAssert.Sha256(tar);
        var tarUi = new FakeUserInteraction { FilesToPick = new[] { tar } };
        await Run(() => new MainViewModel(tarUi, null, new TestServices().Services).ProcessShellCommandAsync(args), "TAR 고름");
        Assert.Contains(ArchiveNotEditableException.DefaultMessage, Assert.Single(tarUi.Errors));
        Assert.Empty(tarUi.ProgressSessions);
        Assert.Equal(tarSha, TreeAssert.Sha256(tar));
    }

    /// <summary>
    /// 계약(W2-06 검증 지적): 7z.dll 을 못 불러온 PC(보안 프로그램이 막음 — 흉내)에서는 7z 를 고치려 하면 '이 형식은 고칠 수 없어요'(7z 는 고칠 수 있는 형식이라 틀린 말)가 아니라
    /// '이 PC에서는 7Z 파일을 고치는 부품(7z.dll)을 불러오지 못해…'. 목록 보기는 대신 열기로 된다. TAR 는 그대로 '이 형식은 고칠 수 없어요'.
    /// </summary>
    [Fact]
    public async Task 막힌_PC에서는_7z를_고칠_수_없는_까닭을_알린다()
    {
        using var t = new TempDir("vno7z");
        string root = t.Dir("원본", "자료");
        File.WriteAllText(Path.Combine(root, "a.txt"), "에이");
        string sevenZip = SevenZipTestArchives.SevenZip(root, t.P("막힘.7z"));
        string sha = TreeAssert.Sha256(sevenZip);
        string src = t.Dir("넣을 것");
        File.WriteAllText(Path.Combine(src, "x.txt"), "x");

        using var blocked = SevenZipLibrary.SimulateUnavailableForTests();
        var (vm, ui) = await Open(sevenZip);
        Assert.False(vm.CanEditArchive);
        ui.ChooseByLabel = "지우기";
        ui.TextAnswers.Enqueue("새 폴더");
        await Run(() => vm.DeleteEntriesAsync(new[] { EntryAt(vm, "자료/a.txt") }), "지우기");
        await Run(() => vm.NewFolderInArchiveCommand.ExecuteAsync(null), "새 폴더");
        await Run(() => vm.AddPathsToArchiveAsync(new[] { Path.Combine(src, "x.txt") }), "끌어 놓기 넣기");
        Assert.Equal(3, ui.Infos.Count);
        Assert.All(ui.Infos, m => Assert.Equal(ArchiveNotEditableException.SevenZipEngineMessage, m));
        Assert.Empty(ui.ChoicePrompts);
        Assert.Empty(ui.ProgressSessions);

        // 우클릭 'zip에 추가'로 그 7z 에 넣으려 해도 같은 까닭
        var cmdUi = new FakeUserInteraction();
        await Run(() => new MainViewModel(cmdUi, null, new TestServices().Services)
            .ProcessShellCommandAsync(new[] { "--add-to", sevenZip, Path.Combine(src, "x.txt") }), "우클릭 넣기");
        Assert.Contains(ArchiveNotEditableException.SevenZipEngineMessage, Assert.Single(cmdUi.Errors));
        Assert.Equal(sha, TreeAssert.Sha256(sevenZip));

        // 고칠 수 없는 형식(TAR)·나뉜 7z 는 7z.dll 과 상관없이 늘 같은 안내
        Assert.Equal(ArchiveNotEditableException.DefaultMessage, ArchiveServiceLocator.NotEditableMessage(t.P("묶음.tar")));
        Assert.Equal(ArchiveNotEditableException.DefaultMessage, ArchiveServiceLocator.NotEditableMessage(t.P("조각.7z.001")));
    }

    /// <summary>
    /// 계약(W2-06 검증 지적): 반디집 ZIP(0x7075 유니코드 경로 — 목록 이름이 원시 CP949 이름과 다름)을 화면에서 고쳐도 목록에 보이는 이름으로 된다 —
    /// 같은 이름을 넣으면 묻고([덮어쓰기]면 바꿈 — 두 개가 되지 않음), 지우기는 '찾지 못했어요' 없이 지운다.
    /// </summary>
    [Fact]
    public async Task 반디집_유니코드_경로_ZIP도_화면에서_고친다()
    {
        using var t = new TempDir("vnfd");
        var target = ArchiveEditTests.Prepare("nfd_bandizip_default", t);
        var (vm, ui) = await Open(target.Path);
        Assert.True(vm.CanEditArchive);
        string english = vm.Entries.Select(e => TreeAssert.NormalizeEntryPath(e.Path)).Single(k => k.EndsWith("/english.txt", StringComparison.Ordinal));
        string folder = EditPlanner<object>.ParentOf(english);
        int before = (await Svc.For(target.Path).ListEntriesAsync(target.Path)).Count;

        string src = t.Dir("넣을 것");
        File.WriteAllText(Path.Combine(src, "english.txt"), "새 영어 내용");
        ui.CollisionAnswers.Enqueue(new CollisionAnswer(CollisionChoice.Overwrite, false));
        bool added = false;
        await Run(async () => added = await vm.AddPathsToArchiveAsync(new[] { Path.Combine(src, "english.txt") }, folder), "같은 이름 넣기");
        Assert.True(added);
        Assert.Empty(ui.Errors);
        Assert.Equal(english, TreeAssert.NormalizeEntryPath(Assert.Single(ui.CollisionPrompts).TargetPath));
        var afterAdd = await Svc.For(target.Path).ListEntriesAsync(target.Path);
        Assert.Equal(before, afterAdd.Count);
        Assert.Single(afterAdd, e => TreeAssert.NormalizeEntryPath(e.Path) == english);

        ui.ChooseByLabel = "지우기";
        bool deleted = false;
        await Run(async () => deleted = await vm.DeleteEntriesAsync(new[] { EntryAt(vm, english) }), "지우기");
        Assert.True(deleted);
        Assert.Empty(ui.Errors);
        Assert.DoesNotContain(english, vm.Entries.Select(e => TreeAssert.NormalizeEntryPath(e.Path)));
        Assert.Equal(before - 1, (await Svc.For(target.Path).ListEntriesAsync(target.Path)).Count);
    }

    /// <summary>
    /// 명령 차례(_commandGate)를 잡은 명령줄 처리 안에서 끌어 놓기 → [이 압축 파일에 넣기]로 이어져도 멈추지 않는다
    /// (고치기도 같은 차례를 쓰지만 이미 잡은 흐름이면 다시 기다리지 않음).
    /// </summary>
    [Fact]
    public async Task 명령줄_경로_넣기는_차례를_다시_기다리지_않는다()
    {
        using var t = new TempDir("vgate");
        string zip = await MakeZip(t);
        var (vm, ui) = await Open(zip);
        ui.ChooseByLabel = "이 압축 파일에 넣기";
        string src = t.Dir("넣을 것");
        File.WriteAllText(Path.Combine(src, "명령줄.txt"), "c");

        await Run(() => vm.ProcessCommandLineAsync(new[] { Path.Combine(src, "명령줄.txt") }), "명령줄 경로");

        Assert.Empty(ui.Errors);
        Assert.Contains("명령줄.txt", await Files(zip));
    }

    /// <summary>지우기 확인 문구: 4개 이상이면 이름 셋 + '외 N개'.</summary>
    [Fact]
    public void 지우기_문구는_이름_셋까지()
    {
        var entries = Enumerable.Range(1, 5).Select(i => new ArchiveEntry { Path = $"f{i}.txt" }).ToList();
        string m = MainViewModel.DeleteQuestion(entries);
        Assert.StartsWith("5개를 압축 파일에서 지울까요?", m);
        Assert.Contains("- f3.txt", m);
        Assert.DoesNotContain("- f4.txt", m);
        Assert.Contains("외 2개", m);
        Assert.Contains("되돌릴 수 없어요", m);
    }
}
