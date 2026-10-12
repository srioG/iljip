using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Markup;
using Iljip.Models;
using Iljip.Services;
using Iljip.Tests.TestSupport;
using Iljip.ViewModels;

namespace Iljip.Tests.Contract;

/// <summary>
/// [I0-5] 목록 정렬(VM 이 <see cref="EntrySortComparer"/> 로 — 폴더 먼저, 폴더를 옮겨도 유지)과 열 머리 템플릿(손잡이·화살표).
/// 창을 띄우지 않는다: XAML 은 소스 읽기 + 스타일 사전만 메모리에서 읽어 템플릿을 적용해 본다. 실제 끌기·화살표 모양은 MANUAL_CHECKLIST.
/// </summary>
public class SortingTests
{
    private static ArchiveEntry F(string name, long size = 0, DateTime? date = null) =>
        new() { Path = "폴더/" + name, Size = size, LastModified = date };

    private static ArchiveEntry D(string name) => new() { Path = "폴더/" + name, IsDirectory = true, Size = -1, CompressedSize = -1 };

    private static string[] Sorted(IEnumerable<ArchiveEntry> items, string member, bool descending) =>
        items.OrderBy(e => e, new EntrySortComparer(member, descending)).Select(e => e.Name).ToArray();

    [Theory]
    [InlineData(EntrySortComparer.Name)]
    [InlineData(EntrySortComparer.Size)]
    [InlineData(EntrySortComparer.LastModified)]
    public void 폴더가_언제나_먼저(string member)
    {
        var items = new[] { F("a.txt", 5, new DateTime(2026, 1, 1)), D("나"), F("b.txt", 9), D("가") };
        Assert.Equal(new[] { "가", "나" }, Sorted(items, member, false).Take(2).OrderBy(x => x).ToArray());
        Assert.Equal(new[] { "가", "나" }, Sorted(items, member, true).Take(2).OrderBy(x => x).ToArray());
    }

    [Fact]
    public void 크기순_오름_내림()
    {
        var items = new[] { F("a.txt", 10), F("b.txt", 5), F("c.txt", 20), D("폴더") };
        Assert.Equal(new[] { "폴더", "b.txt", "a.txt", "c.txt" }, Sorted(items, EntrySortComparer.Size, false));
        Assert.Equal(new[] { "폴더", "c.txt", "a.txt", "b.txt" }, Sorted(items, EntrySortComparer.Size, true));
    }

    [Fact]
    public void 날짜순_없는_날짜는_늘_맨_뒤()
    {
        var items = new[] { F("a.txt", date: new DateTime(2026, 1, 2)), F("b.txt"), F("c.txt", date: new DateTime(2025, 1, 1)) };
        Assert.Equal(new[] { "c.txt", "a.txt", "b.txt" }, Sorted(items, EntrySortComparer.LastModified, false));
        Assert.Equal(new[] { "a.txt", "c.txt", "b.txt" }, Sorted(items, EntrySortComparer.LastModified, true));
    }

    [Fact]
    public void 값이_같으면_이름_오름차순()
    {
        var items = new[] { F("다.txt", 1), F("가.txt", 1), F("나.txt", 1) };
        Assert.Equal(new[] { "가.txt", "나.txt", "다.txt" }, Sorted(items, EntrySortComparer.Size, true));
    }

    [Fact]
    public void 이름은_대소문자를_가리지_않는다()
    {
        var items = new[] { F("b.TXT"), F("A.txt"), F("가.txt"), F("a2.txt") };
        Assert.Equal(new[] { "A.txt", "a2.txt", "b.TXT", "가.txt" }, Sorted(items, EntrySortComparer.Name, false));
    }

