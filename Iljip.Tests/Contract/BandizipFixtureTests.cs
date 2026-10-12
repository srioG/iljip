using Iljip.Services;
using Iljip.Tests.TestSupport;

namespace Iljip.Tests.Contract;

/// <summary>
/// [범위 1] 반디집이 만든 픽스처(manifest.json)를 일집이 반디집과 '같게' 읽고 푸는지.
/// 반디집을 지운 뒤에는 픽스처를 다시 만들 수 없으므로 픽스처 파일 자체가 원본이다.
/// </summary>
public class BandizipFixtureTests
{
    public static TheoryData<string> ListOk => FixtureSets.Data(FixtureSets.ListOk);
    public static TheoryData<string> ExtractOk => FixtureSets.Data(FixtureSets.ExtractOk);
    public static TheoryData<string> SevenZip => FixtureSets.Data(FixtureSets.SevenZipBandizip);
    public static TheoryData<string> KoreanPw => FixtureSets.Data(FixtureSets.KoreanPasswordZip);
    public static TheoryData<string> Nfd => FixtureSets.Data(FixtureSets.Nfd);

    /// <summary>목록(경로·크기·폴더 여부)이 반디집 기대값과 정확히 같다 — 한글 이름(CP949/UTF-8) 포함.</summary>
    [Theory]
    [MemberData(nameof(ListOk))]
    public async Task 목록이_반디집_기대값과_같다(string id)
    {
        var fx = FixtureCatalog.Get(id);
        var entries = await Svc.For(fx.FullPath).ListEntriesAsync(fx.FullPath, fx.Password);
        TreeAssert.ListEquals(entries, fx.Expected, context: id);
    }

    /// <summary>
    /// 목록의 수정 시각이 원본 시각과 같다 — 시간대 밀림 방지. 기준은 형식마다 다르다(시간대와 무관하게 맞도록):
    /// tar·7z 는 절대 시각 2026-01-01T18:04:06Z, ZIP 은 DOS 벽시계 2026-01-02 03:04:06(또는 NTFS 추가필드의 절대 시각).
    /// </summary>
    [Theory]
    [MemberData(nameof(ListOk))]
    public async Task 목록의_파일_수정시각이_원본과_같다(string id)
    {
        var fx = FixtureCatalog.Get(id);
        var entries = await Svc.For(fx.FullPath).ListEntriesAsync(fx.FullPath, fx.Password);
        var bad = new List<string>();
        foreach (var e in entries.Where(e => !e.IsDirectory))
        {
            if (e.LastModified is null) { bad.Add($"{e.Path}: 시각 없음"); continue; }
            if (MtimeExpect.CheckListed(e.LastModified.Value, fx) is { } why) bad.Add($"{e.Path}: {why}");
        }
        Assert.True(bad.Count == 0, $"{id} 수정 시각 불일치 {bad.Count}건:\n  " + string.Join("\n  ", bad));
    }

    /// <summary>전체 해제 결과(폴더·파일·sha256)가 반디집 기대값과 정확히 같고, 기대 밖의 파일이 생기지 않는다.</summary>
    [Theory]
    [MemberData(nameof(ExtractOk))]
    public async Task 전체_해제_결과가_반디집_기대값과_같다(string id)
    {
        var fx = FixtureCatalog.Get(id);
        using var tmp = new TempDir("fx");
        string dest = tmp.P("out");
        await Svc.For(fx.FullPath).ExtractAsync(fx.FullPath, dest, fx.Password);
        TreeAssert.ExtractedTreeEquals(dest, fx.Expected, context: id);
    }

    /// <summary>풀린 파일의 수정 시각이 원본 시각으로 복원된다(반디집과 같음).</summary>
    [Theory]
    [MemberData(nameof(ExtractOk))]
    public async Task 풀린_파일의_수정시각이_원본과_같다(string id)
    {
        var fx = FixtureCatalog.Get(id);
        using var tmp = new TempDir("mt");
        string dest = tmp.P("out");
        await Svc.For(fx.FullPath).ExtractAsync(fx.FullPath, dest, fx.Password);
        var bad = Directory.EnumerateFiles(dest, "*", SearchOption.AllDirectories)
            .Select(f => MtimeExpect.CheckFile(f, fx) is { } why ? $"{Path.GetRelativePath(dest, f)}: {why}" : null)
            .OfType<string>().ToList();
        Assert.True(bad.Count == 0, $"{id} 풀린 파일 수정 시각 불일치 {bad.Count}건:\n  " + string.Join("\n  ", bad));
    }

