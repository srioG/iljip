using System;
using System.IO;
using System.IO.Pipes;
using System.Threading;
using System.Windows;
using Iljip.Services;
using Iljip.ViewModels;
using Microsoft.Win32;

namespace Iljip;

public enum AppTheme { Light, Dark }

public partial class App : Application
{
    // 단일 인스턴스 식별자 (사용자 세션 단위)
    private const string MutexName = @"Local\Iljip_SingleInstance_2A4F7C91";
    private const string PipeName = "Iljip_SingleInstance_Pipe_2A4F7C91";
    // 명령줄 인자 IPC 구분자. 0x1F(Unit Separator)는 Windows 파일 경로에 들어갈 수 없어 충돌이 없다.
    private const char ArgSeparator = '\u001F';
    private Mutex? _mutex;

    /// <summary>현재 적용된 테마.</summary>
    public static AppTheme CurrentTheme { get; private set; } = AppTheme.Light;

    /// <summary>테마가 바뀌면 발생 (UI가 아이콘 등 갱신용으로 구독).</summary>
    public static event Action<AppTheme>? ThemeChanged;

    protected override async void OnStartup(StartupEventArgs e)
    {
        // CP949 사용을 위한 코드 페이지 프로바이더 등록 — MainWindow 생성 전에 처리
        System.Text.Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);

        // 시스템 테마를 첫 렌더 전에 반영 (Windows 설정의 앱 모드 따라가기)
        ApplyTheme(IsSystemDark() ? AppTheme.Dark : AppTheme.Light);

        // 자동화/배치에서 지정 폴더 해제만 필요한 경우에는 WPF 창을 띄우지 않고 끝낸다.
        // 재사용장치 같은 외부 자동화가 프로세스 종료를 안정적으로 기다릴 수 있게 하기 위함.
        if (IsHeadlessExtractCommand(e.Args))
        {
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            int exitCode = await RunHeadlessExtractAsync(e.Args);
            Shutdown(exitCode);
            return;
        }

        // W2-12(I1-1): 파일 연결 넣기·빼기('--register-associations [--drop-handler]'·'--unregister-associations') — 자체 프로세스로 HKCU 에만 쓰고 끝난다
        // (단일 인스턴스 파이프로 넘기지 않음). 사용자가 직접 부를 때만 쓰이므로 결과를 짧은 창 하나로 알린다. 종료 코드 0 됨·1 못 함.
        if (StartupDecision.IsAssociationCommand(e.Args))
        {
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            var run = FileAssociationCommand.Run(CommandLineParser.Parse(e.Args), AppServices.CreateDefault(), Environment.ProcessPath);
            if (run is not null)
                MessageBox.Show(run.Message, "일집 — 파일 연결", MessageBoxButton.OK, run.Ok ? MessageBoxImage.Information : MessageBoxImage.Warning);
            Shutdown(run?.ExitCode ?? 1);
            return;
        }

        // ===== 단일 인스턴스 =====
        // 이미 일집이 떠 있으면, 새로 들어온 명령줄 인자를 기존 창에 넘기고 이 프로세스는 종료한다.
        // W2-05: 넘긴 쪽이 '받았음'(1바이트)을 확인한다 — 앞 일집이 우클릭 풀기를 마치고 끝나는 중이라 받지 않으면(또는 못 넘기면)
        // 그 일집이 끝나기를 잠깐 기다려 이 프로세스가 첫 일집이 되고, 그래도 안 되면 혼자 처리한다(명령을 잃지 않게).
        // W2-11: 새 창('--new-window')·관리자 권한으로 다시 시도('--elevated')는 뮤텍스를 잡지 않고 파이프로 넘기지도 열지도 않는다
        // (StartupDecision — 뒤에 켜는 일집이 이 창이 아니라 처음 일집에 붙게. 관리자 일집의 파이프는 권한이 달라 막히기도 함).
        _elevatedInstance = CommandLineParser.Parse(e.Args).Elevated;
        if (!StartupDecision.SkipsSingleInstance(e.Args))
        {
            _mutex = new Mutex(initiallyOwned: true, MutexName, out bool isFirstInstance);
            if (StartupDecision.Decide(e.Args, isFirstInstance) == StartupKind.Forward)
            {
                if (ForwardArgsToRunningInstance(e.Args))
                {
                    Shutdown();
                    return;
                }
                isFirstInstance = TryTakeOverSingleInstance();
            }
            _ownsMutex = isFirstInstance;
            if (isFirstInstance) StartPipeServer();
        }

