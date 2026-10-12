using System.Reflection;
using Iljip.Models;
using Iljip.Services;
using Iljip.Tests.TestSupport;

namespace Iljip.Tests.Contract;

/// <summary>
/// [범위 11] 알려진 빈틈 — 남은 것은 Skip. 본문은 '고쳐지면 반디집처럼 돼야 할 올바른 기대'다.
/// 엔진 교체 후 Skip 만 지우고 돌려 보면 된다. 메운 빈틈(TAR-빈폴더 W1-A, I2-11 W1-C, I2-2 의 풀기·I2-9 W2-01, I2-1 W2-02)은 Skip 을 지운 채 여기 둔다.
/// I2-2 의 '나눠 만들기'(W2-03)는 Skip 테스트가 없던 새 계약이라 Contract/SplitCreateTests.cs 에 있다.
/// 남은 Skip: I2-10(NFD — BandizipFixtureTests). 목록은 README 의 Skip 목록.
/// </summary>
public class KnownGapTests
{
    public static TheoryData<string> NumberedSplit => FixtureSets.Data(FixtureSets.NumberedSplit);
    public static TheoryData<string> UnsupportedMulti => FixtureSets.Data(FixtureSets.UnsupportedMulti);

    /// <summary>[I2-2 의 풀기 — W2-01 고침] .7z.001 / .zip.001 숫자 분할을 첫 조각으로 열고 풀 수 있어야 한다.</summary>
    [Theory]
    [MemberData(nameof(NumberedSplit))]
    public async Task 숫자분할_첫조각으로_열고_풀린다(string id) => await OpensLikeBandizip(id);

    /// <summary>
    /// [I2-9 — W2-01 고침] zipx·tgz·tar.gz·txz·tbz2·iso·lzh 를 열고 풀면 안의 내용(tar 는 tar 안 파일들)이 바로 보이고 풀린다 → 기대값은 expected_entries
    /// (펼친 tar 안 파일 전부의 크기·sha256).
    /// ★ 일집은 반디집보다 한 단계 더 펼친다(W2-01 요구 — 약화가 아니라 강화): 반디집 7.46 은 .tar.xz(txz_two_step)·.tbz2(tbz2_python)를
    /// 안쪽 '.tar 하나'로만 보여 주지만(매니페스트 bandizip_view.shows_inner_tar_only — 반디집 실측 기록이라 고치지 않음), 일집은 .tar.gz 처럼
    /// 한 번에 펼친다(CompressedTarArchiveService). 예전 기대(.tar 하나와 비교)는 항목 1개만 봤고, 지금은 안의 파일 전부와 비교한다.
    /// </summary>
    [Theory]
    [MemberData(nameof(UnsupportedMulti))]
    public async Task 미지원_형식이_반디집처럼_열리고_풀린다(string id) => await OpensLikeBandizip(id);

    /// <summary>[I2-9 — W2-01 고침] .xz 단일 파일을 열고 풀어야 한다(이름은 .xz 를 뗀 것 — 7z.dll).</summary>
    [Fact]
    public async Task xz_단일파일이_풀린다() => await OpensLikeBandizip("xz_single");

    /// <summary>[I2-9 — W2-01 고침] 자동 풀림 exe(SFX, 안쪽은 zip)를 열고 풀어야 한다. 내용으로만 고르므로(확장자 .exe 는 미등록) 내용 판별로 연다. 픽스처는 로컬 전용(git 제외).</summary>
    [FixtureFact("local/sfx_default.exe")]
    public async Task 자동풀림_exe가_열리고_풀린다() => await OpensLikeBandizip("sfx_default");

    private static async Task OpensLikeBandizip(string id)
    {
        var fx = FixtureCatalog.Get(id);
        var svc = Svc.Locator.ResolveByContent(fx.FullPath);
        Assert.True(svc is not null, $"{id}: '{Path.GetFileName(fx.FullPath)}' 를 열 처리기가 없음");
        IReadOnlyList<ExpectedEntry> expected = fx.Expected;
        var entries = await svc!.ListEntriesAsync(fx.FullPath, fx.Password);
        TreeAssert.ListEquals(entries, expected, context: id);
        // 압축된 tar 는 안쪽 tar 를 바로 펼친다 — '.tar' 항목 하나로 보이면 안 된다(W2-01)
        if (svc is CompressedTarArchiveService)
            Assert.DoesNotContain(entries, e => !e.IsDirectory && e.Path.EndsWith(".tar", StringComparison.OrdinalIgnoreCase));
        using var tmp = new TempDir("gap");
        string dest = tmp.P("out");
        await svc.ExtractAsync(fx.FullPath, dest, fx.Password);
        TreeAssert.ExtractedTreeEquals(dest, expected, context: id);
    }

