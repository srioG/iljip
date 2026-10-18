using System.IO;
using System.Text;

namespace Iljip.Services;

/// <summary>사용자에게 알릴 문제의 종류(W2-08 — I2-8 오류 원인 구분). 화면 문구·고를 수 있는 일(<see cref="UserProblem.Actions"/>)이 이것으로 갈린다.</summary>
public enum ProblemKind
{
    /// <summary>암호가 걸린 압축인데 암호를 주지 않았다.</summary>
    PasswordRequired,
    /// <summary>목록(파일 이름)까지 암호로 잠긴 압축.</summary>
    ListPasswordRequired,
    /// <summary>암호가 틀렸다(손상일 수도 — <see cref="WrongPasswordException.MayBeCorrupt"/>).</summary>
    WrongPassword,
    /// <summary>압축 파일이 손상됐다.</summary>
    Corrupt,
    /// <summary>압축 파일의 끝이 잘렸다(덜 받아짐·덜 복사됨).</summary>
    Truncated,
    /// <summary>풀린 내용이 원래와 다르다(검사값 불일치).</summary>
    ChecksumMismatch,
    /// <summary>나눠진 압축의 조각이 없다.</summary>
    VolumeMissing,
    /// <summary>나눠진 압축의 조각이 덜 받아졌다.</summary>
    VolumeIncomplete,
    /// <summary>일집이 열 수 없는 형식.</summary>
    UnsupportedFormat,
    /// <summary>형식은 알지만 그 안의 압축 방식을 풀 수 없다.</summary>
    UnsupportedMethod,
    /// <summary>7z.dll 을 불러오지 못했다.</summary>
    EngineUnavailable,
    /// <summary>압축할 원본을 못 읽었다.</summary>
    SourceUnreadable,
    /// <summary>압축할 원본을 하나도 담지 못했다.</summary>
    NothingToCompress,
    /// <summary>고칠 수 없는 형식.</summary>
    NotEditable,
    /// <summary>저장할 공간이 부족하다.</summary>
    DiskFull,
    /// <summary>경로가 너무 길다.</summary>
    PathTooLong,
    /// <summary>그 위치에 쓸(읽을) 권한이 없다.</summary>
    AccessDenied,
    /// <summary>다른 프로그램(보안 프로그램 포함)이 파일을 쓰고 있다.</summary>
    InUse,
    /// <summary>사용자가 취소했다(창을 띄우지 않는다).</summary>
    Cancelled,
    /// <summary>알 수 없는 문제(영어 원문은 '자세히'·로그에만).</summary>
    Unknown,

    // ───── 설계 표에는 없지만 예전부터 따로 알리던 것(문구 그대로) ─────

    /// <summary>위험한 경로(대상 폴더 밖)가 들어 있어 그 파일은 풀지 않았다.</summary>
    UnsafePath,
    /// <summary>같은 이름의 파일·폴더가 이미 있어 만들 수 없다.</summary>
    AlreadyExists,
    /// <summary>파일이나 폴더를 찾을 수 없다.</summary>
    NotFound,
    /// <summary>할 수 없는 요청(조각이 너무 많음·나눌 크기가 너무 작음·고치기 요청 거절 등 — 일집 예외 문구 그대로).</summary>
    Rejected,
    /// <summary>서비스가 이미 쉬운 한국어로 쓴 그 밖의 문제(문구 그대로).</summary>
    Other,
}

/// <summary>오류 창에서 고를 수 있는 일 하나(버튼). <see cref="Id"/> 는 화면이 무엇을 할지 가르는 이름, <see cref="Label"/> 은 버튼 글자.</summary>
public sealed record ProblemAction(string Id, string Label);

/// <summary>
/// 사용자에게 보일 문제 하나(W2-08 — <see cref="ErrorCatalog.Describe"/> 가 만든다). 첫 화면에는 <see cref="Heading"/>(무엇을 하다가)·<see cref="Message"/>·
/// <see cref="Advice"/> 만 보이고, 영어 원문(예외 형식·엔진 문구·코드·호출 위치)은 <see cref="Details"/>('자세히' 아래·로그)에만 둔다.
/// </summary>
public sealed record UserProblem
{
    public ProblemKind Kind { get; init; }

    /// <summary>문제 종류의 짧은 이름(예: '압축 파일이 손상됐어요') — 창 제목·큰 글씨(<see cref="Heading"/> 이 없을 때).</summary>
    public string Title { get; init; } = string.Empty;

    /// <summary>쉬운 한국어 문구(영어 원문 없음).</summary>
    public string Message { get; init; } = string.Empty;

    /// <summary>어떻게 하면 되는지(없으면 null).</summary>
    public string? Advice { get; init; }

    /// <summary>'자세히' 아래 원문: 예외 형식·원래 문구·코드(16진수)·안쪽 예외·호출 위치 다섯 줄. 복사해 담당자에게 보낼 수 있다.</summary>
    public string Details { get; init; } = string.Empty;

    /// <summary>창에서 고를 수 있는 일(버튼 — '관리자 권한으로 다시 시도'·'ZIP 고치기 시도' 등). 없으면 비어 있음.</summary>
    public IReadOnlyList<ProblemAction> Actions { get; init; } = Array.Empty<ProblemAction>();

