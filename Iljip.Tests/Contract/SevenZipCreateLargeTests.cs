using System.Diagnostics;
using System.Text;
using Iljip.Models;
using Iljip.Services;
using Iljip.Tests.TestSupport;
using Iljip.ViewModels;
using Xunit.Abstractions;

namespace Iljip.Tests.Contract;

/// <summary>
/// 큰 원본(160MB) 7z 시험은 다른 테스트와 동시에 돌리지 않는다 — 메모리 재기·시간 재기가 흔들리지 않게(병렬 묶음이 다 끝난 뒤 차례로 돈다).
/// 원본은 한 번만 만들어 함께 쓴다(<see cref="SevenZipLargeSource"/>).
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class SevenZipLargeCollection : ICollectionFixture<SevenZipLargeSource>
{
    public const string Name = "7z 큰 원본(따로 실행)";
}

/// <summary>
/// 큰 원본: 무작위 30MB × 4(7z.dll 에 가장 느린 경우) + 글자 20MB × 2(잘 줄어드는 경우) = 160MB. '보통' 수준 압축은 한 번만 해서
/// 진행률·메모리·시간을 함께 잰다(<see cref="NormalRun"/>).
/// </summary>
public sealed class SevenZipLargeSource : IDisposable
{
    public const int RandomFiles = 4;
    public const int RandomMiB = 30;
    public const int TextFiles = 2;
    public const int TextMiB = 20;

    public TempDir Tmp { get; } = new("7zlarge");
    public string Root { get; }
    public long TotalBytes { get; }

    private readonly Lazy<Task<Measurement>> _normal;

    public SevenZipLargeSource()
    {
        Root = Tmp.Dir("큰 원본");
        var rnd = new Random(20261013);
        var buf = new byte[1 << 20];
        for (int i = 0; i < RandomFiles; i++)
        {
            using var f = File.Create(Path.Combine(Root, $"무작위{i}.bin"));
            for (int m = 0; m < RandomMiB; m++)
            {
                rnd.NextBytes(buf);
                f.Write(buf);
            }
        }
        for (int i = 0; i < TextFiles; i++)
        {
            using var f = File.Create(Path.Combine(Root, $"장부{i}.txt"));
            var sb = new StringBuilder();
            long written = 0;
            for (int line = 0; written < (long)TextMiB << 20; line++)
            {
                sb.Clear();
                sb.Append("테스트용 가상 장부 ").Append(i).Append('-').Append(line).Append(" 품목 ").Append(rnd.Next(1000, 9999)).Append(" 수량 ")
                  .Append(rnd.Next(1, 500)).Append(" 단가 ").Append(rnd.Next(100, 99999)).Append("원\r\n");
                var bytes = Encoding.UTF8.GetBytes(sb.ToString());
                int n = (int)Math.Min(bytes.Length, ((long)TextMiB << 20) - written);
                f.Write(bytes, 0, n);
                written += n;
            }
        }
        TotalBytes = Directory.EnumerateFiles(Root).Sum(f => new FileInfo(f).Length);
        _normal = new Lazy<Task<Measurement>>(() => MeasureNormal());
    }

    /// <summary>'보통' 수준 압축 한 번의 실측.</summary>
    /// <param name="Reports">진행률 보고 전부.</param>
    /// <param name="ReportTimes">각 보고의 경과 시간(ms).</param>
    /// <param name="Elapsed">전체 시간.</param>
    /// <param name="BaselineMb">시작 전 프로세스 전용 메모리(MB).</param>
    /// <param name="PeakMb">압축 중 가장 컸던 전용 메모리(MB, 100ms 마다 잼).</param>
    /// <param name="OutputBytes">만든 7z 크기.</param>
    /// <param name="Error">실패했으면 그 예외.</param>
    public sealed record Measurement(List<ArchiveProgress> Reports, List<long> ReportTimes, TimeSpan Elapsed, long BaselineMb, long PeakMb, long OutputBytes, Exception? Error)
    {
        public long IncreaseMb => PeakMb - BaselineMb;
    }

    public Task<Measurement> NormalRun() => _normal.Value;

