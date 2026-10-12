using System.Runtime;
using Iljip.Tests.TestSupport;

namespace Iljip.Tests.Contract;

/// <summary>GC 에 민감한 테스트는 다른 테스트와 동시에 돌리지 않는다(다른 테스트의 메모리 사용이 GC 를 부르면 결과가 흔들림).</summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class SequentialCollection
{
    public const string Name = "순차 실행(GC 민감)";
}

/// <summary>
/// 열기에 실패한 압축 파일을 일집이 '잠근 채' 두지 않는지 — 사용자가 손상 파일을 바로 지우거나 옮길 수 있어야 한다.
/// 예전(B8): 손상 7z·손상 egg/alz 는 라이브러리가 열다 실패하면서 파일 핸들을 닫지 않아(GC 가 돌 때까지) 파일이 잠겼다.
/// 7z 는 SharpCompress 0.50 으로, egg/alz 는 서비스가 스트림을 직접 열고 실패 때 닫도록 고쳤다.
/// </summary>
[Collection(SequentialCollection.Name)]
public class FileLockTests
{
    private static string Make(TempDir tmp, string name, string sourceRel, string how)
    {
        string arc = tmp.P(name);
        byte[] src = File.ReadAllBytes(FixtureCatalog.PathOf(sourceRel));
        File.WriteAllBytes(arc, how switch
        {
            "half" => src[..(src.Length / 2)],
            "garbage" => Enumerable.Range(0, 5000).Select(i => (byte)(i * 7 + 3)).ToArray(),
            _ => throw new ArgumentException(how)
        });
        return arc;
    }

    private static Task Run(string op, string arc, string dest) =>
        op == "list" ? Svc.For(arc).ListEntriesAsync(arc) : Svc.For(arc).ExtractAsync(arc, dest);

    /// <summary>지금도 잠그지 않는 경우: 실패 직후 원본을 바로 지울 수 있다.</summary>
    [Theory]
    [InlineData("trunc_half.zip", "bandizip/zip_default.zip", "half", "extract")]
    [InlineData("garbage.zip", "bandizip/zip_default.zip", "garbage", "list")]
    [InlineData("trunc_half.tar", "bandizip/tar_default.tar", "half", "extract")]
    [InlineData("garbage.rar", "bandizip/zip_default.zip", "garbage", "extract")]
    [InlineData("garbage.bz2", "python/견적서_2026.txt.bz2", "garbage", "extract")]
    public async Task 열기_실패후_원본파일을_바로_지울_수_있다(string name, string sourceRel, string how, string op)
    {
        using var tmp = new TempDir("lock");
        string arc = Make(tmp, name, sourceRel, how);
        var (ex, del) = await FailThenDelete(op, arc, tmp.P("out"));
        Assert.NotNull(ex);
        Assert.Null(del);
        Assert.False(File.Exists(arc));
    }

    /// <summary>[B8 고침] 손상 7z·egg·alz 도 열기 실패 직후 원본을 바로 지울 수 있어야 한다.</summary>
    [Theory]
    [InlineData("trunc_half.7z", "bandizip/7z_default.7z", "half", "list")]
    [InlineData("trunc_half.7z", "bandizip/7z_default.7z", "half", "extract")]
    [InlineData("garbage.egg", "bandizip/zip_default.zip", "garbage", "list")]
    [InlineData("garbage.alz", "bandizip/zip_default.zip", "garbage", "extract")]
    public async Task 손상_7z_egg_alz도_열기_실패후_바로_지울_수_있다(string name, string sourceRel, string how, string op)
    {
        using var tmp = new TempDir("lock8");
        string arc = Make(tmp, name, sourceRel, how);
        var (ex, del) = await FailThenDelete(op, arc, tmp.P("out"));
        Assert.NotNull(ex);
        Assert.Null(del);
        Assert.False(File.Exists(arc));
    }

    // (B8 근거 테스트 'B8_손상_7z_egg_alz는_열기_실패후_파일이_잠긴다'는 위 테스트로 대체됨 — 같은 데이터, 기대만 뒤집힘)

    /// <summary>
    /// 분할 EGG: 첫 조각이 가리키는 다음 조각이 없어 열기에 실패해도(EggDotNet MissingVolumeException), 첫 조각과 같은 폴더의
    /// 다른 조각(EggDotNet 이 분할 후보로 연 형제 파일)까지 모두 바로 지울 수 있다.
    /// </summary>
    [Theory]
    [InlineData("list")]
    [InlineData("extract")]
    public async Task 조각이_빠진_분할_EGG도_열기_실패후_모든_조각을_바로_지울_수_있다(string op)
    {
        using var tmp = new TempDir("lockspl");
        string dir = tmp.Dir("vols");
        string first = Path.Combine(dir, "분할.vol1.egg");
        string other = Path.Combine(dir, "분할.vol3.egg");
        RawEgg.WriteVolumeHeaderOnly(first, headerId: 0x1111, previousId: 0, nextId: 0x2222);   // 다음 조각(0x2222)이 없음
        RawEgg.WriteVolumeHeaderOnly(other, headerId: 0x3333, previousId: 0x2222, nextId: 0);
        var (ex, del) = await FailThenDelete(op, first, tmp.P("out"), other);
        Assert.NotNull(ex);
        Assert.Null(del);
        Assert.False(File.Exists(first) || File.Exists(other));
    }

    /// <summary>분할 EGG 를 정상으로 목록 보기·풀기 한 뒤에는(서비스가 아카이브를 닫은 뒤) 모든 조각을 바로 지울 수 있다.</summary>
    [Fact]
    public async Task 분할_EGG를_풀고나면_모든_조각을_바로_지울_수_있다()
    {
        using var tmp = new TempDir("lockspl2");
        var (items, files, tree) = EggAlzTests.EggSample(tmp);
        string dir = tmp.Dir("vols");
        var vols = RawEgg.WriteSplit(Path.Combine(dir, "분할"), items, parts: 3);
        var svc = Svc.For(vols[0]);
        TreeAssert.ListEquals(await svc.ListEntriesAsync(vols[0]), files, context: "분할 egg");
        string dest = tmp.P("out");
        await svc.ExtractAsync(vols[0], dest);
        TreeAssert.ExtractedTreeEquals(dest, tree, context: "분할 egg");
        foreach (var v in vols)
            Assert.Null(Record.Exception(() => File.Delete(v)));
    }

    /// <summary>
    /// 실패시키고 곧바로 원본(과 <paramref name="alsoDelete"/>) 삭제를 시도한다. 그 사이 GC 가 핸들을 닫아 버리지 않게 GC 금지 구간에서.
    /// DeleteError 는 처음 실패한 삭제의 예외.
    /// </summary>
    private static async Task<(Exception? Failure, Exception? DeleteError)> FailThenDelete(string op, string arc, string dest, params string[] alsoDelete)
    {
        bool noGc = false;
        try
        {
            noGc = GC.TryStartNoGCRegion(32L * 1024 * 1024);
        }
        catch (InvalidOperationException) { /* 이미 금지 구간 */ }
        try
        {
            var ex = await Svc.Catch(() => Run(op, arc, dest));
            var del = Record.Exception(() => File.Delete(arc));
            foreach (var extra in alsoDelete)
                del ??= Record.Exception(() => File.Delete(extra));
            return (ex, del);
        }
        finally
        {
            if (noGc && GCSettings.LatencyMode == GCLatencyMode.NoGCRegion)
            {
                try { GC.EndNoGCRegion(); } catch (InvalidOperationException) { /* 이미 끝남 */ }
            }
        }
    }
}
