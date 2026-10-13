using System.Diagnostics;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using Iljip.Models;
using Iljip.Services;
using Iljip.Tests.TestSupport;
using Iljip.ViewModels;
using Xunit.Abstractions;

namespace Iljip.Tests.Contract;

/// <summary>
/// [W2-09 · I2-14] 둘러보기: 이름으로 찾기(모든 폴더 — 200ms 기다렸다 찾음, NFC·대소문자 무시·글자 일부, 결과에 '경로', Esc 로 지움),
/// Backspace = 위 폴더(UpCommand — 찾는 중이면 찾기 지우기), 상태 줄 '선택: 3개 · 12.4MB'(폴더는 그 아래 전부, 크기를 모르면 '약'),
/// 트리 너비(ui.treeWidth — 기본 260, 창을 닫을 때 한 번 저장). 화면은 가짜, 창 XAML 은 띄우지 않고 만들기만.
/// </summary>
public class BrowseTests
{
    private readonly ITestOutputHelper _out;

    public BrowseTests(ITestOutputHelper output) => _out = output;

    private static Task Run(Func<Task> action, string what) => VmRunner.Run(action, what);

    private static RawZip.Item Item(string name, int size) =>
        new(Encoding.UTF8.GetBytes(name), Enumerable.Repeat((byte)'x', size).ToArray(), true);

    /// <summary>문서/보고서.txt(1000) · 문서/하위/보고서_최종.txt(2000) · 사진/a.jpg(3000) · README.TXT(10) — 폴더 '문서/하위'는 항목 없이 경로로만.</summary>
    private static async Task<(MainViewModel Vm, TestServices Svc)> OpenSample(TempDir tmp, string? nfdName = null)
    {
        string zip = tmp.P("둘러보기.zip");
        var items = new List<RawZip.Item>
        {
            Item("문서/보고서.txt", 1000),
            Item("문서/하위/보고서_최종.txt", 2000),
            Item("사진/a.jpg", 3000),
            Item("README.TXT", 10),
        };
        if (nfdName is not null) items.Add(Item(nfdName, 5));
        RawZip.Write(zip, items.ToArray());
        var svc = new TestServices();
        var vm = new MainViewModel(new FakeUserInteraction(), null, svc.Services);
        await Run(async () => Assert.True(await vm.OpenArchiveAsync(zip)), "열기");
        return (vm, svc);
    }

    private static List<string> Paths(MainViewModel vm) => vm.VisibleEntries.Select(e => e.Path.TrimEnd('/')).ToList();

    // ───────────────────────────── 이름으로 찾기 ─────────────────────────────

    [Fact]
    public async Task 이름으로_모든_폴더에서_찾고_경로를_보인다()
    {
        using var tmp = new TempDir("search");
        var (vm, _) = await OpenSample(tmp);
        Assert.Equal(new[] { "문서", "사진", "README.TXT" }, Paths(vm));   // 맨 위 폴더

        vm.SearchText = "보고서";
        vm.ApplySearchNow();
        Assert.True(vm.IsSearching);
        Assert.Equal(new[] { "문서/보고서.txt", "문서/하위/보고서_최종.txt" }, Paths(vm));
        Assert.Equal(new[] { "문서", "문서/하위" }, vm.VisibleEntries.Select(e => e.FolderPath));
        Assert.Contains("찾은 항목 2개", vm.StatusText);

        // 대소문자 무시·글자 일부
        vm.SearchText = "readme";
        vm.ApplySearchNow();
        Assert.Equal(new[] { "README.TXT" }, Paths(vm));
        Assert.Equal(string.Empty, vm.VisibleEntries[0].FolderPath);

        vm.SearchText = "TxT";
        vm.ApplySearchNow();
        Assert.Equal(3, vm.VisibleEntries.Count);

        // '경로' 열로도 정렬(맨 위 → 문서 → 문서/하위), 한 번 더 누르면 거꾸로
        vm.SortBy(EntrySortComparer.FolderPath);
        Assert.Equal(new[] { "README.TXT", "문서/보고서.txt", "문서/하위/보고서_최종.txt" }, Paths(vm));
        vm.SortBy(EntrySortComparer.FolderPath);
        Assert.Equal(new[] { "문서/하위/보고서_최종.txt", "문서/보고서.txt", "README.TXT" }, Paths(vm));
        vm.SortBy(EntrySortComparer.Name);

        // 폴더 이름도 찾는다(폴더 항목이 따로 없어도 — 경로로만 있는 폴더). 폴더는 먼저(정렬 규칙 그대로)
        vm.SearchText = "하위";
        vm.ApplySearchNow();
        var folder = Assert.Single(vm.VisibleEntries);
        Assert.True(folder.IsDirectory);
        Assert.Equal("문서/하위", folder.Path);

        // 없으면 빈 목록
        vm.SearchText = "없는이름";
        vm.ApplySearchNow();
        Assert.Empty(vm.VisibleEntries);
        Assert.Contains("찾은 항목 0개", vm.StatusText);
    }

