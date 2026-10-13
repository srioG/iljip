using System.Windows;
using Iljip.Models;
using Iljip.Services;
using Microsoft.Win32;

namespace Iljip.Views;

/// <summary>
/// 설정 창(☰ ▸ 설정 — W2-05): 같은 이름이 있을 때(물어보기·덮어쓰기·건너뛰기·이름 바꾸기), 우클릭으로 푼 뒤 폴더 열기, 다 푼 뒤 압축 파일을 휴지통으로.
/// W2-07(I3-6): 압축할 때 쓸모없는 파일 빼기와 그 목록(한 줄에 하나 — 압축 창의 '빼기 목록 고치기'와 같은 설정).
/// W2-07 검증 지적: 압축 창 '만들 곳'의 처음 값(원본이 있는 폴더·마지막으로 만든 곳·정해 둔 폴더 — I2-12), '원본을 휴지통으로'를 켠 채로 열지(I3-4 — 압축 창은 기억하지 않음).
/// 받은 설정의 사본을 고치고 [확인]이면 <see cref="Result"/> 로 돌려준다(저장은 부르는 쪽이 한 번만).
/// </summary>
public partial class SettingsDialog : Window
{
    private readonly AppSettings _settings;

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
        _settings.Compress.FolderChoice = LastFolderRadio.IsChecked == true ? CompressFolderChoice.LastFolder
            : FixedFolderRadio.IsChecked == true ? CompressFolderChoice.FixedFolder
            : CompressFolderChoice.SourceFolder;
        // 정해 둔 폴더는 다른 것을 골라도 칸에 적힌 대로 둔다(다시 고를 때 그대로 보이게)
        string fixedFolder = FixedFolderBox.Text.Trim();
        _settings.Compress.FixedFolder = fixedFolder.Length == 0 ? null : fixedFolder;
        _settings.Compress.DeleteOriginalsAfter = DeleteOriginalsCheck.IsChecked == true;
        _settings.Compress.ExcludeEnabled = ExcludeCheck.IsChecked == true;
        _settings.Compress.ExcludePatterns = PatternFileFilter.ParseLines(ExcludeBox.Text);
        Result = _settings;
        DialogResult = true;
    }
}
