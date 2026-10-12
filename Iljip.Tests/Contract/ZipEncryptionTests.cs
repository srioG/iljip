using System.Text;
using Iljip.Models;
using Iljip.Services;
using Iljip.Tests.TestSupport;

namespace Iljip.Tests.Contract;

/// <summary>
/// [W1-C] ZIP 암호 만들기(AES-256 / ZipCrypto — I2-11, 한글 암호 — B3)와 WinZip AES 직접 해제(<see cref="WinZipAes"/>)의 무결성 확인.
/// </summary>
public class ZipEncryptionTests
{
    /// <summary>ZipCrypto 로 만들면 방식 99(AES)가 아니고 암호 비트가 켜져 있으며, 일집으로 풀면 원본과 같다(영문·한글·이모지 암호).</summary>
    [Theory]
    [InlineData(FixtureCatalog.DefaultPassword)]
    [InlineData(FixtureCatalog.KoreanPassword)]
    [InlineData("암호😀이모지")]
    public async Task ZipCrypto_왕복이_원본과_같다(string password)
    {
        using var tmp = new TempDir("zcrt");
        string src = SampleTree.Create(tmp.Dir("src"));
        string arc = tmp.P("zipcrypto.zip");
        var svc = new ZipArchiveService();
        await svc.CompressAsync(new[] { src }, arc, new CompressionOptions { Password = password, Encryption = ZipEncryption.ZipCrypto });

        var files = ZipHeaders.CentralEntries(arc).Where(e => !e.Name.EndsWith('/')).ToList();
        Assert.All(files, e => Assert.NotEqual(99, e.Method));
        Assert.All(files, e => Assert.True((e.Flags & 1) != 0, "암호 비트 없음: " + e.Name));

        string dest = tmp.P("out");
        await svc.ExtractAsync(arc, dest, password);
        TreeAssert.ExtractedTreeEquals(dest, SampleTree.ExpectedOf(src));
    }

    /// <summary>ZipCrypto + 한글 암호는 CP949 바이트로 쓴다(반디집 호환) — 판정이 Cp949 이고 일집으로도 풀린다.</summary>
    [Fact]
    public async Task ZipCrypto_한글암호는_CP949로_쓰고_일집으로_풀린다()
    {
        using var tmp = new TempDir("zckr");
        string src = SampleTree.Create(tmp.Dir("src"));
        string arc = tmp.P("한글암호.zip");
        await new ZipArchiveService().CompressAsync(new[] { src }, arc,
            new CompressionOptions { Password = FixtureCatalog.KoreanPassword, Encryption = ZipEncryption.ZipCrypto });
        Assert.Equal(PasswordBytes.Cp949, ZipPasswordProbe.Probe(arc, FixtureCatalog.KoreanPassword).Match);

        string dest = tmp.P("out");
        await Svc.For(arc).ExtractAsync(arc, dest, FixtureCatalog.KoreanPassword);
        TreeAssert.ExtractedTreeEquals(dest, SampleTree.ExpectedOf(src));
    }

    /// <summary>AES + 한글 암호(기본): UTF-8 로 쓰이고(SharpZipLib 고정, 반디집과 다름 — 화면이 경고) 일집으로는 풀린다.</summary>
    [Fact]
    public async Task AES_한글암호는_UTF8로_쓰고_일집으로_풀린다()
    {
        using var tmp = new TempDir("aeskr");
        string src = SampleTree.Create(tmp.Dir("src"));
        string arc = tmp.P("한글암호_aes.zip");
        await new ZipArchiveService().CompressAsync(new[] { src }, arc, new CompressionOptions { Password = FixtureCatalog.KoreanPassword });
        Assert.All(ZipHeaders.CentralEntries(arc).Where(e => !e.Name.EndsWith('/')), e => Assert.Equal(99, e.Method));
        Assert.Equal(PasswordBytes.Utf8, ZipPasswordProbe.Probe(arc, FixtureCatalog.KoreanPassword).Match);

        string dest = tmp.P("out");
        await Svc.For(arc).ExtractAsync(arc, dest, FixtureCatalog.KoreanPassword);
        TreeAssert.ExtractedTreeEquals(dest, SampleTree.ExpectedOf(src));
    }