        // 창을 닫아도 저절로 끝나지 않게 한다 — 끝내는 때는 '메인 창을 닫았고 우클릭 풀기가 없음' 또는 '메인 창 없이 우클릭 풀기만 하고 다 끝남'
        // (W2-05 — 우클릭 풀기는 메인 창 없이 진행·암호·결과 창만 띄워, 그 창들이 닫힐 때마다 앱이 끝나면 안 된다. ShutdownIfIdleAsync)
        ShutdownMode = ShutdownMode.OnExplicitShutdown;

        // IJ3-01(W2-04 보관본에서): 지난번에 남은 임시 사본(꺼진 일집의 안의 파일 열기·끌어내기, 오래된 미리 보기·목록 캐시·다른 이름으로 저장·긴 명령줄 목록,
        // 24시간 지난 Ctrl+C 사본) 청소 — 뒤에서(시작을 늦추지 않게, TempJanitor). 창 없이 풀기(위 분기)에서는 하지 않는다.
        // 청소를 시작한 뒤 생긴 폴더·다른 일집이 잡은 폴더는 건드리지 않는다.
        // I5-A: 꺼진 일집이 남긴 '압축에 아직 넣지 않은 고친 파일'(일집을 닫은 뒤 다른 프로그램에서 저장한 것)은 지우지 않고 모아 — 메인 창을 띄운 뒤 묻는다(OfferLeftoversAsync)
        var leftovers = new List<LeftoverFile>();
        var janitor = Task.Run(() => TempJanitor.CleanupStale(SystemClock.Instance, leftovers));

