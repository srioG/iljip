using System.IO;
using Iljip.Services;

namespace Iljip.ViewModels;

// W2-02(I1-7): 목록까지 암호로 잠긴 압축(7z '파일 이름도 암호화'·RAR 머리 암호)의 열기 암호 흐름.
// 묶음마다 partial 로 나눠 충돌을 줄인다(공통 설계 §0).
public partial class MainViewModel
{
    /// <summary>
    /// 열기 단계의 암호 창(<see cref="OpenArchiveAsync(string, string?)"/>). 처음엔 목록까지 잠긴 압축이면 '파일 목록까지 암호로 잠긴…'
    /// (<see cref="PasswordPromptReason.ListLocked"/>), 넣은 암호가 틀렸으면 '암호가 맞지 않아요' 경고를 보이고 다시 묻는다. 취소하면 null.
    /// </summary>
    /// <param name="triedPassword">방금 써 본 암호(처음이면 null).</param>
    private string? AskListPassword(string archivePath, Exception ex, string? triedPassword)
    {
        if (!string.IsNullOrEmpty(triedPassword))
            _ui.ShowWarning(WrongPasswordException.DefaultMessage);
        var (reason, message) = PasswordPrompt(ex, triedPassword);
        return _ui.AskPassword(Path.GetFileName(archivePath), reason, message);
    }

    /// <summary>
    /// 암호 창의 이유와 문구: 써 본 암호가 있으면 '틀림'(일집 예외면 그 문구), 없으면 목록까지 잠긴 압축은 <see cref="PasswordPromptReason.ListLocked"/>
    /// + '파일 목록까지 암호로 잠긴…', 그 밖은 '암호 필요'. 풀기(<see cref="ExtractToFolderAsync"/>)도 같은 규칙을 쓴다.
    /// </summary>
    private static (PasswordPromptReason Reason, string Message) PasswordPrompt(Exception ex, string? triedPassword)
    {
        if (!string.IsNullOrEmpty(triedPassword))
            return (PasswordPromptReason.Wrong, PasswordPromptMessage(ex, PasswordPromptReason.Wrong));
        if (ex is ListPasswordRequiredException)
            return (PasswordPromptReason.ListLocked, ListPasswordRequiredException.ListMessage);
        return (PasswordPromptReason.Required, PasswordPromptMessage(ex, PasswordPromptReason.Required));
    }
}
