namespace Iljip.Models;

/// <summary>여러 개 풀기에서 못 한 것 하나: 압축 파일 이름, 쉬운 한국어 이유, 자세히(날 오류 — '자세히' 아래에만 보임).</summary>
public sealed record OperationFailure(string Name, string Reason, string? Detail = null);

/// <summary>
/// 여러 개를 한꺼번에 한 결과(W2-05 — I2-6 여러 개 풀기). 화면은 <see cref="Headline"/>('5개 중 4개를 풀었어요.')·실패 목록·알림을 보여 준다.
/// <see cref="Failures"/> 의 <see cref="OperationFailure.Detail"/>(영어 엔진 문구·예외 형식)은 첫 화면이 아니라 '자세히' 아래에만.
/// </summary>
public sealed class OperationSummary
{
    /// <summary>창 제목(예: '압축 풀기 결과').</summary>
    public string Title { get; init; } = "압축 풀기 결과";

    /// <summary>모두 몇 개였는지(시작하지 못한 것 포함).</summary>
    public int Total { get; init; }

    /// <summary>끝까지 한 것.</summary>
    public int Succeeded { get; init; }

    /// <summary>못 한 것(암호를 넣지 않아 건너뜀 포함).</summary>
    public IReadOnlyList<OperationFailure> Failures { get; init; } = Array.Empty<OperationFailure>();

    /// <summary>중간에 취소해 손대지 않은 것.</summary>
    public IReadOnlyList<string> NotStarted { get; init; } = Array.Empty<string>();

    /// <summary>그 밖의 알림(압축 파일을 휴지통으로 옮기지 못함 등).</summary>
    public IReadOnlyList<string> Notes { get; init; } = Array.Empty<string>();

    /// <summary>중간에 취소했는가.</summary>
    public bool Cancelled { get; init; }

    /// <summary>한 줄 요약: '5개를 모두 풀었어요.' / '5개 중 4개를 풀었어요.' / 취소면 '…풀고 멈췄어요.'</summary>
    public string Headline =>
        Cancelled
            ? $"{Total}개 중 {Succeeded}개를 풀고 멈췄어요."
            : Succeeded == Total && Total > 0
                ? $"{Total}개를 모두 풀었어요."
                : $"{Total}개 중 {Succeeded}개를 풀었어요.";

    /// <summary>
    /// 첫 화면에 보일 글(자세히 빼고): 요약 줄, 실패마다 '이름 — 이유', 손대지 않은 것, 알림. 날 오류는 넣지 않는다.
    /// </summary>
    public string ToDisplayText()
    {
        var lines = new List<string> { Headline };
        if (Failures.Count > 0)
        {
            lines.Add(string.Empty);
            lines.Add("못 푼 파일:");
            foreach (var f in Failures)
                lines.Add($"- {f.Name} — {f.Reason}");
        }
        if (NotStarted.Count > 0)
        {
            lines.Add(string.Empty);
            lines.Add($"취소해서 손대지 않은 파일 {NotStarted.Count}개:");
            foreach (var n in NotStarted.Take(10))
                lines.Add($"- {n}");
            if (NotStarted.Count > 10) lines.Add($"외 {NotStarted.Count - 10}개");
        }
        if (Notes.Count > 0)
        {
            lines.Add(string.Empty);
            lines.AddRange(Notes);
        }
        return string.Join("\n", lines);
    }

    /// <summary>'자세히' 아래 글(실패마다 날 오류). 없으면 빈 문자열.</summary>
    public string DetailText() =>
        string.Join("\n\n", Failures.Where(f => !string.IsNullOrEmpty(f.Detail)).Select(f => $"[{f.Name}]\n{f.Detail}"));
}
