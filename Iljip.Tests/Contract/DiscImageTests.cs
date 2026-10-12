using Iljip.Services;
using Iljip.Tests.TestSupport;
using SharpSevenZip;

namespace Iljip.Tests.Contract;

/// <summary>
/// [W2-01 (d), 1차 W1-B 검증 지적] ISO 는 UDF 를 먼저 연다(7-Zip·반디집처럼). 예전엔 늘 7z.dll 'Iso'(ISO9660) 형식으로 열어
/// UDF 만 든 이미지(DVD 이미지 등)는 멀쩡한데도 '압축 파일이 손상됐어요. 다시 받아 보세요.', Joliet 없는 ISO9660+UDF 다리 이미지는
/// ISO9660 쪽 대문자 8.3 이름('____~1/______~1.TXT')이 보이고 한글 이름이 사라졌다.
/// 이미지는 Windows 에 든 IMAPI2FS 로 그때그때 만든다(<see cref="DiscImages"/> — pycdlib 픽스처 iso_default 는 Joliet 이 있어 못 잡았음).
/// </summary>
public class DiscImageTests
{
    private static readonly string[] ExpectedPaths =
    {
        "한글폴더", "한글폴더/견적서 (최종).txt", "한글폴더/하위 폴더", "한글폴더/하위 폴더/data.bin",
    };

    private static string MakeSource(TempDir tmp)
    {
        string root = tmp.Dir("원본", "한글폴더");
        File.WriteAllText(Path.Combine(root, "견적서 (최종).txt"), string.Concat(Enumerable.Repeat("견적 금액 1,234,000원\r\n", 300)));
        var bin = new byte[3000];
        new Random(5).NextBytes(bin);
        File.WriteAllBytes(Path.Combine(tmp.Dir("원본", "한글폴더", "하위 폴더"), "data.bin"), bin);
        return root;
    }

    /// <summary>
    /// 계약: UDF 만·ISO9660+UDF(Joliet 없음)·ISO9660+Joliet+UDF·ISO9660+Joliet(UDF 없음) 모두 한글 이름 그대로 목록이 보이고 풀린다.
    /// UDF 가 들어 있으면 내용으로 'UDF' 로 알아본다.
    /// </summary>
    [DiscImageTheory]
    [InlineData(DiscImages.Udf, ArchiveKind.Udf)]
    [InlineData(DiscImages.Iso9660 | DiscImages.Udf, ArchiveKind.Udf)]
    [InlineData(DiscImages.Iso9660 | DiscImages.Joliet | DiscImages.Udf, ArchiveKind.Udf)]
    [InlineData(DiscImages.Iso9660 | DiscImages.Joliet, ArchiveKind.Iso)]
    public async Task 디스크_이미지가_한글_이름으로_열리고_풀린다(int fileSystems, ArchiveKind kind)
    {
        using var tmp = new TempDir("udf");
        string root = MakeSource(tmp);
        string iso = tmp.P($"이미지{fileSystems}.iso");
        DiscImages.Create(root, iso, fileSystems);

        Assert.Equal(kind, ArchiveSniffer.Detect(iso));
        var svc = Svc.For(iso);
        Assert.IsType<SevenZipFormatsArchiveService>(svc);
        var paths = (await svc.ListEntriesAsync(iso)).Select(e => e.Path).OrderBy(p => p, StringComparer.Ordinal).ToList();
        Assert.Equal(ExpectedPaths, paths);

        string dest = tmp.P("out");
        await svc.ExtractAsync(iso, dest);
        Assert.Equal(TreeAssert.Sha256(Path.Combine(root, "견적서 (최종).txt")), TreeAssert.Sha256(Path.Combine(dest, "한글폴더", "견적서 (최종).txt")));
        Assert.Equal(TreeAssert.Sha256(Path.Combine(root, "하위 폴더", "data.bin")), TreeAssert.Sha256(Path.Combine(dest, "한글폴더", "하위 폴더", "data.bin")));
    }

