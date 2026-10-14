using System.Windows;
using Iljip.Models;
using Iljip.Services;
using Iljip.Tests.TestSupport;
using Iljip.ViewModels;

namespace Iljip.Tests.Contract;

/// <summary>
/// [W2-04 — I1-4] 목록에서 Ctrl+C → 탐색기에 붙여넣기. 고른 항목을 <c>temp\clip\guid\</c> 에 꺼내(진행 창·취소) 그 경로를 클립보드(CF_HDROP)에 넣는다.
/// 가짜 클립보드(<see cref="FakeClipboard"/>)로 창 없이 — 진짜 클립보드는 쓰지 않는다(넣을 데이터의 모양만 따로 확인).
/// </summary>
public class ClipboardCopyTests
{
    private static readonly FixtureInfo Zip = FixtureCatalog.Get("zip_default");

    [Fact]
    public async Task 고른_파일과_폴더를_꺼내_최상위_경로를_복사로_넣는다()
    {
        using var tmp = new TempDir("clip");
        using var _ = TestServices.UseAppRoot(tmp);
        var ts = new TestServices();
        var ui = new FakeUserInteraction();
        var vm = new MainViewModel(ui, services: ts.Services);
        string clipRoot = AppPaths.ClipRoot;
        await VmRunner.Run(async () =>
        {
            await vm.OpenArchiveAsync(Zip.FullPath);
            vm.NavigateToFolder("일집_샘플");
            var sel = vm.VisibleEntries.Where(e => e.Name is "하위폴더" or "readme.txt").ToList();
            Assert.True(await vm.CopyEntriesToClipboardAsync(sel));
        }, "Ctrl+C");

        var call = Assert.Single(ts.Clipboard.Calls);
        Assert.Equal(DragDropEffects.Copy, call.Effect);   // Preferred DropEffect = 복사
        Assert.Equal(2, call.Paths.Count);
        Assert.All(call.Paths, p => Assert.StartsWith(clipRoot + Path.DirectorySeparatorChar, p));
        string folder = call.Paths.Single(p => Path.GetFileName(p) == "하위폴더");
        string readme = call.Paths.Single(p => Path.GetFileName(p) == "readme.txt");
        Assert.True(Directory.Exists(folder));
        Assert.Equal(Zip.Expected.Single(e => e.Path == "일집_샘플/readme.txt").Sha256, TreeAssert.Sha256(readme));
        Assert.Equal(Zip.Expected.Single(e => e.Path == "일집_샘플/하위폴더/2단계/3단계/깊은 파일 [끝].txt").Sha256,
            TreeAssert.Sha256(Path.Combine(folder, "2단계", "3단계", "깊은 파일 [끝].txt")));
        // 고르지 않은 것은 꺼내지 않음
        Assert.False(File.Exists(Path.Combine(Path.GetDirectoryName(readme)!, "견적서_2026.txt")));
        Assert.Single(ui.ProgressSessions);
        Assert.Empty(ui.Errors);
        Assert.StartsWith("2개 항목을 복사했어요", vm.StatusText);

        // 붙여넣기가 늦을 수 있어 앱을 닫아도 사본은 남긴다(다음 시작 때 24시간 지난 것만 지움)
        vm.Shutdown();
        Assert.True(File.Exists(readme));
    }

    [Fact]
    public async Task 진행_창을_취소하면_클립보드도_사본도_없다()
    {
        using var tmp = new TempDir("clipc");
        using var _ = TestServices.UseAppRoot(tmp);
        var ts = new TestServices();
        var ui = new FakeUserInteraction { CancelProgress = true };
        var vm = new MainViewModel(ui, services: ts.Services);
        string clipRoot = AppPaths.ClipRoot;
        bool ok = true;
        await VmRunner.Run(async () =>
        {
            await vm.OpenArchiveAsync(Zip.FullPath);
            vm.NavigateToFolder("일집_샘플");
            ok = await vm.CopyEntriesToClipboardAsync(vm.VisibleEntries.ToList());
        }, "Ctrl+C 취소");

        Assert.False(ok);
        Assert.Empty(ts.Clipboard.Calls);
        Assert.False(Directory.Exists(clipRoot) && Directory.EnumerateFileSystemEntries(clipRoot).Any(), "취소했는데 사본이 남음");
        Assert.Equal("복사를 취소했어요", vm.StatusText);
        Assert.Empty(ui.Errors);
    }

