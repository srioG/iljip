using System.IO;

namespace Iljip.Services;

/// <summary>
/// 예외 → 화면에 보일 쉬운 한국어 문구, 창 없이 풀기(--extract-to)의 종료 코드.
/// 일집 자체 예외(<see cref="ArchiveException"/>)는 Message 가 이미 화면 문구다.
/// ★ 손상 문구에 '압축 파일이 아닌'을 넣지 말 것 — 재사용장치가 그 말로 '압축 아님'을 분류한다(재사용장치_processor.py 491행).
/// </summary>
public static class ErrorMessages
{
    private const int ErrorDiskFull = unchecked((int)0x80070070);         // ERROR_DISK_FULL
    private const int ErrorHandleDiskFull = unchecked((int)0x80070027);   // ERROR_HANDLE_DISK_FULL
    private const int ErrorSharingViolation = unchecked((int)0x80070020); // ERROR_SHARING_VIOLATION(다른 프로그램이 파일을 열어 둠)
    private const int ErrorLockViolation = unchecked((int)0x80070021);    // ERROR_LOCK_VIOLATION
    private const int ErrorFileExists = unchecked((int)0x80070050);       // ERROR_FILE_EXISTS
    private const int ErrorAlreadyExists = unchecked((int)0x800700B7);    // ERROR_ALREADY_EXISTS(같은 이름의 파일이 있어 폴더를 못 만듦 등)

    /// <summary>경로 공격 거부 문구의 머리(서비스들이 InvalidDataException 에 쓰는 기존 계약).</summary>
    public const string UnsafePathPrefix = "안전하지 않은 경로: ";

    public static string ToUserMessage(Exception ex) => Known(ex) ?? $"알 수 없는 오류가 났어요: {ex.Message}";

    /// <summary>
    /// 엔진 영어 문구를 한 글자도 보이지 않는 쉬운 한국어(W2-05 — 탐색기 우클릭 풀기·여러 개 풀기의 오류 창·요약 이유).
    /// 일집이 아는 오류는 <see cref="ToUserMessage"/> 와 같고, 모르는 오류(SharpCompress 'Failed to locate the Zip Header' 등)만
    /// <see cref="UnknownPlainMessage"/> 로 바꾼다 — 날 문구는 부르는 쪽이 기록(로그)·요약 '자세히'에 남긴다.
    /// </summary>
    public static string ToPlainMessage(Exception ex) => Known(ex) ?? UnknownPlainMessage;

    /// <summary>모르는 오류의 쉬운 문구(★ '압축 파일이 아닌' 금지 — 재사용장치 분류 말).</summary>
    public const string UnknownPlainMessage = "파일이 손상됐거나 끝까지 받아지지 않은 것 같아요. 원본을 다시 받아서 풀어 보세요.";

    /// <summary>일집이 아는 오류의 화면 문구. 모르면 null.</summary>
    private static string? Known(Exception ex) => ex switch
    {
        ArchiveException a => a.Message,
        OperationCanceledException => "취소했어요.",
        InvalidDataException d when d.Message.StartsWith(UnsafePathPrefix, StringComparison.Ordinal)
            => $"위험한 경로가 들어 있어 그 파일은 풀지 않았어요: {d.Message[UnsafePathPrefix.Length..]}",
        IOException io when io.HResult is ErrorDiskFull or ErrorHandleDiskFull => "디스크 공간이 모자라요.",
        PathTooLongException => "경로가 너무 길어요. 더 짧은 폴더에 풀어 보세요.",
        UnauthorizedAccessException u => "이 폴더에 쓸 권한이 없어요" + PathSuffix(u.Message),
        // 서비스가 이미 쉬운 한국어로 쓴 문구(TAR '도중에 바뀌었어요'·EGG '풀다가 멈췄어요' 등)는 그대로 보인다.
        // 따옴표 안(경로)의 한글은 세지 않는다 — 회사 PC 경로는 거의 한글이라, 한글 폴더가 든 영어 .NET 문구가 그대로 나가던 문제.
        _ when ContainsHangul(WithoutQuoted(ex.Message)) => ex.Message,
        IOException io when io.HResult is ErrorSharingViolation or ErrorLockViolation
            => "다른 프로그램이 파일을 쓰고 있어요. 그 프로그램을 닫고 다시 해 보세요" + PathSuffix(io.Message),
        IOException io when io.HResult is ErrorFileExists or ErrorAlreadyExists
            => "같은 이름의 파일이나 폴더가 이미 있어 만들 수 없어요" + PathSuffix(io.Message),
        FileNotFoundException or DirectoryNotFoundException => "파일이나 폴더를 찾을 수 없어요" + PathSuffix(ex.Message),
        _ => null,
    };

    /// <summary>창 없이 풀기의 종료 코드: 형식 미지원 2, 그 밖의 실패 1(성공 0 은 호출자).</summary>
    public static int HeadlessExitCode(Exception ex) => ex is UnsupportedFormatException ? 2 : 1;

    /// <summary>"Access to the path 'X' is denied." 같은 문구에서 따옴표 안 경로를 꺼낸다.</summary>
    internal static string? QuotedPath(string message)
    {
        int a = message.IndexOf('\'');
        int b = a < 0 ? -1 : message.IndexOf('\'', a + 1);
        return b > a + 1 ? message[(a + 1)..b] : null;
    }

    /// <summary>문구 끝에 붙일 ": 경로."(따옴표 안 경로가 있을 때) 또는 ".".</summary>
    private static string PathSuffix(string message) => QuotedPath(message) is { } p ? $": {p}" : ".";

    /// <summary>작은따옴표로 싼 부분(.NET 문구의 경로)을 뺀 나머지.</summary>
    private static string WithoutQuoted(string? s)
    {
        if (string.IsNullOrEmpty(s)) return string.Empty;
        var sb = new System.Text.StringBuilder(s.Length);
        bool inQuote = false;
        foreach (char c in s)
        {
            if (c == '\'') { inQuote = !inQuote; continue; }
            if (!inQuote) sb.Append(c);
        }
        return sb.ToString();
    }

    private static bool ContainsHangul(string? s)
    {
        if (string.IsNullOrEmpty(s)) return false;
        foreach (char c in s)
            if (c is >= '가' and <= '힣') return true;
        return false;
    }
}