    /// <summary>무엇을 하다가 난 문제인지(예: '파일을 열 수 없어요') — 부르는 쪽이 채운다. 없으면 null(창은 <see cref="Title"/>).</summary>
    public string? Heading { get; init; }

    /// <summary>첫 화면에 보일 글 전부(무엇을 하다가 + 문구 + 어떻게) — '자세히'는 넣지 않는다.</summary>
    public string ToDisplayText() =>
        string.Join("\n\n", new[] { Heading, Message, Advice }.Where(s => !string.IsNullOrEmpty(s)));
}

/// <summary>
/// 예외 → 사용자에게 보일 문제(W2-08 — I2-8). 화면·창 없이 풀기·여러 개 풀기 요약·만든 압축 시험의 오류 문구는 모두 여기 한 곳에서 만든다
/// (<see cref="ErrorMessages"/> 는 이것을 감싼 예전 이름).
/// <list type="bullet">
/// <item>일집 예외(<see cref="ArchiveException"/>)는 문구가 이미 쉬운 한국어라 그대로(검사값 불일치만 '검사값' 말로 바꿈 — 영어 'CRC' 가 보이지 않게).</item>
/// <item>입출력 오류는 형식·코드(HResult)로 갈라 쉬운 한국어로: 공간 부족(0x80070070·0x80070027)·경로가 너무 김(0x800700CE)·권한 없음(0x80070005)·
///   다른 프로그램이 씀(0x80070020·0x80070021 — 보안 프로그램이 검사 중일 수 있음).</item>
/// <item>엔진(SharpCompress·SharpZipLib·7z.dll·EggDotNet) 예외는 손상·잘림으로. 정말 모르는 예외는 '알 수 없는 문제가 생겼어요.' + 로그 안내.</item>
/// <item>영어 원문(형식·문구·코드·안쪽 예외·호출 위치 다섯 줄)은 <see cref="UserProblem.Details"/> 에만.</item>
/// </list>
/// ★ 손상·암호·조각·7z.dll 문구에 '압축 파일이 아닌'·'not archive' 를 넣지 말 것 — 재사용장치가 그 말로 '압축 아님'을 분류한다(재사용장치_processor.py 491행).
/// </summary>
public static class ErrorCatalog
{
    private const int ErrorFileNotFound = unchecked((int)0x80070002);      // ERROR_FILE_NOT_FOUND
    private const int ErrorPathNotFound = unchecked((int)0x80070003);      // ERROR_PATH_NOT_FOUND
    private const int ErrorAccessDenied = unchecked((int)0x80070005);      // ERROR_ACCESS_DENIED
    private const int ErrorSharingViolation = unchecked((int)0x80070020);  // ERROR_SHARING_VIOLATION(다른 프로그램이 파일을 열어 둠)
    private const int ErrorLockViolation = unchecked((int)0x80070021);     // ERROR_LOCK_VIOLATION
    private const int ErrorHandleDiskFull = unchecked((int)0x80070027);    // ERROR_HANDLE_DISK_FULL
    private const int ErrorFileExists = unchecked((int)0x80070050);        // ERROR_FILE_EXISTS
    private const int ErrorDiskFull = unchecked((int)0x80070070);          // ERROR_DISK_FULL
    private const int ErrorAlreadyExists = unchecked((int)0x800700B7);     // ERROR_ALREADY_EXISTS
    private const int ErrorFilenameTooLong = unchecked((int)0x800700CE);   // ERROR_FILENAME_EXCED_RANGE
    private const int ErrorInvalidName = unchecked((int)0x8007007B);       // ERROR_INVALID_NAME(이름 한 조각이 255자를 넘을 때도 — 긴 경로를 켠 PC)

    /// <summary>운영 체제 오류 번호 중 네트워크(공유 폴더) 연결 문제.</summary>
    private static readonly HashSet<int> NetworkCodes = new() { 51, 53, 54, 55, 59, 64, 65, 67, 1203, 1222, 1231, 1232, 1236 };

    /// <summary>운영 체제 오류 번호 중 장치(디스크·USB) 읽기·쓰기 문제.</summary>
    private static readonly HashSet<int> DeviceCodes = new() { 21, 23, 25, 27, 29, 30, 31, 483, 1006, 1110, 1112, 1117, 1167, 1392 };

    /// <summary>운영 체제 오류 번호 중 이름에 쓸 수 없는 글자.</summary>
    private static readonly HashSet<int> BadNameCodes = new() { 123, 161 };

    // ───── 문구(설계 표 그대로) ─────

    public const string DiskFullMessage = "저장할 공간이 부족해요.";
    public const string AccessDeniedMessage = "이 위치에 쓸 권한이 없어요";
    public const string InUseMessage = "다른 프로그램(보안 프로그램 포함)이 파일을 쓰고 있어요. 잠시 뒤 다시 해 주세요.";
    public const string PathTooLongMessage = "경로가 너무 길어요. 더 짧은 위치를 골라 주세요.";
    public const string UnknownMessage = "알 수 없는 문제가 생겼어요.";
    public const string UnknownAdvice = "같은 일이 되풀이되면 [로그 폴더 열기]를 눌러 기록 파일을 담당자에게 보내 주세요.";
    public const string InUseAdvice = "보안 프로그램이 검사 중일 수 있어요. 그 파일을 연 프로그램이 있으면 닫아 주세요.";
    public const string DiskFullAdvice = "필요 없는 파일을 지우거나 다른 드라이브를 골라 주세요.";
    public const string AccessDeniedAdvice = "다른 폴더(예: 바탕 화면)를 골라 보세요.";
    public const string CancelledMessage = "취소했어요.";

