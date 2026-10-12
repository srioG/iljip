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
/// 예전 버그: 크기 0 항목(폴더 항목·빈 파일)이 든 EGG(B9), CP949 한글 이름 ALZ(B10) — 둘 다 서비스에서 우회해 고침(아래 테스트).
/// 진짜 알집 산출물로는 확인하지 못했다(MANUAL_CHECKLIST 의 egg 항목으로 사람이 확인).
/// </summary>
public class EggAlzTests
{
    /// <summary>EGG 표본: SampleTree 의 '크기가 있는 파일'만(폴더 항목·빈 파일은 따로 시험 — EGG_폴더항목과_빈파일이_있어도_열리고_풀린다). 기대 목록·기대 트리 포함.</summary>
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

    /// <summary>[B10 고침] 알집이 만드는 ALZ 의 한글 이름(CP949)이 바르게 보이고 그 이름으로 풀려야 한다.</summary>
    [Fact]
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

    /// <summary>
    /// 화면이 넘기는 '복구된 한글 이름'으로 고른 ALZ 항목도 풀린다(서비스가 복구 이름 ↔ EggDotNet 항목을 짝지어 둠).
    /// 폴더를 고르면 그 아래가, 파일을 고르면 그 파일만.
    /// </summary>
    [Theory]
    [InlineData("문서")]
    [InlineData("문서/견적서.txt")]
    public async Task ALZ_한글_이름으로_선택해제가_된다(string selector)
    {
        using var tmp = new TempDir("alzks");
        string arc = tmp.P("korean.alz");
        WriteKoreanAlz(arc);
        string dest = tmp.P("out");
        await Svc.For(arc).ExtractEntriesAsync(arc, dest, new[] { selector });
        Assert.Equal(new[] { "문서/견적서.txt" }, TreeAssert.Files(dest));
        Assert.Equal("korean name", File.ReadAllText(Path.Combine(dest, "문서", "견적서.txt")));
    }

    internal static void WriteKoreanAlz(string arc)
    {
        var cp949 = Encoding.GetEncoding(949);
        RawAlz.Write(arc,
            new RawAlz.Item(cp949.GetBytes("문서\\"), null),
            new RawAlz.Item(cp949.GetBytes("문서\\견적서.txt"), Encoding.UTF8.GetBytes("korean name")));
    }

    /// <summary>
    /// [B10 보강] ALZ 이름이 한글 없이 한자·기호만 있는 CP949('漢字①.txt'), CP949 확장 한글('똠방각하_뷁.txt'), UTF-8 이 섞여도 모두 바르다.
    /// 예전엔 ZIP 용 판별(CP949 결과에 한글이 있어야 채택)을 써서 한자·기호 이름이 CP437 로 깨졌다('∙╙φ«¿τ.txt').
    /// </summary>
    [Fact]
    public async Task ALZ_한자_기호_확장한글_UTF8_이름이_모두_바르게_보인다()
    {
        var cp949 = Encoding.GetEncoding(949);
        string[] cpNames = { "漢字①.txt", "똠방각하_뷁.txt" };
        foreach (var n in cpNames)
        {
            byte[] raw = cp949.GetBytes(n);
            Assert.Equal(n, cp949.GetString(raw));                       // 전제: CP949 로 표현됨
            Assert.False(System.Text.Unicode.Utf8.IsValid(raw), n);       // 전제: UTF-8 로는 깨짐(EggDotNet 이 � 로 읽음)
        }
        using var tmp = new TempDir("alzh");
        string arc = tmp.P("mixed.alz");
        RawAlz.Write(arc,
            new RawAlz.Item(cp949.GetBytes(cpNames[0]), Encoding.UTF8.GetBytes("hanja")),
            new RawAlz.Item(cp949.GetBytes(cpNames[1]), Encoding.UTF8.GetBytes("ext hangul")),
            new RawAlz.Item(Encoding.UTF8.GetBytes("유티에프.txt"), Encoding.UTF8.GetBytes("utf8")));
        var want = new[] { "漢字①.txt", "똠방각하_뷁.txt", "유티에프.txt" };
        Assert.Equal(want, (await Svc.For(arc).ListEntriesAsync(arc)).Select(e => e.Path));
        string dest = tmp.P("out");
        await Svc.For(arc).ExtractAsync(arc, dest);
        Assert.Equal(want.OrderBy(x => x, StringComparer.Ordinal), TreeAssert.Files(dest));
        Assert.Equal("hanja", File.ReadAllText(Path.Combine(dest, "漢字①.txt")));
    }

