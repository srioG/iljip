using Iljip.Models;
using Iljip.Services;
using Iljip.Tests.TestSupport;

namespace Iljip.Tests.Contract;

/// <summary>
/// [범위 8] 진행률(IProgress&lt;ArchiveProgress&gt;): 0~100% 를 넘지 않고, 바이트·파일 수가 뒤로 가지 않으며,
/// 마지막 보고는 '완료'(CurrentFile 빈 문자열, 처리량 = 총량)다. 진행 창이 99% 에 멈추지 않게 하는 계약.
/// </summary>
public class ProgressTests
{
    /// <param name="expectIntermediate">
    /// true 면 '도중 보고'(0 &lt; 처리량 &lt; 총량)가 하나 이상 있어야 한다 — 완료 보고 하나만 내는 엔진이면
    /// 진행 창이 처음 상태로 멈춰 있다가 끝에 닫힌다(B7 과 같은 증상). 여러 파일이거나 여러 청크(81,920B 초과)인 경우에만 켠다.
    /// </param>
    internal static void AssertWellFormed(IReadOnlyList<ArchiveProgress> reports, string context, bool expectTotalsKnown = true,
                                          bool expectIntermediate = false)
    {
        Assert.NotEmpty(reports);
        if (expectIntermediate)
            Assert.True(reports.Any(r => r.BytesProcessed > 0 && r.BytesProcessed < r.TotalBytes),
                $"{context}: 도중 진행률 보고(0 < 처리량 < 총량)가 하나도 없음 — 진행 창이 움직이지 않음");
        long lastBytes = -1;
        int lastFiles = -1;
        foreach (var r in reports)
        {
            Assert.InRange(r.Percentage, 0.0, 100.0);
            Assert.True(r.BytesProcessed >= lastBytes, $"{context}: 처리 바이트가 줄어듦 {lastBytes} → {r.BytesProcessed}");
            Assert.True(r.FilesProcessed >= lastFiles, $"{context}: 처리 파일 수가 줄어듦");
            Assert.True(r.FilesProcessed <= r.TotalFiles, $"{context}: 처리 파일 수가 총수보다 큼");
            if (expectTotalsKnown) Assert.True(r.BytesProcessed <= r.TotalBytes, $"{context}: 처리 바이트가 총량보다 큼");
            lastBytes = r.BytesProcessed;
            lastFiles = r.FilesProcessed;
        }
        var last = reports[^1];
        Assert.Equal(string.Empty, last.CurrentFile);
        Assert.Equal(last.TotalBytes, last.BytesProcessed);
        Assert.Equal(last.TotalFiles, last.FilesProcessed);
        Assert.Equal(1, reports.Count(r => r.CurrentFile.Length == 0));   // 완료 보고는 딱 한 번, 맨 끝
    }

    [Theory]
    [InlineData("zip_default", 81824, 9)]
    [InlineData("tar_default", 81824, 9)]
    [InlineData("py7z_pw_names_visible", 81824, 9)]
    [InlineData("zip_aes256", 81824, 9)]
    public async Task 해제_진행률이_0에서_100으로_끝난다(string id, long totalBytes, int totalFiles)
    {
        var fx = FixtureCatalog.Get(id);
        using var tmp = new TempDir("pg");
        var rec = new ProgressRecorder();
        await Svc.For(fx.FullPath).ExtractAsync(fx.FullPath, tmp.P("out"), fx.Password, rec);
        AssertWellFormed(rec.Reports, id, expectIntermediate: true);
        Assert.Equal(totalBytes, rec.Reports[^1].TotalBytes);
        Assert.Equal(totalFiles, rec.Reports[^1].TotalFiles);
        Assert.Equal(100.0, rec.Reports[^1].Percentage);
    }

    [Fact]
    public async Task 선택해제_진행률은_고른_것만_센다()
    {
        var fx = FixtureCatalog.Get("zip_default");
        using var tmp = new TempDir("pgs");
        var rec = new ProgressRecorder();
        await Svc.For(fx.FullPath).ExtractEntriesAsync(fx.FullPath, tmp.P("out"), new[] { "일집_샘플/하위폴더" }, null, rec);
        AssertWellFormed(rec.Reports, "선택", expectIntermediate: true);
        Assert.Equal(170 + 79, rec.Reports[^1].TotalBytes);
        Assert.Equal(2, rec.Reports[^1].TotalFiles);
    }

