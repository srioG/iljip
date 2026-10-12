using System.IO.Compression;
using System.Text;
using Iljip.Services;
using Iljip.Tests.TestSupport;
using SharpSevenZip;

namespace Iljip.Tests.Contract;

/// <summary>
/// [W1-B 2차 검증 지적] Windows 가 그 이름 그대로는 못 만드는 항목 이름 — 모든 해제 경로(7z.dll·SharpCompress·압축된 tar·EGG)가 같은 규칙
/// (<see cref="EntryPaths"/>, 7-Zip·반디집과 같은 방식)으로 바꿔 쓴다:
/// ':'·'?'·'*' 등 → '_'(예전 .tar.gz·.tar·.zip 은 'time 10:00.log' 를 0바이트 'time 10' + 숨은 스트림 '00.log' 로 갈라 내용을 숨겼고,
/// '?' 는 해제를 중간에 멈췄다), 장치 이름(CON·aux·nul.txt) → 앞에 '_', 점만 있는 폴더('...') → '.._'
/// (예전 7z.dll 경로는 임시 폴더 경로가 드러난 'DirectoryNotFound' 원문으로 멈췄다). 화면 목록은 원래 이름 그대로다.
/// 경로 공격(../·절대 경로)은 바꾸기 전 원래 이름으로 막는다.
/// </summary>
public class EntryNameTests
{
    private static readonly (string Name, string Text)[] OddNames =
    {
        ("time 10:00.log", "colon-data"),
        ("q?s*.txt", "wild"),
        ("CON.txt", "con"),
        ("aux", "aux-file"),
        ("nul.txt", "nul"),
        ("lpt1/안.txt", "in-device-dir"),
        (".../evil3.txt", "dots"),
        ("ok.txt:Zone.Identifier", "ads"),
        ("ok.txt", "ok"),
    };

    /// <summary>바꾼 뒤의 기대 이름(대상 폴더 기준, '/' 구분).</summary>
    private static readonly Dictionary<string, string> SafeNames = new(StringComparer.Ordinal)
    {
        ["time 10:00.log"] = "time 10_00.log",
        ["q?s*.txt"] = "q_s_.txt",
        ["CON.txt"] = "_CON.txt",
        ["aux"] = "_aux",
        ["nul.txt"] = "_nul.txt",
        ["lpt1/안.txt"] = "_lpt1/안.txt",
        [".../evil3.txt"] = ".._/evil3.txt",
        ["ok.txt:Zone.Identifier"] = "ok.txt_Zone.Identifier",
        ["ok.txt"] = "ok.txt",
    };

    private static void AssertSafeTree(string dest, IEnumerable<(string Name, string Text)> items, string context)
    {
        var expected = items.ToDictionary(i => SafeNames[i.Name], i => i.Text, StringComparer.Ordinal);
        var files = TreeAssert.Files(dest);
        Assert.True(expected.Keys.OrderBy(x => x, StringComparer.Ordinal).SequenceEqual(files),
            $"{context}: 풀린 파일이 기대와 다름\n  기대: {string.Join(" | ", expected.Keys.OrderBy(x => x, StringComparer.Ordinal))}\n  실제: {string.Join(" | ", files)}");
        foreach (var (rel, text) in expected)
            Assert.Equal(text, File.ReadAllText(Path.Combine(dest, rel)));
        // 숨은 데이터 스트림(ADS)이 생기지 않았다 — ':' 가 든 이름이 'ok.txt' 의 스트림으로 들어가던 것
        foreach (var f in Directory.EnumerateFiles(dest, "*", SearchOption.AllDirectories))
            Assert.False(File.Exists(f + ":Zone.Identifier"), $"{context}: 숨은 스트림이 생김: {f}");
    }

    /// <summary>계약: 이상한 이름이 든 .tar.gz(압축된 tar 처리기)를 풀면 Windows 에서 쓸 수 있는 이름으로 바꿔 내용을 모두 남긴다.</summary>
    [Fact]
    public async Task 이상한_이름의_tar_gz도_바꾼_이름으로_모두_풀린다()
    {
        using var tmp = new TempDir("ntgz");
        string tar = tmp.P("x.tar");
        RawTar.Write(tar, OddNames.Select(n => (n.Name, Encoding.UTF8.GetBytes(n.Text))).ToArray());
        string arc = tmp.P("이름.tar.gz");
        using (var o = File.Create(arc))
        using (var gz = new GZipStream(o, System.IO.Compression.CompressionLevel.Fastest))
            gz.Write(File.ReadAllBytes(tar));
        var svc = Svc.For(arc);
        Assert.IsType<CompressedTarArchiveService>(svc);
        var list = await svc.ListEntriesAsync(arc);
        Assert.Contains(list, e => e.Path == "time 10:00.log");   // 화면 목록은 원래 이름
        string dest = tmp.P("out");
        await svc.ExtractAsync(arc, dest);
        AssertSafeTree(dest, OddNames, ".tar.gz");
    }

    /// <summary>계약: .tar(SharpCompress 공통 해제)도 같은 규칙 — 예전엔 같은 문제가 이미 있었다.</summary>
    [Fact]
    public async Task 이상한_이름의_tar도_바꾼_이름으로_모두_풀린다()
    {
        using var tmp = new TempDir("ntar");
        string arc = tmp.P("이름.tar");
        RawTar.Write(arc, OddNames.Select(n => (n.Name, Encoding.UTF8.GetBytes(n.Text))).ToArray());
        string dest = tmp.P("out");
        await Svc.For(arc).ExtractAsync(arc, dest);
        AssertSafeTree(dest, OddNames, ".tar");
    }

