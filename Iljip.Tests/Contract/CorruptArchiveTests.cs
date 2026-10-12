using System.Text;
using Iljip.Tests.TestSupport;

namespace Iljip.Tests.Contract;

/// <summary>
/// [범위 6] 손상 파일: 뒷부분을 자른 zip/7z/tar/gz/tar.gz, 바이트를 망가뜨린 zip/7z/gz, 쓰레기·빈 파일, 서명 뒤가 쓰레기인 rar,
/// 데이터 한가운데가 깨진 zip → 예외가 나고, 대상 폴더에 '틀린 내용'이나 절반짜리(0바이트 포함) 파일이 남지 않는다.
/// (EGG/ALZ 의 잘림·CRC 는 EggAlzTests)
/// 계약 테스트(예외 + 오염 없음)와 '기록_' 테스트(지금 엔진의 예외 타입·문구)를 나눠 두었다 —
/// 엔진을 바꾸면 기록 쪽만 새 값으로 고치고, 계약 쪽은 그대로 통과해야 한다.
/// </summary>
public class CorruptArchiveTests
{
    /// <summary>
    /// (이름(확장자로 서비스 결정), 원본 픽스처, 망가뜨리는 방법, 지금 예외 타입 전체 이름, 문구 핵심(없으면 null))
    /// 예외 타입·문구는 SharpCompress 0.50.5 / EggDotNet 0.4.0 실측 값(W1-B 에서 일집 자체 예외로 한 번 더 바뀜).
    /// </summary>
    public static TheoryData<string, string, string, string, string?> Cases => new()
    {
        { "trunc_half.zip", "bandizip/zip_default.zip", "half", "SharpCompress.Common.ArchiveException", "Failed to locate the Zip Header" },
        { "trunc_tail.zip", "bandizip/zip_default.zip", "tail30", "SharpCompress.Common.ArchiveException", "Failed to locate the Zip Header" },
        { "deflate_broken.zip", "bandizip/zip_default.zip", "xor200", "SharpCompress.Compressors.Deflate.ZlibException", "Bad state (invalid block type)" },
        { "trunc_half.7z", "bandizip/7z_default.7z", "half", "SharpCompress.Common.ArchiveOperationException", "nextHeaderOffset is invalid" },
        { "data_broken.7z", "bandizip/7z_default.7z", "xor100", "SharpCompress.Compressors.LZMA.DataErrorException", "Data Error" },
        { "trunc_half.tar", "bandizip/tar_default.tar", "half", "SharpCompress.Common.IncompleteArchiveException", "Failed to read TAR header" },
        { "garbage.zip", "bandizip/zip_default.zip", "garbage", "SharpCompress.Common.ArchiveOperationException", "Cannot determine compressed stream type" },
        { "empty.zip", "bandizip/zip_default.zip", "empty", "SharpCompress.Common.ArchiveOperationException", "Cannot determine compressed stream type" },
        { "garbage.7z", "bandizip/7z_default.7z", "garbage", "SharpCompress.Common.ArchiveOperationException", "Cannot determine compressed stream type" },
        { "garbage.bz2", "python/견적서_2026.txt.bz2", "garbage", "System.IO.InvalidDataException", "올바른 BZIP2(.bz2) 형식이 아니거나 손상" },
        // .gz 는 W1-A 1차 검증 지적으로 추가: 잘린 .gz 가 '완료'로 끝나며 절반짜리를 남기던 문제(GZipArchiveService 가 확인값을 봄)
        { "trunc_half.gz", "bandizip/견적서_2026.txt.gz", "half", "System.IO.InvalidDataException", "끝부분이 잘려 있어요" },
        { "trunc_half.tar.gz", "bandizip/targz_default.tar.gz", "half", "System.IO.InvalidDataException", "끝부분이 잘려 있어요" },
        // RAR·EGG·ALZ 는 반디집이 만들 수 없어 픽스처가 없다 — 여기선 '깨진 파일 거절'만(EGG/ALZ 성공 경로는 EggAlzTests)
        { "garbage.rar", "bandizip/zip_default.zip", "garbage", "SharpCompress.Common.ArchiveOperationException", "Cannot determine compressed stream type" },
        // RAR 서명 + 쓰레기: SharpCompress 0.50 은 RAR5 를 '항목 0개'로 조용히 열었다 → RarArchiveService 가 손상으로 알림.
        // RAR4 는 엔진이 이미 머리 오류를 낸다(대조용).
        { "rar5_sig_garbage.rar", "bandizip/zip_default.zip", "rar5sig", "System.IO.InvalidDataException", "손상되었거나 들어 있는 파일이 없어요" },
        { "rar4_sig_garbage.rar", "bandizip/zip_default.zip", "rar4sig", "SharpCompress.Common.InvalidFormatException", "Unknown Rar Header" },
        { "garbage.egg", "bandizip/zip_default.zip", "garbage", "EggDotNet.Exceptions.UnknownEggException", "EGG format is unknown or unsupported" },
        { "garbage.alz", "bandizip/zip_default.zip", "garbage", "EggDotNet.Exceptions.UnknownEggException", "EGG format is unknown or unsupported" },
    };

