using Iljip.Models;
using Iljip.Services;
using Iljip.Tests.TestSupport;
using Iljip.Views;

namespace Iljip.Tests.Contract;

/// <summary>
/// [W2-02, I2-1] 압축 옵션 창의 칸 규칙(<see cref="CompressionOptionsRules"/> — 창은 띄우지 않고 순수 함수로 시험).
/// .7z: 솔리드·'파일 이름도 암호로 숨기기'를 보이고 ZIP 암호 방식 칸은 숨김, 한글 암호 경고 없음, 이름 암호화는 암호가 있을 때만.
/// .zip: 예전 그대로(암호 방식 칸·한글 암호 경고). TAR·GZ·BZ2: 암호 칸을 끔. 수준 글자는 저장만·빠름·보통·최대.
/// (창의 실제 모양·체크 상자 켜짐은 MANUAL_CHECKLIST)
/// </summary>
public class CompressionOptionsRulesTests
{
    [Fact]
    public void 칸_모양은_형식에_따라_다르다()
    {
        var zip = CompressionOptionsRules.LayoutFor(".zip");
        Assert.True(zip.PasswordEnabled);
        Assert.True(zip.ShowZipEncryption);
        Assert.False(zip.ShowSevenZipOptions);

        var sz = CompressionOptionsRules.LayoutFor(".7z");
        Assert.True(sz.PasswordEnabled);
        Assert.False(sz.ShowZipEncryption);
        Assert.True(sz.ShowSevenZipOptions);
        Assert.Contains("파일 이름도 암호로 숨기기", sz.Hint);
        Assert.Equal(sz, CompressionOptionsRules.LayoutFor(".7Z"));   // 대문자 확장자도 같게

        foreach (var ext in new[] { ".tar", ".gz", ".bz2" })
        {
            var l = CompressionOptionsRules.LayoutFor(ext);
            Assert.False(l.PasswordEnabled, ext);
            Assert.False(l.ShowZipEncryption, ext);
            Assert.False(l.ShowSevenZipOptions, ext);
            Assert.Contains("ZIP이나 7Z", l.Hint);
        }
    }

    [Fact]
    public void 수준_글자는_저장만_빠름_보통_최대()
    {
        Assert.Equal(4, CompressionOptionsRules.LevelLabels.Length);
        Assert.StartsWith("저장만", CompressionOptionsRules.LevelLabels[0]);
        Assert.StartsWith("빠름", CompressionOptionsRules.LevelLabels[1]);
        Assert.StartsWith("보통", CompressionOptionsRules.LevelLabels[2]);
        Assert.StartsWith("최대", CompressionOptionsRules.LevelLabels[3]);
        Assert.Equal(new[] { CompressionLevelOption.Store, CompressionLevelOption.Fastest, CompressionLevelOption.Normal, CompressionLevelOption.Maximum },
            Enumerable.Range(0, 4).Select(CompressionOptionsRules.LevelOf));
        Assert.Equal(CompressionLevelOption.Normal, CompressionOptionsRules.LevelOf(-1));   // 아무것도 안 고름 = 보통
    }

    /// <summary>'파일 이름도 암호로 숨기기'는 7z 이고 암호를 넣었을 때만 고를 수 있다.</summary>
    [Theory]
    [InlineData(".7z", "암호1234", true)]
    [InlineData(".7z", "", false)]
    [InlineData(".7z", null, false)]
    [InlineData(".zip", "암호1234", false)]
    [InlineData(".tar", "암호1234", false)]
    public void 파일이름_암호화는_7z에_암호가_있을_때만(string ext, string? password, bool can) =>
        Assert.Equal(can, CompressionOptionsRules.CanEncryptFileNames(ext, password));

    /// <summary>한글 암호 호환 경고는 ZIP 에만(7z 는 암호를 UTF-16 으로 써서 반디집·7-Zip 과 맞음 — BandizipCliTests 로 확인).</summary>
    [Fact]
    public void 한글_암호_경고는_ZIP에만()
    {
        Assert.True(CompressionOptionsRules.ChecksPasswordCompat(".zip"));
        Assert.False(CompressionOptionsRules.ChecksPasswordCompat(".7z"));
        Assert.False(CompressionOptionsRules.ChecksPasswordCompat(".tar"));
        // 같은 한글 암호를 ZIP AES 로 판정하면 경고 대상이다(창은 7z 에서 이 판정을 부르지 않는다)
        Assert.Equal(PasswordCompat.NonAsciiAes, PasswordPolicy.CheckCompat(FixtureCatalog.KoreanPassword, ".zip", ZipEncryption.Aes256));
    }

    [Fact]
    public void 결과는_형식에_맞는_값만_담는다()
    {
        var sz = CompressionOptionsRules.Build(3, "암호1234", ".7z", ZipEncryption.ZipCrypto, solid: false, encryptFileNames: true);
        Assert.Equal(CompressionLevelOption.Maximum, sz.Level);
        Assert.Equal("암호1234", sz.Password);
        Assert.Equal(ZipEncryption.Aes256, sz.Encryption);   // ZipCrypto 는 ZIP 에만
        Assert.False(sz.Solid);
        Assert.True(sz.EncryptFileNames);

        var noPw = CompressionOptionsRules.Build(2, "", ".7z", ZipEncryption.Aes256, solid: true, encryptFileNames: true);
        Assert.Null(noPw.Password);
        Assert.False(noPw.EncryptFileNames);   // 암호 없이 이름 암호화는 없음
        Assert.True(noPw.Solid);

        var zip = CompressionOptionsRules.Build(0, "pw", ".zip", ZipEncryption.ZipCrypto, solid: false, encryptFileNames: true);
        Assert.Equal(CompressionLevelOption.Store, zip.Level);
        Assert.Equal(ZipEncryption.ZipCrypto, zip.Encryption);
        Assert.True(zip.Solid);                // ZIP 에는 솔리드가 없음 — 기본값 그대로
        Assert.False(zip.EncryptFileNames);

        var tar = CompressionOptionsRules.Build(1, "pw", ".tar", ZipEncryption.Aes256, solid: true, encryptFileNames: true);
        Assert.Null(tar.Password);             // 암호 칸이 꺼진 형식
        Assert.False(tar.HasPassword);
        Assert.Equal(CompressionLevelOption.Fastest, tar.Level);
    }

    /// <summary>계약(소스): 창의 7z 칸 문구와 이름(XAML) — 체크 상자 두 개, 솔리드 기본 켜짐, 이름 암호화는 처음엔 꺼짐(암호를 넣으면 켜짐).</summary>
    [Fact]
    public void 창_XAML에_7z_칸이_있다()
    {
        string xaml = File.ReadAllText(RepoFiles.PathOf("Iljip", "Views", "CompressionOptionsDialog.xaml"));
        Assert.Contains("x:Name=\"SevenZipPanel\"", xaml);
        Assert.Contains("솔리드 압축(더 작게, 일부만 꺼내기는 느림)", xaml);
        Assert.Contains("파일 이름도 암호로 숨기기", xaml);
        Assert.Matches("x:Name=\"SolidCheck\"\\s+IsChecked=\"True\"", xaml);
        Assert.Matches("x:Name=\"EncryptNamesCheck\"\\s+IsEnabled=\"False\"", xaml);
        string code = File.ReadAllText(RepoFiles.PathOf("Iljip", "Views", "CompressionOptionsDialog.xaml.cs"));
        Assert.Contains("CompressionOptionsRules.LayoutFor", code);
        Assert.Contains("CompressionOptionsRules.Build", code);
        Assert.Contains("CompressionOptionsRules.ChecksPasswordCompat", code);
    }
}
