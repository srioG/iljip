using System.ComponentModel;
using System.IO;

namespace Iljip.Services;

/// <summary>관리자 권한으로 다시 띄울 것: 실행 파일과 인자(맨 앞 '--elevated'). <see cref="CommandLine"/> 은 실제로 넘기는 한 줄.</summary>
public sealed record ElevationRetry(string Exe, IReadOnlyList<string> Args)
{
    public string CommandLine => CommandLineQuoting.Join(Args);
}

/// <summary><see cref="ElevationService.BuildRetry"/> 의 답: 다시 띄울 것, 또는 못 하는 까닭(쉬운 한국어).</summary>
public sealed record ElevationPlan(ElevationRetry? Retry, string? Problem);

/// <summary>
/// '관리자 권한으로 다시 시도'(W2-11, I3-16): Program Files 처럼 권한이 필요한 폴더에 풀거나 압축하다 권한 오류가 나면 같은 작업을 관리자 권한(runas)으로 다시 한다.
/// <list type="bullet">
/// <item>명령줄로 나타낼 수 있는 작업은 같은 명령(<see cref="ParsedCommand"/>)을 다시 인자로 만들고 맨 앞에 '--elevated' 를 붙인다
///   (일집 안 '다른 곳에 풀기' → '--extract-to 압축 폴더' — 앞에 '--elevated' 가 있어 창 없이 풀기(재사용장치 계약)로 해석되지 않고 진행 창이 뜬다,
///   '여기에 풀기' → '--extract-here', 압축 → '--compress-to 만들폴더 원본…').</item>
/// <item>명령줄로 나타낼 수 없는 작업(압축 고치기·고른 항목만 풀기 등)은 '관리자 권한으로 일집 다시 열기'(<see cref="ReopenCommand"/> — 인자는 압축 파일 경로)만.</item>
/// <item>암호는 절대 인자에 넣지 않는다(<see cref="ParsedCommand"/> 에 암호 칸이 없음) — 관리자 일집이 다시 묻는다.</item>
/// <item>관리자 일집은 대개 작업 폴더가 System32 라 경로는 모두 전체 경로로, 연결된 네트워크 드라이브(Z:)는 관리자 쪽에 보이지 않아 UNC 로 바꾼다
///   (못 바꾸면 <see cref="NetworkDriveProblem"/>).</item>
/// <item>'--elevated' 로 뜬 일집·이미 관리자 권한인 일집은 다시 권하지 않는다(무한 반복 방지).</item>
/// <item>쓰려던 곳이 네트워크(공유 폴더·NAS·네트워크 드라이브)면 권하지 않는다(<see cref="CanHelpWriteTo"/> — 서버는 관리자 권한이어도 같은 사용자로 보므로
///   쓰기가 막힌 공유 폴더는 그대로 막힘, W2-11 검증 지적). 읽기 전용 파일을 덮어쓰지 못한 것도 권한 오류가 아니다(<see cref="IsAccessDenied"/>).</item>
/// </list>
/// 오류 분류(W2-08 ErrorCatalog 가 다시 하면 그 '권한 없음' 분류의 Actions 에 <see cref="RetryActionId"/> 를 넣을 것)는 지금 <see cref="ActionsFor"/> 가 맡는다.
/// </summary>
public static class ElevationService
{
    /// <summary>오류 창에서 고를 일의 id(W2-08 문제 창의 ProblemAction.Id 와 같은 자리).</summary>
    public const string RetryActionId = "elevate-retry";

    /// <summary>권한 오류 창의 단추.</summary>
    public const string RetryLabel = "관리자 권한으로 다시 시도";

    /// <summary>명령줄로 나타낼 수 없는 작업의 단추.</summary>
    public const string ReopenLabel = "관리자 권한으로 일집 다시 열기";

    /// <summary>권한 오류 문구 뒤에 붙는 질문.</summary>
    public const string RetryQuestion = "관리자 권한으로 다시 해 볼까요? (Windows 가 허락을 물어요. 관리자 암호를 물을 수도 있어요)";

    /// <summary>고치기처럼 명령줄로 나타낼 수 없는 작업의 질문.</summary>
    public const string ReopenQuestion = "관리자 권한으로 일집을 다시 열어서 해 볼까요? (Windows 가 허락을 물어요. 새 창에서 같은 일을 다시 해 주세요)";

    /// <summary>연결된 네트워크 드라이브(Z:)를 UNC 로 못 바꿀 때.</summary>
    public const string NetworkDriveProblem =
        "네트워크 드라이브는 관리자 권한으로 다시 시도할 수 없어요. 내 PC 의 폴더(바탕 화면 등)를 고르거나 담당자에게 알려 주세요.";

    /// <summary>일집 실행 파일 경로를 모를 때(드묾).</summary>
    public const string NoExecutableProblem = "일집 실행 파일을 찾지 못해 관리자 권한으로 다시 시작할 수 없어요.";