    /// <summary>UDF 이미지는 확장자가 .iso 가 아니어도(.img) 내용으로 알아보고 연다.</summary>
    [DiscImageFact]
    public async Task 확장자가_달라도_UDF_이미지를_연다()
    {
        using var tmp = new TempDir("udfimg");
        string root = MakeSource(tmp);
        string img = tmp.P("디스크.img");
        DiscImages.Create(root, img, DiscImages.Udf);
        var paths = (await Svc.For(img).ListEntriesAsync(img)).Select(e => e.Path).OrderBy(p => p, StringComparer.Ordinal).ToList();
        Assert.Equal(ExpectedPaths, paths);
    }

    /// <summary>
    /// 계약(W2-01 (d)): 끝이 잘린 디스크 이미지(UDF 만·ISO9660+UDF 다리·ISO9660+Joliet)는 조용히 반쪽을 남기지 않는다.
    /// 7z.dll 의 '끝 잘림' 표시는 정상 이미지에도 떠서 믿지 않고, ISO9660 볼륨 크기(PVD)·UDF 끝 기준점(AVDP)으로 마지막 구역까지 있는지 본다.
    /// - 내용(파일 데이터)이 든 구역 안에서 잘리면 반드시 '끝부분이 잘려' + 아무것도 남기지 않음(새로 만든 대상 폴더도).
    /// - 그 밖의 자리: 오류면 손상(볼륨 설명자·기준점 뒤면 '잘림')이고 아무것도 남기지 않음. 오류 없이 끝났다면 모든 파일이 원본과 같아야 한다
    ///   (IMAPI2FS 는 UDF 내용 뒤를 기준점 사본으로 채워, 그 채움만 잘린 이미지는 내용이 온전하다 — 반디집처럼 그대로 푼다).
    /// </summary>
    [DiscImageTheory]
    [InlineData(DiscImages.Udf)]
    [InlineData(DiscImages.Iso9660 | DiscImages.Udf)]
    [InlineData(DiscImages.Iso9660 | DiscImages.Joliet)]
    public async Task 잘린_디스크_이미지는_조용히_반쪽을_남기지_않는다(int fileSystems)
    {
        using var tmp = new TempDir("udfcut");
        string root = MakeSource(tmp);
        string iso = tmp.P("full.iso");
        DiscImages.Create(root, iso, fileSystems);
        var bytes = File.ReadAllBytes(iso);
        Assert.Null(await Svc.Catch(() => Svc.For(iso).ListEntriesAsync(iso)));
        var data = File.ReadAllBytes(Path.Combine(root, "하위 폴더", "data.bin"));
        int dataAt = bytes.AsSpan().IndexOf(data);
        Assert.True(dataAt > 0, "이미지 안에서 data.bin 내용을 못 찾음");
        int inContent = dataAt + data.Length / 2;

        foreach (int cut in new[] { bytes.Length / 8, bytes.Length / 4, bytes.Length / 2, bytes.Length * 3 / 4, bytes.Length - 2048, bytes.Length - 1, inContent }.Distinct())
        {
            string arc = tmp.P($"t{cut}.iso");
            File.WriteAllBytes(arc, bytes[..cut]);
            string parent = tmp.P($"새 폴더 {cut}");
            string dest = Path.Combine(parent, "out");
            string ctx = $"{fileSystems} {cut}/{bytes.Length}";
            var ex = await Svc.Catch(() => Svc.For(arc).ExtractAsync(arc, dest));
            if (ex is null)
            {
                Assert.True(cut > dataAt + data.Length, $"{ctx}: 내용 구역 안에서 잘렸는데 오류 없이 끝남");
                Assert.Equal(TreeAssert.Sha256(data), TreeAssert.Sha256(Path.Combine(dest, "한글폴더", "하위 폴더", "data.bin")));
                Assert.Equal(TreeAssert.Sha256(Path.Combine(root, "견적서 (최종).txt")), TreeAssert.Sha256(Path.Combine(dest, "한글폴더", "견적서 (최종).txt")));
                continue;
            }
            var corrupt = Assert.IsAssignableFrom<CorruptArchiveException>(ex);
            bool pastDescriptors = (fileSystems & DiscImages.Iso9660) != 0 ? cut >= 17 * 2048 : cut >= 258 * 2048;
            if (pastDescriptors || cut == inContent)
                Assert.True(corrupt.IsTruncated, $"{ctx}: 잘림으로 알리지 않음 — {corrupt.Message}");
            Assert.False(Internals.IsPasswordRelated(ex));
            Assert.False(Directory.Exists(parent), $"{ctx}: 대상에 무언가 남음");
        }
    }

