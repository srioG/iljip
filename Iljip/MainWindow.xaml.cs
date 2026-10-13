using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Iljip.Models;
using Iljip.Services;
using Iljip.ViewModels;

namespace Iljip;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        Closing += Window_Closing;
        Closed += (_, _) =>
        {
            ViewModel?.RememberTreeWidth(_treeWidth);   // W2-09(I2-14): 막대로 바꾼 트리 너비 — 닫을 때 한 번만 저장
            ViewModel?.Shutdown();   // W2-06: 연 파일 감시를 멈추고 임시 작업 폴더를 지운다
        };
    }

    /// <summary>왼쪽 폴더 트리 너비(W2-09 — 처음은 설정 ui.treeWidth, 막대를 끌면 바뀜). 압축을 보지 않을 때는 트리 칸을 0 으로 접는다.</summary>
    private double _treeWidth = UiSettings.DefaultTreeWidth;

    /// <summary>W2-06(I3-3): 고친 파일을 다시 넣을지 물은 뒤 닫기로 했다(두 번째 Close 는 묻지 않음).</summary>
    private bool _exitConfirmed;

    /// <summary>
    /// 창 닫기(W2-06, I3-3): 압축 속에서 연 파일을 고쳐 놓고 아직 넣지 않았으면 하나씩 묻는다([넣기][넣지 않기(닫기)][취소]).
    /// 묻는 동안은 닫기를 멈추고, 다 끝나면 다시 닫는다. [취소]면 닫지 않는다.
    /// </summary>
    private async void Window_Closing(object? sender, CancelEventArgs e)
    {
        if (_exitConfirmed || ViewModel is not { HasOpenedEntries: true } vm) return;
        e.Cancel = true;
        bool close;
        try
        {
            close = await vm.ConfirmExitAsync();
        }
        catch (Exception ex)
        {
            Logger.LogError("닫기 전 고친 파일 확인 실패", ex);
            close = true;
        }
        if (!close) return;
        _exitConfirmed = true;
        Close();
    }

    private MainViewModel? ViewModel => DataContext as MainViewModel;

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        UpdateThemeIcon();

        if (ViewModel is { } vm)
        {
            vm.PropertyChanged += ViewModel_PropertyChanged;
            _treeWidth = vm.TreeWidth;
            UpdateColumns();
            UpdateSortArrows();
            UpdateTreeColumn();
        }
    }

    private void ViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        // 모드가 바뀌면 컬럼 표시를 갱신
        if (e.PropertyName is nameof(MainViewModel.IsArchiveMode)
            or nameof(MainViewModel.IsStagingMode)
            or nameof(MainViewModel.IsEmptyState))
        {
            UpdateColumns();
            UpdateTreeColumn();
        }
        else if (e.PropertyName is nameof(MainViewModel.IsSearching))
        {
            UpdateColumns();
        }
        else if (e.PropertyName is nameof(MainViewModel.SortMember) or nameof(MainViewModel.SortDescending))
        {
            UpdateSortArrows();
        }
    }

    /// <summary>
    /// 열 머리를 누르면: DataGrid 기본 정렬(CollectionView — 폴더·파일이 섞이고, 폴더를 옮기면 풀림)을 막고 VM 이 정렬한다(I0-5).
    /// 화살표는 <see cref="UpdateSortArrows"/> 가 VM 상태대로 그린다.
    /// </summary>
    private void EntriesGrid_Sorting(object sender, System.Windows.Controls.DataGridSortingEventArgs e)
    {
        e.Handled = true;
        if (ViewModel is not { } vm) return;
        vm.SortBy(e.Column.SortMemberPath);
        UpdateSortArrows();
    }

    /// <summary>정렬 기준 열에만 화살표(오름 ▲ / 내림 ▼). 머리줄 템플릿(Styles.xaml)이 SortDirection 으로 그린다.</summary>
    private void UpdateSortArrows()
    {
        if (ViewModel is not { } vm) return;
        foreach (var col in EntriesGrid.Columns)
        {
            col.SortDirection = col.SortMemberPath == vm.SortMember
                ? vm.SortDescending ? ListSortDirection.Descending : ListSortDirection.Ascending
                : null;
        }
    }

    /// <summary>압축 크기·압축률 컬럼은 압축 파일을 열어 본 경우에만 의미가 있으므로 그때만 표시.</summary>
    private void UpdateColumns()
    {
        if (ViewModel is not { } vm) return;
        var vis = vm.IsArchiveMode ? Visibility.Visible : Visibility.Collapsed;
        ColCompressed.Visibility = vis;
        ColRatio.Visibility = vis;
        // W2-09(I2-14): 이름으로 찾는 중에만 '경로'(들어 있는 폴더)
        ColFolder.Visibility = vm.IsArchiveMode && vm.IsSearching ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>트리 칸 너비: 압축을 보는 중이면 기억한 너비(가르는 막대로 바꿀 수 있음), 아니면 0(트리를 접음).</summary>
    private void UpdateTreeColumn()
    {
        if (ViewModel is not { } vm) return;
        if (vm.IsArchiveMode)
        {
            TreeColumn.MinWidth = 120;
            TreeColumn.Width = new GridLength(_treeWidth);
        }
        else
        {
            if (TreeColumn.ActualWidth >= 120) _treeWidth = TreeColumn.ActualWidth;
            TreeColumn.MinWidth = 0;
            TreeColumn.Width = new GridLength(0);
        }
    }

    /// <summary>가르는 막대를 다 끌었으면 그 너비를 기억한다(저장은 창을 닫을 때 한 번).</summary>
    private void TreeSplitter_DragCompleted(object sender, DragCompletedEventArgs e)
    {
        if (TreeColumn.ActualWidth >= 120) _treeWidth = TreeColumn.ActualWidth;
    }

    // ───── W2-09(I2-14) 둘러보기: 이름으로 찾기·Backspace 위 폴더·선택 합계 ─────

    /// <summary>Ctrl+F: 이름으로 찾기 칸으로(압축을 보는 중일 때).</summary>
    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.F && Keyboard.Modifiers == ModifierKeys.Control && ViewModel is { IsArchiveMode: true })
        {
            e.Handled = true;
            SearchBox.Focus();
            SearchBox.SelectAll();
        }
    }

    /// <summary>찾기 칸: Esc = 찾기 지우기, Enter = 기다리지 않고 바로 찾기, ↓ = 목록으로.</summary>
    private void SearchBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (ViewModel is not { } vm) return;
        if (e.Key == Key.Escape)
        {
            e.Handled = true;
            vm.ClearSearch();
        }
        else if (e.Key == Key.Enter)
        {
            e.Handled = true;
            vm.ApplySearchNow();
        }
        else if (e.Key == Key.Down && EntriesGrid.Items.Count > 0)
        {
            e.Handled = true;
            if (EntriesGrid.SelectedIndex < 0) EntriesGrid.SelectedIndex = 0;
            EntriesGrid.Focus();
        }
    }

    /// <summary>목록에서 고른 것이 바뀌면 상태 줄의 '선택: N개 · 크기'를 고친다.</summary>
    private void EntriesGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ViewModel is not { } vm) return;
        vm.UpdateSelection(EntriesGrid.SelectedItems.OfType<ArchiveEntry>().ToList());
    }

    private void Theme_Click(object sender, RoutedEventArgs e)
    {
        App.ToggleTheme();
        UpdateThemeIcon();
    }

    /// <summary>다크 모드면 '해'(라이트로 전환), 라이트 모드면 '달'(다크로 전환) 아이콘을 보여준다.</summary>
    private void UpdateThemeIcon()
    {
        bool dark = App.CurrentTheme == AppTheme.Dark;
        if (FindResource(dark ? "IconSun" : "IconMoon") is Geometry g)
            ThemeIcon.Data = g;
    }

    /// <summary>좌측 폴더 트리에서 폴더를 선택하면 우측 목록을 그 폴더 내용으로 갱신.</summary>
    private void FolderTree_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        if (ViewModel is { } vm && e.NewValue is ArchiveTreeNode node)
            vm.GoToFolder(node);   // W2-09: 찾는 중이면 찾기를 지우고 그 폴더로
    }

    /// <summary>목록에서 폴더를 더블클릭하면 그 폴더로 진입. 파일이면 꺼내서 기본 프로그램으로 연다(I1-3 — W2-06 이 최소로 들임).</summary>
    private async void EntriesGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (ViewModel is not { IsArchiveMode: true } vm) return;
        if (e.OriginalSource is DependencyObject src && FindParent<DataGridColumnHeader>(src) is not null) return;   // 열 머리 두 번 누르기
        if (EntriesGrid.SelectedItem is ArchiveEntry { IsDirectory: true } entry)
        {
            vm.GoToFolder(entry.Path);   // W2-09: 찾은 결과의 폴더면 찾기를 지우고 그 폴더로
            e.Handled = true;
        }
        else if (EntriesGrid.SelectedItem is ArchiveEntry file)
        {
            e.Handled = true;
            await vm.OpenEntryAsync(file);
        }
    }

    /// <summary>우클릭 메뉴 → 열기(파일은 기본 프로그램으로, 폴더는 들어감).</summary>
    private async void OpenSelected_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel is not { IsArchiveMode: true } vm) return;
        if (EntriesGrid.SelectedItem is ArchiveEntry entry) await vm.OpenEntryAsync(entry);
    }

    // ───── W2-06(I2-3) 압축 고치기: 지우기(Del)·이름 바꾸기(F2 — 목록 안에서) ─────

    /// <summary>[지우기]·우클릭 '지우기': 고른 항목을 압축 파일에서 지운다(VM 이 먼저 묻는다).</summary>
    private async void DeleteSelected_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel is not { IsArchiveMode: true } vm) return;
        var selected = EntriesGrid.SelectedItems.OfType<ArchiveEntry>().ToList();
        if (selected.Count == 0)
        {
            vm.StatusText = "지울 항목을 먼저 골라 주세요";
            return;
        }
        await vm.DeleteEntriesAsync(selected);
    }

    /// <summary>[이름 바꾸기]·우클릭 '이름 바꾸기': 고른 항목 하나의 이름 칸을 바로 고칠 수 있게 연다(F2 와 같음).</summary>
    private void RenameSelected_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel is not { IsArchiveMode: true } vm) return;
        if (EntriesGrid.SelectedItem is not ArchiveEntry entry)
        {
            vm.StatusText = "이름을 바꿀 항목을 먼저 골라 주세요";
            return;
        }
        StartRename(vm, entry);
    }

    /// <summary>이름 바꾸기를 우리가 시작했는가 — 그 밖의 편집(글자 입력·두 번 누르기)은 막는다.</summary>
    private bool _renameRequested;

    /// <summary>이름 칸을 편집 상태로(고칠 수 없는 형식이면 VM 이 '이 형식은 고칠 수 없어요' 안내).</summary>
    private void StartRename(MainViewModel vm, ArchiveEntry entry)
    {
        if (!vm.CheckEditable()) return;
        EntriesGrid.SelectedItem = entry;
        EntriesGrid.ScrollIntoView(entry);
        EntriesGrid.CurrentCell = new DataGridCellInfo(entry, ColName);
        EntriesGrid.Focus();
        _renameRequested = true;
        try
        {
            EntriesGrid.BeginEdit();
        }
        finally
        {
            _renameRequested = false;
        }
    }

    /// <summary>편집은 이름 바꾸기(F2·메뉴)로 시작한 것만 — 글자를 치거나 고른 칸을 다시 눌러 편집이 시작되지 않게.</summary>
    private void EntriesGrid_BeginningEdit(object? sender, DataGridBeginningEditEventArgs e)
    {
        if (!_renameRequested || ViewModel is not { IsArchiveMode: true } || e.Column != ColName) e.Cancel = true;
    }

    /// <summary>이름 칸이 열리면 글자 칸에 포커스, 확장자 앞까지 골라 둔다(탐색기와 같게).</summary>
    private void RenameBox_Loaded(object sender, RoutedEventArgs e)
    {
        if (sender is not TextBox box) return;
        box.Focus();
        string text = box.Text;
        string ext = Path.GetExtension(text);
        bool isDir = box.DataContext is ArchiveEntry { IsDirectory: true };
        int stem = !isDir && ext.Length > 0 && ext.Length < text.Length ? text.Length - ext.Length : text.Length;
        box.Select(0, stem);
    }

    /// <summary>
    /// 이름 칸을 마쳤을 때(Enter·다른 곳 누르기 = 확정, Esc = 취소): 확정이면 VM 이 압축 안 이름을 바꾸고 목록을 다시 읽는다.
    /// 목록을 다시 읽기 전에 편집을 먼저 끝낸다(Dispatcher.Yield — 편집 중에 목록을 비우면 DataGrid 가 예외를 냄).
    /// </summary>
    private async void EntriesGrid_CellEditEnding(object? sender, DataGridCellEditEndingEventArgs e)
    {
        if (e.EditAction != DataGridEditAction.Commit) return;
        if (e.Row.Item is not ArchiveEntry entry || ViewModel is not { } vm) return;
        string text = FindChild<TextBox>(e.EditingElement)?.Text ?? string.Empty;
        await Dispatcher.Yield(DispatcherPriority.Background);
        await vm.RenameEntryAsync(entry, text);
    }

    private static T? FindChild<T>(DependencyObject? root) where T : DependencyObject
    {
        if (root is null) return null;
        if (root is T hit) return hit;
        int n = VisualTreeHelper.GetChildrenCount(root);
        for (int i = 0; i < n; i++)
            if (FindChild<T>(VisualTreeHelper.GetChild(root, i)) is { } found) return found;
        return null;
    }

    private static T? FindParent<T>(DependencyObject? node) where T : DependencyObject
    {
        while (node is not null and not T)
            node = node is Visual or System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetParent(node) : LogicalTreeHelper.GetParent(node);
        return node as T;
    }

    /// <summary>우클릭 메뉴 → 선택한 항목만 압축 풀기.</summary>
    private async void ExtractSelected_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel is not { } vm) return;
        var selected = EntriesGrid.SelectedItems.OfType<ArchiveEntry>().ToList();
        if (selected.Count == 0) return;
        await vm.ExtractSelectedAsync(selected);
    }

    /// <summary>
    /// 목록에서 Delete 키 → 스테이징(압축 대기) 모드면 선택 항목을 목록에서 뺀다.
    /// W2-06: 압축 파일을 보는 중이면 Del = 압축 파일에서 지우기(VM 이 먼저 묻는다), F2 = 이름 바꾸기, Enter = 열기.
    /// 이름 칸을 고치는 중(글자 칸)의 키는 그 칸에 맡긴다.
    /// </summary>
    private async void EntriesGrid_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.OriginalSource is TextBox) return;
        if (ViewModel is { IsArchiveMode: true } avm)
        {
            // W2-09(I2-14): Backspace = 위 폴더(찾는 중이면 찾기 지우기), Esc = 찾기 지우기.
            // 이름을 고치는 중(글자 칸)의 키는 맨 위에서 그 칸에 맡겼다 — 글자를 지우는 Backspace 가 폴더를 옮기지 않게.
            if (e.Key == Key.Back)
            {
                e.Handled = true;
                avm.UpCommand.Execute(null);
                return;
            }
            if (e.Key == Key.Escape && avm.IsSearching)
            {
                e.Handled = true;
                avm.ClearSearch();
                return;
            }
            if (e.Key == Key.Delete)
            {
                var picked = EntriesGrid.SelectedItems.OfType<ArchiveEntry>().ToList();
                e.Handled = true;
                if (picked.Count > 0) await avm.DeleteEntriesAsync(picked);
            }
            else if (e.Key == Key.F2 && EntriesGrid.SelectedItem is ArchiveEntry one)
            {
                e.Handled = true;
                StartRename(avm, one);
            }
            else if (e.Key == Key.Enter && EntriesGrid.SelectedItem is ArchiveEntry target)
            {
                e.Handled = true;
                await avm.OpenEntryAsync(target);
            }
            return;
        }
        if (e.Key != Key.Delete) return;
        if (ViewModel is not { IsStagingMode: true } vm) return;

        var selected = EntriesGrid.SelectedItems
            .OfType<ArchiveEntry>()
            .ToList();
        if (selected.Count == 0) return;

        vm.RemoveEntries(selected);
        e.Handled = true;
    }

    private void Window_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop)
            ? DragDropEffects.Copy
            : DragDropEffects.None;
        e.Handled = true;
    }

    /// <summary>
    /// 끌어 놓기. 경로만 먼저 꺼내 두고 Drop 이벤트를 곧바로 끝낸 뒤(<see cref="System.Windows.Threading.Dispatcher.Yield()"/>) 처리한다 —
    /// OLE 끌어 놓기는 받는 쪽 Drop 이 끝날 때까지 끌기를 시작한 탐색기 창이 기다리므로, 그 안에서 선택 창(모달)을 띄우면
    /// 창이 떠 있는 동안 그 탐색기 창이 멈춘다(W1-D 1차 검증). 꺼낸 뒤의 끌기 데이터는 쓰지 않는다.
    /// </summary>
    private async void Window_Drop(object sender, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return;
        if (e.Data.GetData(DataFormats.FileDrop) is not string[] paths) return;
        if (paths.Length == 0) return;
        if (ViewModel is not { } vm) return;

        await System.Windows.Threading.Dispatcher.Yield(System.Windows.Threading.DispatcherPriority.Background);
        await vm.HandleDroppedPathsAsync(paths);
    }

    /// <summary>'일집 정보' 창: 버전 + 7z.dll 상태 + 공개 소프트웨어 안내·라이선스 전문(I2-15).</summary>
    private void About_Click(object sender, RoutedEventArgs e)
    {
        new Views.AboutWindow { Owner = this }.ShowDialog();
    }

    /// <summary>☰ 단추: 아래로 메뉴를 펼친다(W2-05 — 설정).</summary>
    private void Menu_Click(object sender, RoutedEventArgs e)
    {
        if (MenuButton.ContextMenu is not { } menu) return;
        menu.PlacementTarget = MenuButton;
        menu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
        menu.IsOpen = true;
    }

    /// <summary>☰ ▸ 설정: 같은 이름이 있을 때(I1-5)·우클릭으로 푼 뒤 폴더 열기(I1-6)·다 푼 뒤 압축 파일 휴지통으로(I2-5). [확인]이면 한 번 저장.</summary>
    private void Settings_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel is not { } vm) return;
        var dlg = new Views.SettingsDialog(vm.Settings) { Owner = this };
        if (dlg.ShowDialog() == true && dlg.Result is { } result)
            vm.ApplySettings(result);
    }
}
