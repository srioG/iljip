using System.Text;
using Iljip.Services;
using Iljip.Tests.TestSupport;

namespace Iljip.Tests.Contract;

/// <summary>
/// [범위 10] KoreanFileNameDecoder: 원본 바이트 → 이름. UTF-8(엄격) → CP949(한글이 보이면) → CP437(무손실) 순서.
/// 그리고 실제 ZIP 에서 EFS(UTF-8) 플래그 유무·CP949 원시 이름이 바르게 보이는지.
/// </summary>
public class KoreanFileNameDecoderTests
{
    private static readonly Encoding Cp949 = Encoding.GetEncoding(949);
    private static readonly Encoding Cp437 = Encoding.GetEncoding(437);

    [Theory]
    [InlineData("견적서_2026.txt")]
    [InlineData("하위폴더/보고서 최종.txt")]
    [InlineData("똠방각하 펲시콜라.txt")]   // 완성형 밖 글자(CP949 확장)
    public void CP949_바이트는_한글로_읽는다(string name)
    {
        byte[] b = Cp949.GetBytes(name);
        Assert.Equal(name, KoreanFileNameDecoder.DecodeBytes(b, 0, b.Length));
    }

    [Theory]
    [InlineData("유니코드_이름.txt")]
    [InlineData("日本語ファイル.txt")]
    [InlineData("emoji_😀.txt")]
    [InlineData("café.txt")]
    public void UTF8_바이트는_UTF8로_읽는다(string name)
    {
        byte[] b = Encoding.UTF8.GetBytes(name);
        Assert.Equal(name, KoreanFileNameDecoder.DecodeBytes(b, 0, b.Length));
    }

    [Theory]
    [InlineData("plain.txt")]
    [InlineData("Report (final) v2.txt")]
    [InlineData("a/b/c.d")]
    public void 순수_ASCII는_그대로(string name)
    {
        byte[] b = Encoding.ASCII.GetBytes(name);
        Assert.Equal(name, KoreanFileNameDecoder.DecodeBytes(b, 0, b.Length));
        Assert.Equal(name, KoreanFileNameDecoder.Decode(name));
    }

    /// <summary>UTF-8 도 아니고 CP949 로 읽어도 한글이 안 나오면 CP437 로 1:1 보존(옛 서양 ZIP 의 'café').</summary>
    [Fact]
    public void 한글이_아닌_옛_CP437_이름은_CP437로_보존()
    {
        byte[] b = Cp437.GetBytes("café.txt");   // 0x82 = é
        Assert.Equal("café.txt", KoreanFileNameDecoder.DecodeBytes(b, 0, b.Length));
    }

    [Fact]
    public void 시작위치와_길이를_지킨다()
    {
        byte[] name = Cp949.GetBytes("한글.txt");
        byte[] buf = new byte[name.Length + 6];
        Array.Copy(name, 0, buf, 3, name.Length);
        Assert.Equal("한글.txt", KoreanFileNameDecoder.DecodeBytes(buf, 3, name.Length));
    }

    [Fact]
    public void 빈_입력은_빈_문자열()
    {
        Assert.Equal(string.Empty, KoreanFileNameDecoder.DecodeBytes(Array.Empty<byte>(), 0, 0));
        Assert.Equal(string.Empty, KoreanFileNameDecoder.DecodeBytes(new byte[] { 0x41 }, 0, 0));
        Assert.Equal(string.Empty, KoreanFileNameDecoder.DecodeBytes(null!, 0, 3));
    }

    /// <summary>Decode(문자열) 대비 경로: CP437 로 잘못 읽힌 문자열을 CP949 로 되살리고, 이미 맞는 한글은 그대로 둔다.</summary>
    [Fact]
    public void 문자열_대비경로_Decode()
    {
        string misread = Cp437.GetString(Cp949.GetBytes("보고서.txt"));
        Assert.Equal("보고서.txt", KoreanFileNameDecoder.Decode(misread));
        Assert.Equal("보고서.txt", KoreanFileNameDecoder.Decode("보고서.txt"));
        Assert.Equal(string.Empty, KoreanFileNameDecoder.Decode(string.Empty));
    }

    /// <summary>
    /// 실제 ZIP: EFS 플래그 켠 UTF-8, 플래그 없는 CP949(구형 알집·탐색기), 플래그 없는 UTF-8(맥 등), CP437 서양 이름, ASCII
    /// 를 한 파일에 섞어도 목록 이름이 모두 바르다.
    /// </summary>
    [Fact]
    public async Task ZIP_이름_인코딩_섞여도_바르게_보인다()
    {
        using var tmp = new TempDir("enc");
        string arc = tmp.P("names.zip");
        RawZip.Write(arc,
            new RawZip.Item(Encoding.UTF8.GetBytes("유니코드_플래그.txt"), new byte[] { 1 }, Utf8Flag: true),
            new RawZip.Item(Cp949.GetBytes("완성형_한글.txt"), new byte[] { 2 }, Utf8Flag: false),
            new RawZip.Item(Encoding.UTF8.GetBytes("플래그없는_UTF8.txt"), new byte[] { 3 }, Utf8Flag: false),
            new RawZip.Item(Cp437.GetBytes("café.txt"), new byte[] { 4 }, Utf8Flag: false),
            new RawZip.Item(Encoding.ASCII.GetBytes("plain.txt"), new byte[] { 5 }, Utf8Flag: false));
        var names = (await new ZipArchiveService().ListEntriesAsync(arc)).Select(e => e.Path).ToList();
        Assert.Equal(new[] { "유니코드_플래그.txt", "완성형_한글.txt", "플래그없는_UTF8.txt", "café.txt", "plain.txt" }, names);

        string dest = tmp.P("out");
        await new ZipArchiveService().ExtractAsync(arc, dest);
        // 이름 → 내용(1바이트씩 다름)까지: 이름이 맞아도 내용이 다른 항목과 뒤바뀌면 실패
        var expected = new Dictionary<string, byte>
        {
            ["유니코드_플래그.txt"] = 1, ["완성형_한글.txt"] = 2, ["플래그없는_UTF8.txt"] = 3, ["café.txt"] = 4, ["plain.txt"] = 5,
        };
        Assert.Equal(expected.Keys.OrderBy(x => x, StringComparer.Ordinal), TreeAssert.Files(dest));
        foreach (var (name, b) in expected)
            Assert.Equal(new[] { b }, File.ReadAllBytes(Path.Combine(dest, name)));
    }
}
