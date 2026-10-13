using Iljip.Models;

namespace Iljip.Services;

/// <summary>암호 창을 띄우는 이유.</summary>
public enum PasswordPromptReason
{
    /// <summary>암호가 필요하다(아직 안 넣음).</summary>
    Required,
    /// <summary>넣은 암호가 맞지 않다.</summary>
    Wrong,
    /// <summary>파일 목록까지 암호로 잠겨 있어 열기 전에 암호가 필요하다(W2-02 — 7z '파일 이름도 암호화'·RAR 머리 암호, I1-7).</summary>
    ListLocked,
}

/// <summary>진행 창 하나(해제·압축 한 번). <see cref="Close"/> 는 여러 번 불러도 된다.</summary>
public interface IProgressSession
{
    IProgress<ArchiveProgress> Progress { get; }

    /// <summary>사용자가 [취소]를 누르면 취소된다.</summary>
    CancellationToken CancellationToken { get; }

    void Close();
}

/// <summary>
/// 화면(MainViewModel)이 사용자에게 묻고 알리는 모든 창(README 의 'IUserInteraction 주입' 제안 — W1-D 에서 들임).
/// 실제 앱은 WPF 창(<see cref="Iljip.Views.WpfUserInteraction"/>), 계약 테스트는 가짜(Iljip.Tests/TestSupport/FakeUserInteraction)를 넣어
/// '우클릭 풀기 → 진행 → 결과', '드롭 → 선택 창', '압축 → 건너뛴 파일 안내' 같은 흐름을 창 없이 시험한다.
/// (W1-B 보관본의 같은 이름 인터페이스와 모양을 맞췄다 — 다시 합칠 때 겹치게. <see cref="Choose"/> 는 W1-D 에서 더함)
/// </summary>
public interface IUserInteraction
{
    /// <summary>암호를 묻는다. 취소하면 null.</summary>
    string? AskPassword(string archiveName, PasswordPromptReason reason, string message);

    void ShowError(string message);

    void ShowWarning(string message);

    void ShowInfo(string message);

    /// <summary>
    /// 문구 하나와 버튼 여럿(<see cref="Iljip.Views.ChoiceDialog"/>). 고른 버튼 순번(0부터), 창을 닫으면 -1.
    /// <paramref name="defaultIndex"/> 는 기본 버튼(Enter·처음 포커스) — 되돌릴 수 없는 일을 하는 버튼이 아닌 것으로.
    /// </summary>
    int Choose(string message, IReadOnlyList<string> choices, int defaultIndex = 0);

    /// <summary>폴더 고르기. 취소하면 null.</summary>
    string? PickFolder(string title);

    /// <summary>파일 고르기. 취소하면 null.</summary>
    IReadOnlyList<string>? PickFiles(string title, string? filter, bool multiselect);

    /// <summary>저장할 경로 고르기. 취소하면 null.</summary>
    string? AskSavePath(string title, string filter, string defaultName, string initialDirectory, string defaultExtension);

    /// <summary>압축 옵션 창. 취소하면 null. (W2-07: 한 화면 압축 창 <see cref="AskCompression"/> 을 옵션 칸만 보이게 띄우는 어댑터 — 일집 화면은 더 쓰지 않음)</summary>
    CompressionOptions? AskCompressionOptions(string targetExtension);

    /// <summary>진행 창을 띄운다(곧바로 돌아옴).</summary>
    IProgressSession ShowProgress(string title);

    // ───── W2-05(같은 이름 묻기·여러 개 풀기 요약)에서 더한 것 — 이미 있는 멤버는 그대로 ─────

    /// <summary>
    /// 풀 때 같은 이름 파일이 이미 있을 때 묻는다(I1-5): ‘‘보고서.xlsx’ 파일이 이미 있어요.’ + 있는 파일·새 파일의 크기·날짜,
    /// [덮어쓰기][건너뛰기][이름 바꾸기][취소] + ‘남은 파일도 똑같이’. 창을 닫으면 [취소].
    /// ★ 해제 작업 스레드에서 불린다 — 진짜 창은 UI 스레드로 넘겨(Dispatcher.Invoke) 띄운다.
    /// </summary>
    CollisionAnswer AskCollision(CollisionQuestion question);

    /// <summary>여러 개를 한꺼번에 한 결과(I2-6): 요약 한 줄·못 한 것과 이유·알림. 날 오류는 '자세히' 아래에만.</summary>
    void ShowSummary(OperationSummary summary);

    // ───── W2-06(압축 고치기 — 새 폴더 이름)에서 더한 것 — 이미 있는 멤버는 그대로 ─────

    /// <summary>
    /// 글 한 줄을 묻는다(새 폴더 이름 등). <paramref name="initialText"/> 를 채워 두고 확장자 앞까지 골라 둔다. 취소하면 null.
    /// </summary>
    string? AskText(string title, string message, string initialText);

    // ───── W2-07(한 화면 압축 창 — I2-12)에서 더한 것 — 이미 있는 멤버는 그대로 ─────

    /// <summary>
    /// 한 화면 압축 창(이름·위치·형식·수준·암호·나누기·각각 따로·날짜·쓸모없는 파일 빼기·시험·원본 지우기). 취소하면 null.
    /// 예전 3단계(대기 목록 → 저장 창 → 옵션 창) 중 저장 창·옵션 창을 이 창 하나로 바꿨다. <see cref="AskCompressionOptions"/> 는 이 창을 옵션 칸만 보이게 띄우는 어댑터.
    /// 같은 이름의 압축 파일이 이미 있으면 창이 '바꿀까요?'를 묻고 답을 <see cref="CompressionRequest.OverwriteExisting"/> 에 담는다.
    /// </summary>
    CompressionRequest? AskCompression(CompressionDialogRequest request);

    // ───── W2-09(풀기 창 — I2-13)에서 더한 것 — 이미 있는 멤버는 그대로 ─────

    /// <summary>
    /// 풀기 창: 풀 곳(최근 푼 곳 목록 + [찾아보기])·'압축파일명으로 폴더 만들기'·'다 풀고 압축 파일을 휴지통으로'·'다 풀면 폴더 열기'. 취소하면 null.
    /// '다른 곳에 풀기'(툴바·Ctrl+E·선택 항목 풀기)·명령줄 '--extract'·열기 창에서 여러 개 고르기가 예전 폴더 고르기 창(<see cref="PickFolder"/>) 대신 쓴다.
    /// </summary>
    ExtractTarget? AskExtractTarget(ExtractTargetRequest request);
}