    /// <summary>
    /// 반디집 7z(일반·헤더 암호·한글 암호): 빈 파일을 뺀 나머지는 선택 해제로 내용이 정확히 풀린다.
    /// (전체 해제는 아래 두 테스트 — 예전엔 빈 파일에서 실패했던 B1, SharpCompress 0.50 으로 고쳐짐)
    /// </summary>
    [Theory]
    [MemberData(nameof(SevenZip))]
    public async Task 반디집_7z는_빈파일을_뺀_나머지가_정확히_풀린다(string id)
    {
        var fx = FixtureCatalog.Get(id);
        var nonEmpty = fx.ExpectedFiles.Where(e => e.Size > 0).ToList();
        using var tmp = new TempDir("7z");
        string dest = tmp.P("out");
        await Svc.For(fx.FullPath).ExtractEntriesAsync(fx.FullPath, dest, nonEmpty.Select(e => e.Path).ToList(), fx.Password);

        var snap = TreeAssert.Snapshot(dest);
        foreach (var e in nonEmpty)
            Assert.True(snap.TryGetValue(e.Path, out var sha) && sha == e.Sha256, $"{id}: {e.Path} 내용 불일치/없음");
        Assert.Equal(nonEmpty.Count, snap.Values.Count(v => v is not null));
    }

    /// <summary>[B1 고침] 반디집 7z 전체 해제 — 빈 파일까지 반디집과 똑같이 풀려야 한다.</summary>
    [Theory]
    [InlineData("7z_default")]
    public async Task 반디집_7z_전체_해제가_반디집과_같다(string id)
    {
        var fx = FixtureCatalog.Get(id);
        using var tmp = new TempDir("7zb");
        string dest = tmp.P("out");
        await Svc.For(fx.FullPath).ExtractAsync(fx.FullPath, dest, fx.Password);
        TreeAssert.ExtractedTreeEquals(dest, fx.Expected, context: id);
    }

    /// <summary>
    /// [B1 고침] 헤더(파일명) 암호 7z 를 서비스에 맞는 암호를 직접 주고 전체 해제하면 반디집과 같아야 한다.
    /// 예전엔 B1(빈 파일) 때문에만 실패했다. ★ I1-7(화면이 열기 단계에서 암호를 안 물음)과는 무관 —
    /// 이 테스트가 통과해도 I1-7 이 해결된 것이 아니다(I1-7 은 자동 테스트 없음, MANUAL_CHECKLIST 2절).
    /// </summary>
    [Theory]
    [InlineData("7z_header_encrypted")]
    [InlineData("7z_header_encrypted_korean_pw")]
    public async Task 헤더암호_7z_전체_해제가_반디집과_같다(string id)
    {
        var fx = FixtureCatalog.Get(id);
        using var tmp = new TempDir("7zh");
        string dest = tmp.P("out");
        await Svc.For(fx.FullPath).ExtractAsync(fx.FullPath, dest, fx.Password);
        TreeAssert.ExtractedTreeEquals(dest, fx.Expected, context: id);
    }

    /// <summary>
    /// [B3 고침] 반디집이 한글 암호(테스트암호)로 만든 ZipCrypto·AES ZIP 을 맞는 암호로 풀 수 있어야 한다.
    /// 반디집은 암호를 CP949 바이트로 쓴다 — ZipPasswordProbe 가 CP949 로 판정하면 ZipCrypto 는 엔진에 바이트 그대로, AES 는 WinZipAes 가 직접 푼다.
    /// </summary>
    [Theory]
    [MemberData(nameof(KoreanPw))]
    public async Task 반디집_한글암호_ZIP이_풀린다(string id)
    {
        var fx = FixtureCatalog.Get(id);
        using var tmp = new TempDir("kpw");
        string dest = tmp.P("out");
        await Svc.For(fx.FullPath).ExtractAsync(fx.FullPath, dest, fx.Password);
        TreeAssert.ExtractedTreeEquals(dest, fx.Expected, context: id);
    }