    /// <summary>반디집 한글 암호 AES ZIP(CP949)에서 고른 파일만 풀어도 맞게 풀린다(판정은 고른 항목 기준, AES 는 직접 해제).</summary>
    [Fact]
    public async Task 반디집_한글암호_AES_선택해제()
    {
        var fx = FixtureCatalog.Get("zip_aes256_korean_pw");
        var pick = fx.ExpectedFiles.First(e => e.Size > 0);
        using var tmp = new TempDir("aessel");
        string dest = tmp.P("out");
        await Svc.For(fx.FullPath).ExtractEntriesAsync(fx.FullPath, dest, new[] { pick.Path }, fx.Password);
        Assert.Equal(new[] { pick.Path }, TreeAssert.Files(dest));
        Assert.Equal(pick.Sha256, TreeAssert.Sha256(Path.Combine(dest, pick.Path)));
    }

    /// <summary>
    /// AES 데이터가 깨진 ZIP 은 맞는 암호로 풀어도 오류(인증값 HMAC 불일치·Deflate 오류)이고 깨진 파일이 남지 않는다.
    /// 일집 AES(AE-2)는 CRC 칸이 0 이라 예전(SharpCompress — HMAC 을 안 봄, W1-C 실측)엔 틀린 내용을 조용히 풀었다.
    /// 반디집 AES(AE-1)는 CRC 도 있다. 판정이 AES 확인값(16비트)으로 암호를 이미 확인했으므로 '손상'으로 알린다 —
    /// 화면이 암호를 다시 묻지 않고, 깨진 파일 앞에서 맞게 풀린 파일은 남는다(W1-C 1차 검증 지적: 예전엔 '틀린 암호'로 다시 묻고 멀쩡한 파일까지 지웠다).
    /// </summary>
    [Theory]
    [InlineData("iljip")]
    [InlineData("bandizip")]
    public async Task AES_데이터가_깨지면_오류_그리고_파일이_남지_않는다(string maker)
    {
        using var tmp = new TempDir("aesbad");
        string good;
        string password = FixtureCatalog.DefaultPassword;
        Dictionary<string, string> expected;
        if (maker == "iljip")
        {
            // 항목 하나(큰.bin) — 깨진 그 파일만 있으니 남을 파일도 없다
            string src = tmp.Dir("src");
            var big = new byte[200_000];
            new Random(7).NextBytes(big);
            File.WriteAllBytes(Path.Combine(src, "큰.bin"), big);
            good = tmp.P("good.zip");
            await new ZipArchiveService().CompressAsync(new[] { Path.Combine(src, "큰.bin") }, good, new CompressionOptions { Password = password, Level = CompressionLevelOption.Store });
            expected = new Dictionary<string, string>();
        }
        else
        {
            var fx = FixtureCatalog.Get("zip_aes256");
            good = fx.FullPath;
            expected = ShaMap(fx);
        }
        string arc = tmp.P("flip.zip");
        var broken = FlipMiddleOfLargestEntry(good, arc);   // 암호문 한가운데 1비트

        string dest = tmp.P("out");
        var ex = await Svc.Catch(() => Svc.For(arc).ExtractAsync(arc, dest, password));
        AssertCorruptAndGoodFilesKept(ex, arc, broken, dest, expected);
    }

