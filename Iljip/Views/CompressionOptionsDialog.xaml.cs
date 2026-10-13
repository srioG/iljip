using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Iljip.Models;
using Iljip.Services;
using Iljip.ViewModels;
using Microsoft.Win32;

namespace Iljip.Views;

/// <summary>
/// 한 화면 압축 창(W2-07, I2-12 — 예전 저장 창 + 옵션 창): 이름·위치·형식·수준·암호·나누기·각각 따로·날짜·쓸모없는 파일 빼기·시험·원본 지우기.
/// 보이기 규칙과 값은 <see cref="CompressionDialogViewModel"/>(순수 VM — DataContext). 이 창은 묶기와 암호 칸, [압축] 때의 확인만 한다:
/// 암호는 두 번 넣어 일치를 확인하고, '암호 보이기'·8자 권장 안내(I0-3), ZIP 암호 방식 선택(I2-11), 한글 암호 호환 경고(B3) — 판정은 <see cref="PasswordPolicy"/>.
/// 7Z(W2-02): 솔리드·'파일 이름도 암호로 숨기기', W2-03: ZIP·7Z 에 '나누기'(1MB 이상). 칸 모양·옵션 만들기는 <see cref="CompressionOptionsRules"/>.
/// 같은 이름의 압축 파일이 이미 있으면 '바꿀까요?'(바꾸기 / 새 이름으로 / 취소) — 예전 저장 창의 덮어쓰기 확인과 같다.
/// 암호는 VM·설정에 두지 않는다(옵션에만 담아 넘김).
/// </summary>
public partial class CompressionOptionsDialog : Window
{
    /// <summary>옵션 칸만 보이는 창(예전 옵션 창 어댑터)의 결과. 한 화면 창이면 <see cref="Request"/> 의 Options 와 같다.</summary>
    public CompressionOptions? Result { get; private set; }

    /// <summary>[압축]으로 닫았을 때의 요청.</summary>
    public CompressionRequest? Request { get; private set; }

    private static readonly Brush WarningBrush = new SolidColorBrush(Color.FromRgb(0xD9, 0x77, 0x06));   // 주황(밝은·어두운 테마 모두 보임)

    private readonly CompressionDialogViewModel _vm;
    private bool _syncing;   // 암호 칸 ↔ 보이는 칸 맞추는 중(되먹임 방지)
    private bool _rememberedEncryptNames;   // 암호를 지워 '파일 이름 숨기기'가 꺼지기 전 값(다시 암호를 넣으면 되살림)

    /// <summary>옵션 칸만 보이는 창(예전 생성자 — <see cref="IUserInteraction.AskCompressionOptions"/> 어댑터).</summary>
    public CompressionOptionsDialog(string? targetExtension = null)
        : this(new CompressionDialogViewModel(new CompressionDialogRequest { FixedExtension = targetExtension ?? ".zip" }, SystemClock.Instance))
    {
    }

    public CompressionOptionsDialog(CompressionDialogViewModel vm)
    {
        InitializeComponent();
        _vm = vm;
        DataContext = vm;
        // 작은 화면에서도 [압축]·[취소]가 보이게 — 넘치면 가운데가 굴러간다
        MaxHeight = Math.Max(480, SystemParameters.WorkArea.Height - 24);

        LevelCombo.ItemsSource = CompressionOptionsRules.LevelLabels;
        LevelCombo.SelectedIndex = vm.LevelIndex;   // 기억한 수준(기본 보통)

        // 나누기(W2-03) — ZIP·7Z 만. 기억한 값(기본 '나누지 않음')
        SplitCombo.ItemsSource = CompressionOptionsRules.SplitLabels;
        SplitCustomBox.Text = vm.SplitCustomText;
        SplitCombo.SelectedIndex = vm.SplitIndex;
        SplitHintText.Text = CompressionOptionsRules.SplitHint;

        // 기억한 ZIP 방식·7Z 솔리드·파일 이름 숨기기(암호를 넣어야 고를 수 있음)
        ZipCryptoRadio.IsChecked = vm.UseZipCrypto;
        AesRadio.IsChecked = !vm.UseZipCrypto;
        SolidCheck.IsChecked = vm.Solid;
        _rememberedEncryptNames = vm.EncryptFileNames;

        ApplyLayout();
        vm.PropertyChanged += Vm_PropertyChanged;
        Loaded += (_, _) =>
        {
            if (vm.ShowNameAndPlace && vm.NameEditable)
            {
                NameBox.Focus();
                NameBox.SelectAll();
            }
        };
    }

