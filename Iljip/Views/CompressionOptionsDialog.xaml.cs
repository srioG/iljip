using System.Windows;
using Iljip.Models;

namespace Iljip.Views;

public partial class CompressionOptionsDialog : Window
{
    public CompressionOptions? Result { get; private set; }

    public CompressionOptionsDialog(string? targetExtension = null)
    {
        InitializeComponent();

        // 압축 가능한 포맷별 비밀번호 안내
        if (!string.Equals(targetExtension, ".zip", StringComparison.OrdinalIgnoreCase))
        {
            PasswordBox1.IsEnabled = false;
            PasswordBox2.IsEnabled = false;
            HintText.Text = $"* {targetExtension} 포맷은 비밀번호를 지원하지 않아요.\n  ZIP 포맷을 사용하면 AES-256 암호화가 가능해요.";
        }
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        string p1 = PasswordBox1.Password;
        string p2 = PasswordBox2.Password;

        if (!string.IsNullOrEmpty(p1) && p1 != p2)
        {
            MessageBox.Show("비밀번호가 일치하지 않아요.", "일집", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var level = LevelCombo.SelectedIndex switch
        {
            0 => CompressionLevelOption.Store,
            1 => CompressionLevelOption.Fastest,
            2 => CompressionLevelOption.Normal,
            3 => CompressionLevelOption.Maximum,
            _ => CompressionLevelOption.Normal
        };

        Result = new CompressionOptions
        {
            Level = level,
            Password = string.IsNullOrEmpty(p1) ? null : p1
        };

        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }
}
