using System.Globalization;
using System.Text;
using Iljip.Models;
using Iljip.Services;
using Iljip.Tests.TestSupport;
using SharpSevenZip;

namespace Iljip.Tests.Contract;

/// <summary>
/// [W2-01] 7z.dll 해제 경로(SevenZipEngine)의 계약: 임시 폴더에 먼저 풀고 결과를 보고 옮기므로
/// 암호가 틀리거나 손상이면 대상 폴더에 틀린·절반짜리 파일이 남지 않고, 임시 폴더도 남지 않으며, 같은 이름은 비켜 쓴다.
/// 숫자 분할의 빠진 조각은 그 이름으로 알리고, 자동 풀림 exe(7z)도 내용으로 찾아 연다.
/// </summary>
public class SevenZipEngineTests
{
    private static void AssertNoStaging(string dest)
    {
        if (!Directory.Exists(dest)) return;
        var left = Directory.EnumerateDirectories(dest, SevenZipEngine.StagingPrefix + "*", SearchOption.AllDirectories).ToList();
        Assert.True(left.Count == 0, "임시 폴더가 남음: " + string.Join(", ", left));
    }

    /// <summary>계약: 틀린 암호(zipx AES·7z)는 '암호 틀림'이고, 7z.dll 이 만든 0바이트 파일까지 대상 폴더에 하나도 남지 않는다.</summary>
    [Theory]
    [InlineData("zipx_aes")]
    [InlineData("py7z_pw_names_visible")]
    public async Task 틀린_암호는_파일도_임시폴더도_남기지_않는다(string id)
    {
        var fx = FixtureCatalog.Get(id);
        using var tmp = new TempDir("7zw");
        string dest = tmp.P("out");
        var ex = await Svc.Catch(() => Svc.For(fx.FullPath).ExtractAsync(fx.FullPath, dest, "틀린암호"));
        Assert.IsType<WrongPasswordException>(ex);
        Assert.True(Internals.IsPasswordRelated(ex!));
        TreeAssert.NoFiles(dest, id);
        AssertNoStaging(dest);
    }

    /// <summary>계약: 성공·손상 어느 쪽이든 임시 폴더(.일집-임시-…)가 대상 폴더에 남지 않는다.</summary>
    [Fact]
    public async Task 성공해도_손상이어도_임시폴더가_남지_않는다()
    {
        using var tmp = new TempDir("7zst");
        var fx = FixtureCatalog.Get("7z_default");
        string ok = tmp.P("ok");
        await Svc.For(fx.FullPath).ExtractAsync(fx.FullPath, ok);
        AssertNoStaging(ok);

        byte[] b = File.ReadAllBytes(fx.FullPath);
        for (int i = 100; i < 140; i++) b[i] ^= 0x5A;
        string broken = tmp.P("data_broken.7z");
        File.WriteAllBytes(broken, b);
        string bad = tmp.P("bad");
        Assert.IsType<CorruptArchiveException>(await Svc.Catch(() => Svc.For(broken).ExtractAsync(broken, bad)));
        AssertNoStaging(bad);
    }

    /// <summary>계약: 같은 곳에 두 번 풀면(7z.dll 경로도) 기존 파일을 덮지 않고 '이름 (1)'로 비켜 쓴다.</summary>
    [Theory]
    [InlineData("7z_default")]
    [InlineData("iso_default")]
    public async Task 두번_풀면_이름1로_비켜쓴다(string id)
    {
        var fx = FixtureCatalog.Get(id);
        using var tmp = new TempDir("7zcol");
        string dest = tmp.P("out");
        var svc = Svc.For(fx.FullPath);
        await svc.ExtractAsync(fx.FullPath, dest);
        string mine = Path.Combine(dest, "일집_샘플", "readme.txt");
        File.WriteAllText(mine, "사용자가 고친 내용");
        string mineSha = TreeAssert.Sha256(mine);

        await svc.ExtractAsync(fx.FullPath, dest);
        Assert.Equal(mineSha, TreeAssert.Sha256(mine));
        string renamed = Path.Combine(dest, "일집_샘플", "readme (1).txt");
        Assert.Equal(fx.Expected.Single(e => e.Path == "일집_샘플/readme.txt").Sha256, TreeAssert.Sha256(renamed));
        Assert.True(File.Exists(Path.Combine(dest, "일집_샘플", "빈파일 (1).txt")));
        AssertNoStaging(dest);
    }

