using Iljip.Models;
using Iljip.Services;
using Iljip.Tests.TestSupport;

namespace Iljip.Tests.Contract;

/// <summary>
/// [범위 3] 틀린 암호·암호 없음 → 예외가 나고, 대상 폴더에 (부분)파일이 남지 않는다.
/// 화면(MainViewModel)은 예외를 IsPasswordRelated 로 보고 암호 창을 다시 띄우므로, 그 판정도 함께 고정한다.
/// 예외 '타입·문구'는 지금 엔진(SharpCompress) 기준 기록('기록_' 테스트, Trait Kind=Record) — 엔진을 바꾸면 달라져도 되지만
/// 계약 테스트의 '예외 + 파일 안 남음 + 암호 문제로 알아봄'은 지켜야 한다. 두 가지를 다른 테스트로 나눠
/// 기록이 먼저 실패해서 계약 결과가 가려지는 일이 없게 했다.
/// </summary>
public class PasswordTests
{
    /// <summary>(픽스처, 넣는 암호, 지금 예외 타입 전체 이름, 문구 핵심)</summary>
    public static TheoryData<string, string?, string, string> CurrentFailures => new()
    {
        { "bandizip/zip_zipcrypto.zip", "wrong", "SharpCompress.Common.CryptographicException", "The password did not match." },
        { "bandizip/zip_zipcrypto.zip", null, "SharpCompress.Common.CryptographicException", "No password supplied for encrypted zip." },
        { "bandizip/zip_aes256.zip", "wrong", "SharpCompress.Common.InvalidFormatException", "bad password" },
        { "bandizip/zip_aes256.zip", null, "SharpCompress.Common.CryptographicException", "No password supplied for encrypted zip." },
        { "python/7z_pw_names_visible.7z", "wrong", "SharpCompress.Compressors.LZMA.DataErrorException", "Data Error" },
        { "python/7z_pw_names_visible.7z", null, "SharpCompress.Common.CryptographicException", "Encrypted 7Zip archive has no password specified." },
        { "bandizip/7z_header_encrypted.7z", "wrong", "SharpCompress.Compressors.LZMA.DataErrorException", "Data Error" },
        { "bandizip/7z_header_encrypted.7z", null, "SharpCompress.Common.CryptographicException", "Encrypted 7Zip archive has no password specified." },
    };

    /// <summary>계약: 틀린 암호·암호 없음이면 예외가 나고, 대상 폴더에 파일이 하나도 남지 않는다.</summary>
    [Theory]
    [MemberData(nameof(CurrentFailures))]
    public async Task 틀리거나_없는_암호는_예외_그리고_파일이_남지_않는다(string rel, string? password, string _, string _1)
    {
        string arc = FixtureCatalog.PathOf(rel);
        using var tmp = new TempDir("pw");
        string dest = tmp.P("out");
        var ex = await Svc.Catch(() => Svc.For(arc).ExtractAsync(arc, dest, password));
        Assert.True(ex is not null, $"{rel}: 암호가 틀리거나 없는데 예외 없이 끝남");
        TreeAssert.NoFiles(dest, rel);
    }

    /// <summary>현재 동작 기록: 지금 엔진(SharpCompress)이 내는 예외 타입·문구.</summary>
    [Theory, Trait(Kinds.Key, Kinds.Record)]
    [MemberData(nameof(CurrentFailures))]
    public async Task 기록_암호실패_예외_타입과_문구(string rel, string? password, string exType, string msg)
    {
        string arc = FixtureCatalog.PathOf(rel);
        using var tmp = new TempDir("pwr0");
        var ex = await Svc.Catch(() => Svc.For(arc).ExtractAsync(arc, tmp.P("out"), password));
        Assert.NotNull(ex);
        Assert.Equal(exType, ex!.GetType().FullName);
        Assert.Contains(msg, ex.Message);
    }