    /// <summary>맥(NFD)으로 친 글자·NFD 이름 항목도 같은 글자로 찾는다(찾는 글자·이름 모두 NFC 로 맞춤).</summary>
    [Fact]
    public async Task NFD로_쳐도_NFD_이름도_찾는다()
    {
        using var tmp = new TempDir("searchnfd");
        var (vm, _) = await OpenSample(tmp, nfdName: "맥/견적서.txt".Normalize(NormalizationForm.FormD));
        vm.SearchText = "견적".Normalize(NormalizationForm.FormD);
        vm.ApplySearchNow();
        Assert.Equal(new[] { "맥/견적서.txt" }, Paths(vm));
        vm.SearchText = "보고서".Normalize(NormalizationForm.FormD);
        vm.ApplySearchNow();
        Assert.Equal(2, vm.VisibleEntries.Count);
    }

    /// <summary>Esc(찾기 지우기): 찾기 칸이 비고 보던 폴더 목록으로 돌아온다.</summary>
    [Fact]
    public async Task 찾기를_지우면_보던_폴더로()
    {
        using var tmp = new TempDir("searchclear");
        var (vm, _) = await OpenSample(tmp);
        vm.GoToFolder("문서");
        Assert.Equal(new[] { "문서/하위", "문서/보고서.txt" }, Paths(vm));
        vm.SearchText = "jpg";
        vm.ApplySearchNow();
        Assert.Equal(new[] { "사진/a.jpg" }, Paths(vm));

        vm.ClearSearchCommand.Execute(null);
        Assert.False(vm.IsSearching);
        Assert.Equal(string.Empty, vm.SearchText);
        Assert.Equal(new[] { "문서/하위", "문서/보고서.txt" }, Paths(vm));
    }

    /// <summary>치는 동안은 찾지 않고, 멈춘 뒤(<see cref="MainViewModel.SearchDelay"/>) 마지막 글자로 한 번 찾는다.</summary>
    [Fact]
    public async Task 치기를_멈추면_마지막_글자로_찾는다()
    {
        using var tmp = new TempDir("searchdelay");
        var (vm, _) = await OpenSample(tmp);
        vm.SearchDelay = TimeSpan.FromMilliseconds(300);
        vm.SearchText = "사";
        vm.SearchText = "보고";
        Assert.False(vm.IsSearching);   // 아직(기다리는 중)
        Assert.Equal(new[] { "문서", "사진", "README.TXT" }, Paths(vm));

        var sw = Stopwatch.StartNew();
        while (!vm.IsSearching && sw.Elapsed < TimeSpan.FromSeconds(10)) await Task.Delay(20);
        Assert.True(vm.IsSearching, "기다린 뒤에도 찾지 않음");
        await Task.Delay(50);
        Assert.Equal(new[] { "문서/보고서.txt", "문서/하위/보고서_최종.txt" }, Paths(vm));
    }

