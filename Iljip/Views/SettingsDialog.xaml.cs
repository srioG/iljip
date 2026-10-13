using System.Windows;
using Iljip.Services;

namespace Iljip.Views;

/// <summary>
/// 설정 창(☰ ▸ 설정 — W2-05): 같은 이름이 있을 때(물어보기·덮어쓰기·건너뛰기·이름 바꾸기), 우클릭으로 푼 뒤 폴더 열기, 다 푼 뒤 압축 파일을 휴지통으로.
/// 받은 설정의 사본을 고치고 [확인]이면 <see cref="Result"/> 로 돌려준다(저장은 부르는 쪽이 한 번만).
/// </summary>
public partial class SettingsDialog : Window
{
    private readonly AppSettings _settings;

    public SettingsDialog(AppSettings current)
    {
        InitializeComponent();
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
    }

    /// <summary>[확인]이면 고친 설정, 아니면 null.</summary>
    public AppSettings? Result { get; private set; }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        var x = _settings.Extract;
        x.OnCollision = OverwriteRadio.IsChecked == true ? CollisionPolicy.Overwrite
            : SkipRadio.IsChecked == true ? CollisionPolicy.Skip
            : RenameRadio.IsChecked == true ? CollisionPolicy.Rename
            : CollisionPolicy.Ask;
        x.OpenFolderAfter = OpenFolderCheck.IsChecked == true;
        x.DeleteArchiveAfter = DeleteArchiveCheck.IsChecked == true;
        Result = _settings;
        DialogResult = true;
    }
}
