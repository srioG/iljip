using System.Diagnostics;
using Iljip.Tests.TestSupport;
using Iljip.ViewModels;

namespace Iljip.Tests.Contract;

/// <summary>
/// [범위 12] 명령줄(셸 확장 우클릭 메뉴가 넘기는 인자) 해석.
/// MainViewModel.ProcessCommandLineAsync 는 해석과 '창 띄우기'(폴더 선택·진행 창·MessageBox·저장 창)가 한 함수에 섞여 있어,
/// 창이 뜨지 않는 경로만 여기서 시험한다(--open 성공, 경로만 준 경우, 인자 없음/잘못된 인자).
/// --extract / --extract-here / --compress 와 실패 경로(MessageBox)는 사람이 확인(MANUAL_CHECKLIST) — 리팩터링 제안은 README.
/// 다른 프로그램(배치·자동화)이 부르는 '--extract-to 창 없이 풀기'(App.RunHeadlessExtractAsync)는 실제 Iljip.exe 를 띄워 확인한다.
/// </summary>
public class CommandLineTests
{
    /// <summary>
    /// MainViewModel.ProcessCommandLineAsync 를 시간 제한(60초)을 걸고 부른다. 화면 코드가 바뀌어 MessageBox·폴더 선택 창 같은
    /// 모달 창으로 빠지면 테스트 호스트가 영영 멈추므로, 스레드 풀에서 돌리고 기다리다 시간이 넘으면 실패로 끝낸다
    /// (떠 버린 창은 테스트 프로세스가 끝날 때 같이 닫힌다).
    /// </summary>
    private static async Task RunVm(MainViewModel vm, string[] args)
    {
        var run = Task.Run(() => vm.ProcessCommandLineAsync(args));
        try
        {
            await run.WaitAsync(TimeSpan.FromSeconds(60));
        }
        catch (TimeoutException)
        {
            Assert.Fail($"ProcessCommandLineAsync({string.Join(" ", args)}) 가 60초 안에 안 끝남 — MessageBox 등 창이 떠서 멈춘 것으로 보임");
        }
    }

    /// <summary>MessageBox 가 뜨는 실패 경로로 빠지지 않게, 열기 전에 서비스로 먼저 읽혀지는지 확인한다.</summary>
    private static async Task<string> SafeArchive(string id)
    {
        var fx = FixtureCatalog.Get(id);
        await Svc.For(fx.FullPath).ListEntriesAsync(fx.FullPath);   // 여기서 실패하면 화면 경로를 타지 않고 테스트 실패
        return fx.FullPath;
    }

    [Fact]
    public async Task 인자가_없으면_아무것도_안_한다()
    {
        var vm = new MainViewModel();
        await RunVm(vm, Array.Empty<string>());
        Assert.True(vm.IsEmptyState);
        Assert.Equal("준비됨", vm.StatusText);
    }

    [Fact]
    public async Task open은_압축파일을_열어_목록과_폴더트리를_채운다()
    {
        string zip = await SafeArchive("zip_default");
        var vm = new MainViewModel();
        await RunVm(vm, new[] { "--open", zip });

        Assert.Equal(zip, vm.CurrentArchivePath);
        Assert.True(vm.IsArchiveMode);
        Assert.Equal(15, vm.Entries.Count);
        Assert.Equal("15개 항목", vm.StatusText);
        Assert.Equal("zip_default.zip - 일집", vm.WindowTitle);
        var root = Assert.Single(vm.FolderTree);
        Assert.Equal("zip_default.zip", root.Name);
        Assert.Same(root, vm.SelectedFolder);
        // 루트에는 최상위 폴더 하나만 보인다
        var top = Assert.Single(vm.VisibleEntries);
        Assert.True(top.IsDirectory);
        Assert.Equal("일집_샘플", top.Path);
        // 하위 폴더로 들어가면 직속 항목만
        vm.NavigateToFolder("일집_샘플/하위폴더");
        Assert.Equal(new[] { "일집_샘플/하위폴더/2단계", "일집_샘플/하위폴더/보고서 최종.txt" },
            vm.VisibleEntries.Select(e => e.Path).ToArray());
    }

