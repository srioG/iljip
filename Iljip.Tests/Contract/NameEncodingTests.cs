using System.Text;
using Iljip.Services;
using Iljip.Services.Editing;
using Iljip.Tests.TestSupport;

namespace Iljip.Tests.Contract;

/// <summary>
/// [W2-09 · I2-10] 압축 안 이름의 글자 방식과 맥(NFD) 이름.
/// - 이름 방식은 압축 파일 하나에 하나로 정한다(<see cref="ArchiveNameEncodingDetector"/>): 한국어(CP949) 먼저, 그다음 일본어(Shift-JIS)·중국어(GBK).
///   한국어 이름은 예전과 글자 하나 다르지 않게 보여야 한다(KoreanFileNameDecoderTests·반디집 픽스처가 그대로 지킴).
/// - 맥에서 만든 ZIP 의 자모 분리(NFD) 한글 이름은 NFC(완성형)로 보이고 NFC 로 풀린다. 원래(NFD) 이름으로 골라도, 화면(NFC) 이름으로 골라도 풀린다.
/// </summary>
public class NameEncodingTests
{
    private static readonly Encoding Cp949 = Encoding.GetEncoding(949);
    private static readonly Encoding Cp932 = Encoding.GetEncoding(932);
    private static readonly Encoding Cp936 = Encoding.GetEncoding(936);
    private static readonly Encoding Cp437 = Encoding.GetEncoding(437);

    private static string Nfd(string s) => s.Normalize(NormalizationForm.FormD);

    // ───────────────────────────── 방식 정하기(판별기) ─────────────────────────────

    [Fact]
    public void CP949_한글_이름은_한국어로_정한다()
    {
        var names = new[] { "보고서.txt", "하위폴더/견적서 최종.hwp", "회의록_2026.docx" }.Select(Cp949.GetBytes);
        Assert.Equal(NameEncoding.Korean, ArchiveNameEncodingDetector.Detect(names));
    }

    /// <summary>짧은 이름 하나뿐이면 CP949·GBK 둘 다 그럴듯할 수 있다 — 한국어가 먼저(설계: 한국어 우선).</summary>
    [Theory]
    [InlineData("보고서")]
    [InlineData("보고서.txt")]
    [InlineData("견적")]
    public void 애매한_짧은_CP949_이름은_한국어로_본다(string name)
    {
        Assert.Equal(NameEncoding.Korean, ArchiveNameEncodingDetector.Detect(new[] { Cp949.GetBytes(name) }));
    }

    [Fact]
    public void ShiftJIS_이름은_일본어로_정한다()
    {
        var names = new[] { "テスト資料.txt", "報告書/会議メモ.txt" }.Select(Cp932.GetBytes);
        Assert.Equal(NameEncoding.Japanese, ArchiveNameEncodingDetector.Detect(names));
        // 한 이름뿐이어도
        Assert.Equal(NameEncoding.Japanese, ArchiveNameEncodingDetector.Detect(new[] { Cp932.GetBytes("テスト資料.txt") }));
    }

    [Fact]
    public void GBK_이름은_중국어로_정한다()
    {
        var names = new[] { "测试文件.txt", "报告/数据统计.xlsx", "会议记录.docx" }.Select(Cp936.GetBytes);
        Assert.Equal(NameEncoding.Chinese, ArchiveNameEncodingDetector.Detect(names));
        Assert.Equal(NameEncoding.Chinese, ArchiveNameEncodingDetector.Detect(new[] { Cp936.GetBytes("测试文件.txt") }));
    }