    /// <summary>
    /// [I2-1 — W2-02 고침] 7z 로 압축한 뒤 다시 풀면 원본과 같아야 한다(한글·빈 파일·빈 폴더 포함).
    /// 수준·솔리드·암호·파일 이름 암호화·진행률·취소·메모리는 SevenZipCreateTests·SevenZipCreateLargeTests.
    /// </summary>
    [Fact]
    public async Task 압축_7z_왕복이_원본과_같다()
    {
        using var tmp = new TempDir("7zc");
        string src = SampleTree.Create(tmp.Dir("src"));
        var expected = SampleTree.ExpectedOf(src);
        string arc = tmp.P("왕복.7z");
        var svc = Svc.For(arc);
        await svc.CompressAsync(new[] { src }, arc);
        TreeAssert.ListEquals(await svc.ListEntriesAsync(arc), expected);
        string dest = tmp.P("out");
        await svc.ExtractAsync(arc, dest);
        TreeAssert.ExtractedTreeEquals(dest, expected);
    }

    /// <summary>[TAR-빈폴더 고침] TAR 로 압축할 때 빈 폴더도 담겨야 한다(ZIP 과 같게 — SharpCompress 0.50 TarWriter.WriteDirectory).</summary>
    [Fact]
    public async Task TAR_압축이_빈폴더를_담는다()
    {
        using var tmp = new TempDir("tare");
        string src = SampleTree.Create(tmp.Dir("src"));
        string arc = tmp.P("a.tar");
        var svc = Svc.For(arc);
        await svc.CompressAsync(new[] { src }, arc);
        string dest = tmp.P("out");
        await svc.ExtractAsync(arc, dest);
        Assert.True(Directory.Exists(Path.Combine(dest, SampleTree.RootName, "빈 폴더")));
    }

    /// <summary>
    /// [I2-11 메움] ZIP 암호 방식으로 ZipCrypto 를 고를 수 있어야 하고, 그렇게 만든 ZIP 은 AES(방식 99)가 아니어야 한다
    /// (Windows 탐색기·구형 프로그램 호환). CompressionOptions 에서 이름에 'ZipCrypto' 가 들어간 enum 값을 가진 속성을
    /// 리플렉션으로 찾는다(W1-C: <see cref="CompressionOptions.Encryption"/> = <see cref="ZipEncryption.ZipCrypto"/>).
    /// </summary>
    [Fact]
    public async Task ZIP_암호를_ZipCrypto로_고를_수_있다()
    {
        var prop = typeof(CompressionOptions).GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .FirstOrDefault(p => p.PropertyType.IsEnum && p.CanWrite &&
                                 Enum.GetNames(p.PropertyType).Any(n => n.Contains("ZipCrypto", StringComparison.OrdinalIgnoreCase)));
        Assert.True(prop is not null, "CompressionOptions 에 ZipCrypto 를 고르는 속성이 없음");
        var zipCrypto = Enum.Parse(prop!.PropertyType,
            Enum.GetNames(prop.PropertyType).First(n => n.Contains("ZipCrypto", StringComparison.OrdinalIgnoreCase)));
        var options = new CompressionOptions { Password = FixtureCatalog.DefaultPassword };
        prop.SetValue(options, zipCrypto);   // init 전용이면 리플렉션 SetValue 로도 설정됨

        using var tmp = new TempDir("zc");
        string src = SampleTree.Create(tmp.Dir("src"));
        string arc = tmp.P("zipcrypto.zip");
        await new ZipArchiveService().CompressAsync(new[] { src }, arc, options);

        var encrypted = ZipHeaders.CentralEntries(arc).Where(h => (h.Flags & 1) != 0).ToList();
        Assert.NotEmpty(encrypted);
        Assert.All(encrypted, h => Assert.NotEqual(99, h.Method));

        string dest = tmp.P("out");
        await new ZipArchiveService().ExtractAsync(arc, dest, FixtureCatalog.DefaultPassword);
        TreeAssert.ExtractedTreeEquals(dest, SampleTree.ExpectedOf(src));
    }