    private static async Task<(Exception? Ex, string Dest)> Run(TempDir tmp, string name, string sourceRel, string how)
    {
        string arc = tmp.P(name);
        File.WriteAllBytes(arc, Damage(File.ReadAllBytes(FixtureCatalog.PathOf(sourceRel)), how));
        string dest = tmp.P("out");
        return (await Svc.Catch(() => Svc.For(arc).ExtractAsync(arc, dest)), dest);
    }

    /// <summary>계약: 손상 파일은 예외로 알리고, 대상 폴더에 틀린 내용·절반짜리 파일이 남지 않는다.</summary>
    [Theory]
    [MemberData(nameof(Cases))]
    public async Task 손상파일은_예외_그리고_대상폴더_오염_없음(string name, string sourceRel, string how, string _, string? _1)
    {
        using var tmp = new TempDir("bad");
        var (ex, dest) = await Run(tmp, name, sourceRel, how);
        Assert.True(ex is not null, $"{name}: 손상 파일인데 예외 없이 끝남");
        AssertNoWrongContent(dest, sourceRel);
    }

    /// <summary>현재 동작 기록: 지금 엔진이 내는 예외 타입·문구.</summary>
    [Theory, Trait(Kinds.Key, Kinds.Record)]
    [MemberData(nameof(Cases))]
    public async Task 기록_손상파일_예외_타입과_문구(string name, string sourceRel, string how, string exType, string? msg)
    {
        using var tmp = new TempDir("badr");
        var (ex, _) = await Run(tmp, name, sourceRel, how);
        Assert.NotNull(ex);
        Assert.Equal(exType, ex!.GetType().FullName);
        if (msg is not null) Assert.Contains(msg, ex.Message);
    }

    /// <summary>
    /// 뒤가 잘린 .bz2 / .tar 도 절반짜리 파일을 남기지 않는다.
    /// tar 는 '반복_압축잘됨.txt'(76800B) 데이터 한가운데를 자르지만, 지금 엔진은 해제 전에 항목을 훑어 총량을 세는 단계에서
    /// 'Failed to read TAR header' 로 먼저 실패해 아무것도 쓰지 않는다 — '쓰는 도중 실패'는 아래 ZIP 사례가 맡는다.
    /// </summary>
    [Theory]
    [InlineData("trunc_half.bz2", "python/견적서_2026.txt.bz2")]
    [InlineData("trunc_data.tar", "bandizip/tar_default.tar")]
    [InlineData("trunc_half.gz", "bandizip/견적서_2026.txt.gz")]
    [InlineData("trunc_half.tar.gz", "bandizip/targz_default.tar.gz")]
    public async Task 잘린_파일은_절반짜리를_남기지_않는다(string name, string sourceRel)
    {
        using var tmp = new TempDir("bad2");
        string arc = tmp.P(name);
        var src = File.ReadAllBytes(FixtureCatalog.PathOf(sourceRel));
        byte[] cut = name.EndsWith(".tar") ? src[..(IndexOf(src, Encoding.ASCII.GetBytes("ustar")) + 30000)] : src[..(src.Length / 2)];
        File.WriteAllBytes(arc, cut);
        string dest = tmp.P("out");
        var ex = await Svc.Catch(() => Svc.For(arc).ExtractAsync(arc, dest));
        Assert.True(ex is not null, "잘린 파일인데 예외 없이 끝남");
        AssertNoWrongContent(dest, sourceRel);
    }