    /// <summary>엔진이 그대로 낸 '데이터 오류'(손상인지 잘림인지 모름)의 쉬운 문구 — 예전 <see cref="ErrorMessages.UnknownPlainMessage"/> 와 같다.</summary>
    public const string EngineDataMessage = "파일이 손상됐거나 끝까지 받아지지 않은 것 같아요. 원본을 다시 받아서 풀어 보세요.";

    /// <summary>암호 문제를 엔진 예외 그대로 받았을 때(번역이 없는 경로).</summary>
    public const string EnginePasswordMessage = "암호가 없거나 맞지 않아요. 암호를 다시 넣어 주세요.";

    // ───── 운영 체제가 알린 그 밖의 입출력 오류(W2-08 검증 지적 2차 — 운영 체제 문구를 그대로 보이지 않게) ─────

    public const string NetworkMessage = "네트워크 위치(공유 폴더)에 연결할 수 없어요";
    public const string NetworkAdvice = "네트워크 연결을 확인하고 다시 해 주세요. 파일을 내 PC 로 복사해서 해 보셔도 돼요.";
    public const string DeviceMessage = "디스크나 USB 에서 읽거나 쓰지 못했어요";
    public const string DeviceAdvice = "장치가 제대로 연결돼 있는지 확인하고 다시 해 주세요. 되풀이되면 파일을 다른 곳에 복사해서 해 보세요.";
    public const string BadNameMessage = "파일이나 폴더 이름에 쓸 수 없는 글자가 들어 있어요";

    // ───── 고를 수 있는 일(버튼) ─────

    /// <summary>'관리자 권한으로 다시 시도'(권한 없음 — 실제 동작은 W2-11. 그 전까지 화면은 이 버튼을 걸러 낸다).</summary>
    public const string ElevateActionId = "elevate";

    /// <summary>'ZIP 고치기 시도'(ZIP 이 손상·잘림·검사값 불일치일 때 — 살릴 수 있는 파일만 새 ZIP 으로, I3-12).</summary>
    public const string RepairZipActionId = "repair-zip";

    public static readonly ProblemAction ElevateAction = new(ElevateActionId, "관리자 권한으로 다시 시도");
    public static readonly ProblemAction RepairZipAction = new(RepairZipActionId, "ZIP 고치기 시도");

    /// <summary>검사값 불일치 문구(항목 이름 포함).</summary>
    public static string ChecksumMessage(string? entryPath) =>
        string.IsNullOrEmpty(entryPath)
            ? "파일 내용이 원래와 달라요(검사값 불일치). 압축 파일이 손상됐을 수 있어요."
            : $"‘{entryPath}’의 내용이 원래와 달라요(검사값 불일치). 압축 파일이 손상됐을 수 있어요.";

    /// <summary>
    /// 예외를 사용자에게 보일 문제로 바꾼다. <paramref name="archiveName"/>(압축 파일 이름 — 경로여도 됨)이 '.zip' 이고 손상·잘림·검사값 불일치면
    /// 'ZIP 고치기 시도'를 고를 수 있게 한다. 권한 없음은 '관리자 권한으로 다시 시도'.
    /// </summary>
    public static UserProblem Describe(Exception ex, string? archiveName = null)
    {
        var (kind, message, advice) = Classify(ex);
        var actions = new List<ProblemAction>();
        if (kind == ProblemKind.AccessDenied) actions.Add(ElevateAction);
        if (kind is ProblemKind.Corrupt or ProblemKind.Truncated or ProblemKind.ChecksumMismatch && IsZipName(archiveName))
            actions.Add(RepairZipAction);
        return new UserProblem
        {
            Kind = kind,
            Title = TitleOf(kind),
            Message = message,
            Advice = advice,
            Details = DetailsOf(ex),
            Actions = actions,
        };
    }

    /// <summary>압축(만들기)하다 난 모르는 오류의 쉬운 문구.</summary>
    public const string CompressFailedMessage = "압축하는 중에 알 수 없는 문제가 생겼어요.";

    /// <summary>
    /// 압축(만들기) 실패의 문제(W2-08 검증 지적 2차): <see cref="Describe"/> 와 같되, 엔진이 낸 데이터 오류·모르는 오류는 풀기 문구
    /// ('…원본을 다시 받아서 풀어 보세요')가 아니라 '압축하는 중에 알 수 없는 문제가 생겼어요.'(+ 로그 안내). 사용자 환경 문제(공간·권한·잠금 — 엔진이 감싼 것도)·
    /// 일집 예외(원본 못 읽음 등)는 그 문구 그대로. 고칠 일(ZIP 고치기 등)은 없다.
    /// </summary>
    public static UserProblem DescribeCompressFailure(Exception ex)
    {
        var p = Describe(ex) with { Actions = Array.Empty<ProblemAction>() };
        if (ex is ArchiveException || IsEnvironmentProblem(ex)
            || p.Kind is not (ProblemKind.Unknown or ProblemKind.Corrupt or ProblemKind.Truncated or ProblemKind.ChecksumMismatch or ProblemKind.WrongPassword))
            return p;
        return p with { Kind = ProblemKind.Unknown, Title = TitleOf(ProblemKind.Unknown), Message = CompressFailedMessage, Advice = UnknownAdvice };
    }

