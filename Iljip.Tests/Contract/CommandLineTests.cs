using System.Diagnostics;
using Iljip.Tests.TestSupport;
using Iljip.ViewModels;

namespace Iljip.Tests.Contract;

/// <summary>
/// [범위 12] 명령줄(셸 확장 우클릭 메뉴가 넘기는 인자) 처리. 해석 자체는 <see cref="CommandLineParserTests"/>(순수 함수).
/// W1-D 부터 창 띄우기가 IUserInteraction 으로 빠져, 가짜 창(<see cref="FakeUserInteraction"/>)을 넣으면 '--extract-here'·'--extract-to-folder'
/// (진행 창·암호 창 포함)도 창 없이 시험한다. 실제 WPF 창(new MainViewModel())을 쓰는 테스트는 창이 뜨지 않는 경로만이다.
/// --extract(폴더 고르기)·--compress 의 실제 창 모양과 셸 메뉴는 사람이 확인(MANUAL_CHECKLIST).
/// 다른 프로그램(배치·자동화)이 부르는 '--extract-to 창 없이 풀기'(App.RunHeadlessExtractAsync)는 실제 Iljip.exe 를 띄워 확인한다.
/// </summary>
public class CommandLineTests
{
    /// <summary>MainViewModel.ProcessCommandLineAsync 를 시간 제한(60초)을 걸고 스레드 풀에서 부른다(<see cref="VmRunner"/>).</summary>
    private static Task RunVm(MainViewModel vm, string[] args) =>
        VmRunner.Run(() => vm.ProcessCommandLineAsync(args), $"ProcessCommandLineAsync({string.Join(" ", args)})");

    /// <summary>MessageBox 가 뜨는 실패 경로로 빠지지 않게, 열기 전에 서비스로 먼저 읽혀지는지 확인한다.</summary>
    private static async Task<string> SafeArchive(string id)
    {
        var fx = FixtureCatalog.Get(id);
        await Svc.For(fx.FullPath).ListEntriesAsync(fx.FullPath);   // 여기서 실패하면 화면 경로를 타지 않고 테스트 실패
        return fx.FullPath;
    }