    [Fact]
    public async Task 암호_ZIP은_한번_묻고_복사한다()
    {
        using var tmp = new TempDir("clippw");
        using var _ = TestServices.UseAppRoot(tmp);
        var fx = FixtureCatalog.Get("zip_aes256");
        var ts = new TestServices();
        var ui = new FakeUserInteraction(fx.Password);
        var vm = new MainViewModel(ui, services: ts.Services);
        await VmRunner.Run(async () =>
        {
            await vm.OpenArchiveAsync(fx.FullPath);
            vm.NavigateToFolder("일집_샘플/data");
            await vm.CopyEntriesToClipboardAsync(vm.VisibleEntries.ToList());
            await vm.CopyEntriesToClipboardAsync(vm.VisibleEntries.ToList());   // 두 번째는 묻지 않음
        }, "암호 Ctrl+C");

        Assert.Single(ui.PasswordPrompts);
        Assert.Equal(2, ts.Clipboard.Calls.Count);
        Assert.Equal(fx.Expected.Single(e => e.Path == "일집_샘플/data/sample.csv").Sha256, TreeAssert.Sha256(Assert.Single(ts.Clipboard.Calls[0].Paths)));
    }

    [Fact]
    public async Task 암호_창을_취소하면_클립보드에_넣지_않는다()
    {
        using var tmp = new TempDir("clippwc");
        using var _ = TestServices.UseAppRoot(tmp);
        var fx = FixtureCatalog.Get("zip_aes256");
        var ts = new TestServices();
        var vm = new MainViewModel(new FakeUserInteraction(), services: ts.Services);
        string clipRoot = AppPaths.ClipRoot;
        await VmRunner.Run(async () =>
        {
            await vm.OpenArchiveAsync(fx.FullPath);
            vm.NavigateToFolder("일집_샘플/data");
            await vm.CopyEntriesToClipboardAsync(vm.VisibleEntries.ToList());
        }, "암호 취소");
        Assert.Empty(ts.Clipboard.Calls);
        Assert.False(Directory.Exists(clipRoot) && Directory.EnumerateFileSystemEntries(clipRoot).Any());
    }

    [Fact]
    public async Task 인터넷에서_받은_압축이면_복사한_파일에도_표식을_붙인다()
    {
        using var tmp = new TempDir("clipzone");
        using var _ = TestServices.UseAppRoot(tmp);
        string arc = tmp.P("받은.zip");
        File.Copy(Zip.FullPath, arc);
        const string zone = "[ZoneTransfer]\r\nZoneId=3\r\n";
        File.WriteAllText(arc + ZoneIdentifier.StreamName, zone);
        var ts = new TestServices();
        var vm = new MainViewModel(new FakeUserInteraction(), services: ts.Services);
        await VmRunner.Run(async () =>
        {
            await vm.OpenArchiveAsync(arc);
            vm.NavigateToFolder("일집_샘플");
            await vm.CopyEntriesToClipboardAsync(vm.VisibleEntries.Where(e => e.Name is "하위폴더" or "readme.txt").ToList());
        }, "표식");
        var paths = Assert.Single(ts.Clipboard.Calls).Paths;
        Assert.Equal(zone, File.ReadAllText(paths.Single(p => p.EndsWith("readme.txt")) + ZoneIdentifier.StreamName));
        string deep = Path.Combine(paths.Single(p => p.EndsWith("하위폴더")), "보고서 최종.txt");
        Assert.Equal(zone, File.ReadAllText(deep + ZoneIdentifier.StreamName));
    }

    // ───────────────────── 디스크에서 이름이 바뀌는 항목(W2-04 1차 검증) ─────────────────────

    /// <summary>맥·리눅스에서 만든 압축처럼 Windows 가 그대로 못 쓰는 이름(':' '?' 장치 이름)이 든 압축 — 풀기 엔진이 디스크 이름을 바꾼다(EntryPaths).</summary>
    private static string MacZip(TempDir tmp) => TestZip.Create(tmp.P("맥에서.zip"),
        TestZip.Text("맥에서/회의록 10:00.txt", "회의"),
        TestZip.Text("맥에서/왜?.txt", "왜"),
        TestZip.Text("맥에서/CON.txt", "장치"),
        TestZip.Text("맥에서/자료:모음/안.txt", "안"),
        TestZip.Text("맥에서/보통.txt", "보통"));

    private static async Task<(MainViewModel Vm, FakeUserInteraction Ui, TestServices Ts)> OpenAt(string zip, string folder, ArchiveServiceLocator? locator = null)
    {
        var ts = new TestServices();
        var ui = new FakeUserInteraction();
        var vm = new MainViewModel(ui, locator, ts.Services);
        await VmRunner.Run(async () =>
        {
            Assert.True(await vm.OpenArchiveAsync(zip));
            vm.NavigateToFolder(folder);
        }, "열기");
        return (vm, ui, ts);
    }