    /// <summary>
    /// 이름은 탐색기처럼 숫자를 수 크기로 비교한다(자연 정렬, StrCmpLogicalW — 반디집도 같음). W1-D 처음엔 서수 비교라
    /// '10.txt' 가 '2.txt' 앞에, '견적서_10' 이 '견적서_2' 앞에 왔다(1차 검증 지적).
    /// </summary>
    [Fact]
    public void 이름의_숫자는_수_크기로_비교한다()
    {
        Assert.Equal(new[] { "1.txt", "2.txt", "10.txt" }, Sorted(new[] { F("10.txt"), F("2.txt"), F("1.txt") }, EntrySortComparer.Name, false));
        Assert.Equal(new[] { "1월.xlsx", "2월.xlsx", "10월.xlsx", "11월.xlsx" },
            Sorted(new[] { F("11월.xlsx"), F("10월.xlsx"), F("2월.xlsx"), F("1월.xlsx") }, EntrySortComparer.Name, false));
        Assert.Equal(new[] { "견적서_2.txt", "견적서_10.txt" }, Sorted(new[] { F("견적서_10.txt"), F("견적서_2.txt") }, EntrySortComparer.Name, false));
        Assert.Equal(new[] { "견적서_10.txt", "견적서_2.txt" }, Sorted(new[] { F("견적서_2.txt"), F("견적서_10.txt") }, EntrySortComparer.Name, true));
        // 폴더도 같은 규칙(폴더 먼저는 그대로)
        Assert.Equal(new[] { "2단계", "10단계", "a.txt" }, Sorted(new[] { F("a.txt"), D("10단계"), D("2단계") }, EntrySortComparer.Name, false));
    }

    [Fact]
    public void 이름_비교는_탐색기_함수와_같다()
    {
        Assert.True(EntrySortComparer.CompareNames("2.txt", "10.txt") < 0);
        Assert.True(EntrySortComparer.CompareNames("B.txt", "a.txt") > 0);
        Assert.Equal(0, EntrySortComparer.CompareNames("ABC.TXT", "abc.txt"));   // 대소문자만 다르면 같음 → 차례는 전체 경로로
        Assert.Equal(0, EntrySortComparer.CompareNames(null, ""));
    }

    [Fact]
    public void 모르는_열은_이름순()
    {
        var c = new EntrySortComparer("없는열");
        Assert.Equal(EntrySortComparer.Name, c.Member);
        Assert.False(EntrySortComparer.IsSortable("Path"));
        Assert.False(EntrySortComparer.IsSortable(null));
    }

    // ───────────── 화면(VM) ─────────────

    private static async Task<MainViewModel> OpenZipDefault()
    {
        var vm = new MainViewModel(new FakeUserInteraction());
        string zip = FixtureCatalog.Get("zip_default").FullPath;
        await VmRunner.Run(() => vm.OpenArchiveAsync(zip), "열기");
        return vm;
    }

    /// <summary>
    /// 기본은 이름 오름차순: 폴더(이름순) 다음 파일(이름순) — data, 빈폴더, 하위폴더 다음 파일들. 이름 차례는 탐색기와 같은 자연 정렬
    /// (이 샘플은 숫자 차이가 없어 W1-D 처음의 서수 비교 차례와도 같다).
    /// </summary>
    [Fact]
    public async Task 기본은_폴더_먼저_이름순()
    {
        var vm = await OpenZipDefault();
        vm.NavigateToFolder("일집_샘플");

        var names = vm.VisibleEntries.Select(e => e.Name).ToList();
        Assert.Equal(new[] { "data", "빈폴더", "하위폴더" }, names.Take(3).ToArray());
        var byName = Comparer<string>.Create(EntrySortComparer.CompareNames);
        var expected = vm.SelectedFolder!.Children.Select(c => c.Name).OrderBy(n => n, byName)
            .Concat(vm.SelectedFolder.Files.Select(f => f.Name).OrderBy(n => n, byName)).ToList();
        Assert.Equal(expected, names);
        var old = vm.SelectedFolder.Children.Select(c => c.Name).OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
            .Concat(vm.SelectedFolder.Files.Select(f => f.Name).OrderBy(n => n, StringComparer.OrdinalIgnoreCase)).ToList();
        Assert.Equal(old, names);
        Assert.Equal(EntrySortComparer.Name, vm.SortMember);
        Assert.False(vm.SortDescending);
    }