    /// <summary>
    /// 이름 끝에 구분자가 없는 폴더 항목(폴더 속성만 있음 — ALZ 0x10, EGG 0x80)도 폴더로 보이고 폴더로 풀린다.
    /// 예전엔 끝 '/' 만 봐서 0바이트 '파일'로 풀리고, 그 아래 항목에서 "Cannot create … already exists" 로 해제가 실패했다.
    /// (진짜 알집이 폴더 이름 끝에 구분자를 붙이는지는 확인 못 함 — MANUAL_CHECKLIST 6절)
    /// </summary>
    [Theory]
    [InlineData("alz_cp949")]
    [InlineData("alz_ascii")]
    [InlineData("egg")]
    public async Task 끝_구분자_없는_폴더항목도_폴더로_풀린다(string kind)
    {
        using var tmp = new TempDir("dirattr");
        var cp949 = Encoding.GetEncoding(949);
        byte[] data = Encoding.UTF8.GetBytes("in folder");
        string folder = kind == "alz_ascii" ? "docs" : "문서";
        string arc;
        if (kind == "egg")
        {
            arc = tmp.P("noslash.egg");
            RawEgg.Write(arc, new[] { new RawEgg.Item(folder, null, DirSlash: false), new RawEgg.Item(folder + "/a.txt", data) });
        }
        else
        {
            var enc = kind == "alz_ascii" ? Encoding.ASCII : cp949;
            arc = tmp.P("noslash.alz");
            RawAlz.Write(arc, new RawAlz.Item(enc.GetBytes(folder), null), new RawAlz.Item(enc.GetBytes(folder + "\\a.txt"), data));
        }
        var svc = Svc.For(arc);
        var listed = await svc.ListEntriesAsync(arc);
        Assert.Equal(new[] { (folder + "/", true), (folder + "/a.txt", false) }, listed.Select(e => (e.Path, e.IsDirectory)));
        string dest = tmp.P("out");
        await svc.ExtractAsync(arc, dest);
        Assert.True(Directory.Exists(Path.Combine(dest, folder)), "폴더가 안 생김");
        Assert.Equal(new[] { folder + "/a.txt" }, TreeAssert.Files(dest));
        Assert.Equal("in folder", File.ReadAllText(Path.Combine(dest, folder, "a.txt")));
    }

    // ───────────── 내용 확인(크기·CRC) — W1-A 1차 검증 지적 ─────────────
    // EggDotNet 은 풀린 내용을 확인하지 않아(ChecksumValid 를 따로 부르지 않으면) 받다 만 egg/alz 도 짧은 파일을 남기고 '완료'로 끝났다.
    // 서비스가 크기·CRC 를 직접 맞춰 보고, 틀리면 쓰다 만 파일을 지우고 예외를 낸다.

    private static readonly byte[] BigData = Pseudo(200_000, 77);

    private static byte[] Pseudo(int n, int seed)
    {
        var b = new byte[n];
        new Random(seed).NextBytes(b);
        return b;
    }

    /// <summary>앞.txt(작음) + 큰.bin(200,000B, Store) 을 담은 EGG 또는 ALZ. ALZ 이름은 CP949.</summary>
    private static string WriteFrontAndBig(TempDir tmp, string kind, string name)
    {
        string arc = tmp.P(name + "." + kind);
        if (kind == "egg")
        {
            RawEgg.Write(arc, new[] { new RawEgg.Item("폴더/앞.txt", FrontData), new RawEgg.Item("폴더/큰.bin", BigData) });
        }
        else
        {
            var cp949 = Encoding.GetEncoding(949);
            RawAlz.Write(arc,
                new RawAlz.Item(cp949.GetBytes("폴더\\"), null),
                new RawAlz.Item(cp949.GetBytes("폴더\\앞.txt"), FrontData),
                new RawAlz.Item(cp949.GetBytes("폴더\\큰.bin"), BigData));
        }
        return arc;
    }

    private static readonly byte[] FrontData = Encoding.UTF8.GetBytes("앞 파일 — 온전함");