    /// <summary>다른 압축을 열면 찾기가 지워지고, 고친 뒤 다시 읽어도(같은 압축) 찾기는 그대로 새 목록에서.</summary>
    [Fact]
    public async Task 다른_압축을_열면_찾기를_지운다()
    {
        using var tmp = new TempDir("searchopen");
        var (vm, _) = await OpenSample(tmp);
        vm.SearchText = "보고서";
        vm.ApplySearchNow();
        string other = tmp.P("다른.zip");
        RawZip.Write(other, RawZip.Utf8("보고서_다른.txt", "x"));
        await Run(async () => Assert.True(await vm.OpenArchiveAsync(other)), "다른 압축 열기");
        Assert.False(vm.IsSearching);
        Assert.Equal(string.Empty, vm.SearchText);
        Assert.Equal(new[] { "보고서_다른.txt" }, Paths(vm));
    }

    /// <summary>찾은 결과의 폴더로 들어가면(두 번 누르기·트리에서 고름) 찾기를 지우고 그 폴더로.</summary>
    [Fact]
    public async Task 찾은_폴더로_들어가면_찾기를_지운다()
    {
        using var tmp = new TempDir("searchgo");
        var (vm, _) = await OpenSample(tmp);
        vm.SearchText = "하위";
        vm.ApplySearchNow();
        vm.GoToFolder(vm.VisibleEntries[0].Path);
        Assert.False(vm.IsSearching);
        Assert.Equal("문서/하위", vm.SelectedFolder!.FullPath);
        Assert.Equal(new[] { "문서/하위/보고서_최종.txt" }, Paths(vm));
    }

    /// <summary>
    /// [W2-09 검증 지적] 찾은 결과의 폴더에서 Enter·우클릭 '열기'(OpenEntryAsync)도 두 번 누르기와 같게 찾기를 지우고 그 폴더로 —
    /// 예전엔 고른 폴더만 바뀌고 목록은 찾은 결과 그대로라 아무 일도 안 한 것처럼 보였다.
    /// </summary>
    [Fact]
    public async Task 찾은_폴더에서_Enter도_찾기를_지우고_그_폴더로()
    {
        using var tmp = new TempDir("searchenter");
        var (vm, _) = await OpenSample(tmp);
        vm.SearchText = "하위";
        vm.ApplySearchNow();
        var folder = Assert.Single(vm.VisibleEntries);
        Assert.True(await vm.OpenEntryAsync(folder));
        Assert.False(vm.IsSearching);
        Assert.Equal(string.Empty, vm.SearchText);
        Assert.Equal("문서/하위", vm.SelectedFolder!.FullPath);
        Assert.Equal(new[] { "문서/하위/보고서_최종.txt" }, Paths(vm));
    }