    /// <summary>압축할 목록(사무 파일 이름)도 탐색기 차례: 1월 → 2월 → 10월.</summary>
    [Fact]
    public void 압축할_목록의_숫자_이름도_수_크기_차례()
    {
        using var tmp = new TempDir("sortnat");
        var paths = new[] { "10월 정산.xlsx", "2월 정산.xlsx", "1월 정산.xlsx" }.Select(n =>
        {
            string p = tmp.P(n);
            File.WriteAllText(p, n);
            return p;
        }).ToList();
        var vm = new MainViewModel(new FakeUserInteraction());
        vm.AddToStaging(paths);

        Assert.Equal(new[] { "1월 정산.xlsx", "2월 정산.xlsx", "10월 정산.xlsx" }, vm.VisibleEntries.Select(e => e.Name).ToArray());
    }

    [Fact]
    public async Task 같은_열을_두번_누르면_내림차순()
    {
        var vm = await OpenZipDefault();
        vm.NavigateToFolder("일집_샘플");

        vm.SortBy(EntrySortComparer.Size);
        Assert.Equal(EntrySortComparer.Size, vm.SortMember);
        Assert.False(vm.SortDescending);
        var files = vm.VisibleEntries.Where(e => !e.IsDirectory).Select(e => e.Size).ToList();
        Assert.Equal(files.OrderBy(x => x), files);

        vm.SortBy(EntrySortComparer.Size);
        Assert.True(vm.SortDescending);
        files = vm.VisibleEntries.Where(e => !e.IsDirectory).Select(e => e.Size).ToList();
        Assert.Equal(files.OrderByDescending(x => x), files);
        Assert.True(vm.VisibleEntries.Take(3).All(e => e.IsDirectory), "내림차순에서도 폴더가 먼저");
        Assert.Equal("반복_압축잘됨.txt", vm.VisibleEntries.First(e => !e.IsDirectory).Name);   // 가장 큰 파일(76,800바이트)

        vm.SortBy(EntrySortComparer.Name);   // 다른 열 → 그 열 오름차순
        Assert.Equal(EntrySortComparer.Name, vm.SortMember);
        Assert.False(vm.SortDescending);
    }

    [Fact]
    public async Task 폴더를_옮겨도_정렬이_유지된다()
    {
        var vm = await OpenZipDefault();
        vm.SortBy(EntrySortComparer.Name);   // 이름 내림차순
        Assert.True(vm.SortDescending);

        vm.NavigateToFolder("일집_샘플/하위폴더");
        Assert.Equal(new[] { "2단계", "보고서 최종.txt" }, vm.VisibleEntries.Select(e => e.Name).ToArray());   // 폴더 먼저
        vm.NavigateToFolder("일집_샘플");
        var files = vm.VisibleEntries.Where(e => !e.IsDirectory).Select(e => e.Name).ToList();
        Assert.Equal(files.OrderByDescending(n => n, Comparer<string>.Create(EntrySortComparer.CompareNames)), files);
        var dirs = vm.VisibleEntries.Where(e => e.IsDirectory).Select(e => e.Name).ToList();
        Assert.Equal(new[] { "하위폴더", "빈폴더", "data" }, dirs);
    }

    [Fact]
    public async Task 압축할_목록도_고른_열로_정렬된다()
    {
        using var tmp = new TempDir("sortst");
        string big = tmp.P("큰.txt");
        File.WriteAllText(big, new string('x', 1000));
        string small = tmp.P("작은.txt");
        File.WriteAllText(small, "x");
        var vm = new MainViewModel(new FakeUserInteraction());
        vm.AddToStaging(new[] { small, big, tmp.Dir("폴더") });

        vm.SortBy(EntrySortComparer.Size);
        vm.SortBy(EntrySortComparer.Size);
        Assert.Equal(new[] { "폴더", "큰.txt", "작은.txt" }, vm.VisibleEntries.Select(e => e.Name).ToArray());
        Assert.Equal(new[] { "작은.txt", "큰.txt", "폴더" }, vm.Entries.Select(e => e.Name).ToArray());   // 원본 목록(넣은 차례)은 그대로
    }

