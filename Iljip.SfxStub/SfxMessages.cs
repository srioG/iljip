using System;
using System.IO;

namespace Iljip.SfxStub;

/// <summary>실패의 종류(종료 코드·화면이 할 일을 가름).</summary>
internal enum SfxErrorKind
{
    /// <summary>명령줄 인자가 틀림(종료 코드 2).</summary>
    Arguments,
    /// <summary>파일이 손상됐거나 끝이 잘림.</summary>
    Damaged,
    /// <summary>암호가 필요함(안 넣음).</summary>
    PasswordRequired,
    /// <summary>암호가 틀림.</summary>
    WrongPassword,
    /// <summary>안전하지 않은 경로(풀 폴더 밖을 가리키는 이름 등) — 아무것도 풀지 않음.</summary>
    UnsafePath,
    /// <summary>풀 수 없는 압축 방식.</summary>
    Unsupported,
    /// <summary>쓰기 실패(공간·권한·잠김·경로) — 다른 곳을 고르면 될 수 있음.</summary>
    Io,
    /// <summary>사용자가 취소함.</summary>
    Cancelled,
}

/// <summary>스텁이 알리는 실패 — 문구는 늘 쉬운 한국어(무인 모드는 표준 오류에 한 줄, 화면 모드는 창에).</summary>
internal sealed class SfxException : Exception
{
    public SfxErrorKind Kind { get; }

    public SfxException(SfxErrorKind kind, string message, Exception? inner = null)
        : base(message, inner)
    {
        Kind = kind;
    }
}

/// <summary>화면·표준 오류에 보이는 문구(한 곳에서만 만든다).</summary>
internal static class SfxMessages
{
    public const string Title = "일집 자동 풀림";

    public const string Damaged =
        "이 자동 풀림 파일이 손상됐거나 끝부분이 잘렸어요. 내려받기나 복사가 끝까지 됐는지 확인하고 다시 받아 주세요.";

    public const string PasswordRequiredSilent = "암호가 걸린 자동 풀림 파일이에요. /p:암호 로 암호를 넣어 주세요.";

    public const string PasswordRequired = "암호가 걸린 파일이에요. 암호를 넣어 주세요.";

    public const string WrongPassword = "암호가 맞지 않아요. 다시 넣어 주세요.";

    public const string WrongPasswordOrDamaged = "암호가 틀렸거나 파일이 손상됐어요. 암호를 다시 확인해 주세요.";

    public const string Cancelled = "취소했어요. 풀던 파일은 남기지 않았어요.";

    public const string FolderMissing = "풀 위치를 넣어 주세요.";

    public const string FolderInvalid = "풀 위치가 올바르지 않아요. [찾아보기]로 골라 주세요.";

    public const string SilentNeedsFolder = "무인 모드(/s)에는 풀 위치 /d:<폴더> 가 필요해요.";

    public static string Checksum(string name) =>
        $"‘{name}’의 내용이 원래와 달라요(검사값 불일치). 파일이 손상됐을 수 있어요. 다시 받아 주세요.";

    public static string UnsafePath(string name) =>
        $"안전하지 않은 경로(‘{name}’)가 들어 있어서 풀지 않았어요.";

    public static string DuplicateName(string name) =>
        $"같은 이름(‘{name}’)이 두 번 들어 있어서 풀지 않았어요. 파일이 손상됐을 수 있어요.";

    public static string Unsupported(string what) => $"이 압축 방식({what})은 풀 수 없어요.";

    public static string UnknownArgument(string arg) => $"알 수 없는 인자예요: {arg}";

    public static string FolderBlockedByFile(string name) => $"‘{name}’ 이름의 파일이 있어서 폴더를 만들 수 없어요.";

    public static string FileBlockedByFolder(string name) => $"‘{name}’ 이름의 폴더가 있어서 파일을 풀 수 없어요.";

    /// <summary>같은 풀 곳의 다른 자동 풀림이 너무 오래 끝나지 않음(그쪽 창이 질문에 머물러 있는 등).</summary>
    public const string DestinationBusy = "같은 풀 위치에 다른 자동 풀림이 아직 풀고 있어요. 그쪽이 끝난 뒤 다시 해 주세요.";

    /// <summary>화면 모드 진행 줄: 같은 풀 곳의 다른 자동 풀림을 기다리는 중.</summary>
    public const string WaitingForOther = "같은 곳에 다른 풀기가 진행 중이에요. 끝나기를 기다리는 중...";

    /// <summary>지난번에 강제로 끊긴 풀기의 흔적을 정리하며 덮어쓰기 전 원래 파일을 보이는 폴더로 옮겼음.</summary>
    public static string RescuedFromEarlier(int count, string folderName) =>
        $"지난번에 끝까지 못 푼 흔적을 정리했어요. 덮어쓰기 전 원래 파일 {count}개는 ‘{folderName}’ 폴더에 옮겨 두었어요.";