    /// <summary>
    /// 계약: 우클릭 '"이름"\ 에 풀기'(--extract-to-folder)는 압축 확장자를 통째로 뗀 이름의 폴더에 푼다
    /// ('자료.tar.gz' → '자료', '견적.7z.001' → '견적' — 예전엔 '자료.tar'·'견적.7z' 폴더). W2-01: .tar.gz 는 안쪽 tar 내용까지 펼친다.
    /// ('--extract-here' 는 main 계약대로 압축 파일이 있는 폴더에 바로 푼다 — 아래 extract_here는_압축파일이_있는_폴더에_바로_푼다)
    /// </summary>
    [Theory]
    [InlineData("targz_default", "자료.tar.gz", "자료")]
    [InlineData("7z_split", "견적.7z.001", "견적")]
    public async Task extract_to_folder는_압축_확장자를_뗀_폴더에_푼다(string id, string name, string folder)
    {
        var fx = FixtureCatalog.Get(id);
        using var tmp = new TempDir("here");
        string ap = tmp.P(name);
        if (fx.SplitVolumes.Count == 0)
            File.Copy(fx.FullPath, ap);
        else
            foreach (var v in fx.SplitVolumes)   // 조각마다 같은 새 이름으로('견적.7z.001'·'.002'·'.003')
                File.Copy(Path.Combine(FixtureCatalog.Root, v.Replace('/', Path.DirectorySeparatorChar)), tmp.P(name[..^".001".Length] + v[^4..]));
        var ui = new FakeUserInteraction();
        var vm = new MainViewModel(ui);
        await RunVm(vm, new[] { "--extract-to-folder", ap });
        Assert.Empty(ui.Errors);
        TreeAssert.ExtractedTreeEquals(tmp.P(folder), fx.Expected, context: name);
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
    [InlineData("--extract-to-folder", "없음")]
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

    // ───────────── 우클릭 '여기에 풀기'·'"이름"\ 에 풀기'(I0-1) — 가짜 창으로 ─────────────

    /// <summary>작업 폴더에 픽스처 사본을 두고 그 경로를 돌려준다(원본 픽스처 폴더에는 풀지 않음).</summary>
    private static string CopyFixture(TempDir tmp, string id, string? name = null)
    {
        var fx = FixtureCatalog.Get(id);
        string work = tmp.Dir("작업 폴더");
        string dst = Path.Combine(work, name ?? Path.GetFileName(fx.FullPath));
        File.Copy(fx.FullPath, dst);
        return dst;
    }

    private static ExpectedEntry FileEntry(string root, string file) =>
        new(Path.GetRelativePath(root, file).Replace('\\', '/'), new FileInfo(file).Length, TreeAssert.Sha256(file), false);

    /// <summary>'--extract-here' = 압축 파일이 있는 폴더에 바로 푼다(반디집 '여기에 풀기'). '이름' 폴더를 만들지 않는다.</summary>
    [Fact]
    public async Task extract_here는_압축파일이_있는_폴더에_바로_푼다()
    {
        var fx = FixtureCatalog.Get("zip_default");
        using var tmp = new TempDir("xh");
        string zip = CopyFixture(tmp, "zip_default");
        string work = Path.GetDirectoryName(zip)!;
        var ui = new FakeUserInteraction();
        var vm = new MainViewModel(ui);

        await RunVm(vm, new[] { "--extract-here", zip });

        Assert.Empty(ui.Errors);
        Assert.Empty(ui.Warnings);
        Assert.Empty(ui.PasswordPrompts);
        Assert.Single(ui.ProgressSessions);
        Assert.Equal($"압축 해제 완료: {work}", vm.StatusText);
        Assert.False(Directory.Exists(Path.Combine(work, "zip_default")), "예전처럼 '이름' 폴더가 생김");
        TreeAssert.ExtractedTreeEquals(work, fx.Expected.Append(FileEntry(work, zip)));
    }

    /// <summary>'--extract-here' 를 두 번 하면 같은 이름 파일은 '이름 (1).ext' 로 비켜 쓴다(기존 규칙 — 원본 비파괴).</summary>
    [Fact]
    public async Task extract_here를_두번_하면_파일은_비켜_쓴다()
    {
        using var tmp = new TempDir("xh2");
        string zip = CopyFixture(tmp, "zip_default");
        string work = Path.GetDirectoryName(zip)!;
        await RunVm(new MainViewModel(new FakeUserInteraction()), new[] { "--extract-here", zip });
        var ui = new FakeUserInteraction();
        await RunVm(new MainViewModel(ui), new[] { "--extract-here", zip });

        Assert.Empty(ui.Errors);
        Assert.True(File.Exists(Path.Combine(work, "일집_샘플", "견적서_2026.txt")));
        Assert.True(File.Exists(Path.Combine(work, "일집_샘플", "견적서_2026 (1).txt")), "두 번째 해제가 비켜 쓰지 않음");
    }

    /// <summary>'--extract-to-folder' = '부모\이름' 폴더에. 한 번 더 하면 '이름 (2)' 새 폴더(기존 폴더에 합치지 않음).</summary>
    [Fact]
    public async Task extract_to_folder는_이름_폴더에_풀고_다시_하면_이름_2()
    {
        var fx = FixtureCatalog.Get("zip_default");
        using var tmp = new TempDir("xf");
        string zip = CopyFixture(tmp, "zip_default");
        string work = Path.GetDirectoryName(zip)!;

        var ui = new FakeUserInteraction();
        await RunVm(new MainViewModel(ui), new[] { "--extract-to-folder", zip });
        Assert.Empty(ui.Errors);
        TreeAssert.ExtractedTreeEquals(Path.Combine(work, "zip_default"), fx.Expected);

        var ui2 = new FakeUserInteraction();
        var vm2 = new MainViewModel(ui2);
        await RunVm(vm2, new[] { "--extract-to-folder", zip });
        Assert.Empty(ui2.Errors);
        Assert.Equal($"압축 해제 완료: {Path.Combine(work, "zip_default (2)")}", vm2.StatusText);
        TreeAssert.ExtractedTreeEquals(Path.Combine(work, "zip_default (2)"), fx.Expected);
        // 첫 폴더는 그대로(합쳐 풀었다면 '(1)' 파일이 생겼을 것)
        TreeAssert.ExtractedTreeEquals(Path.Combine(work, "zip_default"), fx.Expected);
    }

    /// <summary>
    /// 여러 압축을 골라 '각각 이름 폴더에 풀기' 하면 명령이 거의 동시에 들어온다(파일마다 Iljip.exe → 단일 인스턴스 파이프).
    /// 같은 VM 에 동시에 넣어도 하나씩 차례로 모두 풀린다(겹쳐 돌면 '열렸나' 확인이 엇갈려 하나가 말없이 빠질 수 있었다).
    /// </summary>
    [Fact]
    public async Task 동시에_들어온_이름_폴더_풀기는_모두_풀린다()
    {
        var zipFx = FixtureCatalog.Get("zip_default");
        var tarFx = FixtureCatalog.Get("tar_default");
        using var tmp = new TempDir("xfm");
        string zip = CopyFixture(tmp, "zip_default");
        string tar = CopyFixture(tmp, "tar_default");
        string sameName = CopyFixture(tmp, "tar_default", "zip_default.tar");   // 'zip_default.zip' 과 같은 폴더 이름 'zip_default' 을 고름
        string work = Path.GetDirectoryName(zip)!;
        var ui = new FakeUserInteraction();
        var vm = new MainViewModel(ui);

        await VmRunner.Run(() => Task.WhenAll(
            Task.Run(() => vm.ProcessCommandLineAsync(new[] { "--extract-to-folder", zip })),
            Task.Run(() => vm.ProcessCommandLineAsync(new[] { "--extract-to-folder", tar })),
            Task.Run(() => vm.ProcessCommandLineAsync(new[] { "--extract-to-folder", sameName }))), "동시 명령 3개");

        Assert.Empty(ui.Errors);
        Assert.Equal(3, ui.ProgressSessions.Count);
        TreeAssert.ExtractedTreeEquals(Path.Combine(work, "tar_default"), tarFx.Expected);
        // 같은 이름을 고른 둘은 먼저 처리된 쪽이 'zip_default', 다음이 'zip_default (2)'(차례는 들어온 순서라 정해지지 않음)
        var folders = new[] { "zip_default", "zip_default (2)" }.Select(f => Path.Combine(work, f)).ToList();
        Assert.All(folders, f => Assert.True(Directory.Exists(f), $"폴더 없음: {f}"));
        Assert.False(Directory.Exists(Path.Combine(work, "zip_default (3)")));
        var exps = new[] { zipFx.Expected, tarFx.Expected };
        foreach (var f in folders)
            Assert.True(exps.Any(e => Matches(f, e)), $"{f}: 풀린 내용이 zip·tar 기대 어느 쪽과도 다름(합쳐 풀렸거나 빠짐)");
    }

    private static bool Matches(string dest, IEnumerable<ExpectedEntry> expected)
    {
        try { TreeAssert.ExtractedTreeEquals(dest, expected); return true; }
        catch (Xunit.Sdk.XunitException) { return false; }
    }

    /// <summary>
    /// 'x.tar.gz' 는 'x' 폴더(예전 '--extract-here' 는 'x.tar' 폴더). W2-01 부터 안에는 안쪽 tar 의 내용이 바로 펼쳐진다
    /// (예전엔 GZ 처리기가 꺼낸 안쪽 tar 하나 — '.tar' 를 또 풀어야 했다). '.tar' 파일은 남지 않는다.
    /// </summary>
    [Fact]
    public async Task extract_to_folder는_tar_gz를_x_폴더에_푼다()
    {
        var fx = FixtureCatalog.Get("targz_default");
        using var tmp = new TempDir("xfg");
        string arc = CopyFixture(tmp, "targz_default", "x.tar.gz");
        string work = Path.GetDirectoryName(arc)!;
        var ui = new FakeUserInteraction();

        await RunVm(new MainViewModel(ui), new[] { "--extract-to-folder", arc });

        Assert.Empty(ui.Errors);
        Assert.False(Directory.Exists(Path.Combine(work, "x.tar")), "예전 규칙('x.tar' 폴더)으로 풀림");
        TreeAssert.ExtractedTreeEquals(Path.Combine(work, "x"), fx.Expected, context: "x.tar.gz");
        Assert.DoesNotContain(TreeAssert.Files(Path.Combine(work, "x")), f => f.EndsWith(".tar", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// 암호 압축: 가짜 암호 창에 맞는 암호를 주면 이름 폴더에 풀린다(첫 시도는 암호 없이 → '암호가 걸린 압축 파일이에요' 로 물음).
    /// 반디집이 한글 암호로 만든 ZIP(AES·ZipCrypto, W1-C B3)도 화면 흐름으로 확인 — W1-C README 가 'IUserInteraction 이 들어오면 붙일 것'이라 한 VM 테스트.
    /// </summary>
    [Theory]
    [InlineData("zip_aes256", FixtureCatalog.DefaultPassword)]
    [InlineData("zip_aes256_korean_pw", FixtureCatalog.KoreanPassword)]
    [InlineData("zip_zipcrypto_korean_pw", FixtureCatalog.KoreanPassword)]
    public async Task extract_to_folder는_암호를_묻고_푼다(string id, string password)
    {
        var fx = FixtureCatalog.Get(id);
        using var tmp = new TempDir("xfp");
        string zip = CopyFixture(tmp, id);
        string work = Path.GetDirectoryName(zip)!;
        var ui = new FakeUserInteraction(password);

        await RunVm(new MainViewModel(ui), new[] { "--extract-to-folder", zip });

        var prompt = Assert.Single(ui.PasswordPrompts);
        Assert.Equal(Iljip.Services.PasswordPromptReason.Required, prompt.Reason);
        Assert.Equal(Path.GetFileName(zip), prompt.ArchiveName);
        Assert.Equal(Iljip.Services.PasswordRequiredException.DefaultMessage, prompt.Message);
        Assert.Empty(ui.Errors);
        TreeAssert.ExtractedTreeEquals(Path.Combine(work, id), fx.Expected);
    }

    /// <summary>틀린 암호를 넣으면 '암호가 맞지 않아요'로 다시 묻고, 맞는 암호면 풀린다. 틀린 시도는 아무것도 남기지 않는다.</summary>
    [Fact]
    public async Task 암호를_틀리면_다시_묻고_맞으면_풀린다()
    {
        var fx = FixtureCatalog.Get("zip_aes256");
        using var tmp = new TempDir("xfw");
        string zip = CopyFixture(tmp, "zip_aes256");
        string work = Path.GetDirectoryName(zip)!;
        var ui = new FakeUserInteraction("틀린암호", FixtureCatalog.DefaultPassword);

        await RunVm(new MainViewModel(ui), new[] { "--extract-to-folder", zip });

        Assert.Equal(new[] { Iljip.Services.PasswordPromptReason.Required, Iljip.Services.PasswordPromptReason.Wrong },
            ui.PasswordPrompts.Select(p => p.Reason).ToArray());
        Assert.Equal(Iljip.Services.WrongPasswordException.DefaultMessage, ui.PasswordPrompts[1].Message);
        Assert.Empty(ui.Errors);
        TreeAssert.ExtractedTreeEquals(Path.Combine(work, "zip_aes256"), fx.Expected);
        Assert.False(Directory.Exists(Path.Combine(work, "zip_aes256 (2)")), "틀린 시도가 폴더를 남겨 번호가 밀림");
    }

    /// <summary>암호 창을 취소하면 아무것도 남지 않는다 — 만들었던 '이름' 폴더도 지운다(W1-C 되돌림).</summary>
    [Fact]
    public async Task extract_to_folder에서_암호를_취소하면_폴더도_안_남는다()
    {
        using var tmp = new TempDir("xfc");
        string zip = CopyFixture(tmp, "zip_aes256");
        string work = Path.GetDirectoryName(zip)!;
        var ui = new FakeUserInteraction();   // 암호 창 = 취소
        var vm = new MainViewModel(ui);

        await RunVm(vm, new[] { "--extract-to-folder", zip });

        Assert.Single(ui.PasswordPrompts);
        Assert.Equal("압축 해제 취소됨", vm.StatusText);
        Assert.Equal(new[] { Path.GetFileName(zip) }, Directory.EnumerateFileSystemEntries(work).Select(Path.GetFileName).ToArray());
    }

    // ───────────── 파일 이름까지 암호가 걸린 7z(헤더 암호, I1-7 화면 쪽) — W1-D 1차 검증 회귀 ─────────────

    /// <summary>
    /// 반디집 '파일 이름 암호화' 7z 를 우클릭으로 풀면: 열기 단계에서 암호를 한 번 묻고(문구 '파일 이름까지 암호가 걸려 있어요…'),
    /// 그 암호로 풀어 다시 묻지 않는다. W1-D 처음엔 '열기에 성공해야 푼다'로 바뀌어 오류 창만 뜨고 어느 경로로도 못 풀었다(회귀).
    /// </summary>
    [Theory]
    [InlineData("--extract-to-folder", "7z_header_encrypted", FixtureCatalog.DefaultPassword)]
    [InlineData("--extract-to-folder", "7z_header_encrypted_korean_pw", FixtureCatalog.KoreanPassword)]
    [InlineData("--extract-here", "7z_header_encrypted", FixtureCatalog.DefaultPassword)]
    [InlineData("--extract-here", "7z_header_encrypted_korean_pw", FixtureCatalog.KoreanPassword)]
    public async Task 헤더암호_7z도_우클릭으로_암호를_한번_묻고_풀린다(string verb, string id, string password)
    {
        var fx = FixtureCatalog.Get(id);
        using var tmp = new TempDir("xh7");
        string arc = CopyFixture(tmp, id);
        string work = Path.GetDirectoryName(arc)!;
        var ui = new FakeUserInteraction(password);
        var vm = new MainViewModel(ui);

        await RunVm(vm, new[] { verb, arc });

        Assert.Empty(ui.Errors);
        Assert.Empty(ui.Warnings);
        var prompt = Assert.Single(ui.PasswordPrompts);
        Assert.Equal(Iljip.Services.PasswordPromptReason.ListLocked, prompt.Reason);   // W2-02(I1-7): 목록까지 잠긴 압축 전용 이유
        Assert.Equal(Iljip.Services.PasswordRequiredException.HeaderMessage, prompt.Message);
        Assert.Equal(arc, vm.CurrentArchivePath);
        if (verb == "--extract-here")
            TreeAssert.ExtractedTreeEquals(work, fx.Expected.Append(FileEntry(work, arc)));
        else
            TreeAssert.ExtractedTreeEquals(Path.Combine(work, id), fx.Expected);
    }

    /// <summary>헤더 암호 7z 를 열 때 틀린 암호면 '암호가 맞지 않아요'로 다시 묻고, 취소하면 아무것도 안 한다(오류 창·폴더 없음).</summary>
    [Fact]
    public async Task 헤더암호_7z_열기에서_틀리면_다시_묻고_취소하면_풀지_않는다()
    {
        using var tmp = new TempDir("xh7c");
        string arc = CopyFixture(tmp, "7z_header_encrypted");
        string work = Path.GetDirectoryName(arc)!;
        var ui = new FakeUserInteraction("틀린암호");   // 두 번째 창은 취소
        var vm = new MainViewModel(ui);

        await RunVm(vm, new[] { "--extract-to-folder", arc });

        Assert.Equal(new[] { Iljip.Services.PasswordPromptReason.ListLocked, Iljip.Services.PasswordPromptReason.Wrong },
            ui.PasswordPrompts.Select(p => p.Reason).ToArray());
        Assert.Equal(Iljip.Services.WrongPasswordException.DefaultMessage + Iljip.Services.WrongPasswordException.HeaderNote, ui.PasswordPrompts[1].Message);
        Assert.Contains("맞지 않아요", Assert.Single(ui.Warnings));   // W2-02(I1-7): 틀린 암호 경고 한 번
        Assert.Empty(ui.Errors);
        Assert.Empty(ui.ProgressSessions);   // 풀기를 시작하지 않음
        Assert.Equal("열기를 취소했어요", vm.StatusText);
        Assert.True(vm.IsEmptyState);
        Assert.Equal(new[] { Path.GetFileName(arc) }, Directory.EnumerateFileSystemEntries(work).Select(Path.GetFileName).ToArray());
    }

    /// <summary>
    /// [W2-01 (f)] 7z.dll 로 바뀐 뒤에도 I0-1 흐름 그대로 — '여기에 풀기'(--extract-here)로 헤더 암호 7z 를 풀면 암호를 한 번 묻고,
    /// 틀리면 '암호가 맞지 않아요(손상일 수도)'로 다시 묻고, 맞으면 더 묻지 않고 그 자리에 풀린다. 틀린 뒤 취소하면 오류 창 없이 아무것도 남지 않는다.
    /// </summary>
    [Fact]
    public async Task 헤더암호_7z_여기에_풀기는_7z_dll로도_틀리면_다시_묻고_취소하면_아무것도_없다()
    {
        Assert.True(Iljip.Services.SevenZipLibrary.IsAvailable, "7z.dll 경로를 시험해야 함");
        var fx = FixtureCatalog.Get("7z_header_encrypted");
        var expectedReasons = new[] { Iljip.Services.PasswordPromptReason.ListLocked, Iljip.Services.PasswordPromptReason.Wrong };   // W2-02(I1-7)

        using var tmp = new TempDir("xh7w");
        string arc = CopyFixture(tmp, "7z_header_encrypted");
        string work = Path.GetDirectoryName(arc)!;
        var ui = new FakeUserInteraction("틀린암호", fx.Password);
        await RunVm(new MainViewModel(ui), new[] { "--extract-here", arc });
        Assert.Equal(expectedReasons, ui.PasswordPrompts.Select(p => p.Reason).ToArray());
        Assert.Equal(Iljip.Services.WrongPasswordException.DefaultMessage + Iljip.Services.WrongPasswordException.HeaderNote, ui.PasswordPrompts[1].Message);
        Assert.Empty(ui.Errors);
        TreeAssert.ExtractedTreeEquals(work, fx.Expected.Append(FileEntry(work, arc)));

        using var tmp2 = new TempDir("xh7x");
        string arc2 = CopyFixture(tmp2, "7z_header_encrypted");
        string work2 = Path.GetDirectoryName(arc2)!;
        var ui2 = new FakeUserInteraction("틀린암호");   // 두 번째 창은 취소
        await RunVm(new MainViewModel(ui2), new[] { "--extract-here", arc2 });
        Assert.Equal(expectedReasons, ui2.PasswordPrompts.Select(p => p.Reason).ToArray());
        Assert.Empty(ui2.Errors);
        Assert.Equal(new[] { Path.GetFileName(arc2) }, Directory.EnumerateFileSystemEntries(work2).Select(Path.GetFileName).ToArray());
    }

    /// <summary>
    /// 열기(--open·경로만 = 더블클릭·끌어 놓기)도 헤더 암호 7z 면 암호를 묻고 목록을 보인다. 열 때 받은 암호는 기억해서,
    /// 이어서 풀 때([압축 풀기]·선택 항목 풀기) 다시 묻지 않는다.
    /// </summary>
    [Theory]
    [InlineData("--open")]
    [InlineData(null)]
    public async Task 헤더암호_7z를_열면_암호를_묻고_목록을_보이며_풀때_다시_묻지_않는다(string? verb)
    {
        var fx = FixtureCatalog.Get("7z_header_encrypted");
        using var tmp = new TempDir("xh7o");
        string arc = CopyFixture(tmp, "7z_header_encrypted");
        var ui = new FakeUserInteraction(FixtureCatalog.DefaultPassword);
        var vm = new MainViewModel(ui);

        await RunVm(vm, verb is null ? new[] { arc } : new[] { verb, arc });

        Assert.Single(ui.PasswordPrompts);
        Assert.Empty(ui.Errors);
        Assert.Equal(arc, vm.CurrentArchivePath);
        Assert.Equal(15, vm.Entries.Count);
        Assert.Equal("15개 항목", vm.StatusText);

        string dest = tmp.P("풀 곳");
        Assert.True(await RunVmResult(() => vm.ExtractToFolderAsync(arc, dest)), "열 때 받은 암호로 풀리지 않음");
        Assert.Single(ui.PasswordPrompts);   // 다시 묻지 않음
        TreeAssert.ExtractedTreeEquals(dest, fx.Expected);
    }

    /// <summary>다른 압축을 열면 앞 압축의 암호는 잊는다(그 압축을 풀 때 다시 묻는다).</summary>
    [Fact]
    public async Task 다른_압축을_열면_기억한_암호를_잊는다()
    {
        using var tmp = new TempDir("xh7f");
        string arc = CopyFixture(tmp, "7z_header_encrypted");
        string zip = await SafeArchive("zip_default");
        var ui = new FakeUserInteraction(FixtureCatalog.DefaultPassword, FixtureCatalog.DefaultPassword);
        var vm = new MainViewModel(ui);

        await RunVm(vm, new[] { "--open", arc });
        await RunVm(vm, new[] { "--open", zip });
        Assert.Equal(zip, vm.CurrentArchivePath);

        Assert.True(await RunVmResult(() => vm.ExtractToFolderAsync(arc, tmp.P("풀 곳"))));
        Assert.Equal(2, ui.PasswordPrompts.Count);   // 열 때 한 번 + 지금 열린 압축이 아니라 풀 때 다시 한 번
    }

    private static async Task<bool> RunVmResult(Func<Task<bool>> action)
    {
        bool result = false;
        await VmRunner.Run(async () => result = await action(), "VM 호출");
        return result;
    }

    // ───────────── 암호 창은 [취소]할 때까지(W1-D 1차 검증 — 예전엔 세 번째 암호를 써 보지 않음) ─────────────

    /// <summary>
    /// 암호 창은 몇 번이고 다시 뜨고, 마지막으로 넣은 암호도 써 본다. 예전엔 창을 세 번 띄우고도 세 번째 암호는 시도하지 않은 채
    /// '비밀번호가 3회 모두 일치하지 않아…'로 끝났다(그래서 세 번째에 맞게 넣어도 실패).
    /// </summary>
    [Theory]
    [InlineData(2)]
    [InlineData(4)]
    public async Task 틀린_암호_뒤에_넣은_맞는_암호는_몇_번째든_풀린다(int wrongCount)
    {
        var fx = FixtureCatalog.Get("zip_aes256_korean_pw");
        using var tmp = new TempDir("xfr");
        string zip = CopyFixture(tmp, "zip_aes256_korean_pw");
        string work = Path.GetDirectoryName(zip)!;
        var answers = Enumerable.Range(1, wrongCount).Select(i => (string?)$"틀림{i}").Append(FixtureCatalog.KoreanPassword).ToArray();
        var ui = new FakeUserInteraction(answers);

        await RunVm(new MainViewModel(ui), new[] { "--extract-to-folder", zip });

        Assert.Equal(wrongCount + 1, ui.PasswordPrompts.Count);
        Assert.Equal(Iljip.Services.PasswordPromptReason.Required, ui.PasswordPrompts[0].Reason);
        Assert.All(ui.PasswordPrompts.Skip(1), p => Assert.Equal(Iljip.Services.PasswordPromptReason.Wrong, p.Reason));
        Assert.Empty(ui.Warnings);
        Assert.Empty(ui.Errors);
        TreeAssert.ExtractedTreeEquals(Path.Combine(work, "zip_aes256_korean_pw"), fx.Expected);
    }

    /// <summary>틀린 암호를 여러 번 넣다가 취소하면 '취소됨'으로 끝나고(경고 창 없음) 아무것도 남지 않는다.</summary>
    [Fact]
    public async Task 틀린_암호를_넣다가_취소하면_아무것도_안_남는다()
    {
        using var tmp = new TempDir("xfr2");
        string zip = CopyFixture(tmp, "zip_aes256");
        string work = Path.GetDirectoryName(zip)!;
        var ui = new FakeUserInteraction("틀림1", "틀림2", "틀림3");   // 네 번째 창은 취소
        var vm = new MainViewModel(ui);

        await RunVm(vm, new[] { "--extract-to-folder", zip });

        Assert.Equal(4, ui.PasswordPrompts.Count);
        Assert.Empty(ui.Warnings);
        Assert.Equal("압축 해제 취소됨", vm.StatusText);
        Assert.Equal(new[] { Path.GetFileName(zip) }, Directory.EnumerateFileSystemEntries(work).Select(Path.GetFileName).ToArray());
    }

    // ───────────── '"이름"\ 에 풀기'를 끝내지 못했을 때 빈 폴더 정리(W1-D 1차 검증) ─────────────

    /// <summary>폴더 안에 큰 파일 하나짜리 ZIP(취소가 첫 파일을 쓰는 도중에 걸리게 4 MiB 난수).</summary>
    private static async Task<string> MakeBigZip(TempDir tmp, string name)
    {
        string src = tmp.Dir("원본", "큰 자료");
        var data = new byte[4 * 1024 * 1024];
        new Random(42).NextBytes(data);
        File.WriteAllBytes(Path.Combine(src, "큰.bin"), data);
        string zip = Path.Combine(tmp.Dir("작업 폴더"), name);
        await new Iljip.Services.ZipArchiveService().CompressAsync(new[] { src }, zip,
            new Iljip.Models.CompressionOptions { Level = Iljip.Models.CompressionLevelOption.Store });
        return zip;
    }

    /// <summary>
    /// '"이름"\ 에 풀기'를 첫 파일을 쓰는 도중 취소하면 빈 '이름' 폴더(안에 빈 하위 폴더뿐)를 남기지 않는다 — 그래서 다시 풀면 '(2)' 로 밀리지 않는다.
    /// 같은 이름 폴더가 이미 있어 '이름 (2)' 에 풀다 취소해도 '이름 (2)' 만 지우고 원래 폴더는 그대로.
    /// </summary>
    [Fact]
    public async Task 이름_폴더_풀기를_도중에_취소하면_빈_폴더를_남기지_않는다()
    {
        using var tmp = new TempDir("xfcan");
        string zip = await MakeBigZip(tmp, "큰.zip");
        string work = Path.GetDirectoryName(zip)!;

        var ui = new FakeUserInteraction { CancelOnFirstBytes = true };
        var vm = new MainViewModel(ui);
        await RunVm(vm, new[] { "--extract-to-folder", zip });
        Assert.Equal("압축 해제 취소됨", vm.StatusText);
        Assert.Empty(ui.Errors);
        Assert.Equal(new[] { "큰.zip" }, Directory.EnumerateFileSystemEntries(work).Select(Path.GetFileName).ToArray());

        // 다시 풀면 '큰'(번호 밀림 없음)
        await RunVm(new MainViewModel(new FakeUserInteraction()), new[] { "--extract-to-folder", zip });
        Assert.True(File.Exists(Path.Combine(work, "큰", "큰 자료", "큰.bin")));
        Assert.False(Directory.Exists(Path.Combine(work, "큰 (2)")));

        // 이제 '큰' 이 있으니 '큰 (2)' 에 풀다 취소 → '큰 (2)' 만 사라지고 '큰' 은 그대로
        await RunVm(new MainViewModel(new FakeUserInteraction { CancelOnFirstBytes = true }), new[] { "--extract-to-folder", zip });
        Assert.False(Directory.Exists(Path.Combine(work, "큰 (2)")), "취소한 '큰 (2)' 가 빈 채로 남음");
        Assert.True(File.Exists(Path.Combine(work, "큰", "큰 자료", "큰.bin")));
    }

    /// <summary>
    /// 손상(CRC 오류)으로 멈췄을 때: 맞게 풀린 파일이 하나도 없으면 '이름' 폴더를 지우고, 앞서 풀린 파일이 있으면 남긴다(기존 계약 — 깨진 그 파일만 지움).
    /// </summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task 이름_폴더_풀기가_손상으로_멈추면_파일이_없을_때만_폴더를_지운다(bool breakFirst)
    {
        using var tmp = new TempDir("xfbad");
        string src = tmp.Dir("원본", "자료");
        File.WriteAllText(Path.Combine(src, "가.txt"), "FIRST-FILE-CONTENT-0123456789");
        File.WriteAllText(Path.Combine(src, "나.txt"), "SECOND-FILE-CONTENT-0123456789");
        string zip = Path.Combine(tmp.Dir("작업 폴더"), "손상.zip");
        await new Iljip.Services.ZipArchiveService().CompressAsync(new[] { src }, zip,
            new Iljip.Models.CompressionOptions { Level = Iljip.Models.CompressionLevelOption.Store });
        var bytes = File.ReadAllBytes(zip);
        int at = IndexOf(bytes, System.Text.Encoding.ASCII.GetBytes(breakFirst ? "FIRST-FILE" : "SECOND-FILE"));
        Assert.True(at > 0, "내용 위치를 못 찾음");
        bytes[at + 3] ^= 0x20;
        File.WriteAllBytes(zip, bytes);
        string work = Path.GetDirectoryName(zip)!;
        var ui = new FakeUserInteraction();

        await RunVm(new MainViewModel(ui), new[] { "--extract-to-folder", zip });

        Assert.Contains("CRC", Assert.Single(ui.Errors));
        string dest = Path.Combine(work, "손상");
        if (breakFirst)
            Assert.False(Directory.Exists(dest), "파일 하나 없는 '손상' 폴더가 남음");
        else
            Assert.Equal(new[] { "자료/가.txt" }, TreeAssert.Files(dest));
    }

    private static int IndexOf(byte[] haystack, byte[] needle)
    {
        for (int i = 0; i + needle.Length <= haystack.Length; i++)
            if (haystack.AsSpan(i, needle.Length).SequenceEqual(needle)) return i;
        return -1;
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

    /// <summary>
    /// 지원하지 않는 형식이면 종료 코드 2(파일은 있음) + stderr 영어 'Unsupported archive type: 확장자' — 재사용장치가 이 문구로 '압축 아님'을 가른다.
    /// (예전 픽스처 lzh_default 는 W2-01 에서 풀리게 돼 내용도 확장자도 모르는 파일로 바꿈)
    /// </summary>
    [IljipExeFact]
    public async Task 실행파일_extract_to_미지원형식은_2()
    {
        using var tmp = new TempDir("exe2");
        string junk = tmp.P("x.unknownext");
        File.WriteAllBytes(junk, Enumerable.Range(0, 3000).Select(i => (byte)(i * 7 + 3)).ToArray());
        string dest = tmp.P("out");
        var (code, err) = await RunIljipCapture(new[] { "--extract-to", junk, dest });
        Assert.Equal(2, code);
        Assert.Contains("Unsupported archive type: .unknownext", err);
        TreeAssert.NoFiles(dest);
    }

    /// <summary>
    /// [1차 W1-B 검증 지적] 속이 ZIP 인 문서(.xlsx·.docx·.hwpx)는 내용 판별로는 ZIP 이지만 압축이 아니다 — 창 없이 풀기는 예전처럼
    /// 종료 코드 2 + 'Unsupported archive type: .xlsx' 로 끝나고 아무것도 풀지 않는다(내용 판별을 넣은 뒤 0 으로 문서 속 XML 을 풀던 것).
    /// 이름만 바꾼 진짜 압축(.dat 인 ZIP 등)은 계속 내용대로 푼다.
    /// </summary>
    [IljipExeFact]
    public async Task 실행파일_extract_to_ZIP기반_문서는_미지원_2()
    {
        using var tmp = new TempDir("exedoc");
        foreach (var name in new[] { "보고서.xlsx", "계약서.docx", "공문.hwpx" })
        {
            string doc = tmp.P(name);
            RawZip.Write(doc, RawZip.Utf8("[Content_Types].xml", "<Types/>"), RawZip.Utf8("docProps/app.xml", "<Properties/>"));
            string dest = tmp.P("out_" + name);
            var (code, err) = await RunIljipCapture(new[] { "--extract-to", doc, dest });
            Assert.Equal(2, code);
            Assert.Contains("Unsupported archive type: " + Path.GetExtension(name), err);
            TreeAssert.NoFiles(dest);
        }

        string renamed = tmp.P("자료.dat");
        RawZip.Write(renamed, RawZip.Utf8("a.txt", "zip inside"));
        string dest2 = tmp.P("out_dat");
        var (code2, err2) = await RunIljipCapture(new[] { "--extract-to", renamed, dest2 });
        Assert.True(code2 == 0, err2);
        Assert.Equal(new[] { "a.txt" }, TreeAssert.Files(dest2));
    }

    /// <summary>[W2-01] 창 없이 풀기도 새 형식(7z.dll·압축된 tar)을 푼다 — 재사용장치가 쓰는 길.</summary>
    [IljipExeFact]
    public async Task 실행파일_extract_to는_7z_lzh_iso_tgz도_푼다()
    {
        foreach (var id in new[] { "7z_default", "lzh_default", "iso_default", "tgz_default" })
        {
            var fx = FixtureCatalog.Get(id);
            using var tmp = new TempDir("exe7");
            string dest = tmp.P("out");
            var (code, err) = await RunIljipCapture(new[] { "--extract-to", fx.FullPath, dest });
            Assert.True(code == 0, $"{id}: 종료 코드 {code} — {err}");
            TreeAssert.ExtractedTreeEquals(dest, fx.Expected, context: id);
        }
    }

    /// <summary>
    /// [W2-01, 1차 W1-B 검증 지적 (b)] 끝이 잘린 xz·tar.xz·LZH·RAR 를 창 없이 풀면 30초 안에 끝나고(예전 xz 는 CPU 100% 로 영영 안 끝남)
    /// 종료 코드 1 + 쉬운 한국어 한 줄('잘려'). 재사용장치가 '압축 아님'으로 가르는 말은 들어 있지 않고 대상에 아무것도 남지 않는다.
    /// </summary>
    [IljipExeFact]
    public async Task 실행파일_extract_to_잘린_압축은_30초_안에_1로_끝난다()
    {
        using var tmp = new TempDir("exetr");
        string src = Path.Combine(tmp.Dir("src"), "자료.bin");
        var data = new byte[300_000];
        new Random(3).NextBytes(data.AsSpan(0, 150_000));
        File.WriteAllBytes(src, data);
        string tar = tmp.P("묶음.tar");
        await new Iljip.Services.TarArchiveService().CompressAsync(new[] { src }, tar);
        var cases = new List<(string Name, byte[] Bytes)>();
        var xz = File.ReadAllBytes(SevenZipTestArchives.Xz(src, tmp.P("full.bin.xz")));
        cases.Add(("자료.bin.xz", xz[..(xz.Length - 35)]));
        var txz = File.ReadAllBytes(SevenZipTestArchives.Xz(tar, tmp.P("full.tar.xz")));
        cases.Add(("묶음.tar.xz", txz[..(txz.Length - 33)]));
        var lzh = File.ReadAllBytes(FixtureCatalog.Get("lzh_default").FullPath);
        cases.Add(("자료.lzh", lzh[..(lzh.Length - 1)]));
        var rar = RawRar.Rar4(new[] { ("a.txt", System.Text.Encoding.ASCII.GetBytes("aaaa")), ("b.txt", System.Text.Encoding.ASCII.GetBytes("bbbb")) });
        cases.Add(("자료.rar", rar.Bytes[..rar.BlockEnds[1]]));   // 머리 사이(1차 W1-B 의 '남은 한계')

        foreach (var (name, bytes) in cases)
        {
            string arc = tmp.P(name);
            File.WriteAllBytes(arc, bytes);
            string dest = tmp.P("out_" + name);
            var sw = Stopwatch.StartNew();
            var (code, err) = await RunIljipCapture(new[] { "--extract-to", arc, dest });
            Assert.True(sw.Elapsed < TimeSpan.FromSeconds(30), $"{name}: {sw.Elapsed.TotalSeconds:F0}초 걸림");
            Assert.True(code == 1, $"{name}: 종료 코드 {code} — {err}");
            string line = err.Trim();
            Assert.DoesNotContain('\n', line);
            Assert.Contains("잘려", line);
            foreach (var bad in new[] { "압축 파일이 아닌", "압축파일이 아닌", "not archive", "not an archive", "unsupported archive" })
                Assert.DoesNotContain(bad, line, StringComparison.OrdinalIgnoreCase);
            Assert.False(Directory.Exists(dest) && Directory.EnumerateFileSystemEntries(dest).Any(), $"{name}: 대상에 무언가 남음");
        }
    }

    /// <summary>
    /// [W2-10 검증 지적 2차] 끝이 잘린 일집 자동 풀림 exe(꼬리·ZIP 끝 머리가 없음 — 덜 받아진 첨부)를 창 없이 풀면 '지원하지 않는 형식'(종료 코드 2 ·
    /// 'Unsupported archive type: .exe')이 아니라 손상으로 끝난다 — 종료 코드 1 + 쉬운 한국어 한 줄 '압축 파일의 끝부분이 잘려 있어요…'(재사용장치가 '압축 아님'으로 가르는 말 없음)·
    /// 대상에 아무것도 없음.
    /// 멀쩡한 것은 0 으로 풀고, 스텁만 있는 exe(붙은 압축 없음)는 예전처럼 2.
    /// </summary>
    [IljipExeFact]
    public async Task 실행파일_extract_to_끝이_잘린_자동풀림은_손상으로_1()
    {
        using var tmp = new TempDir("exesfxcut");
        string src = tmp.Dir("src", "한글");
        var data = new byte[300_000];
        new Random(8).NextBytes(data);
        File.WriteAllBytes(Path.Combine(src, "가.bin"), data);
        File.WriteAllText(Path.Combine(src, "나.txt"), string.Concat(Enumerable.Repeat("나", 50_000)));
        string good = tmp.P("good.exe");
        await Iljip.Services.SfxBuilder.CreateAsync(new[] { src }, good);
        byte[] all = File.ReadAllBytes(good);
        string cut = tmp.P("cut.exe");
        File.WriteAllBytes(cut, all[..^5000]);
        string bare = tmp.P("stubonly.exe");
        File.Copy(Iljip.Services.SfxBuilder.StubPath, bare);

        string okDest = tmp.P("out_good");
        var (okCode, okErr) = await RunIljipCapture(new[] { "--extract-to", good, okDest });
        Assert.True(okCode == 0, okErr);
        Assert.Equal(data, File.ReadAllBytes(Path.Combine(okDest, "한글", "가.bin")));

        string dest = tmp.P("out_cut");
        var (code, err) = await RunIljipCapture(new[] { "--extract-to", cut, dest });
        Assert.True(code == 1, $"종료 코드 {code} — {err}");
        string line = err.Trim();
        Assert.DoesNotContain('\n', line);
        Assert.Contains("끝부분이 잘려 있어요", line);
        foreach (var bad in new[] { "압축 파일이 아닌", "압축파일이 아닌", "not archive", "not an archive", "unsupported archive" })
            Assert.DoesNotContain(bad, line, StringComparison.OrdinalIgnoreCase);
        Assert.False(Directory.Exists(dest) && Directory.EnumerateFileSystemEntries(dest).Any(), "대상에 무언가 남음");

        var (bareCode, bareErr) = await RunIljipCapture(new[] { "--extract-to", bare, tmp.P("out_bare") });
        Assert.Equal(2, bareCode);
        Assert.Contains("Unsupported archive type: .exe", bareErr);
    }

    /// <summary>
    /// 암호가 필요한 파일(헤더 암호 7z)은 종료 코드 1 + 쉬운 한국어 문구. 재사용장치가 '압축 아님'으로 가르는 말
    /// ('압축 파일이 아닌'·'unsupported archive')은 들어 있지 않다. 파일은 남지 않는다.
    /// </summary>
    [IljipExeFact]
    public async Task 실행파일_extract_to_암호필요는_1이고_압축아님으로_분류되지_않는다()
    {
        var fx = FixtureCatalog.Get("7z_header_encrypted");
        using var tmp = new TempDir("exe1");
        string dest = tmp.P("out");
        var (code, err) = await RunIljipCapture(new[] { "--extract-to", fx.FullPath, dest });
        Assert.Equal(1, code);
        Assert.Contains("암호", err);
        Assert.DoesNotContain("압축 파일이 아닌", err);
        Assert.DoesNotContain("unsupported archive", err, StringComparison.OrdinalIgnoreCase);
        TreeAssert.NoFiles(dest);
    }

    /// <summary>암호 ZIP 은 창 없이 풀기로 풀 수 없다(암호를 넘길 방법이 없음) — 종료 코드 1, 파일·새 폴더가 남지 않는다(W1-C 판정은 만들기 전).</summary>
    [IljipExeFact]
    public async Task 실행파일_extract_to_암호파일은_1_그리고_파일없음()
    {
        var fx = FixtureCatalog.Get("zip_aes256");
        using var tmp = new TempDir("exe4");
        string dest = tmp.P("out");
        int code = await RunIljip(new[] { "--extract-to", fx.FullPath, dest });
        Assert.Equal(1, code);
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

    private static async Task<int> RunIljip(string[] args) => (await RunIljipCapture(args)).Code;

    /// <summary>Iljip.exe 를 창 없이 실행하고 (종료 코드, stderr) 를 돌려준다. stderr 는 재사용장치처럼 UTF-8 → CP949 순으로 읽는다.</summary>
    private static async Task<(int Code, string StdErr)> RunIljipCapture(string[] args)
    {
        // 안전장치: --extract-to 의 압축 파일이 없으면 Iljip 이 창(GUI)으로 뜨므로 절대 그렇게 부르지 않는다.
        Assert.True(args.Length == 3 && File.Exists(args[1]), "창 없이 풀기 조건이 아님 — 창이 뜰 수 있어 실행하지 않음");
        var psi = new ProcessStartInfo(IljipExe.Path!) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true };
        foreach (var a in args) psi.ArgumentList.Add(a);
        using var p = Process.Start(psi)!;
        using var errBytes = new MemoryStream();
        var readErr = p.StandardError.BaseStream.CopyToAsync(errBytes);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        try
        {
            await p.WaitForExitAsync(cts.Token);
            await readErr;
        }
        catch (OperationCanceledException)
        {
            try { p.Kill(entireProcessTree: true); } catch { /* 이미 끝남 */ }
            Assert.Fail("Iljip.exe 가 60초 안에 끝나지 않음(창이 떴을 수 있음)");
        }
        return (p.ExitCode, DecodeToolOutput(errBytes.ToArray()));
    }

    private static string DecodeToolOutput(byte[] raw)
    {
        try
        {
            return new System.Text.UTF8Encoding(false, throwOnInvalidBytes: true).GetString(raw);
        }
        catch (System.Text.DecoderFallbackException)
        {
            return System.Text.Encoding.GetEncoding(949).GetString(raw);
        }
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