    /// <summary>계약: 풀린 파일의 수정 시각이 원본 시각과 같다(7z.dll 경로 — 임시 폴더에서 옮겨도 유지).</summary>
    [Theory]
    [InlineData("7z_default")]
    [InlineData("zipx_default")]
    public async Task 풀린_파일의_수정시각이_원본과_같다(string id)
    {
        var fx = FixtureCatalog.Get(id);
        using var tmp = new TempDir("7zmt");
        string dest = tmp.P("out");
        await Svc.For(fx.FullPath).ExtractAsync(fx.FullPath, dest, fx.Password);
        var bad = Directory.EnumerateFiles(dest, "*", SearchOption.AllDirectories)
            .Select(f => MtimeExpect.CheckFile(f, fx) is { } why ? $"{Path.GetRelativePath(dest, f)}: {why}" : null)
            .OfType<string>().ToList();
        Assert.True(bad.Count == 0, string.Join("\n", bad));
    }

    /// <summary>계약(I2-2): 숫자 분할에서 뒤 조각이 빠지면 '그 조각이 없다'고 이름을 대고 알린다(손상으로 뭉뚱그리지 않음). 파일은 남지 않는다.</summary>
    [Theory]
    [InlineData("7z_split", "7z_split.7z.003")]
    [InlineData("zip_numbered_split", "zip_numbered_split.zip.003")]
    [InlineData("7z_split", "7z_split.7z.002")]          // W2-01: 가운데 조각이 빠져도(뒤 조각은 있음) 그 이름
    [InlineData("zip_numbered_split", "zip_numbered_split.zip.002")]
    public async Task 숫자분할_조각이_빠지면_그_조각_이름을_알린다(string id, string missing)
    {
        var fx = FixtureCatalog.Get(id);
        using var tmp = new TempDir("vol");
        string dir = tmp.Dir("vols");
        foreach (var v in fx.SplitVolumes.Where(v => Path.GetFileName(v) != missing))
            File.Copy(FixtureCatalog.PathOf(v), Path.Combine(dir, Path.GetFileName(v)));
        string first = Path.Combine(dir, Path.GetFileName(fx.File));
        string dest = tmp.P("out");

        var listEx = await Svc.Catch(() => Svc.For(first).ListEntriesAsync(first));
        var ex = await Svc.Catch(() => Svc.For(first).ExtractAsync(first, dest));
        foreach (var e in new[] { listEx, ex })
        {
            var vm = Assert.IsType<VolumeMissingException>(e);
            Assert.Equal(missing, vm.MissingVolumeName);
            Assert.Contains(missing, vm.Message);
            Assert.False(Internals.IsPasswordRelated(vm));
        }
        TreeAssert.NoFiles(dest);
    }

    /// <summary>
    /// 계약(I2-9): 7z 자동 풀림 exe — 실행 파일 머리 뒤에 7z 가 붙은 것을 내용으로 찾아 연다(확장자 .exe 는 등록하지 않음).
    /// 진짜 7-Zip SFX 모듈 대신 'MZ' 로 시작하는 4KB 머리 + 7z.dll 로 만든 7z 로 흉내 낸다(실물 7-Zip SFX 는 MANUAL_CHECKLIST).
    /// </summary>
    [Fact]
    public async Task SFX_7z_자동풀림_exe도_열고_푼다()
    {
        using var tmp = new TempDir("sfx7");
        string src = SampleTree.Create(tmp.Dir("src"));
        string sz = tmp.P("inner.7z");
        SevenZipTestArchives.SevenZip(src, sz);
        var stub = new byte[4096];
        stub[0] = (byte)'M';
        stub[1] = (byte)'Z';
        string exe = tmp.P("설치.exe");
        File.WriteAllBytes(exe, stub.Concat(File.ReadAllBytes(sz)).ToArray());

        Assert.Equal(ArchiveKind.SevenZipSfx, ArchiveSniffer.Detect(exe, out long offset));
        Assert.Equal(4096, offset);
        var svc = Svc.For(exe);
        Assert.IsType<SfxArchiveService>(svc);
        var expected = SampleTree.ExpectedOf(src);
        TreeAssert.ListEquals(await svc.ListEntriesAsync(exe), expected);
        string dest = tmp.P("out");
        await svc.ExtractAsync(exe, dest);
        TreeAssert.ExtractedTreeEquals(dest, expected);
        Assert.Null(Svc.Locator.ResolveByExtension(exe));   // 확장자만으로는 고르지 않음
    }

