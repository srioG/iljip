using System.Windows;
using Iljip.Models;
using Iljip.Services;
using Microsoft.Win32;

namespace Iljip.Views;

/// <summary>
/// 설정 창(☰ ▸ 설정 — W2-05): 같은 이름이 있을 때(물어보기·덮어쓰기·건너뛰기·이름 바꾸기), 우클릭으로 푼 뒤 폴더 열기, 다 푼 뒤 압축 파일을 휴지통으로,
/// 인터넷에서 받은 압축이면 꺼낸 파일에도 그 표시 붙이기(IJ3-03 — extract.keepInternetMark).
/// W2-07(I3-6): 압축할 때 쓸모없는 파일 빼기와 그 목록(한 줄에 하나 — 압축 창의 '빼기 목록 고치기'와 같은 설정).
/// W2-07 검증 지적: 압축 창 '만들 곳'의 처음 값(원본이 있는 폴더·마지막으로 만든 곳·정해 둔 폴더 — I2-12), '원본을 휴지통으로'를 켠 채로 열지(I3-4 — 압축 창은 기억하지 않음).
/// 받은 설정의 사본을 고치고 [확인]이면 <see cref="Result"/> 로 돌려준다(저장은 부르는 쪽이 한 번만).
/// </summary>
public partial class SettingsDialog : Window
{
    private readonly AppSettings _settings;

    /// <summary>파일 연결 단추가 부를 일집 창(W2-12). null 이면 '파일 연결' 칸을 숨긴다.</summary>
    private readonly ViewModels.MainViewModel? _vm;

    /// <param name="vm">파일 연결(W2-12 — 넣기·빼기·기본 앱 설정 열기)을 할 일집 창. 단추는 누르는 즉시 적용한다([취소]와 상관없음).</param>
    public SettingsDialog(AppSettings current, ViewModels.MainViewModel vm)
        : this(current)
    {
        _vm = vm;
        AssociationPanel.Visibility = Visibility.Visible;
        DropHandlerCheck.IsEnabled = vm.CanRegisterDropHandler;
        ShowAssociationState();
    }

    private void ShowAssociationState() =>
        AssociationStateText.Text = _vm?.IsFileAssociationRegistered == true
            ? "지금: 일집이 ‘연결 프로그램’에 들어가 있어요."
            : "지금: 일집이 ‘연결 프로그램’에 들어가 있지 않아요.";

    private void RegisterAssociations_Click(object sender, RoutedEventArgs e)
    {
        if (_vm is null) return;
        AssociationResultText.Text = _vm.RegisterFileAssociations(DropHandlerCheck.IsChecked == true);
        ShowAssociationState();
    }

    private void UnregisterAssociations_Click(object sender, RoutedEventArgs e)
    {
        if (_vm is null) return;
        AssociationResultText.Text = _vm.UnregisterFileAssociations();
        ShowAssociationState();
    }

    private void OpenDefaultApps_Click(object sender, RoutedEventArgs e)
    {
        if (_vm is null) return;
        AssociationResultText.Text = _vm.OpenDefaultAppsSettings()
            ?? "Windows ‘기본 앱’ 설정을 열었어요. 목록에서 ‘일집’을 찾아 압축 파일 형식마다 일집을 골라 주세요.";
    }