    /// <summary>예외(암호 문제로 오해하지 않음) + 대상 폴더에는 온전히 끝난 앞.txt 만 남음(잘리거나 틀린 큰.bin 은 지워짐).</summary>
    private static async Task AssertFailsLeavingOnlyFront(string arc, string dest, string context)
    {
        var ex = await Svc.Catch(() => Svc.For(arc).ExtractAsync(arc, dest));
        Assert.True(ex is not null, context + ": 손상 파일인데 예외 없이 끝남");
        Assert.IsType<InvalidDataException>(ex);
        Assert.False(Internals.IsPasswordRelated(ex!), context + ": 손상을 암호 문제로 오해함: " + ex!.Message);
        Assert.Equal(new[] { "폴더/앞.txt" }, TreeAssert.Files(dest));
        Assert.Equal(TreeAssert.Sha256(FrontData), TreeAssert.Sha256(Path.Combine(dest, "폴더", "앞.txt")));
    }

    /// <summary>계약: 뒤가 잘린 EGG·ALZ(받다 만 파일)는 예외로 알리고, 잘린 큰 파일을 짧은 채로 남기지 않는다.</summary>
    [Theory]
    [InlineData("egg", 30)]
    [InlineData("egg", 60)]
    [InlineData("egg", 98)]
    [InlineData("alz", 30)]
    [InlineData("alz", 98)]
    public async Task 잘린_EGG_ALZ는_예외_그리고_짧은_파일을_남기지_않는다(string kind, int percent)
    {
        using var tmp = new TempDir("eggcut");
        byte[] b = File.ReadAllBytes(WriteFrontAndBig(tmp, kind, "full"));
        string arc = tmp.P("cut." + kind);
        File.WriteAllBytes(arc, b[..(int)(b.Length * (long)percent / 100)]);
        await AssertFailsLeavingOnlyFront(arc, tmp.P("out"), $"{kind} {percent}%");
    }

    /// <summary>계약: 데이터 1바이트가 깨진 EGG·ALZ(크기는 맞음) — CRC 가 맞지 않으니 예외, 틀린 내용 파일을 남기지 않는다.</summary>
    [Theory]
    [InlineData("egg")]
    [InlineData("alz")]
    public async Task 데이터가_깨진_EGG_ALZ는_CRC로_알아챈다(string kind)
    {
        using var tmp = new TempDir("eggcrc");
        string arc = WriteFrontAndBig(tmp, kind, "crc");
        byte[] b = File.ReadAllBytes(arc);
        int at = CorruptArchiveTests.IndexOf(b, BigData[..64]);
        Assert.True(at > 0, "큰.bin 데이터 위치를 못 찾음");
        b[at + 100_000] ^= 0x01;
        File.WriteAllBytes(arc, b);
        await AssertFailsLeavingOnlyFront(arc, tmp.P("out"), kind);
    }

    /// <summary>
    /// 잘린 ALZ 라도 이름까지 온전한 항목은 CP949 한글 이름이 바르게 보인다(예전엔 끝 표시 'CLZ' 가 없으면 이름 복구를 포기해 �).
    /// 끝 표시만 빠지고 데이터는 온전하면 끝까지 풀린다.
    /// </summary>
    [Fact]
    public async Task 잘린_ALZ도_온전한_항목의_한글_이름은_보인다()
    {
        using var tmp = new TempDir("alzcutname");
        byte[] b = File.ReadAllBytes(WriteFrontAndBig(tmp, "alz", "full"));
        var want = new[] { "폴더/", "폴더/앞.txt", "폴더/큰.bin" };

        string cutData = tmp.P("cut_data.alz");             // 큰.bin 데이터 중간에서 끊김
        File.WriteAllBytes(cutData, b[..(b.Length - 50_000)]);
        Assert.Equal(want, (await Svc.For(cutData).ListEntriesAsync(cutData)).Select(e => e.Path));

        string cutEnd = tmp.P("cut_end.alz");               // 끝 표시(CLZ 8바이트)만 빠짐
        File.WriteAllBytes(cutEnd, b[..(b.Length - 8)]);
        Assert.Equal(want, (await Svc.For(cutEnd).ListEntriesAsync(cutEnd)).Select(e => e.Path));
        string dest = tmp.P("out");
        await Svc.For(cutEnd).ExtractAsync(cutEnd, dest);
        Assert.Equal(new[] { "폴더/앞.txt", "폴더/큰.bin" }, TreeAssert.Files(dest));
        Assert.Equal(TreeAssert.Sha256(BigData), TreeAssert.Sha256(Path.Combine(dest, "폴더", "큰.bin")));
    }