    [Theory]
    [InlineData(".zip")]
    [InlineData(".tar")]
    public async Task 압축_진행률이_0에서_100으로_끝난다(string ext)
    {
        using var tmp = new TempDir("pc");
        string src = SampleTree.Create(tmp.Dir("src"));
        long total = Directory.EnumerateFiles(src, "*", SearchOption.AllDirectories).Sum(f => new FileInfo(f).Length);
        int count = Directory.EnumerateFiles(src, "*", SearchOption.AllDirectories).Count();
        string arc = tmp.P("p" + ext);
        var rec = new ProgressRecorder();
        await Svc.For(arc).CompressAsync(new[] { src }, arc, null, rec);
        AssertWellFormed(rec.Reports, ext, expectIntermediate: true);
        Assert.Equal(total, rec.Reports[^1].TotalBytes);
        Assert.Equal(count, rec.Reports[^1].TotalFiles);
    }

    [Theory]
    [InlineData(".gz")]
    [InlineData(".bz2")]
    public async Task 단일파일_압축_진행률이_완료로_끝난다(string ext)
    {
        using var tmp = new TempDir("pc1");
        string one = tmp.P("one.bin");
        var data = new byte[300_000];
        new Random(3).NextBytes(data);
        File.WriteAllBytes(one, data);
        string arc = tmp.P("one.bin" + ext);
        var rec = new ProgressRecorder();
        await Svc.For(arc).CompressAsync(new[] { one }, arc, null, rec);
        AssertWellFormed(rec.Reports, ext, expectIntermediate: true);   // 300,000B = 여러 청크
        Assert.Equal(data.Length, rec.Reports[^1].TotalBytes);
        Assert.Equal(1, rec.Reports[^1].TotalFiles);
    }

    /// <summary>
    /// 순수 bz2 해제: 원본 크기를 미리 몰라 도중 보고의 총량은 0(=0%), 끝에서야 총량=처리량 으로 완료 보고(현재 동작 기록).
    /// </summary>
    [Fact, Trait(Kinds.Key, Kinds.Record)]
    public async Task bz2_해제_진행률은_총량을_모르다가_끝에_완료()
    {
        var fx = FixtureCatalog.Get("bz2_single_python");
        using var tmp = new TempDir("pb");
        var rec = new ProgressRecorder();
        await Svc.For(fx.FullPath).ExtractAsync(fx.FullPath, tmp.P("out"), null, rec);
        Assert.All(rec.Reports.Take(rec.Reports.Count - 1), r => Assert.Equal(0, r.TotalBytes));
        AssertWellFormed(rec.Reports, "bz2", expectTotalsKnown: false);
        Assert.Equal(2464, rec.Reports[^1].TotalBytes);
    }

    /// <summary>ThrottledArchiveProgress 를 '컨텍스트 없음'(그 자리에서 바로 호출) 상태로 돌려 본다.</summary>
    internal static List<ArchiveProgress> RunThrottled(long intervalMs, int intermediateCount)
    {
        var prev = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(null);
        try
        {
            var seen = new List<ArchiveProgress>();
            var throttled = new ThrottledArchiveProgress(seen.Add, intervalMs);
            for (int i = 1; i <= intermediateCount; i++)
                throttled.Report(new ArchiveProgress { CurrentFile = "a.txt", BytesProcessed = i, TotalBytes = 100, TotalFiles = 1 });
            throttled.Report(new ArchiveProgress { CurrentFile = "", BytesProcessed = 100, TotalBytes = 100, FilesProcessed = 1, TotalFiles = 1 });
            return seen;
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(prev);
        }
    }

    /// <summary>ThrottledArchiveProgress: 완료 보고(빈 CurrentFile)는 반드시 통과시킨다 — 진행 창이 99% 에서 멈추지 않게.</summary>
    [Fact]
    public void 솎아내기_진행률은_완료보고를_반드시_통과시킨다()
    {
        var seen = RunThrottled(intervalMs: 60_000, intermediateCount: 50);
        var last = seen[^1];
        Assert.Equal(string.Empty, last.CurrentFile);
        Assert.Equal(100.0, last.Percentage);
    }

    /// <summary>[B7] 중간 보고도 첫 건은 바로, 이후는 간격마다 통과해야 진행 창이 움직인다.</summary>
    [Fact(Skip = Known.B7)]
    public void 솎아내기_진행률은_첫_중간보고를_통과시킨다()
    {
        var seen = RunThrottled(intervalMs: 60_000, intermediateCount: 50);
        Assert.Equal(2, seen.Count);                 // 첫 중간 보고 1건 + 완료 1건
        Assert.Equal(1, seen[0].BytesProcessed);
    }

    [Theory]
    [InlineData(0, 0, 0.0)]
    [InlineData(50, 0, 0.0)]
    [InlineData(25, 100, 25.0)]
    [InlineData(100, 100, 100.0)]
    public void Percentage_계산(long processed, long total, double expected)
    {
        Assert.Equal(expected, new ArchiveProgress { BytesProcessed = processed, TotalBytes = total }.Percentage);
    }
}
