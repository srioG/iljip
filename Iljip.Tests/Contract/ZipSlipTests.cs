using System.Text;
using Iljip.Services;
using Iljip.Tests.TestSupport;

namespace Iljip.Tests.Contract;

/// <summary>
/// [범위 4] 경로 공격(zip-slip) 방어: '../evil.txt', '..\evil.txt', 'safe/../../evil.txt', 절대경로, 그리고
/// '형제 접두사' 공격('../dest_evil.txt', '../dest2/evil.txt', 'a/../../dest_evil.txt')이 든 zip·tar·egg 를
/// 테스트 안에서 만들어 풀었을 때 대상 폴더 밖에 아무것도 생기지 않는다.
/// <para>
/// 형제 접두사 공격은 '대상 폴더 경로 문자열로 시작하는가'만 보고 끝 구분자를 빠뜨린 흔한 검사 버그
/// (GetFullPath(dest) 로 StartsWith → '…\dest_evil.txt' 도 '…\dest' 로 시작하므로 통과)를 잡는다.
/// 엔진을 바꾸면서 해제 코드를 새로 쓰면 가장 다시 생기기 쉬운 보안 회귀다.
/// </para>
/// 안전을 위해 '절대경로' 공격은 실제 C:\ 대신 이 테스트의 임시 폴더 안 절대경로를 쓴다
/// (방어가 깨져도 사용자 PC 를 건드리지 않게) — 모양은 'C:/…/evil.txt' 와 같은 드라이브 절대경로다.
/// 루트 상대('/evil.txt')·드라이브 상대('C:evil.txt') 경로는 방어가 깨지면 드라이브 루트·알 수 없는 폴더에 쓰게 되므로 넣지 않았다.
/// <para>
/// 계약 테스트는 '대상 폴더 밖 무사'만 본다. 7-Zip 계열 엔진은 '../' 를 거부하지 않고 벗겨 내 대상 폴더 '안'에 풀기도 하는데,
/// 그것도 안전하므로 계약 위반이 아니다. 지금 엔진의 거부 방식(예외 타입·문구)은 '기록_' 테스트에 따로 있다.
/// </para>
/// </summary>
public class ZipSlipTests
{
    private static readonly string[] AttackNames =
    {
        "../evil.txt", "..\\evil.txt", "safe/../../evil.txt", "ABS",
        "../dest_evil.txt", "../dest2/evil.txt", "a/../../dest_evil.txt",
    };

    public static TheoryData<string, string> Attacks
    {
        get
        {
            var d = new TheoryData<string, string>();
            foreach (var fmt in new[] { "zip", "tar", "egg" })
                foreach (var a in AttackNames) d.Add(fmt, a);
            return d;
        }
    }

    internal static readonly byte[] Evil = Encoding.UTF8.GetBytes("EVIL");

    /// <summary>형식별로 (이름, 데이터) 항목을 그대로 담은 압축 파일을 만든다.</summary>
    internal static void Make(string format, string arc, params (string Name, byte[] Data)[] items)
    {
        switch (format)
        {
            // zipx: 같은 ZIP 바이트를 .zipx 이름으로 → 7z.dll 해제 경로(SevenZipEngine)를 시험(W2-01)
            case "zip" or "zipx": RawZip.Write(arc, items.Select(i => new RawZip.Item(Encoding.UTF8.GetBytes(i.Name), i.Data)).ToArray()); break;
            case "tar": RawTar.Write(arc, items); break;
            case "egg": RawEgg.Write(arc, items.Select(i => new RawEgg.Item(i.Name, i.Data))); break;
            default: throw new ArgumentException(format);
        }
    }