    /// <summary>
    /// 창 없이 풀기(--extract-to, 재사용장치)의 stderr 한 줄: <see cref="Describe"/> 의 문구만(줄바꿈은 빈칸). 영어 원문·안내는 넣지 않는다.
    /// 지원하지 않는 형식의 영어 'Unsupported archive type: 확장자'(종료 코드 2)는 App 이 따로 쓴다(재사용장치 계약).
    /// </summary>
    public static string HeadlessMessage(Exception ex) => Describe(ex).Message.ReplaceLineEndings(" ");

    /// <summary>
    /// 사용자 환경(공간·권한·다른 프로그램·경로·없는 파일) 문제인가 — 압축 파일 자체의 문제가 아니다. 압축 검사(<see cref="ArchiveTestService"/>)는
    /// 이것을 '손상'으로 적지 않고 그대로 알린다. 운영 체제가 알린 그 밖의 입출력 오류(네트워크 경로가 끊김·USB 를 뺌·디스크 읽기 오류 등 —
    /// 코드가 0x8007xxxx 인 IOException, 엔진이 낸 것·스트림 끝은 빼고)도 환경 문제다(W2-08 검증 지적: 압축 검사가 '문제가 있어요'·고치기를 권하고,
    /// ZIP 고치기가 그 항목을 '못 살린 파일'로 세어 반쪽 결과를 만들었다).
    /// </summary>
    public static bool IsEnvironmentProblem(Exception ex)
    {
        // 엔진이 감싼 예외는 안쪽 원인으로(W2-08 검증 지적 2차 — Classify 와 같게)
        if (ex is not (ArchiveException or OperationCanceledException) && EnvironmentKind(ex) is null && !IsOsIoError(ex) && InnerCause(ex) is { } cause)
            return IsEnvironmentProblem(cause);
        var kind = Classify(ex).Kind;
        if (kind is ProblemKind.DiskFull or ProblemKind.PathTooLong or ProblemKind.AccessDenied or ProblemKind.InUse
            or ProblemKind.AlreadyExists or ProblemKind.NotFound)
            return true;
        return kind is ProblemKind.Other or ProblemKind.Unknown && IsOsIoError(ex);
    }

    /// <summary>운영 체제가 알린 입출력 오류인가(코드가 Win32 — 0x8007xxxx, 엔진 예외·스트림 끝 아님).</summary>
    private static bool IsOsIoError(Exception ex) =>
        ex is IOException and not EndOfStreamException && (ex.HResult & unchecked((int)0xFFFF0000)) == unchecked((int)0x80070000) && !FromEngine(ex);

    /// <summary>
    /// 문구가 운영 체제의 오류 문구(FormatMessage — 한국어 Windows 면 한국어)인가. .NET 은 공간 부족(112·39)·잠금(33) 같은 코드의 문구를
    /// 운영 체제에서 받아 '문구' 또는 '문구 : '경로''로 만든다 — 서비스가 쓴 쉬운 한국어 문구와 가르려고 본다(W2-08 검증 지적: 한국어 Windows 에서
    /// '저장할 공간이 부족해요.' 대신 '디스크 공간이 부족합니다. : …'가 그대로 보임).
    /// </summary>
    internal static bool IsOsMessage(Exception ex)
    {
        if ((ex.HResult & unchecked((int)0xFFFF0000)) != unchecked((int)0x80070000)) return false;
        string os;
        try
        {
            os = System.Runtime.InteropServices.Marshal.GetPInvokeErrorMessage(ex.HResult & 0xFFFF).TrimEnd();
        }
        catch
        {
            return false;
        }
        if (os.Length == 0) return false;
        string m = (ex.Message ?? string.Empty).TrimEnd();
        return m == os || m.StartsWith(os + " : ", StringComparison.Ordinal);
    }

