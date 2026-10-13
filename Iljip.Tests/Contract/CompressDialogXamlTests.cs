using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using Iljip.Models;
using Iljip.Services;
using Iljip.Tests.TestSupport;
using Iljip.ViewModels;
using Iljip.Views;

namespace Iljip.Tests.Contract;

/// <summary>
/// [W2-07 검증 지적 2차] 한 화면 압축 창(<see cref="CompressionOptionsDialog"/>)·옵션 칸만 보이는 창(예전 옵션 창 어댑터)·설정 창(<see cref="SettingsDialog"/>)의
/// XAML 이 실제로 읽히는지 — 띄우지 않고 만들기만 한다(STA 스레드, <see cref="WpfAppScope"/>). XAML 오타·없는 자원(StaticResource)·틀린 묶기 경로는
/// 컴파일로 안 잡히고 앱을 켤 때(또는 그 창을 열 때) 터지므로 여기서 만든다. 묶기는 양쪽(VM → 칸, 칸 → VM)으로 확인하고 묶기 오류가 0 인지 본다.
/// </summary>
[Collection(WpfApplicationCollection.Name)]
public class CompressDialogXamlTests
{
    private static CompressionDialogRequest Request(TempDir t, out string a, out string b)
    {
        string src = t.Dir("원본");
        a = Path.Combine(src, "가.txt");
        b = Path.Combine(src, "나.txt");
        File.WriteAllText(a, "a");
        File.WriteAllText(b, "b");
        return new CompressionDialogRequest
        {
            Sources = new[] { a, b },
            AllSourcesAreFiles = true,
            SuggestedFolder = src,
            SuggestedBaseName = "묶음",
            AvailableFormats = new[] { CompressionFormat.Zip, CompressionFormat.SevenZip, CompressionFormat.Tar, CompressionFormat.GZip, CompressionFormat.BZip2 },
            Defaults = new CompressSettings(),
        };
    }

    /// <summary>묶기 추적이 켜졌는지(없는 경로를 일부러 묶어 오류가 잡히는지) — 아래 '묶기 오류 0' 이 헛되지 않게.</summary>
    private static void AssertBindingLogWorks(BindingErrorLog log)
    {
        var probe = new TextBlock { DataContext = new object() };
        probe.SetBinding(TextBlock.TextProperty, new Binding("없는속성"));
        WpfAppScope.Pump();
        Assert.NotEmpty(log.Errors);
        log.Clear();
    }

    /// <summary>
    /// 계약: 한 화면 압축 창이 만들어지고(띄우지 않음) VM 과 양쪽으로 묶인다 — 이름·형식·만들 곳·각각 따로·날짜. 형식을 바꾸면 칸이 바뀐다
    /// (7Z → 7Z 칸·나누기 보임, TAR → 암호 칸 끔·나누기 숨김). 묶기 오류 0.
    /// </summary>
    [Fact]
    public void 압축_창이_읽히고_VM과_양쪽으로_묶인다()
    {
        using var t = new TempDir("xamlcompress");
        var request = Request(t, out _, out _);
        string other = t.Dir("다른 곳");
        WpfAppScope.Run(log =>
        {
            AssertBindingLogWorks(log);
            var vm = new CompressionDialogViewModel(request, new FakeClock().SetLocal(2026, 10, 13));
            var w = new CompressionOptionsDialog(vm);
            try
            {
                WpfAppScope.Pump();
                // VM → 칸
                Assert.Equal("묶음", w.NameBox.Text);
                Assert.Equal(CompressionFormat.Zip, w.FormatCombo.SelectedValue);
                Assert.Equal(request.SuggestedFolder, w.FolderCombo.Text);
                Assert.True(w.NameBox.IsEnabled);
                Assert.True(w.EachCheck.IsEnabled);   // 원본 둘
                Assert.Equal(Visibility.Visible, w.ZipEncryptionPanel.Visibility);
                Assert.Equal(Visibility.Collapsed, w.SevenZipPanel.Visibility);

                // 칸 → VM
                w.FormatCombo.SelectedValue = CompressionFormat.SevenZip;
                Assert.Equal(CompressionFormat.SevenZip, vm.Format);
                Assert.Equal(Visibility.Visible, w.SevenZipPanel.Visibility);
                Assert.Equal(Visibility.Visible, w.SplitPanel.Visibility);
                Assert.Equal(Visibility.Collapsed, w.ZipEncryptionPanel.Visibility);

                vm.Format = CompressionFormat.Tar;
                WpfAppScope.Pump();
                Assert.Equal(CompressionFormat.Tar, w.FormatCombo.SelectedValue);
                Assert.False(w.PasswordBox1.IsEnabled);
                Assert.Equal(Visibility.Collapsed, w.SplitPanel.Visibility);

                w.NameBox.Text = "거래처 묶음";
                Assert.Equal("거래처 묶음", vm.BaseName);
                w.FolderCombo.Text = other;
                Assert.Equal(other, vm.OutputFolder);
                w.DateCheck.IsChecked = true;
                w.DateCombo.SelectedValue = DateNameFormat.YearMonthDayDashed;
                Assert.True(vm.DateEnabled);
                Assert.Equal(DateNameFormat.YearMonthDayDashed, vm.DateFormat);
                Assert.Equal("거래처 묶음_2026-10-13.tar", vm.PreviewName);

                w.EachCheck.IsChecked = true;
                Assert.True(vm.EachSeparately);
                WpfAppScope.Pump();
                Assert.False(w.NameBox.IsEnabled);   // 각각 따로면 이름은 원본마다

                w.TestCheck.IsChecked = true;
                Assert.True(vm.TestAfter);
                w.ExcludeCheck.IsChecked = false;
                Assert.False(vm.ExcludeEnabled);

                WpfAppScope.Pump();
                Assert.Empty(log.Errors);
            }
            finally
            {
                w.Close();
            }
        });
    }

