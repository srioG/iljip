using System.Text;
using Iljip.Services;
using Iljip.Tests.TestSupport;

namespace Iljip.Tests.Contract;

/// <summary>
/// [범위 13] EGG/ALZ(이스트소프트 알집 형식) 성공 경로. 반디집은 EGG/ALZ 를 못 만들고 이 PC 에는 알집이 없어서,
/// 테스트 안에서 최소 작성기(<see cref="RawEgg"/>·<see cref="RawAlz"/>, Store 방식·암호 없음)로 합성 데이터를 만들어 쓴다.
/// 만든 EGG 는 EggDotNet 자체 CRC 검사(ChecksumValid)로 교차 확인한다.
/// <para>
/// EggAlzArchiveService 는 SharpCompressArchiveServiceBase 를 상속하지 않고 zip-slip 방어·동명 파일 비켜 쓰기·
/// 부분 파일 정리·진행률을 따로 복제해 갖고 있으므로, 같은 계약을 여기서 따로 고정한다.
/// 엔진을 7z.dll 계열로 바꾸면 EGG/ALZ 는 지원이 없으니(7-Zip 은 egg 미지원) 이 묶음이 '빠지면 안 되는 형식'을 지킨다.
/// </para>
/// 알려진 한계: 크기 0 항목(폴더 항목·빈 파일)이 든 EGG 는 B9, CP949 한글 이름 ALZ 는 B10(Known.cs).
/// 진짜 알집 산출물로는 확인하지 못했다(MANUAL_CHECKLIST 의 egg 항목으로 사람이 확인).
/// </summary>
public class EggAlzTests
{
    /// <summary>EGG 표본: SampleTree 의 '크기가 있는 파일'만(폴더 항목·빈 파일은 B9 라 뺌). 기대 목록·기대 트리 포함.</summary>
    internal static (List<RawEgg.Item> Items, List<ExpectedEntry> ExpectedFiles, List<ExpectedEntry> ExpectedTree) EggSample(TempDir tmp)
    {
        string src = SampleTree.Create(tmp.Dir("src"));
        string parent = Path.GetDirectoryName(src)!;
        var all = SampleTree.ExpectedOf(src);
        var files = all.Where(e => !e.IsDir && e.Size > 0).ToList();
        var dirs = all.Where(e => e.IsDir && files.Any(f => f.Path.StartsWith(e.Path + "/", StringComparison.Ordinal))).ToList();
        var items = files.Select(f => new RawEgg.Item(f.Path, File.ReadAllBytes(Path.Combine(parent, f.Path)))).ToList();
        return (items, files, files.Concat(dirs).ToList());
    }

    [Fact]
    public async Task EGG_목록과_해제가_원본과_같다()
    {
        using var tmp = new TempDir("egg");
        var (items, files, tree) = EggSample(tmp);
        string arc = tmp.P("표본.egg");
        RawEgg.Write(arc, items);

        // 작성기 교차 확인: EggDotNet 이 직접 읽어 CRC 가 맞아야 한다(일집 코드를 거치지 않음)
        using (var ea = EggDotNet.EggFile.Open(arc))
        {
            Assert.Equal(items.Count, ea.Entries.Count());
            Assert.All(ea.Entries, e => Assert.True(e.ChecksumValid(), "EggDotNet CRC 불일치(작성기 문제): " + e.FullName));
        }

        var svc = Svc.For(arc);
        Assert.IsType<EggAlzArchiveService>(svc);
        TreeAssert.ListEquals(await svc.ListEntriesAsync(arc), files, context: "egg");
        string dest = tmp.P("out");
        await svc.ExtractAsync(arc, dest);
        TreeAssert.ExtractedTreeEquals(dest, tree, context: "egg");
    }

