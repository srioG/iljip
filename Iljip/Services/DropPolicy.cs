namespace Iljip.Services;

/// <summary>지금 창이 무엇을 보여 주는 중인가(끌어 놓기 판단용).</summary>
public enum ViewState
{
    /// <summary>빈 창.</summary>
    Empty,
    /// <summary>압축할 목록을 모으는 중.</summary>
    Staging,
    /// <summary>압축 파일을 열어 보는 중.</summary>
    Archive,
}

/// <summary>끌어 놓은 것으로 할 수 있는 일.</summary>
public enum DropChoice
{
    /// <summary>그 압축 파일을 연다(보던 것·모으던 목록은 비워진다).</summary>
    OpenArchive,
    /// <summary>지금 모으는 압축할 목록에 더한다.</summary>
    AddToStaging,
    /// <summary>보던 압축을 닫고 새 압축할 목록을 만든다.</summary>
    NewStaging,
    /// <summary>아무것도 안 한다.</summary>
    Cancel,
}

public enum DropDecisionKind
{
    /// <summary>묻지 않고 연다.</summary>
    OpenDirect,
    /// <summary>묻지 않고 압축할 목록에 넣는다.</summary>
    StageDirect,
    /// <summary>선택 창으로 묻는다(<see cref="DropDecision.Options"/> 차례대로 버튼).</summary>
    Ask,
}

/// <summary>끌어 놓기 판단 결과. <see cref="Kind"/> 가 Ask 일 때만 <see cref="Message"/>·<see cref="Options"/> 가 찬다.</summary>
public sealed record DropDecision(DropDecisionKind Kind, string? Message, IReadOnlyList<DropChoice> Options)
{
    public static readonly DropDecision OpenDirect = new(DropDecisionKind.OpenDirect, null, Array.Empty<DropChoice>());
    public static readonly DropDecision StageDirect = new(DropDecisionKind.StageDirect, null, Array.Empty<DropChoice>());

    /// <summary>
    /// 선택 창의 기본 버튼(Enter·처음 포커스) 순번 — 늘 [취소](W1-D 1차 검증). 첫 버튼이 기본이면 끌어 놓은 뒤 Enter 한 번에
    /// 보던 압축이 닫히거나 모으던 목록이 비워졌다. 버튼이 없으면 -1.
    /// </summary>
    public int DefaultIndex
    {
        get
        {
            for (int i = 0; i < Options.Count; i++)
                if (Options[i] == DropChoice.Cancel) return i;
            return Options.Count > 0 ? 0 : -1;
        }
    }
}

/// <summary>
/// 창에 끌어 놓은 파일을 어떻게 할지(I0-4 — 예전엔 보던 압축이 말없이 닫히고 새 압축 목록이 됐다). 순수 함수.
/// <list type="bullet">
/// <item>빈 창 + 압축 파일 1개 → 바로 연다. 빈 창 + 그 밖 → 바로 압축할 목록에.</item>
/// <item>압축할 목록을 모으는 중 + 일반 파일 → 바로 더한다(예전과 같음). + 압축 파일 1개 → [이 압축 파일 열기] [압축할 목록에 넣기] [취소].</item>
/// <item>압축을 보는 중 + 무엇이든 → [새로 압축할 목록 만들기] [취소]. 압축 파일 1개면 맨 앞에 [이 압축 파일 열기].</item>
/// </list>
/// '열린 압축에 추가'는 압축 고치기(I2-3)가 없어 넣지 않는다.
/// 압축 파일인지는 <paramref name="isArchiveFile"/>(화면은 '있는 파일 + 확장자'로 — 내용은 보지 않는다. 속이 ZIP 인 .docx 는 압축할 파일)로 판단한다.
/// </summary>
public static class DropPolicy
{
    public const string AskWhileStaging = "압축할 목록을 모으는 중이에요. 끌어 온 압축 파일을 어떻게 할까요?";
    public const string AskWhileArchive = "지금 압축 파일을 보고 있어요. 끌어 온 파일을 어떻게 할까요?";

    public static DropDecision Decide(ViewState state, IReadOnlyList<string> paths, Func<string, bool> isArchiveFile)
    {
        bool singleArchive = paths.Count == 1 && isArchiveFile(paths[0]);
        switch (state)
        {
            case ViewState.Staging:
                return singleArchive
                    ? new DropDecision(DropDecisionKind.Ask, AskWhileStaging,
                        new[] { DropChoice.OpenArchive, DropChoice.AddToStaging, DropChoice.Cancel })
                    : DropDecision.StageDirect;

            case ViewState.Archive:
                return new DropDecision(DropDecisionKind.Ask, AskWhileArchive,
                    singleArchive
                        ? new[] { DropChoice.OpenArchive, DropChoice.NewStaging, DropChoice.Cancel }
                        : new[] { DropChoice.NewStaging, DropChoice.Cancel });

            default:
                return singleArchive ? DropDecision.OpenDirect : DropDecision.StageDirect;
        }
    }

    /// <summary>선택 창 버튼 문구(지금 상태에 맞게 — '비워져요'·'닫혀요'를 미리 알린다).</summary>
    public static string Label(DropChoice choice, ViewState state) => choice switch
    {
        DropChoice.OpenArchive => state == ViewState.Staging
            ? "이 압축 파일 열기 (압축할 목록은 비워져요)"
            : "이 압축 파일 열기 (보던 압축은 닫혀요)",
        DropChoice.AddToStaging => "압축할 목록에 넣기",
        DropChoice.NewStaging => "새로 압축할 목록 만들기 (보던 압축은 닫혀요)",
        _ => "취소",
    };
}