    /// <summary>계약: 옵션 칸만 보이는 창(예전 옵션 창 어댑터 — 고정 확장자)도 만들어지고, 이름·만들 곳·형식·그 밖 칸은 숨는다. 묶기 오류 0.</summary>
    [Fact]
    public void 옵션_칸만_보이는_창도_읽힌다()
    {
        WpfAppScope.Run(log =>
        {
            AssertBindingLogWorks(log);
            var w = new CompressionOptionsDialog(".7z");
            try
            {
                WpfAppScope.Pump();
                var namePanel = (FrameworkElement)((FrameworkElement)w.NameBox.Parent).Parent;
                Assert.Equal(Visibility.Collapsed, namePanel.Visibility);
                var otherPanel = (FrameworkElement)w.EachCheck.Parent;
                Assert.Equal(Visibility.Collapsed, otherPanel.Visibility);
                Assert.Equal(Visibility.Visible, w.SevenZipPanel.Visibility);
                Assert.Equal(Visibility.Visible, w.SplitPanel.Visibility);
                Assert.True(w.PasswordBox1.IsEnabled);
                Assert.Empty(log.Errors);
            }
            finally
            {
                w.Close();
            }
        });
    }

    /// <summary>계약: 설정 창이 만들어지고 받은 설정이 칸에 들어간다(같은 이름·우클릭 풀기 + 압축하기 절 — 만들 곳 처음 값·원본 휴지통·빼기 목록). 묶기 오류 0.</summary>
    [Fact]
    public void 설정_창이_읽히고_설정이_칸에_들어간다()
    {
        var s = new AppSettings();
        s.Extract.OnCollision = CollisionPolicy.Skip;
        s.Extract.OpenFolderAfter = false;
        s.Extract.DeleteArchiveAfter = true;
        s.Compress.FolderChoice = CompressFolderChoice.FixedFolder;
        s.Compress.FixedFolder = @"C:\늘 보내는 곳";
        s.Compress.DeleteOriginalsAfter = true;
        s.Compress.ExcludeEnabled = false;
        s.Compress.ExcludePatterns = new List<string> { "*.bak", "임시/" };
        WpfAppScope.Run(log =>
        {
            AssertBindingLogWorks(log);
            var w = new SettingsDialog(s);
            try
            {
                WpfAppScope.Pump();
                Assert.True(w.SkipRadio.IsChecked);
                Assert.False(w.OpenFolderCheck.IsChecked);
                Assert.True(w.DeleteArchiveCheck.IsChecked);
                Assert.True(w.FixedFolderRadio.IsChecked);
                Assert.False(w.SourceFolderRadio.IsChecked);
                Assert.Equal(@"C:\늘 보내는 곳", w.FixedFolderBox.Text);
                Assert.True(w.DeleteOriginalsCheck.IsChecked);
                Assert.False(w.ExcludeCheck.IsChecked);
                Assert.Equal(new[] { "*.bak", "임시/" }, PatternFileFilter.ParseLines(w.ExcludeBox.Text));
                Assert.Null(w.Result);   // [확인] 전에는 결과 없음
                Assert.True(w.SfxMailWarningCheck.IsChecked);   // W2-10: 기본은 '묻기'
                Assert.Empty(log.Errors);
            }
            finally
            {
                w.Close();
            }
        });
    }

