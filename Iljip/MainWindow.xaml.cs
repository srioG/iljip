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

    private async void Window_Drop(object sender, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return;
        if (e.Data.GetData(DataFormats.FileDrop) is not string[] paths) return;
        if (paths.Length == 0) return;
        if (ViewModel is null) return;

        await ViewModel.HandleDroppedPathsAsync(paths);
    }

    private void About_Click(object sender, RoutedEventArgs e)
    {
        var asm = System.Reflection.Assembly.GetExecutingAssembly();
        // csproj <Version>이 반영된 어셈블리 버전을 동적으로 표시 (하드코딩 방지)
        string version = asm.GetName().Version?.ToString(3) ?? "0.0.0";

        MessageBox.Show(
            $"일집 (Iljip)\n광고 없는 압축 프로그램\n\nVersion {version}",
            "일집 정보",
            MessageBoxButton.OK,
            MessageBoxImage.Information);
    }
}