    /// <summary>
    /// [W1-C 1차 검증] 반디집 ZipCrypto(여러 항목) + 맞는 암호인데 데이터가 깨짐: 판정이 모든 항목의 확인 바이트로 암호를 확인했으므로
    /// '틀린 암호'가 아니라 손상(CRC·Deflate 오류) — 다시 묻지 않고, 깨진 파일은 없고, 그 앞에서 맞게 풀린 파일은 남는다.
    /// (항목 하나짜리 ZipCrypto 는 확인 바이트가 하나뿐이라 틀린 암호와 구분이 안 돼 예전대로 '틀린 암호(손상일 수도)' — FixedBugTests.항목하나_…)
    /// </summary>
    [Fact]
    public async Task 맞는_암호인데_ZipCrypto_데이터가_깨지면_손상으로_알리고_맞게_풀린_파일은_남는다()
    {
        var fx = FixtureCatalog.Get("zip_zipcrypto");
        using var tmp = new TempDir("zcbad");
        string arc = tmp.P("flip.zip");
        var broken = FlipMiddleOfLargestEntry(fx.FullPath, arc);
        string dest = tmp.P("out");
        var ex = await Svc.Catch(() => Svc.For(arc).ExtractAsync(arc, dest, fx.Password));
        AssertCorruptAndGoodFilesKept(ex, arc, broken, dest, ShaMap(fx));
    }

    /// <summary>
    /// 손상 예외(암호 예외 아님), 깨진 항목은 없음, 깨진 항목 앞(중앙 디렉터리 순서)의 파일은 모두 남고 <paramref name="expected"/>(경로 → sha256)와 같은 내용.
    /// </summary>
    private static void AssertCorruptAndGoodFilesKept(Exception? ex, string arc, ZipCentralEntry broken, string dest, IReadOnlyDictionary<string, string> expected)
    {
        var corrupt = Assert.IsAssignableFrom<CorruptArchiveException>(ex);
        Assert.False(Internals.IsPasswordRelated(corrupt), "맞는 암호인데 손상을 암호 문제로 알림(다시 묻게 됨): " + corrupt.Message);
        Assert.Equal(broken.Name, corrupt.EntryPath);
        Assert.Contains("손상", corrupt.Message);

        var central = ZipPasswordProbe.Probe(arc, null).Entries;
        var before = central.Where(e => !e.Name.EndsWith('/') && e.Index < broken.Index).Select(e => e.Name).OrderBy(n => n, StringComparer.Ordinal).ToList();
        if (expected.Count > 0) Assert.NotEmpty(before);   // 여러 항목 픽스처: 깨진 항목 앞에 남아야 할 파일이 정말 있는지(남김을 시험하는지)
        Assert.Equal(before, TreeAssert.Files(dest).OrderBy(n => n, StringComparer.Ordinal).ToList());
        foreach (var f in before)
            Assert.Equal(expected[f], TreeAssert.Sha256(Path.Combine(dest, f)));
    }

    // ───── W1-C 1차 검증 지적: 두 후보(UTF-8·CP949)가 모두 1바이트 검사를 통과하는 한글 암호 ZipCrypto ─────

