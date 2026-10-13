using Iljip.Services;
using Iljip.Tests.TestSupport;
using Iljip.ViewModels;

namespace Iljip.Tests.Contract;

/// <summary>
/// [W2-05 I1-6] 탐색기 우클릭 풀기('여기에 풀기'·'"이름"\ 에 풀기')는 메인 창 없이 진행 창만 띄워 풀고, 끝나면 설정(extract.openFolderAfter)에 따라
/// 새로 만든 폴더를 탐색기로 연다('여기에 풀기'는 이미 보던 폴더라 열지 않음). 오류는 쉬운 한국어 창 하나(영어 엔진 문구 없음).
/// App 은 <see cref="ParsedCommand.ShowMainWindow"/> 가 false 인 명령을 <see cref="MainViewModel.ProcessShellCommandAsync"/> 로 넘긴다
/// (App 쪽 창 띄우기 순서는 <see cref="AppStartupContractTests"/> 가 소스로, 실제 화면은 MANUAL_CHECKLIST 12절).
/// 셸(탐색기 열기)·휴지통·설정은 가짜(<see cref="TestServices"/>) — 진짜 탐색기는 절대 부르지 않는다.
/// </summary>
public class ShellExtractFlowTests
{
    private static Task RunShell(MainViewModel vm, params string[] args) =>
        VmRunner.Run(() => vm.ProcessShellCommandAsync(args), $"ProcessShellCommandAsync({string.Join(" ", args)})");

    private static string CopyFixture(TempDir tmp, string id, string? name = null)
    {
        var fx = FixtureCatalog.Get(id);
        string work = tmp.Dir("작업 폴더");
        string dst = Path.Combine(work, name ?? Path.GetFileName(fx.FullPath));
        File.Copy(fx.FullPath, dst);
        return dst;
    }

    /// <summary>'"이름"\ 에 풀기': 메인 창을 쓰지 않고(보던 것 그대로) 진행 창 하나로 풀고, 새 폴더를 탐색기로 한 번 연다.</summary>
    [Fact]
    public async Task 이름_폴더에_풀면_진행_창만_쓰고_새_폴더를_한번_연다()
    {
        var fx = FixtureCatalog.Get("zip_default");
        using var tmp = new TempDir("sxf");
        string zip = CopyFixture(tmp, "zip_default");
        string dest = Path.Combine(Path.GetDirectoryName(zip)!, "zip_default");
        var ui = new FakeUserInteraction();
        var svc = new TestServices();
        var vm = new MainViewModel(ui, null, svc.Services);

        Assert.False(CommandLineParser.Parse(new[] { "--extract-to-folder", zip }).ShowMainWindow);
        await RunShell(vm, "--extract-to-folder", zip);

        Assert.Empty(ui.Errors);
        Assert.Empty(ui.Warnings);
        Assert.Empty(ui.Summaries);   // 하나면 요약 창 없음
        Assert.Single(ui.ProgressSessions);
        TreeAssert.ExtractedTreeEquals(dest, fx.Expected);
        Assert.Equal(new[] { (dest, false) }, svc.Shell.OpenedFolders);
        Assert.Empty(svc.Shell.OpenedFiles);
        Assert.True(vm.IsEmptyState, "우클릭 풀기가 메인 창의 보기를 바꿈(압축을 열었음)");
        Assert.Null(vm.CurrentArchivePath);
        Assert.Equal($"압축 해제 완료: {dest}", vm.StatusText);
    }

    /// <summary>설정에서 '풀고 나서 폴더 열기'를 끄면 열지 않는다.</summary>
    [Fact]
    public async Task 폴더_열기를_끄면_열지_않는다()
    {
        using var tmp = new TempDir("sxo");
        string zip = CopyFixture(tmp, "zip_default");
        var ui = new FakeUserInteraction();
        var svc = TestServices.With(e => e.OpenFolderAfter = false);
        await RunShell(new MainViewModel(ui, null, svc.Services), "--extract-to-folder", zip);

        Assert.Empty(ui.Errors);
        Assert.True(Directory.Exists(Path.Combine(Path.GetDirectoryName(zip)!, "zip_default")));
        Assert.Empty(svc.Shell.OpenedFolders);
    }