    [Fact]
    public void 정렬할_수_없는_열은_무시()
    {
        var vm = new MainViewModel(new FakeUserInteraction());
        vm.SortBy("Path");
        vm.SortBy(null);
        Assert.Equal(EntrySortComparer.Name, vm.SortMember);
        Assert.False(vm.SortDescending);
    }

    // ───────────── XAML 계약(소스) ─────────────

    [Fact]
    public void 이름_열에_SortMemberPath가_있고_정렬은_VM이_맡는다()
    {
        string xaml = RepoFiles.Read("Iljip", "MainWindow.xaml");
        Assert.Matches(new Regex(@"<DataGridTemplateColumn\s+Header=""이름""[^>]*SortMemberPath=""Name"""), xaml);
        Assert.Contains("Sorting=\"EntriesGrid_Sorting\"", xaml);
        // 나머지 열은 바인딩 경로가 곧 정렬 기준 — 그 이름이 VM 이 아는 열이어야 한다
        foreach (Match m in Regex.Matches(xaml, @"<DataGridTextColumn[^>]*Binding=""\{Binding (\w+)"))
            Assert.True(EntrySortComparer.IsSortable(m.Groups[1].Value), $"정렬할 수 없는 열: {m.Groups[1].Value}");

        string code = RepoFiles.Read("Iljip", "MainWindow.xaml.cs");
        Assert.Matches(new Regex(@"EntriesGrid_Sorting\([^)]*\)\s*\{\s*e\.Handled\s*=\s*true;", RegexOptions.Singleline), code);
    }

    [Fact]
    public void 열_머리_템플릿에_손잡이_둘과_정렬_트리거가_있다()
    {
        string styles = RepoFiles.Read("Iljip", "Resources", "Styles.xaml");
        var m = Regex.Match(styles, @"<Style TargetType=""DataGridColumnHeader"">(?<body>.*?)</Style>", RegexOptions.Singleline);
        Assert.True(m.Success, "DataGridColumnHeader 스타일을 못 찾음");
        string body = m.Groups["body"].Value;
        Assert.Contains("x:Name=\"PART_LeftHeaderGripper\"", body);
        Assert.Contains("x:Name=\"PART_RightHeaderGripper\"", body);
        Assert.Contains("<Trigger Property=\"SortDirection\" Value=\"Ascending\">", body);
        Assert.Contains("<Trigger Property=\"SortDirection\" Value=\"Descending\">", body);
        Assert.Contains("{DynamicResource FgMutedBrush}", body);   // 화살표 색은 테마 따라
    }

    /// <summary>스타일 사전을 메모리에서 읽어(창 없이) 머리 템플릿을 적용하면 손잡이 Thumb 둘과 화살표가 실제로 생긴다(XAML 오타·형 변환 오류 잡기).</summary>
    [Fact]
    public void 열_머리_템플릿이_실제로_적용된다()
    {
        Sta.Run(() =>
        {
            var dict = (ResourceDictionary)XamlReader.Parse(RepoFiles.Read("Iljip", "Resources", "Styles.xaml"));
            var style = Assert.IsType<Style>(dict[typeof(DataGridColumnHeader)]);
            var header = new DataGridColumnHeader();
            header.Resources.MergedDictionaries.Add(dict);
            header.Style = style;
            Assert.True(header.ApplyTemplate(), "템플릿이 적용되지 않음");

            var template = header.Template;
            Assert.IsType<Thumb>(template.FindName("PART_LeftHeaderGripper", header));
            Assert.IsType<Thumb>(template.FindName("PART_RightHeaderGripper", header));
            var arrow = Assert.IsType<System.Windows.Shapes.Path>(template.FindName("SortArrow", header));
            Assert.Equal(Visibility.Collapsed, arrow.Visibility);   // 정렬 기준 열이 아니면 숨김

            var sortTriggers = template.Triggers.OfType<Trigger>().Where(t => t.Property == DataGridColumnHeader.SortDirectionProperty).ToList();
            Assert.Equal(2, sortTriggers.Count);
        });
    }
}
