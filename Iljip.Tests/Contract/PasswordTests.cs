using Iljip.Models;
using Iljip.Services;
using Iljip.Tests.TestSupport;

namespace Iljip.Tests.Contract;

/// <summary>
/// [범위 3] 틀린 암호·암호 없음 → 예외가 나고, 대상 폴더에 (부분)파일이 남지 않는다.
/// 화면(MainViewModel)은 예외를 IsPasswordRelated 로 보고 암호 창을 다시 띄우므로, 그 판정도 함께 고정한다.
/// 예외 '타입·문구'는 기록('기록_' 테스트, Trait Kind=Record) — W1-C 부터 서비스가 엔진 예외를 일집 암호 예외
/// (PasswordRequiredException·WrongPasswordException, 문구는 쉬운 한국어)로 바꿔 던지고 화면은 타입으로 판정한다(B5).
/// W2-01 부터 7z·zipx 는 7z.dll(SevenZipEngine)로 풀지만 같은 일집 예외·같은 문구다.
/// 계약 테스트의 '예외 + 파일 안 남음 + 암호 문제로 알아봄'은 엔진이 바뀌어도 지켜야 한다. 두 가지를 다른 테스트로 나눠
/// 기록이 먼저 실패해서 계약 결과가 가려지는 일이 없게 했다.
/// </summary>
public class PasswordTests
{
    /// <summary>(픽스처, 넣는 암호, 지금 예외 타입 전체 이름, 문구 핵심)</summary>
    public static TheoryData<string, string?, string, string> CurrentFailures => new()
    {
        { "bandizip/zip_zipcrypto.zip", "wrong", "Iljip.Services.WrongPasswordException", WrongPasswordException.DefaultMessage },
        { "bandizip/zip_zipcrypto.zip", null, "Iljip.Services.PasswordRequiredException", PasswordRequiredException.DefaultMessage },
        { "bandizip/zip_aes256.zip", "wrong", "Iljip.Services.WrongPasswordException", WrongPasswordException.DefaultMessage },
        { "bandizip/zip_aes256.zip", null, "Iljip.Services.PasswordRequiredException", PasswordRequiredException.DefaultMessage },
        // 7z 는 암호 확인값이 없어 틀린 암호가 'Data Error' 로만 드러난다 → 손상과 구분이 안 된다는 말을 덧붙임
        { "python/7z_pw_names_visible.7z", "wrong", "Iljip.Services.WrongPasswordException", WrongPasswordException.DefaultMessage + WrongPasswordException.HeaderNote },
        { "python/7z_pw_names_visible.7z", null, "Iljip.Services.PasswordRequiredException", PasswordRequiredException.DefaultMessage },
        { "bandizip/7z_header_encrypted.7z", "wrong", "Iljip.Services.WrongPasswordException", WrongPasswordException.DefaultMessage + WrongPasswordException.HeaderNote },
        { "bandizip/7z_header_encrypted.7z", null, "Iljip.Services.PasswordRequiredException", PasswordRequiredException.HeaderMessage },
        // W2-01: zipx 는 7z.dll — ZIP AES 는 암호 확인값이 있어 7z.dll 이 '틀린 암호'로 딱 잘라 알린다(덧말 없음)
        { "bandizip/zipx_aes.zipx", "wrong", "Iljip.Services.WrongPasswordException", WrongPasswordException.DefaultMessage },
        { "bandizip/zipx_aes.zipx", null, "Iljip.Services.PasswordRequiredException", PasswordRequiredException.DefaultMessage },
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

    /// <summary>현재 동작 기록: 서비스가 내는 예외 타입·문구(엔진 예외는 InnerException 에).</summary>
    [Theory, Trait(Kinds.Key, Kinds.Record)]
    [MemberData(nameof(CurrentFailures))]
    public async Task 기록_암호실패_예외_타입과_문구(string rel, string? password, string exType, string msg)
    {
        string arc = FixtureCatalog.PathOf(rel);
        using var tmp = new TempDir("pwr0");
        var ex = await Svc.Catch(() => Svc.For(arc).ExtractAsync(arc, tmp.P("out"), password));
        Assert.NotNull(ex);
        Assert.Equal(exType, ex!.GetType().FullName);
        Assert.Equal(msg, ex.Message);
    }

    /// <summary>
    /// 계약(W1-C): 암호 오류면 이번 해제가 새로 만든 것은 폴더까지 하나도 남지 않는다 — 없던 대상 폴더도 지운다(롤백 장부).
    /// ZIP 은 아무것도 만들기 전에 암호를 판정하고, 7z(이름 보임)는 폴더 항목을 만든 뒤 첫 파일에서야 틀린 암호가 드러난다.
    /// </summary>
    [Theory]
    [MemberData(nameof(CurrentFailures))]
    public async Task 암호_오류면_대상폴더도_남지_않는다(string rel, string? password, string _, string _1)
    {
        string arc = FixtureCatalog.PathOf(rel);
        using var tmp = new TempDir("pwl");
        string dest = tmp.P("새 폴더", "out");
        var ex = await Svc.Catch(() => Svc.For(arc).ExtractAsync(arc, dest, password));
        Assert.IsAssignableFrom<ArchivePasswordException>(ex);
        Assert.False(Directory.Exists(tmp.P("새 폴더")), $"{rel}: 암호 오류인데 새로 만든 폴더가 남음");
    }

    /// <summary>계약: 원래 있던 대상 폴더와 그 안의 파일은 암호 오류 롤백이 건드리지 않는다.</summary>
    [Theory]
    [InlineData("python/7z_pw_names_visible.7z")]
    [InlineData("bandizip/zip_zipcrypto.zip")]
    public async Task 암호_오류_롤백은_원래_있던_것을_건드리지_않는다(string rel)
    {
        string arc = FixtureCatalog.PathOf(rel);
        using var tmp = new TempDir("pwk");
        string dest = tmp.Dir("out");
        File.WriteAllText(Path.Combine(dest, "원래 파일.txt"), "keep");
        Directory.CreateDirectory(Path.Combine(dest, "일집_샘플"));   // 압축 안 폴더와 이름이 같은, 원래 있던 빈 폴더
        var ex = await Svc.Catch(() => Svc.For(arc).ExtractAsync(arc, dest, "wrong"));
        Assert.IsType<WrongPasswordException>(ex);
        Assert.Equal(new[] { "원래 파일.txt" }, TreeAssert.Files(dest));
        Assert.True(Directory.Exists(Path.Combine(dest, "일집_샘플")), "원래 있던 빈 폴더가 지워짐");
    }

    /// <summary>
    /// 화면이 '암호 다시 묻기'로 넘어가는 경우들: 암호 없음(전부), ZIP·zipx 의 틀린 암호.
    /// (7z 틀린 암호·ZipCrypto 검사를 우연히 통과한 틀린 암호는 아래 '어떤_틀린_암호도…' — 예전 B5)
    /// </summary>
    [Theory]
    [InlineData("bandizip/zip_zipcrypto.zip", "wrong")]
    [InlineData("bandizip/zip_zipcrypto.zip", null)]
    [InlineData("bandizip/zip_aes256.zip", "wrong")]
    [InlineData("bandizip/zip_aes256.zip", null)]
    [InlineData("python/7z_pw_names_visible.7z", null)]
    [InlineData("python/7z_pw_names_visible.7z", "wrong")]
    [InlineData("bandizip/7z_header_encrypted.7z", null)]
    [InlineData("bandizip/7z_header_encrypted.7z", "wrong")]
    [InlineData("bandizip/zipx_aes.zipx", "wrong")]
    public async Task 암호_실패는_화면이_암호문제로_알아본다(string rel, string? password)
    {
        string arc = FixtureCatalog.PathOf(rel);
        using var tmp = new TempDir("pwr");
        var ex = await Svc.Catch(() => Svc.For(arc).ExtractAsync(arc, tmp.P("out"), password));
        Assert.NotNull(ex);
        Assert.True(Internals.IsPasswordRelated(ex!), $"{rel}: {ex!.GetType().Name}('{ex.Message}') 를 암호 문제로 못 알아봄");
    }

    /// <summary>
    /// [B5 고침] 틀린 암호는 어떤 경우든 화면이 암호 문제로 알아보고 다시 물어야 한다:
    /// 7z 의 틀린 암호, 그리고 ZipCrypto 의 1바이트 암호 검사를 우연히 통과하는 틀린 암호(약 1/256 확률 — 픽스처에서 계산).
    /// </summary>
    [Theory]
    [InlineData("python/7z_pw_names_visible.7z", "wrong")]
    [InlineData("bandizip/7z_header_encrypted.7z", "wrong")]
    [InlineData("bandizip/zip_zipcrypto.zip", ZipCryptoLucky.Marker)]
    public async Task 어떤_틀린_암호도_화면이_암호문제로_알아본다(string rel, string password)
    {
        string arc = FixtureCatalog.PathOf(rel);
        if (password == ZipCryptoLucky.Marker)   // 첫 항목의 1바이트 검사를 통과하는 틀린 암호 중 첫 번째
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
    /// [I1-7 서비스 쪽] 헤더 암호 7z: 암호 없이 목록 → 화면이 '암호 문제'로 알아볼 수 있는 예외, 틀린 암호 → 역시 암호 예외(W1-C, B5).
    /// 맞는 암호면 목록이 맞다(BandizipFixtureTests.목록이_반디집_기대값과_같다 에서 확인).
    /// 화면은 이 예외를 받으면 '열기' 단계에서 암호를 묻고 다시 연다(W1-D 1차 검증 — CommandLineTests.헤더암호_7z…).
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
        Assert.True(Internals.IsPasswordRelated(wrong!), $"{wrong!.GetType().Name}('{wrong.Message}') 를 암호 문제로 못 알아봄");
    }

    /// <summary>현재 동작 기록: 헤더 암호 7z 목록의 예외 타입(암호 없음 / 틀린 암호) — 둘 다 '헤더 암호' 표시. W2-01 부터 7z.dll(머리 바이트로 헤더 암호 탐지).</summary>
    [Fact, Trait(Kinds.Key, Kinds.Record)]
    public async Task 기록_헤더암호_7z_목록_예외_타입()
    {
        var fx = FixtureCatalog.Get("7z_header_encrypted");
        var svc = Svc.For(fx.FullPath);
        var noPw = await Svc.Catch(() => svc.ListEntriesAsync(fx.FullPath, null));
        Assert.True(Assert.IsType<PasswordRequiredException>(noPw).IsHeaderEncrypted);
        var wrong = await Svc.Catch(() => svc.ListEntriesAsync(fx.FullPath, "wrong"));
        Assert.True(Assert.IsType<WrongPasswordException>(wrong).IsHeaderEncrypted);
        // W2-01: 7z.dll 이 '열기 실패'로 알린 것을 머리 바이트(ArchiveHeaderProbe)로 헤더 암호라고 알아 바꾼 것
        Assert.StartsWith("SharpSevenZip.", wrong!.InnerException?.GetType().FullName);
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