    /// <summary>파일 연결 칸 없이(일집 창 없이 — 시험의 화면 배치 확인 등).</summary>
    public SettingsDialog(AppSettings current)
    {
        InitializeComponent();
        // 작은 화면에서도 [확인]·[취소]가 보이게 — 넘치면 가운데가 굴러간다
        MaxHeight = Math.Max(480, SystemParameters.WorkArea.Height - 24);
        _settings = current.Clone();
        var e = _settings.Extract;
        (e.OnCollision switch
        {
            CollisionPolicy.Overwrite => OverwriteRadio,
            CollisionPolicy.Skip => SkipRadio,
            CollisionPolicy.Rename => RenameRadio,
            _ => AskRadio,
        }).IsChecked = true;
        OpenFolderCheck.IsChecked = e.OpenFolderAfter;
        DeleteArchiveCheck.IsChecked = e.DeleteArchiveAfter;
        // IJ3-03(I2-15): 인터넷에서 받은 압축이면 꺼낸 파일에도 그 표시(기본 켬)
        InternetMarkCheck.IsChecked = e.KeepInternetMark;
        // W2-07 검증 지적(I2-12): 만들 곳의 처음 값
        var c = _settings.Compress;
        (c.FolderChoice switch
        {
            CompressFolderChoice.LastFolder => LastFolderRadio,
            CompressFolderChoice.FixedFolder => FixedFolderRadio,
            _ => SourceFolderRadio,
        }).IsChecked = true;
        FixedFolderBox.Text = c.FixedFolder ?? string.Empty;
        // W2-07 검증 지적(I3-4): 원본을 휴지통으로 — 압축 창의 처음 값(압축 창은 기억하지 않음)
        DeleteOriginalsCheck.IsChecked = c.DeleteOriginalsAfter;
        // W2-07(I3-6): 쓸모없는 파일 빼기
        ExcludeCheck.IsChecked = c.ExcludeEnabled;
        ExcludeBox.Text = PatternFileFilter.ToLines(c.ExcludePatterns);
        // W2-10(I3-1): 자동 풀림 exe 를 만들기 전 메일 안내를 묻는가(설정 값은 '건너뛰기'라 거꾸로)
        SfxMailWarningCheck.IsChecked = !_settings.Sfx.SkipMailWarning;
        // W2-12(I1-1): 파일 연결 칸은 일집 창(VM)을 받은 생성자에서만 보인다
        AssociationPanel.Visibility = Visibility.Collapsed;
    }

    private void ExcludeReset_Click(object sender, RoutedEventArgs e) => ExcludeBox.Text = PatternFileFilter.ToLines(PatternFileFilter.DefaultPatterns);

    private void FixedFolderBrowse_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFolderDialog { Title = "압축 파일을 만들 곳(정해 둔 폴더) 고르기" };
        if (System.IO.Directory.Exists(FixedFolderBox.Text.Trim())) dlg.InitialDirectory = FixedFolderBox.Text.Trim();
        if (dlg.ShowDialog(this) == true)
        {
            FixedFolderBox.Text = dlg.FolderName;
            FixedFolderRadio.IsChecked = true;
        }
    }

    /// <summary>[확인]이면 고친 설정, 아니면 null.</summary>
    public AppSettings? Result { get; private set; }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        // '정해 둔 폴더'를 골랐으면 그 폴더가 있어야 한다(없으면 알리고 그 칸으로)
        if (FixedFolderRadio.IsChecked == true && CompressSettings.CheckFixedFolder(FixedFolderBox.Text) is { } problem)
        {
            MessageBox.Show(this, problem, "일집", MessageBoxButton.OK, MessageBoxImage.Warning);
            FixedFolderBox.Focus();
            FixedFolderBox.SelectAll();
            return;
        }

        var x = _settings.Extract;
        x.OnCollision = OverwriteRadio.IsChecked == true ? CollisionPolicy.Overwrite
            : SkipRadio.IsChecked == true ? CollisionPolicy.Skip
            : RenameRadio.IsChecked == true ? CollisionPolicy.Rename
            : CollisionPolicy.Ask;
        x.OpenFolderAfter = OpenFolderCheck.IsChecked == true;
        x.DeleteArchiveAfter = DeleteArchiveCheck.IsChecked == true;
        x.KeepInternetMark = InternetMarkCheck.IsChecked == true;
        _settings.Compress.FolderChoice = LastFolderRadio.IsChecked == true ? CompressFolderChoice.LastFolder
            : FixedFolderRadio.IsChecked == true ? CompressFolderChoice.FixedFolder
            : CompressFolderChoice.SourceFolder;
        // 정해 둔 폴더는 다른 것을 골라도 칸에 적힌 대로 둔다(다시 고를 때 그대로 보이게)
        string fixedFolder = FixedFolderBox.Text.Trim();
        _settings.Compress.FixedFolder = fixedFolder.Length == 0 ? null : fixedFolder;
        _settings.Compress.DeleteOriginalsAfter = DeleteOriginalsCheck.IsChecked == true;
        _settings.Compress.ExcludeEnabled = ExcludeCheck.IsChecked == true;
        _settings.Compress.ExcludePatterns = PatternFileFilter.ParseLines(ExcludeBox.Text);
        _settings.Sfx.SkipMailWarning = SfxMailWarningCheck.IsChecked != true;
        Result = _settings;
        DialogResult = true;
    }
}