    /// <summary>
    /// [W2-09 검증 지적] 한자가 많은 한국어 이름(CP949)도 한국어 — 예전엔 한글 비율로 못 정하고 GBK(중국어)로 넘어가, 예전 규칙이 바르게 보이던 한글 이름('보고서.txt')까지
    /// '焊绊辑.txt' 처럼 깨졌다. 이제 GBK 로 읽어 사용자 정의 글자(PUA)·드문 2급 한자가 나오면 중국어가 아니고, 자주 쓰는 한글이 둘 이상이면 한자가 많아도 한국어다.
    /// 이름은 글자 하나 다르지 않게 풀린다(한자뿐인 이름 '會議錄.hwp' 도 — 예전 규칙은 CP437 로 깨졌음).
    /// </summary>
    [Theory]
    [InlineData("株式會社 일진 見積書.xlsx", "보고서.txt")]
    [InlineData("보고서.txt", "會議錄.hwp", "見積書.xlsx")]
    [InlineData("보고서.txt", "회의록.hwp", "見積書.xlsx")]
    [InlineData("株式會社 일진.xlsx")]
    [InlineData("(株)일진 見積書.xlsx")]
    [InlineData("第1回 이사회 會議錄.hwp")]
    [InlineData("尹_택배 經理.docx")]
    [InlineData("東京 사진 全羅.docx")]
    [InlineData("經理 새 폴더 (株).txt", "目錄.docx")]
    [InlineData("會議錄.hwp", "見積書.xlsx")]
    public void 한자가_많은_한국어_이름도_한국어로_정하고_그대로_푼다(params string[] names)
    {
        var raw = names.Select(Cp949.GetBytes).ToList();
        Assert.Equal(NameEncoding.Korean, ArchiveNameEncodingDetector.Detect(raw));
        var d = NameDecoder.For(raw);
        Assert.Equal(names, raw.Select(b => d.Decode(b, 0, b.Length)));
        // 한글이 든 이름은 예전 규칙과도 같다(한국어 우선 — 예전에 바르게 보이던 이름은 계속 바르게)
        foreach (var (name, b) in names.Zip(raw))
            if (name.Any(c => c is >= '가' and <= '힣'))
                Assert.Equal(KoreanFileNameDecoder.DecodeBytes(b, 0, b.Length), d.Decode(b, 0, b.Length));
    }

    /// <summary>진짜 중국어 이름에 드문 2급 한자(사람 이름의 '鑫' 등)가 조금 섞여도 중국어 그대로(2급 한자가 한자의 25% 이하).</summary>
    [Fact]
    public void 드문_한자가_조금_섞인_중국어_이름은_중국어()
    {
        var names = new[] { "张鑫_简历.docx", "会议记录.docx", "数据统计.xlsx" };
        var raw = names.Select(Cp936.GetBytes).ToList();
        Assert.Equal(NameEncoding.Chinese, ArchiveNameEncodingDetector.Detect(raw));
        var d = NameDecoder.For(raw);
        Assert.Equal(names, raw.Select(b => d.Decode(b, 0, b.Length)));
    }

    [Fact]
    public void 표시_없는_UTF8_이름은_UTF8_ASCII뿐이면_정하지_않는다()
    {
        Assert.Equal(NameEncoding.Utf8, ArchiveNameEncodingDetector.Detect(new[] { Encoding.UTF8.GetBytes("맥에서_만든.txt"), Encoding.ASCII.GetBytes("a.txt") }));
        Assert.Equal(NameEncoding.Auto, ArchiveNameEncodingDetector.Detect(new[] { Encoding.ASCII.GetBytes("plain.txt"), Encoding.ASCII.GetBytes("dir/b.txt") }));
        Assert.Equal(NameEncoding.Auto, ArchiveNameEncodingDetector.Detect(Array.Empty<byte[]>()));
    }

    /// <summary>한글(CP949)과 옛 서양(CP437 'café')이 섞이면 하나로 못 정한다 — 이름마다 예전 규칙(둘 다 바르게 보임, KoreanFileNameDecoderTests).</summary>
    [Fact]
    public void 섞인_옛_이름은_정하지_않고_이름마다_예전_규칙()
    {
        var names = new[] { Cp949.GetBytes("완성형_한글.txt"), Cp437.GetBytes("café.txt") };
        Assert.Equal(NameEncoding.Auto, ArchiveNameEncodingDetector.Detect(names));
        var d = NameDecoder.For(names);
        Assert.Equal("완성형_한글.txt", d.Decode(names[0], 0, names[0].Length));
        Assert.Equal("café.txt", d.Decode(names[1], 0, names[1].Length));
    }