    /// <summary>되돌리기에서 원래 파일을 제자리로 못 돌려놨음 — 어디에 있는지(보이는 폴더·숨은 임시 폴더).</summary>
    public static string? NotRestored(int moved, string? folderName, int left, string stagingName)
    {
        string? a = moved > 0 && folderName is not null
            ? $"덮어쓰려던 원래 파일 {moved}개를 제자리로 되돌리지 못해 ‘{folderName}’ 폴더에 옮겨 두었어요."
            : null;
        string? b = left > 0
            ? $"원래 파일 {left}개는 숨은 폴더 ‘{stagingName}\\{Iljip.Services.SfxStaging.BackupFolderName}’ 안에 있어요(지우지 마세요 — 다음에 같은 곳에 풀면 보이는 폴더로 옮겨요)."
            : null;
        return a is null ? b : b is null ? a : a + " " + b;
    }

    /// <summary>쓰기 실패를 쉬운 말로(HResult 로 가름 — 일집 본체 ErrorMessages 와 같은 분류).</summary>
    public static string FromIo(Exception ex)
    {
        int hr = ex.HResult;
        if (hr == unchecked((int)0x80070070) || hr == unchecked((int)0x80070027))
            return "저장할 공간이 부족해요. 공간을 비우거나 다른 곳을 골라 주세요.";
        if (ex is UnauthorizedAccessException || hr == unchecked((int)0x80070005))
            return "이 위치에 쓸 권한이 없어요. 다른 폴더(예: 바탕 화면)를 골라 주세요.";
        if (hr == unchecked((int)0x80070020) || hr == unchecked((int)0x80070021))
            return "다른 프로그램(보안 프로그램 포함)이 파일을 쓰고 있어요. 잠시 뒤 다시 해 주세요.";
        if (ex is PathTooLongException || hr == unchecked((int)0x800700CE) || hr == unchecked((int)0x8007006F))
            return "경로가 너무 길어요. 더 짧은 위치를 골라 주세요.";
        if (ex is DirectoryNotFoundException || ex is DriveNotFoundException || hr == unchecked((int)0x80070015))
            return "풀 위치를 찾을 수 없어요. 다른 폴더를 골라 주세요.";
        // 다른 프로그램이 같은 곳을 동시에 건드림(W2-10 검증 지적 1차 — 예전엔 '알 수 없는 문제(IOException …)' 그대로)
        if (hr == unchecked((int)0x800700B7) || hr == unchecked((int)0x80070050))
            return "같은 이름 파일이 방금 생겼어요(다른 프로그램이 같은 곳에 쓰는 중일 수 있어요). 잠시 뒤 다시 해 주세요.";
        if (ex is FileNotFoundException)
            return "풀던 중에 파일이 사라졌어요(다른 프로그램이 같은 곳을 건드렸을 수 있어요). 잠시 뒤 다시 해 주세요.";
        return $"풀다가 알 수 없는 문제가 생겼어요({ex.GetType().Name}: {OneLine(ex.Message)}).";
    }

    /// <summary>예외 하나를 사용자 문구로(일집 실패는 그 문구, 그 밖은 쓰기 실패 분류).</summary>
    public static string For(Exception ex) => ex switch
    {
        SfxException s => s.Message,
        OperationCanceledException => Cancelled,
        _ => FromIo(ex),
    };

    /// <summary>예외의 종류(무인 모드 종료 코드·화면 동작).</summary>
    public static SfxErrorKind KindOf(Exception ex) => ex switch
    {
        SfxException s => s.Kind,
        OperationCanceledException => SfxErrorKind.Cancelled,
        _ => SfxErrorKind.Io,
    };

    public static string OneLine(string? s) => (s ?? string.Empty).Replace("\r\n", " ").Replace('\n', ' ').Replace('\r', ' ').Trim();

    /// <summary>명령줄 도움말(화면 모드에서 /? 를 주면 보임).</summary>
    public const string Usage =
        "일집 자동 풀림 파일이에요. 두 번 누르면 풀 위치를 묻고 풀어요.\n\n" +
        "명령줄(자동화용 — 창을 띄우지 않음):\n" +
        "  /s            무인 모드(창·메시지 없이 풂)\n" +
        "  /d:<폴더>     풀 위치(무인 모드에서는 꼭 필요 — 없으면 만듦)\n" +
        "  /p:<암호>     암호(암호가 걸린 파일일 때)\n\n" +
        "무인 모드는 같은 이름 파일을 덮어써요. 종료 코드: 0 성공, 1 실패, 2 인자 오류. 오류는 표준 오류에 UTF-8 로 한 줄.";
}
