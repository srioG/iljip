using System.IO;

namespace Iljip.Services;

/// <summary>파일 연결 넣기·빼기 한 번의 결과(화면 문구와 명령줄 종료 코드).</summary>
public sealed record AssociationRun(bool Ok, bool Changed, string Message)
{
    /// <summary>'--register-associations'·'--unregister-associations' 의 종료 코드: 0 됨, 1 못 함.</summary>
    public int ExitCode => Ok ? 0 : 1;
}

/// <summary>
/// 파일 연결 넣기·빼기(W2-12, I1-1) — ☰ ▸ 설정 '파일 연결'과 명령줄 '--register-associations [--drop-handler]'·'--unregister-associations' 가 같이 쓴다.
/// 레지스트리·셸 알림은 <see cref="AppServices"/>(시험은 가짜 — 진짜 HKCU·SHChangeNotify 를 부르지 않음). 화면은 띄우지 않고 문구만 돌려준다.
/// 드롭 처리기는 셸 확장 DLL(<see cref="ShellExtensionDllName"/>)이 일집 옆에 있을 때만(MSIX 셸 확장 설치본 — 외부 위치 폴더에 같이 둠).
/// </summary>
public static class FileAssociationCommand
{
    /// <summary>셸 확장 DLL 이름(일집 실행 파일과 같은 폴더 — packaging\Build.ps1 이 같이 둔다).</summary>
    public const string ShellExtensionDllName = "Iljip.ShellExtension.dll";

    /// <summary>일집 옆의 셸 확장 DLL(없으면 null).</summary>
    public static string? DropHandlerDllFor(string? exePath)
    {
        if (string.IsNullOrEmpty(exePath)) return null;
        try
        {
            string dll = Path.Combine(Path.GetDirectoryName(exePath) ?? string.Empty, ShellExtensionDllName);
            return File.Exists(dll) ? dll : null;
        }
        catch
        {
            return null;
        }
    }

    public const string NoExeMessage = "일집 실행 파일 위치를 알 수 없어 연결하지 못했어요.";

    public const string NoDropHandlerNote = "(zip·7z 위에 끌어 놓아 넣기는 셸 확장(우클릭 메뉴)을 설치한 일집에서만 켤 수 있어요.)";

    /// <summary>넣기. <paramref name="dropHandler"/> 면 드롭 처리기도(DLL 이 있을 때만 — 없으면 문구에 알림).</summary>
    public static AssociationRun Register(AppServices services, string? exePath, bool dropHandler)
    {
        if (string.IsNullOrEmpty(exePath)) return new AssociationRun(false, false, NoExeMessage);
        string? dll = dropHandler ? DropHandlerDllFor(exePath) : null;
        try
        {
            var result = new FileAssociationRegistrar(services.Registry).Register(new FileAssociationOptions { ExePath = exePath, DropHandlerDll = dll });
            NotifyQuietly(services);
            string message = dropHandler && dll is null ? result.Message + "\n\n" + NoDropHandlerNote : result.Message;
            return new AssociationRun(true, true, message);
        }
        catch (Exception ex)
        {
            Logger.LogError("파일 연결을 넣지 못함", ex);
            return new AssociationRun(false, false, $"파일 연결을 넣지 못했어요.\n\n{ErrorMessages.ToPlainMessage(ex)}");
        }
    }

    /// <summary>빼기(일집이 넣은 것 — 기록에 적힌 것만).</summary>
    public static AssociationRun Unregister(AppServices services)
    {
        try
        {
            var result = new FileAssociationRegistrar(services.Registry).Unregister();
            if (result.Changed) NotifyQuietly(services);
            return new AssociationRun(true, result.Changed, result.Message);
        }
        catch (Exception ex)
        {
            Logger.LogError("파일 연결을 빼지 못함", ex);
            return new AssociationRun(false, false, $"파일 연결을 빼지 못했어요.\n\n{ErrorMessages.ToPlainMessage(ex)}");
        }
    }

    /// <summary>
    /// 명령줄('--register-associations [--drop-handler]'·'--unregister-associations')을 처리한다. 됐으면 설정 shell.associationsRegistered 를 기록(최신 설정에 그 칸만).
    /// 다른 동사면 null.
    /// </summary>
    public static AssociationRun? Run(ParsedCommand cmd, AppServices services, string? exePath)
    {
        AssociationRun run;
        bool registered;
        switch (cmd.Verb)
        {
            case CommandVerb.RegisterAssociations:
                run = Register(services, exePath, cmd.Paths.Contains(CommandLineParser.DropHandlerOption, StringComparer.OrdinalIgnoreCase));
                registered = true;
                break;
            case CommandVerb.UnregisterAssociations:
                run = Unregister(services);
                registered = false;
                break;
            default:
                return null;
        }
        if (run.Ok && run.Changed)
        {
            try { services.Settings.Update(s => s.Shell.AssociationsRegistered = registered); }
            catch (Exception ex) { Logger.LogError("설정(shell.associationsRegistered)을 저장하지 못함", ex); }   // 기록용 — 연결 자체는 됐다
        }
        return run;
    }

    private static void NotifyQuietly(AppServices services)
    {
        try { services.Shell.NotifyAssociationsChanged(); }
        catch (Exception ex) { Logger.LogError("파일 연결 바뀜 알림 실패", ex); }   // 알림만 못 함 — 탐색기를 다시 켜면 보임
    }
}
