using System.Windows;
using Iljip.Services;

namespace Iljip.Views;

/// <summary>'일집 정보' 창 — 버전, 압축 구성 요소(7z.dll) 상태, 공개 소프트웨어 안내와 라이선스 전문(I2-15).</summary>
public partial class AboutWindow : Window
{
    public AboutWindow()
    {
        InitializeComponent();

        var asm = System.Reflection.Assembly.GetExecutingAssembly();
        // csproj <Version>이 반영된 어셈블리 버전을 동적으로 표시 (하드코딩 방지)
        string version = asm.GetName().Version?.ToString(3) ?? "0.0.0";
        VersionText.Text = $"광고 없는 압축 프로그램 · 버전 {version}";

        string wrapper = typeof(SharpSevenZip.SharpSevenZipExtractor).Assembly.GetName().Version?.ToString(3) ?? "?";
        EngineText.Text = SevenZipLibrary.IsAvailable
            ? $"압축 구성 요소: 7-Zip {SevenZipLibrary.DllVersion} (x64\\7z.dll) · SharpSevenZip {wrapper}"
            : $"압축 구성 요소(7z.dll)를 불러오지 못했어요 — {LoadFailureReason()}. " +
              "ZIP·TAR·GZ·BZ2·EGG·ALZ·7Z·RAR 는 그대로 풀 수 있지만 ZIPX·ISO·CAB·LZH·WIM·XZ 는 열 수 없어요." +
              $" (SharpSevenZip {wrapper})";
    }

    /// <summary>7z.dll 을 못 불러온 까닭(쉬운 말). 자세한 원인은 로그에 있다(SevenZipLibrary.EnsureLoaded).</summary>
    private static string LoadFailureReason() => SevenZipLibrary.LoadError switch
    {
        System.IO.FileNotFoundException => "파일이 없어요",
        PlatformNotSupportedException => "64비트 Windows 에서만 쓸 수 있어요",
        _ => "보안 프로그램이 막았을 수 있어요",
    };

    /// <summary>라이선스 전문이 든 폴더를 탐색기로 연다(사용자가 단추를 눌렀을 때만).</summary>
    private void LicenseFolder_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe", $"\"{ThirdPartyNotices.LicensesFolder}\"")
            {
                UseShellExecute = true,
            });
        }
        catch (Exception ex)
        {
            Logger.LogError("라이선스 폴더 열기 실패", ex);
            MessageBox.Show(this, "라이선스 폴더를 열지 못했어요:\n" + ThirdPartyNotices.LicensesFolder, "일집 정보",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void License_Click(object sender, RoutedEventArgs e)
    {
        if (LicenseBox.Visibility == Visibility.Visible)
        {
            LicenseBox.Visibility = Visibility.Collapsed;
            LicenseButton.Content = "라이선스 전문 보기";
            return;
        }
        if (string.IsNullOrEmpty(LicenseBox.Text))
            LicenseBox.Text = ThirdPartyNotices.LoadAll();
        LicenseBox.Visibility = Visibility.Visible;
        LicenseButton.Content = "라이선스 접기";
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
