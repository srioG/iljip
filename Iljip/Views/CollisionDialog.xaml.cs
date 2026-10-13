using System.IO;
using System.Windows;
using Iljip.Services;

namespace Iljip.Views;

/// <summary>
/// 풀 때 같은 이름 파일이 이미 있으면 묻는 창(I1-5, W2-05): ‘‘보고서.xlsx’ 파일이 이미 있어요.’ + 있는 파일·새 파일의 크기·날짜,
/// [덮어쓰기][건너뛰기][이름 바꾸기][취소] + ‘남은 파일도 똑같이’. 기본 버튼(Enter)은 [이름 바꾸기](원래 파일을 건드리지 않는 쪽),
/// 창 닫기(X)·Esc 는 [취소].
/// </summary>
public partial class CollisionDialog : Window
{
    public CollisionDialog(CollisionQuestion question)
    {
        InitializeComponent();
        MessageText.Text = $"‘{Path.GetFileName(question.TargetPath)}’ 파일이 이미 있어요.";
        ExistingText.Text = Describe(question.ExistingSize, question.ExistingTime);
        IncomingText.Text = Describe(question.IncomingSize, question.IncomingTime);
        if (question.Remaining > 0)
            ApplyToAllCheck.Content = $"남은 파일도 똑같이 (남은 파일 {question.Remaining}개)";
        Loaded += (_, _) => RenameButton.Focus();
    }

    /// <summary>고른 답(닫으면 [취소]).</summary>
    public CollisionAnswer Answer { get; private set; } = new(CollisionChoice.Cancel, false);

    private void Choose(CollisionChoice choice)
    {
        Answer = new CollisionAnswer(choice, ApplyToAllCheck.IsChecked == true);
        DialogResult = true;
    }

    private void Overwrite_Click(object sender, RoutedEventArgs e) => Choose(CollisionChoice.Overwrite);
    private void Skip_Click(object sender, RoutedEventArgs e) => Choose(CollisionChoice.Skip);
    private void Rename_Click(object sender, RoutedEventArgs e) => Choose(CollisionChoice.Rename);
    private void Cancel_Click(object sender, RoutedEventArgs e) => Choose(CollisionChoice.Cancel);

    /// <summary>'12.3 KB · 2026-10-13 14:05' (크기를 모르면 '크기 모름').</summary>
    internal static string Describe(long size, DateTime? time)
    {
        string s = size < 0 ? "크기 모름" : FormatBytes(size);
        return time is { } t ? $"{s} · {t:yyyy-MM-dd HH:mm}" : s;
    }

    private static string FormatBytes(long bytes)
    {
        const double KB = 1024;
        const double MB = KB * 1024;
        const double GB = MB * 1024;
        if (bytes >= GB) return $"{bytes / GB:F2} GB";
        if (bytes >= MB) return $"{bytes / MB:F2} MB";
        if (bytes >= KB) return $"{bytes / KB:F1} KB";
        return $"{bytes} 바이트";
    }
}