    /// <summary>
    /// 계약(W2-01 (d)): 반디집 픽스처 iso_default(ISO9660+Joliet, 7z.dll 이 정상인데도 '끝 잘림' 표시를 띄움)는 온전하면 풀리고,
    /// 끝을 한 구역(2048바이트)이나 1바이트만 잘라도 볼륨 크기(353 구역 × 2048 = 파일 길이)로 잘림을 알린다.
    /// </summary>
    [Fact]
    public async Task 반디집_iso는_끝을_조금만_잘라도_잘렸다고_알린다()
    {
        var fx = FixtureCatalog.Get("iso_default");
        using var tmp = new TempDir("isocut");
        await Svc.For(fx.FullPath).ExtractAsync(fx.FullPath, tmp.P("full"));
        TreeAssert.ExtractedTreeEquals(tmp.P("full"), fx.Expected);
        var bytes = File.ReadAllBytes(fx.FullPath);
        foreach (int cut in new[] { bytes.Length - 1, bytes.Length - 2048, bytes.Length / 2, 17 * 2048 })
        {
            string arc = tmp.P($"t{cut}.iso");
            File.WriteAllBytes(arc, bytes[..cut]);
            string parent = tmp.P($"새 폴더 {cut}");
            var ex = Assert.IsAssignableFrom<CorruptArchiveException>(await Svc.Catch(() => Svc.For(arc).ExtractAsync(arc, Path.Combine(parent, "out"))));
            Assert.True(ex.IsTruncated, $"iso {cut}: 잘림으로 알리지 않음 — {ex.Message}");
            Assert.Contains("끝부분이 잘려", ErrorMessages.ToUserMessage(ex));
            Assert.False(Directory.Exists(parent));
            Assert.True(Assert.IsAssignableFrom<CorruptArchiveException>(await Svc.Catch(() => Svc.For(arc).ListEntriesAsync(arc))).IsTruncated);
        }
    }

    /// <summary>
    /// 계약: 첫 형식으로 못 열면(손상으로 보임) 대신 형식으로 한 번 더 연다 — UDF 쪽이 깨진 다리 이미지를 ISO9660 으로 여는 장치.
    /// (UDF 가 없는 Joliet 이미지를 'UDF 먼저, 안 되면 ISO' 로 열어 본다)
    /// </summary>
    [DiscImageFact]
    public void 첫_형식으로_못_열면_대신_형식으로_연다()
    {
        using var tmp = new TempDir("udffb");
        string root = MakeSource(tmp);
        string iso = tmp.P("joliet.iso");
        DiscImages.Create(root, iso, DiscImages.Iso9660 | DiscImages.Joliet);
        var only = new SevenZipEngine.Source { ArchivePath = iso, Format = InArchiveFormat.Udf, OpenStream = () => File.OpenRead(iso) };
        Assert.IsType<CorruptArchiveException>(Record.Exception(() => SevenZipEngine.List(only, null, default)));
        var withFallback = new SevenZipEngine.Source
        {
            ArchivePath = iso, Format = InArchiveFormat.Udf, FallbackFormat = InArchiveFormat.Iso, OpenStream = () => File.OpenRead(iso),
        };
        var paths = SevenZipEngine.List(withFallback, null, default).Select(e => e.Path).OrderBy(p => p, StringComparer.Ordinal).ToList();
        Assert.Equal(ExpectedPaths, paths);
    }
}
