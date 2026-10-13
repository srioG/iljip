using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace Iljip.Views;

/// <summary>
/// 문구 하나와 버튼 여러 개(위에서 아래로, 긴 설명을 담을 수 있게 줄바꿈)를 보여 주고 고른 버튼 순번을 돌려주는 창.
/// 기본 버튼(Enter·처음 포커스)은 정해 준 순번(안 정하면 첫 버튼). 닫기(X)·Esc 는 -1.
/// 끌어 놓기 선택 창은 [취소]를 기본으로 준다(W1-D 1차 검증 — Enter 한 번에 보던 압축이 닫히지 않게).
/// </summary>
public partial class ChoiceDialog : Window
{
    /// <summary>고른 버튼 순번(0부터). 고르지 않고 닫으면 -1.</summary>
    public int ChosenIndex { get; private set; } = -1;

    /// <summary>확인란을 켰는가(<c>checkText</c> 를 주었을 때만 보임 — W2-10 '다시 묻지 않기').</summary>
    public bool IsChecked => OptionCheck.IsChecked == true;

    /// <param name="checkText">주면 버튼 아래에 이 글자의 확인란을 보인다(W2-10).</param>
    public ChoiceDialog(string message, IReadOnlyList<string> choices, int defaultIndex = 0, string? checkText = null)
    {
        if (defaultIndex < 0 || defaultIndex >= choices.Count) defaultIndex = 0;
        InitializeComponent();
        MessageText.Text = message;
        if (!string.IsNullOrEmpty(checkText))
        {
            OptionCheck.Content = checkText;
            OptionCheck.Visibility = Visibility.Visible;
        }
        for (int i = 0; i < choices.Count; i++)
        {
            int index = i;
            var button = new Button
            {
                Content = new TextBlock { Text = choices[i], TextWrapping = TextWrapping.Wrap, Margin = new Thickness(12, 6, 12, 6) },
                MinHeight = 34,
                Margin = new Thickness(0, 0, 0, 8),
                HorizontalContentAlignment = HorizontalAlignment.Stretch,
                IsDefault = i == defaultIndex,
            };
            button.SetResourceReference(StyleProperty, "DialogButton");
            button.Click += (_, _) =>
            {
                ChosenIndex = index;
                DialogResult = true;
            };
            ButtonsPanel.Children.Add(button);
        }
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape) DialogResult = false;
        };
        Loaded += (_, _) => (ButtonsPanel.Children.Count > defaultIndex ? ButtonsPanel.Children[defaultIndex] as Button : null)?.Focus();
    }

    /// <summary>창을 띄우고 고른 순번(닫으면 -1)을 돌려준다. 첫 버튼이 기본.</summary>
    public static int Ask(Window? owner, string message, params string[] choices) => Ask(owner, message, 0, choices);

    /// <summary>창을 띄우고 고른 순번(닫으면 -1)을 돌려준다. <paramref name="defaultIndex"/> 버튼이 기본(Enter·처음 포커스).</summary>
    public static int Ask(Window? owner, string message, int defaultIndex, IReadOnlyList<string> choices)
    {
        var dlg = new ChoiceDialog(message, choices, defaultIndex) { Owner = owner };
        dlg.ShowDialog();
        return dlg.ChosenIndex;
    }
}
