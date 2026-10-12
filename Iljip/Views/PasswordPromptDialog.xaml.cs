using System.Windows;

namespace Iljip.Views;

/// <summary>풀 때 암호를 묻는 창. 이유 한 줄(<paramref name="message"/>)과 '암호 보이기'(I0-3).</summary>
public partial class PasswordPromptDialog : Window
{
    public string? Password { get; private set; }

    private bool _syncing;   // 암호 칸 ↔ 보이는 칸 맞추는 중(되먹임 방지)

    public PasswordPromptDialog(string? message = null)
    {
        InitializeComponent();
        if (!string.IsNullOrEmpty(message))
            MessageText.Text = message;
        Loaded += (_, _) => PasswordBox1.Focus();
    }

    private void Password_Changed(object sender, RoutedEventArgs e) => Sync(() => PlainBox1.Text = PasswordBox1.Password);
    private void Plain_Changed(object sender, RoutedEventArgs e) => Sync(() => PasswordBox1.Password = PlainBox1.Text);

    private void Sync(Action copy)
    {
        if (_syncing) return;
        _syncing = true;
        try { copy(); }
        finally { _syncing = false; }
    }

    private void ShowPassword_Changed(object sender, RoutedEventArgs e)
    {
        bool show = ShowPasswordCheck.IsChecked == true;
        PlainBox1.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        PasswordBox1.Visibility = show ? Visibility.Collapsed : Visibility.Visible;
        if (show) PlainBox1.Focus();
        else PasswordBox1.Focus();
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        Password = PasswordBox1.Password;
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }
}
