using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace Iljip.Views;

/// <summary>
/// 문구 하나와 버튼 여러 개(위에서 아래로, 긴 설명을 담을 수 있게 줄바꿈)를 보여 주고 고른 버튼 순번을 돌려주는 창.
/// 첫 버튼이 기본(Enter). 닫기(X)·Esc 는 -1.
/// </summary>
public partial class ChoiceDialog : Window
{
    /// <summary>고른 버튼 순번(0부터). 고르지 않고 닫으면 -1.</summary>
    public int ChosenIndex { get; private set; } = -1;

    public ChoiceDialog(string message, IReadOnlyList<string> choices)
    {
        InitializeComponent();
        MessageText.Text = message;
        for (int i = 0; i < choices.Count; i++)
        {
            int index = i;
            var button = new Button
            {
                Content = new TextBlock { Text = choices[i], TextWrapping = TextWrapping.Wrap, Margin = new Thickness(12, 6, 12, 6) },
                MinHeight = 34,
                Margin = new Thickness(0, 0, 0, 8),
                HorizontalContentAlignment = HorizontalAlignment.Stretch,
                IsDefault = i == 0,
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
        Loaded += (_, _) => (ButtonsPanel.Children.Count > 0 ? ButtonsPanel.Children[0] as Button : null)?.Focus();
    }

    /// <summary>창을 띄우고 고른 순번(닫으면 -1)을 돌려준다.</summary>
    public static int Ask(Window? owner, string message, params string[] choices)
    {
        var dlg = new ChoiceDialog(message, choices) { Owner = owner };
        dlg.ShowDialog();
        return dlg.ChosenIndex;
    }
}
