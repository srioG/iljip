namespace Iljip.Services;

/// <summary>지금 시각(설계 §5 — 시험에서 '24시간 지남' 같은 판단을 가짜 시각으로 시험하려고 뺐다). W2-04(임시 폴더 청소)·W2-07 이 쓴다.</summary>
public interface IClock
{
    DateTime UtcNow { get; }
}

/// <summary>진짜 시계.</summary>
public sealed class SystemClock : IClock
{
    public static readonly SystemClock Instance = new();

    public DateTime UtcNow => DateTime.UtcNow;
}
