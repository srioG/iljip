using System.Windows;
using Iljip.Models;

namespace Iljip.Views;

public partial class ProgressDialog : Window
{
    private readonly CancellationTokenSource _cts = new();

    public ProgressDialog()
    {
        InitializeComponent();
    }

    public CancellationToken CancellationToken => _cts.Token;

    public void UpdateProgress(ArchiveProgress p)
    {
        // 워커 스레드에서 직접 들어오면 UI 스레드로 비동기 마샬링(동기 Invoke로 워커를 막지 않음).
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(() => UpdateProgress(p));
            return;
        }

        CurrentFileText.Text = string.IsNullOrEmpty(p.CurrentFile) ? "완료 중..." : p.CurrentFile;
        Bar.Value = p.Percentage;
        StatusText.Text = $"{p.FilesProcessed} / {p.TotalFiles} 파일  |  {FormatBytes(p.BytesProcessed)} / {FormatBytes(p.TotalBytes)}";
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        _cts.Cancel();
        CurrentFileText.Text = "취소 중...";
    }

    protected override void OnClosed(EventArgs e)
    {
        _cts.Dispose();
        base.OnClosed(e);
    }

    private static string FormatBytes(long bytes)
    {
        const double KB = 1024;
        const double MB = KB * 1024;
        const double GB = MB * 1024;
        if (bytes >= GB) return $"{bytes / GB:F2} GB";
        if (bytes >= MB) return $"{bytes / MB:F2} MB";
        if (bytes >= KB) return $"{bytes / KB:F1} KB";
        return $"{bytes} B";
    }
}
