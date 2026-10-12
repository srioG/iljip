using System.Text;
using Iljip.Models;
using Iljip.Services;
using Iljip.Tests.TestSupport;

namespace Iljip.Tests.Contract;

/// <summary>
/// [W1-C] ZIP 암호 판정(<see cref="ZipPasswordProbe"/>) — 풀기 전에, 아무것도 만들기 전에 '어떤 암호 바이트로 맞는가'를 본다(B3·B4·B5 의 핵심).
/// 반디집 한글 암호 = CP949, 일집 AES = UTF-8, ZipCrypto 는 모든 항목의 1바이트 검사를 통과해야 '맞음'. 판정할 수 없는 모양은 예외 없이 Unknown.
/// </summary>
public class ZipPasswordProbeTests
{
    [Theory]
    [InlineData("zip_zipcrypto", FixtureCatalog.DefaultPassword, PasswordBytes.Utf8)]
    [InlineData("zip_aes256", FixtureCatalog.DefaultPassword, PasswordBytes.Utf8)]
    [InlineData("zip_zipcrypto_korean_pw", FixtureCatalog.KoreanPassword, PasswordBytes.Cp949)]
    [InlineData("zip_aes256_korean_pw", FixtureCatalog.KoreanPassword, PasswordBytes.Cp949)]
    [InlineData("zip_zipcrypto", "wrong", PasswordBytes.None)]
    [InlineData("zip_aes256", "wrong", PasswordBytes.None)]
    [InlineData("zip_zipcrypto_korean_pw", "테스트암오", PasswordBytes.None)]
    [InlineData("zip_aes256_korean_pw", "테스트암오", PasswordBytes.None)]
    public void 반디집_암호_ZIP의_암호_바이트를_판정한다(string id, string password, PasswordBytes expected)
    {
        var r = ZipPasswordProbe.Probe(FixtureCatalog.Get(id).FullPath, password);
        Assert.True(r.AnyEncrypted);
        Assert.Equal(expected, r.Match);
        Assert.Equal(id.Contains("aes"), r.HasAes);
        Assert.Equal(id.Contains("zipcrypto"), r.HasZipCrypto);
        if (expected is PasswordBytes.Utf8 or PasswordBytes.Cp949) Assert.NotNull(r.MatchedPasswordBytes);
    }