    /// <summary>7z(폴더 끝 '/' 없음)도 같은 폴더 트리가 된다.</summary>
    [Fact]
    public async Task open은_7z도_같은_트리를_만든다()
    {
        string sz = await SafeArchive("7z_default");
        var vm = new MainViewModel();
        await RunVm(vm, new[] { "--open", sz });
        vm.NavigateToFolder("일집_샘플");
        var names = vm.VisibleEntries.Select(e => e.Name).ToList();
        Assert.Equal(new[] { "data", "빈폴더", "하위폴더" }, names.Take(3).ToArray());
        Assert.Contains("견적서_2026.txt", names);
    }

    /// <summary>키워드 없이 압축 파일 경로 하나만 주면(탐색기에서 더블클릭/드롭과 같음) 연다.</summary>
    [Fact]
    public async Task 경로만_주면_압축파일은_연다()
    {
        string tar = await SafeArchive("tar_default");
        var vm = new MainViewModel();
        await RunVm(vm, new[] { tar });
        Assert.Equal(tar, vm.CurrentArchivePath);
        Assert.Equal(15, vm.Entries.Count);
    }

    /// <summary>현재 동작 기록: 키워드는 대소문자를 가린다. '--OPEN' 은 키워드가 아니라 '경로 목록'으로 처리되는데, 결과적으로 압축 파일은 열린다.</summary>
    [Fact, Trait(Kinds.Key, Kinds.Record)]
    public async Task 대문자_키워드는_경로로_취급되어_결국_열린다()
    {
        string zip = await SafeArchive("zip_default");
        var vm = new MainViewModel();
        await RunVm(vm, new[] { "--OPEN", zip });
        Assert.Equal(zip, vm.CurrentArchivePath);
    }

    /// <summary>일반 파일·폴더 경로들을 주면 '압축 대기' 목록에 넣는다(창 없음).</summary>
    [Fact]
    public async Task 일반_파일과_폴더는_압축대기로_들어간다()
    {
        using var tmp = new TempDir("cli");
        string src = SampleTree.Create(tmp.Dir("src"));
        string file = Path.Combine(src, "English.TXT");
        var vm = new MainViewModel();
        await RunVm(vm, new[] { file, src, tmp.P("없는 경로") });

        Assert.True(vm.IsStagingMode);
        Assert.Equal(2, vm.Entries.Count);
        Assert.Equal("새 압축 (2개 항목) - 일집", vm.WindowTitle);
        Assert.Equal("압축 대기 2개 항목 — 압축 버튼을 눌러주세요", vm.StatusText);
        Assert.Contains(vm.Entries, e => e.Path == "English.TXT" && !e.IsDirectory && e.Size == new FileInfo(file).Length);
        Assert.Contains(vm.Entries, e => e.Path == SampleTree.RootName && e.IsDirectory);

        // 같은 경로를 다시 넣어도 중복되지 않는다
        await RunVm(vm, new[] { file, src });
        Assert.Equal(2, vm.Entries.Count);
    }

    /// <summary>파일이 없거나 인자가 모자라면 조용히 아무것도 안 한다(오류 창도 없음).</summary>
    [Theory]
    [InlineData("--open", "없음")]
    [InlineData("--extract", "없음")]
    [InlineData("--extract-here", "없음")]
    [InlineData("--extract-to", "없음")]
    [InlineData("--compress", null)]
    [InlineData("--open", null)]
    public async Task 대상이_없으면_아무것도_안_한다(string verb, string? arg)
    {
        using var tmp = new TempDir("cli0");
        var args = arg is null ? new[] { verb } : new[] { verb, tmp.P(arg + ".zip"), tmp.P("dest") };
        var vm = new MainViewModel();
        await RunVm(vm, args);
        Assert.True(vm.IsEmptyState);
        Assert.False(Directory.Exists(tmp.P("dest")));
    }

    // ───────────── 창 없이 풀기: Iljip.exe --extract-to <압축> <폴더> ─────────────

