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

    /// <summary>
    /// 파일 연결 넣기·빼기('--register-associations'·'--unregister-associations' — W2-12). 자체 프로세스로 HKCU 에만 쓰고 끝난다
    /// (단일 인스턴스·파이프와 상관없음 — 떠 있는 일집에 넘기지 않음).
    /// </summary>
    Associations,
}

/// <summary>
/// 켜질 때의 결정(W2-11 — App.OnStartup 에서 떼어 낸 순수 함수, 시험은 StartupDecisionTests). 순서:
/// ① 창 없이 풀기 — 첫 인자가 정확히 '--extract-to'(대소문자 무시)면 늘(I5-B: 예전엔 둘째가 있는 파일·셋째가 빈칸이 아닐 때만이라, 없는 경로·폴더·빈 문자열을 주면
///    일반 실행으로 넘어가 메인 창과 '문제가 생겼어요' 창이 떠 끝나지 않았다 — 최종 감사 IX-F5. 이제 인자 문제는 창 없이 쉬운 한국어 한 줄·종료 코드 1,
///    <see cref="HeadlessArgumentProblem"/>). '--elevated' 가 앞에 붙으면 첫 인자가 달라 여기 걸리지 않음(대화형 — 진행 창)
/// ② '--new-window'·'--elevated' 표시면 따로 뜨는 창(뮤텍스를 얻었든 못 얻었든)
/// ③ 그 밖은 예전대로 — 뮤텍스를 얻었으면 첫 일집, 못 얻었으면 넘기기.
/// </summary>
public static class StartupDecision
{
    public static StartupKind Decide(IReadOnlyList<string>? args, bool mutexAcquired, Func<string, bool>? fileExists = null)
    {
        args ??= Array.Empty<string>();
        if (IsHeadlessExtract(args, fileExists)) return StartupKind.Headless;
        if (IsAssociationCommand(args)) return StartupKind.Associations;
        if (SkipsSingleInstance(args)) return StartupKind.Independent;
        return mutexAcquired ? StartupKind.FirstInstance : StartupKind.Forward;
    }

    /// <summary>
    /// 창 없이 풀기인가(재사용장치 계약) — 첫 인자가 '--extract-to'(대소문자 무시)면 인자가 모자라거나 압축 파일이 없어도 그렇다(I5-B — 창을 띄우지 않고
    /// <see cref="HeadlessArgumentProblem"/> 의 한 줄·종료 코드 1 로 끝내려고). <paramref name="fileExists"/> 는 예전 판정(둘째가 있는 파일)에 쓰던 자리 —
    /// 이제 보지 않는다(부르는 모양을 바꾸지 않으려고 남김).
    /// </summary>
    public static bool IsHeadlessExtract(IReadOnlyList<string>? args, Func<string, bool>? fileExists = null)
    {
        _ = fileExists;
        return args is { Count: > 0 } && args[0].Equals(CommandLineParser.ExtractToKeyword, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>창 없이 풀기 인자가 모자람(압축 파일·풀 폴더 중 빈 것) — 표준 오류 한 줄.</summary>
    public const string HeadlessUsageMessage = "창 없이 풀기에는 압축 파일과 풀 폴더를 모두 주어야 해요. (Iljip.exe --extract-to <압축 파일> <풀 폴더>)";

    /// <summary>창 없이 풀기에 준 압축 파일이 없음 — 표준 오류 한 줄(뒤에 경로).</summary>
    public const string HeadlessMissingArchiveMessage = "풀 압축 파일을 찾을 수 없어요: ";

    /// <summary>창 없이 풀기에 압축 파일 대신 폴더를 줌 — 표준 오류 한 줄(뒤에 경로).</summary>
    public const string HeadlessFolderGivenMessage = "풀 압축 파일 대신 폴더를 받았어요: ";

    /// <summary>
    /// 창 없이 풀기('--extract-to 압축 폴더') 인자의 문제 — 쉬운 한국어 한 줄(없으면 null). I5-B(최종 감사 IX-F5): 압축 파일이 비었거나 풀 폴더가 비었음·
    /// 압축 파일이 없음(백신 격리·경로 오류)·압축 파일 자리에 폴더. App 은 이 줄을 표준 오류로, 오류 로그에 'Headless extract failed: &lt;경로&gt;' 와 함께 남기고
    /// 종료 코드 1(재사용장치 약속의 '그 밖의 실패' — 반디집으로 다시 풂. '압축 아님' 표지 없음). 창은 띄우지 않는다.
    /// </summary>
    public static string? HeadlessArgumentProblem(IReadOnlyList<string> args, Func<string, bool>? fileExists = null, Func<string, bool>? directoryExists = null)
    {
        fileExists ??= File.Exists;
        directoryExists ??= Directory.Exists;
        string archive = args.Count > 1 ? args[1] : string.Empty;
        string dest = args.Count > 2 ? args[2] : string.Empty;
        if (string.IsNullOrWhiteSpace(archive) || string.IsNullOrWhiteSpace(dest)) return HeadlessUsageMessage;
        if (directoryExists(archive)) return HeadlessFolderGivenMessage + archive;
        if (!fileExists(archive)) return HeadlessMissingArchiveMessage + archive;
        return null;
    }

    /// <summary>단일 인스턴스(뮤텍스·파이프)를 건너뛰는가 — 맨 앞에 '--new-window' 나 '--elevated' 가 있으면, 또는 파일 연결 명령(W2-12)이면.</summary>
    public static bool SkipsSingleInstance(IReadOnlyList<string>? args)
    {
        if (args is null || args.Count == 0) return false;
        var cmd = CommandLineParser.Parse(args as string[] ?? args.ToArray());
        return cmd.NewWindow || cmd.Elevated || IsAssociationCommand(args);
    }

    /// <summary>파일 연결 넣기·빼기인가(W2-12 — 첫 인자(표시 뒤)가 '--register-associations'·'--unregister-associations').</summary>
    public static bool IsAssociationCommand(IReadOnlyList<string>? args)
    {
        if (args is null || args.Count == 0) return false;
        var verb = CommandLineParser.Parse(args as string[] ?? args.ToArray()).Verb;
        return verb is CommandVerb.RegisterAssociations or CommandVerb.UnregisterAssociations;
    }
}