    /// <summary>
    /// 확인 실패: 암호 걸린 항목이면 화면이 암호를 다시 묻는 틀린 암호 예외(옛 ZIP 방식 EGG 암호는 틀린 암호가 검사를 우연히
    /// 통과해 내용만 틀릴 수 있음 — '손상일 수도' 문구 포함), 아니면 묻지 않는 손상 예외(W1-C: 문구가 아니라 타입으로 판정).
    /// </summary>
    [Fact]
    public void 확인값_불일치는_암호_항목에서만_암호를_다시_묻게_한다()
    {
        var encrypted = EggAlzArchiveService.DamagedEntry("a.txt", encrypted: true, "x");
        Assert.IsType<WrongPasswordException>(encrypted);
        Assert.True(Internals.IsPasswordRelated(encrypted));
        Assert.False(Internals.IsPasswordRelated(EggAlzArchiveService.DamagedEntry("a.txt", encrypted: false, "x")));
    }

    // ───── 암호 EGG(W1-C) — RawEgg 의 'Standard'(전통 ZipCrypto) 암호 ─────

    private const string EggPassword = "egg-pass-1234";

    /// <summary>앞.txt + 큰.bin 을 같은 암호로 건 EGG.</summary>
    private static string WriteEncryptedEgg(TempDir tmp, string name, string password = EggPassword)
    {
        string arc = tmp.P(name + ".egg");
        RawEgg.Write(arc, new[]
        {
            new RawEgg.Item("폴더/앞.txt", FrontData, Password: password),
            new RawEgg.Item("폴더/큰.bin", BigData, Password: password),
        });
        return arc;
    }

    /// <summary>작성기 확인 겸 성공 경로: 맞는 암호면 원본과 같게 풀린다(EggDotNet 자체 CRC 확인 포함).</summary>
    [Fact]
    public async Task 암호_EGG는_맞는_암호로_풀린다()
    {
        using var tmp = new TempDir("eggpw");
        string arc = WriteEncryptedEgg(tmp, "pw");
        string dest = tmp.P("out");
        await Svc.For(arc).ExtractAsync(arc, dest, EggPassword);
        Assert.Equal(new[] { "폴더/앞.txt", "폴더/큰.bin" }, TreeAssert.Files(dest));
        Assert.Equal(TreeAssert.Sha256(FrontData), TreeAssert.Sha256(Path.Combine(dest, "폴더", "앞.txt")));
        Assert.Equal(TreeAssert.Sha256(BigData), TreeAssert.Sha256(Path.Combine(dest, "폴더", "큰.bin")));
    }

    /// <summary>계약(W1-C, B5): 암호 EGG 에 틀린 암호 → WrongPasswordException, 암호 없음 → PasswordRequiredException. 어느 쪽이든 파일·새 폴더가 남지 않는다.</summary>
    [Theory]
    [InlineData("wrong-pass", typeof(WrongPasswordException))]
    [InlineData(null, typeof(PasswordRequiredException))]
    [InlineData("", typeof(PasswordRequiredException))]
    public async Task 암호_EGG의_틀리거나_없는_암호는_암호예외_그리고_파일없음(string? password, Type expected)
    {
        using var tmp = new TempDir("eggpw2");
        string arc = WriteEncryptedEgg(tmp, "pw");
        string dest = tmp.P("out");
        var ex = await Svc.Catch(() => Svc.For(arc).ExtractAsync(arc, dest, password));
        Assert.IsType(expected, ex);
        Assert.True(Internals.IsPasswordRelated(ex!));
        Assert.False(Directory.Exists(dest), "암호 오류인데 대상 폴더(또는 파일)가 남음");
    }