    /// <summary>
    /// 항목 하나짜리 ZipCrypto 에서 한글 암호의 두 바이트(UTF-8·CP949)가 모두 1바이트 검사를 통과하면(약 1/128) 판정이 못 가린다.
    /// 그래도 맞는 암호면 풀려야 한다 — 먼저 쓴 후보(CP949)가 틀리면 만든 것을 지우고 다른 후보로 처음부터 다시 푼다(같은 이름 그대로, '이름 (1)' 없음).
    /// 예전: UTF-8 이 통과하면 그것만 써서 '암호가 맞지 않아요'로 영영 못 풀었다(반디집·일집이 CP949 로 쓴 압축). 저장·Deflate 둘 다(틀린 후보는 CRC·Deflate 오류로 드러남).
    /// </summary>
    [Theory]
    [InlineData(PasswordBytes.Cp949, false)]
    [InlineData(PasswordBytes.Cp949, true)]
    [InlineData(PasswordBytes.Utf8, false)]
    [InlineData(PasswordBytes.Utf8, true)]
    public async Task 두_후보가_모두_통과하는_한글암호_ZipCrypto도_맞는_암호로_풀린다(PasswordBytes writtenWith, bool deflate)
    {
        using var tmp = new TempDir("zcamb");
        string arc = tmp.P("모호.zip");
        byte[] data = Encoding.UTF8.GetBytes(string.Concat(Enumerable.Repeat("한 항목 한글 암호 시험 0123456789\n", 800)));
        var (written, other) = ZipPasswordProbeTests.Candidates(writtenWith);
        RawZipCrypto.Write(arc, new RawZipCrypto.Item("보고서.txt", data, written, deflate, RawZipCrypto.OtherPasses(other)));
        Assert.Equal(PasswordBytes.Utf8, ZipPasswordProbe.Probe(arc, ZipPasswordProbeTests.KoreanPw).AlternateMatch);   // 정말 못 가리는 모양인지

        string dest = tmp.P("out");
        await Svc.For(arc).ExtractAsync(arc, dest, ZipPasswordProbeTests.KoreanPw);
        Assert.Equal(new[] { "보고서.txt" }, TreeAssert.Files(dest));
        Assert.Equal(TreeAssert.Sha256(data), TreeAssert.Sha256(Path.Combine(dest, "보고서.txt")));

        // 골라 풀기(목록에서 더블클릭해 열기와 같은 길)
        string dest2 = tmp.P("out2");
        await Svc.For(arc).ExtractEntriesAsync(arc, dest2, new[] { "보고서.txt" }, ZipPasswordProbeTests.KoreanPw);
        Assert.Equal(new[] { "보고서.txt" }, TreeAssert.Files(dest2));
        Assert.Equal(TreeAssert.Sha256(data), TreeAssert.Sha256(Path.Combine(dest2, "보고서.txt")));
    }

    /// <summary>
    /// 일집이 만든 항목 하나짜리 ZipCrypto + 한글 암호(CP949 로 씀)에서 UTF-8 바이트가 우연히 검사를 통과하는 경우(검증 하네스 'ambig' 재현) —
    /// 맞는 암호로 전체·골라 풀기가 모두 된다. 암호 머리가 난수라 그런 압축이 나올 때까지 암호를 바꿔 가며 만든다(평균 128번, 상한 5000번).
    /// </summary>
    [Fact]
    public async Task 일집이_만든_항목하나_ZipCrypto_한글암호는_UTF8이_우연히_통과해도_풀린다()
    {
        using var tmp = new TempDir("zcamb2");
        string src = tmp.P("보고서.txt");
        File.WriteAllText(src, string.Concat(Enumerable.Repeat("한 항목 반디집 호환 시험 0123456789\n", 500)));
        var svc = new ZipArchiveService();
        for (int i = 0; i < 5000; i++)
        {
            string pw = "한글암호" + i;
            string arc = tmp.P($"a{i}.zip");
            await svc.CompressAsync(new[] { src }, arc, new CompressionOptions { Password = pw, Encryption = ZipEncryption.ZipCrypto });
            var probe = ZipPasswordProbe.Probe(arc, pw);
            if (probe.AlternateMatch == PasswordBytes.None) { File.Delete(arc); continue; }   // UTF-8 이 통과하지 않음(대부분)

            Assert.Equal(PasswordBytes.Cp949, probe.Match);
            string dest = tmp.P("out");
            await Svc.For(arc).ExtractAsync(arc, dest, pw);
            Assert.Equal(new[] { "보고서.txt" }, TreeAssert.Files(dest));
            Assert.Equal(TreeAssert.Sha256(src), TreeAssert.Sha256(Path.Combine(dest, "보고서.txt")));

            string dest2 = tmp.P("out2");
            await Svc.For(arc).ExtractEntriesAsync(arc, dest2, new[] { "보고서.txt" }, pw);
            Assert.Equal(TreeAssert.Sha256(src), TreeAssert.Sha256(Path.Combine(dest2, "보고서.txt")));
            return;
        }
        Assert.Fail("5000번 안에 UTF-8 바이트가 우연히 통과하는 압축이 안 나옴(확률상 거의 불가능 — 일집 ZipCrypto 암호 머리 방식이 바뀌었는지 볼 것)");
    }

