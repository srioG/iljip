using System.Windows;
using Iljip.Models;

namespace Iljip.Views;

/// <summary>
/// 여러 개를 한꺼번에 한 결과 창(I2-6, W2-05): 요약 한 줄('5개 중 4개를 풀었어요.'), 못 푼 파일 '이름 — 이유', 알림.
/// 날 오류(예외 형식·영어 엔진 문구)는 접힌 '자세히' 아래에만 — 첫 화면에는 쉬운 한국어만.
/// </summary>
public partial class SummaryDialog : Window
{
    public SummaryDialog(OperationSummary summary)
    {
        InitializeComponent();
        Title = $"{summary.Title} - 일집";
        HeadlineText.Text = summary.Headline;
        string all = summary.ToDisplayText();
        // 첫 줄(요약)은 위에 따로 보이므로 본문에서는 뺀다
        int nl = all.IndexOf('\n');
        string body = nl < 0 ? string.Empty : all[(nl + 1)..].TrimStart('\n');
        BodyText.Text = body;
        BodyText.Visibility = body.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
        string detail = summary.DetailText();
        DetailText.Text = detail;
        DetailExpander.Visibility = detail.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
    }

    private void Ok_Click(object sender, RoutedEventArgs e) => DialogResult = true;
}
