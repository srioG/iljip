using Iljip.Models;
using Iljip.Services;

namespace Iljip.Tests.Contract;

/// <summary>
/// ThrottledArchiveProgress(진행 창으로 가는 진행률 솎아내기)의 규칙:
/// 첫 중간 보고는 바로 통과, 그다음부터는 간격마다 하나, 완료 보고(CurrentFile 빈 문자열)는 언제나 통과.
/// (B7 — 예전엔 시작값 long.MinValue 뺄셈 오버플로로 중간 보고가 하나도 통과하지 않았다)
/// </summary>
public class ThrottledArchiveProgressTests
{
    private static ArchiveProgress Mid(long bytes) => new() { CurrentFile = "a.txt", BytesProcessed = bytes, TotalBytes = 100, TotalFiles = 1 };
    private static ArchiveProgress Final() => new() { CurrentFile = "", BytesProcessed = 100, TotalBytes = 100, FilesProcessed = 1, TotalFiles = 1 };

    /// <summary>컨텍스트 없음(그 자리에서 바로 호출) 상태로 만든다 — 테스트 스레드의 SynchronizationContext 영향 제거.</summary>
    private static (ThrottledArchiveProgress P, List<ArchiveProgress> Seen) Make(long intervalMs)
    {
        var prev = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(null);
        try
        {
            var seen = new List<ArchiveProgress>();
            return (new ThrottledArchiveProgress(seen.Add, intervalMs), seen);
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(prev);
        }
    }

    [Fact]
    public void 첫_중간보고는_바로_통과한다()
    {
        var (p, seen) = Make(intervalMs: 60_000);
        p.Report(Mid(7));
        Assert.Equal(7, Assert.Single(seen).BytesProcessed);
    }

    [Fact]
    public void 간격이_크면_두번째_중간보고부터_걸러진다()
    {
        var (p, seen) = Make(intervalMs: 60_000);
        for (int i = 1; i <= 20; i++) p.Report(Mid(i));
        Assert.Equal(1, Assert.Single(seen).BytesProcessed);
    }

    [Fact]
    public void 완료보고는_간격과_상관없이_언제나_통과한다()
    {
        var (p, seen) = Make(intervalMs: 60_000);
        p.Report(Final());                 // 중간 보고 없이 바로 완료
        p.Report(Mid(1));
        p.Report(Mid(2));                  // 걸러짐
        p.Report(Final());                 // 직전 보고 바로 뒤라도 통과
        Assert.Equal(new[] { "", "a.txt", "" }, seen.Select(s => s.CurrentFile));
        Assert.Equal(100.0, seen[^1].Percentage);
    }

    [Fact]
    public void 간격이_지나면_다음_중간보고가_통과한다()
    {
        var (p, seen) = Make(intervalMs: 300);
        p.Report(Mid(1));
        p.Report(Mid(2));                  // 걸러짐(간격 안)
        Thread.Sleep(450);
        p.Report(Mid(3));                  // 간격이 지나 통과
        Assert.Equal(new long[] { 1, 3 }, seen.Select(s => s.BytesProcessed));
    }
}