    /// <summary>
    /// 계약: 파일을 '쓰는 도중' 데이터가 깨져 멈추면, 쓰다 만 파일(여기선 앞 160 KiB 가 이미 기록된 300,000B 파일)을 지운다.
    /// 앞 항목(앞.txt)은 끝까지 맞게 풀렸으므로 남아도 된다. 망가뜨리는 법: Deflate '저장 블록' 4번째 머리의 길이 보수(NLEN)를 틀리게.
    /// </summary>
    [Fact]
    public async Task 쓰는_도중_깨지면_쓰다만_파일을_남기지_않는다()
    {
        using var tmp = new TempDir("bad3");
        var data = new byte[300_000];
        new Random(5).NextBytes(data);
        byte[] first = Encoding.UTF8.GetBytes("first");
        string arc = tmp.P("mid_broken.zip");
        RawZip.Write(arc,
            new RawZip.Item(Encoding.UTF8.GetBytes("앞.txt"), first),
            new RawZip.Item(Encoding.UTF8.GetBytes("큰.bin"), data, true, DeflateStored.Encode(data, corruptBlock: 3)));
        string dest = tmp.P("out");
        var ex = await Svc.Catch(() => Svc.For(arc).ExtractAsync(arc, dest));
        Assert.True(ex is not null, "데이터가 깨졌는데 예외 없이 끝남");
        AssertNoWrongContent(dest, new Dictionary<string, string>
        {
            ["앞.txt"] = TreeAssert.Sha256(first),
            ["큰.bin"] = TreeAssert.Sha256(data),
        });
    }

    // ───── .gz 내용 확인(W1-A 1차 검증 지적) ─────
    // SharpCompress 0.50 과 .NET GZipStream 은 gzip 끝의 CRC32·원래 크기를 보지 않고 잘림도 알아채지 못해, 받다 만 .gz 가
    // '완료'로 끝나며 절반짜리 파일을 정상 이름으로 남겼다(B2 를 고친 뒤로는 반디집·일집이 만든 흔한 .gz 가 모두 이 길).
    // 일집은 풀린 내용을 SharpZipLib GZipInputStream 으로 읽어 멤버마다 맞춰 본다(GZipArchiveService.OpenEntryData).

    private static readonly byte[] GzPayload = Payload(300_000, 11);

    /// <summary>
    /// 계약: 잘리거나 확인값이 틀린 .gz 는 예외로 알리고(화면이 암호 문제로 오해하지 않게), 대상 폴더에 아무 파일도 남기지 않는다.
    /// half=반만, header10=머리 10바이트만, crc/isize=끝 확인값 1바이트 뒤집기, trailer_cut4=끝 4바이트 잘림,
    /// second_member_half=온전한 멤버 뒤에 잘린 둘째 멤버. withName=원본 이름(FNAME) 칸 있음(리눅스 gzip 방식).
    /// </summary>
    [Theory]
    [InlineData("half", false)]
    [InlineData("half", true)]
    [InlineData("header10", false)]
    [InlineData("crc", false)]
    [InlineData("crc", true)]
    [InlineData("isize", false)]
    [InlineData("trailer_cut4", false)]
    [InlineData("second_member_half", false)]
    public async Task 손상된_gz는_예외_그리고_파일을_남기지_않는다(string how, bool withName)
    {
        using var tmp = new TempDir("gzbad");
        string good = tmp.P("good.bin.gz");
        RawGz.Write(good, withName ? Encoding.UTF8.GetBytes("원본.bin") : null, GzPayload);
        byte[] b = File.ReadAllBytes(good);
        byte[] bad = how switch
        {
            "half" => b[..(b.Length / 2)],
            "header10" => b[..10],
            "crc" => Xor(b, b.Length - 8, b.Length - 7),
            "isize" => Xor(b, b.Length - 4, b.Length - 3),
            "trailer_cut4" => b[..(b.Length - 4)],
            "second_member_half" => b.Concat(b[..(b.Length / 2)]).ToArray(),
            _ => throw new ArgumentException(how)
        };
        string arc = tmp.P("bad.bin.gz");
        File.WriteAllBytes(arc, bad);
        string dest = tmp.P("out");
        var ex = await Svc.Catch(() => Svc.For(arc).ExtractAsync(arc, dest));
        Assert.True(ex is not null, $"{how}: 손상된 .gz 인데 예외 없이 끝남");
        Assert.False(Internals.IsPasswordRelated(ex!), "손상을 암호 문제로 오해함: " + ex!.Message);
        TreeAssert.NoFiles(dest);
    }