    private static (ProblemKind Kind, string Message, string? Advice) Classify(Exception ex)
    {
        switch (ex)
        {
            case OperationCanceledException:
                return (ProblemKind.Cancelled, CancelledMessage, null);
            case ArchiveException a:
                return (KindOf(a), a is ChecksumMismatchException c ? ChecksumMessage(c.EntryPath) : a.Message, null);
            case InvalidDataException d when d.Message.StartsWith(ErrorMessages.UnsafePathPrefix, StringComparison.Ordinal):
                return (ProblemKind.UnsafePath, $"위험한 경로가 들어 있어 그 파일은 풀지 않았어요: {d.Message[ErrorMessages.UnsafePathPrefix.Length..]}", null);
        }

        // 엔진(7z.dll 등)이 감싼 예외라도 안쪽에 사용자 환경 문제(공간·권한·잠금·경로·운영 체제 입출력 오류)·취소가 있으면 그 원인으로
        // (W2-08 검증 지적 2차: 바깥만 보고 '파일이 손상됐거나 끝까지 받아지지 않은 것 같아요'로 알림 — 압축 실패에도).
        if (EnvironmentKind(ex) is null && !IsOsIoError(ex) && InnerCause(ex) is { } cause) return Classify(cause);

        // 입출력 환경 문제(형식·코드로) — 서비스가 이미 쉬운 한국어로 썼으면 그 문구를 살린다(예: '저장할 공간이 부족해요. 풀려면 1.2GB 정도가 필요해요.').
        // 운영 체제가 준 문구(한국어 Windows 면 한국어 — '디스크 공간이 부족합니다. : …')는 서비스 문구가 아니니 설계 문구로 바꾼다.
        // IJ3-02((g)): 경로 안의 작은따옴표(폴더 '26년 견적)에 속지 않게 — 경로(첫 따옴표~마지막 따옴표)를 통째로 빼고, 영어 낱말이 넷 넘게 이어지면 영어 문구로 본다.
        // 엔진(SharpCompress·SharpSevenZip 등)이 낸 문구는 늘 영어다 — 따옴표 없이 붙은 한글 경로·이름이 있어도 한국어 문구로 보지 않는다(IJ3-02 5차).
        bool korean = !FromEngine(ex) && LooksKorean(ex.Message);
        if (EnvironmentKind(ex) is { } env)
        {
            string text = korean && !IsOsMessage(ex) ? ex.Message : env switch
            {
                ProblemKind.DiskFull => DiskFullMessage,
                ProblemKind.PathTooLong => PathTooLongMessage,
                ProblemKind.AccessDenied => AccessDeniedMessage + PathSuffix(ex.Message),
                ProblemKind.InUse => InUseMessage + PathNote(ex.Message),
                ProblemKind.AlreadyExists => "같은 이름의 파일이나 폴더가 이미 있어 만들 수 없어요" + PathSuffix(ex.Message),
                _ => "파일이나 폴더를 찾을 수 없어요" + PathSuffix(ex.Message),
            };
            string? advice = env switch
            {
                ProblemKind.DiskFull => DiskFullAdvice,
                ProblemKind.AccessDenied => AccessDeniedAdvice,
                ProblemKind.InUse => InUseAdvice,
                _ => null,
            };
            return (env, text, advice);
        }

        // 운영 체제가 알린 그 밖의 입출력 오류(네트워크·장치·이름 등) — 운영 체제 문구(한국어 Windows 면 '네트워크 경로를 찾지 못했습니다. : …', 영어 Windows 면 영어)를
        // 그대로 보이지 않고 쉬운 말로(W2-08 검증 지적 2차). 서비스가 한국어로 쓴 문구는 그대로.
        if (IsOsIoError(ex) && (!korean || IsOsMessage(ex))) return OsIoProblem(ex);

        // 서비스가 이미 쉬운 한국어로 쓴 문구(TAR '도중에 바뀌었어요'·EGG '풀다가 멈췄어요' 등)는 그대로.
        // 따옴표 안(경로)의 한글은 세지 않는다 — 회사 PC 경로는 거의 한글이라, 한글 폴더가 든 영어 .NET 문구가 그대로 나가던 문제.
        if (korean)
            return (ex is InvalidDataException or EndOfStreamException ? ProblemKind.Corrupt : ProblemKind.Other, ex.Message, null);

        // 엔진 예외가 번역되지 않고 올라온 경우
        string typeName = ex.GetType().Name;
        bool engine = FromEngine(ex);
        if (engine && (typeName.Contains("Crypto", StringComparison.OrdinalIgnoreCase) || typeName.Contains("Decrypt", StringComparison.OrdinalIgnoreCase)))
            return (ProblemKind.WrongPassword, EnginePasswordMessage, null);
        // 엔진이 모르는 압축 방식(SharpCompress 'Unsupported compression method: 97')은 손상이 아니다 — 고치기를 권하지 않는다(W2-08 검증 지적)
        if (engine && UnsupportedMethodName(ex) is { } method)
            return (ProblemKind.UnsupportedMethod, $"이 압축 방식({method})은 아직 풀 수 없어요.", null);
        if (ex is EndOfStreamException || typeName == "IncompleteArchiveException" || engine && LooksTruncated(ex))
            return (ProblemKind.Truncated, CorruptArchiveException.TruncatedMessage, null);
        if (engine || ex is InvalidDataException)
            return (ProblemKind.Corrupt, EngineDataMessage, null);

        return (ProblemKind.Unknown, UnknownMessage, UnknownAdvice);
    }

    private static ProblemKind KindOf(ArchiveException a) => a switch
    {
        ListPasswordRequiredException => ProblemKind.ListPasswordRequired,
        PasswordRequiredException => ProblemKind.PasswordRequired,
        WrongPasswordException => ProblemKind.WrongPassword,
        ChecksumMismatchException => ProblemKind.ChecksumMismatch,
        VolumeIncompleteException => ProblemKind.VolumeIncomplete,
        CorruptArchiveException { IsTruncated: true } => ProblemKind.Truncated,
        CorruptArchiveException => ProblemKind.Corrupt,
        VolumeMissingException => ProblemKind.VolumeMissing,
        UnsupportedFormatException => ProblemKind.UnsupportedFormat,
        UnsupportedMethodException => ProblemKind.UnsupportedMethod,
        EngineUnavailableException => ProblemKind.EngineUnavailable,
        SourceUnreadableException => ProblemKind.SourceUnreadable,
        NothingToCompressException => ProblemKind.NothingToCompress,
        ArchiveNotEditableException => ProblemKind.NotEditable,
        _ => ProblemKind.Rejected,
    };