    private async Task<Measurement> MeasureNormal()
    {
        string arc = Tmp.P("보통.7z");
        var times = new List<long>();
        var sw = new Stopwatch();
        var rec = new ProgressRecorder(_ => { lock (times) times.Add(sw.ElapsedMilliseconds); });
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        using var proc = Process.GetCurrentProcess();
        proc.Refresh();
        long baseline = proc.PrivateMemorySize64;
        long peak = baseline;
        using var stop = new CancellationTokenSource();
        var sampler = Task.Run(async () =>
        {
            using var p = Process.GetCurrentProcess();
            while (!stop.IsCancellationRequested)
            {
                p.Refresh();
                long now = p.PrivateMemorySize64;
                if (now > Interlocked.Read(ref peak)) Interlocked.Exchange(ref peak, now);
                try { await Task.Delay(100, stop.Token); } catch (OperationCanceledException) { }
            }
        });
        sw.Start();
        var error = await Svc.Catch(() => new SevenZipArchiveService().CompressAsync(new[] { Root }, arc,
            new CompressionOptions { Level = CompressionLevelOption.Normal }, rec));
        sw.Stop();
        stop.Cancel();
        await sampler;
        long output = File.Exists(arc) ? new FileInfo(arc).Length : -1;
        if (File.Exists(arc)) File.Delete(arc);
        const long Mb = 1024 * 1024;
        return new Measurement(rec.Reports.ToList(), times.ToList(), sw.Elapsed, baseline / Mb, Interlocked.Read(ref peak) / Mb, output, error);
    }

    public void Dispose() => Tmp.Dispose();
}

/// <summary>
/// [W2-02, I2-1] 1차 W1-B 검증 지적(진행 막대가 몇 초 만에 100%·취소가 끝날 때까지 안 먹음·메모리 1.7~3.4GB)을 큰 원본으로 지킨다.
/// 기준: 진행률은 줄지 않고 서로 다른 값이 10개 이상·끝은 100%, 원본을 다 읽은 시점이 전체 시간의 절반보다 뒤, 메모리 증가 700MB 아래(이 PC RAM 7.8GB),
/// 취소는 3초 안·대상(덮어쓰려던 기존 파일)은 바이트 그대로·임시 파일 없음. 값을 7-Zip 기본으로 되돌리면 메모리 시험이 실패하는 것을 한 번 확인했다(HANDOFF W2-02).
/// </summary>
[Collection(SevenZipLargeCollection.Name)]
public class SevenZipCreateLargeTests
{
    private const long MemoryLimitMb = 700;
    private static readonly TimeSpan CancelLimit = TimeSpan.FromSeconds(3);

    private readonly SevenZipLargeSource _src;
    private readonly ITestOutputHelper _out;

    public SevenZipCreateLargeTests(SevenZipLargeSource src, ITestOutputHelper output)
    {
        _src = src;
        _out = output;
    }

    /// <summary>계약: 보통 수준 160MB — 진행률이 실제를 따라간다(줄지 않음·서로 다른 값 10개 이상·끝 100%·다 읽은 시점이 절반 뒤).</summary>
    [Fact]
    public async Task 보통_수준_큰_원본의_진행률이_실제를_따라간다()
    {
        var m = await _src.NormalRun();
        Assert.Null(m.Error);
        ProgressTests.AssertWellFormed(m.Reports, "7z 보통 160MB", expectIntermediate: true);
        Assert.Equal(_src.TotalBytes, m.Reports[^1].TotalBytes);
        Assert.Equal(100.0, m.Reports[^1].Percentage);
        int distinct = m.Reports.Select(r => r.BytesProcessed).Distinct().Count();
        Assert.True(distinct >= 10, $"서로 다른 진행 값이 {distinct}개뿐");
        // 원본을 다 읽은(진행률 100%) 시점 — 7z.dll 이 원본을 메모리로 미리 다 읽으면 몇 초 만에 100% 가 된다(1차 지적)
        int full = m.Reports.FindIndex(r => r.BytesProcessed >= _src.TotalBytes);
        double fullAt = m.ReportTimes[full] / m.Elapsed.TotalMilliseconds;
        Assert.True(fullAt > 0.5, $"원본을 전체 시간의 {fullAt:P0} 만에 다 읽음 — 진행 막대가 실제보다 앞섬");
    }