    /// <summary>정한 방식이 있어도 UTF-8 표시가 있는 이름·UTF-8 로 풀리는 이름은 UTF-8, 정한 방식으로 못 푸는 이름은 예전 규칙(그 이름만).</summary>
    [Fact]
    public void 정한_방식이어도_UTF8_이름은_UTF8로()
    {
        var d = new NameDecoder(NameEncoding.Japanese);
        byte[] utf8 = Encoding.UTF8.GetBytes("유니코드.txt");
        Assert.Equal("유니코드.txt", d.Decode(utf8, 0, utf8.Length, utf8Flagged: true));
        Assert.Equal("유니코드.txt", d.Decode(utf8, 0, utf8.Length));
        byte[] sjis = Cp932.GetBytes("テスト.txt");
        Assert.Equal("テスト.txt", d.Decode(sjis, 0, sjis.Length));
    }

    // ───────────────────────────── 실제 압축 파일(목록·풀기·고르기·고치기) ─────────────────────────────

    public static TheoryData<string, string[]> LegacyZips => new()
    {
        { "932", new[] { "テスト資料.txt", "報告書/会議メモ.txt", "plain.txt" } },
        { "936", new[] { "测试文件.txt", "报告/数据统计.xlsx", "plain.txt" } },
        { "949", new[] { "보고서.txt", "하위폴더/견적서 최종.hwp", "plain.txt" } },
        // W2-09 검증 지적: 한자가 많은 한국어 이름(예전엔 압축 전체가 중국어로 읽혀 '보고서' 까지 깨짐)
        { "949", new[] { "株式會社 일진 見積書.xlsx", "會議錄/보고서.txt", "plain.txt" } },
    };

    private static Encoding Cp(string codePage) => Encoding.GetEncoding(int.Parse(codePage));

    private static string WriteLegacyZip(TempDir tmp, string codePage, string[] names)
    {
        string arc = tmp.P($"names_{codePage}.zip");
        RawZip.Write(arc, names.Select((n, i) => new RawZip.Item(Cp(codePage).GetBytes(n), new[] { (byte)(i + 1) }, Utf8Flag: false)).ToArray());
        return arc;
    }

    /// <summary>
    /// UTF-8 표시 없는 ZIP: 일본어(Shift-JIS)·중국어(GBK) 이름이 그 글자로 보이고 그 이름으로 풀린다. 한국어(CP949)는 예전과 같다.
    /// 이름 → 내용(1바이트씩 다름)까지 맞춰 본다(이름이 맞아도 다른 항목과 뒤바뀌면 실패).
    /// </summary>
    [Theory]
    [MemberData(nameof(LegacyZips))]
    public async Task 옛_방식_이름_ZIP이_그_글자로_보이고_그_이름으로_풀린다(string codePage, string[] names)
    {
        using var tmp = new TempDir("enc");
        string arc = WriteLegacyZip(tmp, codePage, names);
        var svc = Svc.For(arc);
        var listed = (await svc.ListEntriesAsync(arc)).Where(e => !e.IsDirectory).Select(e => e.Path).ToList();
        Assert.Equal(names, listed);

        string dest = tmp.P("out");
        await svc.ExtractAsync(arc, dest);
        Assert.Equal(names.OrderBy(x => x, StringComparer.Ordinal), TreeAssert.Files(dest));
        for (int i = 0; i < names.Length; i++)
            Assert.Equal(new[] { (byte)(i + 1) }, File.ReadAllBytes(Path.Combine(dest, names[i])));
    }