    /// <summary>입출력 환경 문제의 종류(형식·코드로). 아니면 null.</summary>
    private static ProblemKind? EnvironmentKind(Exception ex) => ex switch
    {
        PathTooLongException => ProblemKind.PathTooLong,
        UnauthorizedAccessException => ProblemKind.AccessDenied,
        FileNotFoundException or DirectoryNotFoundException or DriveNotFoundException => ProblemKind.NotFound,
        IOException io => io.HResult switch
        {
            ErrorDiskFull or ErrorHandleDiskFull => ProblemKind.DiskFull,
            ErrorFilenameTooLong => ProblemKind.PathTooLong,
            // 이름 한 조각이 255자를 넘으면 운영 체제는 '이름 구문이 잘못됨'(123)으로 알린다(긴 경로를 켠 PC — W2-08 검증 지적 2차)
            ErrorInvalidName when HasOverlongSegment(io.Message) => ProblemKind.PathTooLong,
            ErrorAccessDenied => ProblemKind.AccessDenied,
            ErrorSharingViolation or ErrorLockViolation => ProblemKind.InUse,
            ErrorFileExists or ErrorAlreadyExists => ProblemKind.AlreadyExists,
            ErrorFileNotFound or ErrorPathNotFound => ProblemKind.NotFound,
            _ => null,
        },
        _ => null,
    };

    /// <summary>문구 속 경로(따옴표 안)에 255자를 넘는 이름 조각이 있는가.</summary>
    private static bool HasOverlongSegment(string? message)
    {
        string? path = message is null ? null : ErrorMessages.QuotedPath(message);
        return path is not null && path.Split('\\', '/').Any(seg => seg.Length > 255);
    }

    /// <summary>안쪽 예외 중 사용자 환경 문제·운영 체제 입출력 오류·취소(처음 것). 없으면 null.</summary>
    private static Exception? InnerCause(Exception ex)
    {
        int depth = 0;
        for (var e = ex.InnerException; e is not null && depth < 8; e = e.InnerException, depth++)
            if (e is OperationCanceledException || EnvironmentKind(e) is not null || IsOsIoError(e)) return e;
        return null;
    }

    /// <summary>
    /// 운영 체제가 알린 그 밖의 입출력 오류의 쉬운 문구(오류 번호로 묶어서 — 원문·번호는 '자세히'에). 묶음에 없는 번호는 예전 계약대로
    /// '알 수 없는 문제가 생겼어요.'(+ 로그 안내 — 한국어 Windows 의 운영 체제 문구도 그대로 보이지 않게).
    /// </summary>
    private static (ProblemKind Kind, string Message, string? Advice) OsIoProblem(Exception ex)
    {
        int code = ex.HResult & 0xFFFF;
        string suffix = PathSuffix(ex.Message);
        if (NetworkCodes.Contains(code)) return (ProblemKind.Other, NetworkMessage + suffix, NetworkAdvice);
        if (DeviceCodes.Contains(code)) return (ProblemKind.Other, DeviceMessage + suffix, DeviceAdvice);
        if (BadNameCodes.Contains(code)) return (ProblemKind.Other, BadNameMessage + suffix, null);
        return (ProblemKind.Unknown, UnknownMessage, UnknownAdvice);
    }

    private static string TitleOf(ProblemKind kind) => kind switch
    {
        ProblemKind.PasswordRequired or ProblemKind.ListPasswordRequired => "암호가 필요해요",
        ProblemKind.WrongPassword => "암호가 맞지 않아요",
        ProblemKind.Corrupt => "압축 파일이 손상됐어요",
        ProblemKind.Truncated => "압축 파일이 잘려 있어요",
        ProblemKind.ChecksumMismatch => "내용이 원래와 달라요",
        ProblemKind.VolumeMissing => "나눠진 조각이 없어요",
        ProblemKind.VolumeIncomplete => "조각이 덜 받아졌어요",
        ProblemKind.UnsupportedFormat => "열 수 없는 형식이에요",
        ProblemKind.UnsupportedMethod => "풀 수 없는 압축 방식이에요",
        ProblemKind.EngineUnavailable => "부품을 불러오지 못했어요",
        ProblemKind.SourceUnreadable => "원본 파일을 읽을 수 없어요",
        ProblemKind.NothingToCompress => "압축할 파일이 없어요",
        ProblemKind.NotEditable => "고칠 수 없는 형식이에요",
        ProblemKind.DiskFull => "공간이 부족해요",
        ProblemKind.PathTooLong => "경로가 너무 길어요",
        ProblemKind.AccessDenied => "권한이 없어요",
        ProblemKind.InUse => "다른 프로그램이 쓰고 있어요",
        ProblemKind.Cancelled => "취소했어요",
        ProblemKind.UnsafePath => "위험한 경로가 있어요",
        ProblemKind.AlreadyExists => "같은 이름이 있어요",
        ProblemKind.NotFound => "찾을 수 없어요",
        ProblemKind.Rejected => "할 수 없어요",
        ProblemKind.Other => "문제가 생겼어요",
        _ => "알 수 없는 문제가 생겼어요",
    };

