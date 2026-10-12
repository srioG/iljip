using Iljip.Models;
using Iljip.Services;
using Iljip.Tests.TestSupport;

namespace Iljip.Tests.Contract;

/// <summary>
/// [범위 5] 취소: 큰(32 MiB, 테스트 중 생성) 압축의 해제·압축 도중 CancellationToken 을 취소하면
/// OperationCanceledException 계열이 나고 부분 결과가 정리된다(커밋 c8471d0 '부분파일 정리' 검증).
/// 취소 시점은 시간이 아니라 '진행률이 8 MiB 를 넘는 순간'으로 정해 매번 같은 결과가 나오게 한다.
/// </summary>
public class CancellationTests
{
    internal const int BigSize = 32 * 1024 * 1024;
    private const long CancelAfter = 8L * 1024 * 1024;

    /// <summary>작은 파일(a_작은.txt) 다음에 큰 파일(z_큰.bin)이 오는 원본 폴더.</summary>
    internal static string MakeSource(TempDir tmp)
    {
        string root = tmp.Dir("src", "취소 원본");
        File.WriteAllText(Path.Combine(root, "a_작은.txt"), "먼저 끝나는 작은 파일");
        var data = new byte[BigSize];
        new Random(7).NextBytes(data);
        File.WriteAllBytes(Path.Combine(root, "z_큰.bin"), data);
        return root;
    }

    /// <remarks>
    /// .7z(W2-01, 7z.dll): 7z.dll 이 읽는 입력 스트림이 취소를 확인해 큰 파일 한가운데서 멈추고, 임시 폴더에서 끝난 파일만 옮긴다.
    /// </remarks>
    [Theory]
    [InlineData(".zip")]
    [InlineData(".tar")]
    [InlineData(".7z")]
    public async Task 해제_도중_취소하면_OCE_그리고_중단된_파일만_지워진다(string ext)
    {
        using var tmp = new TempDir("cx");
        string src = MakeSource(tmp);
        string arc = tmp.P("big" + ext);
        var svc = Svc.For(arc);
        if (ext == ".7z")
            SevenZipTestArchives.SevenZip(src, arc, store: true);   // 일집의 7z 만들기는 W2-02 — 재료는 7z.dll 로 직접
        else
            await svc.CompressAsync(new[] { src }, arc, new CompressionOptions { Level = CompressionLevelOption.Store });
        string smallSha = TreeAssert.Sha256(Path.Combine(src, "a_작은.txt"));

        using var cts = new CancellationTokenSource();
        var progress = new ProgressRecorder(p =>
        {
            if (p.CurrentFile.EndsWith("z_큰.bin", StringComparison.Ordinal) && p.BytesProcessed > CancelAfter)
                cts.Cancel();
        });
        string dest = tmp.P("out");
        var ex = await Svc.Catch(() => svc.ExtractAsync(arc, dest, null, progress, cts.Token));

        Assert.IsAssignableFrom<OperationCanceledException>(ex);
        Assert.True(cts.IsCancellationRequested);
        // 이미 끝난 앞 파일은 보존, 쓰던 큰 파일은 삭제(절반짜리가 정상 이름으로 남으면 안 됨)
        Assert.Equal(new[] { "취소 원본/a_작은.txt" }, TreeAssert.Files(dest));
        Assert.Equal(smallSha, TreeAssert.Sha256(Path.Combine(dest, "취소 원본", "a_작은.txt")));
    }

    private static async Task<(Exception? Ex, string Arc)> CancelZipCompress(TempDir tmp)
    {
        string src = MakeSource(tmp);
        string arc = tmp.P("big.zip");
        using var cts = new CancellationTokenSource();
        var progress = new ProgressRecorder(p => { if (p.BytesProcessed > CancelAfter) cts.Cancel(); });
        var ex = await Svc.Catch(() => Svc.For(arc).CompressAsync(new[] { src }, arc, new CompressionOptions { Level = CompressionLevelOption.Store }, progress, cts.Token));
        return (ex, arc);
    }

    /// <summary>계약: ZIP 압축 도중 취소 → OCE, 압축이 끝까지 가지 않는다(남은 파일이 있다면 큰 파일이 다 들어가지 않은 것).</summary>
    [Fact]
    public async Task ZIP_압축_도중_취소하면_OCE()
    {
        using var tmp = new TempDir("cc");
        var (ex, arc) = await CancelZipCompress(tmp);
        Assert.IsAssignableFrom<OperationCanceledException>(ex);
        Assert.False(File.Exists(arc) && new FileInfo(arc).Length >= BigSize, "취소했는데 큰 파일이 다 들어감");
    }