    /// <summary>
    /// 항목 10만 개: 찾기 한 번이 100ms 안(미리 만든 NFC·소문자 이름 목록을 훑음). 이름 목록 만들기·목록 다시 그리기(정렬 포함) 시간도 기록한다.
    /// 다른 시험이 같이 돌아 바쁜 PC 에서도 흔들리지 않게 다섯 번 재서 가장 빠른 값으로 본다.
    /// </summary>
    [Fact]
    public async Task 항목_10만개도_찾기가_100ms_안()
    {
        using var tmp = new TempDir("search100k");
        string zip = tmp.P("큰목록.zip");
        const int N = 100_000;
        var items = new RawZip.Item[N];
        for (int i = 0; i < N; i++)
            items[i] = new RawZip.Item(Encoding.UTF8.GetBytes($"폴더{i % 100}/하위{i % 7}/파일_{i}.txt"), new[] { (byte)1 }, true);
        RawZip.Write(zip, items);
        var vm = new MainViewModel(new FakeUserInteraction(), null, new TestServices().Services);
        var sw = Stopwatch.StartNew();
        await Run(async () => Assert.True(await vm.OpenArchiveAsync(zip)), "10만 개 열기");
        long openMs = sw.ElapsedMilliseconds;

        sw.Restart();
        var first = vm.FindByName(MainViewModel.NormalizeSearch("파일_5"));   // 처음: 이름 목록 만들기 포함
        long indexMs = sw.ElapsedMilliseconds;
        int expected = Enumerable.Range(0, N).Count(i => $"파일_{i}.txt".Contains("파일_5"));
        Assert.Equal(expected, first.Count);

        var times = new List<double>();
        for (int k = 0; k < 5; k++)
        {
            sw.Restart();
            var found = vm.FindByName(MainViewModel.NormalizeSearch("파일_5"));
            times.Add(sw.Elapsed.TotalMilliseconds);
            Assert.Equal(expected, found.Count);
        }

        sw.Restart();
        vm.SearchText = "파일_99";
        vm.ApplySearchNow();
        long refreshMs = sw.ElapsedMilliseconds;
        Assert.Equal(Enumerable.Range(0, N).Count(i => $"파일_{i}.txt".Contains("파일_99")), vm.VisibleEntries.Count);

        _out.WriteLine($"기록_10만_찾기: 열기 {openMs}ms, 첫 찾기(이름 목록 만들기 포함) {indexMs}ms, 찾기 {string.Join(" / ", times.Select(t => t.ToString("0.0")))}ms, " +
                       $"찾기+목록 다시 그리기 {refreshMs}ms (결과 {vm.VisibleEntries.Count}개)");
        Assert.True(times.Min() < 100, $"찾기가 100ms 를 넘음: {times.Min():0.0}ms");
    }

    // ───────────────────────────── 위 폴더(Backspace) ─────────────────────────────

    [Fact]
    public async Task 위_폴더로_가고_맨_위에서는_그대로()
    {
        using var tmp = new TempDir("up");
        var (vm, _) = await OpenSample(tmp);
        vm.GoToFolder("문서/하위");
        Assert.Equal("문서/하위", vm.SelectedFolder!.FullPath);

        vm.UpCommand.Execute(null);
        Assert.Equal("문서", vm.SelectedFolder!.FullPath);
        Assert.Equal(new[] { "문서/하위", "문서/보고서.txt" }, Paths(vm));
        vm.UpCommand.Execute(null);
        Assert.Equal(string.Empty, vm.SelectedFolder!.FullPath);
        vm.UpCommand.Execute(null);   // 맨 위 — 그대로
        Assert.Equal(string.Empty, vm.SelectedFolder!.FullPath);
        Assert.Equal(new[] { "문서", "사진", "README.TXT" }, Paths(vm));

        // 찾는 중이면 Backspace 는 찾기 지우기(보던 폴더로)
        vm.GoToFolder("사진");
        vm.SearchText = "보고서";
        vm.ApplySearchNow();
        vm.UpCommand.Execute(null);
        Assert.False(vm.IsSearching);
        Assert.Equal("사진", vm.SelectedFolder!.FullPath);
    }