        // 전역 예외 처리: 예외를 파일 로그에 남긴 뒤 사용자에게 안내하고 앱을 계속 실행한다.
        // (로깅은 Logger.LogError가 담당. 정식 배포 시 구조적 로깅으로 교체 가능.)
        DispatcherUnhandledException += (s, args) =>
        {
            Logger.LogError("DispatcherUnhandledException", args.Exception);

            // W2-08: 영어 예외 문구를 그대로 보이지 않는다 — 쉬운 한국어 문제 창(원문은 '자세히'·로그)
            args.Handled = true;
            try
            {
                new Iljip.Views.WpfUserInteraction().ShowProblem(
                    ErrorCatalog.Describe(args.Exception) with { Heading = "예기치 못한 문제가 생겼어요." });
            }
            catch (Exception shown)
            {
                Logger.LogError("문제 창을 띄우지 못함", shown);
                MessageBox.Show(
                    $"예기치 못한 문제가 생겼어요.\n\n{ErrorCatalog.Describe(args.Exception).Message}",
                    "일집",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        };

        base.OnStartup(e);

        // 창은 여기서 직접 만든다(App.xaml 에 StartupUri 없음). 예전엔 StartupUri 가 OnStartup 이 끝난 '뒤'에 창을 만들어
        // 아래 MainWindow 가 늘 null 이었다 — 일집이 꺼져 있을 때 우클릭 메뉴(--open·--extract-here …)를 고르면 빈 창만 떴다
        // (이미 켜져 있으면 파이프로 넘겨 받아 동작했다).
        var win = new MainWindow();
        MainWindow = win;
        win.Closed += MainWindow_Closed;
        MarkElevated(win);

        // W2-12: 셸 확장이 아주 많이 골라 '--list-file 목록파일'로 넘겼으면 이 일집이 처리하므로 여기서 그 파일의 경로들로 바꾼다(읽고 지움).
        // 떠 있는 일집에 넘길 때는 짧은 원래 인자를 넘기고 받는 쪽이 바꾼다(OnSecondInstanceLaunched).
        string[] startArgs = CommandLineParser.ExpandListFile(e.Args);

        // W2-05(I1-6): 탐색기 우클릭 풀기('여기에 풀기'·'"이름"\ 에 풀기')는 메인 창을 띄우지 않고 진행 창만 — 끝나면(결과 폴더를 연 뒤) 앱을 끝낸다.
        // 창(VM)은 만들어 두기만 한다: 그사이 다른 명령(열기 등)이 파이프로 들어오면 그때 띄운다.
        if (!CommandLineParser.Parse(startArgs).ShowMainWindow && win.DataContext is MainViewModel shellVm)
        {
            await RunWindowlessAsync(shellVm, startArgs);
            return;
        }

        // 명령줄 인자가 있으면 창이 다 뜬 뒤 MainWindow의 ViewModel에 위임
        if (startArgs.Length > 0 && win.DataContext is MainViewModel vm)
        {
            string[] args = startArgs;
            win.Loaded += async (_, _) =>
            {
                await vm.ProcessCommandLineAsync(args);
            };
        }

        _mainWindowShown = true;
        win.Show();
        if (win.DataContext is MainViewModel leftoverVm) _ = OfferLeftoversAsync(leftoverVm, janitor, leftovers);
    }

    /// <summary>
    /// I5-A: 시작 청소가 끝나면(뒤에서) 남긴 '압축에 아직 넣지 않은 고친 파일'을 묻는다 — [압축에 넣기][폴더 열기][버리기][나중에](<see cref="MainViewModel.OfferLeftoverChangesAsync"/>).
    /// 메인 창 없이 우클릭 풀기로 시작했으면 묻지 않는다(다음 일반 시작 때 다시 찾음).
    /// </summary>
    private static async Task OfferLeftoversAsync(MainViewModel vm, Task janitor, List<LeftoverFile> leftovers)
    {
        try
        {
            await janitor;
            if (leftovers.Count > 0) await vm.OfferLeftoverChangesAsync(leftovers.ToList());
        }
        catch (Exception ex)
        {
            Logger.LogError("넣지 않은 고친 파일 알림 실패", ex);
        }
    }

    // ===== 메인 창 없이 우클릭 풀기(W2-05, I1-6) — 언제 끝낼지 =====

    /// <summary>단일 인스턴스 표시(뮤텍스)를 이 프로세스가 갖고 있는가.</summary>
    private bool _ownsMutex;

    /// <summary>메인 창을 띄운 적이 있는가 / 그 창을 닫았는가.</summary>
    private bool _mainWindowShown;
    private bool _mainWindowClosed;

    /// <summary>돌고 있는(또는 받아 두고 아직 시작 안 한) 명령 수 — 0 이 되어야 끝낼 수 있다(UI 스레드에서만 바꿈).</summary>
    private int _busy;

    /// <summary>끝내기로 했다 — 이후 파이프로 오는 명령은 받지 않는다(넘긴 쪽이 직접 처리).</summary>
    private bool _shuttingDown;

    /// <summary>'관리자 권한으로 다시 시도'로 뜬 일집인가(W2-11 — '--elevated'). 다시 권한 상승을 권하지 않는다.</summary>
    private bool _elevatedInstance;

    /// <summary>창의 VM 에 '관리자 권한으로 다시 시도로 뜬 일집' 표시를 넘긴다(다시 권하지 않음 — 제목의 '(관리자)'는 VM 이 실제 권한으로 정함).</summary>
    private void MarkElevated(Window win)
    {
        if (_elevatedInstance && win.DataContext is MainViewModel vm) vm.IsElevatedInstance = true;
    }

    /// <summary>우클릭 풀기 하나를 메인 창 없이 한다. 끝나면 할 일이 없을 때 앱을 끝낸다.</summary>
    private async Task RunWindowlessAsync(MainViewModel vm, string[] args)
    {
        _busy++;
        try
        {
            await vm.ProcessShellCommandAsync(args);
        }
        catch (Exception ex)
        {
            Logger.LogError("우클릭 풀기 실패", ex);
        }
        finally
        {
            _busy--;
        }
        await ShutdownIfIdleAsync();
    }

    private async void MainWindow_Closed(object? sender, EventArgs e)
    {
        _mainWindowClosed = true;
        await ShutdownIfIdleAsync();
    }

    /// <summary>
    /// 할 일이 없으면 끝낸다: 메인 창이 떠 있으면 그 창을 닫을 때까지 기다리고, 메인 창 없이 우클릭 풀기만 했으면 잠깐 기다렸다가
    /// (여러 개를 골라 우클릭하면 파일마다 일집이 떠 파이프로 거의 동시에 들어온다 — 늦게 온 것도 같은 묶음에 붙게) 그래도 없으면 끝낸다.
    /// </summary>
    private async Task ShutdownIfIdleAsync()
    {
        if (_shuttingDown || _busy > 0) return;
        if (_mainWindowShown && !_mainWindowClosed) return;
        if (!_mainWindowShown)
        {
            await Task.Delay(700);
            if (_shuttingDown || _busy > 0 || (_mainWindowShown && !_mainWindowClosed)) return;
        }
        _shuttingDown = true;
        Shutdown();
    }

    /// <summary>메인 창을 띄우고 앞으로(처음이면 띄우고, 닫았으면 새로 만든다).</summary>
    private MainViewModel? ShowMainWindowAndActivate()
    {
        if (_mainWindowClosed || MainWindow is null)
        {
            var fresh = new MainWindow();
            fresh.Closed += MainWindow_Closed;
            MarkElevated(fresh);
            MainWindow = fresh;
            _mainWindowClosed = false;
            _mainWindowShown = false;
        }
        var win = MainWindow;
        if (!_mainWindowShown)
        {
            _mainWindowShown = true;
            win.Show();
            // 우클릭 풀기로 시작했다가(단일 인스턴스 표시를 넘겨받지 못했으면) 지금부터 이 창이 첫 일집이 되게
            if (!_ownsMutex) _ownsMutex = TryAcquireMutex(0);
        }
        if (win.WindowState == WindowState.Minimized)
            win.WindowState = WindowState.Normal;
        win.Activate();
        // 잠깐 Topmost로 올렸다 내려 확실히 앞으로
        win.Topmost = true;
        win.Topmost = false;
        win.Focus();
        return win.DataContext as MainViewModel;
    }

    /// <summary>
    /// 창 없이 풀기인가(재사용장치 계약 — 판정은 <see cref="StartupDecision.IsHeadlessExtract"/>, W2-11 에서 떼어 냄). I5-B: 첫 인자가 '--extract-to' 면 늘 —
    /// 없는 압축 파일·폴더·빈 문자열도 창 없이 한 줄·종료 코드 1(예전엔 메인 창과 문제 창이 떠 끝나지 않았다).
    /// </summary>
    private static bool IsHeadlessExtractCommand(string[] args) => StartupDecision.IsHeadlessExtract(args);

    /// <summary>창 없이 풀기 실패의 로그 머리(뒤에 압축 경로) — MANUAL_CHECKLIST 8절의 '지켜보기' 명령이 이 글로 찾는다.</summary>
    internal const string HeadlessFailureLogHead = "Headless extract failed: ";

    private static async Task<int> RunHeadlessExtractAsync(string[] args)
    {
        // I5-B(최종 감사 IX-F5): 첫 인자가 '--extract-to' 면 인자가 모자라도(빈 문자열 포함) 여기로 온다 — 창을 띄우지 않고 한 줄·종료 코드 1
        string archivePath = args.Length > 1 ? args[1] : string.Empty;
        string destFolder = args.Length > 2 ? args[2] : string.Empty;

        // ★ 재사용장치(_extract_with_iljip)가 종료 코드와 stderr 문구로 결과를 가른다(ReuseDeviceContractTests):
        //   0 성공 / 2 지원하지 않는 형식 — stderr 는 정확히 영어 'Unsupported archive type: <확장자>' / 1 그 밖의 실패 — 쉬운 한국어 한 줄
        //   (손상·암호·조각·7z.dll 문구에는 '압축 파일이 아닌'·'not archive' 같은 '압축 아님' 표지를 넣지 않는다). 설정은 읽지 않는다.
        //   IJ3-03: 인터넷에서 받은 표시(Zone.Identifier)도 풀린 파일에 붙이지 않는다(재사용장치의 엑셀 자동 처리를 막지 않게 — 서비스만 부르고 화면의 표식 붙이기를 타지 않음).
        //   I5-B: 압축 파일이 없음·폴더를 줌·인자가 빔 → 1(한 줄). tar 링크를 내용 없이 만들었으면 → 1(한 줄 — 내용이 빠진 파일을 '성공'으로 넘기지 않게,
        //   재사용장치는 반디집으로 다시 풂. 풀린 파일은 지우지 않음 — 재사용장치는 실패면 임시 폴더를 버림).
        if (StartupDecision.HeadlessArgumentProblem(args) is { } argumentProblem)
        {
            Logger.LogNote(HeadlessFailureLogHead + archivePath + Environment.NewLine + argumentProblem);
            Console.Error.WriteLine(argumentProblem);
            return 1;
        }
        try
        {
            var locator = new ArchiveServiceLocator();
            // W2-01: 확장자로 먼저, 모르는 확장자면 내용(서명)으로 — 이름이 바뀐 압축도 푼다.
            // 단 속이 ZIP 인 문서(.xlsx·.docx·.hwpx 등)는 압축이 아니므로 늘 미지원(종료 코드 2).
            var service = locator.ResolveForAutomation(archivePath);
            if (service is null)
            {
                Console.Error.WriteLine($"Unsupported archive type: {Path.GetExtension(archivePath)}");
                return 2;
            }

            Directory.CreateDirectory(destFolder);
            // I5-B: 알릴 것(내용 없이 만든 링크)을 모은다 — 같은 이름 범위(화면용)는 열지 않는다(설정·묻기가 새지 않게)
            using var notes = ExtractionNotes.Begin();
            await service.ExtractAsync(archivePath, destFolder);
            if (notes.HeadlessLine() is { } linkLine)
            {
                Logger.LogNote(HeadlessFailureLogHead + archivePath + Environment.NewLine + linkLine);
                Console.Error.WriteLine(linkLine);
                return 1;
            }
            return 0;
        }
        // 3차 통합 점검: 로그 첫 줄에 압축 경로를 남긴다 — 재배포 뒤 재사용장치가 반디집으로 넘긴 진짜 egg 를 이 로그에서 찾게
        // (재사용장치 화면 로그는 수동 모드에서 파일로 남지 않음. 표준 오류 문구는 그대로).
        catch (UnsupportedFormatException ex)
        {
            Logger.LogError(HeadlessFailureLogHead + archivePath, ex);
            Console.Error.WriteLine($"Unsupported archive type: {ex.Extension}");
            return ErrorMessages.HeadlessExitCode(ex);
        }
        catch (Exception ex)
        {
            Logger.LogError(HeadlessFailureLogHead + archivePath, ex);
            // W2-08: 쉬운 한국어 한 줄만(ErrorCatalog — 영어 원문·안내는 넣지 않음, 원문은 로그에)
            Console.Error.WriteLine(ErrorCatalog.HeadlessMessage(ex));
            return ErrorMessages.HeadlessExitCode(ex);
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        if (_ownsMutex)
        {
            try { _mutex?.ReleaseMutex(); } catch { /* 소유 안 했으면 무시 */ }
        }
        _mutex?.Dispose();
        base.OnExit(e);
    }

    // ===== 단일 인스턴스 IPC =====

    /// <summary>
    /// 이미 실행 중인 인스턴스로 명령줄 인자를 Named Pipe로 전달. 받는 쪽이 '받았음'(1)을 돌려줘야 true(W2-05) —
    /// 받는 쪽이 끝나는 중이라 거절(0)하거나 응답이 없으면 false(이 프로세스가 직접 처리).
    /// 예전 판(받기만 하는 파이프)에는 연결 자체가 안 되어 false — 그때도 직접 처리한다.
    /// </summary>
    private static bool ForwardArgsToRunningInstance(string[] args)
    {
        try
        {
            using var client = new NamedPipeClientStream(".", PipeName, PipeDirection.InOut);
            client.Connect(2000);
            // 경로 인자들을 0x1F(Unit Separator)로 구분해 한 줄로 전송.
            // 탭(\t)은 Windows 파일명에 들어갈 수 있어 깨질 수 있으나, 0x1F는 파일명에 허용되지 않아 안전.
            byte[] line = new System.Text.UTF8Encoding(false).GetBytes(string.Join(ArgSeparator, args) + "\n");
            client.Write(line, 0, line.Length);
            client.Flush();
            // '받았음' 1바이트를 기다린다(받는 쪽은 UI 스레드에서 판단 — 모달 창이 떠 있어도 곧 답함). 너무 오래 답이 없으면 직접 처리한다
            // (받는 쪽은 우리가 응답을 읽어 간 것을 확인한 뒤에만 실행하므로 둘이 같이 하지 않는다 — PipeServerLoop)
            var ack = new byte[1];
            var read = client.ReadAsync(ack, 0, 1);
            if (!read.Wait(TimeSpan.FromSeconds(30))) return false;
            return read.Result == 1 && ack[0] == 1;
        }
        catch
        {
            return false;   // 넘기지 못함 — 직접 처리
        }
    }

    /// <summary>
    /// 앞 일집이 받지 않았을 때(끝나는 중): 그 일집이 끝나 단일 인스턴스 표시를 놓기를 잠깐 기다려 이 프로세스가 첫 일집이 된다.
    /// 끝내 못 받으면 false — 그래도 이 명령은 이 프로세스가 혼자 처리한다(파이프는 열지 않음).
    /// </summary>
    private bool TryTakeOverSingleInstance() => TryAcquireMutex(3000);

    private bool TryAcquireMutex(int timeoutMs)
    {
        if (_mutex is null) return false;
        try
        {
            return _mutex.WaitOne(timeoutMs);
        }
        catch (AbandonedMutexException)
        {
            return true;   // 앞 일집이 놓지 않고 끝남 — 이제 우리 것
        }
        catch
        {
            return false;
        }
    }

    /// <summary>첫 인스턴스에서 백그라운드로 Named Pipe 수신 대기.</summary>
    private void StartPipeServer()
    {
        var thread = new Thread(PipeServerLoop)
        {
            IsBackground = true,
            Name = "IljipPipeServer"
        };
        thread.Start();
    }

    private void PipeServerLoop()
    {
        int consecutiveFailures = 0;
        while (true)
        {
            try
            {
                using var server = new NamedPipeServerStream(
                    PipeName, PipeDirection.InOut, 1,
                    PipeTransmissionMode.Byte, PipeOptions.None);

                server.WaitForConnection();

                var (line, legacy) = ReadLine(server);
                if (line is not null)
                {
                    // 빈 줄 = 인자 없이 다시 실행(메인 창을 앞으로 — 우클릭 풀기만 하던 중이면 그때 띄움)
                    var args = line.Length == 0 ? Array.Empty<string>() : line.Split(ArgSeparator);
                    // 받을지 정하고(끝내는 중이면 거절) 받으면 할 일 수를 바로 올린다 — 답한 뒤 앱이 끝나 명령을 잃지 않게
                    bool accepted = Dispatcher.Invoke(ReserveForwarded);
                    bool delivered = legacy && accepted;   // 예전 판(응답을 읽지 않음, 줄 끝 \r\n)은 받았다고 보고 실행
                    if (!legacy)
                    {
                        try
                        {
                            server.WriteByte(accepted ? (byte)1 : (byte)0);
                            server.Flush();
                            server.WaitForPipeDrain();   // 넘긴 쪽이 '받았음'을 읽어 갔을 때만 실행(그새 포기하고 직접 처리했으면 하지 않음)
                            delivered = accepted;
                        }
                        catch
                        {
                            delivered = false;
                        }
                    }
                    if (accepted) Dispatcher.InvokeAsync(() => RunForwardedAsync(args, delivered));
                }
                consecutiveFailures = 0;   // 정상 처리 시 실패 카운터 리셋
            }
            catch (Exception ex)
            {
                // 파이프 오류는 무시하고 다음 연결을 계속 대기. 단, (1)코드베이스 로깅 관례대로
                // 첫 실패는 반드시 기록하고(이후는 폭주 로그 방지로 간헐 기록), (2)생성/대기가 즉시
                // 반복 실패하면(파이프명 영구 선점·권한 거부 등 회복 불가) 백오프를 점증하다 일정 횟수
                // 후 단일 인스턴스 IPC만 포기한다(백그라운드 스레드 종료, 앱 본체는 정상 동작).
                consecutiveFailures++;
                if (consecutiveFailures == 1 || consecutiveFailures % 50 == 0)
                    Logger.LogError($"PipeServerLoop 실패(x{consecutiveFailures})", ex);
                if (consecutiveFailures >= 100)
                {
                    Logger.LogError("PipeServerLoop 회복 불가 — 단일 인스턴스 IPC 비활성화", ex);
                    return;
                }
                int delay = Math.Min(200 * consecutiveFailures, 2000);
                try { Thread.Sleep(delay); } catch { /* 종료 중 인터럽트 무시 */ }
            }
        }
    }

    /// <summary>
    /// 파이프에서 한 줄(UTF-8, '\n' 까지)을 읽는다. 아무것도 안 보내고 끊으면 null. <c>Legacy</c> = 줄 끝이 '\r\n'(예전 판 — StreamWriter.WriteLine,
    /// 응답을 읽지 않음). StreamReader 를 쓰지 않는 것은 응답을 같은 파이프로 쓰려고.
    /// </summary>
    private static (string? Line, bool Legacy) ReadLine(Stream s)
    {
        var bytes = new List<byte>();
        while (true)
        {
            int b = s.ReadByte();
            if (b < 0) return bytes.Count == 0 ? (null, true) : (Decode(bytes), true);
            if (b == '\n')
            {
                bool legacy = bytes.Count > 0 && bytes[^1] == '\r';
                return (Decode(bytes), legacy);
            }
            bytes.Add((byte)b);
            if (bytes.Count > 1 << 20) return (Decode(bytes), true);   // 비정상적으로 긴 줄
        }

        static string Decode(List<byte> b) => new System.Text.UTF8Encoding(false).GetString(b.ToArray()).TrimEnd('\r');
    }

    /// <summary>두 번째 실행이 넘긴 명령을 받을지(UI 스레드). 끝내는 중이면 받지 않는다(넘긴 쪽이 직접 처리). 받으면 할 일 수를 바로 올린다.</summary>
    private bool ReserveForwarded()
    {
        if (_shuttingDown) return false;
        _busy++;
        return true;
    }

    /// <summary>받아 둔 명령을 처리한다(<paramref name="delivered"/> 가 false 면 넘긴 쪽이 직접 처리했으므로 하지 않고 자리만 놓는다).</summary>
    private async Task RunForwardedAsync(string[] args, bool delivered)
    {
        try
        {
            if (delivered) await OnSecondInstanceLaunched(args);
        }
        catch (Exception ex)
        {
            Logger.LogError("넘겨받은 명령 처리 실패", ex);
        }
        finally
        {
            _busy--;
        }
        await ShutdownIfIdleAsync();
    }

    /// <summary>
    /// 두 번째 인스턴스가 보낸 인자를 처리한다. 탐색기 우클릭 풀기(W2-05, I1-6)는 메인 창을 앞으로 가져오지 않고 진행 창만,
    /// 그 밖은 메인 창을 (처음이면 띄워서) 앞으로 가져온 뒤 처리한다.
    /// </summary>
    private async Task OnSecondInstanceLaunched(string[] args)
    {
        // W2-12: 넘긴 쪽은 '--list-file 목록파일'을 그대로 넘긴다(파이프 한 줄이 길어지지 않게) — 처리하는 이 일집이 그 파일의 경로들로 바꾼다
        args = CommandLineParser.ExpandListFile(args);
        if (!CommandLineParser.Parse(args).ShowMainWindow)
        {
            if (MainWindow?.DataContext is MainViewModel shellVm)
                await RunWindowlessAsync(shellVm, args);
            return;
        }

        var vm = ShowMainWindowAndActivate();
        if (args.Length > 0 && vm is not null)
            await vm.ProcessCommandLineAsync(args);
    }

    // ===== 테마 =====

    /// <summary>테마 팔레트(MergedDictionaries[0])를 교체한다. 색상은 모두 DynamicResource라 즉시 반영.</summary>
    public static void ApplyTheme(AppTheme theme)
    {
        var dicts = Current.Resources.MergedDictionaries;
        string src = theme == AppTheme.Dark
            ? "Resources/Themes/Dark.xaml"
            : "Resources/Themes/Light.xaml";

        var newDict = new ResourceDictionary { Source = new Uri(src, UriKind.Relative) };
        if (dicts.Count > 0)
            dicts[0] = newDict;
        else
            dicts.Add(newDict);

        CurrentTheme = theme;
        ThemeChanged?.Invoke(theme);
    }

    public static void ToggleTheme()
        => ApplyTheme(CurrentTheme == AppTheme.Dark ? AppTheme.Light : AppTheme.Dark);

    /// <summary>Windows 설정의 "앱 모드"가 어둡게로 되어 있는지.</summary>
    private static bool IsSystemDark()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return key?.GetValue("AppsUseLightTheme") is int v && v == 0;
        }
        catch
        {
            return false; // 못 읽으면 라이트 기본
        }
    }
}