    /// <summary>계약: 여러 멤버를 이어 붙인 .gz 는 멤버를 모두 이어서 푼다(gzip·7-Zip 과 같게) — 예전엔 첫 멤버만 쓰고 '완료'.</summary>
    [Fact]
    public async Task 여러_멤버를_이어붙인_gz는_모두_풀린다()
    {
        using var tmp = new TempDir("gzcat");
        byte[] second = Payload(123_457, 12);
        RawGz.Write(tmp.P("m1.gz"), null, GzPayload);
        RawGz.Write(tmp.P("m2.gz"), null, second);
        string arc = tmp.P("둘.bin.gz");
        File.WriteAllBytes(arc, File.ReadAllBytes(tmp.P("m1.gz")).Concat(File.ReadAllBytes(tmp.P("m2.gz"))).ToArray());
        string dest = tmp.P("out");
        await Svc.For(arc).ExtractAsync(arc, dest);
        Assert.Equal(new[] { "둘.bin" }, TreeAssert.Files(dest));
        Assert.Equal(TreeAssert.Sha256(GzPayload.Concat(second).ToArray()), TreeAssert.Sha256(Path.Combine(dest, "둘.bin")));
    }

    /// <summary>계약: 온전한 멤버 뒤에 붙은 0 채움(테이프·전송 도구가 붙이는 것)은 무시하고 푼다(gzip 은 경고만, 7-Zip 은 '끝 뒤에 데이터 있음' 경고).</summary>
    [Fact]
    public async Task 끝에_0이_덧붙은_gz도_풀린다()
    {
        using var tmp = new TempDir("gzpad");
        string good = tmp.P("good.gz");
        RawGz.Write(good, null, GzPayload);
        string arc = tmp.P("채움.bin.gz");
        File.WriteAllBytes(arc, File.ReadAllBytes(good).Concat(new byte[512]).ToArray());
        string dest = tmp.P("out");
        await Svc.For(arc).ExtractAsync(arc, dest);
        Assert.Equal(new[] { "채움.bin" }, TreeAssert.Files(dest));
        Assert.Equal(TreeAssert.Sha256(GzPayload), TreeAssert.Sha256(Path.Combine(dest, "채움.bin")));
    }

    private static byte[] Payload(int n, int seed)
    {
        var b = new byte[n];
        new Random(seed).NextBytes(b);
        return b;
    }

    /// <summary>현재 동작 기록: 손상 파일 목록 보기 — 머리(목록)가 멀쩡한 경우만 읽히고, 아니면 예외.</summary>
    [Theory, Trait(Kinds.Key, Kinds.Record)]
    [InlineData("trunc_half.zip", "bandizip/zip_default.zip", "half", false)]
    [InlineData("deflate_broken.zip", "bandizip/zip_default.zip", "xor200", true)]
    [InlineData("trunc_half.7z", "bandizip/7z_default.7z", "half", false)]
    [InlineData("data_broken.7z", "bandizip/7z_default.7z", "xor100", true)]
    [InlineData("garbage.zip", "bandizip/zip_default.zip", "garbage", false)]
    public async Task 기록_손상파일_목록보기(string name, string sourceRel, string how, bool listReadable)
    {
        using var tmp = new TempDir("badl");
        string arc = tmp.P(name);
        File.WriteAllBytes(arc, Damage(File.ReadAllBytes(FixtureCatalog.PathOf(sourceRel)), how));
        var ex = await Svc.Catch(() => Svc.For(arc).ListEntriesAsync(arc));
        Assert.Equal(listReadable, ex is null);
    }

    /// <summary>[B4 고침] Store 방식 데이터 1바이트가 깨진 ZIP — CRC 불일치로 예외가 나고 틀린 내용 파일이 남지 않아야 한다.</summary>
    [Fact]
    public async Task CRC가_틀린_ZIP은_오류로_알려야_한다()
    {
        using var tmp = new TempDir("crc");
        string arc = MakeCrcBroken(tmp);
        string dest = tmp.P("out");
        var ex = await Svc.Catch(() => Svc.For(arc).ExtractAsync(arc, dest));
        Assert.NotNull(ex);
        AssertNoWrongContent(dest, "bandizip/zip_store_l0.zip");
    }

