using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
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
            // W2-09(I2-14): 막대로 바꾼 트리 너비 + W2-11(I3-15): 이번에 연 최근 파일 — 닫을 때 바뀐 것이 있으면 한 번만 저장
            ViewModel?.SaveOnExit(_treeWidth);
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
            vm.RefreshRecentFiles();   // W2-11(I3-15): 시작 화면 '최근 파일'
            vm.Preview.PropertyChanged += Preview_PropertyChanged;
            vm.ApplyStartupSettings();   // IJ3-01(I3-7): 미리 보기 칸 켜 둔 것(settings.json ui.previewVisible)
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
        else if (e.PropertyName is nameof(MainViewModel.IsStartVisible) && ViewModel is { IsStartVisible: true } svm && !_recentRefreshQueued)
        {
            // W2-11: 시작 화면으로 돌아오면(닫기·지우기) 최근 파일을 다시 본다(그새 지운 파일은 '없음') — 한꺼번에 여러 번 알려 와도 한 번만
            _recentRefreshQueued = true;
            Dispatcher.BeginInvoke(DispatcherPriority.Background, () =>
            {
                _recentRefreshQueued = false;
                if (svm.IsStartVisible) svm.RefreshRecentFiles();
            });
        }
        else if (e.PropertyName is nameof(MainViewModel.SelectedEntry) && ViewModel?.SelectedEntry is { } sel)
        {
            // IJ3-01(I3-7): 미리 보기에서 이전/다음으로 넘기면(목록 선택은 SelectedItem 묶기로 따라감) 그 줄이 보이게
            EntriesGrid.ScrollIntoView(sel);
        }
    }

    // ===== IJ3-01(W2-04 다시): 끌어 꺼내기·Ctrl+C·미리 보기 (논리는 VM — 여기는 손 점검 대상 껍데기) =====

    private List<ArchiveEntry> SelectedEntries() => EntriesGrid.SelectedItems.OfType<ArchiveEntry>().ToList();

    /// <summary>우클릭 '복사 (Ctrl+C)' — 고른 것을 꺼내 클립보드에(탐색기에서 붙여넣기).</summary>
    private async void CopySelected_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel is not { IsArchiveMode: true } vm) return;
        var selected = SelectedEntries();
        if (selected.Count > 0) await vm.CopyEntriesToClipboardAsync(selected);
    }

    // --- 끌어 꺼내기: 끌기 임계값을 넘으면 ole32 DoDragDrop(VirtualFileDataObject — 놓은 순간에 꺼냄) ---

    private Point? _dragStart;
    private ArchiveEntry? _selectOnMouseUp;

    private void EntriesGrid_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _dragStart = null;
        _selectOnMouseUp = null;
        if (ViewModel is not { IsArchiveMode: true }) return;
        if (FindParent<DataGridRow>(e.OriginalSource as DependencyObject) is not { } row) return;
        if (row.IsEditing) return;   // 이름을 고치는 중(F2)의 글자 고르기는 끌기가 아님
        _dragStart = e.GetPosition(EntriesGrid);
        // 여러 개를 고른 채 그중 하나를 잡아 끌 때 선택이 하나로 줄지 않게 — 끌지 않고 놓으면 그때 그것만 고른다
        if (e.ClickCount == 1 && row.IsSelected && EntriesGrid.SelectedItems.Count > 1 && Keyboard.Modifiers == ModifierKeys.None)
        {
            _selectOnMouseUp = row.Item as ArchiveEntry;
            EntriesGrid.Focus();
            e.Handled = true;
        }
    }

    private void EntriesGrid_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (_selectOnMouseUp is { } item)
        {
            EntriesGrid.SelectedItems.Clear();
            EntriesGrid.SelectedItem = item;
        }
        _selectOnMouseUp = null;
        _dragStart = null;
    }

    private async void EntriesGrid_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (_dragStart is not { } start || e.LeftButton != MouseButtonState.Pressed) return;
        var pos = e.GetPosition(EntriesGrid);
        if (Math.Abs(pos.X - start.X) < SystemParameters.MinimumHorizontalDragDistance
            && Math.Abs(pos.Y - start.Y) < SystemParameters.MinimumVerticalDragDistance) return;
        _dragStart = null;
        _selectOnMouseUp = null;
        if (ViewModel is not { IsArchiveMode: true } vm) return;
        var selected = SelectedEntries();
        if (selected.Count == 0) return;

        // 암호는 끌기 전에만 묻는다(끄는 도중에는 못 물음) — 물었으면 마우스를 놓은 뒤라 이번 끌기는 하지 않고 '다시 끌어 주세요'
        if (vm.DragNeedsPassword(selected))
        {
            await vm.EnsurePasswordForDragAsync(selected);
            return;
        }
        var data = vm.CreateDragData(selected);
        if (data is null) return;
        try
        {
            EntriesGrid.ReleaseMouseCapture();
            Mouse.Capture(null);
            data.DoDragDrop(DragDropEffects.Copy);
        }
        catch (Exception ex)
        {
            Iljip.Services.Logger.LogError("끌어 꺼내기 실패", ex);
        }
    }

    // --- 미리 보기 확대(맞춤 / 비율) ---

    private void Preview_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(ImagePreviewViewModel.IsFit) or nameof(ImagePreviewViewModel.ZoomPercent) or nameof(ImagePreviewViewModel.Image))
            UpdatePreviewZoom();
    }

    private void PreviewScroll_SizeChanged(object sender, SizeChangedEventArgs e) => UpdatePreviewZoom();

    /// <summary>맞춤이면 칸에 맞게(스크롤 없음), 비율이면 그림 화소 × 비율(스크롤). 맞춤일 때 실제 비율을 VM 에 알려 +/− 의 출발점으로.</summary>
    private void UpdatePreviewZoom()
    {
        if (ViewModel?.Preview is not { } p) return;
        var bmp = p.Image as BitmapSource;
        if (bmp is not null && PreviewScroll.ActualWidth > 0 && PreviewScroll.ActualHeight > 0)
            p.FitPercent = Math.Min(100.0 * PreviewScroll.ActualWidth / bmp.PixelWidth, 100.0 * PreviewScroll.ActualHeight / bmp.PixelHeight);
        if (p.IsFit || bmp is null)
        {
            PreviewScroll.HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;
            PreviewScroll.VerticalScrollBarVisibility = ScrollBarVisibility.Disabled;
            PreviewImage.Stretch = Stretch.Uniform;
            PreviewImage.Width = double.NaN;
            PreviewImage.Height = double.NaN;
        }
        else
        {
            PreviewScroll.HorizontalScrollBarVisibility = ScrollBarVisibility.Auto;
            PreviewScroll.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
            PreviewImage.Stretch = Stretch.Fill;
            PreviewImage.Width = bmp.PixelWidth * p.ZoomScale;
            PreviewImage.Height = bmp.PixelHeight * p.ZoomScale;
        }
    }

    /// <summary>시작 화면 최근 파일 다시 보기를 이미 걸어 두었는가.</summary>
    private bool _recentRefreshQueued;

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

    /// <summary>목록에서 폴더를 더블클릭하면 그 폴더로 진입. 파일이면 꺼내서 기본 프로그램으로 연다(I1-3).</summary>
    private async void EntriesGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (ViewModel is not { IsArchiveMode: true } vm) return;
        if (FindParent<DataGridRow>(e.OriginalSource as DependencyObject) is null) return;   // 열 머리·스크롤 막대 두 번 누르기는 무시(줄에서만)
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
    /// IJ3-01: Ctrl+C = 꺼내서 클립보드에, 미리 보기 칸을 켰으면 ←→ = 이전/다음 그림.
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
            // IJ3-01(I1-4): Ctrl+C = 고른 것을 꺼내 클립보드에(탐색기에 붙여넣기 — 뒤에서 꺼내고 진행 창·취소)
            if (e.Key == Key.C && Keyboard.Modifiers == ModifierKeys.Control)
            {
                e.Handled = true;
                var toCopy = SelectedEntries();
                if (toCopy.Count > 0) await avm.CopyEntriesToClipboardAsync(toCopy);
                return;
            }
            // IJ3-01(I3-7): 미리 보기 칸을 켰으면 ←→ 로 그 폴더의 그림 넘기기
            if (avm.IsPreviewPaneVisible && e.Key is Key.Left or Key.Right && Keyboard.Modifiers == ModifierKeys.None)
            {
                e.Handled = true;
                if (e.Key == Key.Left) await avm.Preview.PrevCommand.ExecuteAsync(null);
                else await avm.Preview.NextCommand.ExecuteAsync(null);
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
        var dlg = new Views.SettingsDialog(vm.Settings, vm) { Owner = this };   // W2-12: '파일 연결' 단추는 이 창(VM)으로
        if (dlg.ShowDialog() == true && dlg.Result is { } result)
            vm.ApplySettings(result);
    }
}
