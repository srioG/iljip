using System.Windows;
using System.Windows.Threading;
using Iljip.Models;

namespace Iljip.Views;

public partial class ProgressDialog : Window
{
    private readonly CancellationTokenSource _cts = new();
    private bool _cancelling;

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

        bool unknownLength = p.IsStage && p.TotalBytes <= 0;
        double? bar = BarValue(p);
        Bar.IsIndeterminate = bar is null;
        if (bar is { } value) Bar.Value = value;
        // 파일 수를 미리 모르는 형식(.tar.gz 등)은 크기만 보인다
        string files = p.TotalFiles > 0 ? $"{p.FilesProcessed} / {p.TotalFiles} 파일  |  " : string.Empty;
        StatusText.Text = unknownLength ? string.Empty : $"{files}{FormatBytes(p.BytesProcessed)} / {FormatBytes(p.TotalBytes)}";
        // 취소를 누른 뒤에는 '취소하는 중' 안내를 지우지 않는다
        if (!_cancelling)
        {
            string file = string.IsNullOrEmpty(p.CurrentFile) ? "완료 중..." : p.CurrentFile;
            CurrentFileText.Text = p.BatchCount > 1
                ? $"{p.BatchIndex}/{p.BatchCount} · {p.BatchName} — {file}"
                : file;
        }
    }

    /// <summary>
    /// 막대 값(0~100), 또는 null 이면 움직이는 막대(얼마나 남았는지 모름). 크기를 모르는 단계 알림(W2-07 — '만든 압축 파일을 시험하는 중' 등)은
    /// 움직이는 막대(멈춘 것처럼 보이지 않게). 단 여러 개를 차례로 하는 중의 단계 알림(2/5 — 각각 따로 압축의 '원본을 살펴보는 중')은 막대를 그대로
    /// '앞서 끝난 수 / 전체'. 여러 개 풀기(W2-05, I2-6)면 막대는 전체, 맨 윗줄은 '2/5 · b.zip — 지금 파일'.
    /// (만든 압축 시험은 다 만든 뒤라 여러 개여도 '2/5'를 붙이지 않고 움직이는 막대로 보낸다 — 막대가 100% 에서 0% 로 돌아가지 않게, W2-07 검증 지적 2차)
    /// </summary>
    internal static double? BarValue(ArchiveProgress p) =>
        p.IsStage && p.TotalBytes <= 0 && p.BatchCount <= 1 ? null : p.OverallPercentage;

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        if (_cancelling) return;
        _cancelling = true;
        _cts.Cancel();
        CancelButton.IsEnabled = false;
        CurrentFileText.Text = "취소하는 중이에요…";
        // 7z.dll 의 일부 경로(여러 조각 RAR 등 — 경로로 여는 것)는 지금 풀고 있는 파일을 끝낸 뒤에야 멈춘다(W2-01) —
        // 오래 걸리면 이유를 알려 준다.
        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            if (IsLoaded) CurrentFileText.Text = "지금 하던 부분을 마무리하는 중이에요. 곧 멈춰요.";
        };
        timer.Start();
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
