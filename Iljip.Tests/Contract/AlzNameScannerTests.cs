using System.Text;
using Iljip.Services;
using Iljip.Tests.TestSupport;

namespace Iljip.Tests.Contract;

/// <summary>
/// AlzNameScanner(B10 우회 — ALZ 이름 원본 바이트 직접 읽기): 맞는 ALZ 에서는 이름 바이트를 그대로 돌려주고,
/// 조금이라도 이상하면(잘림·서명 깨짐·항목 수 불일치·분할) 예외 없이 null(= EggDotNet 이름 그대로)을 돌려준다.
/// 합성 ALZ(<see cref="RawAlz"/>)로만 확인 — 진짜 알집 ALZ 는 MANUAL_CHECKLIST 6절.
/// </summary>
public class AlzNameScannerTests
{
    private static readonly Encoding Cp949 = Encoding.GetEncoding(949);

    [Fact]
    public void 한글_ALZ의_이름_바이트가_CP949_원본과_같다()
    {
        using var tmp = new TempDir("alzs");
        string arc = tmp.P("korean.alz");
        EggAlzTests.WriteKoreanAlz(arc);
        var names = AlzNameScanner.TryReadNames(arc);
        Assert.NotNull(names);
        Assert.Equal(2, names!.Count);
        Assert.Equal(Cp949.GetBytes("문서\\"), names[0]);
        Assert.Equal(Cp949.GetBytes("문서\\견적서.txt"), names[1]);
    }

    /// <summary>데이터 크기 칸(1·2·4바이트 등)과 빈 파일·폴더 항목이 섞여도 항목 경계를 맞게 건너뛴다.</summary>
    [Fact]
    public void 데이터와_빈항목이_섞여도_이름을_순서대로_읽는다()
    {
        using var tmp = new TempDir("alzs2");
        string arc = tmp.P("mixed.alz");
        var items = new[]
        {
            new RawAlz.Item(Cp949.GetBytes("폴더\\"), null),
            new RawAlz.Item(Cp949.GetBytes("폴더\\큰.bin"), new byte[70_000]),
            new RawAlz.Item(Encoding.ASCII.GetBytes("empty.txt"), Array.Empty<byte>()),
            new RawAlz.Item(Cp949.GetBytes("끝.txt"), Encoding.UTF8.GetBytes("end")),
        };
        RawAlz.Write(arc, items);
        var names = AlzNameScanner.TryReadNames(arc);
        Assert.NotNull(names);
        Assert.Equal(items.Select(i => i.Name), names!);
    }

    /// <summary>TryReadNames 는 끝까지 온전한 ALZ 만 다룬다 — 잘리면 예외 없이 null(잘린 ALZ 는 TryScan 이 '읽은 데까지' 돌려줌, 아래).</summary>
    [Theory]
    [InlineData(6)]       // 파일 머리도 못 채움
    [InlineData(12)]      // 첫 항목 머리 중간
    [InlineData(-20)]     // 마지막 항목 이름 끝(끝 표시 없음)
    [InlineData(-12)]     // 마지막 항목 데이터 중간(끝 표시 없음)
    [InlineData(-8)]      // 끝 표시 'CLZ' 만 잘림
    public void 잘린_ALZ는_예외없이_null(int cut)
    {
        using var tmp = new TempDir("alzt");
        string full = tmp.P("full.alz");
        EggAlzTests.WriteKoreanAlz(full);
        var b = File.ReadAllBytes(full);
        string arc = tmp.P("cut.alz");
        File.WriteAllBytes(arc, b[..(cut > 0 ? cut : b.Length + cut)]);
        Assert.Null(AlzNameScanner.TryReadNames(arc));
    }