    /// <summary>
    /// 현재 동작 기록: 서비스는 '쓰다 만 압축 파일'을 지우지 않는다. 지우는 일은 호출자 몫
    /// (MainViewModel.CompressAsync 의 catch 가 File.Delete — 커밋 c8471d0). 엔진이 스스로 지우게 바뀌면 이 테스트는 지우고, 화면 쪽 정리는 그대로 둘 것.
    /// </summary>
    [Fact, Trait(Kinds.Key, Kinds.Record)]
    public async Task 기록_ZIP_압축_취소후_부분_압축파일은_서비스가_안_지운다()
    {
        using var tmp = new TempDir("ccr");
        var (_, arc) = await CancelZipCompress(tmp);
        Assert.True(File.Exists(arc), "현재는 부분 ZIP 이 남는 것이 동작(정리는 호출자 MainViewModel)");
    }

    /// <summary>
    /// [B6 고침] TAR 압축 도중(큰 파일을 쓰기 시작할 때) 취소하면 OCE 가 나야 한다.
    /// 예전: TarArchiveService 는 파일과 파일 '사이'에서만 취소를 확인해, 마지막 큰 파일을 쓰는 동안 누른 취소는 무시되고 정상 완료됐다.
    /// (파일 '한가운데'에서 누른 취소는 FixedBugTests.TAR_마지막_큰파일_쓰는중_취소하면_OCE)
    /// </summary>
    [Fact]
    public async Task TAR_압축_도중_취소하면_OCE()
    {
        using var tmp = new TempDir("ct");
        string src = MakeSource(tmp);
        string arc = tmp.P("big.tar");
        using var cts = new CancellationTokenSource();
        var progress = new ProgressRecorder(p => { if (p.CurrentFile.EndsWith("z_큰.bin", StringComparison.Ordinal)) cts.Cancel(); });
        var ex = await Svc.Catch(() => Svc.For(arc).CompressAsync(new[] { src }, arc, null, progress, cts.Token));
        Assert.IsAssignableFrom<OperationCanceledException>(ex);
    }

    /// <summary>이미 취소된 토큰: 바로 취소 예외, 파일은 하나도 안 풀린다.</summary>
    [Fact]
    public async Task 이미_취소된_토큰이면_아무것도_안_푼다()
    {
        var fx = FixtureCatalog.Get("zip_default");
        using var tmp = new TempDir("cpre");
        string dest = tmp.P("out");
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var ex = await Svc.Catch(() => Svc.For(fx.FullPath).ExtractAsync(fx.FullPath, dest, null, null, cts.Token));
        Assert.IsAssignableFrom<OperationCanceledException>(ex);
        TreeAssert.NoFiles(dest);
    }

    /// <summary>현재 동작 기록: 이미 취소된 토큰이면 대상 폴더도 만들지 않는다(작업이 시작되지 않음 — Task.Run 이 토큰을 먼저 봄).</summary>
    [Fact, Trait(Kinds.Key, Kinds.Record)]
    public async Task 기록_이미_취소된_토큰이면_대상폴더도_안_만든다()
    {
        var fx = FixtureCatalog.Get("zip_default");
        using var tmp = new TempDir("cprer");
        string dest = tmp.P("out");
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        await Svc.Catch(() => Svc.For(fx.FullPath).ExtractAsync(fx.FullPath, dest, null, null, cts.Token));
        Assert.False(Directory.Exists(dest));
    }

    /// <summary>BZ2 해제 도중 취소도 OCE 이고 쓰던 파일이 지워진다(BZip2 전용 경로).</summary>
    [Fact]
    public async Task BZ2_해제_도중_취소하면_쓰던_파일이_지워진다()
    {
        using var tmp = new TempDir("cb");
        string one = tmp.P("큰.bin");
        var data = new byte[1024 * 1024];   // BZip2 압축이 느려(순수 C#) 1 MiB 로 — 해제 읽기는 여러 번에 나뉨
        new Random(11).NextBytes(data);
        File.WriteAllBytes(one, data);
        string arc = tmp.P("큰.bin.bz2");
        var svc = Svc.For(arc);
        await svc.CompressAsync(new[] { one }, arc);

        using var cts = new CancellationTokenSource();
        var progress = new ProgressRecorder(p => { if (p.BytesProcessed > 200 * 1024) cts.Cancel(); });
        string dest = tmp.P("out");
        var ex = await Svc.Catch(() => svc.ExtractAsync(arc, dest, null, progress, cts.Token));
        Assert.IsAssignableFrom<OperationCanceledException>(ex);
        TreeAssert.NoFiles(dest);
    }
}