    /// <summary>예전엔 클립보드 경로를 '꺼낸 곳\원래 이름' 으로 다시 만들어, 이름이 바뀐 항목은 꺼내 놓고도 '찾지 못했어요'로 실패했다.</summary>
    [Theory]
    [InlineData("회의록 10:00.txt", "회의록 10_00.txt", "회의")]
    [InlineData("왜?.txt", "왜_.txt", "왜")]
    [InlineData("CON.txt", "_CON.txt", "장치")]
    [InlineData("보통.txt", "보통.txt", "보통")]
    public async Task 디스크에서_이름이_바뀌는_파일도_복사된다(string name, string diskName, string content)
    {
        using var tmp = new TempDir("clipmac1");
        using var _ = TestServices.UseAppRoot(tmp);
        var (vm, ui, ts) = await OpenAt(MacZip(tmp), "맥에서");
        bool ok = false;
        await VmRunner.Run(async () => ok = await vm.CopyEntriesToClipboardAsync(vm.VisibleEntries.Where(e => e.Name == name).ToList()), "Ctrl+C");

        Assert.True(ok, string.Join(" / ", ui.Errors));
        Assert.Empty(ui.Errors);
        string path = Assert.Single(Assert.Single(ts.Clipboard.Calls).Paths);
        Assert.Equal(diskName, Path.GetFileName(path));
        Assert.Equal(content, File.ReadAllText(path));
    }

    [Fact]
    public async Task 디스크에서_이름이_바뀌는_폴더도_복사된다()
    {
        using var tmp = new TempDir("clipmac2");
        using var _ = TestServices.UseAppRoot(tmp);
        var (vm, ui, ts) = await OpenAt(MacZip(tmp), "맥에서");
        await VmRunner.Run(() => vm.CopyEntriesToClipboardAsync(vm.VisibleEntries.Where(e => e.Name == "자료:모음").ToList()), "Ctrl+C");

        Assert.Empty(ui.Errors);
        string dir = Assert.Single(Assert.Single(ts.Clipboard.Calls).Paths);
        Assert.Equal("자료_모음", Path.GetFileName(dir));
        Assert.Equal("안", File.ReadAllText(Path.Combine(dir, "안.txt")));
    }

    /// <summary>예전엔 여러 개를 고르면 이름이 바뀐 항목만 클립보드에서 말없이 빠지고 상태 줄은 '2개 항목을 복사했어요'(고른 것은 5개)였다.</summary>
    [Fact]
    public async Task 이름이_바뀌는_항목을_한꺼번에_골라도_빠짐없이_복사된다()
    {
        using var tmp = new TempDir("clipmac3");
        using var _ = TestServices.UseAppRoot(tmp);
        var (vm, ui, ts) = await OpenAt(MacZip(tmp), "맥에서");
        var all = vm.VisibleEntries.ToList();
        Assert.Equal(5, all.Count);
        await VmRunner.Run(() => vm.CopyEntriesToClipboardAsync(all), "Ctrl+C 전부");

        Assert.Empty(ui.Errors);
        var paths = Assert.Single(ts.Clipboard.Calls).Paths;
        Assert.Equal(new[] { "_CON.txt", "보통.txt", "왜_.txt", "자료_모음", "회의록 10_00.txt" }, paths.Select(Path.GetFileName).OrderBy(n => n, StringComparer.Ordinal));
        Assert.All(paths, p => Assert.True(File.Exists(p) || Directory.Exists(p), p));
        Assert.StartsWith("5개 항목을 복사했어요", vm.StatusText);
    }

    /// <summary>
    /// 리눅스에서 만든 압축엔 대소문자만 다른 두 파일이 있을 수 있다. 예전엔 열쇠를 대소문자 무시로 합쳐 하나만 클립보드에 넣었다.
    /// 이제 둘 다 넣고 내용도 제자리(뒤엣것은 풀기 엔진처럼 '이름 (1)').
    /// </summary>
    [Fact]
    public async Task 대소문자만_다른_두_파일을_같이_고르면_둘_다_제_내용으로_복사된다()
    {
        using var tmp = new TempDir("clipcase");
        using var _ = TestServices.UseAppRoot(tmp);
        string zip = TestZip.Create(tmp.P("리눅스.zip"), TestZip.Text("대소/Report.txt", "R1"), TestZip.Text("대소/report.txt", "R2"));
        var (vm, ui, ts) = await OpenAt(zip, "대소");
        Assert.Equal(2, vm.VisibleEntries.Count);
        await VmRunner.Run(async () =>
        {
            await vm.CopyEntriesToClipboardAsync(vm.VisibleEntries.ToList());
            await vm.CopyEntriesToClipboardAsync(vm.VisibleEntries.Where(e => e.Name == "report.txt").ToList());   // 뒤엣것만
        }, "Ctrl+C 대소문자");

        Assert.Empty(ui.Errors);
        var both = ts.Clipboard.Calls[0].Paths;
        Assert.Equal(2, both.Count);
        Assert.Equal(new[] { ("Report.txt", "R1"), ("report (1).txt", "R2") },
            both.Select(p => (Path.GetFileName(p), File.ReadAllText(p))).OrderBy(x => x.Item1, StringComparer.Ordinal));
        Assert.StartsWith("1개 항목을", vm.StatusText);
        Assert.Equal("R2", File.ReadAllText(Assert.Single(ts.Clipboard.Calls[1].Paths)));   // 고른 것의 내용(대소문자 무시로 같은 이름인 앞엣것이 아님)
    }

