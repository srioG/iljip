using System.IO;

namespace Iljip.Services;

/// <summary>일집이 켜질 때 어떻게 뜰지(<see cref="StartupDecision.Decide"/>).</summary>
public enum StartupKind
{
    /// <summary>창 없이 풀기('--extract-to 압축 폴더' — 재사용장치 계약). 단일 인스턴스와 상관없이 혼자 풀고 끝난다.</summary>
    Headless,

    /// <summary>
    /// 따로 뜨는 창('--new-window' — 새 창, '--elevated' — 관리자 권한으로 다시 시도): 단일 인스턴스 표시(뮤텍스)를 잡지 않고 파이프로 넘기지도, 파이프를 열지도 않는다.
    /// 그래야 뒤에 켜는 일집이 이 창이 아니라 처음 일집에 붙는다(관리자 일집의 파이프는 권한이 달라 막히기도 함).
    /// </summary>
    Independent,

    /// <summary>첫 일집 — 파이프 서버가 되어 뒤에 켜지는 일집의 명령을 받는다.</summary>
    FirstInstance,

    /// <summary>이미 떠 있는 일집에 명령을 넘긴다(못 넘기면 넘겨받기를 해 보고, 그래도 안 되면 혼자 처리 — W2-05).</summary>
    Forward,
}

/// <summary>
/// 켜질 때의 결정(W2-11 — App.OnStartup 에서 떼어 낸 순수 함수, 시험은 StartupDecisionTests). 순서:
/// ① 창 없이 풀기(예전 판정 그대로 — 첫 인자가 정확히 '--extract-to'(대소문자 무시), 둘째가 있는 파일, 셋째가 빈칸이 아님. '--elevated' 가 앞에 붙으면 첫 인자가 달라 여기 걸리지 않음)
/// ② '--new-window'·'--elevated' 표시면 따로 뜨는 창(뮤텍스를 얻었든 못 얻었든)
/// ③ 그 밖은 예전대로 — 뮤텍스를 얻었으면 첫 일집, 못 얻었으면 넘기기.
/// </summary>
public static class StartupDecision
{
    public static StartupKind Decide(IReadOnlyList<string>? args, bool mutexAcquired, Func<string, bool>? fileExists = null)
    {
        args ??= Array.Empty<string>();
        if (IsHeadlessExtract(args, fileExists)) return StartupKind.Headless;
        if (SkipsSingleInstance(args)) return StartupKind.Independent;
        return mutexAcquired ? StartupKind.FirstInstance : StartupKind.Forward;
    }

    /// <summary>창 없이 풀기인가(재사용장치 계약 — 예전 App.IsHeadlessExtractCommand 와 같은 판정).</summary>
    public static bool IsHeadlessExtract(IReadOnlyList<string>? args, Func<string, bool>? fileExists = null)
    {
        if (args is null || args.Count < 3) return false;
        fileExists ??= File.Exists;
        return args[0].Equals(CommandLineParser.ExtractToKeyword, StringComparison.OrdinalIgnoreCase)
               && fileExists(args[1])
               && !string.IsNullOrWhiteSpace(args[2]);
    }

    /// <summary>단일 인스턴스(뮤텍스·파이프)를 건너뛰는가 — 맨 앞에 '--new-window' 나 '--elevated' 가 있으면.</summary>
    public static bool SkipsSingleInstance(IReadOnlyList<string>? args)
    {
        if (args is null || args.Count == 0) return false;
        var cmd = CommandLineParser.Parse(args as string[] ?? args.ToArray());
        return cmd.NewWindow || cmd.Elevated;
    }
}