    /// <summary>반디집 자동 풀림 exe(안쪽 ZIP)의 선택 해제도 된다. 픽스처는 로컬 전용(git 제외).</summary>
    [FixtureFact("local/sfx_default.exe")]
    public async Task 반디집_자동풀림_exe의_선택해제()
    {
        var fx = FixtureCatalog.Get("sfx_default");
        Assert.Equal(ArchiveKind.ZipSfx, ArchiveSniffer.Detect(fx.FullPath));
        using var tmp = new TempDir("sfxsel");
        string dest = tmp.P("out");
        await Svc.For(fx.FullPath).ExtractEntriesAsync(fx.FullPath, dest, new[] { "일집_샘플/하위폴더" });
        Assert.Equal(new[] { "일집_샘플/하위폴더/2단계/3단계/깊은 파일 [끝].txt", "일집_샘플/하위폴더/보고서 최종.txt" }, TreeAssert.Files(dest));
    }

    /// <summary>
    /// 7z.dll 이 이름에 못 쓰는 문자(':' 등)를 '_' 로 바꿔 쓰는 규칙을 일집도 똑같이 계산한다 — 임시 폴더에서 파일을 찾는 데 쓴다.
    /// </summary>
    [Theory]
    [InlineData("a/b.txt", "a\\b.txt")]
    [InlineData("a.txt:evil", "a.txt_evil")]
    [InlineData("폴더/파일?.txt", "폴더\\파일_.txt")]
    [InlineData("x\\y", "x\\y")]
    public void 이름_바꾸기_규칙이_7z_dll과_같다(string name, string expected)
    {
        Assert.Equal(expected, SevenZipEngine.SanitizeLikeSharpSevenZip(name));
    }

    /// <summary>
    /// ZIP 을 7z.dll 이 아니라 SharpCompress 로 계속 읽는 이유(ZIP 엔진 결정): 이름이 '한글폴더/문서.txt' 인 ZIP 세 가지
    /// (UTF-8 + 플래그 / UTF-8 인데 플래그 없음 / CP949) 를 일집 ZIP 처리기는 모두 바르게 읽는다(계약).
    /// </summary>
    [Theory]
    [InlineData("utf8_flag")]
    [InlineData("utf8_noflag")]
    [InlineData("cp949")]
    public async Task ZIP_한글이름_세가지를_모두_바르게_읽는다(string variant)
    {
        using var tmp = new TempDir("zipname");
        string arc = MakeNameZip(tmp, variant);
        var names = (await new ZipArchiveService().ListEntriesAsync(arc)).Select(e => e.Path).ToList();
        Assert.Equal(new[] { "한글폴더/문서.txt" }, names);
    }

    /// <summary>
    /// 현재 동작 기록(ZIP 엔진 결정의 근거): 같은 세 ZIP 을 7z.dll 로 읽으면 'UTF-8 인데 플래그 없음'(맥·리눅스 도구가 흔히 만듦)의 이름이 깨진다.
    /// CP949 는 이 PC 의 OEM 코드 페이지가 949(한국어 Windows)일 때만 바르게 읽혀 그때만 확인한다.
    /// </summary>
    [Fact, Trait(Kinds.Key, Kinds.Record)]
    public void 기록_7z_dll은_플래그없는_UTF8_ZIP_이름을_깨뜨린다()
    {
        using var tmp = new TempDir("zipname7");
        string Name(string variant)
        {
            string arc = MakeNameZip(tmp, variant);
            var src = new SevenZipEngine.Source
            {
                ArchivePath = arc,
                Format = InArchiveFormat.Zip,
                OpenStream = () => new FileStream(arc, FileMode.Open, FileAccess.Read, FileShare.Read),
            };
            return Assert.Single(SevenZipEngine.List(src, null, CancellationToken.None), e => !e.IsDirectory).Path;
        }

        Assert.Equal("한글폴더/문서.txt", Name("utf8_flag"));
        Assert.NotEqual("한글폴더/문서.txt", Name("utf8_noflag"));
        if (CultureInfo.CurrentCulture.TextInfo.OEMCodePage == 949)
            Assert.Equal("한글폴더/문서.txt", Name("cp949"));
    }

    private static string MakeNameZip(TempDir tmp, string variant)
    {
        const string name = "한글폴더/문서.txt";
        byte[] data = Encoding.UTF8.GetBytes("내용");
        var item = variant switch
        {
            "utf8_flag" => new RawZip.Item(Encoding.UTF8.GetBytes(name), data, Utf8Flag: true),
            "utf8_noflag" => new RawZip.Item(Encoding.UTF8.GetBytes(name), data, Utf8Flag: false),
            _ => new RawZip.Item(Encoding.GetEncoding(949).GetBytes(name), data, Utf8Flag: false),
        };
        string arc = tmp.P(variant + ".zip");
        RawZip.Write(arc, item);
        return arc;
    }
}