    /// <summary>같은 곳에 두 번 풀면 기존 파일을 덮지 않고 '이름 (1)' 로 비켜 쓴다(EGG 전용 복제 코드).</summary>
    [Fact]
    public async Task EGG_두번_풀면_덮지_않고_이름1로_비켜쓴다()
    {
        using var tmp = new TempDir("eggc");
        var (items, _, _) = EggSample(tmp);
        string arc = tmp.P("표본.egg");
        RawEgg.Write(arc, items);
        string dest = tmp.P("out");
        var svc = Svc.For(arc);
        await svc.ExtractAsync(arc, dest);
        string mine = Path.Combine(dest, SampleTree.RootName, "한글 파일.txt");
        File.WriteAllText(mine, "사용자가 고친 내용");
        string mineSha = TreeAssert.Sha256(mine);

        await svc.ExtractAsync(arc, dest);

        Assert.Equal(mineSha, TreeAssert.Sha256(mine));
        string renamed = Path.Combine(dest, SampleTree.RootName, "한글 파일 (1).txt");
        Assert.True(File.Exists(renamed), "비켜 쓴 파일 없음");
        Assert.Equal(TreeAssert.Sha256(items.Single(i => i.Name.EndsWith("/한글 파일.txt", StringComparison.Ordinal)).Data!), TreeAssert.Sha256(renamed));
    }

    /// <summary>선택 해제: 폴더를 고르면 그 아래만, 경로째 풀린다.</summary>
    [Fact]
    public async Task EGG_선택해제는_고른_폴더만_푼다()
    {
        using var tmp = new TempDir("eggs");
        var (items, files, _) = EggSample(tmp);
        string arc = tmp.P("표본.egg");
        RawEgg.Write(arc, items);
        string dest = tmp.P("out");
        await Svc.For(arc).ExtractEntriesAsync(arc, dest, new[] { SampleTree.RootName + "/하위" });
        var snap = TreeAssert.Snapshot(dest);
        var want = files.Where(f => f.Path.StartsWith(SampleTree.RootName + "/하위/", StringComparison.Ordinal)).ToList();
        Assert.NotEmpty(want);
        foreach (var f in want)
            Assert.True(snap.TryGetValue(f.Path, out var sha) && sha == f.Sha256, "없음/내용 다름: " + f.Path);
        Assert.Equal(want.Count, snap.Values.Count(v => v is not null));
    }

    /// <summary>EGG 해제 도중 취소 → OCE, 이미 끝난 앞 파일은 보존, 쓰던 큰 파일은 지워진다(EGG 전용 복제 코드).</summary>
    [Fact]
    public async Task EGG_해제_도중_취소하면_OCE_그리고_중단된_파일만_지워진다()
    {
        using var tmp = new TempDir("eggx");
        string src = CancellationTests.MakeSource(tmp);
        string root = Path.GetFileName(src);
        string arc = tmp.P("big.egg");
        RawEgg.Write(arc, new[]
        {
            new RawEgg.Item(root + "/a_작은.txt", File.ReadAllBytes(Path.Combine(src, "a_작은.txt"))),
            new RawEgg.Item(root + "/z_큰.bin", File.ReadAllBytes(Path.Combine(src, "z_큰.bin"))),
        });
        using var cts = new CancellationTokenSource();
        var progress = new ProgressRecorder(p =>
        {
            if (p.CurrentFile.EndsWith("z_큰.bin", StringComparison.Ordinal) && p.BytesProcessed > 8L * 1024 * 1024) cts.Cancel();
        });
        string dest = tmp.P("out");
        var ex = await Svc.Catch(() => Svc.For(arc).ExtractAsync(arc, dest, null, progress, cts.Token));
        Assert.IsAssignableFrom<OperationCanceledException>(ex);
        Assert.Equal(new[] { root + "/a_작은.txt" }, TreeAssert.Files(dest));
        Assert.Equal(TreeAssert.Sha256(Path.Combine(src, "a_작은.txt")), TreeAssert.Sha256(Path.Combine(dest, root, "a_작은.txt")));
    }

    [Fact]
    public async Task EGG_해제_진행률이_0에서_100으로_끝난다()
    {
        using var tmp = new TempDir("eggp");
        var (items, files, _) = EggSample(tmp);
        string arc = tmp.P("표본.egg");
        RawEgg.Write(arc, items);
        var rec = new ProgressRecorder();
        await Svc.For(arc).ExtractAsync(arc, tmp.P("out"), null, rec);
        ProgressTests.AssertWellFormed(rec.Reports, "egg", expectIntermediate: true);
        Assert.Equal(files.Sum(f => f.Size), rec.Reports[^1].TotalBytes);
        Assert.Equal(files.Count, rec.Reports[^1].TotalFiles);
    }

    // ───────────── ALZ ─────────────