    /// <summary>
    /// '자세히' 원문: 형식·문구·코드(16진수)·안쪽 예외(차례로)·호출 위치 위 다섯 줄. 로그(<see cref="Logger"/>)에는 예외 전체가 따로 남는다.
    /// </summary>
    internal static string DetailsOf(Exception ex)
    {
        var sb = new StringBuilder();
        sb.Append(ex.GetType().FullName).Append(": ").AppendLine(ex.Message);
        sb.Append("HResult: 0x").AppendLine(unchecked((uint)ex.HResult).ToString("X8", System.Globalization.CultureInfo.InvariantCulture));
        int depth = 0;
        for (var inner = ex.InnerException; inner is not null && depth < 8; inner = inner.InnerException, depth++)
        {
            sb.Append("  <- ").Append(inner.GetType().FullName).Append(": ").Append(inner.Message)
              .Append(" (0x").Append(unchecked((uint)inner.HResult).ToString("X8", System.Globalization.CultureInfo.InvariantCulture)).AppendLine(")");
        }
        var stack = (ex.StackTrace ?? string.Empty).Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Take(5).ToList();
        foreach (var line in stack) sb.Append("  ").AppendLine(line);
        return sb.ToString().TrimEnd();
    }

    /// <summary>'ZIP 고치기 시도'를 권할 이름인가('.zip' 과 나눠진 ZIP 의 조각 '.zip.001'·'.z01' — <see cref="ZipRepairer.IsRepairableName"/>).</summary>
    private static bool IsZipName(string? archiveName) => ZipRepairer.IsRepairableName(archiveName);

    /// <summary>엔진(SharpCompress·SharpZipLib·7z.dll·EggDotNet)이 낸 예외인가 — 형식의 이름공간, 아니면 던진 곳의 이름공간으로.</summary>
    private static bool FromEngine(Exception ex)
    {
        for (var t = ex.GetType(); t is not null && t != typeof(Exception); t = t.BaseType)
            if (IsEngineNamespace(t.Namespace)) return true;
        try
        {
            return IsEngineNamespace(ex.TargetSite?.DeclaringType?.Namespace);
        }
        catch
        {
            return false;
        }
    }

    private static bool IsEngineNamespace(string? ns) =>
        ns is not null && (ns.StartsWith("SharpCompress", StringComparison.Ordinal) || ns.StartsWith("ICSharpCode.SharpZipLib", StringComparison.Ordinal)
                           || ns.StartsWith("SharpSevenZip", StringComparison.Ordinal) || ns.StartsWith("EggDotNet", StringComparison.Ordinal));

    private static readonly System.Text.RegularExpressions.Regex UnsupportedMethodPattern =
        new(@"Unsupported compression method:?\s*(?<m>[A-Za-z0-9_]+)", System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.CultureInvariant);

    /// <summary>ZIP 압축 방식 번호 → 이름(APPNOTE 4.4.5 — 화면에 보일 이름).</summary>
    private static readonly Dictionary<int, string> ZipMethodNames = new()
    {
        [1] = "Shrink", [2] = "Reduce", [3] = "Reduce", [4] = "Reduce", [5] = "Reduce", [6] = "Implode", [9] = "Deflate64", [10] = "DCL Implode",
        [12] = "BZip2", [14] = "LZMA", [16] = "CMPSC", [18] = "TERSE", [19] = "LZ77", [20] = "Zstandard", [93] = "Zstandard", [94] = "MP3", [95] = "XZ",
        [96] = "JPEG", [97] = "WavPack", [98] = "PPMd",
    };

    /// <summary>
    /// 풀 수 없는 압축 방식의 이름 — 일집 예외(<see cref="UnsupportedMethodException"/>)면 그 방식, 엔진이 낸 '지원하지 않는 방식'(NotSupportedException
    /// 'Unsupported compression method: 97')이면 방식 이름(모르는 번호는 '방식 97'). 아니면 null.
    /// </summary>
    internal static string? UnsupportedMethodName(Exception ex)
    {
        if (ex is UnsupportedMethodException u) return string.IsNullOrEmpty(u.Method) ? null : u.Method;
        if (ex is not NotSupportedException) return null;
        var m = UnsupportedMethodPattern.Match(ex.Message ?? string.Empty);
        if (!m.Success) return null;
        string token = m.Groups["m"].Value;
        return int.TryParse(token, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out int n)
            ? ZipMethodNames.TryGetValue(n, out var name) ? name : $"방식 {n}"
            : token;
    }

    private static bool LooksTruncated(Exception ex)
    {
        for (Exception? e = ex; e is not null; e = e.InnerException)
        {
            string m = e.Message ?? string.Empty;
            if (e is EndOfStreamException || m.Contains("Unexpected end", StringComparison.OrdinalIgnoreCase)
                || m.Contains("end of stream", StringComparison.OrdinalIgnoreCase) || m.Contains("EOF", StringComparison.Ordinal))
                return true;
        }
        return false;
    }

    /// <summary>문구 끝에 붙일 ": 경로"(따옴표 안 경로가 있을 때) 또는 ".".</summary>
    private static string PathSuffix(string message) => ErrorMessages.QuotedPath(message) is { } p ? $": {p}" : ".";

    /// <summary>문구 뒤에 덧붙일 " (파일: 경로)"(따옴표 안 경로가 있을 때) 또는 빈 문자열 — 문구가 이미 마침표로 끝날 때.</summary>
    private static string PathNote(string message) => ErrorMessages.QuotedPath(message) is { } p ? $" (파일: {p})" : string.Empty;