    /// <summary>
    /// TryScan: 잘린 ALZ 는 이름까지 온전히 읽은 항목만 돌려주고 Complete=false(EggDotNet 도 그 항목들만 보여 주므로 짝이 맞음).
    /// 파일 머리가 모자라면 ALZ 로 볼 수 없어 null. 항목 속성(폴더 0x10)도 같이 읽는다.
    /// </summary>
    [Theory]
    [InlineData(6, -1)]       // 파일 머리도 못 채움 → null
    [InlineData(12, 0)]       // 첫 항목 머리 중간 → 0개
    [InlineData(-20, 1)]      // 마지막 항목 이름 끝 1바이트가 잘림(이름 15B·데이터 11B·끝 표시 8B) → 그 항목은 버림
    [InlineData(-12, 2)]      // 마지막 항목 데이터 중간 → 이름은 2개 모두 온전
    [InlineData(-8, 2)]       // 끝 표시만 잘림
    [InlineData(0, 2)]        // 안 자름 → Complete=true
    public void 잘린_ALZ는_온전한_항목까지만_읽는다(int cut, int expectedCount)
    {
        using var tmp = new TempDir("alztc");
        string full = tmp.P("full.alz");
        EggAlzTests.WriteKoreanAlz(full);
        var b = File.ReadAllBytes(full);
        string arc = tmp.P("cut.alz");
        File.WriteAllBytes(arc, cut == 0 ? b : b[..(cut > 0 ? cut : b.Length + cut)]);
        var scan = AlzNameScanner.TryScan(arc);
        if (expectedCount < 0)
        {
            Assert.Null(scan);
            return;
        }
        Assert.NotNull(scan);
        Assert.Equal(cut == 0, scan!.Complete);
        Assert.Equal(expectedCount, scan.Entries.Count);
        var want = new[] { Cp949.GetBytes("문서\\"), Cp949.GetBytes("문서\\견적서.txt") }.Take(expectedCount);
        Assert.Equal(want, scan.Entries.Select(e => e.Name));
        Assert.Equal(new[] { true, false }.Take(expectedCount), scan.Entries.Select(e => e.IsDirectory));
    }

    /// <summary>
    /// 이름 바이트 풀기: UTF-8 이면 UTF-8, 아니면 CP949 로 빈틈없이 풀리면 CP949(한글이 없는 한자·기호 이름도),
    /// 둘 다 아니면 CP437(바이트 1:1 보존 — 0xFF 는 U+00A0).
    /// </summary>
    [Fact]
    public void 이름_바이트는_UTF8_CP949_CP437_순으로_푼다()
    {
        Assert.Equal("유티에프.txt", AlzNameScanner.DecodeName(Encoding.UTF8.GetBytes("유티에프.txt")));
        Assert.Equal("plain.txt", AlzNameScanner.DecodeName(Encoding.ASCII.GetBytes("plain.txt")));
        Assert.Equal("漢字①.txt", AlzNameScanner.DecodeName(Cp949.GetBytes("漢字①.txt")));
        Assert.Equal("견적서.txt", AlzNameScanner.DecodeName(Cp949.GetBytes("견적서.txt")));
        // CP949 가 아닌 바이트: 끊긴 2바이트 글자(0xA1 뒤가 없음), .NET CP949 가 엉뚱한 글자로 푸는 홑바이트(0xFF→U+F8F7, 0x80→U+0080)
        // → CP437 1:1(0xA1='í', 0xFF=NBSP, 0x80='Ç')
        Assert.Equal("Aí", AlzNameScanner.DecodeName(new byte[] { 0x41, 0xA1 }));
        Assert.Equal("A ", AlzNameScanner.DecodeName(new byte[] { 0x41, 0xFF }));
        Assert.Equal("AÇ", AlzNameScanner.DecodeName(new byte[] { 0x41, 0x80 }));
    }