    /// <summary>목록에 보인 이름으로 골라 풀면 그 항목만 풀린다(일본어·중국어 이름도).</summary>
    [Theory]
    [MemberData(nameof(LegacyZips))]
    public async Task 옛_방식_이름으로_골라_풀린다(string codePage, string[] names)
    {
        using var tmp = new TempDir("encsel");
        string arc = WriteLegacyZip(tmp, codePage, names);
        string dest = tmp.P("out");
        await Svc.For(arc).ExtractEntriesAsync(arc, dest, new[] { names[1] });
        Assert.Equal(new[] { names[1] }, TreeAssert.Files(dest));
        Assert.Equal(new byte[] { 2 }, File.ReadAllBytes(Path.Combine(dest, names[1])));
    }

    /// <summary>
    /// 압축 고치기(ZIP 원시 읽기 — <see cref="ZipRawRewriter"/>)도 목록과 같은 이름으로 항목을 찾는다: 일본어·중국어 이름 항목을 지우고 이름을 바꾼 뒤에도
    /// 남은 항목 이름·내용이 그대로다(원래 바이트는 그대로 옮김 — 새 이름만 UTF-8).
    /// </summary>
    [Theory]
    [MemberData(nameof(LegacyZips))]
    public async Task 옛_방식_이름_ZIP을_고쳐도_이름이_맞다(string codePage, string[] names)
    {
        using var tmp = new TempDir("encedit");
        string arc = WriteLegacyZip(tmp, codePage, names);
        var editor = ArchiveServiceLocator.EditorFor(arc)!;
        await editor.EditAsync(arc, new EditOp[] { new EditOp.Delete(new[] { names[0] }), new EditOp.Rename(names[2], "새 이름.txt") }, null, null, CancellationToken.None);

        var listed = (await Svc.For(arc).ListEntriesAsync(arc)).Where(e => !e.IsDirectory).Select(e => e.Path).OrderBy(x => x, StringComparer.Ordinal).ToList();
        Assert.Equal(new[] { names[1], "새 이름.txt" }.OrderBy(x => x, StringComparer.Ordinal), listed);
        string dest = tmp.P("out");
        await Svc.For(arc).ExtractAsync(arc, dest);
        Assert.Equal(new byte[] { 2 }, File.ReadAllBytes(Path.Combine(dest, names[1])));
        Assert.Equal(new byte[] { 3 }, File.ReadAllBytes(Path.Combine(dest, "새 이름.txt")));
    }

    /// <summary>UTF-8 표시가 있는 이름은 섞여 있어도 UTF-8 그대로(일본어 압축으로 정해져도).</summary>
    [Fact]
    public async Task 표시_있는_UTF8_이름과_ShiftJIS_이름이_섞여도_둘_다_바르다()
    {
        using var tmp = new TempDir("encmix");
        string arc = tmp.P("mix.zip");
        RawZip.Write(arc,
            new RawZip.Item(Encoding.UTF8.GetBytes("한글_유니코드.txt"), new byte[] { 1 }, Utf8Flag: true),
            new RawZip.Item(Cp932.GetBytes("テスト資料.txt"), new byte[] { 2 }, Utf8Flag: false));
        var listed = (await Svc.For(arc).ListEntriesAsync(arc)).Select(e => e.Path).ToList();
        Assert.Equal(new[] { "한글_유니코드.txt", "テスト資料.txt" }, listed);
    }

