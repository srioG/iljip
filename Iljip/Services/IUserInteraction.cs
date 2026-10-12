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

    /// <summary>압축 옵션 창. 취소하면 null.</summary>
    CompressionOptions? AskCompressionOptions(string targetExtension);

    /// <summary>진행 창을 띄운다(곧바로 돌아옴).</summary>
    IProgressSession ShowProgress(string title);
}