    /// <summary>NFD(자모 분리) 이름 ZIP 도 '내용'은 정확히 풀린다 — 이름을 NFC 로 바꿔 비교(이름 자체는 I2-10).</summary>
    [Theory]
    [MemberData(nameof(Nfd))]
    public async Task NFD_이름_ZIP도_내용은_정확히_풀린다(string id)
    {
        var fx = FixtureCatalog.Get(id);
        using var tmp = new TempDir("nfd");
        string dest = tmp.P("out");
        await Svc.For(fx.FullPath).ExtractAsync(fx.FullPath, dest, fx.Password);
        TreeAssert.ExtractedTreeEquals(dest, fx.Expected, nfc: true, context: id);
    }

    /// <summary>[I2-10] NFD 이름 ZIP 의 목록·풀린 이름이 반디집처럼 NFC(완성형)여야 한다.</summary>
    [Theory(Skip = Known.I2_10)]
    [MemberData(nameof(Nfd))]
    public async Task NFD_이름이_NFC로_보이고_NFC로_풀린다(string id)
    {
        var fx = FixtureCatalog.Get(id);
        var svc = Svc.For(fx.FullPath);
        var entries = await svc.ListEntriesAsync(fx.FullPath, fx.Password);
        TreeAssert.ListEquals(entries, fx.Expected, nfc: false, context: id);   // 정규화 없이 같아야 함
        using var tmp = new TempDir("nfc");
        string dest = tmp.P("out");
        await svc.ExtractAsync(fx.FullPath, dest, fx.Password);
        TreeAssert.ExtractedTreeEquals(dest, fx.Expected, nfc: false, context: id);
    }

    /// <summary>순수 bzip2(.bz2) 단일 파일: 이름은 압축 파일 이름에서 .bz2 를 뗀 것, 내용은 정확히 풀린다.</summary>
    [Fact]
    public async Task bz2_단일파일은_이름을_파일명에서_추정하고_정확히_풀린다()
    {
        var fx = FixtureCatalog.Get("bz2_single_python");
        var svc = Svc.For(fx.FullPath);
        var only = Assert.Single(await svc.ListEntriesAsync(fx.FullPath));
        Assert.Equal("견적서_2026.txt", only.Path);
        Assert.False(only.IsDirectory);

        using var tmp = new TempDir("bz2");
        string dest = tmp.P("out");
        await svc.ExtractAsync(fx.FullPath, dest);
        TreeAssert.ExtractedTreeEquals(dest, fx.Expected, context: fx.Id);
    }

    /// <summary>현재 동작 기록: BZ2 처리기, 목록 크기 0(raw bzip2 는 풀기 전엔 모름 — 반디집은 풀어 보고 2464 를 보여 줌), 시각 없음.</summary>
    [Fact, Trait(Kinds.Key, Kinds.Record)]
    public async Task 기록_bz2_목록은_크기0_시각없음()
    {
        var fx = FixtureCatalog.Get("bz2_single_python");
        var svc = Svc.For(fx.FullPath);
        Assert.IsType<BZip2ArchiveService>(svc);
        var only = Assert.Single(await svc.ListEntriesAsync(fx.FullPath));
        Assert.Equal(0, only.Size);
        Assert.Null(only.LastModified);
    }