    /// <summary>꺼냈는데 고른 항목 중 하나의 자리를 못 찾으면 말없이 빼지 않고 알리고, 클립보드에는 아무것도 넣지 않는다(사본도 지움).</summary>
    [Fact]
    public async Task 꺼낸_자리를_못_찾은_항목이_있으면_알리고_넣지_않는다()
    {
        using var tmp = new TempDir("clipmiss");
        using var _ = TestServices.UseAppRoot(tmp);
        string zip = TestZip.Create(tmp.P("이상한엔진.dropzip"), TestZip.Text("폴더/보통.txt", "보통"), TestZip.Text("폴더/다른.txt", "다른"));
        var locator = new ArchiveServiceLocator();
        locator.Register(new RenamesAfterExtract(".dropzip", "보통.txt"));
        var (vm, ui, ts) = await OpenAt(zip, "폴더", locator);
        string clipRoot = AppPaths.ClipRoot;
        bool ok = true;
        await VmRunner.Run(async () => ok = await vm.CopyEntriesToClipboardAsync(vm.VisibleEntries.ToList()), "Ctrl+C");

        Assert.False(ok);
        Assert.Empty(ts.Clipboard.Calls);
        string err = Assert.Single(ui.Errors);
        Assert.StartsWith("복사하지 못했어요:", err);
        Assert.Contains("- 보통.txt", err);
        Assert.DoesNotContain("다른.txt", err);
        Assert.Equal("오류 발생", vm.StatusText);
        Assert.False(Directory.Exists(clipRoot) && Directory.EnumerateFileSystemEntries(clipRoot).Any(), "실패했는데 사본이 남음");
    }

    /// <summary>ZIP 으로 풀되, 다 푼 뒤 그 이름의 파일을 다른 이름으로 바꾸는 가짜 엔진(꺼낸 자리를 못 찾는 경우 흉내).</summary>
    private sealed class RenamesAfterExtract(string extension, string renameFile) : IArchiveService
    {
        private readonly ZipArchiveService _zip = new();

        public IReadOnlyList<string> SupportedExtensions => new[] { extension };

        public Task<IReadOnlyList<ArchiveEntry>> ListEntriesAsync(string archivePath, string? password = null, CancellationToken cancellationToken = default) =>
            _zip.ListEntriesAsync(archivePath, password, cancellationToken);

        public Task ExtractAsync(string archivePath, string destinationFolder, string? password = null, IProgress<ArchiveProgress>? progress = null, CancellationToken cancellationToken = default) =>
            _zip.ExtractAsync(archivePath, destinationFolder, password, progress, cancellationToken);

        public async Task ExtractEntriesAsync(string archivePath, string destinationFolder, IReadOnlyCollection<string> entryPaths, string? password = null,
            IProgress<ArchiveProgress>? progress = null, CancellationToken cancellationToken = default)
        {
            await _zip.ExtractEntriesAsync(archivePath, destinationFolder, entryPaths, password, progress, cancellationToken);
            foreach (var f in Directory.EnumerateFiles(destinationFolder, renameFile, SearchOption.AllDirectories).ToList())
                File.Move(f, f + ".다른이름");
        }

        public Task<CompressResult> CompressAsync(IEnumerable<string> sourcePaths, string archivePath, CompressionOptions? options = null, IProgress<ArchiveProgress>? progress = null, CancellationToken cancellationToken = default) =>
            _zip.CompressAsync(sourcePaths, archivePath, options, progress, cancellationToken);
    }

    // ───────────────────── 사본이 쌓이지 않게(W2-04 1차 검증) ─────────────────────

