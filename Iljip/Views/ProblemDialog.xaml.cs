using System.IO;
using System.Windows;
using System.Windows.Controls;
using Iljip.Services;

namespace Iljip.Views;

/// <summary>
/// 문제 창(W2-08 — I2-8 오류 원인 한국어 구분). 첫 화면에는 쉬운 한국어만(무엇을 하다가·문구·어떻게), 영어 원문(예외 형식·엔진 문구·코드·호출 위치)은
/// 접힌 '자세히' 아래에만 — 복사해 담당자에게 보낼 수 있다. '로그 폴더 열기'는 늘 있다. 고를 수 있는 일(<see cref="UserProblem.Actions"/>)은 버튼으로.
/// </summary>
public partial class ProblemDialog : Window
{
    private readonly UserProblem _problem;

    /// <summary>고른 일의 이름(<see cref="ProblemAction.Id"/>). 그냥 닫으면 null.</summary>
    public string? ChosenAction { get; private set; }

    public ProblemDialog(UserProblem problem)
    {
        _problem = problem;
        InitializeComponent();
        Title = $"{problem.Title} - 일집";
        HeadingText.Text = string.IsNullOrEmpty(problem.Heading) ? problem.Title : problem.Heading;
        MessageText.Text = problem.Message;
        MessageText.Visibility = string.IsNullOrEmpty(problem.Message) ? Visibility.Collapsed : Visibility.Visible;
        AdviceText.Text = problem.Advice ?? string.Empty;
        AdviceText.Visibility = string.IsNullOrEmpty(problem.Advice) ? Visibility.Collapsed : Visibility.Visible;
        DetailText.Text = problem.Details;
        DetailExpander.Visibility = string.IsNullOrEmpty(problem.Details) ? Visibility.Collapsed : Visibility.Visible;
        foreach (var action in problem.Actions)
        {
            string id = action.Id;
            var button = new Button
            {
                Content = new TextBlock { Text = action.Label, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(12, 6, 12, 6) },
                MinHeight = 34,
                Margin = new Thickness(0, 0, 0, 8),
                HorizontalContentAlignment = HorizontalAlignment.Stretch,
            };
            button.SetResourceReference(StyleProperty, "DialogButton");
            button.Click += (_, _) =>
            {
                ChosenAction = id;
                DialogResult = true;
            };
            ActionsPanel.Children.Add(button);
        }
        ActionsPanel.Visibility = problem.Actions.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
    }

    private void Copy_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Clipboard.SetText(string.Join("\n\n", new[] { HeadingText.Text, _problem.Message, _problem.Details }.Where(s => !string.IsNullOrEmpty(s))));
        }
        catch (Exception ex)
        {
            // 다른 프로그램이 클립보드를 잡고 있으면 실패할 수 있다 — 창은 그대로
            Logger.LogError("자세한 내용 복사 실패", ex);
        }
    }

    private void OpenLogs_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            string dir = Logger.LogFolder;
            Directory.CreateDirectory(dir);
            new ShellLauncher().OpenFolder(dir);
        }
        catch (Exception ex)
        {
            Logger.LogError("로그 폴더 열기 실패", ex);
        }
    }

    private void Ok_Click(object sender, RoutedEventArgs e) => DialogResult = true;
}
