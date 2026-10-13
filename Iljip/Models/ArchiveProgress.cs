namespace Iljip.Models;

/// <summary>
/// 압축/해제 진행률 보고용 모델.
/// </summary>
public sealed class ArchiveProgress
{
    public string CurrentFile { get; init; } = string.Empty;
    public long BytesProcessed { get; init; }
    public long TotalBytes { get; init; }
    public int FilesProcessed { get; init; }
    public int TotalFiles { get; init; }

    public double Percentage =>
        TotalBytes <= 0 ? 0 : (double)BytesProcessed / TotalBytes * 100.0;

    // ───── W2-05(I2-6 여러 개 풀기)에서 더한 것: 진행 창 하나에 '2/5 · b.zip' 과 전체 막대 ─────

    /// <summary>여러 개 풀기에서 지금 압축의 순번(1부터). 하나만 풀면 0.</summary>
    public int BatchIndex { get; init; }

    /// <summary>여러 개 풀기의 전체 압축 수(중간에 더 들어오면 늘어남). 하나만 풀면 0.</summary>
    public int BatchCount { get; init; }

    /// <summary>여러 개 풀기에서 지금 압축 파일 이름.</summary>
    public string BatchName { get; init; } = string.Empty;

    /// <summary>
    /// 단계 알림인가(W2-07 검증 지적 — 예: '만든 압축 파일을 시험하는 중'). 진행 창 앞의 솎아내기(<see cref="Iljip.Services.ThrottledArchiveProgress"/>)가
    /// 버리지 않고 늘 보낸다 — 한 번만 보내는 알림이 바로 앞 보고와 40ms 안에 붙어 와도 창에 닿게. 크기(<see cref="TotalBytes"/>)가 0 이면
    /// 진행 창은 막대를 '움직이는 막대'(얼마나 남았는지 모름)로 보인다.
    /// </summary>
    public bool IsStage { get; init; }

    /// <summary>전체 막대 값(0~100): 여러 개 풀기면 (앞서 끝난 압축 수 + 지금 압축의 진행) / 전체, 아니면 <see cref="Percentage"/>.</summary>
    public double OverallPercentage =>
        BatchCount > 1 && BatchIndex >= 1
            ? Math.Clamp(((BatchIndex - 1) + Percentage / 100.0) / BatchCount * 100.0, 0, 100)
            : Percentage;
}