    private const int EAccessDenied = unchecked((int)0x80070005);   // E_ACCESSDENIED(HRESULT_FROM_WIN32(ERROR_ACCESS_DENIED))
    private const int ErrorAccessDenied = 5;

    /// <summary>
    /// 권한 오류(쓸 권한 없음)인가: <see cref="UnauthorizedAccessException"/>, HResult 0x80070005(일집이 쉬운 말로 감싼 IOException 포함), Win32 오류 5.
    /// 안쪽 예외도 본다. 일집이 이미 뜻을 정한 오류(<see cref="ArchiveException"/> — 원본을 못 읽음·손상·암호 등)는 아니다.
    /// 덮어쓸 파일이 '읽기 전용'(<see cref="ReadOnlyTargetException"/>)인 것도 아니다 — 관리자 권한으로 다시 해도 똑같이 실패한다(W2-11 검증 지적).
    /// </summary>
    public static bool IsAccessDenied(Exception? ex)
    {
        for (int depth = 0; ex is not null && depth < 10; depth++, ex = ex.InnerException)
        {
            // I5-B: 다른 프로그램이 열어 둔 파일(TargetInUseException)도 권한 문제가 아니다 — 관리자 권한으로 다시 해도 같다
            if (ex is ArchiveException or ReadOnlyTargetException or TargetInUseException) return false;
            if (ex is UnauthorizedAccessException) return true;
            if (ex.HResult == EAccessDenied) return true;
            if (ex is Win32Exception { NativeErrorCode: ErrorAccessDenied }) return true;
        }
        return false;
    }

    /// <summary>
    /// 이 오류의 창에 넣을 일(W2-08 문제 창의 Actions 자리): 권한 오류이고 권할 수 있으면(<paramref name="canElevate"/> — '--elevated' 로 뜬 일집도,
    /// 이미 관리자 권한인 일집도 아님) <see cref="RetryActionId"/> 하나, 아니면 없음.
    /// </summary>
    public static IReadOnlyList<string> ActionsFor(Exception? ex, bool canElevate) =>
        canElevate && IsAccessDenied(ex) ? new[] { RetryActionId } : Array.Empty<string>();

    /// <summary>
    /// 관리자 권한으로 다시 하면 이 곳(<paramref name="writeTarget"/> — 풀 곳·만들 곳·고칠 압축 파일 등 쓰려던 폴더나 파일)에 쓸 수 있게 될 수 있는가.
    /// 네트워크 경로(UNC·네트워크 드라이브 — <see cref="FolderProbe.IsNetwork"/>, 디스크를 보지 않음)면 false: 공유 폴더의 쓰기 권한은 서버가 사용자로 정해
    /// 관리자 권한으로 다시 해도 같다. 모르면(null) true.
    /// </summary>
    public static bool CanHelpWriteTo(string? writeTarget) => string.IsNullOrWhiteSpace(writeTarget) || !FolderProbe.IsNetwork(writeTarget);

    /// <summary>명령줄로 나타낼 수 없는 작업(고치기 등)의 다시 시도: 그 압축 파일을 관리자 일집에서 연다.</summary>
    public static ParsedCommand ReopenCommand(string archivePath) => new(CommandVerb.Open, archivePath, null, Array.Empty<string>());

    /// <summary>
    /// 관리자 권한으로 다시 띄울 실행 파일·인자를 만든다: 경로는 모두 전체 경로로, 네트워크 드라이브는 UNC 로(<paramref name="drives"/>), 맨 앞에 '--elevated'.
    /// 새 창 표시는 뗀다(관리자 일집은 어차피 따로 뜸). 못 하면 <see cref="ElevationPlan.Problem"/>.
    /// </summary>
    public static ElevationPlan BuildRetry(ParsedCommand command, string? exe, IMappedDriveResolver drives)
    {
        if (string.IsNullOrWhiteSpace(exe)) return new ElevationPlan(null, NoExecutableProblem);
        bool failed = false;
        string? Fix(string? path)
        {
            if (path is null) return null;
            string full;
            try
            {
                full = Path.GetFullPath(path);
            }
            catch
            {
                full = path;
            }
            string? unc = drives.ToUncIfMapped(full);
            if (unc is null) failed = true;
            return unc ?? full;
        }

        var archives = command.Archives.Select(a => Fix(a)!).ToList();
        var paths = command.Paths.Select(p => Fix(p)!).ToList();
        var fixedCommand = new ParsedCommand(command.Verb, archives.Count > 0 ? archives[0] : Fix(command.Archive), Fix(command.Dest), paths)
        {
            Archives = archives,
            Elevated = true,
            NewWindow = false,
        };
        if (failed) return new ElevationPlan(null, NetworkDriveProblem);
        return new ElevationPlan(new ElevationRetry(exe, CommandLineParser.ToArgs(fixedCommand)), null);
    }
}