    /// <summary>TAR(이름 표시가 없음)도 같은 판별로: Shift-JIS·GBK 이름이 그 글자로 보이고 풀린다.</summary>
    [Theory]
    [InlineData("932", "テスト資料.txt", "報告書/会議メモ.txt")]
    [InlineData("936", "测试文件.txt", "报告/数据统计.xlsx")]
    public async Task 옛_방식_이름_TAR도_그_글자로_보인다(string codePage, string a, string b)
    {
        using var tmp = new TempDir("enctar");
        string arc = tmp.P("names.tar");
        RawTar.Write(arc,
            new RawTar.Entry(a, new byte[] { 1 }, RawName: Cp(codePage).GetBytes(a)),
            new RawTar.Entry(b, new byte[] { 2 }, RawName: Cp(codePage).GetBytes(b)));
        var listed = (await Svc.For(arc).ListEntriesAsync(arc)).Where(e => !e.IsDirectory).Select(e => e.Path).ToList();
        Assert.Equal(new[] { a, b }, listed);
        string dest = tmp.P("out");
        await Svc.For(arc).ExtractAsync(arc, dest);
        Assert.Equal(new byte[] { 1 }, File.ReadAllBytes(Path.Combine(dest, a)));
        Assert.Equal(new byte[] { 2 }, File.ReadAllBytes(Path.Combine(dest, b)));
    }

    // ───────────────────────────── 맥(NFD) 이름 ─────────────────────────────