    [Theory]
    [InlineData("file")]      // 파일 머리 'ALZ\1' 깨짐
    [InlineData("entry")]     // 두 번째 항목 머리 'BLZ\1' 깨짐
    public void 서명이_깨진_ALZ는_예외없이_null(string where)
    {
        using var tmp = new TempDir("alzb");
        string arc = tmp.P("bad.alz");
        EggAlzTests.WriteKoreanAlz(arc);
        var b = File.ReadAllBytes(arc);
        byte[] blz = Encoding.ASCII.GetBytes("BLZ\u0001");
        var entryHeads = Enumerable.Range(0, b.Length - blz.Length + 1).Where(i => b.AsSpan(i, blz.Length).SequenceEqual(blz)).ToList();
        Assert.Equal(2, entryHeads.Count);
        int at = where == "file" ? 0 : entryHeads[1];
        b[at] ^= 0xFF;
        File.WriteAllBytes(arc, b);
        Assert.Null(AlzNameScanner.TryReadNames(arc));
    }

    [Fact]
    public void 없는_파일이나_ALZ가_아닌_파일은_null()
    {
        using var tmp = new TempDir("alzn");
        Assert.Null(AlzNameScanner.TryReadNames(tmp.P("없음.alz")));
        string egg = tmp.P("not.alz");
        File.WriteAllText(egg, "this is not an alz file");
        Assert.Null(AlzNameScanner.TryReadNames(egg));
    }

    /// <summary>분할 ALZ(.a00 조각이 같은 폴더에 있음)는 이름이 조각에 걸칠 수 있어 다루지 않는다.</summary>
    [Fact]
    public void 분할_ALZ는_null()
    {
        using var tmp = new TempDir("alzsp");
        string arc = tmp.P("korean.alz");
        EggAlzTests.WriteKoreanAlz(arc);
        File.WriteAllBytes(tmp.P("korean.a00"), new byte[] { 1, 2, 3 });
        Assert.Null(AlzNameScanner.TryReadNames(arc));
    }

    // ───── 이름 바꿔 쓰기 판정(TryRecoverNames) ─────

    private static string[] LibraryNames(params string[] cp949Names) =>
        cp949Names.Select(n => Encoding.UTF8.GetString(Cp949.GetBytes(n))).ToArray();   // EggDotNet 처럼 UTF-8 로 읽은 깨진 이름

    [Fact]
    public void 깨진_이름이면_CP949로_푼_이름으로_바꾼다()
    {
        using var tmp = new TempDir("alzr");
        string arc = tmp.P("korean.alz");
        EggAlzTests.WriteKoreanAlz(arc);
        var lib = LibraryNames("문서\\", "문서\\견적서.txt");
        Assert.All(lib, n => Assert.Contains('�', n));
        Assert.Equal(new[] { "문서\\", "문서\\견적서.txt" }, AlzNameScanner.TryRecoverNames(arc, lib));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    public void 항목_수가_다르면_null(int count)
    {
        using var tmp = new TempDir("alzc");
        string arc = tmp.P("korean.alz");
        EggAlzTests.WriteKoreanAlz(arc);
        var lib = LibraryNames("문서\\", "문서\\견적서.txt", "문서\\추가.txt").Take(count).ToList();
        Assert.Null(AlzNameScanner.TryRecoverNames(arc, lib));
    }

    [Fact]
    public void ASCII_부분이_다르면_null()
    {
        using var tmp = new TempDir("alza");
        string arc = tmp.P("korean.alz");
        EggAlzTests.WriteKoreanAlz(arc);
        Assert.Null(AlzNameScanner.TryRecoverNames(arc, LibraryNames("문서\\", "문서\\견적서.csv")));
    }

    [Fact]
    public void 깨진_이름이_없으면_고칠_게_없어_null()
    {
        using var tmp = new TempDir("alzo");
        string arc = tmp.P("ascii.alz");
        RawAlz.Write(arc, new RawAlz.Item(Encoding.ASCII.GetBytes("a.txt"), new byte[] { 1 }));
        Assert.Null(AlzNameScanner.TryRecoverNames(arc, new[] { "a.txt" }));
    }
}