    /// <summary>
    /// 예전엔 Ctrl+C 마다 사본을 새로 꺼내고 지우지 않아(앱을 닫아도, 다음 시작 때 24시간 지난 것만) 큰 동영상을 몇 번 복사하면 C: 에 수 GB 가 남았다.
    /// 이제 켜 둔 동안은 최근 2개(지금 클립보드 것 + 붙여넣기가 아직 돌 수 있는 바로 앞 것), 닫을 때는 마지막 1개만 남긴다.
    /// </summary>
    [Fact]
    public async Task 사본은_켜_둔_동안_최근_2개_닫을_때_마지막_1개만_남는다()
    {
        using var tmp = new TempDir("clipprune");
        using var _ = TestServices.UseAppRoot(tmp);
        var ts = new TestServices();
        var vm = new MainViewModel(new FakeUserInteraction(), services: ts.Services);
        string clipRoot = AppPaths.ClipRoot;
        await VmRunner.Run(async () =>
        {
            await vm.OpenArchiveAsync(Zip.FullPath);
            vm.NavigateToFolder("일집_샘플");
            var readme = vm.VisibleEntries.Where(e => e.Name == "readme.txt").ToList();
            for (int i = 0; i < 3; i++) Assert.True(await vm.CopyEntriesToClipboardAsync(readme));
        }, "Ctrl+C 세 번");

        // 사본 폴더 = temp\clip 바로 아래 guid 폴더(그 안에 압축 안 경로대로 꺼냄)
        string CopyDir(int i) => Path.Combine(clipRoot, Path.GetRelativePath(clipRoot, Assert.Single(ts.Clipboard.Calls[i].Paths)).Split(Path.DirectorySeparatorChar)[0]);
        Assert.Equal(3, ts.Clipboard.Calls.Count);
        Assert.False(Directory.Exists(CopyDir(0)), "클립보드에서 밀려난 오래된 사본이 남음");
        Assert.True(File.Exists(ts.Clipboard.Calls[1].Paths[0]), "바로 앞 사본은 붙여넣기가 아직 돌 수 있어 남겨야 함");
        Assert.True(File.Exists(ts.Clipboard.Calls[2].Paths[0]));
        Assert.Equal(2, Directory.GetDirectories(clipRoot).Length);

        vm.Shutdown();
        Assert.False(Directory.Exists(CopyDir(1)), "닫을 때 앞 사본이 남음");
        Assert.True(File.Exists(ts.Clipboard.Calls[2].Paths[0]), "마지막 사본은 일집을 꺼도 붙여넣기 되게 남겨야 함");
        Assert.Equal(new[] { CopyDir(2) }, Directory.GetDirectories(clipRoot));
    }

    // ───────────────────── IJ3-01(b): 이름이 바뀌는 항목 — 빠짐없이 + 알림 ─────────────────────

    /// <summary>Windows 가 그대로 못 쓰는 이름을 모은 압축('|' '"' '&lt;' '&gt;' '*' ':' '?', 점만으로 된 이름, 장치 이름, 이름이 바뀌는 폴더).</summary>
    private static string OddNamesZip(TempDir tmp) => TestZip.Create(tmp.P("이상한이름.zip"),
        TestZip.Text("모음/파이프|이름.txt", "1"),
        TestZip.Text("모음/\"따옴표\".txt", "2"),
        TestZip.Text("모음/<꺾쇠>.txt", "3"),
        TestZip.Text("모음/별*표.txt", "4"),
        TestZip.Text("모음/...", "5"),
        TestZip.Text("모음/CON", "6"),
        TestZip.Text("모음/폴더?/안:쪽.txt", "7"),
        TestZip.Text("모음/보통.txt", "8"));

    /// <summary>
    /// IJ3-01(b): 디스크에서 이름이 바뀌는 항목을 여럿 골라도 하나도 빠지지 않고 안전한 이름으로 복사되고, 바뀐 이름을 알린다
    /// (예전엔 이런 항목이 '찾지 못했어요'로 실패하거나, 여럿을 고르면 말없이 빠졌고, 이름이 바뀌었다는 말도 없었다).
    /// </summary>
    [Fact]
    public async Task 이름이_바뀌는_항목은_빠짐없이_안전한_이름으로_복사하고_바뀐_이름을_알린다()
    {
        using var tmp = new TempDir("clipodd");
        using var _ = TestServices.UseAppRoot(tmp);
        var (vm, ui, ts) = await OpenAt(OddNamesZip(tmp), "모음");
        var all = vm.VisibleEntries.ToList();
        Assert.Equal(8, all.Count);
        bool ok = false;
        await VmRunner.Run(async () => ok = await vm.CopyEntriesToClipboardAsync(all), "Ctrl+C 이상한 이름");

        Assert.True(ok, string.Join(" / ", ui.Errors));
        Assert.Empty(ui.Errors);
        var paths = Assert.Single(ts.Clipboard.Calls).Paths;
        Assert.Equal(8, paths.Count);
        var byName = paths.ToDictionary(p => Path.GetFileName(p)!, StringComparer.Ordinal);
        Assert.Equal(new[] { ".._", "_CON", "_꺾쇠_.txt", "_따옴표_.txt", "별_표.txt", "보통.txt", "파이프_이름.txt", "폴더_" },
            byName.Keys.OrderBy(n => n, StringComparer.Ordinal));
        Assert.Equal("1", File.ReadAllText(byName["파이프_이름.txt"]));
        Assert.Equal("2", File.ReadAllText(byName["_따옴표_.txt"]));
        Assert.Equal("3", File.ReadAllText(byName["_꺾쇠_.txt"]));
        Assert.Equal("4", File.ReadAllText(byName["별_표.txt"]));
        Assert.Equal("5", File.ReadAllText(byName[".._"]));
        Assert.Equal("6", File.ReadAllText(byName["_CON"]));
        Assert.Equal("7", File.ReadAllText(Path.Combine(byName["폴더_"], "안_쪽.txt")));
        Assert.Equal("8", File.ReadAllText(byName["보통.txt"]));

        // 알림: 바뀐 것 8개(고른 것 7개 + 고른 폴더 '폴더?' 안의 '안:쪽.txt') 중 5개 + '외 3개', 바뀌지 않은 것은 없음
        string notice = Assert.Single(ui.Infos);
        Assert.Contains("바꿔서 복사했어요", notice);
        var lines = notice.Split('\n');
        Assert.Equal(5, lines.Count(l => l.StartsWith("- ", StringComparison.Ordinal)));
        Assert.Contains("외 3개", lines);
        Assert.DoesNotContain("보통.txt", notice);
        Assert.All(lines.Where(l => l.StartsWith("- ", StringComparison.Ordinal)), l => Assert.Contains(" → ", l));
        Assert.StartsWith("8개 항목을 복사했어요(이름 8개는 바꿈)", vm.StatusText);
        Assert.Equal("- 폴더? → 폴더_", lines.First(l => l.StartsWith("- ", StringComparison.Ordinal)));   // 고른 차례(목록 차례 — 폴더 먼저)대로
    }