    private void Vm_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(CompressionDialogViewModel.Format)) ApplyLayout();
    }

    /// <summary>형식에 맞게 칸을 보이고 숨긴다(규칙은 <see cref="CompressionOptionsRules.LayoutFor"/> — VM 의 보이기 값과 같은 규칙).</summary>
    private void ApplyLayout()
    {
        var layout = CompressionOptionsRules.LayoutFor(_vm.Extension);
        ZipEncryptionPanel.Visibility = layout.ShowZipEncryption ? Visibility.Visible : Visibility.Collapsed;
        SevenZipPanel.Visibility = layout.ShowSevenZipOptions ? Visibility.Visible : Visibility.Collapsed;
        SplitPanel.Visibility = layout.ShowSplit ? Visibility.Visible : Visibility.Collapsed;
        HintText.Text = layout.Hint;

        // 암호를 못 거는 형식(TAR·GZ·BZ2): 칸을 끄고 안내만
        PasswordBox1.IsEnabled = PasswordBox2.IsEnabled = PlainBox1.IsEnabled = PlainBox2.IsEnabled = layout.PasswordEnabled;
        ShowPasswordCheck.IsEnabled = layout.PasswordEnabled;
        LengthHint.Visibility = layout.PasswordEnabled ? Visibility.Visible : Visibility.Collapsed;
        UpdateEncryptNames();
    }

    /// <summary>'직접 입력'을 고르면 MB 칸을 보인다.</summary>
    private void SplitCombo_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (SplitCustomBox is null) return;   // InitializeComponent 도중
        bool custom = SplitCombo.SelectedIndex == CompressionOptionsRules.SplitCustomIndex;
        SplitCustomBox.Visibility = SplitUnitText.Visibility = custom ? Visibility.Visible : Visibility.Collapsed;
        if (custom && IsLoaded)
        {
            SplitCustomBox.Focus();
            SplitCustomBox.SelectAll();
        }
    }

    private void Browse_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFolderDialog { Title = "압축 파일을 만들 곳 고르기" };
        if (System.IO.Directory.Exists(_vm.OutputFolder)) dlg.InitialDirectory = _vm.OutputFolder;
        if (dlg.ShowDialog(this) == true) _vm.OutputFolder = dlg.FolderName;
    }

    private void ExcludeEdit_Click(object sender, RoutedEventArgs e) => _vm.ShowExcludeEditor = !_vm.ShowExcludeEditor;

    private void ExcludeReset_Click(object sender, RoutedEventArgs e) => _vm.ResetExcludePatterns();

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
        _vm.HasPassword = PasswordBox1.Password.Length > 0;
        UpdateLengthHint();
        UpdateEncryptNames();
    }

    /// <summary>7Z '파일 이름도 암호로 숨기기'는 암호를 넣었을 때만 고를 수 있다(암호를 지우면 체크도 푼다 — 다시 넣으면 앞의 값으로).</summary>
    private void UpdateEncryptNames()
    {
        if (EncryptNamesCheck is null) return;   // InitializeComponent 도중
        bool can = CompressionOptionsRules.CanEncryptFileNames(_vm.Extension, PasswordBox1.Password);
        if (can && !EncryptNamesCheck.IsEnabled) EncryptNamesCheck.IsChecked = _rememberedEncryptNames;
        if (!can && EncryptNamesCheck.IsEnabled) _rememberedEncryptNames = EncryptNamesCheck.IsChecked == true;
        EncryptNamesCheck.IsEnabled = can;
        if (!can) EncryptNamesCheck.IsChecked = false;
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
        // 0) 이름·위치·형식(GZ·BZ2 는 파일 하나씩) — VM 규칙
        if (_vm.Validate() is { } problem)
        {
            MessageBox.Show(this, problem, "일집", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var layout = CompressionOptionsRules.LayoutFor(_vm.Extension);
        string p1 = PasswordBox1.Password;
        string p2 = PasswordBox2.Password;

        // 1) 두 칸 일치(짧은 암호는 안내만 하고 막지 않음) — ZIP·7Z
        if (layout.PasswordEnabled && PasswordPolicy.CheckEntry(p1, p2) == PasswordEntryCheck.Mismatch)
        {
            MessageBox.Show(this, "비밀번호가 일치하지 않아요.", "일집", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        // 1-2) 나누기 크기(W2-03) — 직접 입력이 1MB 미만·숫자가 아니면 알리고 그 칸으로
        long? splitBytes = null;
        if (layout.ShowSplit && !CompressionOptionsRules.TryGetSplitBytes(SplitCombo.SelectedIndex, SplitCustomBox.Text, out splitBytes, out string? splitError))
        {
            MessageBox.Show(this, splitError, "일집", MessageBoxButton.OK, MessageBoxImage.Warning);
            SplitCustomBox.Focus();
            SplitCustomBox.SelectAll();
            return;
        }

        bool hasPassword = layout.PasswordEnabled && !string.IsNullOrEmpty(p1);
        var encryption = CompressionOptionsRules.UsesZipEncryption(_vm.Extension) && ZipCryptoRadio.IsChecked == true ? ZipEncryption.ZipCrypto : ZipEncryption.Aes256;

        // 2) 다른 프로그램과의 호환(한글 암호, ZIP 만 — 7Z 암호는 UTF-16 이라 반디집·7-Zip 과 맞음).
        //    방식을 ZipCrypto 로 바꾸면 바뀐 방식으로 한 번 더 판정한다(경고를 건너뛰지 않게).
        bool check = hasPassword && CompressionOptionsRules.ChecksPasswordCompat(_vm.Extension);
        while (check)
        {
            check = false;
            switch (PasswordPolicy.CheckCompat(p1, _vm.Extension, encryption))
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

        var options = CompressionOptionsRules.Build(LevelCombo.SelectedIndex, p1, _vm.Extension, encryption,
            solid: SolidCheck.IsChecked == true, encryptFileNames: EncryptNamesCheck.IsChecked == true, splitBytes: splitBytes);

        // 기억할 값(암호 빼고) — 7Z 칸·ZIP 방식·나누기
        _vm.LevelIndex = LevelCombo.SelectedIndex;
        _vm.UseZipCrypto = encryption == ZipEncryption.ZipCrypto;
        _vm.Solid = SolidCheck.IsChecked == true;
        _vm.EncryptFileNames = EncryptNamesCheck.IsEnabled ? EncryptNamesCheck.IsChecked == true : _rememberedEncryptNames;
        _vm.SplitIndex = SplitCombo.SelectedIndex;
        _vm.SplitCustomText = SplitCustomBox.Text;

        Result = options;
        if (!_vm.ShowNameAndPlace)
        {
            DialogResult = true;   // 옵션 칸만 보이는 창(예전 옵션 창)
            return;
        }

        // 3) 같은 이름이 있으면 묻는다(예전 저장 창의 덮어쓰기 확인과 같음) — 기본 버튼은 '새 이름으로'(원래 파일이 남게)
        //    만들 압축이 압축하려는 원본 그 자체면(‘a.zip’ 을 같은 곳에 a.zip 으로) [바꾸기]는 주지 않는다 — 원본이 빈 압축으로 덮이므로(W2-07 검증 지적)
        bool overwrite = false;
        bool split = options.SplitBytes is > 0;
        if (_vm.TargetExists(split) && _vm.TargetIsSource)
        {
            if (ChoiceDialog.Ask(this, _vm.ExistingTargetMessage, 0, new[]
                    {
                        $"새 이름으로 만들기 ({_vm.AlternativeName(split)})",
                        "취소 (이름 다시 쓰기)",
                    }) != 0)
            {
                NameBox.Focus();
                NameBox.SelectAll();
                return;
            }
        }
        else if (_vm.TargetExists(split))
        {
            switch (ChoiceDialog.Ask(this, _vm.ExistingTargetMessage, 1, new[]
                    {
                        "바꾸기 (원래 파일은 없어져요)",
                        $"새 이름으로 만들기 ({_vm.AlternativeName(split)})",
                        "취소 (이름 다시 쓰기)",
                    }))
            {
                case 0:
                    overwrite = true;
                    break;
                case 1:
                    break;
                default:
                    NameBox.Focus();
                    NameBox.SelectAll();
                    return;
            }
        }

        Request = _vm.BuildRequest(options, overwrite);
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