    /// <summary>tmp 안 스냅숏 비교: dest 밖에 새로 생긴 것이 없고, 공격 내용(EVIL)도 dest 밖에는 없다.</summary>
    internal static void AssertNothingOutside(TempDir tmp, string dest, SortedDictionary<string, string?> before, string context)
    {
        var after = TreeAssert.Snapshot(tmp.Root);
        string destRel = Path.GetRelativePath(tmp.Root, dest).Replace('\\', '/');
        bool Inside(string k) => k == destRel || k.StartsWith(destRel + "/", StringComparison.Ordinal);
        var newOutside = after.Keys.Except(before.Keys).Where(k => !Inside(k)).ToList();
        Assert.True(newOutside.Count == 0, $"{context}: 대상 폴더 밖에 생김: " + string.Join(", ", newOutside));
        string evilSha = TreeAssert.Sha256(Evil);
        var evilOutside = after.Where(kv => kv.Value == evilSha && !Inside(kv.Key)).Select(kv => kv.Key).ToList();
        Assert.True(evilOutside.Count == 0, $"{context}: 공격 내용이 대상 폴더 밖에 있음: " + string.Join(", ", evilOutside));
    }

    private static (TempDir Tmp, string Dest, string Arc) Setup(string format, string attack, string tag)
    {
        var tmp = new TempDir(tag);
        string dest = Path.Combine(tmp.Dir("a", "b"), "dest");
        string name = attack == "ABS" ? tmp.P("abs_evil.txt").Replace('\\', '/') : attack;
        string arc = tmp.P("slip." + format);
        Make(format, arc, ("ok.txt", Encoding.UTF8.GetBytes("ok")), (name, Evil));
        return (tmp, dest, arc);
    }

    /// <summary>계약: 어떤 경로 공격도 대상 폴더 밖에 아무것도 만들지 않는다(예외로 멈추든, 벗겨 내 안에 풀든).</summary>
    [Theory]
    [MemberData(nameof(Attacks))]
    public async Task 경로공격_항목은_대상폴더_밖에_아무것도_만들지_않는다(string format, string attack)
    {
        var (tmp, dest, arc) = Setup(format, attack, "slip");
        using (tmp)
        {
            var before = TreeAssert.Snapshot(tmp.Root);
            await Svc.Catch(() => Svc.For(arc).ExtractAsync(arc, dest));
            AssertNothingOutside(tmp, dest, before, $"{format} '{attack}'");
        }
    }

    /// <summary>현재 동작 기록: 공격 항목에서 InvalidDataException("안전하지 않은 경로: …") 로 멈추고, 앞 항목(ok.txt)만 풀려 있다.</summary>
    [Theory, Trait(Kinds.Key, Kinds.Record)]
    [MemberData(nameof(Attacks))]
    public async Task 기록_경로공격은_InvalidDataException으로_멈추고_앞항목만_남는다(string format, string attack)
    {
        var (tmp, dest, arc) = Setup(format, attack, "slipr");
        using (tmp)
        {
            var ex = await Svc.Catch(() => Svc.For(arc).ExtractAsync(arc, dest));
            Assert.IsType<InvalidDataException>(ex);
            Assert.Contains("안전하지 않은 경로", ex!.Message);
            Assert.Equal(new[] { "ok.txt" }, TreeAssert.Files(dest));
        }
    }

    public static TheoryData<string, string> SelectAttacks
    {
        get
        {
            var d = new TheoryData<string, string>();
            foreach (var fmt in new[] { "zip", "tar", "egg" })
                foreach (var a in new[] { "../evil.txt", "../dest_evil.txt", "../dest2/evil.txt" }) d.Add(fmt, a);
            return d;
        }
    }

    /// <summary>계약: 선택 해제(ExtractEntriesAsync)로 공격 항목을 직접 골라도 대상 폴더 밖은 무사하다.</summary>
    [Theory]
    [MemberData(nameof(SelectAttacks))]
    public async Task 선택_해제로_공격항목을_골라도_밖에_안_생긴다(string format, string attack)
    {
        using var tmp = new TempDir("slip2");
        string dest = Path.Combine(tmp.Dir("a", "b"), "dest");
        string arc = tmp.P("slip." + format);
        Make(format, arc, (attack, Evil));
        var before = TreeAssert.Snapshot(tmp.Root);
        await Svc.Catch(() => Svc.For(arc).ExtractEntriesAsync(arc, dest, new[] { attack }));
        AssertNothingOutside(tmp, dest, before, $"{format} 선택 '{attack}'");
    }