    /// <summary>
    /// 현재 동작 기록(보안 결정 보류 — 보고 안건): 인터넷에서 받은 압축(Zone.Identifier = 웹에서 받은 파일 표시, MOTW)을 풀어도
    /// 풀린 파일에는 그 표시를 옮기지 않는다 — 7z.dll(PropagateMarkOfTheWeb=false)·SharpCompress(ZIP) 모두.
    /// 그래서 풀린 Office 문서는 '보호된 보기'·매크로 차단 없이 열린다(Windows 탐색기의 기본 ZIP 풀기는 옮긴다).
    /// 켜면 재사용장치(엑셀 COM 으로 여는 자동 처리)에 영향이 있을 수 있어 사용자 결정 전까지 그대로 둔다 — 바꾸면 이 기록도 고칠 것.
    /// </summary>
    [Theory, Trait(Kinds.Key, Kinds.Record)]
    [InlineData("7z_default")]
    [InlineData("zip_default")]
    public async Task 기록_웹에서_받은_표시를_풀린_파일에_옮기지_않는다(string id)
    {
        var fx = FixtureCatalog.Get(id);
        using var tmp = new TempDir("motw");
        string arc = tmp.P("받은 파일" + Path.GetExtension(fx.FullPath));
        File.Copy(fx.FullPath, arc);
        File.WriteAllText(arc + ":Zone.Identifier", "[ZoneTransfer]\r\nZoneId=3\r\n");
        Assert.True(HasZoneIdentifier(arc), "시험 준비 실패: 압축 파일에 웹 표시를 달지 못함");

        string dest = tmp.P("out");
        await Svc.For(arc).ExtractAsync(arc, dest);
        var files = Directory.EnumerateFiles(dest, "*", SearchOption.AllDirectories).ToList();
        Assert.NotEmpty(files);
        Assert.All(files, f => Assert.False(HasZoneIdentifier(f), "풀린 파일에 웹 표시가 붙음: " + f));
    }

    private static bool HasZoneIdentifier(string path)
    {
        try
        {
            using var s = new FileStream(path + ":Zone.Identifier", FileMode.Open, FileAccess.Read);
            return true;
        }
        catch (FileNotFoundException)
        {
            return false;
        }
    }
}

/// <summary>ZIP 중앙 디렉터리를 훑어 항목별 (압축 방식, 플래그, 이름 바이트)를 꺼낸다 — 암호 방식·EFS 플래그 확인용.</summary>
public static class ZipHeaders
{
    public static List<(int Method, int Flags, string Name)> CentralEntries(string zipPath) =>
        RawCentralEntries(zipPath).Select(e => (e.Method, e.Flags, System.Text.Encoding.UTF8.GetString(e.NameBytes))).ToList();

    /// <summary>
    /// 한국어 Windows 탐색기(와 구형 도구)가 ZIP 이름을 읽는 방식: EFS 플래그(bit 11)가 있으면 UTF-8, 없으면 시스템 코드 페이지(CP949).
    /// (.NET ZipFile.Open(…, entryNameEncoding) 은 .NET 8 에서 플래그가 있어도 지정 인코딩으로 읽어 이 흉내로 쓸 수 없음 — 실측)
    /// </summary>
    public static List<string> ExplorerNames(string zipPath) =>
        RawCentralEntries(zipPath).Select(e => ((e.Flags & 0x0800) != 0 ? System.Text.Encoding.UTF8 : System.Text.Encoding.GetEncoding(949))
                                                .GetString(e.NameBytes)).ToList();

    public static List<(int Method, int Flags, byte[] NameBytes)> RawCentralEntries(string zipPath)
    {
        var b = File.ReadAllBytes(zipPath);
        var list = new List<(int, int, byte[])>();
        // 중앙 디렉터리를 읽는다(데이터 설명자가 있어도 안전).
        int eocd = -1;
        for (int i = b.Length - 22; i >= 0; i--)
            if (BitConverter.ToUInt32(b, i) == 0x06054b50) { eocd = i; break; }
        if (eocd < 0) throw new InvalidDataException("EOCD 없음");
        int count = BitConverter.ToUInt16(b, eocd + 10);
        int p = (int)BitConverter.ToUInt32(b, eocd + 16);
        for (int n = 0; n < count; n++)
        {
            if (BitConverter.ToUInt32(b, p) != 0x02014b50) throw new InvalidDataException("중앙 디렉터리 깨짐");
            int flags = BitConverter.ToUInt16(b, p + 8);
            int method = BitConverter.ToUInt16(b, p + 10);
            int nameLen = BitConverter.ToUInt16(b, p + 28), extraLen = BitConverter.ToUInt16(b, p + 30), commentLen = BitConverter.ToUInt16(b, p + 32);
            list.Add((method, flags, b[(p + 46)..(p + 46 + nameLen)]));
            p += 46 + nameLen + extraLen + commentLen;
        }
        return list;
    }
}
