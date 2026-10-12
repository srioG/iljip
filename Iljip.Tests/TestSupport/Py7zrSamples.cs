using System.Text;

namespace Iljip.Tests.TestSupport;

/// <summary>
/// 다른 프로그램(파이썬 py7zr 1.1.0, header_encryption=True)이 만든 '파일 이름까지 암호화한' 7z 두 개(W2-02, I1-7 — 일집이 만든 것만으로 시험하지 않게).
/// 426바이트씩이라 픽스처 파일 대신 base64 로 둔다(매니페스트·FixtureCoverageTests 밖). 만든 스크립트(요지):
/// <code>
/// with py7zr.SevenZipFile(name, "w", password=pw, header_encryption=True) as z:
///     z.writeall("src/자료", "자료"); z.write("src/readme.txt", "readme.txt")
/// </code>
/// 내용: 자료/(폴더), 자료/견적서.txt(55B, UTF-8 '테스트용 가상 견적서 — py7zr 헤더 암호' + CRLF), 자료/빈 폴더(빈 폴더), 자료/빈파일.txt(0B),
/// readme.txt(31B). 수정 시각 2025-03-04 05:06:08(만든 PC 현지). 암호는 <see cref="FixtureCatalog.DefaultPassword"/>·<see cref="FixtureCatalog.KoreanPassword"/>.
/// </summary>
public static class Py7zrSamples
{
    /// <summary>암호 'Iljip-Test-1234', 파일 이름 암호화.</summary>
    public const string HeaderEncryptedBase64 =
        "N3q8ryccAAT+22EzYAEAAAAAAAAqAAAAAAAAAD0ljRNg3TMB8DMOI6VRqL2OXH+FRuIzSNOE5cAdPT+EXmOoZaqg5o1dld3UVg0WkaMI8rAK5T5wB9o8PKB7aMbud68Ksz8Ej4swxDV9UVxl68K/2UhdTwJpziMYijWXpDwaWhnj4yFkwBGKJZ5ynhju8cEhj00wf3/2HWZgZulku7rxWmu5oTw2BS3HQbxATtkSEPUcFd+4MJ4KOru/wKPfPkOALJjdRPW8d6uyKytdr6pj/YjWAdspGLIe3imDmW7nXGO1AZxl1Gi3xqN54DzyWiRJAk10yZNLoEGhJK1LxdJkfQQ3mTYbIonnvYJgBwffeCQcWfRQQM3tmi4UA2RhIXo2MnGbNmboawQqMXSFd5F/pz6icfX4dnz90kTNFm/W6p0rvDoK66MvrBQ9xsTZ8FJgZYqTnQcaO8vx96FEDBqEVGJlP03KdsjyIH4lLanErPoMGfmJ9N0UAkifKURN/i0sFwZgAQmBAAAHCwEAASQG8QcBElMPTmMftH+4nxsRCJhSBm4ZzgyA8QAA";

    /// <summary>암호 '테스트암호'(한글), 파일 이름 암호화.</summary>
    public const string HeaderEncryptedKoreanBase64 =
        "N3q8ryccAAS7k4RhYAEAAAAAAAAqAAAAAAAAAKt31kiymuZCch88ZQhxEKnC9QsCJmjSemPvfrkq1+7GTHM+UBwd1ORcsiLnrMh5EhsEdg2Q+8ZvRN2cEb381uQkhw9NHoM2i4Cm+i1SL48I5bSXtxWJCgwztdtihN0elki4I3Xb4Hi+hVqY94E7DgkP2Oj9XmlyhrL4DMFTBsCIF8syMEwPKlJHIcjh1811NUzLrWzrzgMxq04U586kg0H86MgXKwlaPUgpLF9BPK+z3T8OelwCI8QLFZ2XdblkwDElQzgrg7MyNMXcfiAexSINWBarjbnqIw34vCIkWijyynhXYwov8VP9LhxXocLC3FcfDAazwXKDkXdRguaQVyGdDBIb5UtbMDbaQLIDawpaR5XiZcvrPpotn2ix6+6laO4fDaXBo3IKwDmm3Esgj6p6+YiXy0jbaQB7yHuZ4Z0PlhcLS3lQK1J3QDFH47O3E+0P7EkR11YMNgaOHFnsB6Ly1mHyFwZgAQmBAAAHCwEAASQG8QcBElMPIMzgYg/dFenExL0asimJJAyA8QAA";

    private static readonly byte[] Quote = Encoding.UTF8.GetBytes("테스트용 가상 견적서 — py7zr 헤더 암호\r\n");
    private static readonly byte[] Readme = Encoding.ASCII.GetBytes("py7zr header encrypted sample\r\n");

    /// <summary>풀린 결과 기대(폴더 포함, '/' 구분).</summary>
    public static List<ExpectedEntry> Expected() => new()
    {
        new("자료", 0, null, true),
        new("자료/견적서.txt", Quote.Length, TreeAssert.Sha256(Quote), false),
        new("자료/빈 폴더", 0, null, true),
        new("자료/빈파일.txt", 0, TreeAssert.Sha256(Array.Empty<byte>()), false),
        new("readme.txt", Readme.Length, TreeAssert.Sha256(Readme), false),
    };

    /// <summary>표본을 <paramref name="dir"/> 에 <paramref name="name"/> 으로 쓰고 (경로, 암호)를 돌려준다.</summary>
    public static (string Path, string Password) Write(string dir, bool koreanPassword, string? name = null)
    {
        string path = System.IO.Path.Combine(dir, name ?? (koreanPassword ? "py7z_hdr_ko.7z" : "py7z_hdr.7z"));
        File.WriteAllBytes(path, Convert.FromBase64String(koreanPassword ? HeaderEncryptedKoreanBase64 : HeaderEncryptedBase64));
        return (path, koreanPassword ? FixtureCatalog.KoreanPassword : FixtureCatalog.DefaultPassword);
    }
}
