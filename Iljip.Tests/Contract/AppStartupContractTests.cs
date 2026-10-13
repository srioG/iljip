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

    private static string Between(string src, string from, string to)
    {
        int a = src.IndexOf(from, StringComparison.Ordinal);
        int b = a < 0 ? -1 : src.IndexOf(to, a + from.Length, StringComparison.Ordinal);
        Assert.True(a >= 0 && b > a, $"'{from}' ~ '{to}' 를 찾지 못함");
        return src[a..b];
    }

    /// <summary>
    /// W2-05(I1-6): 탐색기 우클릭 풀기(ShowMainWindow == false)는 메인 창을 띄우지 않는다 — OnStartup 에서 창(VM)을 만든 뒤
    /// win.Show() 보다 먼저 RunWindowlessAsync 로 넘기고 돌아간다. 창이 닫힐 때 앱이 저절로 끝나지 않게 OnExplicitShutdown.
    /// (실제 화면 — 진행 창만 뜨고 메인 창이 안 뜨는지 — 은 MANUAL_CHECKLIST 12절)
    /// </summary>
    [Fact]
    public void 우클릭_풀기는_메인_창을_띄우기_전에_따로_돈다()
    {
        string src = RepoFiles.Read("Iljip", "App.xaml.cs");
        string body = Between(src, "protected override async void OnStartup", "private static bool IsHeadlessExtractCommand");
        int assign = body.IndexOf("MainWindow = win", StringComparison.Ordinal);
        int check = body.IndexOf(".ShowMainWindow", StringComparison.Ordinal);
        int windowless = body.IndexOf("RunWindowlessAsync(", StringComparison.Ordinal);
        int ret = body.IndexOf("return;", windowless, StringComparison.Ordinal);
        int show = body.IndexOf("win.Show()", StringComparison.Ordinal);
        Assert.True(check > assign, "우클릭 풀기 판단이 창을 만든 뒤에 있어야 함(진행 창 소유·나중에 들어오는 열기 명령)");
        Assert.True(windowless > check && ret > windowless && ret < show, "우클릭 풀기는 win.Show() 전에 RunWindowlessAsync 로 넘기고 돌아가야 함");
        Assert.Contains("ShutdownMode = ShutdownMode.OnExplicitShutdown", body);

        string windowlessBody = Between(src, "private async Task RunWindowlessAsync", "private async void MainWindow_Closed");
        Assert.Contains("ProcessShellCommandAsync(", windowlessBody);
        Assert.Contains("ShutdownIfIdleAsync()", windowlessBody);
        Assert.DoesNotContain(".Show()", windowlessBody);
    }

    /// <summary>W2-05: 이미 떠 있는 일집이 파이프로 우클릭 풀기를 받으면 메인 창을 앞으로 가져오지 않는다(진행 창만). 그 밖은 메인 창을 띄워 처리.</summary>
    [Fact]
    public void 두번째_실행의_우클릭_풀기는_메인_창을_앞으로_가져오지_않는다()
    {
        string src = RepoFiles.Read("Iljip", "App.xaml.cs");
        string body = Between(src, "private async Task OnSecondInstanceLaunched", "// ===== 테마 =====");
        int check = body.IndexOf(".ShowMainWindow", StringComparison.Ordinal);
        int windowless = body.IndexOf("RunWindowlessAsync(", StringComparison.Ordinal);
        int ret = body.IndexOf("return;", windowless, StringComparison.Ordinal);
        int activate = body.IndexOf("ShowMainWindowAndActivate()", StringComparison.Ordinal);
        Assert.True(check >= 0 && windowless > check && ret > windowless && activate > ret, "우클릭 풀기 분기가 메인 창 띄우기보다 먼저여야 함");
        Assert.True(body.IndexOf("ProcessCommandLineAsync(", StringComparison.Ordinal) > activate);
    }

    /// <summary>
    /// W2-05: 두 번째 실행은 넘긴 명령을 받는 쪽이 '받았음'(1바이트)을 돌려줘야 끝난다 — 앞 일집이 우클릭 풀기를 마치고 끝나는 중이면
    /// 명령을 잃지 않고 직접 처리한다. 받는 쪽은 넘긴 쪽이 답을 읽어 간 뒤에만 실행(둘이 같이 하지 않게). 예전 판(받기만 하는 파이프, 줄 끝 \r\n)과도 맞물린다.
    /// </summary>
    [Fact]
    public void 파이프는_받았음_답을_주고받는다()
    {
        string src = RepoFiles.Read("Iljip", "App.xaml.cs");
        string fwd = Between(src, "private static bool ForwardArgsToRunningInstance", "private bool TryTakeOverSingleInstance");
        Assert.Contains("PipeDirection.InOut", fwd);
        Assert.Contains("ack[0] == 1", fwd);
        string server = Between(src, "private void PipeServerLoop", "private static (string? Line, bool Legacy) ReadLine");
        Assert.Contains("PipeDirection.InOut", server);
        Assert.Contains("WaitForPipeDrain()", server);
        Assert.Contains("ReserveForwarded", server);
        string start = Between(src, "protected override async void OnStartup", "private static bool IsHeadlessExtractCommand");
        int forward = start.IndexOf("ForwardArgsToRunningInstance(e.Args)", StringComparison.Ordinal);
        int takeOver = start.IndexOf("TryTakeOverSingleInstance()", StringComparison.Ordinal);
        Assert.True(forward >= 0 && takeOver > forward, "넘기지 못하면 직접 처리(첫 일집 되기)로 이어져야 함");
    }

    /// <summary>
    /// W2-05: 같은 이름 창은 해제 작업 스레드에서 불린다 — WPF 창은 UI 스레드(Dispatcher.Invoke)에서 띄워야 한다(안 그러면 InvalidOperationException).
    /// 앱이 없으면(시험·창 없이 풀기) 묻지 않고 [취소].
    /// </summary>
    [Fact]
    public void 같은_이름_창은_UI_스레드에서_띄운다()
    {
        string src = RepoFiles.Read("Iljip", "Views", "WpfUserInteraction.cs");
        string body = Between(src, "public CollisionAnswer AskCollision", "public void ShowSummary");
        Assert.Contains("Dispatcher", body);
        Assert.Contains("Invoke(", body);
        Assert.Contains("CollisionChoice.Cancel", body);
    }
}