    /// <summary>IJ3-01(b): 폴더를 고르면 그 안에서 바뀌는 이름도 알린다(항목마다 처음 바뀐 조각 하나 — 바뀐 폴더 안의 파일은 그 폴더 하나로).</summary>
    [Fact]
    public async Task 폴더를_고르면_그_안에서_바뀌는_이름도_알린다()
    {
        using var tmp = new TempDir("clipoddfolder");
        using var _ = TestServices.UseAppRoot(tmp);
        var (vm, ui, ts) = await OpenAt(OddNamesZip(tmp), string.Empty);
        await VmRunner.Run(() => vm.CopyEntriesToClipboardAsync(vm.VisibleEntries.Where(e => e.Name == "모음").ToList()), "Ctrl+C 폴더");

        Assert.Empty(ui.Errors);
        string dir = Assert.Single(Assert.Single(ts.Clipboard.Calls).Paths);
        Assert.Equal("모음", Path.GetFileName(dir));
        Assert.Equal(8, Directory.GetFiles(dir, "*", SearchOption.AllDirectories).Length);
        string notice = Assert.Single(ui.Infos);
        Assert.Contains("- 모음/파이프|이름.txt → 파이프_이름.txt", notice);
        Assert.Contains("외 2개", notice);   // 7개(폴더 '폴더?' 하나 + 파일 6개)
        Assert.StartsWith("1개 항목을 복사했어요(이름 7개는 바꿈)", vm.StatusText);
    }

    [Fact]
    public void 폴더_안에서_바뀌는_이름은_처음_바뀐_조각만_한_번씩_센다()
    {
        var all = new List<ArchiveEntry>
        {
            new() { Path = "상자/자료:모음/", IsDirectory = true },
            new() { Path = "상자/자료:모음/가?.txt" },
            new() { Path = "상자/자료:모음/나.txt" },
            new() { Path = "상자/보통/끝점." },
            new() { Path = "상자/보통/괜찮음.txt" },
            new() { Path = "상자/LPT1.log" },
            new() { Path = "다른/콜론:밖.txt" },   // 고른 폴더 밖 — 세지 않음
        };
        var picks = ClipboardCopyPlan.Picks(new[] { new ArchiveEntry { Path = "상자", IsDirectory = true } });
        var renamed = ClipboardCopyPlan.NestedRenames(picks, all, CancellationToken.None);
        Assert.Equal(new[]
        {
            new ClipboardCopyPlan.Renamed("상자/자료:모음", "자료_모음"),
            new ClipboardCopyPlan.Renamed("상자/보통/끝점.", "끝점"),
            new ClipboardCopyPlan.Renamed("상자/LPT1.log", "_LPT1.log"),
        }, renamed);
        Assert.Null(ClipboardCopyPlan.RenamedNotice(Array.Empty<ClipboardCopyPlan.Renamed>()));
        Assert.Equal(".._", ClipboardCopyPlan.DiskName("..."));
        Assert.Equal("_CON", ClipboardCopyPlan.DiskName("CON"));

        // 같은 열쇠(대소문자까지 같을 때만)는 하나로, 대소문자만 다른 두 항목은 둘 다
        var p2 = ClipboardCopyPlan.Picks(new[]
        {
            new ArchiveEntry { Path = "대소/Report.txt" }, new ArchiveEntry { Path = "대소/report.txt" }, new ArchiveEntry { Path = "/대소/Report.txt" },
        });
        Assert.Equal(new[] { "대소/Report.txt", "대소/report.txt" }, p2.Select(p => p.Key));
    }