    /// <summary>
    /// 압축 크기(화면의 압축률 계산에 쓰임)가 말이 되는 범위다: 비어 있지 않은 파일은 0 &lt; 압축 크기 ≤ 압축 파일 크기,
    /// 빈 파일·폴더는 0 이하. 7z(솔리드)는 엔진마다 항목별 값이 달라(첫 항목에 몰아 주거나 0) '합계 ≤ 파일 크기'만 본다.
    /// </summary>
    [Theory]
    [MemberData(nameof(ListOk))]
    public async Task 목록의_압축크기가_범위_안에_있다(string id)
    {
        var fx = FixtureCatalog.Get(id);
        long archiveLen = fx.SplitVolumes.Count > 0
            ? fx.SplitVolumes.Sum(v => new FileInfo(FixtureCatalog.PathOf(v)).Length)
            : new FileInfo(fx.FullPath).Length;
        var entries = await Svc.For(fx.FullPath).ListEntriesAsync(fx.FullPath, fx.Password);
        var bad = new List<string>();
        foreach (var e in entries)
        {
            bool empty = e.IsDirectory || e.Size == 0;
            if (e.CompressedSize > archiveLen) bad.Add($"{e.Path}: 압축 크기 {e.CompressedSize} > 파일 크기 {archiveLen}");
            if (empty && e.CompressedSize > 0) bad.Add($"{e.Path}: 빈 항목인데 압축 크기 {e.CompressedSize}");
            if (!empty && fx.Format != "7z" && e.CompressedSize <= 0) bad.Add($"{e.Path}: 압축 크기 {e.CompressedSize}(0 이하)");
        }
        long sum = entries.Where(e => e.CompressedSize > 0).Sum(e => e.CompressedSize);
        if (sum > archiveLen) bad.Add($"압축 크기 합계 {sum} > 파일 크기 {archiveLen}");
        Assert.True(bad.Count == 0, $"{id}:\n  " + string.Join("\n  ", bad));
    }

    /// <summary>현재 동작 기록: SharpCompress 는 7z 항목의 압축 크기를 모두 0 으로 준다(화면 압축률이 의미 없음). 엔진이 바뀌면 달라져도 됨.</summary>
    [Fact, Trait(Kinds.Key, Kinds.Record)]
    public async Task 기록_7z_항목별_압축크기는_모두_0()
    {
        var fx = FixtureCatalog.Get("7z_default");
        var entries = await Svc.For(fx.FullPath).ListEntriesAsync(fx.FullPath);
        Assert.All(entries, e => Assert.Equal(0, e.CompressedSize));
    }

    /// <summary>[B2 고침] 반디집 .gz(FNAME 없음): 이름은 파일명에서 .gz 를 뗀 것으로 보이고 정확히 풀려야 한다(반디집 동작).</summary>
    [Fact]
    public async Task gz_단일파일_반디집산출물이_풀린다()
    {
        var fx = FixtureCatalog.Get("gz_single");
        var svc = Svc.For(fx.FullPath);
        var entries = await svc.ListEntriesAsync(fx.FullPath);
        Assert.Equal("견적서_2026.txt", Assert.Single(entries).Path);
        using var tmp = new TempDir("gz");
        string dest = tmp.P("out");
        await svc.ExtractAsync(fx.FullPath, dest);
        TreeAssert.ExtractedTreeEquals(dest, fx.Expected, context: fx.Id);
    }

    /// <summary>
    /// 현재 동작 기록: 반디집 .tar.gz 는 GZ 처리기가 안쪽 .tar 하나('이름.tar', 한 겹만 벗긴 크기)로 보여 주고 그 .tar 를 꺼낸다
    /// (.tar.bz2 를 BZ2 처리기가 다루는 방식과 같음). 반디집은 tar 안 내용을 바로 보여 준다(KnownGapTests I2-9) —
    /// tar 까지 한 번에 펼치게 바뀌면 이 기록은 지우거나 고칠 것.
    /// (SharpCompress 0.38 때는 이름 빈 항목 하나 + 해제 '안전하지 않은 경로'(B2), 0.50 형식 판별로는 아예 못 열어 GZ 처리기가 gzip 으로 직접 연다)
    /// </summary>
    [Fact, Trait(Kinds.Key, Kinds.Record)]
    public async Task 기록_tar_gz는_안쪽_tar_하나로_보이고_풀린다()
    {
        var fx = FixtureCatalog.Get("targz_default");
        var inner = fx.InnerTar ?? throw new InvalidOperationException("targz_default 에 inner_tar 가 없음");
        var svc = Svc.For(fx.FullPath);
        Assert.IsType<GZipArchiveService>(svc);
        var only = Assert.Single(await svc.ListEntriesAsync(fx.FullPath));
        Assert.Equal(inner.Name, only.Path);
        Assert.Equal(inner.Size, only.Size);

        using var tmp = new TempDir("tgzr");
        string dest = tmp.P("out");
        await svc.ExtractAsync(fx.FullPath, dest);
        Assert.Equal(new[] { inner.Name }, TreeAssert.Files(dest));
        Assert.Equal(inner.Sha256, TreeAssert.Sha256(Path.Combine(dest, inner.Name)));
    }
}
