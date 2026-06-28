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

        // ===== 단일 인스턴스 =====
        // 이미 일집이 떠 있으면, 새로 들어온 명령줄 인자를 기존 창에 넘기고 이 프로세스는 종료한다.
        _mutex = new Mutex(initiallyOwned: true, MutexName, out bool isFirstInstance);
        if (!isFirstInstance)
        {
            ForwardArgsToRunningInstance(e.Args);
            Shutdown();
            return;
        }
        StartPipeServer();

        // 전역 예외 처리: 예외를 파일 로그에 남긴 뒤 사용자에게 안내하고 앱을 계속 실행한다.
        // (로깅은 Logger.LogError가 담당. 정식 배포 시 구조적 로깅으로 교체 가능.)
        DispatcherUnhandledException += (s, args) =>
        {
            Logger.LogError("DispatcherUnhandledException", args.Exception);

            MessageBox.Show(
                $"예기치 못한 오류가 발생했어요:\n\n{args.Exception.Message}",
                "일집",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            args.Handled = true;
        };

        base.OnStartup(e);

        // 명령줄 인자가 있으면 MainWindow의 ViewModel에 위임
        if (e.Args.Length > 0 && MainWindow?.DataContext is MainViewModel vm)
        {
            MainWindow.Loaded += async (_, _) =>
            {
                await vm.ProcessCommandLineAsync(e.Args);
            };
        }
    }

    private static bool IsHeadlessExtractCommand(string[] args)
    {
        return args.Length >= 3
               && args[0].Equals("--extract-to", StringComparison.OrdinalIgnoreCase)
               && File.Exists(args[1])
               && !string.IsNullOrWhiteSpace(args[2]);
    }

    private static async Task<int> RunHeadlessExtractAsync(string[] args)
    {
        string archivePath = args[1];
        string destFolder = args[2];

        try
        {
            var locator = new ArchiveServiceLocator();
            var service = locator.Resolve(archivePath);
            if (service is null)
            {
                Console.Error.WriteLine($"Unsupported archive type: {Path.GetExtension(archivePath)}");
                return 2;
            }

            Directory.CreateDirectory(destFolder);
            await service.ExtractAsync(archivePath, destFolder);
            return 0;
        }
        catch (Exception ex)
        {
            Logger.LogError("Headless extract failed", ex);
            Console.Error.WriteLine(ex.Message);
            return 1;
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        try { _mutex?.ReleaseMutex(); } catch { /* 소유 안 했으면 무시 */ }
        _mutex?.Dispose();
        base.OnExit(e);
    }

    // ===== 단일 인스턴스 IPC =====

    /// <summary>이미 실행 중인 인스턴스로 명령줄 인자를 Named Pipe로 전달.</summary>
    private static void ForwardArgsToRunningInstance(string[] args)
    {
        try
        {
            using var client = new NamedPipeClientStream(".", PipeName, PipeDirection.Out);
            client.Connect(2000);
            using var writer = new StreamWriter(client) { AutoFlush = true };
            // 경로 인자들을 0x1F(Unit Separator)로 구분해 한 줄로 전송.
            // 탭(\t)은 Windows 파일명에 들어갈 수 있어 깨질 수 있으나, 0x1F는 파일명에 허용되지 않아 안전.
            writer.WriteLine(string.Join(ArgSeparator, args));
        }
        catch
        {
            // 전달 실패해도 그냥 종료 (사용자는 기존 창을 직접 쓰면 됨)
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
        while (true)
        {
            try
            {
                using var server = new NamedPipeServerStream(
                    PipeName, PipeDirection.In, 1,
                    PipeTransmissionMode.Byte, PipeOptions.None);

                server.WaitForConnection();

                using var reader = new StreamReader(server);
                string? line = reader.ReadLine();
                if (!string.IsNullOrEmpty(line))
                {
                    var args = line.Split(ArgSeparator);
                    Dispatcher.Invoke(() => OnSecondInstanceLaunched(args));
                }
            }
            catch
            {
                // 파이프 오류는 무시하고 다음 연결을 계속 대기.
                // 단, 생성/대기가 즉시 반복 실패하면(파이프명 선점 등) 백오프 없이 CPU를 점유하므로 잠깐 쉰다.
                try { Thread.Sleep(200); } catch { /* 종료 중 인터럽트 무시 */ }
            }
        }
    }

    /// <summary>두 번째 인스턴스가 보낸 인자를 처리하고 창을 앞으로 가져온다.</summary>
    private async void OnSecondInstanceLaunched(string[] args)
    {
        if (MainWindow is { } win)
        {
            if (win.WindowState == WindowState.Minimized)
                win.WindowState = WindowState.Normal;
            win.Activate();
            // 잠깐 Topmost로 올렸다 내려 확실히 앞으로
            win.Topmost = true;
            win.Topmost = false;
            win.Focus();
        }

        if (args.Length > 0 && MainWindow?.DataContext is MainViewModel vm)
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