    /// <summary>현재 동작 기록: 선택 해제의 공격 항목도 InvalidDataException, 대상 폴더에 파일 없음.</summary>
    [Theory, Trait(Kinds.Key, Kinds.Record)]
    [MemberData(nameof(SelectAttacks))]
    public async Task 기록_선택해제_공격항목은_InvalidDataException(string format, string attack)
    {
        using var tmp = new TempDir("slip2r");
        string dest = Path.Combine(tmp.Dir("a", "b"), "dest");
        string arc = tmp.P("slip." + format);
        Make(format, arc, (attack, Evil));
        var ex = await Svc.Catch(() => Svc.For(arc).ExtractEntriesAsync(arc, dest, new[] { attack }));
        Assert.IsType<InvalidDataException>(ex);
        TreeAssert.NoFiles(dest);
    }

    /// <summary>
    /// 계약: Windows 특수 이름(대체 데이터 스트림 'a.txt:evil', 끝 점·공백, 예약 이름 'con.txt', './')도 대상 폴더 밖으로 못 나간다.
    /// (지금은 모두 예외 없이 대상 폴더 안에 풀린다 — 끝 점·공백은 Windows 가 떼어 'evil.txt' 가 됨)
    /// </summary>
    [Theory]
    [InlineData("a.txt:evil")]
    [InlineData("evil.txt.")]
    [InlineData("evil.txt ")]
    [InlineData("con.txt")]
    [InlineData("sub/./x.txt")]
    [InlineData("./../evil.txt")]
    public async Task Windows_특수이름도_대상폴더_밖으로_못_나간다(string name)
    {
        using var tmp = new TempDir("slipw");
        string dest = Path.Combine(tmp.Dir("a", "b"), "dest");
        string arc = tmp.P("w.zip");
        Make("zip", arc, ("a.txt", Encoding.UTF8.GetBytes("a")), (name, Evil));
        var before = TreeAssert.Snapshot(tmp.Root);
        await Svc.Catch(() => Svc.For(arc).ExtractAsync(arc, dest));
        AssertNothingOutside(tmp, dest, before, $"'{name}'");
    }

    /// <summary>
    /// 계약: tar 링크 항목(심볼릭 '2'·하드 '1', 대상이 밖을 가리킴) 뒤에 그 링크 아래로 쓰는 항목이 와도 밖은 무사하다.
    /// (지금: 링크를 빈 일반 파일로 만들고, 다음 항목은 그 파일 때문에 폴더를 못 만들어 IOException)
    /// </summary>
    [Theory]
    [InlineData('2', "../../outside")]
    [InlineData('1', "../../outside")]
    [InlineData('2', "ABS")]
    public async Task tar_링크항목을_거쳐도_대상폴더_밖에_못_쓴다(char type, string target)
    {
        using var tmp = new TempDir("slipl");
        string dest = Path.Combine(tmp.Dir("a", "b"), "dest");
        string outside = tmp.Dir("outside");
        string arc = tmp.P("l.tar");
        RawTar.Write(arc,
            new RawTar.Entry("link", Array.Empty<byte>(), type, target == "ABS" ? outside.Replace('\\', '/') : target),
            new RawTar.Entry("link/evil.txt", Evil));
        var before = TreeAssert.Snapshot(tmp.Root);
        await Svc.Catch(() => Svc.For(arc).ExtractAsync(arc, dest));
        AssertNothingOutside(tmp, dest, before, $"tar 링크 {type} → {target}");
        Assert.Empty(Directory.EnumerateFileSystemEntries(outside));
    }

    // ───── 7z.dll 해제 경로(W2-01): 같은 공격을 .zipx 로 ─────

    public static TheoryData<string, string> ZipxAttacks
    {
        get
        {
            var d = new TheoryData<string, string>();
            foreach (var a in AttackNames) d.Add("zipx", a);
            return d;
        }
    }

