using System.Reflection;
using Iljip.Models;
using Iljip.Services;
using Iljip.Tests.TestSupport;

namespace Iljip.Tests.Contract;

/// <summary>
/// [범위 11] 알려진 빈틈 — 전부 Skip. 본문은 '고쳐지면 반디집처럼 돼야 할 올바른 기대'다.
/// 엔진 교체 후 Skip 만 지우고 돌려 보면 된다.
/// (지금 관찰된 실제 동작은 RoutingTests 의 '미등록 확장자' 테스트와 README 의 Skip 목록에 적어 둠)
/// </summary>
public class KnownGapTests
{
    public static TheoryData<string> NumberedSplit => FixtureSets.Data(FixtureSets.NumberedSplit);
    public static TheoryData<string> UnsupportedMulti => FixtureSets.Data(FixtureSets.UnsupportedMulti);

    /// <summary>[I2-2] .7z.001 / .zip.001 숫자 분할을 첫 조각으로 열고 풀 수 있어야 한다.</summary>
    [Theory(Skip = Known.I2_2)]
    [MemberData(nameof(NumberedSplit))]
    public async Task 숫자분할_첫조각으로_열고_풀린다(string id) => await OpensLikeBandizip(id);

    /// <summary>
    /// [I2-9] zipx·tgz·tar.gz·txz·tbz2·iso·lzh 를 '반디집 7.46 과 같게' 열고 풀어야 한다. 반디집 동작이 형식마다 다르다(매니페스트 실측):
    /// <list type="bullet">
    /// <item>zipx·tgz·tar.gz·iso·lzh: 안의 내용(tar 는 tar 안 파일들)을 바로 보여 주고 푼다 → 기대값은 expected_entries.</item>
    /// <item>.tar.xz(txz_two_step)·.tbz2(tbz2_python): 안쪽 '.tar 하나'만 보여 주고 그 .tar 를 꺼낸다(bandizip_view.shows_inner_tar_only)
    ///   → 기대값은 항목 1개 '&lt;이름&gt;.tar'(inner_tar 의 크기·sha256). 7z.dll 기본 동작도 같은 방식이다.</item>
    /// </list>
    /// tar 까지 한 번에 펼치는 것은 '반디집보다 나은' 선택 목표라 여기서 시험하지 않는다(README 참고).
    /// </summary>
    [Theory(Skip = Known.I2_9)]
    [MemberData(nameof(UnsupportedMulti))]
    public async Task 미지원_형식이_반디집처럼_열리고_풀린다(string id) => await OpensLikeBandizip(id);

    /// <summary>[I2-9] .xz 단일 파일을 열고 풀어야 한다.</summary>
    [Fact(Skip = Known.I2_9)]
    public async Task xz_단일파일이_풀린다() => await OpensLikeBandizip("xz_single");

    /// <summary>[I2-9] 자동 풀림 exe(SFX, 안쪽은 zip)를 열고 풀어야 한다. 픽스처는 로컬 전용(git 제외).</summary>
    [FixtureFact("local/sfx_default.exe", Skip = Known.I2_9)]
    public async Task 자동풀림_exe가_열리고_풀린다() => await OpensLikeBandizip("sfx_default");

    private static async Task OpensLikeBandizip(string id)
    {
        var fx = FixtureCatalog.Get(id);
        var svc = Svc.Locator.Resolve(fx.FullPath);
        Assert.True(svc is not null, $"{id}: 확장자 '{Path.GetExtension(fx.FullPath)}' 가 등록되지 않음");
        IReadOnlyList<ExpectedEntry> expected = fx.Expected;
        var entries = await svc!.ListEntriesAsync(fx.FullPath, fx.Password);
        if (fx.BandizipShowsInnerTarOnly)
        {
            var inner = fx.InnerTar ?? throw new InvalidOperationException($"{id}: shows_inner_tar_only 인데 inner_tar 가 없음");
            expected = new[] { new ExpectedEntry(inner.Name, inner.Size, inner.Sha256, false) };
            // 목록: '.tar' 항목 하나. 크기는 단일 스트림이라 풀기 전엔 모를 수 있어(bz2 처럼 0) 0 도 허용
            var only = Assert.Single(entries);
            Assert.Equal(inner.Name, TreeAssert.NormalizeEntryPath(only.Path));
            Assert.Contains(only.Size, new[] { inner.Size, 0L });
        }
        else
        {
            TreeAssert.ListEquals(entries, expected, context: id);
        }
        using var tmp = new TempDir("gap");
        string dest = tmp.P("out");
        await svc.ExtractAsync(fx.FullPath, dest, fx.Password);
        TreeAssert.ExtractedTreeEquals(dest, expected, context: id);
    }

    /// <summary>[I2-1] 7z 로 압축한 뒤 다시 풀면 원본과 같아야 한다(한글·빈 파일·빈 폴더 포함).</summary>
    [Fact(Skip = Known.I2_1)]
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
    /// [I2-11] ZIP 암호 방식으로 ZipCrypto 를 고를 수 있어야 하고, 그렇게 만든 ZIP 은 AES(방식 99)가 아니어야 한다
    /// (Windows 탐색기·구형 프로그램 호환). API 가 아직 없으므로 CompressionOptions 에서
    /// 이름에 'ZipCrypto' 가 들어간 enum 값을 가진 속성을 리플렉션으로 찾는다 — 새 API 이름이 다르면 이 부분만 고칠 것.
    /// </summary>
    [Fact(Skip = Known.I2_11)]
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