    /// <summary>
    /// 화면이 '암호 다시 묻기'로 넘어가는 경우들: 암호 없음(전부), ZIP 의 틀린 암호.
    /// (7z 틀린 암호는 B5 — 아래 Skip 테스트)
    /// </summary>
    [Theory]
    [InlineData("bandizip/zip_zipcrypto.zip", "wrong")]
    [InlineData("bandizip/zip_zipcrypto.zip", null)]
    [InlineData("bandizip/zip_aes256.zip", "wrong")]
    [InlineData("bandizip/zip_aes256.zip", null)]
    [InlineData("python/7z_pw_names_visible.7z", null)]
    [InlineData("bandizip/7z_header_encrypted.7z", null)]
    public async Task 암호_실패는_화면이_암호문제로_알아본다(string rel, string? password)
    {
        string arc = FixtureCatalog.PathOf(rel);
        using var tmp = new TempDir("pwr");
        var ex = await Svc.Catch(() => Svc.For(arc).ExtractAsync(arc, tmp.P("out"), password));
        Assert.NotNull(ex);
        Assert.True(Internals.IsPasswordRelated(ex!), $"{rel}: {ex!.GetType().Name}('{ex.Message}') 를 암호 문제로 못 알아봄");
    }

    /// <summary>
    /// [B5] 틀린 암호는 어떤 경우든 화면이 암호 문제로 알아보고 다시 물어야 한다:
    /// 7z 의 틀린 암호, 그리고 ZipCrypto 의 1바이트 암호 검사를 우연히 통과하는 틀린 암호(약 1/256 확률 — 픽스처에서 계산).
    /// </summary>
    [Theory(Skip = Known.B5 + " / ZipCrypto 행은 B4(CRC 미검사)도 고쳐야 통과")]
    [InlineData("python/7z_pw_names_visible.7z", "wrong")]
    [InlineData("bandizip/7z_header_encrypted.7z", "wrong")]
    [InlineData("bandizip/zip_zipcrypto.zip", BugEvidenceTests.ZipCryptoLuckyMarker)]
    public async Task 어떤_틀린_암호도_화면이_암호문제로_알아본다(string rel, string password)
    {
        string arc = FixtureCatalog.PathOf(rel);
        if (password == BugEvidenceTests.ZipCryptoLuckyMarker)   // 검사를 통과하는 틀린 암호 중 첫 번째(지금은 쓰레기 파일을 남기는 B4 경우일 수 있음)
            password = ZipCryptoLucky.FindWrongPasswordPassingCheck(arc, FixtureCatalog.DefaultPassword);
        using var tmp = new TempDir("pw5");
        string dest = tmp.P("out");
        var ex = await Svc.Catch(() => Svc.For(arc).ExtractAsync(arc, dest, password));
        Assert.NotNull(ex);
        Assert.True(Internals.IsPasswordRelated(ex!), $"{ex!.GetType().Name}('{ex.Message}')");
        TreeAssert.NoFiles(dest);
    }

    /// <summary>
    /// 이름이 보이는 암호 ZIP·7z 는 암호 없이도 '목록'은 읽힌다(반디집과 같음) — 화면이 열기 단계에서 암호를 안 물어도 되는 이유.
    /// </summary>
    [Theory]
    [InlineData("bandizip/zip_zipcrypto.zip")]
    [InlineData("bandizip/zip_aes256.zip")]
    [InlineData("python/7z_pw_names_visible.7z")]
    public async Task 이름이_보이는_암호파일은_암호없이_목록이_읽힌다(string rel)
    {
        var fx = FixtureCatalog.ByFile(rel);
        var entries = await Svc.For(fx.FullPath).ListEntriesAsync(fx.FullPath, null);
        TreeAssert.ListEquals(entries, fx.Expected, context: rel);
    }