    /// <summary>
    /// 여러 항목 ZIP 에서 한 파일만 골라 풀 때, 고른 항목에서는 두 후보가 다 통과해도 고르지 않은 항목이 가려 줘서 맞게 풀린다
    /// (검증 하네스 'sel' 재현 — 400개 중 6개를 맞는 암호로 못 골라 풀었다). UTF-8 로 쓴 경우는 기본 순서(CP949 먼저)가 틀린 쪽이라 가려내기가 꼭 필요하다.
    /// </summary>
    [Theory]
    [InlineData(PasswordBytes.Cp949)]
    [InlineData(PasswordBytes.Utf8)]
    public async Task 여러_항목에서_한_파일만_골라도_모호한_한글암호가_풀린다(PasswordBytes writtenWith)
    {
        using var tmp = new TempDir("zcsel");
        string arc = tmp.P("여럿.zip");
        var (written, other) = ZipPasswordProbeTests.Candidates(writtenWith);
        byte[] picked = Encoding.UTF8.GetBytes(string.Concat(Enumerable.Repeat("고른 파일 내용\n", 300)));
        RawZipCrypto.Write(arc,
            new RawZipCrypto.Item("자료/다른1.txt", Encoding.UTF8.GetBytes("다른 파일 1"), written, true, RawZipCrypto.OtherPasses(other, passes: false)),
            new RawZipCrypto.Item("자료/고른.txt", picked, written, true, RawZipCrypto.OtherPasses(other)),
            new RawZipCrypto.Item("자료/다른2.txt", Encoding.UTF8.GetBytes("다른 파일 2"), written, true, RawZipCrypto.OtherPasses(other, passes: false)));

        string dest = tmp.P("out");
        await Svc.For(arc).ExtractEntriesAsync(arc, dest, new[] { "자료/고른.txt" }, ZipPasswordProbeTests.KoreanPw);
        Assert.Equal(new[] { "자료/고른.txt" }, TreeAssert.Files(dest));
        Assert.Equal(TreeAssert.Sha256(picked), TreeAssert.Sha256(Path.Combine(dest, "자료", "고른.txt")));
    }

    /// <summary>
    /// 두 후보가 모두 통과하는데 데이터까지 깨졌으면 두 번 다 실패한다 — 근거가 약해(확인 바이트 하나) 틀린 암호와 구분이 안 되므로
    /// '틀린 암호(손상일 수도)'로 다시 묻고, 두 번의 시도가 만든 것은 하나도 남지 않는다(대상 폴더도).
    /// </summary>
    [Fact]
    public async Task 두_후보가_모두_통과하는데_데이터가_깨지면_틀린암호_손상일수도_그리고_아무것도_안_남는다()
    {
        using var tmp = new TempDir("zcamb3");
        string good = tmp.P("good.zip");
        byte[] data = Encoding.UTF8.GetBytes(string.Concat(Enumerable.Repeat("깨질 파일 0123456789\n", 500)));
        var (written, other) = ZipPasswordProbeTests.Candidates(PasswordBytes.Cp949);
        RawZipCrypto.Write(good, new RawZipCrypto.Item("보고서.txt", data, written, HeaderFilter: RawZipCrypto.OtherPasses(other)));
        string arc = tmp.P("flip.zip");
        FlipMiddleOfLargestEntry(good, arc);

        string dest = tmp.P("out");
        var ex = await Svc.Catch(() => Svc.For(arc).ExtractAsync(arc, dest, ZipPasswordProbeTests.KoreanPw));
        var wrong = Assert.IsType<WrongPasswordException>(ex);
        Assert.True(wrong.MayBeCorrupt);
        Assert.Contains("손상", wrong.Message);
        Assert.False(Directory.Exists(dest), "두 번 다 실패했는데 대상 폴더(또는 파일)가 남음");
    }