    /// <summary>
    /// 계약(W2-10, I3-1): 압축 창에서 '자동 풀림 EXE' 를 고르면 형식 칸 바로 아래 안내(회사 메일은 exe 를 막는 경우가 많음)가 보이고,
    /// ZIP 암호 방식 칸은 보이고 나누기·7z 칸은 숨는다. ZIP 으로 돌아가면 안내가 숨는다. 묶기 오류 0.
    /// </summary>
    [Fact]
    public void 자동풀림을_고르면_압축_창에_메일_안내가_보인다()
    {
        using var t = new TempDir("xamlsfx");
        var request = Request(t, out _, out _);
        request = new CompressionDialogRequest
        {
            Sources = request.Sources,
            AllSourcesAreFiles = request.AllSourcesAreFiles,
            SuggestedFolder = request.SuggestedFolder,
            SuggestedBaseName = request.SuggestedBaseName,
            AvailableFormats = request.AvailableFormats.Append(CompressionFormat.Sfx).ToArray(),
            Defaults = request.Defaults,
        };
        WpfAppScope.Run(log =>
        {
            AssertBindingLogWorks(log);
            var vm = new CompressionDialogViewModel(request, new FakeClock().SetLocal(2026, 10, 14));
            var w = new CompressionOptionsDialog(vm);
            try
            {
                WpfAppScope.Pump();
                Assert.Equal(Visibility.Collapsed, w.SfxNoticeBox.Visibility);

                w.FormatCombo.SelectedValue = CompressionFormat.Sfx;
                WpfAppScope.Pump();
                Assert.Equal(CompressionFormat.Sfx, vm.Format);
                Assert.Equal(Visibility.Visible, w.SfxNoticeBox.Visibility);
                Assert.Equal("회사 메일은 실행 파일(.exe)을 막는 경우가 많아요. 받는 사람이 못 받을 수 있어요. 메일로 보낼 거면 ZIP을 권해요.", w.SfxNoticeText.Text);
                Assert.Equal(Visibility.Visible, w.ZipEncryptionPanel.Visibility);
                Assert.Equal(Visibility.Collapsed, w.SplitPanel.Visibility);
                Assert.Equal(Visibility.Collapsed, w.SevenZipPanel.Visibility);
                Assert.True(w.PasswordBox1.IsEnabled);
                Assert.Equal("묶음.exe", vm.PreviewName);

                vm.Format = CompressionFormat.Zip;
                WpfAppScope.Pump();
                Assert.Equal(Visibility.Collapsed, w.SfxNoticeBox.Visibility);
                Assert.Empty(log.Errors);
            }
            finally
            {
                w.Close();
            }
        });
    }

    /// <summary>계약(W2-10): 설정 창의 '자동 풀림 EXE 를 만들기 전에 … 안내 묻기' 칸은 sfx.skipMailWarning 의 반대 — 껐으면(다시 묻지 않기) 빈 칸으로 열린다.</summary>
    [Fact]
    public void 설정_창의_메일_안내_칸은_건너뛰기의_반대()
    {
        var s = new AppSettings();
        s.Sfx.SkipMailWarning = true;
        WpfAppScope.Run(log =>
        {
            var w = new SettingsDialog(s);
            try
            {
                WpfAppScope.Pump();
                Assert.False(w.SfxMailWarningCheck.IsChecked);
                Assert.Empty(log.Errors);
            }
            finally
            {
                w.Close();
            }
        });
    }

