using System.Text.RegularExpressions;
using Iljip.Tests.TestSupport;

namespace Iljip.Tests.Contract;

/// <summary>
/// [I0-1 전제] 첫 실행 인자 버그: App.xaml 에 StartupUri 가 있으면 WPF 가 OnStartup 이 끝난 '뒤'에 창을 만들어
/// OnStartup 안의 MainWindow 가 늘 null 이었다 → 일집이 꺼져 있을 때 우클릭 메뉴를 고르면 빈 창만 떴다(켜져 있으면 파이프로 넘겨 동작).
/// 창을 띄우지 않고 소스로 약속만 확인한다(실제 셸 실행은 MANUAL_CHECKLIST 1절 '일집을 끈 상태에서').
/// </summary>
public class AppStartupContractTests
{
    [Fact]
    public void App_xaml에_StartupUri가_없다()
    {
        string xaml = RepoFiles.Read("Iljip", "App.xaml");
        Assert.DoesNotMatch(new Regex(@"\bStartupUri\s*="), xaml);
    }

    /// <summary>OnStartup: 단일 인스턴스 확인 → 창을 직접 만들어 MainWindow 로 → 명령줄 인자를 VM 에 넘김 → Show.</summary>
    [Fact]
    public void OnStartup이_창을_직접_만들고_인자를_넘긴다()
    {
        string src = RepoFiles.Read("Iljip", "App.xaml.cs");
        int start = src.IndexOf("protected override async void OnStartup", StringComparison.Ordinal);
        int end = src.IndexOf("private static bool IsHeadlessExtractCommand", StringComparison.Ordinal);
        Assert.True(start >= 0 && end > start, "App.OnStartup 을 찾지 못함");
        string body = src[start..end];

        int headless = body.IndexOf("IsHeadlessExtractCommand(e.Args)", StringComparison.Ordinal);
        int mutex = body.IndexOf("new Mutex(", StringComparison.Ordinal);
        int create = body.IndexOf("new MainWindow()", StringComparison.Ordinal);
        int assign = body.IndexOf("MainWindow = win", StringComparison.Ordinal);
        int process = body.IndexOf("ProcessCommandLineAsync(", StringComparison.Ordinal);
        int show = body.IndexOf("win.Show()", StringComparison.Ordinal);

        Assert.True(headless >= 0, "창 없이 풀기 분기가 없어짐");
        Assert.True(mutex > headless, "단일 인스턴스 확인이 창 없이 풀기 뒤에 있어야 함");
        Assert.True(create > mutex, "MainWindow 를 단일 인스턴스 확인 뒤에 직접 만들어야 함");
        Assert.True(assign > create, "만든 창을 Application.MainWindow 로 정해야 함(두 번째 실행 처리·창 소유자)");
        Assert.True(process > create, "만든 창의 VM 에 명령줄 인자를 넘겨야 함");
        Assert.True(show > create, "창을 띄워야 함");
    }
}