    /// <summary>zip_store_l0 의 readme.txt 내용('line 01')의 한 글자를 바꾼 ZIP(헤더·CRC 값은 그대로).</summary>
    internal static string MakeCrcBroken(TempDir tmp)
    {
        var b = File.ReadAllBytes(FixtureCatalog.PathOf("bandizip/zip_store_l0.zip"));
        int i = IndexOf(b, Encoding.ASCII.GetBytes("line 01"));
        Assert.True(i > 0, "픽스처에서 'line 01' 을 못 찾음");
        b[i + 5] = (byte)'X';
        string arc = tmp.P("crc_broken.zip");
        File.WriteAllBytes(arc, b);
        return arc;
    }

    /// <summary>원본 픽스처의 기대 맵(경로 → sha256) 기준으로 검사.</summary>
    private static void AssertNoWrongContent(string dest, string sourceRel) =>
        AssertNoWrongContent(dest, FixtureCatalog.ByFile(sourceRel).ExpectedFiles.ToDictionary(e => e.Path, e => e.Sha256!, StringComparer.Ordinal));

    /// <summary>
    /// 대상 폴더에 남은 파일은 모두 '같은 경로의 원본과 같은 내용'이어야 한다(손상 전에 이미 끝난 파일은 남아도 됨).
    /// 기대에 없는 경로, 내용이 다른 파일, 원본은 비어 있지 않은데 0바이트로 남은 파일(쓰다 만 파일)은 모두 실패.
    /// </summary>
    private static void AssertNoWrongContent(string dest, IReadOnlyDictionary<string, string> expected)
    {
        if (!Directory.Exists(dest)) return;
        string emptySha = TreeAssert.Sha256(Array.Empty<byte>());
        var problems = new List<string>();
        foreach (var rel in TreeAssert.Files(dest))
        {
            string sha = TreeAssert.Sha256(Path.Combine(dest, rel));
            if (!expected.TryGetValue(rel, out var want)) problems.Add($"기대에 없는 파일: {rel}");
            else if (sha == emptySha && want != emptySha) problems.Add($"쓰다 만(0바이트) 파일: {rel}");
            else if (sha != want) problems.Add($"내용이 원본과 다름(절반·손상): {rel} ({new FileInfo(Path.Combine(dest, rel)).Length}B)");
        }
        Assert.True(problems.Count == 0, "손상 해제 뒤 대상 폴더 오염:\n  " + string.Join("\n  ", problems));
    }

    private static byte[] Damage(byte[] b, string how) => how switch
    {
        "half" => b[..(b.Length / 2)],
        "tail30" => b[..(b.Length - 30)],
        "xor200" => Xor(b, 200, 260),
        "xor100" => Xor(b, 100, 140),
        "garbage" => Enumerable.Range(0, 5000).Select(i => (byte)(i * 7 + 3)).ToArray(),
        "empty" => Array.Empty<byte>(),
        "rar5sig" => RarSignature5.Concat(Garbage(5000)).ToArray(),
        "rar4sig" => RarSignature4.Concat(Garbage(5000)).ToArray(),
        _ => throw new ArgumentException(how)
    };

    private static readonly byte[] RarSignature5 = { 0x52, 0x61, 0x72, 0x21, 0x1A, 0x07, 0x01, 0x00 };   // 'Rar!' 1A 07 01 00
    private static readonly byte[] RarSignature4 = { 0x52, 0x61, 0x72, 0x21, 0x1A, 0x07, 0x00 };         // 'Rar!' 1A 07 00

    private static byte[] Garbage(int n) => Payload(n, 4242);

    private static byte[] Xor(byte[] b, int from, int to)
    {
        var c = (byte[])b.Clone();
        for (int i = from; i < to && i < c.Length; i++) c[i] ^= 0x5A;
        return c;
    }

    internal static int IndexOf(byte[] h, byte[] n)
    {
        for (int i = 0; i <= h.Length - n.Length; i++)
        {
            int j = 0;
            while (j < n.Length && h[i + j] == n[j]) j++;
            if (j == n.Length) return i;
        }
        return -1;
    }
}