    /// <summary>계약(7z.dll 경로): 어떤 경로 공격도 대상 폴더 밖에 아무것도 만들지 않는다. 임시 폴더도 남지 않는다.</summary>
    [Theory]
    [MemberData(nameof(ZipxAttacks))]
    public async Task zipx_7z_dll_경로도_경로공격은_대상폴더_밖에_아무것도_만들지_않는다(string format, string attack)
    {
        var (tmp, dest, arc) = Setup(format, attack, "slipx");
        using (tmp)
        {
            var before = TreeAssert.Snapshot(tmp.Root);
            await Svc.Catch(() => Svc.For(arc).ExtractAsync(arc, dest));
            AssertNothingOutside(tmp, dest, before, $"{format} '{attack}'");
            Assert.Empty(Directory.Exists(dest) ? Directory.EnumerateDirectories(dest, SevenZipEngine.StagingPrefix + "*") : Array.Empty<string>());
        }
    }

    /// <summary>
    /// 현재 동작 기록(7z.dll 경로): 쓰기 전에 모든 이름을 먼저 검사해 공격 항목이 있으면 InvalidDataException("안전하지 않은 경로: …")으로
    /// 멈추고 아무것도 풀지 않는다(SharpCompress 경로는 앞 항목 ok.txt 를 이미 풀어 둔 뒤 멈춤 — 위 기록과 다른 점).
    /// </summary>
    [Theory, Trait(Kinds.Key, Kinds.Record)]
    [MemberData(nameof(ZipxAttacks))]
    public async Task 기록_zipx_7z_dll_경로는_공격이_있으면_아무것도_풀지_않는다(string format, string attack)
    {
        var (tmp, dest, arc) = Setup(format, attack, "slipxr");
        using (tmp)
        {
            var ex = await Svc.Catch(() => Svc.For(arc).ExtractAsync(arc, dest));
            Assert.IsType<InvalidDataException>(ex);
            Assert.Contains("안전하지 않은 경로", ex!.Message);
            TreeAssert.NoFiles(dest);
        }
    }

    /// <summary>계약(7z.dll 경로): Windows 특수 이름도 대상 폴더 밖으로 못 나간다(7z.dll 은 못 쓰는 문자를 '_' 로 바꿔 씀).</summary>
    [Theory]
    [InlineData("a.txt:evil")]
    [InlineData("evil.txt.")]
    [InlineData("evil.txt ")]
    [InlineData("sub/./x.txt")]
    [InlineData("./../evil.txt")]
    public async Task zipx_7z_dll_경로도_Windows_특수이름이_밖으로_못_나간다(string name)
    {
        using var tmp = new TempDir("slipwx");
        string dest = Path.Combine(tmp.Dir("a", "b"), "dest");
        string arc = tmp.P("w.zipx");
        Make("zipx", arc, ("a.txt", Encoding.UTF8.GetBytes("a")), (name, Evil));
        var before = TreeAssert.Snapshot(tmp.Root);
        await Svc.Catch(() => Svc.For(arc).ExtractAsync(arc, dest));
        AssertNothingOutside(tmp, dest, before, $"zipx '{name}'");
    }

    /// <summary>현재 동작 기록: 목록 보기는 공격 이름을 그대로 보여 줄 뿐 아무것도 쓰지 않는다.</summary>
    [Fact, Trait(Kinds.Key, Kinds.Record)]
    public async Task 기록_목록보기는_공격이름을_그대로_보여주기만_한다()
    {
        using var tmp = new TempDir("slip3");
        string arc = tmp.P("slip.zip");
        RawZip.Write(arc, RawZip.Utf8("../evil.txt", "x"), RawZip.Utf8("..\\evil2.txt", "y"));
        var names = (await new ZipArchiveService().ListEntriesAsync(arc)).Select(e => e.Path).ToList();
        Assert.Equal(new[] { "../evil.txt", "..\\evil2.txt" }, names);
        Assert.Equal(new[] { "slip.zip" }, TreeAssert.Files(tmp.Root));
    }
}