    /// <summary>첫 항목의 1바이트 검사를 우연히 통과하는 틀린 암호도 둘째 항목부터 걸러져 None(예전 B4·B5 의 근본 원인).</summary>
    [Fact]
    public void 첫_항목만_통과하는_틀린_암호는_None()
    {
        string arc = FixtureCatalog.Get("zip_zipcrypto").FullPath;
        foreach (var lucky in ZipCryptoLucky.WrongPasswordsPassingCheck(arc, FixtureCatalog.DefaultPassword).Take(5))
            Assert.Equal(PasswordBytes.None, ZipPasswordProbe.Probe(arc, lucky).Match);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void 암호를_안_주면_암호항목이_있어도_None(string? password)
    {
        var r = ZipPasswordProbe.Probe(FixtureCatalog.Get("zip_aes256").FullPath, password);
        Assert.True(r.AnyEncrypted);
        Assert.Equal(PasswordBytes.None, r.Match);
    }

    [Theory]
    [InlineData("zip_default")]
    [InlineData("zip_cp949")]
    [InlineData("zip_store_l0")]
    public void 암호_없는_ZIP은_NotApplicable(string id)
    {
        var r = ZipPasswordProbe.Probe(FixtureCatalog.Get(id).FullPath, "아무 암호");
        Assert.False(r.AnyEncrypted);
        Assert.Equal(PasswordBytes.NotApplicable, r.Match);
        Assert.Equal(FixtureCatalog.Get(id).Expected.Count, r.Entries.Count);   // 반디집 ZIP 은 폴더 항목까지 모두 들어 있음
    }

    /// <summary>고른 항목(include)에 암호 항목이 없으면 암호가 필요 없다(폴더 항목만 고름).</summary>
    [Fact]
    public void 고른_항목에_암호항목이_없으면_NotApplicable()
    {
        var r = ZipPasswordProbe.Probe(FixtureCatalog.Get("zip_aes256").FullPath, null, name => name.EndsWith('/'));
        Assert.False(r.AnyEncrypted);
        Assert.Equal(PasswordBytes.NotApplicable, r.Match);
    }

    /// <summary>일집이 만든 것: AES 는 UTF-8(SharpZipLib 고정), ZipCrypto 한글 암호는 CP949(반디집과 같게), ZipCrypto 이모지 암호는 UTF-8.</summary>
    [Theory]
    [InlineData(ZipEncryption.Aes256, FixtureCatalog.KoreanPassword, PasswordBytes.Utf8)]
    [InlineData(ZipEncryption.ZipCrypto, FixtureCatalog.KoreanPassword, PasswordBytes.Cp949)]
    [InlineData(ZipEncryption.ZipCrypto, "암호😀", PasswordBytes.Utf8)]
    [InlineData(ZipEncryption.ZipCrypto, "ascii-1234", PasswordBytes.Utf8)]
    public async Task 일집이_만든_암호_ZIP의_암호_바이트(ZipEncryption encryption, string password, PasswordBytes expected)
    {
        using var tmp = new TempDir("prb");
        string src = SampleTree.Create(tmp.Dir("src"));
        string arc = tmp.P("made.zip");
        await new ZipArchiveService().CompressAsync(new[] { src }, arc, new CompressionOptions { Password = password, Encryption = encryption });
        var r = ZipPasswordProbe.Probe(arc, password);
        Assert.Equal(expected, r.Match);
        Assert.Equal(encryption == ZipEncryption.Aes256, r.HasAes);
    }

    /// <summary>판정할 수 없는 모양은 예외 없이 Unknown(엔진·CRC·롤백 장부가 맡음).</summary>
    [Theory]
    [InlineData("garbage")]
    [InlineData("empty")]
    [InlineData("half")]
    [InlineData("split_missing_piece")]
    [InlineData("missing")]
    [InlineData("prefixed")]
    public void 판정할_수_없는_모양은_예외없이_Unknown(string how)
    {
        using var tmp = new TempDir("prbu");
        string path = tmp.P(how + ".zip");
        byte[] zc = File.ReadAllBytes(FixtureCatalog.Get("zip_zipcrypto").FullPath);
        switch (how)
        {
            case "garbage": File.WriteAllBytes(path, Enumerable.Range(0, 5000).Select(i => (byte)(i * 7 + 3)).ToArray()); break;
            case "empty": File.WriteAllBytes(path, Array.Empty<byte>()); break;
            case "half": File.WriteAllBytes(path, zc[..(zc.Length / 2)]); break;
            case "split_missing_piece":
                // 분할(.z01·.z02)의 마지막 조각만 — 앞 조각이 없어 판정 못 함(W2-03 전에는 조각이 다 있어도 'split_last' 로 Unknown 이었다 — 아래 새 시험)
                File.Copy(FixtureCatalog.Get("zip_split").FullPath, path);
                break;
            case "missing": break;   // 파일 없음
            case "prefixed": File.WriteAllBytes(path, new byte[] { (byte)'M', (byte)'Z' }.Concat(new byte[4094]).Concat(zc).ToArray()); break;   // SFX 처럼 앞에 다른 것
        }
        var r = ZipPasswordProbe.Probe(path, FixtureCatalog.DefaultPassword);
        Assert.Equal(PasswordBytes.Unknown, r.Match);
        Assert.False(r.AnyEncrypted);
    }

    /// <summary>
    /// W2-03: PKZIP 나눔(.z01·.z02 … .zip)도 조각이 다 있으면 세트를 이어 보통 ZIP 처럼 판정한다(<see cref="SpannedZipStream"/>) —
    /// 반디집 나눔 픽스처는 암호가 없어 NotApplicable, 목록은 manifest 의 항목 수 그대로. 반디집이 만든 한글 ZipCrypto 암호 ZIP 을 일집이 나눈 것은
    /// CP949 로 맞는다(예전엔 마지막 조각만 봐서 Unknown → 엔진이 UTF-8 로만 맞춰 보다 '암호가 맞지 않아요').
    /// </summary>
    [Fact]
    public void 나눔_ZIP도_조각이_다_있으면_판정한다()
    {
        var fx = FixtureCatalog.Get("zip_split");
        var r = ZipPasswordProbe.Probe(fx.FullPath, FixtureCatalog.DefaultPassword);
        Assert.Equal(PasswordBytes.NotApplicable, r.Match);
        Assert.False(r.AnyEncrypted);
        Assert.Equal(fx.Expected.Count, r.Entries.Count);

        using var tmp = new TempDir("prbs");
        string work = tmp.P("한글암호.zip");
        File.Copy(FixtureCatalog.Get("zip_zipcrypto_korean_pw").FullPath, work);
        var pieces = ZipSpanWriter.Split(work, tmp.P("한글암호나눔"), 1500, CancellationToken.None);
        Assert.True(pieces.Count >= 3);
        var k = ZipPasswordProbe.Probe(pieces[^1], FixtureCatalog.KoreanPassword);
        Assert.Equal(PasswordBytes.Cp949, k.Match);
        Assert.True(k.HasZipCrypto);
        Assert.Equal(PasswordBytes.None, ZipPasswordProbe.Probe(pieces[^1], "틀린암호").Match);
    }

    /// <summary>진짜 자동 풀림 exe(반디집 SFX, 로컬 전용 픽스처)도 예외 없이 Unknown.</summary>
    [FixtureFact("local/sfx_default.exe")]
    public void 자동풀림_exe는_Unknown()
    {
        Assert.Equal(PasswordBytes.Unknown, ZipPasswordProbe.Probe(FixtureCatalog.PathOf("local/sfx_default.exe"), "x").Match);
    }

    [Theory]
    [InlineData("테스트암호", true)]
    [InlineData("abc123", true)]
    [InlineData("암호😀", false)]
    public void CP949로_바꿀_수_있는지(string password, bool encodable)
    {
        Assert.Equal(encodable, ZipPasswordProbe.TryCp949Bytes(password) is not null);
    }

    // ───── W1-C 1차 검증 지적: 두 후보가 모두 통과하는 한글 암호, 근거의 세기 ─────

    internal const string KoreanPw = "한글암호";

    /// <summary>(쓴 바이트, 다른 후보 바이트). 한글 암호의 UTF-8·CP949 바이트.</summary>
    internal static (byte[] Written, byte[] Other) Candidates(PasswordBytes writtenWith)
    {
        byte[] utf8 = Encoding.UTF8.GetBytes(KoreanPw), cp949 = ZipPasswordProbe.TryCp949Bytes(KoreanPw)!;
        return writtenWith == PasswordBytes.Cp949 ? (cp949, utf8) : (utf8, cp949);
    }

    /// <summary>
    /// 항목 하나짜리 ZipCrypto 에서 두 후보(UTF-8·CP949)가 다 1바이트 검사를 통과하면(약 1/128) 가릴 근거가 없다 —
    /// CP949(반디집·알집·일집이 한글 ZipCrypto 암호를 쓰는 방식)를 먼저, UTF-8 을 대안으로 남긴다. 확인됨은 아님.
    /// 예전: UTF-8 을 먼저 보고 거기서 멈춰, CP949 로 쓴 압축을 맞는 암호로도 영영 못 풀었다.
    /// </summary>
    [Theory]
    [InlineData(PasswordBytes.Cp949)]
    [InlineData(PasswordBytes.Utf8)]
    public void 두_후보가_모두_통과하고_못_가리면_CP949_먼저_UTF8은_대안(PasswordBytes writtenWith)
    {
        using var tmp = new TempDir("prbamb");
        string arc = tmp.P("모호.zip");
        var (written, other) = Candidates(writtenWith);
        RawZipCrypto.Write(arc, new RawZipCrypto.Item("보고서.txt", Encoding.UTF8.GetBytes("보고서 내용"), written, HeaderFilter: RawZipCrypto.OtherPasses(other)));

        var r = ZipPasswordProbe.Probe(arc, KoreanPw);
        Assert.Equal(PasswordBytes.Cp949, r.Match);
        Assert.Equal(ZipPasswordProbe.TryCp949Bytes(KoreanPw), r.MatchedPasswordBytes);
        Assert.Equal(PasswordBytes.Utf8, r.AlternateMatch);
        Assert.Equal(Encoding.UTF8.GetBytes(KoreanPw), r.AlternatePasswordBytes);
        Assert.False(r.Confirmed);
    }

    /// <summary>
    /// 여러 항목 중 하나만 골라 풀 때(더블클릭 열기·선택 해제) 고른 항목에서 두 후보가 다 통과하면, 고르지 않은 항목으로 가린다 —
    /// 대안 없이 맞는 쪽 하나, 그리고 고르지 않은 항목의 근거까지 더해 확인됨.
    /// </summary>
    [Theory]
    [InlineData(PasswordBytes.Cp949)]
    [InlineData(PasswordBytes.Utf8)]
    public void 고른_항목이_모호하면_고르지_않은_항목으로_가린다(PasswordBytes writtenWith)
    {
        using var tmp = new TempDir("prbsel");
        string arc = tmp.P("여럿.zip");
        var (written, other) = Candidates(writtenWith);
        RawZipCrypto.Write(arc,
            new RawZipCrypto.Item("자료/고른.txt", Encoding.UTF8.GetBytes("고른 파일"), written, HeaderFilter: RawZipCrypto.OtherPasses(other)),
            new RawZipCrypto.Item("자료/다른1.txt", Encoding.UTF8.GetBytes("다른 파일 1"), written, HeaderFilter: RawZipCrypto.OtherPasses(other, passes: false)),
            new RawZipCrypto.Item("자료/다른2.txt", Encoding.UTF8.GetBytes("다른 파일 2"), written, HeaderFilter: RawZipCrypto.OtherPasses(other, passes: false)));

        var r = ZipPasswordProbe.Probe(arc, KoreanPw, name => name == "자료/고른.txt");
        Assert.Equal(writtenWith, r.Match);
        Assert.Equal(written, r.MatchedPasswordBytes);
        Assert.Equal(PasswordBytes.None, r.AlternateMatch);
        Assert.Null(r.AlternatePasswordBytes);
        Assert.True(r.Confirmed, "고른 항목 1바이트 + 고르지 않은 항목 2바이트 = 24비트인데 확인됨이 아님");

        var whole = ZipPasswordProbe.Probe(arc, KoreanPw);
        Assert.Equal(writtenWith, whole.Match);
        Assert.Equal(PasswordBytes.None, whole.AlternateMatch);
    }

    /// <summary>
    /// 확인됨(근거 16비트 이상 = 틀린 암호가 우연히 통과할 확률 1/65536 이하): 반디집 여러 항목 ZipCrypto·AES 는 예,
    /// 항목 하나짜리 ZipCrypto(확인 바이트 하나 = 8비트)는 아니오, 둘이면 예. 확인됐을 때만 풀다 나는 데이터 오류를 '손상'으로 본다.
    /// </summary>
    [Fact]
    public void 근거가_16비트_이상이면_확인됨()
    {
        Assert.True(ZipPasswordProbe.Probe(FixtureCatalog.Get("zip_zipcrypto").FullPath, FixtureCatalog.DefaultPassword).Confirmed);
        Assert.True(ZipPasswordProbe.Probe(FixtureCatalog.Get("zip_aes256").FullPath, FixtureCatalog.DefaultPassword).Confirmed);
        Assert.True(ZipPasswordProbe.Probe(FixtureCatalog.Get("zip_zipcrypto_korean_pw").FullPath, FixtureCatalog.KoreanPassword).Confirmed);

        using var tmp = new TempDir("prbconf");
        byte[] pw = Encoding.UTF8.GetBytes("ascii-1234");
        string one = tmp.P("one.zip"), two = tmp.P("two.zip");
        RawZipCrypto.Write(one, new RawZipCrypto.Item("a.txt", Encoding.UTF8.GetBytes("aaa"), pw));
        RawZipCrypto.Write(two, new RawZipCrypto.Item("a.txt", Encoding.UTF8.GetBytes("aaa"), pw), new RawZipCrypto.Item("b.txt", Encoding.UTF8.GetBytes("bbb"), pw));

        var r1 = ZipPasswordProbe.Probe(one, "ascii-1234");
        Assert.Equal(PasswordBytes.Utf8, r1.Match);
        Assert.False(r1.Confirmed);
        Assert.True(ZipPasswordProbe.Probe(two, "ascii-1234").Confirmed);
        Assert.True(ZipPasswordProbe.Probe(two, "ascii-1234", n => n == "a.txt").Confirmed, "고른 항목이 하나라도 고르지 않은 항목이 근거를 더해야 함");
    }
}
