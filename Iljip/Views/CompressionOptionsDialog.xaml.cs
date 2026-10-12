using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Iljip.Models;
using Iljip.Services;

namespace Iljip.Views;

/// <summary>
/// 압축 옵션 창(수준·암호). 암호는 두 번 넣어 일치를 확인하고, '암호 보이기'·8자 권장 안내(I0-3), ZIP 암호 방식 선택(I2-11),
/// 한글 암호 호환 경고(B3)를 둔다. 판정은 <see cref="PasswordPolicy"/>(순수 함수) — 이 창은 그 결과대로 묻고 고칠 뿐이다.
/// </summary>
public partial class CompressionOptionsDialog : Window
{
    public CompressionOptions? Result { get; private set; }

    private static readonly Brush WarningBrush = new SolidColorBrush(Color.FromRgb(0xD9, 0x77, 0x06));   // 주황(밝은·어두운 테마 모두 보임)

    private readonly string? _targetExtension;
    private readonly bool _isZip;
    private bool _syncing;   // 암호 칸 ↔ 보이는 칸 맞추는 중(되먹임 방지)

    public CompressionOptionsDialog(string? targetExtension = null)
    {
        InitializeComponent();
        _targetExtension = targetExtension;
        _isZip = string.Equals(targetExtension, ".zip", StringComparison.OrdinalIgnoreCase);

        if (!_isZip)
        {
            // 암호를 못 거는 형식: 칸을 끄고 안내만
            PasswordBox1.IsEnabled = false;
            PasswordBox2.IsEnabled = false;
            ShowPasswordCheck.IsEnabled = false;
            ZipEncryptionPanel.Visibility = Visibility.Collapsed;
            LengthHint.Visibility = Visibility.Collapsed;
            HintText.Text = $"* {targetExtension} 포맷은 비밀번호를 지원하지 않아요.\n  ZIP을 쓰면 암호를 걸 수 있어요.";
        }
    }

    // ───── 암호 보이기: 두 칸을 늘 같은 값으로 ─────

    private void Password1_Changed(object sender, RoutedEventArgs e) => Sync(() => PlainBox1.Text = PasswordBox1.Password);
    private void Password2_Changed(object sender, RoutedEventArgs e) => Sync(() => PlainBox2.Text = PasswordBox2.Password);
    private void Plain1_Changed(object sender, RoutedEventArgs e) => Sync(() => PasswordBox1.Password = PlainBox1.Text);
    private void Plain2_Changed(object sender, RoutedEventArgs e) => Sync(() => PasswordBox2.Password = PlainBox2.Text);

    private void Sync(Action copy)
    {
        if (_syncing) return;
        _syncing = true;
        try { copy(); }
        finally { _syncing = false; }
        UpdateLengthHint();
    }

    private void ShowPassword_Changed(object sender, RoutedEventArgs e)
    {
        bool show = ShowPasswordCheck.IsChecked == true;
        PlainBox1.Visibility = PlainBox2.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        PasswordBox1.Visibility = PasswordBox2.Visibility = show ? Visibility.Collapsed : Visibility.Visible;
    }

    private void UpdateLengthHint()
    {
        if (LengthHint is null) return;   // InitializeComponent 도중
        string p = PasswordBox1.Password;
        bool tooShort = p.Length > 0 && PasswordPolicy.LengthInCharacters(p) < PasswordPolicy.RecommendedLength;
        LengthHint.Text = tooShort ? "암호가 짧아요 (8자 이상 권장)" : "8자 이상을 권장해요";
        if (tooShort) LengthHint.Foreground = WarningBrush;
        else LengthHint.SetResourceReference(TextBlock.ForegroundProperty, "FgMutedBrush");
    }

    // ───── 확인 ─────

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        string p1 = PasswordBox1.Password;
        string p2 = PasswordBox2.Password;

        // 1) 두 칸 일치(짧은 암호는 안내만 하고 막지 않음)
        if (_isZip && PasswordPolicy.CheckEntry(p1, p2) == PasswordEntryCheck.Mismatch)
        {
            MessageBox.Show("비밀번호가 일치하지 않아요.", "일집", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        bool hasPassword = _isZip && !string.IsNullOrEmpty(p1);
        var encryption = _isZip && ZipCryptoRadio.IsChecked == true ? ZipEncryption.ZipCrypto : ZipEncryption.Aes256;

        // 2) 다른 프로그램과의 호환(한글 암호). 방식을 ZipCrypto 로 바꾸면 바뀐 방식으로 한 번 더 판정한다(경고를 건너뛰지 않게).
        bool check = hasPassword;
        while (check)
        {
            check = false;
            switch (PasswordPolicy.CheckCompat(p1, _targetExtension, encryption))
            {
                case PasswordCompat.NonAsciiAes:
                    switch (ChoiceDialog.Ask(this,
                        "한글 암호는 다른 프로그램에서 안 풀릴 수 있어요 — 영문·숫자를 권장해요.\n\n" +
                        "AES-256 방식은 한글 암호를 반디집·알집과 다르게 저장해서, 이 파일은 일집에서만 풀릴 수 있어요.",
                        "영문·숫자로 다시 쓰기",
                        "ZipCrypto 방식으로 만들기 (반디집·알집과 호환, 보안은 약해요)",
                        "그대로 만들기 (일집에서는 풀려요)"))
                    {
                        case 1:
                            encryption = ZipEncryption.ZipCrypto;
                            ZipCryptoRadio.IsChecked = true;
                            check = true;   // ZipCrypto 로 다시 판정
                            break;
                        case 2:
                            break;
                        default:   // 다시 쓰기(기본)·창 닫기
                            FocusPassword();
                            return;
                    }
                    break;

                case PasswordCompat.NotCp949Encodable:
                    // 이모지 등은 어느 방식으로도 반디집·알집과 맞출 수 없어(ZipCrypto 도 UTF-8 로 씀) 방식 바꾸기는 권하지 않는다
                    if (ChoiceDialog.Ask(this,
                        "암호에 다른 프로그램이 못 읽는 글자(이모지 등)가 있어요. 다른 프로그램에서 안 풀릴 수 있어요 — 영문·숫자를 권장해요.",
                        "암호 다시 쓰기",
                        "그대로 만들기 (일집에서는 풀려요)") != 1)
                    {
                        FocusPassword();
                        return;
                    }
                    break;
            }
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
            Password = hasPassword ? p1 : null,
            Encryption = encryption,
        };

        DialogResult = true;
    }

    private void FocusPassword()
    {
        if (ShowPasswordCheck.IsChecked == true) PlainBox1.Focus();
        else PasswordBox1.Focus();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }
}
