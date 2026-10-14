using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using Iljip.Tests.TestSupport;

namespace Iljip.Tests.Contract;

/// <summary>WPF 앱 객체(Application.Current)를 잠깐 만드는 테스트 — 다른 테스트와 동시에 돌리지 않는다(병렬 묶음이 다 끝난 뒤 순차).</summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class WpfApplicationCollection
{
    public const string Name = "순차 실행(WPF 앱 객체)";
}

/// <summary>
/// [W2-06] 메인 창 XAML 이 실제로 읽히는지(띄우지 않고 만들기만 — STA 스레드) + 압축 고치기 화면 장치가 있는지.
/// XAML 오타·없는 자원(StaticResource)은 컴파일로 안 잡히고 앱을 켤 때 터지므로 여기서 만든다.
/// 이름 칸만 편집 가능(F2 — 나머지 열은 읽기 전용), 행 지우기 끔(Del 은 VM 이 묻고 지움), 우클릭·☰ 메뉴에 고치기 항목.
/// 창의 StaticResource 를 찾으려면 앱 객체(Application)가 있어야 해서 잠깐 만들고, 끝나면 Application.Current 를 원래(없음)로 되돌린다
/// — 다른 테스트의 진짜 창 코드(WpfUserInteraction)가 '앱 없음'을 전제로 하므로. 그 사이 다른 테스트가 보지 않게 따로(순차) 돌린다.
/// </summary>
[Collection(WpfApplicationCollection.Name)]
public class MainWindowEditUiTests
{
    [Fact]
    public void 메인_창이_읽히고_고치기_장치가_있다()
    {
        Sta.Run(() =>
        {
            var existing = Application.Current;
            var app = existing ?? new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            bool addedResources = app.Resources.MergedDictionaries.Count == 0;
            if (addedResources)
            {
                app.Resources.MergedDictionaries.Add((ResourceDictionary)XamlReader.Parse(RepoFiles.Read("Iljip", "Resources", "Themes", "Light.xaml")));
                app.Resources.MergedDictionaries.Add((ResourceDictionary)XamlReader.Parse(RepoFiles.Read("Iljip", "Resources", "Styles.xaml")));
            }
            MainWindow? w = null;
            try
            {
                w = new MainWindow();
                var grid = Assert.IsType<DataGrid>(w.FindName("EntriesGrid"));
                Assert.False(grid.IsReadOnly);
                Assert.False(grid.CanUserDeleteRows);
                Assert.False(grid.CanUserAddRows);
                var nameColumn = Assert.IsType<DataGridTemplateColumn>(grid.Columns[0]);
                Assert.Equal("이름", nameColumn.Header);
                Assert.False(nameColumn.IsReadOnly);
                Assert.NotNull(nameColumn.CellEditingTemplate);
                Assert.All(grid.Columns.Skip(1), c => Assert.True(c.IsReadOnly, $"읽기 전용이 아닌 열: {c.Header}"));

                var gridMenu = grid.ContextMenu!.Items.OfType<MenuItem>().Select(m => (string)m.Header).ToList();
                // IJ3-01(I1-4): '열기' 다음에 '복사 (Ctrl+C)'(탐색기에 붙여넣기)
                Assert.Equal(new[] { "선택 항목 압축 풀기", "열기", "복사 (Ctrl+C)", "이름 바꾸기 (F2)", "지우기 (Del)", "파일 넣기...", "새 폴더..." }, gridMenu);

                var menuButton = Assert.IsType<Button>(w.FindName("MenuButton"));
                var menu = menuButton.ContextMenu!.Items.OfType<MenuItem>().Select(m => (string)m.Header).ToList();
                // W2-11(I3-15): 맨 앞에 새 창·다른 이름으로 저장·이 압축 파일 지우기가 더해짐(고치기 항목·설정은 그대로)
                Assert.Equal(new[]
                {
                    "새 창 (Ctrl+N)", "다른 이름으로 저장...", "이 압축 파일 지우기...",
                    "압축 파일에 파일 넣기...", "압축 파일에 폴더 넣기...", "새 폴더...", "이름 바꾸기 (F2)", "지우기 (Del)", "설정...",
                }, menu);

                // W2-11(I3-15): 빈 창의 목록 자리에 시작 화면(큰 단추 셋·최근 파일·모두 지우기)이 읽힌다, Ctrl+N 은 새 창
                var start = Assert.IsType<Iljip.Views.StartView>(w.FindName("StartScreen"));
                Assert.Equal("OpenCommand", CommandPath(Assert.IsType<Button>(start.FindName("OpenButton"))));
                Assert.Equal("StartCompressCommand", CommandPath(Assert.IsType<Button>(start.FindName("CompressButton"))));
                Assert.Equal("StartExtractCommand", CommandPath(Assert.IsType<Button>(start.FindName("ExtractButton"))));
                Assert.Equal("ClearRecentCommand", CommandPath(Assert.IsType<Button>(start.FindName("ClearRecentButton"))));
                // W2-11 검증 지적: 찾을 수 없는 줄만 한꺼번에 빼는 '없는 파일 정리'(그런 줄이 있을 때만 보임)
                var removeMissing = Assert.IsType<Button>(start.FindName("RemoveMissingRecentButton"));
                Assert.Equal("RemoveMissingRecentCommand", CommandPath(removeMissing));
                Assert.Equal("HasMissingRecentFiles", System.Windows.Data.BindingOperations.GetBinding(removeMissing, UIElement.VisibilityProperty)?.Path.Path);
                Assert.Equal("RecentFiles", System.Windows.Data.BindingOperations.GetBinding(
                    Assert.IsType<ItemsControl>(start.FindName("RecentList")), ItemsControl.ItemsSourceProperty)?.Path.Path);
                var ctrlN = w.InputBindings.OfType<System.Windows.Input.KeyBinding>()
                    .Single(k => k.Key == System.Windows.Input.Key.N && k.Modifiers == System.Windows.Input.ModifierKeys.Control);
                Assert.Equal("NewWindowCommand", System.Windows.Data.BindingOperations.GetBinding(ctrlN, System.Windows.Input.InputBinding.CommandProperty)?.Path.Path);
            }
            finally
            {
                w?.Close();
                if (existing is null)
                {
                    // 이 스레드가 끝나면 앱 객체의 Dispatcher 도 죽는다 — 남겨 두면 다른 테스트의 창 코드가 죽은 Dispatcher 로 넘기다 멈춘다
                    var field = typeof(Application).GetField("_appInstance", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
                    Assert.True(field is not null, "WPF 내부 이름이 바뀜(Application._appInstance) — 앱 객체를 되돌리지 못함");
                    field!.SetValue(null, null);
                    Assert.Null(Application.Current);
                }
                else if (addedResources)
                {
                    app.Resources.MergedDictionaries.Clear();
                }
            }
        });
    }

    private static string? CommandPath(Button b) =>
        System.Windows.Data.BindingOperations.GetBinding(b, System.Windows.Controls.Primitives.ButtonBase.CommandProperty)?.Path.Path;

    /// <summary>
    /// 이름 바꾸기 칸은 F2·메뉴로만 열리고(그 밖의 편집 시작은 막음), 이름 칸을 고치는 중의 키(Del·F2)는 그 칸에 맡긴다,
    /// 이름을 확정하면 편집을 먼저 끝낸 뒤(Dispatcher.Yield) VM 에 넘긴다. (소스 확인 — 창을 띄우지 않고는 키를 보낼 수 없어서)
    /// </summary>
    [Fact]
    public void 이름_바꾸기_칸은_F2로만_열리고_확정은_편집을_끝낸_뒤()
    {
        string code = RepoFiles.Read("Iljip", "MainWindow.xaml.cs");
        Assert.Matches(@"EntriesGrid_BeginningEdit\([^)]*\)\s*\{\s*if \(!_renameRequested", code);
        Assert.Matches(@"EntriesGrid_PreviewKeyDown\([^)]*\)\s*\{\s*if \(e\.OriginalSource is TextBox\) return;", code);
        int yield = code.IndexOf("await Dispatcher.Yield(DispatcherPriority.Background);\n        await vm.RenameEntryAsync", StringComparison.Ordinal);
        if (yield < 0) yield = code.IndexOf("await Dispatcher.Yield(DispatcherPriority.Background);\r\n        await vm.RenameEntryAsync", StringComparison.Ordinal);
        Assert.True(yield > 0, "이름 확정 전에 편집을 끝내지 않음(Dispatcher.Yield 없음)");
        Assert.Contains("Closing += Window_Closing;", code);
        Assert.Contains("ViewModel?.Shutdown()", code);
    }
}
