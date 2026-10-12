using Iljip.Models;
using Iljip.Services;
using Iljip.Tests.TestSupport;

namespace Iljip.Tests.Contract;

/// <summary>
/// [W1-C, B3 양방향] 일집이 한글 암호로 만든 압축을 이 PC 의 반디집 콘솔(bz.exe t — 시험만)이 푸는가.
/// 반디집이 없으면 자동으로 건너뛴다. 2026-10-12 반디집 7.46 실측: ZipCrypto(CP949) 0, AES(UTF-8) 14, 7z 헤더 암호(일집이 만들 수 있을 때) 0.
/// ('반디집이 만든 것 → 일집'은 BandizipFixtureTests.반디집_한글암호_ZIP이_풀린다 가 반디집 픽스처로 지킨다)
/// </summary>
public class BandizipCliTests
{
    /// <summary>계약: 일집 ZipCrypto + 한글 암호(CP949 로 씀)는 반디집이 맞는 암호로 푼다.</summary>
    [BandizipCliFact]
    public async Task 일집_ZipCrypto_한글암호는_반디집이_푼다()
    {
        using var tmp = new TempDir("bzzc");
        string arc = await Make(tmp, ZipEncryption.ZipCrypto);
        Assert.Equal(0, await BandizipCli.TestAsync(arc, FixtureCatalog.KoreanPassword));
    }

    /// <summary>
    /// 기록: 일집 AES + 한글 암호(UTF-8 로만 쓸 수 있음)는 반디집이 맞는 암호로도 못 푼다(종료 코드 ≠ 0, 실측 14 'Invalid Password')
    /// — 압축 옵션 창이 AES 한글 암호에 경고를 띄우는 이유. SharpZipLib 가 AES 암호를 CP949 로 쓸 수 있게 되면 이 기록이 바뀐다.
    /// </summary>
    [BandizipCliFact, Trait(Kinds.Key, Kinds.Record)]
    public async Task 기록_일집_AES_한글암호는_반디집이_못_푼다()
    {
        using var tmp = new TempDir("bzaes");
        string arc = await Make(tmp, ZipEncryption.Aes256);
        Assert.NotEqual(0, await BandizipCli.TestAsync(arc, FixtureCatalog.KoreanPassword));
    }

    /// <summary>대조: 영문 암호면 AES 도 반디집이 푼다(암호 바이트가 같음).</summary>
    [BandizipCliFact]
    public async Task 일집_AES_영문암호는_반디집이_푼다()
    {
        using var tmp = new TempDir("bzaesa");
        string src = SampleTree.Create(tmp.Dir("src"));
        string arc = tmp.P("aes_ascii.zip");
        await new ZipArchiveService().CompressAsync(new[] { src }, arc, new CompressionOptions { Password = FixtureCatalog.DefaultPassword });
        Assert.Equal(0, await BandizipCli.TestAsync(arc, FixtureCatalog.DefaultPassword));
    }

    // 7z 한글 암호 + 헤더 암호(실측 0)는 일집이 7z 를 만들 수 있게 되는 W1-B(7z.dll) 이후 시험한다 — 지금 일집은 7z 를 만들 수 없다(I2-1).

    private static async Task<string> Make(TempDir tmp, ZipEncryption encryption)
    {
        string src = SampleTree.Create(tmp.Dir("src"));
        string arc = tmp.P($"korean_{encryption}.zip");
        await new ZipArchiveService().CompressAsync(new[] { src }, arc,
            new CompressionOptions { Password = FixtureCatalog.KoreanPassword, Encryption = encryption });
        return arc;
    }
}