    /// <summary>계약: 보통 수준 160MB 압축 중 메모리 증가가 700MB 아래(1차 W1-B: 1.2~3.4GB).</summary>
    [Fact]
    public async Task 보통_수준_큰_원본의_메모리가_700MB_아래다()
    {
        var m = await _src.NormalRun();
        Assert.Null(m.Error);
        Assert.True(m.IncreaseMb < MemoryLimitMb, $"메모리 증가 {m.IncreaseMb}MB(시작 {m.BaselineMb}MB → 최대 {m.PeakMb}MB) — {MemoryLimitMb}MB 를 넘음");
    }

    /// <summary>기록(이 PC 실측 — HANDOFF W2-02 에 옮겨 적음): 보통 수준 160MB 의 시간·메모리·크기·진행 보고 수.</summary>
    [Fact, Trait(Kinds.Key, Kinds.Record)]
    public async Task 기록_보통_수준_큰_원본_실측()
    {
        var m = await _src.NormalRun();
        int full = m.Reports.FindIndex(r => r.BytesProcessed >= _src.TotalBytes);
        _out.WriteLine($"기록_7z_보통_160MB: 원본 {_src.TotalBytes / 1048576.0:F1}MB(무작위 {SevenZipLargeSource.RandomFiles}x{SevenZipLargeSource.RandomMiB}MB + 글자 {SevenZipLargeSource.TextFiles}x{SevenZipLargeSource.TextMiB}MB)");
        _out.WriteLine($"  설정 {string.Join(";", SevenZipArchiveService.EngineParameters(CompressionOptions.Default).Select(p => p.Name + "=" + p.Value))}, 논리 CPU {Environment.ProcessorCount}");
        _out.WriteLine($"  시간 {m.Elapsed.TotalSeconds:F1}초, 결과 {m.OutputBytes / 1048576.0:F1}MB");
        _out.WriteLine($"  전용 메모리 시작 {m.BaselineMb}MB → 최대 {m.PeakMb}MB (증가 {m.IncreaseMb}MB, 한도 {MemoryLimitMb}MB)");
        _out.WriteLine($"  진행 보고 {m.Reports.Count}개(서로 다른 값 {m.Reports.Select(r => r.BytesProcessed).Distinct().Count()}개), 100% 도달 {(full >= 0 ? m.ReportTimes[full] / 1000.0 : -1):F1}초");
        Assert.Null(m.Error);
        Assert.True(m.OutputBytes > 0);
    }

    /// <summary>
    /// 계약: 보통 수준으로 큰 원본을 압축하는 도중(시작 3초 뒤이고 30% 를 넘긴 뒤 — 그때도 100% 가 아님) 취소 → 3초 안에 <see cref="OperationCanceledException"/>,
    /// 덮어쓰려던 기존 파일은 바이트 그대로, 임시 파일·임시 폴더 없음. 1차 W1-B 는 7z.dll 이 원본을 1초 안에 메모리로 다 읽어(그 뒤로는 취소를 볼 읽기가 없음)
    /// 취소가 압축이 끝날 때까지 미뤄졌다 — 7-Zip 기본값으로 되돌리면 이 시험이 '3초 뒤 이미 100%' 로 실패한다.
    /// </summary>
    [Fact]
    public async Task 도중에_취소하면_3초_안에_멈추고_기존_파일이_그대로다()
    {
        string folder = _src.Tmp.Dir("취소 대상");
        string arc = Path.Combine(folder, "결과.7z");
        var old = Encoding.UTF8.GetBytes("예전에 만든 7z(덮어쓰기를 고른 상태)");
        File.WriteAllBytes(arc, old);
        using var cts = new CancellationTokenSource();
        long seen = 0;
        var rec = new ProgressRecorder(p =>
        {
            long cur;
            while ((cur = Interlocked.Read(ref seen)) < p.BytesProcessed && Interlocked.CompareExchange(ref seen, p.BytesProcessed, cur) != cur) { }
        });
        var task = new SevenZipArchiveService().CompressAsync(new[] { _src.Root }, arc, new CompressionOptions { Level = CompressionLevelOption.Normal }, rec, cts.Token);
        try
        {
            var wait = Stopwatch.StartNew();
            while ((Interlocked.Read(ref seen) < _src.TotalBytes * 3 / 10 || wait.Elapsed < TimeSpan.FromSeconds(3))
                   && !task.IsCompleted && wait.Elapsed < TimeSpan.FromMinutes(2))
                await Task.Delay(20);
            Assert.False(task.IsCompleted, "취소 시점에 닿기 전에 끝남(취소를 시험할 수 없음)");
            long at = Interlocked.Read(ref seen);
            Assert.True(at < _src.TotalBytes, $"시작 {wait.Elapsed.TotalSeconds:F1}초 뒤 진행률이 이미 100% — 7z.dll 이 원본을 메모리로 다 읽은 것");

            var sw = Stopwatch.StartNew();
            cts.Cancel();
            var ex = await Svc.Catch(() => task);
            sw.Stop();
            _out.WriteLine($"기록_7z_취소: {at * 100.0 / _src.TotalBytes:F0}% 지점에서 취소 → {sw.Elapsed.TotalMilliseconds:F0}ms 뒤 멈춤");
            Assert.IsAssignableFrom<OperationCanceledException>(ex);
            Assert.True(sw.Elapsed < CancelLimit, $"취소가 {sw.Elapsed.TotalSeconds:F1}초 뒤에야 먹힘");
            Assert.Equal(old, File.ReadAllBytes(arc));
            SevenZipCreateTests.AssertNoLeftovers(folder);
        }
        finally
        {
            cts.Cancel();
            await Svc.Catch(() => task);
        }
    }