    /// <summary>
    /// 화면: 목록에서 Backspace 는 위 폴더, Esc 는 찾기 지우기 — 이름 칸을 고치는 중(글자 칸)의 키는 그 칸에 맡긴다(글자를 지우는 Backspace 가 폴더를 옮기지 않게).
    /// 찾기 칸의 Esc 는 찾기 지우기. (소스 확인 — 창을 띄우지 않고는 키를 보낼 수 없어서)
    /// </summary>
    [Fact]
    public void 화면_키_처리_Backspace는_이름_고치는_중이_아닐_때만()
    {
        string src = RepoFiles.Read("Iljip", "MainWindow.xaml.cs");
        int handler = src.IndexOf("private async void EntriesGrid_PreviewKeyDown", StringComparison.Ordinal);
        Assert.True(handler > 0);
        string body = src[handler..];
        int textBoxGuard = body.IndexOf("if (e.OriginalSource is TextBox) return;", StringComparison.Ordinal);
        int back = body.IndexOf("e.Key == Key.Back", StringComparison.Ordinal);
        int up = body.IndexOf("UpCommand.Execute", StringComparison.Ordinal);
        Assert.True(textBoxGuard > 0 && back > textBoxGuard && up > back, "Backspace 처리가 이름 칸 확인보다 앞에 있음");
        Assert.Contains("avm.ClearSearch()", body);

        string search = src[src.IndexOf("private void SearchBox_PreviewKeyDown", StringComparison.Ordinal)..];
        Assert.Contains("Key.Escape", search[..search.IndexOf("}", search.IndexOf("Key.Escape", StringComparison.Ordinal), StringComparison.Ordinal)]);
        Assert.Contains("vm.ClearSearch()", search);
    }

    // ───────────────────────────── 선택 합계 ─────────────────────────────

    [Fact]
    public async Task 선택_합계는_폴더_아래_전부를_더한다()
    {
        using var tmp = new TempDir("selsum");
        var (vm, _) = await OpenSample(tmp);
        var root = vm.VisibleEntries.ToList();   // 문서(폴더) · 사진(폴더) · README.TXT
        vm.UpdateSelection(root);
        Assert.Equal("선택: 3개 · 5.9KB", vm.SelectionText);   // 1000+2000+3000+10 = 6010B

        vm.UpdateSelection(new[] { root[0] });   // 문서 = 1000 + 하위 2000
        Assert.Equal("선택: 1개 · 2.9KB", vm.SelectionText);

        vm.UpdateSelection(new[] { root[2] });
        Assert.Equal("선택: 1개 · 10B", vm.SelectionText);

        vm.UpdateSelection(Array.Empty<ArchiveEntry>());
        Assert.Equal(string.Empty, vm.SelectionText);
    }

    /// <summary>크기를 모르는 항목(순수 .bz2 — 원래 크기를 적어 두지 않음)이 있으면 '약'.</summary>
    [Fact]
    public async Task 크기를_모르는_항목이_있으면_약()
    {
        var fx = FixtureCatalog.Get("bz2_single_python");
        var vm = new MainViewModel(new FakeUserInteraction(), null, new TestServices().Services);
        await Run(async () => Assert.True(await vm.OpenArchiveAsync(fx.FullPath)), "bz2 열기");
        vm.UpdateSelection(vm.VisibleEntries.ToList());
        Assert.StartsWith("선택: 1개 · 약 ", vm.SelectionText);

        Assert.True(MainViewModel.IsSizeUnknown(new ArchiveEntry { Path = "a", Size = -1 }));
        Assert.False(MainViewModel.IsSizeUnknown(new ArchiveEntry { Path = "빈 파일", Size = 0, CompressedSize = 2 }));   // 빈 파일(Deflate 2바이트)은 아는 크기
        Assert.False(MainViewModel.IsSizeUnknown(new ArchiveEntry { Path = "b", Size = 5, CompressedSize = 100 }));
    }

    /// <summary>
    /// [W2-09 검증 지적] 압축할 목록(모으는 중)의 폴더는 크기를 세지 않는다(디스크를 다 읽어야 앎) — 폴더만 고르면 크기 없이 '선택: 1개'(예전 '약 0B'),
    /// 파일과 섞이면 파일만 더해 '선택: 2개 · 파일 10B'.
    /// </summary>
    [Fact]
    public void 모으는_목록의_폴더는_크기를_세지_않는다()
    {
        using var tmp = new TempDir("selstage");
        string folder = tmp.Dir("보낼 폴더");
        File.WriteAllBytes(Path.Combine(folder, "안.txt"), new byte[500]);
        string file = tmp.P("보고서.txt");
        File.WriteAllBytes(file, new byte[10]);
        var vm = new MainViewModel(new FakeUserInteraction(), null, new TestServices().Services);
        vm.AddToStaging(new[] { folder, file });
        Assert.True(vm.IsStagingMode);
        var dir = vm.Entries.Single(e => e.IsDirectory);
        var one = vm.Entries.Single(e => !e.IsDirectory);

        vm.UpdateSelection(new[] { dir });
        Assert.Equal("선택: 1개", vm.SelectionText);
        vm.UpdateSelection(new[] { dir, one });
        Assert.Equal("선택: 2개 · 파일 10B", vm.SelectionText);
        vm.UpdateSelection(new[] { one });
        Assert.Equal("선택: 1개 · 10B", vm.SelectionText);
    }