    /// <summary>실제 Iljip.exe 로 '--extract-to' 를 실행하면 창 없이 풀고 종료 코드 0.</summary>
    [IljipExeFact]
    public async Task 실행파일_extract_to는_창없이_풀고_0으로_끝난다()
    {
        var fx = FixtureCatalog.Get("zip_default");
        using var tmp = new TempDir("exe");
        string dest = tmp.P("새 폴더", "풀 곳");   // 없는 폴더 — 만들어서 풀어야 함
        int code = await RunIljip(new[] { "--extract-to", fx.FullPath, dest });
        Assert.Equal(0, code);
        TreeAssert.ExtractedTreeEquals(dest, fx.Expected);
    }

    /// <summary>지원하지 않는 형식이면 종료 코드 2(파일은 있음).</summary>
    [IljipExeFact]
    public async Task 실행파일_extract_to_미지원형식은_2()
    {
        var fx = FixtureCatalog.Get("lzh_default");
        using var tmp = new TempDir("exe2");
        string dest = tmp.P("out");
        int code = await RunIljip(new[] { "--extract-to", fx.FullPath, dest });
        Assert.Equal(2, code);
        TreeAssert.NoFiles(dest);
    }

    /// <summary>키워드 대소문자를 가리지 않는다(창 없이 풀기만 — 화면 쪽 switch 와 다름).</summary>
    [IljipExeFact]
    public async Task 실행파일_EXTRACT_TO_대문자도_된다()
    {
        var fx = FixtureCatalog.Get("tar_default");
        using var tmp = new TempDir("exe3");
        string dest = tmp.P("out");
        int code = await RunIljip(new[] { "--EXTRACT-TO", fx.FullPath, dest });
        Assert.Equal(0, code);
        TreeAssert.ExtractedTreeEquals(dest, fx.Expected);
    }

    private static async Task<int> RunIljip(string[] args)
    {
        // 안전장치: --extract-to 의 압축 파일이 없으면 Iljip 이 창(GUI)으로 뜨므로 절대 그렇게 부르지 않는다.
        Assert.True(args.Length == 3 && File.Exists(args[1]), "창 없이 풀기 조건이 아님 — 창이 뜰 수 있어 실행하지 않음");
        var psi = new ProcessStartInfo(IljipExe.Path!) { UseShellExecute = false, CreateNoWindow = true };
        foreach (var a in args) psi.ArgumentList.Add(a);
        using var p = Process.Start(psi)!;
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        try
        {
            await p.WaitForExitAsync(cts.Token);
        }
        catch (OperationCanceledException)
        {
            try { p.Kill(entireProcessTree: true); } catch { /* 이미 끝남 */ }
            Assert.Fail("Iljip.exe 가 60초 안에 끝나지 않음(창이 떴을 수 있음)");
        }
        return p.ExitCode;
    }
}

/// <summary>빌드된 Iljip.exe 위치. 테스트 출력 폴더 → 형제 프로젝트 출력 폴더 순으로 찾는다.</summary>
public static class IljipExe
{
    public static readonly string? Path = Find();

    private static string? Find()
    {
        string baseDir = AppContext.BaseDirectory.TrimEnd('\\', '/');
        string here = System.IO.Path.Combine(baseDir, "Iljip.exe");
        if (File.Exists(here) && File.Exists(System.IO.Path.Combine(baseDir, "Iljip.runtimeconfig.json"))) return here;

        // <출력 루트>/Iljip.Tests/bin/<구성>/<TFM>/ → <출력 루트>/Iljip/bin/<구성>/<TFM>/Iljip.exe
        var tfm = new DirectoryInfo(baseDir);
        var cfg = tfm.Parent;
        var root = cfg?.Parent?.Parent?.Parent;
        if (cfg is null || root is null) return null;
        string sibling = System.IO.Path.Combine(root.FullName, "Iljip", "bin", cfg.Name, tfm.Name, "Iljip.exe");
        return File.Exists(sibling) ? sibling : null;
    }
}

public sealed class IljipExeFactAttribute : FactAttribute
{
    public IljipExeFactAttribute()
    {
        if (IljipExe.Path is null) Skip = "빌드된 Iljip.exe 를 찾지 못함(테스트 출력 폴더·형제 프로젝트 출력 폴더)";
    }
}