    /// <summary>계약: 처음 바이트를 읽자마자 취소(서비스) → 3초 안에 OCE, 대상 파일·임시 파일 없음.</summary>
    [Fact]
    public async Task 처음_읽자마자_취소해도_3초_안에_멈추고_아무것도_안_남긴다()
    {
        string folder = _src.Tmp.Dir("처음 취소");
        string arc = Path.Combine(folder, "결과.7z");
        using var cts = new CancellationTokenSource();
        var cancelledAt = new Stopwatch();
        var rec = new ProgressRecorder(p =>
        {
            if (p.BytesProcessed > 0 && !cts.IsCancellationRequested)
            {
                cancelledAt.Start();
                cts.Cancel();
            }
        });
        var ex = await Svc.Catch(() => new SevenZipArchiveService().CompressAsync(new[] { _src.Root }, arc, new CompressionOptions { Level = CompressionLevelOption.Normal }, rec, cts.Token));
        cancelledAt.Stop();
        _out.WriteLine($"기록_7z_처음_취소: {cancelledAt.Elapsed.TotalMilliseconds:F0}ms 뒤 멈춤");
        Assert.IsAssignableFrom<OperationCanceledException>(ex);
        Assert.True(cancelledAt.Elapsed < CancelLimit, $"취소가 {cancelledAt.Elapsed.TotalSeconds:F1}초 뒤에야 먹힘");
        Assert.False(File.Exists(arc));
        SevenZipCreateTests.AssertNoLeftovers(folder);
    }

    /// <summary>
    /// 계약(화면): 진행 창 [취소]를 처음 바이트에 누른 것처럼(가짜 창 CancelOnFirstBytes) — 3초 안에 '압축 취소됨', 오류 창 없음,
    /// 덮어쓰려던 기존 파일 바이트 그대로, 임시 파일 없음.
    /// </summary>
    [Fact]
    public async Task 화면에서_처음_읽자마자_취소해도_3초_안에_멈춘다()
    {
        string folder = _src.Tmp.Dir("화면 취소");
        string arc = Path.Combine(folder, "결과.7z");
        var old = Encoding.UTF8.GetBytes("예전 파일");
        File.WriteAllBytes(arc, old);
        var ui = new FakeUserInteraction { SavePath = arc, CompressionOptions = CompressionOptions.Default, CancelOnFirstBytes = true };
        var vm = new MainViewModel(ui);
        vm.AddToStaging(new[] { _src.Root });
        var sw = Stopwatch.StartNew();
        await VmRunner.Run(() => vm.CompressCommand.ExecuteAsync(null), "7z 압축(취소)");
        sw.Stop();
        _out.WriteLine($"기록_7z_화면_취소: 명령 전체 {sw.Elapsed.TotalMilliseconds:F0}ms");
        Assert.Equal("압축 취소됨", vm.StatusText);
        Assert.Empty(ui.Errors);
        Assert.True(sw.Elapsed < CancelLimit, $"화면 취소가 {sw.Elapsed.TotalSeconds:F1}초 걸림");
        Assert.Equal(old, File.ReadAllBytes(arc));
        SevenZipCreateTests.AssertNoLeftovers(folder);
    }
}