    // ───────────────────── IJ3-01(d): 수천 개를 골라도 화면이 굳지 않게 ─────────────────────

    private static string ManyFilesZip(TempDir tmp, int count) =>
        TestZip.Create(tmp.P("많음.zip"), Enumerable.Range(0, count).Select(i => TestZip.Text($"많음/파일 {i:D4}.txt", "x" + i)).ToArray());

    /// <summary>
    /// IJ3-01(d): 수천 개를 골라 Ctrl+C 해도 꺼낸 뒤의 정리(꺼낸 자리 찾기·표식)는 화면 스레드가 아닌 곳에서, 같은 진행 창에 '복사할 파일을 정리하는 중'을 보이며 한다.
    /// 예전엔 화면 스레드에서 고른 것마다 폴더 전체를 다시 읽어(제곱 시간) 진행 창도 [취소]도 없이 오래 굳었다. 클립보드에는 화면 스레드에서 넣는다.
    /// WPF 디스패처(창 없음)가 도는 STA 스레드에서 화면처럼 돌려 본다.
    /// </summary>
    [Fact]
    public async Task 수천_개를_골라도_정리는_화면_스레드_밖에서_진행을_보이며_한다()
    {
        using var tmp = new TempDir("clipmany");
        using var _ = TestServices.UseAppRoot(tmp);
        const int count = 3000;
        string zip = ManyFilesZip(tmp, count);
        var ts = new TestServices();
        var ui = new FakeUserInteraction();
        var stageThreads = new System.Collections.Concurrent.ConcurrentBag<int>();
        ui.OnProgressReport = p =>
        {
            if (p.IsStage && p.CurrentFile == ClipboardCopyPlan.StageCollecting) stageThreads.Add(Environment.CurrentManagedThreadId);
        };
        var vm = new MainViewModel(ui, services: ts.Services);
        int uiThread = -1;
        bool ok = false;
        var sw = new System.Diagnostics.Stopwatch();
        await Task.Run(() => DispatcherThread.Run(async () =>
        {
            uiThread = Environment.CurrentManagedThreadId;
            Assert.True(await vm.OpenArchiveAsync(zip));
            vm.NavigateToFolder("많음");
            var sel = vm.VisibleEntries.ToList();
            Assert.Equal(count, sel.Count);
            sw.Start();
            ok = await vm.CopyEntriesToClipboardAsync(sel);
            sw.Stop();
        })).WaitAsync(TimeSpan.FromSeconds(120));

        Assert.True(ok, string.Join(" / ", ui.Errors));
        var paths = Assert.Single(ts.Clipboard.Calls).Paths;
        Assert.Equal(count, paths.Count);
        Assert.Equal(count, paths.Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.Equal("x1234", File.ReadAllText(paths.Single(p => Path.GetFileName(p) == "파일 1234.txt")));
        Assert.NotEmpty(stageThreads);
        Assert.DoesNotContain(uiThread, stageThreads);   // 정리는 뒤 스레드
        Assert.Equal(new[] { uiThread }, ts.Clipboard.Threads);   // 클립보드는 화면 스레드에서
        var session = Assert.Single(ui.ProgressSessions);   // 꺼내기와 정리가 한 진행 창
        Assert.Contains(session.Reports, p => p.IsStage && p.CurrentFile == ClipboardCopyPlan.StageCollecting && p.TotalFiles == count);
        Assert.Empty(ui.Infos);   // 바뀐 이름 없음
        Assert.StartsWith($"{count}개 항목을 복사했어요", vm.StatusText);
        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(60), $"너무 느림: {sw.Elapsed}");
    }

    /// <summary>IJ3-01(d): 꺼낸 뒤 정리하는 도중 [취소]해도 멈추고, 클립보드는 그대로·사본은 지운다.</summary>
    [Fact]
    public async Task 정리하는_도중_취소하면_클립보드도_사본도_없다()
    {
        using var tmp = new TempDir("clipmanyc");
        using var _ = TestServices.UseAppRoot(tmp);
        string zip = ManyFilesZip(tmp, 400);
        var ts = new TestServices();
        var ui = new FakeUserInteraction { CancelWhen = p => p.IsStage && p.CurrentFile == ClipboardCopyPlan.StageCollecting };
        var vm = new MainViewModel(ui, services: ts.Services);
        string clipRoot = AppPaths.ClipRoot;
        bool ok = true;
        await VmRunner.Run(async () =>
        {
            Assert.True(await vm.OpenArchiveAsync(zip));
            vm.NavigateToFolder("많음");
            ok = await vm.CopyEntriesToClipboardAsync(vm.VisibleEntries.ToList());
        }, "정리 중 취소");

        Assert.False(ok);
        Assert.Empty(ts.Clipboard.Calls);
        Assert.Contains(Assert.Single(ui.ProgressSessions).Reports, p => p.IsStage && p.CurrentFile == ClipboardCopyPlan.StageCollecting);   // 정리 단계에서 취소함
        Assert.False(Directory.Exists(clipRoot) && Directory.EnumerateFileSystemEntries(clipRoot).Any(), "취소했는데 사본이 남음");
        Assert.Equal("복사를 취소했어요", vm.StatusText);
        Assert.Empty(ui.Errors);
    }