    private static string WriteNfdZip(TempDir tmp, bool utf8Flag)
    {
        string arc = tmp.P(utf8Flag ? "nfd_flag.zip" : "nfd_noflag.zip");
        RawZip.Write(arc,
            new RawZip.Item(Encoding.UTF8.GetBytes(Nfd("맥_샘플/") ), Array.Empty<byte>(), utf8Flag),
            new RawZip.Item(Encoding.UTF8.GetBytes(Nfd("맥_샘플/보고서.txt")), new byte[] { 1 }, utf8Flag),
            new RawZip.Item(Encoding.UTF8.GetBytes(Nfd("맥_샘플/하위/견적서.txt")), new byte[] { 2 }, utf8Flag),
            new RawZip.Item(Encoding.UTF8.GetBytes("english.txt"), new byte[] { 3 }, utf8Flag));
        return arc;
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task NFD_이름은_NFC로_보인다(bool utf8Flag)
    {
        using var tmp = new TempDir("nfdlist");
        string arc = WriteNfdZip(tmp, utf8Flag);
        var listed = (await Svc.For(arc).ListEntriesAsync(arc)).Select(e => e.Path.TrimEnd('/')).ToList();
        Assert.Equal(new[] { "맥_샘플", "맥_샘플/보고서.txt", "맥_샘플/하위/견적서.txt", "english.txt" }, listed);
        Assert.All(listed, p => Assert.True(p.IsNormalized(NormalizationForm.FormC), TreeAssert.Escape(p)));
    }

    /// <summary>화면(NFC) 이름으로 골라도, 원래(NFD) 이름으로 골라도 같은 항목이 NFC 이름으로 풀린다. 폴더를 고르면 그 안이 다 풀린다.</summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task NFD_이름_항목을_NFC_이름으로도_NFD_이름으로도_골라_푼다(bool nfdKey)
    {
        using var tmp = new TempDir("nfdsel");
        string arc = WriteNfdZip(tmp, utf8Flag: false);
        string key = nfdKey ? Nfd("맥_샘플/보고서.txt") : "맥_샘플/보고서.txt";
        string dest = tmp.P("out");
        await Svc.For(arc).ExtractEntriesAsync(arc, dest, new[] { key });
        Assert.Equal(new[] { "맥_샘플/보고서.txt" }, TreeAssert.Files(dest));
        Assert.Equal(new byte[] { 1 }, File.ReadAllBytes(Path.Combine(dest, "맥_샘플", "보고서.txt")));

        string dest2 = tmp.P("out2");
        await Svc.For(arc).ExtractEntriesAsync(arc, dest2, new[] { nfdKey ? Nfd("맥_샘플/하위") : "맥_샘플/하위" });
        Assert.Equal(new[] { "맥_샘플/하위/견적서.txt" }, TreeAssert.Files(dest2));
    }

    /// <summary>
    /// NFC 로 모으면 이름이 같아지는 두 항목(완성형 '보고서.txt' 와 자모 분리 '보고서.txt')은 둘 다 풀린다 — 하나는 '보고서 (1).txt'.
    /// 어느 쪽도 다른 쪽을 덮어쓰지 않는다(내용 둘 다 남음).
    /// </summary>
    [Fact]
    public async Task NFC로_같아지는_두_항목은_둘_다_풀리고_하나는_번호가_붙는다()
    {
        using var tmp = new TempDir("nfddup");
        string arc = tmp.P("dup.zip");
        RawZip.Write(arc,
            new RawZip.Item(Encoding.UTF8.GetBytes("보고서.txt"), new byte[] { 1 }, Utf8Flag: true),
            new RawZip.Item(Encoding.UTF8.GetBytes(Nfd("보고서.txt")), new byte[] { 2 }, Utf8Flag: true));
        var listed = (await Svc.For(arc).ListEntriesAsync(arc)).Select(e => e.Path).ToList();
        Assert.Equal(new[] { "보고서.txt", "보고서.txt" }, listed);

        string dest = tmp.P("out");
        await Svc.For(arc).ExtractAsync(arc, dest);
        Assert.Equal(new[] { "보고서 (1).txt", "보고서.txt" }, TreeAssert.Files(dest).Select(f => f.Normalize(NormalizationForm.FormC)).OrderBy(x => x, StringComparer.Ordinal));
        Assert.All(TreeAssert.Files(dest), f => Assert.True(f.IsNormalized(NormalizationForm.FormC), TreeAssert.Escape(f)));
        var contents = TreeAssert.Files(dest).Select(f => File.ReadAllBytes(Path.Combine(dest, f))[0]).OrderBy(x => x).ToList();
        Assert.Equal(new byte[] { 1, 2 }, contents);
    }

    /// <summary>
    /// [W2-09 검증 지적] NFC 로 같아지는 두 파일(완성형·자모 분리 '보고서.txt')은 목록에서 겉보기 이름이 같은 두 줄 — 고치기는 어느 것인지 알 수 없다.
    /// 하나만 골라 지우면(키 하나) 둘 다 지우지 않고 알리고(원본 그대로), 둘 다 고르면(키 둘) 둘 다 지운다. 이름 바꾸기는 둘 다 같은 새 이름이 되지 않게 알리고 멈춘다.
    /// </summary>
    [Fact]
    public async Task NFC로_같아지는_두_파일은_하나만_골라_고치지_않는다()
    {
        using var tmp = new TempDir("nfdedit");
        string arc = tmp.P("dup.zip");
        RawZip.Write(arc,
            new RawZip.Item(Encoding.UTF8.GetBytes("보고서.txt"), new byte[] { 1 }, Utf8Flag: true),
            new RawZip.Item(Encoding.UTF8.GetBytes(Nfd("보고서.txt")), new byte[] { 2 }, Utf8Flag: true),
            new RawZip.Item(Encoding.UTF8.GetBytes("기타.txt"), new byte[] { 3 }, Utf8Flag: true));
        byte[] original = File.ReadAllBytes(arc);
        var editor = ArchiveServiceLocator.EditorFor(arc)!;

        var del = await Assert.ThrowsAsync<ArchiveEditException>(() =>
            editor.EditAsync(arc, new EditOp[] { new EditOp.Delete(new[] { "보고서.txt" }) }, null, null, CancellationToken.None));
        Assert.Contains("2개", del.Message);
        Assert.Equal(original, File.ReadAllBytes(arc));

        var ren = await Assert.ThrowsAsync<ArchiveEditException>(() =>
            editor.EditAsync(arc, new EditOp[] { new EditOp.Rename("보고서.txt", "새이름.txt") }, null, null, CancellationToken.None));
        Assert.Contains("2개", ren.Message);
        Assert.Equal(original, File.ReadAllBytes(arc));

        // 둘 다 고르면(같은 키 두 번 — 화면이 고른 줄마다 하나씩 넘김) 둘 다 지운다
        await editor.EditAsync(arc, new EditOp[] { new EditOp.Delete(new[] { "보고서.txt", Nfd("보고서.txt") }) }, null, null, CancellationToken.None);
        Assert.Equal(new[] { "기타.txt" }, (await Svc.For(arc).ListEntriesAsync(arc)).Select(e => e.Path));
    }

    /// <summary>
    /// 찾은 결과에서 폴더와 그 안 파일을 함께 골라 지우면(W2-09 — 찾기로 여러 폴더의 항목이 한 목록에 보임) 폴더째 지워진다 — 안 파일을 '찾지 못했어요'로 멈추지 않는다.
    /// 같은 이름 두 파일 중 하나를 골랐어도 그 위 폴더를 같이 골랐으면 폴더째 지운다(어느 것인지 물을 필요가 없음).
    /// </summary>
    [Fact]
    public async Task 폴더와_그_안_파일을_함께_골라_지워도_된다()
    {
        using var tmp = new TempDir("delnest");
        string arc = tmp.P("nest.zip");
        RawZip.Write(arc,
            new RawZip.Item(Encoding.UTF8.GetBytes("문서/하위/보고서.txt"), new byte[] { 1 }, Utf8Flag: true),
            new RawZip.Item(Encoding.UTF8.GetBytes(Nfd("문서/하위/보고서.txt")), new byte[] { 2 }, Utf8Flag: true),
            new RawZip.Item(Encoding.UTF8.GetBytes("문서/남김.txt"), new byte[] { 3 }, Utf8Flag: true));
        var editor = ArchiveServiceLocator.EditorFor(arc)!;
        await editor.EditAsync(arc, new EditOp[] { new EditOp.Delete(new[] { "문서/하위/보고서.txt", "문서/하위" }) }, null, null, CancellationToken.None);
        Assert.Equal(new[] { "문서/남김.txt" }, (await Svc.For(arc).ListEntriesAsync(arc)).Where(e => !e.IsDirectory).Select(e => e.Path));
    }

    // ───────────────────────────── 풀 때 파일·폴더 이름이 겹침 ─────────────────────────────

    /// <summary>
    /// [W2-09 검증 지적] NFD 파일 '보고서' 다음에 NFC 폴더 '보고서/' 가 오면(NFC 로 같은 이름) 예전엔 폴더를 못 만들어 풀기가 멈추고 앞 파일이 남았다.
    /// 이제 폴더가 먼저 올 때와 같은 결과: 폴더 '보고서'(안의 파일까지) + 파일 '보고서 (1)'. 이름이 똑같은 파일·폴더(맥이 아닌 압축)도 같다. 내용은 서로 뒤바뀌지 않는다.
    /// </summary>
    [Theory]
    [InlineData(true, false)]
    [InlineData(false, false)]
    [InlineData(true, true)]
    public async Task 파일_뒤에_같은_이름_폴더가_와도_풀리고_파일은_번호가_붙는다(bool nfdFile, bool folderFirst)
    {
        using var tmp = new TempDir("filedir");
        string arc = tmp.P("fd.zip");
        var file = new RawZip.Item(Encoding.UTF8.GetBytes(nfdFile ? Nfd("보고서") : "보고서"), new byte[] { 1 }, Utf8Flag: true);
        var dir = new RawZip.Item(Encoding.UTF8.GetBytes("보고서/"), Array.Empty<byte>(), Utf8Flag: true);
        var inner = new RawZip.Item(Encoding.UTF8.GetBytes("보고서/안.txt"), new byte[] { 2 }, Utf8Flag: true);
        RawZip.Write(arc, folderFirst ? new[] { dir, inner, file } : new[] { file, dir, inner });

        string dest = tmp.P("out");
        await Svc.For(arc).ExtractAsync(arc, dest);
        Assert.Equal(new[] { "보고서 (1)", "보고서/안.txt" }, TreeAssert.Files(dest));
        Assert.Equal(new byte[] { 1 }, File.ReadAllBytes(Path.Combine(dest, "보고서 (1)")));
        Assert.Equal(new byte[] { 2 }, File.ReadAllBytes(Path.Combine(dest, "보고서", "안.txt")));
    }

    /// <summary>풀 곳에 원래 있던 사용자 파일 '보고서' 는 옮기지 않는다 — 같은 이름 폴더를 못 만들면 예전처럼 알리고(쉬운 말), 사용자 파일은 그대로.</summary>
    [Fact]
    public async Task 원래_있던_파일과_같은_이름_폴더는_사용자_파일을_건드리지_않는다()
    {
        using var tmp = new TempDir("filedir2");
        string arc = tmp.P("fd.zip");
        RawZip.Write(arc, new RawZip.Item(Encoding.UTF8.GetBytes("보고서/안.txt"), new byte[] { 2 }, Utf8Flag: true));
        string dest = tmp.Dir("out");
        File.WriteAllBytes(Path.Combine(dest, "보고서"), new byte[] { 9 });

        var ex = await Assert.ThrowsAnyAsync<Exception>(() => Svc.For(arc).ExtractAsync(arc, dest));
        Assert.Contains("같은 이름의 파일이나 폴더가 이미 있어", ErrorMessages.ToUserMessage(ex));
        Assert.Equal(new[] { "보고서" }, TreeAssert.Files(dest));
        Assert.Equal(new byte[] { 9 }, File.ReadAllBytes(Path.Combine(dest, "보고서")));
    }

    /// <summary>7z.dll 로 여는 형식(7z)도: 자모 분리 이름으로 만든 7z 가 NFC 로 보이고 NFC 로 풀린다.</summary>
    [Fact]
    public async Task NFD_이름_7z도_NFC로_보이고_풀린다()
    {
        using var tmp = new TempDir("nfd7z");
        string src = tmp.P("src");
        string folder = Path.Combine(src, Nfd("맥_샘플"));
        Directory.CreateDirectory(folder);
        File.WriteAllBytes(Path.Combine(folder, Nfd("보고서.txt")), new byte[] { 7 });
        string arc = tmp.P("nfd.7z");
        await Svc.For(arc).CompressAsync(new[] { folder }, arc);

        var listed = (await Svc.For(arc).ListEntriesAsync(arc)).Select(e => e.Path.TrimEnd('/')).OrderBy(x => x, StringComparer.Ordinal).ToList();
        Assert.Equal(new[] { "맥_샘플", "맥_샘플/보고서.txt" }, listed);
        string dest = tmp.P("out");
        await Svc.For(arc).ExtractAsync(arc, dest);
        Assert.Equal(new[] { "맥_샘플/보고서.txt" }, TreeAssert.Files(dest));
        string dest2 = tmp.P("out2");
        await Svc.For(arc).ExtractEntriesAsync(arc, dest2, new[] { "맥_샘플/보고서.txt" });
        Assert.Equal(new[] { "맥_샘플/보고서.txt" }, TreeAssert.Files(dest2));
    }

    /// <summary>EGG(EggDotNet)도: 자모 분리 이름이 NFC 로 보이고 NFC 로 풀린다(화면 이름으로 골라도).</summary>
    [Fact]
    public async Task NFD_이름_EGG도_NFC로_보이고_풀린다()
    {
        using var tmp = new TempDir("nfdegg");
        string arc = tmp.P("nfd.egg");
        RawEgg.Write(arc, new[] { new RawEgg.Item(Nfd("맥_샘플/보고서.txt"), new byte[] { 5 }) });
        var only = Assert.Single(await Svc.For(arc).ListEntriesAsync(arc));
        Assert.Equal("맥_샘플/보고서.txt", only.Path);
        string dest = tmp.P("out");
        await Svc.For(arc).ExtractEntriesAsync(arc, dest, new[] { "맥_샘플/보고서.txt" });
        Assert.Equal(new[] { "맥_샘플/보고서.txt" }, TreeAssert.Files(dest));
    }
}