    [Theory]
    [InlineData(0L, "0B")]
    [InlineData(512L, "512B")]
    [InlineData(1536L, "1.5KB")]
    [InlineData(13002342L, "12.4MB")]
    [InlineData(1342177280L, "1.25GB")]
    public void 크기_글자(long bytes, string expected) => Assert.Equal(expected, MainViewModel.FormatSize(bytes));

    // ───────────────────────────── 트리 너비 ─────────────────────────────

    [Fact]
    public void 트리_너비는_기본_260이고_닫을_때_바뀌었으면_한번_저장()
    {
        var svc = new TestServices();
        var vm = new MainViewModel(new FakeUserInteraction(), null, svc.Services);
        Assert.Equal(260, vm.TreeWidth);

        vm.RememberTreeWidth(260.2);   // 그대로면 저장하지 않음
        Assert.Equal(0, svc.Settings.SaveCount);
        vm.RememberTreeWidth(312.6);
        Assert.Equal(1, svc.Settings.SaveCount);
        Assert.Contains("\"treeWidth\": 313", svc.Settings.Json);
        vm.RememberTreeWidth(double.NaN);
        Assert.Equal(1, svc.Settings.SaveCount);

        // 다음 실행: 기억한 너비
        var again = new MainViewModel(new FakeUserInteraction(), null, svc.Services);
        Assert.Equal(313, again.TreeWidth);

        // 쓸 수 없는 값(손으로 고친 설정 등)은 기본값
        var bad = new AppSettings();
        bad.Ui.TreeWidth = 5;
        Assert.Equal(260, new MainViewModel(new FakeUserInteraction(), null, new TestServices(bad).Services).TreeWidth);
    }
}

/// <summary>[W2-09 · I2-14] 메인 창 XAML 의 둘러보기 장치(찾기 칸·가르는 막대·'경로' 열·선택 합계) — 띄우지 않고 만들기만. 앱 객체를 잠깐 만들어 순차로 돈다.</summary>
[Collection(WpfApplicationCollection.Name)]
public class BrowseXamlTests
{
    [Fact]
    public void 메인_창에_찾기_칸_가르는_막대_경로_열이_있다()
    {
        WpfAppScope.Run(log =>
        {
            MainWindow? w = null;
            try
            {
                w = new MainWindow();
                WpfAppScope.Pump();
                Assert.IsType<TextBox>(w.FindName("SearchBox"));
                Assert.IsType<GridSplitter>(w.FindName("TreeSplitter"));
                Assert.IsType<TextBlock>(w.FindName("SelectionTextBlock"));
                var grid = Assert.IsType<DataGrid>(w.FindName("EntriesGrid"));
                var col = Assert.IsType<DataGridTextColumn>(w.FindName("ColFolder"));
                Assert.Equal("경로", col.Header);
                Assert.Equal(Visibility.Collapsed, col.Visibility);   // 찾는 중에만
                Assert.True(col.IsReadOnly);
                Assert.Same(grid.Columns[1], col);
                var tree = Assert.IsType<ColumnDefinition>(w.FindName("TreeColumn"));
                Assert.Equal(0, tree.Width.Value);   // 압축을 보지 않을 때는 접힘
            }
            finally
            {
                w?.Close();
            }
        });
    }
}
