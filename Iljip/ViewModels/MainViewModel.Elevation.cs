using System.ComponentModel;
using Iljip.Services;

namespace Iljip.ViewModels;

// '관리자 권한으로 다시 시도'(W2-11, I3-16): Program Files 처럼 권한이 필요한 폴더에 풀거나 압축하다 권한 오류가 나면 오류 창 대신
// '…쓸 권한이 없어요. 관리자 권한으로 다시 해 볼까요?' [관리자 권한으로 다시 시도][닫기] 를 묻고, 고르면 같은 작업 인자로 일집을 runas 로 다시 띄운다
// (ElevationService — 암호는 넣지 않음, 네트워크 드라이브는 UNC 로). 시험은 가짜 IProcessLauncher 로 인자·분기만 본다(진짜 UAC 금지).
// W2-08(문제 창 ShowProblem·ErrorCatalog)을 다시 하면 이 질문은 '권한 없음' 분류의 Actions(ElevationService.RetryActionId)로 옮긴다.
// 묶음마다 partial 로 나눠 충돌을 줄인다(공통 설계 §0).
public partial class MainViewModel
{
    /// <summary>창 제목 끝(관리자 권한으로 도는 창).</summary>
    internal const string ElevatedTitleSuffix = " (관리자)";

    private bool _isElevatedInstance;

    /// <summary>
    /// '--elevated' 로 뜬 일집인가(App 이 정함). 그러면 다시 권한 상승을 권하지 않는다(무한 반복 방지 — UAC 가 꺼진 PC 처럼 runas 로도 권한이 그대로인 때도 멈추게).
    /// 제목의 '(관리자)'는 이 표시가 아니라 실제 권한(<see cref="IsRunningElevated"/>)을 따른다(W2-11 검증 지적 — 누가 '--elevated' 만 붙여 켜도 관리자라고 보이지 않게).
    /// </summary>
    public bool IsElevatedInstance
    {
        get => _isElevatedInstance;
        set
        {
            if (SetProperty(ref _isElevatedInstance, value))
                OnPropertyChanged(nameof(WindowTitle));
        }
    }

    /// <summary>이 일집이 실제로 관리자 권한으로 도는가(프로세스 토큰 — 모르면 false). 제목 끝 '(관리자)'.</summary>
    public bool IsRunningElevated
    {
        get
        {
            try
            {
                return _services.Processes.IsCurrentProcessElevated;
            }
            catch
            {
                return false;
            }
        }
    }

    /// <summary>
    /// 권한 상승을 권할 수 있는가: 실제로 관리자 권한으로 도는 일집도, '--elevated' 로 뜬 일집(이미 한 번 다시 시도함)도 아님.
    /// </summary>
    public bool CanOfferElevation
    {
        get
        {
            if (IsElevatedInstance) return false;
            try
            {
                return !_services.Processes.IsCurrentProcessElevated;
            }
            catch
            {
                return true;
            }
        }
    }

    /// <summary>
    /// 실패가 권한 오류면 오류 창 대신 '관리자 권한으로 다시 시도'를 묻는다(물었으면 true — 부르는 쪽은 오류 창을 띄우지 않음).
    /// <paramref name="retry"/> 가 있으면 그 명령을 그대로 다시(맨 앞 '--elevated'), 없고 <paramref name="reopenArchive"/> 가 있으면 그 압축을 관리자 일집에서 연다
    /// ('관리자 권한으로 일집 다시 열기' — 고치기·고른 항목만 풀기처럼 명령줄로 나타낼 수 없는 작업). 둘 다 없거나 권한 오류가 아니거나 권할 수 없으면 false.
    /// </summary>
    /// <param name="writeTarget">
    /// 쓰려던 곳(풀 곳·만들 곳·고칠 압축 파일·저장할 파일). 네트워크(공유 폴더·NAS·네트워크 드라이브)면 권하지 않는다 — 관리자 권한으로도 서버 쪽 권한은 같다
    /// (<see cref="ElevationService.CanHelpWriteTo"/>, W2-11 검증 지적).
    /// </param>
    internal bool OfferElevatedRetry(Exception error, string failureMessage, ParsedCommand? retry, string? reopenArchive, string? writeTarget)
    {
        if (ElevationService.ActionsFor(error, CanOfferElevation).Count == 0) return false;
        if (!ElevationService.CanHelpWriteTo(writeTarget)) return false;
        var command = retry ?? (reopenArchive is null ? null : ElevationService.ReopenCommand(reopenArchive));
        if (command is null) return false;
        bool reopen = retry is null;
        string question = reopen ? ElevationService.ReopenQuestion : ElevationService.RetryQuestion;
        string label = reopen ? ElevationService.ReopenLabel : ElevationService.RetryLabel;
        int pick = _ui.Choose($"{failureMessage}\n\n{question}", new[] { label, "닫기" }, defaultIndex: 0);
        if (pick == 0) StartElevated(command);
        return true;
    }

    /// <summary>
    /// 같은 명령을 관리자 권한으로 다시 띄운다. UAC 창에서 [아니요]면(Win32 오류 1223) 조용히 끝(오류 창 없음).
    /// 네트워크 드라이브를 UNC 로 못 바꾸면 그 까닭을 알린다. 띄웠으면 true.
    /// </summary>
    internal bool StartElevated(ParsedCommand command)
    {
        var plan = ElevationService.BuildRetry(command, _services.Processes.ExecutablePath, _services.MappedDrives);
        if (plan.Retry is not { } retry)
        {
            _ui.ShowError(plan.Problem ?? ElevationService.NoExecutableProblem);
            return false;
        }
        try
        {
            _services.Processes.StartElevated(retry.Exe, retry.Args);
            StatusText = "관리자 권한으로 다시 시작했어요";
            return true;
        }
        catch (Win32Exception w) when (w.NativeErrorCode == ProcessLauncher.ErrorCancelled)
        {
            StatusText = "관리자 권한으로 다시 하기를 취소했어요";   // UAC 창에서 [아니요] — 조용히
            return false;
        }
        catch (Exception ex)
        {
            Logger.LogError("관리자 권한으로 다시 시작하지 못함", ex);
            _ui.ShowError($"관리자 권한으로 다시 시작하지 못했어요.\n\n{ErrorMessages.ToUserMessage(ex)}");
            return false;
        }
    }
}
