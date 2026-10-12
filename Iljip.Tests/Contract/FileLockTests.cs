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
/// 현재: 손상 7z·손상 egg/alz 는 라이브러리가 열다 실패하면서 파일 핸들을 닫지 않아(GC 가 돌 때까지) 파일이 잠긴다(B8).
/// zip·tar·rar 등 나머지는 바로 풀린다.
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

    /// <summary>[B8] 손상 7z·egg·alz 도 열기 실패 직후 원본을 바로 지울 수 있어야 한다.</summary>
    [Theory(Skip = Known.B8)]
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
    }

    /// <summary>
    /// [B8 근거] 손상 7z·egg·alz 는 열기 실패 직후 원본을 지우려 하면 IOException(다른 프로세스가 사용 중) — GC 를 막아 둔 상태에서 재현.
    /// ★ 고쳐지면 실패하는 게 정상(BugEvidenceTests 와 같은 규칙: 지우고 위 Skip 해제).
    /// </summary>
    [Theory, Trait(Kinds.Key, Kinds.Record)]
    [InlineData("trunc_half.7z", "bandizip/7z_default.7z", "half", "list")]
    [InlineData("trunc_half.7z", "bandizip/7z_default.7z", "half", "extract")]
    [InlineData("garbage.egg", "bandizip/zip_default.zip", "garbage", "list")]
    [InlineData("garbage.alz", "bandizip/zip_default.zip", "garbage", "extract")]
    public async Task B8_손상_7z_egg_alz는_열기_실패후_파일이_잠긴다(string name, string sourceRel, string how, string op)
    {
        using var tmp = new TempDir("lockb8");
        string arc = Make(tmp, name, sourceRel, how);
        var (ex, del) = await FailThenDelete(op, arc, tmp.P("out"));
        Assert.NotNull(ex);
        Assert.IsType<IOException>(del);
        // GC 가 놓친 핸들을 닫으면 그제야 지워진다 → '누수'임을 확인
        GC.Collect();
        GC.WaitForPendingFinalizers();
        File.Delete(arc);
        Assert.False(File.Exists(arc));
    }

    /// <summary>실패시키고 곧바로 원본 삭제를 시도한다. 그 사이 GC 가 핸들을 닫아 버리지 않게 GC 금지 구간에서.</summary>
    private static async Task<(Exception? Failure, Exception? DeleteError)> FailThenDelete(string op, string arc, string dest)
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