    /// <summary>'여기에 풀기'는 압축 파일이 있는(이미 보던) 폴더에 푸므로 폴더를 열지 않는다(설정이 켜져 있어도).</summary>
    [Fact]
    public async Task 여기에_풀기는_폴더를_열지_않는다()
    {
        var fx = FixtureCatalog.Get("zip_default");
        using var tmp = new TempDir("sxh");
        string zip = CopyFixture(tmp, "zip_default");
        string work = Path.GetDirectoryName(zip)!;
        var ui = new FakeUserInteraction();
        var svc = new TestServices();
        await RunShell(new MainViewModel(ui, null, svc.Services), "--extract-here", zip);

        Assert.Empty(ui.Errors);
        Assert.Empty(svc.Shell.OpenedFolders);
        Assert.True(File.Exists(Path.Combine(work, "일집_샘플", "견적서_2026.txt")));
        Assert.Equal(fx.Expected.Count(e => !e.IsDir) + 1, TreeAssert.Files(work).Count);
    }

    /// <summary>
    /// 손상·잘린 압축을 우클릭으로 풀면: 오류 창 하나(쉬운 한국어), 영어 엔진 문구('CRC'·'Data error'·'Unexpected end' 등) 없음, 요약 창·폴더 열기 없음,
    /// 임시 파일(덮어쓰기용 '.~'·7z.dll 임시 폴더) 남지 않음. 새 '이름' 폴더는 파일이 하나도 안 풀렸으면 지운다(앞서 다 풀린 파일은 남김 — 기존 계약).
    /// </summary>
    [Theory]
    [InlineData("garbage.zip")]
    [InlineData("truncated.zip")]
    [InlineData("truncated.7z")]
    [InlineData("truncated.rar")]
    [InlineData("truncated.tar.gz")]
    [InlineData("truncated.egg")]
    public async Task 손상된_압축은_한국어_오류_하나로_끝난다(string name)
    {
        using var tmp = new TempDir("sxbad");
        string work = tmp.Dir("작업 폴더");
        string arc = Path.Combine(work, name);
        MakeBroken(tmp, name, arc);
        var ui = new FakeUserInteraction();
        var svc = new TestServices();

        await RunShell(new MainViewModel(ui, null, svc.Services), "--extract-to-folder", arc);

        string error = Assert.Single(ui.Errors);
        Assert.Matches("[가-힣]", error);
        Assert.StartsWith($"‘{name}’을 풀지 못했어요.", error);
        foreach (var bad in new[] { "CRC", "Data error", "Unexpected end", "Exception", "Invalid", "stream", "Header" })
            Assert.DoesNotContain(bad, error, StringComparison.OrdinalIgnoreCase);
        foreach (var marker in new[] { "압축 파일이 아닌", "압축파일이 아닌", "not archive", "not an archive", "unsupported archive" })
            Assert.DoesNotContain(marker, error, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(ui.Warnings);
        Assert.Empty(ui.Summaries);
        Assert.Empty(svc.Shell.OpenedFolders);
        Assert.Empty(svc.RecycleBin.Sent);
        Assert.True(File.Exists(arc));
        CollisionTests.NoLeftovers(work, name);
        string dest = Path.Combine(work, ArchiveNaming.FolderNameFor(arc));
        if (Directory.Exists(dest))
            Assert.NotEmpty(TreeAssert.Files(dest));   // 남은 폴더는 앞서 다 풀린 파일이 있을 때만
        if (name == "garbage.zip")
            Assert.Equal(new[] { name }, Directory.EnumerateFileSystemEntries(work).Select(Path.GetFileName).ToArray());
    }

    private static void MakeBroken(TempDir tmp, string name, string arc)
    {
        static void Half(string from, string to)
        {
            var b = File.ReadAllBytes(from);
            File.WriteAllBytes(to, b[..(b.Length / 2)]);
        }
        switch (name)
        {
            case "garbage.zip":
                File.WriteAllBytes(arc, Enumerable.Range(0, 5000).Select(i => (byte)(i * 31 + 7)).ToArray());
                break;
            case "truncated.zip": Half(FixtureCatalog.Get("zip_default").FullPath, arc); break;
            case "truncated.7z": Half(FixtureCatalog.Get("7z_default").FullPath, arc); break;
            case "truncated.tar.gz": Half(FixtureCatalog.Get("targz_default").FullPath, arc); break;
            case "truncated.rar": Half(SyntheticArchives.Build("rar5", tmp.Dir("rar")).Path, arc); break;
            default: Half(SyntheticArchives.Build("egg", tmp.Dir("egg")).Path, arc); break;
        }
    }

    /// <summary>우클릭 '여기에 풀기'에서도 같은 이름이면 묻는다(설정 기본 '묻기') — 고른 답대로.</summary>
    [Fact]
    public async Task 여기에_풀기도_같은_이름이면_묻는다()
    {
        using var tmp = new TempDir("sxq");
        string zip = CopyFixture(tmp, "zip_default");
        string work = Path.GetDirectoryName(zip)!;
        string mine = Path.Combine(work, "일집_샘플", "견적서_2026.txt");
        Directory.CreateDirectory(Path.GetDirectoryName(mine)!);
        File.WriteAllText(mine, "사용자 파일");
        var ui = new FakeUserInteraction();
        ui.CollisionAnswers.Enqueue(new(CollisionChoice.Skip, false));

        await RunShell(new MainViewModel(ui, null, new TestServices().Services), "--extract-here", zip);

        var q = Assert.Single(ui.CollisionPrompts);
        Assert.Equal(mine, q.TargetPath);
        Assert.Equal("사용자 파일", File.ReadAllText(mine));
        Assert.False(File.Exists(Path.Combine(work, "일집_샘플", "견적서_2026 (1).txt")));
        Assert.Empty(ui.Errors);
    }

    /// <summary>설정 '같은 이름이 있을 때 = 덮어쓰기'면 묻지 않고 덮어쓴다(우클릭 풀기에도 적용).</summary>
    [Fact]
    public async Task 같은_이름_설정이_덮어쓰기면_묻지_않는다()
    {
        var fx = FixtureCatalog.Get("zip_default");
        using var tmp = new TempDir("sxw");
        string zip = CopyFixture(tmp, "zip_default");
        string work = Path.GetDirectoryName(zip)!;
        string mine = Path.Combine(work, "일집_샘플", "견적서_2026.txt");
        Directory.CreateDirectory(Path.GetDirectoryName(mine)!);
        File.WriteAllText(mine, "사용자 파일");
        var ui = new FakeUserInteraction();

        await RunShell(new MainViewModel(ui, null, TestServices.With(e => e.OnCollision = CollisionPolicy.Overwrite).Services), "--extract-here", zip);

        Assert.Empty(ui.CollisionPrompts);
        Assert.Equal(fx.Expected.Single(e => e.Path == "일집_샘플/견적서_2026.txt").Sha256, TreeAssert.Sha256(mine));
    }

    /// <summary>파일 이름까지 암호가 걸린 7z 를 우클릭으로 풀면 암호를 한 번('파일 목록까지…') 묻고 푼다(메인 창 없이도 W2-02 흐름).</summary>
    [Fact]
    public async Task 목록까지_잠긴_7z도_우클릭으로_암호를_한번_묻고_푼다()
    {
        var fx = FixtureCatalog.Get("7z_header_encrypted");
        using var tmp = new TempDir("sxp");
        string arc = CopyFixture(tmp, "7z_header_encrypted");
        var ui = new FakeUserInteraction(fx.Password);
        var svc = new TestServices();

        await RunShell(new MainViewModel(ui, null, svc.Services), "--extract-to-folder", arc);

        var p = Assert.Single(ui.PasswordPrompts);
        Assert.Equal(PasswordPromptReason.ListLocked, p.Reason);
        Assert.Equal(ListPasswordRequiredException.ListMessage, p.Message);
        Assert.Empty(ui.Errors);
        string dest = Path.Combine(Path.GetDirectoryName(arc)!, "7z_header_encrypted");
        TreeAssert.ExtractedTreeEquals(dest, fx.Expected);
        Assert.Single(svc.Shell.OpenedFolders);
    }

    /// <summary>암호 창을 취소하면 조용히 끝난다(오류 창·폴더 열기 없음, 새 폴더 남지 않음).</summary>
    [Fact]
    public async Task 암호_창을_취소하면_조용히_끝나고_아무것도_남지_않는다()
    {
        using var tmp = new TempDir("sxpc");
        string zip = CopyFixture(tmp, "zip_aes256");
        string work = Path.GetDirectoryName(zip)!;
        var ui = new FakeUserInteraction();   // 암호 창 = 취소
        var svc = new TestServices();

        await RunShell(new MainViewModel(ui, null, svc.Services), "--extract-to-folder", zip);

        Assert.Single(ui.PasswordPrompts);
        Assert.Empty(ui.Errors);
        Assert.Empty(svc.Shell.OpenedFolders);
        Assert.Equal(new[] { Path.GetFileName(zip) }, Directory.EnumerateFileSystemEntries(work).Select(Path.GetFileName).ToArray());
    }

    /// <summary>우클릭 풀기 동사가 아니면(열기 등) 메인 창 흐름 그대로(<see cref="MainViewModel.ProcessCommandLineAsync"/>).</summary>
    [Fact]
    public async Task 열기는_메인_창_흐름_그대로()
    {
        var fx = FixtureCatalog.Get("zip_default");
        var ui = new FakeUserInteraction();
        var vm = new MainViewModel(ui, null, new TestServices().Services);
        await RunShell(vm, "--open", fx.FullPath);
        Assert.Equal(fx.FullPath, vm.CurrentArchivePath);
        Assert.Empty(ui.ProgressSessions);
    }

    /// <summary>메인 창이 없어 조용히 끝나면 아무 일도 없던 것처럼 보인다 — 우클릭한 파일이 그새 없어졌으면 쉬운 말로 알린다.</summary>
    [Fact]
    public async Task 없는_파일을_우클릭으로_풀면_알린다()
    {
        using var tmp = new TempDir("sxnf");
        string gone = tmp.P("없는 파일.zip");
        var ui = new FakeUserInteraction();
        var svc = new TestServices();

        await RunShell(new MainViewModel(ui, null, svc.Services), "--extract-to-folder", gone);

        string e = Assert.Single(ui.Errors);
        Assert.Contains("찾을 수 없어요", e);
        Assert.Contains(gone, e);
        Assert.Empty(ui.ProgressSessions);
        Assert.Empty(svc.Shell.OpenedFolders);
    }

    /// <summary>탐색기를 못 띄워도(셸 오류) 풀기는 끝난 것 — 오류 창 없이 끝난다.</summary>
    [Fact]
    public async Task 결과_폴더를_못_열어도_오류_창은_없다()
    {
        using var tmp = new TempDir("sxop");
        string zip = CopyFixture(tmp, "zip_default");
        var ui = new FakeUserInteraction();
        var svc = new TestServices();
        var vm = new MainViewModel(ui, null, new Iljip.Services.AppServices
        {
            Shell = new ThrowingShell(),
            Settings = svc.Settings,
            RecycleBin = svc.RecycleBin,
        });

        await RunShell(vm, "--extract-to-folder", zip);

        Assert.Empty(ui.Errors);
        Assert.Empty(ui.Warnings);
        Assert.True(Directory.Exists(Path.Combine(Path.GetDirectoryName(zip)!, "zip_default")));
    }

    private sealed class ThrowingShell : IShellLauncher
    {
        public void OpenFile(string path) => throw new InvalidOperationException("셸 없음");
        public void OpenFolder(string path, bool select = false) => throw new System.ComponentModel.Win32Exception(2);
    }
}