    /// <summary>
    /// 계약(W2-12, I1-1): 설정 창의 '파일 연결' 칸 — 일집 창(VM) 없이 만들면 숨고, VM 을 받으면 보인다. 단추는 누르는 즉시 VM 을 부른다
    /// (가짜 레지스트리·가짜 셸 — 진짜 HKCU·ms-settings 는 안 부름): [연결 프로그램에 넣기] → 넣고 '지금' 글이 바뀜, [기본 앱 설정 열기] → 셸 한 번,
    /// [연결 빼기] → 뺌. 셸 확장 DLL 이 없으면 드롭 처리기 칸은 꺼져 있다. 띄우지 않고 단추 Click 만 일으킨다. 묶기 오류 0.
    /// </summary>
    [Fact]
    public void 설정_창의_파일_연결_칸()
    {
        var svc = new TestServices();
        WpfAppScope.Run(log =>
        {
            var plain = new SettingsDialog(new AppSettings());
            var vm = new MainViewModel(new FakeUserInteraction(), null, svc.Services);
            var w = new SettingsDialog(vm.Settings, vm);
            try
            {
                WpfAppScope.Pump();
                Assert.Equal(Visibility.Collapsed, plain.AssociationPanel.Visibility);
                Assert.Equal(Visibility.Visible, w.AssociationPanel.Visibility);
                Assert.False(w.DropHandlerCheck.IsEnabled);
                Assert.Equal("지금: 일집이 ‘연결 프로그램’에 들어가 있지 않아요.", w.AssociationStateText.Text);

                w.RegisterAssociationsButton.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
                Assert.Equal(FileAssociationRegistrar.RegisteredMessage, w.AssociationResultText.Text);
                Assert.Equal("지금: 일집이 ‘연결 프로그램’에 들어가 있어요.", w.AssociationStateText.Text);
                Assert.True(svc.Registry.KeyExists(@"Software\Classes\Iljip.zip"));

                w.OpenDefaultAppsButton.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
                Assert.Equal(1, svc.Shell.DefaultAppsOpened);
                Assert.StartsWith("Windows ‘기본 앱’ 설정을 열었어요.", w.AssociationResultText.Text);

                w.UnregisterAssociationsButton.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
                Assert.Equal(FileAssociationRegistrar.UnregisteredMessage, w.AssociationResultText.Text);
                Assert.False(svc.Registry.KeyExists(@"Software\Classes\Iljip.zip"));
                Assert.Null(w.Result);   // 단추는 [확인]·[취소]와 상관없음
                Assert.Empty(log.Errors);
            }
            finally
            {
                w.Close();
                plain.Close();
            }
        });
    }

    /// <summary>
    /// 계약(W2-10): 선택 창에 확인란 글자를 주면 버튼 아래에 그 확인란('다시 묻지 않기')이 보이고, 켜면 <see cref="ChoiceDialog.IsChecked"/>.
    /// 글자를 주지 않으면(예전 선택 창들) 숨는다. 띄우지 않고 만들기만 한다.
    /// </summary>
    [Fact]
    public void 선택_창의_다시_묻지_않기_확인란()
    {
        WpfAppScope.Run(log =>
        {
            var with = new ChoiceDialog("문구", new[] { "그래도 만들기", "ZIP으로 만들기", "취소" }, 0, "다시 묻지 않기");
            var without = new ChoiceDialog("문구", new[] { "예", "아니요" });
            try
            {
                WpfAppScope.Pump();
                Assert.Equal(Visibility.Visible, with.OptionCheck.Visibility);
                Assert.Equal("다시 묻지 않기", with.OptionCheck.Content);
                Assert.False(with.IsChecked);
                with.OptionCheck.IsChecked = true;
                Assert.True(with.IsChecked);
                Assert.Equal(-1, with.ChosenIndex);   // 버튼을 누르기 전
                Assert.Equal(Visibility.Collapsed, without.OptionCheck.Visibility);
                Assert.False(without.IsChecked);
                Assert.Empty(log.Errors);
            }
            finally
            {
                with.Close();
                without.Close();
            }
        });
    }
}
