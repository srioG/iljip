using System.IO;
using System.Windows;
using System.Windows.Input;
using Iljip.Models;
using Iljip.Services;
using Iljip.ViewModels;
using Microsoft.Win32;

namespace Iljip.Views;

/// <summary>
/// 풀기 창(W2-09, I2-13 — 예전 폴더 고르기 창 대신): 풀 곳(최근 푼 곳 목록 + [찾아보기])·'압축파일명으로 폴더 만들기'·
/// '다 풀고 압축 파일을 휴지통으로'·'다 풀면 폴더 열기'. 값과 규칙은 <see cref="ExtractOptionsViewModel"/>(DataContext).
/// [풀기]로 닫으면 <see cref="Result"/>, 취소면 null.
/// </summary>
public partial class ExtractOptionsDialog : Window
{
    private readonly ExtractOptionsViewModel _vm;

    public ExtractOptionsDialog(ExtractOptionsViewModel vm)
    {
        InitializeComponent();
        _vm = vm;
        DataContext = vm;
        Loaded += (_, _) =>
        {
            FolderCombo.Focus();
        };
    }

    /// <summary>[풀기]로 닫았을 때의 답.</summary>
    public ExtractTarget? Result { get; private set; }

    private void Browse_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFolderDialog { Title = "압축을 풀 곳 고르기" };
        // 네트워크 폴더(NAS)는 처음 위치로 넘기지 않는다 — 끊겨 있으면 여기서 창이 굳음(W2-09 검증 지적, FolderProbe)
        string current = _vm.Folder?.Trim() ?? string.Empty;
        if (!FolderProbe.IsNetwork(current) && Directory.Exists(current)) dlg.InitialDirectory = current;
        if (dlg.ShowDialog(this) == true) _vm.Folder = dlg.FolderName;
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        // 폴더 확인은 한 번만(네트워크 폴더는 몇 초 기다릴 수 있음 — 그동안 모래시계)
        string? problem;
        ExtractTarget? result;
        var cursor = Mouse.OverrideCursor;
        Mouse.OverrideCursor = Cursors.Wait;
        try
        {
            problem = _vm.TryBuildResult(out result);
        }
        finally
        {
            Mouse.OverrideCursor = cursor;
        }
        if (problem is not null)
        {
            MessageBox.Show(this, problem, "일집", MessageBoxButton.OK, MessageBoxImage.Warning);
            FolderCombo.Focus();
            return;
        }
        Result = result;
        DialogResult = Result is not null;
    }
}