    /// <summary>IJ3-01(d): 진행 창은 모달이 아니라 복사하는 동안 또 Ctrl+C 를 누를 수 있다 — 겹쳐 돌지 않고 알린다.</summary>
    [Fact]
    public async Task 복사하는_도중_또_누르면_겹쳐_돌지_않는다()
    {
        using var tmp = new TempDir("clipagain");
        using var _ = TestServices.UseAppRoot(tmp);
        string zip = ManyFilesZip(tmp, 200);
        var ts = new TestServices();
        var ui = new FakeUserInteraction();
        var vm = new MainViewModel(ui, services: ts.Services);
        bool first = false, second = true;
        string statusAfterSecond = string.Empty;
        await VmRunner.Run(async () =>
        {
            Assert.True(await vm.OpenArchiveAsync(zip));
            vm.NavigateToFolder("많음");
            var sel = vm.VisibleEntries.ToList();
            var running = vm.CopyEntriesToClipboardAsync(sel);
            second = await vm.CopyEntriesToClipboardAsync(sel);
            statusAfterSecond = vm.StatusText;
            first = await running;
        }, "두 번 누르기");

        Assert.True(first);
        Assert.False(second);
        Assert.Equal("앞의 복사가 아직 끝나지 않았어요. 끝난 뒤에 다시 해 주세요", statusAfterSecond);
        Assert.Single(ts.Clipboard.Calls);
        Assert.Single(ui.ProgressSessions);
    }

    /// <summary>
    /// IJ3-01(d): 해제 엔진이 고른 열쇠로 항목을 고르는 일(SharpCompress·7z.dll·EGG 공용)은 열쇠 수와 상관없이 빠르다 — 예전엔 항목마다 열쇠를 하나씩 비교해
    /// (항목 수 × 열쇠 수) 수천 개를 고르면 꺼내기 전부터 오래 걸렸다. 결과(폴더 열쇠는 그 안 전부, 대소문자·'\'·NFD 무시)는 예전과 같다.
    /// </summary>
    [Fact]
    public void 고른_열쇠가_수천_개여도_항목_고르기는_빠르고_결과는_같다()
    {
        var keys = Enumerable.Range(0, 20_000).Select(i => $"폴더{i % 100}/파일{i}.txt").ToList();
        keys.Add("상자/하위/");
        keys.Add(string.Empty);
        var select = SharpCompressArchiveServiceBase.EntrySelector(keys);

        Assert.True(select("폴더3/파일3.txt"));
        Assert.True(select("폴더3\\파일3.TXT"));   // '\'·대소문자 무시
        Assert.True(select("폴더3/파일3.txt".Normalize(System.Text.NormalizationForm.FormD)));   // 맥 NFD 이름
        Assert.False(select("폴더3/파일4.txt"));   // 4 는 폴더4 에
        Assert.True(select("상자/하위"));
        Assert.True(select("상자/하위/깊은/곳/x.txt"));   // 폴더 열쇠는 그 안 전부
        Assert.False(select("상자/하위2/x.txt"));
        Assert.False(select("상자"));
        Assert.False(select(string.Empty));

        var sw = System.Diagnostics.Stopwatch.StartNew();
        int hits = 0;
        for (int i = 0; i < 100_000; i++)
            if (select($"폴더{i % 100}/파일{i}.txt")) hits++;
        sw.Stop();
        Assert.Equal(20_000, hits);
        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(20), $"너무 느림: {sw.Elapsed}");   // 예전(10만 × 2만 비교)은 몇 분
    }

    /// <summary>진짜 클립보드에 넣을 데이터의 모양(넣지는 않음): FileDrop 목록 + 'Preferred DropEffect' = 01 00 00 00(복사).</summary>
    [Fact]
    public void 클립보드_데이터는_파일목록과_복사효과다()
    {
        var paths = new[] { @"C:\임시\가.txt", @"C:\임시\폴더" };
        Sta.Run(() =>
        {
            var data = ClipboardService.BuildFileDropData(paths, DragDropEffects.Copy);
            Assert.Equal(paths, data.GetFileDropList().Cast<string>());
            var ms = Assert.IsType<MemoryStream>(data.GetData(ClipboardService.PreferredDropEffectFormat));
            Assert.Equal(new byte[] { 1, 0, 0, 0 }, ms.ToArray());
        });
    }
}