    /// <summary>
    /// 계약(W1-C, B4 와 같은 규칙): 첫 항목의 1바이트 검사를 우연히 통과한 틀린 암호는 첫 파일을 쓰레기로 '풀어' 쓴 뒤 CRC 에서 걸린다 —
    /// 틀린 암호 예외이고, 장부가 이번에 쓴 것(쓰레기 앞.txt·폴더)을 모두 지운다. 다시 맞는 암호로 풀면 '이름 (1)' 없이 원래 이름으로 풀린다.
    /// </summary>
    [Fact]
    public async Task 암호_EGG_검사통과_틀린암호는_쓰레기를_남기지_않는다()
    {
        using var tmp = new TempDir("egglucky");
        string arc = WriteEncryptedEgg(tmp, "lucky");
        string lucky = EggLuckyPassword(arc);
        string dest = tmp.P("out");
        var ex = await Svc.Catch(() => Svc.For(arc).ExtractAsync(arc, dest, lucky));
        Assert.IsType<WrongPasswordException>(ex);
        Assert.False(Directory.Exists(dest), $"'{lucky}': 우연 통과 틀린 암호가 쓴 것이 남음");

        await Svc.For(arc).ExtractAsync(arc, dest, EggPassword);
        Assert.Equal(new[] { "폴더/앞.txt", "폴더/큰.bin" }, TreeAssert.Files(dest));
    }

    /// <summary>EGG 첫 항목(앞.txt)의 12바이트 암호 머리 검사(마지막 바이트 = CRC 윗바이트)를 통과하는 틀린 ASCII 암호.</summary>
    private static string EggLuckyPassword(string eggPath)
    {
        byte[] b = File.ReadAllBytes(eggPath);
        int at = CorruptArchiveTests.IndexOf(b, BitConverter.GetBytes(0x08D1470Fu));
        Assert.True(at > 0, "암호 머리를 못 찾음");
        byte[] head = b[(at + 8)..(at + 20)];
        byte check = b[at + 23];   // CRC(LE) 의 마지막 바이트 = 윗바이트
        for (int i = 0; i < 1_000_000; i++)
        {
            string pw = "w" + i;
            if (pw != EggPassword && DecryptLastByte(pw, head) == check) return pw;
        }
        throw new InvalidOperationException("통과하는 틀린 암호를 못 찾음");
    }

    private static byte DecryptLastByte(string password, byte[] head)
    {
        uint k0 = 0x12345678, k1 = 0x23456789, k2 = 0x34567890;
        void Upd(byte c)
        {
            k0 = Crc32.Update(k0, c);
            k1 = unchecked((k1 + (k0 & 0xFF)) * 134775813 + 1);
            k2 = Crc32.Update(k2, (byte)(k1 >> 24));
        }
        foreach (char c in password) Upd((byte)c);
        byte last = 0;
        foreach (byte e in head)
        {
            uint t = (k2 | 2) & 0xFFFF;
            last = (byte)(e ^ (byte)((t * (t ^ 1)) >> 8));
            Upd(last);
        }
        return last;
    }

    /// <summary>
    /// 분할 EGG 의 다음 조각 후보는 같은 폴더의 다른 '.egg' 파일뿐(대소문자 무시) — 예전(EggDotNet 기본)엔 폴더의 모든 파일을 열어
    /// 수천 개 폴더에서 느리고 OneDrive '온라인 전용' 파일을 내려받게 만들 수 있었다.
    /// </summary>
    [Fact]
    public void 분할_EGG_조각_후보는_같은_폴더의_다른_egg_파일만()
    {
        using var tmp = new TempDir("splitcand");
        string dir = tmp.Dir("vols");
        foreach (var n in new[] { "a.vol1.egg", "a.vol2.EGG", "b.egg", "notes.txt", "data.bin", "a.vol1.egg.bak", "egg" })
            File.WriteAllBytes(Path.Combine(dir, n), new byte[] { 1 });
        string first = Path.Combine(dir, "a.vol1.egg");
        var got = EggAlzArchiveService.SplitVolumeCandidates(first).Select(Path.GetFileName).OrderBy(x => x, StringComparer.Ordinal);
        Assert.Equal(new[] { "a.vol2.EGG", "b.egg" }, got);
    }

    /// <summary>[B9 고침] 크기 0 항목(폴더 항목·빈 파일)이 든 EGG 도 목록·해제가 원본과 같아야 한다(알집처럼).</summary>
    [Fact]
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