    /// <summary>AES 항목 중간에서 잘린 ZIP 도 예외 + 파일 없음(중앙 디렉터리가 없어 판정은 Unknown → 엔진 오류).</summary>
    [Fact]
    public async Task AES_ZIP이_잘리면_예외_그리고_파일이_남지_않는다()
    {
        var fx = FixtureCatalog.Get("zip_aes256");
        byte[] b = File.ReadAllBytes(fx.FullPath);
        using var tmp = new TempDir("aescut");
        string arc = tmp.P("cut.zip");
        File.WriteAllBytes(arc, b[..(b.Length / 2)]);
        string dest = tmp.P("out");
        var ex = await Svc.Catch(() => Svc.For(arc).ExtractAsync(arc, dest, fx.Password));
        Assert.NotNull(ex);
        TreeAssert.NoFiles(dest);
    }

    /// <summary>
    /// 계약: AES ZIP(일집이 직접 푸는 경로)도 해제 도중 취소하면 OCE — 앞서 끝난 파일은 남고 쓰던 큰 파일만 지워지며(암호 오류가 아니라 롤백 안 함),
    /// 끝나자마자 원본 ZIP 을 지울 수 있다(직접 연 파일 스트림을 닫음).
    /// </summary>
    [Fact]
    public async Task AES_해제_도중_취소하면_OCE_그리고_원본을_잠그지_않는다()
    {
        using var tmp = new TempDir("aescx");
        string src = CancellationTests.MakeSource(tmp);
        string arc = tmp.P("big_aes.zip");
        await new ZipArchiveService().CompressAsync(new[] { src }, arc,
            new CompressionOptions { Level = CompressionLevelOption.Store, Password = FixtureCatalog.DefaultPassword });

        using var cts = new CancellationTokenSource();
        var progress = new ProgressRecorder(p =>
        {
            if (p.CurrentFile.EndsWith("z_큰.bin", StringComparison.Ordinal) && p.BytesProcessed > 8L * 1024 * 1024) cts.Cancel();
        });
        string dest = tmp.P("out");
        var ex = await Svc.Catch(() => Svc.For(arc).ExtractAsync(arc, dest, FixtureCatalog.DefaultPassword, progress, cts.Token));
        Assert.IsAssignableFrom<OperationCanceledException>(ex);
        Assert.Equal(new[] { "취소 원본/a_작은.txt" }, TreeAssert.Files(dest));
        Assert.Null(Record.Exception(() => File.Delete(arc)));
    }

    /// <summary>
    /// <paramref name="source"/> 를 <paramref name="target"/> 으로 복사하면서, 압축 크기가 가장 큰 항목의 데이터(로컬 머리 뒤) 한가운데 1비트를 뒤집는다.
    /// 돌려주는 값은 그 항목(중앙 디렉터리 기준 — 이름·순번).
    /// </summary>
    private static ZipCentralEntry FlipMiddleOfLargestEntry(string source, string target)
    {
        var largest = ZipPasswordProbe.Probe(source, null).Entries.Where(e => !e.Name.EndsWith('/')).MaxBy(e => e.CompressedSize)!;
        long dataStart;
        using (var fs = File.OpenRead(source)) dataStart = ZipPasswordProbe.LocalData(fs, largest).DataStart;
        byte[] b = File.ReadAllBytes(source);
        b[dataStart + largest.CompressedSize / 2] ^= 0x01;
        File.WriteAllBytes(target, b);
        return largest;
    }

    private static Dictionary<string, string> ShaMap(FixtureInfo fx) => fx.ExpectedFiles.ToDictionary(e => e.Path, e => e.Sha256!);
}