    private static readonly byte[] AlzData = Enumerable.Range(0, 100_000).Select(i => (byte)(i * 31 + 7)).ToArray();

    /// <summary>ALZ(이름 ASCII, '\' 구분, 폴더 항목·빈 파일 포함): 목록·해제가 원본과 같다.</summary>
    [Fact]
    public async Task ALZ_목록과_해제가_원본과_같다()
    {
        using var tmp = new TempDir("alz");
        string arc = tmp.P("sample.alz");
        byte[] readme = Encoding.UTF8.GetBytes("ALZ readme\r\n");
        byte[] top = Encoding.UTF8.GetBytes("top level");
        RawAlz.Write(arc,
            new RawAlz.Item(Encoding.ASCII.GetBytes("docs\\"), null),
            new RawAlz.Item(Encoding.ASCII.GetBytes("docs\\readme.txt"), readme),
            new RawAlz.Item(Encoding.ASCII.GetBytes("docs\\sub\\"), null),
            new RawAlz.Item(Encoding.ASCII.GetBytes("docs\\sub\\data.bin"), AlzData),
            new RawAlz.Item(Encoding.ASCII.GetBytes("empty.txt"), Array.Empty<byte>()),
            new RawAlz.Item(Encoding.ASCII.GetBytes("top.txt"), top));
        var expected = new List<ExpectedEntry>
        {
            new("docs", 0, null, true),
            new("docs/readme.txt", readme.Length, TreeAssert.Sha256(readme), false),
            new("docs/sub", 0, null, true),
            new("docs/sub/data.bin", AlzData.Length, TreeAssert.Sha256(AlzData), false),
            new("empty.txt", 0, TreeAssert.Sha256(Array.Empty<byte>()), false),
            new("top.txt", top.Length, TreeAssert.Sha256(top), false),
        };
        var svc = Svc.For(arc);
        Assert.IsType<EggAlzArchiveService>(svc);
        TreeAssert.ListEquals(await svc.ListEntriesAsync(arc), expected, context: "alz");
        string dest = tmp.P("out");
        await svc.ExtractAsync(arc, dest);
        TreeAssert.ExtractedTreeEquals(dest, expected, context: "alz");
    }

    /// <summary>[B10] 알집이 만드는 ALZ 의 한글 이름(CP949)이 바르게 보이고 그 이름으로 풀려야 한다.</summary>
    [Fact(Skip = Known.B10)]
    public async Task ALZ_CP949_한글_이름이_바르게_보인다()
    {
        using var tmp = new TempDir("alzk");
        string arc = tmp.P("korean.alz");
        WriteKoreanAlz(arc);
        var names = (await Svc.For(arc).ListEntriesAsync(arc)).Select(e => e.Path).ToList();
        Assert.Equal(new[] { "문서/", "문서/견적서.txt" }, names);
        string dest = tmp.P("out");
        await Svc.For(arc).ExtractAsync(arc, dest);
        Assert.Equal(new[] { "문서/견적서.txt" }, TreeAssert.Files(dest));
    }

    internal static void WriteKoreanAlz(string arc)
    {
        var cp949 = Encoding.GetEncoding(949);
        RawAlz.Write(arc,
            new RawAlz.Item(cp949.GetBytes("문서\\"), null),
            new RawAlz.Item(cp949.GetBytes("문서\\견적서.txt"), Encoding.UTF8.GetBytes("korean name")));
    }

    /// <summary>[B9] 크기 0 항목(폴더 항목·빈 파일)이 든 EGG 도 목록·해제가 원본과 같아야 한다(알집처럼).</summary>
    [Fact(Skip = Known.B9)]
    public async Task EGG_폴더항목과_빈파일이_있어도_열리고_풀린다()
    {
        using var tmp = new TempDir("egg0");
        string src = SampleTree.Create(tmp.Dir("src"));
        var expected = SampleTree.ExpectedOf(src);
        string arc = tmp.P("full.egg");
        RawEgg.Write(arc, RawEgg.FromFolder(src));
        var svc = Svc.For(arc);
        TreeAssert.ListEquals(await svc.ListEntriesAsync(arc), expected);
        string dest = tmp.P("out");
        await svc.ExtractAsync(arc, dest);
        TreeAssert.ExtractedTreeEquals(dest, expected);
    }
}
