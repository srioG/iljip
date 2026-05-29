using System.Windows;

namespace Iljip.Views;

public partial class PasswordPromptDialog : Window
{
    public string? Password { get; private set; }

    public PasswordPromptDialog(string? message = null)
    {
        InitializeComponent();
        if (!string.IsNullOrEmpty(message))
            MessageText.Text = message;
        Loaded += (_, _) => PasswordBox1.Focus();
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