    /// <summary>
    /// [I1-7 서비스 쪽] 헤더 암호 7z: 암호 없이 목록 → 화면이 '암호 문제'로 알아볼 수 있는 예외, 틀린 암호 → 예외.
    /// 맞는 암호면 목록이 맞다(BandizipFixtureTests.목록이_반디집_기대값과_같다 에서 확인).
    /// 빈틈은 화면이 이 예외를 받아도 '열기' 단계에서는 암호를 묻지 않는다는 것(MANUAL_CHECKLIST 2절, 자동 테스트 없음).
    /// </summary>
    [Fact]
    public async Task 헤더암호_7z_목록은_암호없이_암호예외()
    {
        var fx = FixtureCatalog.Get("7z_header_encrypted");
        var svc = Svc.For(fx.FullPath);
        var noPw = await Svc.Catch(() => svc.ListEntriesAsync(fx.FullPath, null));
        Assert.True(noPw is not null, "헤더 암호 7z 인데 암호 없이 목록이 읽힘");
        Assert.True(Internals.IsPasswordRelated(noPw!), $"{noPw!.GetType().Name}('{noPw.Message}') 를 암호 문제로 못 알아봄");

        var wrong = await Svc.Catch(() => svc.ListEntriesAsync(fx.FullPath, "wrong"));
        Assert.True(wrong is not null, "틀린 암호인데 목록이 읽힘");
    }

    /// <summary>현재 동작 기록: 헤더 암호 7z 목록의 예외 타입(암호 없음 / 틀린 암호).</summary>
    [Fact, Trait(Kinds.Key, Kinds.Record)]
    public async Task 기록_헤더암호_7z_목록_예외_타입()
    {
        var fx = FixtureCatalog.Get("7z_header_encrypted");
        var svc = Svc.For(fx.FullPath);
        var noPw = await Svc.Catch(() => svc.ListEntriesAsync(fx.FullPath, null));
        Assert.Equal("SharpCompress.Common.CryptographicException", noPw?.GetType().FullName);
        var wrong = await Svc.Catch(() => svc.ListEntriesAsync(fx.FullPath, "wrong"));
        Assert.Equal("SharpCompress.Compressors.LZMA.DataErrorException", wrong?.GetType().FullName);
    }

    /// <summary>일집이 만든 AES ZIP 도 틀린 암호·암호 없음에서 같은 계약(예외 + 파일 없음 + 암호 문제로 알아봄).</summary>
    [Theory]
    [InlineData("wrong")]
    [InlineData(null)]
    [InlineData("")]
    public async Task 일집_AES_ZIP_틀리거나_없는_암호(string? password)
    {
        using var tmp = new TempDir("pwi");
        string src = SampleTree.Create(tmp.Dir("src"));
        string arc = tmp.P("aes.zip");
        var svc = new ZipArchiveService();
        await svc.CompressAsync(new[] { src }, arc, new CompressionOptions { Password = "올바른암호" });
        string dest = tmp.P("out");
        var ex = await Svc.Catch(() => svc.ExtractAsync(arc, dest, password));
        Assert.NotNull(ex);
        Assert.True(Internals.IsPasswordRelated(ex!), $"{ex!.GetType().FullName}: {ex.Message}");
        TreeAssert.NoFiles(dest);
    }

    /// <summary>현재 동작: 빈 문자열 암호는 '암호 없음'과 같다(압축할 때 암호화하지 않음).</summary>
    [Fact]
    public async Task 빈_문자열_암호는_암호없음과_같다()
    {
        using var tmp = new TempDir("pwe");
        string src = SampleTree.Create(tmp.Dir("src"));
        string arc = tmp.P("plain.zip");
        await new ZipArchiveService().CompressAsync(new[] { src }, arc, new CompressionOptions { Password = "" });
        Assert.All(ZipHeaders.CentralEntries(arc), e => Assert.Equal(0, e.Flags & 1));
        Assert.False(new CompressionOptions { Password = "" }.HasPassword);
    }
}
