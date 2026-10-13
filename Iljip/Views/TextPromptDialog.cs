using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;

namespace Iljip.Views;

/// <summary>
/// 글 한 줄을 묻는 작은 창(W2-06 — 압축 안 새 폴더 이름). 문구 + 입력 칸 + [확인][취소]. 처음 글자를 채워 두고 확장자 앞까지 골라 둔다.
/// Enter 는 [확인], Esc·닫기는 취소. XAML 없이 코드로 만든다(테마 색·단추 모양은 다른 창과 같은 자원).
/// </summary>
public sealed class TextPromptDialog : Window
{
    private readonly TextBox _box;

    public TextPromptDialog(string title, string message, string initialText)
    {
        Title = string.IsNullOrEmpty(title) ? "일집" : title;
        Width = 420;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        FontFamily = new System.Windows.Media.FontFamily("Segoe UI");
        SetResourceReference(BackgroundProperty, "WindowBgBrush");
        SetResourceReference(TextElement.ForegroundProperty, "FgBrush");

        var panel = new StackPanel { Margin = new Thickness(22) };
        panel.Children.Add(new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 10) });
        _box = new TextBox { Text = initialText ?? string.Empty, MinHeight = 28, Padding = new Thickness(4, 3, 4, 3) };
        panel.Children.Add(_box);

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 16, 0, 0) };
        var ok = new Button { Content = "확인", MinWidth = 84, MinHeight = 32, IsDefault = true, Margin = new Thickness(0, 0, 8, 0) };
        var cancel = new Button { Content = "취소", MinWidth = 84, MinHeight = 32, IsCancel = true };
        ok.SetResourceReference(StyleProperty, "DialogButton");
        cancel.SetResourceReference(StyleProperty, "DialogButton");
        ok.Click += (_, _) => DialogResult = true;
        buttons.Children.Add(ok);
        buttons.Children.Add(cancel);
        panel.Children.Add(buttons);
        Content = panel;

        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape) DialogResult = false;
        };
        Loaded += (_, _) =>
        {
            _box.Focus();
            string text = _box.Text;
            string ext = Path.GetExtension(text);
            int stem = ext.Length > 0 && ext.Length < text.Length ? text.Length - ext.Length : text.Length;
            _box.Select(0, stem);
        };
    }

    /// <summary>넣은 글(앞뒤 빈칸은 뗌).</summary>
    public string Text => _box.Text.Trim();
}
