using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using Iljip.Models;
using Iljip.ViewModels;

namespace Iljip;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private MainViewModel? ViewModel => DataContext as MainViewModel;

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        UpdateThemeIcon();

        if (ViewModel is { } vm)
        {
            vm.PropertyChanged += ViewModel_PropertyChanged;
            UpdateColumns();
            UpdateSortArrows();
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
            vm.SelectedFolder = node;
    }

    /// <summary>목록에서 폴더를 더블클릭하면 그 폴더로 진입.</summary>
    private void EntriesGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (ViewModel is not { IsArchiveMode: true } vm) return;
        if (EntriesGrid.SelectedItem is ArchiveEntry { IsDirectory: true } entry)
        {
            vm.NavigateToFolder(entry.Path);
            e.Handled = true;
        }
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
    /// 목록에서 Delete 키 → 스테이징(압축 대기) 모드일 때만 선택 항목 제거.
    /// 압축 파일을 열어 보는 중에는 무시한다.
    /// </summary>
    private void EntriesGrid_PreviewKeyDown(object sender, KeyEventArgs e)
    {
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