    /// <summary>계약: ZIP(SharpCompress 공통 해제)도 같은 규칙.</summary>
    [Fact]
    public async Task 이상한_이름의_zip도_바꾼_이름으로_모두_풀린다()
    {
        using var tmp = new TempDir("nzip");
        string arc = tmp.P("이름.zip");
        RawZip.Write(arc, OddNames.Select(n => RawZip.Utf8(n.Name, n.Text)).ToArray());
        string dest = tmp.P("out");
        await Svc.For(arc).ExtractAsync(arc, dest);
        AssertSafeTree(dest, OddNames, ".zip");
    }

    /// <summary>계약: EGG(EggDotNet 처리기)도 같은 규칙.</summary>
    [Fact]
    public async Task 이상한_이름의_egg도_바꾼_이름으로_모두_풀린다()
    {
        using var tmp = new TempDir("negg");
        string arc = tmp.P("이름.egg");
        RawEgg.Write(arc, OddNames.Select(n => new RawEgg.Item(n.Name, Encoding.UTF8.GetBytes(n.Text))).ToList());
        string dest = tmp.P("out");
        await Svc.For(arc).ExtractAsync(arc, dest);
        AssertSafeTree(dest, OddNames, ".egg");
    }

    /// <summary>
    /// 계약: 7z.dll 경로도 같은 이름으로 — 장치 이름·점만 있는 폴더는 7z.dll 이 임시 폴더에 그대로 쓰지 못해 따로 꺼낸다(솔리드에서도).
    /// 예전: 'CON.txt'·'aux'·'nul.txt' 가 그대로 만들어져 탐색기·PowerShell 로 다루기 어려웠고, '.../evil3.txt' 는 임시 폴더 경로가 드러난
    /// 'DirectoryNotFound' 원문으로 해제가 중간에 멈췄다. 선택 해제로 그런 항목만 골라도 된다.
    /// </summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task 이상한_이름의_7z도_바꾼_이름으로_모두_풀린다(bool solid)
    {
        using var tmp = new TempDir("n7z");
        string arc = tmp.P("이름.7z");
        Make7z(arc, OddNames, solid);
        var svc = Svc.For(arc);
        var list = await svc.ListEntriesAsync(arc);
        Assert.Contains(list, e => e.Path == "CON.txt");
        string dest = tmp.P("out");
        await svc.ExtractAsync(arc, dest);
        AssertSafeTree(dest, OddNames, $"7z(solid={solid})");
        Assert.False(Directory.EnumerateDirectories(dest, SevenZipEngine.StagingPrefix + "*").Any(), "임시 폴더가 남음");

        string some = tmp.P("some");
        await svc.ExtractEntriesAsync(arc, some, new[] { "CON.txt", "...", "ok.txt" });
        AssertSafeTree(some, OddNames.Where(n => n.Name is "CON.txt" or ".../evil3.txt" or "ok.txt"), "7z 선택 해제");
    }

    /// <summary>
    /// 계약: 앞에 '/' 가 붙은 항목('/abs.txt')을 선택 해제로 고르면 아무 알림 없이 빠지지 않고, 전체 해제처럼 '위험한 경로'로 알린다
    /// (아무것도 쓰기 전에). 예전엔 선택 비교가 앞 '/' 를 떼지 않아 조용히 '완료'로 끝났다.
    /// </summary>
    [Fact]
    public async Task 앞에_슬래시가_붙은_항목을_골라도_조용히_빠지지_않는다()
    {
        using var tmp = new TempDir("nabs");
        string arc = tmp.P("abs.7z");
        Make7z(arc, new[] { ("ok.txt", "ok"), ("/abs.txt", "EVIL") }, solid: false);
        var svc = Svc.For(arc);
        var abs = (await svc.ListEntriesAsync(arc)).Single(e => e.Path.EndsWith("abs.txt", StringComparison.Ordinal));
        string dest = tmp.P("out");
        var ex = await Svc.Catch(() => svc.ExtractEntriesAsync(arc, dest, new[] { abs.Path }));
        var ide = Assert.IsType<InvalidDataException>(ex);
        Assert.StartsWith(ErrorMessages.UnsafePathPrefix, ide.Message);
        TreeAssert.NoFiles(dest);
        Assert.False(File.Exists(Path.Combine(Path.GetPathRoot(dest)!, "abs.txt")));
    }

    /// <summary>EntryPaths 규칙 자체(7-Zip·반디집과 같은 모양).</summary>
    [Theory]
    [InlineData("a/b.txt", "a\\b.txt")]
    [InlineData("time 10:00.log", "time 10_00.log")]
    [InlineData("CON", "_CON")]
    [InlineData("con.tar.gz", "_con.tar.gz")]
    [InlineData("Com1.txt", "_Com1.txt")]
    [InlineData("console.txt", "console.txt")]
    [InlineData("auxiliary/aux.c", "auxiliary\\_aux.c")]
    [InlineData(".../x", ".._\\x")]
    [InlineData("a/../b", "a\\..\\b")]
    [InlineData("dot.", "dot.")]
    public void 이름_바꾸기_규칙(string name, string expected) => Assert.Equal(expected, EntryPaths.ToSafeRelative(name));

    private static void Make7z(string path, IEnumerable<(string Name, string Text)> items, bool solid)
    {
        var dict = items.ToDictionary(i => i.Name, i => new StreamWithAttributes(new MemoryStream(Encoding.UTF8.GetBytes(i.Text))));
        var c = new SharpSevenZipCompressor { ArchiveFormat = OutArchiveFormat.SevenZip };
        c.CustomParameters["s"] = solid ? "on" : "off";
        using var fs = new FileStream(path, FileMode.Create);
        c.CompressStreamDictionary(dict, fs, string.Empty);
    }
}