    /// <summary>
    /// 서비스가 쓴 한국어 문구인가(영어 예외 문구가 아닌가 — IJ3-02 (g)). 경로(작은따옴표·큰따옴표로 싼 부분, 따옴표 없이 'C:\'·'\\' 로 시작하는 경로는 그 뒤 전부)·
    /// 이름(‘ ’로 싼 부분)을 빼고, 남은 낱말 중 파일 이름처럼 보이는 것('견적서's'·'요약.xlsx')도 빼고 한글 낱말이 있어야 한다. 영어 낱말이 넷 넘게 이어지거나
    /// (‘Could not find file …’) 영어 문장의 낱말(is·not·the·Unknown·corrupt …)이 있거나 영어 낱말이 한글 낱말보다 많으면 영어 문구다.
    /// (예전: 경로 안 따옴표에 속아 경로의 한글을 문구로 셌고 — 폴더 '26년 견적, 5차 검증: 따옴표 없이 한글 경로·이름이 든 짧은 영어 엔진 문구
    /// 'Entry 견적서's 요약.xlsx is corrupt'·'Unknown header D:\업무\…' 를 한국어로 보아 그대로 냄)
    /// </summary>
    internal static bool LooksKorean(string? message)
    {
        string rest = WithoutQuoted(message);
        if (!ContainsHangul(rest) || EnglishRun.IsMatch(rest)) return false;
        int korean = 0, english = 0;
        foreach (string raw in rest.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
        {
            string t = raw.Trim(TokenPunctuation);
            if (t.Length == 0 || NameLike(t)) continue;
            if (EnglishSentenceWords.Contains(t)) return false;
            if (ContainsHangul(t)) korean++;
            else if (t.Length >= 2 && t.All(char.IsAsciiLetter)) english++;
        }
        return korean > 0 && !(english >= 2 && english > korean);
    }

    /// <summary>영어 낱말 넷 이상이 이어진 곳(영어 문장).</summary>
    private static readonly System.Text.RegularExpressions.Regex EnglishRun =
        new(@"[A-Za-z]{2,}(?:[\s,;:]+[A-Za-z]{2,}){3,}", System.Text.RegularExpressions.RegexOptions.CultureInvariant);

    /// <summary>낱말 앞뒤에서 떼어 낼 문장 부호.</summary>
    private static readonly char[] TokenPunctuation = { '.', ',', ';', ':', '!', '?', '(', ')', '[', ']', '{', '}', '"', '\'', '-', '—', '·' };

    /// <summary>
    /// 영어 오류 문장에만 나오는 낱말(일집의 한국어 문구에는 없다 — 나오면 따옴표 없이 붙은 이름 속 낱말이 아니라 영어 문장이다).
    /// </summary>
    private static readonly HashSet<string> EnglishSentenceWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "is", "are", "was", "were", "been", "not", "does", "did", "the", "cannot", "could", "failed", "invalid", "unknown", "unexpected",
        "corrupt", "corrupted", "exist", "exists", "denied", "missing", "unsupported", "expected",
    };

    /// <summary>파일 이름·경로처럼 보이는 낱말('\'·'/' 가 듦, 안쪽에 작은따옴표 — 'Kim's', 끝이 '.확장자').</summary>
    private static bool NameLike(string t) =>
        t.Contains('\\') || t.Contains('/') || t.Contains('\'') || FileExtension.IsMatch(t);

    private static readonly System.Text.RegularExpressions.Regex FileExtension =
        new(@"\.[A-Za-z][A-Za-z0-9]{0,5}$", System.Text.RegularExpressions.RegexOptions.CultureInvariant);

    /// <summary>따옴표 없이 쓴 경로의 시작('C:\'·'\\서버').</summary>
    private static readonly System.Text.RegularExpressions.Regex BarePathStart =
        new(@"(?:(?<![A-Za-z])[A-Za-z]:[\\/]|\\\\)", System.Text.RegularExpressions.RegexOptions.CultureInvariant);

    /// <summary>
    /// 경로·이름을 뺀 나머지: 첫 작은따옴표부터 마지막 작은따옴표까지(.NET 문구의 경로 — 경로 안에 따옴표가 있어도 통째로), 큰따옴표로 싼 부분(엔진 문구의 경로),
    /// ‘…’(일집 문구의 이름), 따옴표 없이 쓴 경로('C:\'·'\\' 부터 끝까지 — 경로에는 빈칸이 들 수 있다)를 뺀다.
    /// </summary>
    private static string WithoutQuoted(string? s)
    {
        if (string.IsNullOrEmpty(s)) return string.Empty;
        int a = s.IndexOf('\''), b = s.LastIndexOf('\'');
        string rest = a >= 0 && b > a ? s[..a] + " " + s[(b + 1)..] : s;
        var sb = new StringBuilder(rest.Length);
        bool inName = false, inDouble = false;
        foreach (char c in rest)
        {
            if (c == '‘') { inName = true; continue; }
            if (c == '’') { inName = false; continue; }
            if (c == '"') { inDouble = !inDouble; sb.Append(' '); continue; }
            if (!inName && !inDouble) sb.Append(c);
        }
        string text = sb.ToString();
        var m = BarePathStart.Match(text);
        return m.Success ? text[..m.Index] : text;
    }

    internal static bool ContainsHangul(string? s)
    {
        if (string.IsNullOrEmpty(s)) return false;
        foreach (char c in s)
            if (c is >= '가' and <= '힣') return true;
        return false;
    }
}
