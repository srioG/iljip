using System.Buffers.Binary;
using System.Text;
using ICSharpCode.SharpZipLib.Zip;
using Iljip.Models;
using Iljip.Services;
using Iljip.Tests.TestSupport;
using Iljip.ViewModels;
using Iljip.Views;
using SharpSevenZip;

namespace Iljip.Tests.Contract;

/// <summary>
/// [I2-2 만들기, W2-03] 나눠 만들기(분할 압축 — 메일 첨부 크기에 맞추기).
/// <list type="bullet">
/// <item>7Z: 숫자 조각(이름.7z.001 …) — 7z 하나를 만든 뒤 단순 절단(7-Zip 의 볼륨과 같은 바이트). 첫 조각으로 열고 풀면 원본과 같고, 7z.dll 도 조각을 스스로 찾는다.</item>
/// <item>ZIP: PKZIP 나눔(이름.z01 … 이름.zip — 반디집 기본과 같은 방식, 근거는 HANDOFF W2-03). 첫 조각은 50 4B 07 08 로 시작, 로컬 머리는 늘 한 조각 안,
///   마지막 조각의 끝 머리 디스크 번호 = 조각 수 - 1. 일집(main ZIP 처리기)·7z.dll·반디집(bz.exe t)이 모두 읽는다(목록이 조각을 넘어도, ZIP64 여도).</item>
/// <item>빠진 조각(.z02·.7z.002·.part2.rar)은 그 이름으로 알리고(W2-01 과 같은 문구) 아무것도 만들지 않는다. 조각이 너무 많으면 거절(ZIP 99·7Z 999).</item>
/// <item>취소·실패면 조각도 임시 폴더도 남지 않는다. 같은 이름 세트가 있으면 세트 전체가 '이름 (2)' — 있던 세트는 바이트 그대로.</item>
/// <item>화면: 옵션 창 '나누기' 칸(ZIP·7Z 만), 서비스로 전달, 끝나면 실제 조각 이름으로 안내하고 그 세트를 연다.</item>
/// </list>
/// 이 PC 실측(반디집 7.46·7z.dll 26.03)은 HANDOFF W2-03. Windows 탐색기·알집은 창을 띄워야 해서 MANUAL_CHECKLIST.
/// </summary>
public class SplitCreateTests
{
    // ───────────── 도우미 ─────────────

    /// <summary>무작위(압축 안 되는) 파일을 더한다 — 조각이 여러 개 나오게.</summary>
    private static string AddRandom(string folder, string name, int size, int seed = 20261013)
    {
        var data = new byte[size];
        new Random(seed).NextBytes(data);
        string p = Path.Combine(folder, name);
        File.WriteAllBytes(p, data);
        File.SetLastWriteTime(p, SampleTree.Mtime);
        return p;
    }

    /// <summary>폴더 바로 아래 이름들(정렬) — 남은 것 확인용.</summary>
    private static List<string> TopNames(string folder) =>
        Directory.EnumerateFileSystemEntries(folder).Select(p => Path.GetFileName(p)!).OrderBy(n => n, StringComparer.Ordinal).ToList();

    /// <summary>목록의 모양(경로·크기·폴더 여부) — 두 목록 비교용.</summary>
    private static List<string> Shape(IReadOnlyList<ArchiveEntry> list) =>
        list.Select(e => $"{TreeAssert.NormalizeEntryPath(e.Path)}|{(e.IsDirectory ? -1 : e.Size)}").OrderBy(x => x, StringComparer.Ordinal).ToList();

    private static bool IsAscii(string? s) => s is null || s.All(c => c < 0x80);

    /// <summary>
    /// 시험 쪽 최소 판독기(본체 코드와 따로): 나눔 ZIP 조각들을 읽어 끝 머리·중앙 디렉터리 값과 '모든 로컬 머리가 한 조각 안에 있는가'를 본다.
    /// </summary>
    internal sealed record SpanFacts(
        int Pieces, uint FirstSignature, long ThisDisk, long CdDisk, long EntriesOnLast, long TotalEntries,
        int LastRecordPiece, bool Zip64, List<string> HeaderProblems);

    internal static SpanFacts InspectSpan(IReadOnlyList<string> pieces)
    {
        var data = pieces.Select(File.ReadAllBytes).ToList();
        var starts = new long[data.Count];
        for (int i = 1; i < data.Count; i++) starts[i] = starts[i - 1] + data[i - 1].Length;
        byte[] all = data.SelectMany(d => d).ToArray();
        byte[] last = data[^1];
        static uint U32(byte[] b, long at) => BinaryPrimitives.ReadUInt32LittleEndian(b.AsSpan((int)at));
        static ushort U16(byte[] b, long at) => BinaryPrimitives.ReadUInt16LittleEndian(b.AsSpan((int)at));
        static long U64(byte[] b, long at) => (long)BinaryPrimitives.ReadUInt64LittleEndian(b.AsSpan((int)at));

        int e = -1;
        for (int i = last.Length - 22; i >= 0; i--)
            if (U32(last, i) == 0x06054b50 && i + 22 + U16(last, i + 20) == last.Length) { e = i; break; }
        Assert.True(e >= 0, "마지막 조각에 끝 머리(EOCD)가 없음");
        long thisDisk = U16(last, e + 4), cdDisk = U16(last, e + 6), onDisk = U16(last, e + 8), total = U16(last, e + 10);
        long cdSize = U32(last, e + 12), cdOff = U32(last, e + 16);
        bool zip64 = e >= 20 && U32(last, e - 20) == 0x07064b50;
        if (zip64)
        {
            long zDisk = U32(last, e - 16), zOff = U64(last, e - 12);
            byte[] zPiece = data[(int)zDisk];
            Assert.Equal(0x06064b50u, U32(zPiece, zOff));
            thisDisk = U32(zPiece, zOff + 16);
            cdDisk = U32(zPiece, zOff + 20);
            onDisk = U64(zPiece, zOff + 24);
            total = U64(zPiece, zOff + 32);
            cdSize = U64(zPiece, zOff + 40);
            cdOff = U64(zPiece, zOff + 48);
            Assert.Equal(pieces.Count, (int)U32(last, e - 4));   // 위치 표의 전체 디스크 수
        }

        var problems = new List<string>();
        long p = starts[cdDisk] + cdOff;
        int lastRecordPiece = -1;
        for (long n = 0; n < total; n++)
        {
            Assert.Equal(0x02014b50u, U32(all, p));
            int nameLen = U16(all, p + 28), extraLen = U16(all, p + 30), commentLen = U16(all, p + 32);
            long disk = U16(all, p + 34), offset = U32(all, p + 42);
            if (offset == uint.MaxValue) problems.Add($"기록 {n}: 위치가 ZIP64 칸에 있음(조각 안 위치는 작아야 함)");
            byte[] piece = data[(int)disk];
            if (offset + 30 > piece.Length || U32(piece, offset) != 0x04034b50)
                problems.Add($"기록 {n}: 조각 {disk} 의 {offset} 에 로컬 머리가 없음");
            else if (offset + 30 + U16(piece, offset + 26) + U16(piece, offset + 28) > piece.Length)
                problems.Add($"기록 {n}: 로컬 머리가 조각 {disk} 를 넘어감");
            lastRecordPiece = Array.FindLastIndex(starts, s => s <= p);
            p += 46 + nameLen + extraLen + commentLen;
        }
        return new SpanFacts(pieces.Count, U32(data[0], 0), thisDisk, cdDisk, onDisk, total, lastRecordPiece, zip64, problems);
    }

    /// <summary>나눔 ZIP 의 공통 모양 확인: 첫 조각 서명·끝 머리 디스크 번호·머리가 조각을 안 넘음·조각 크기.</summary>
    private static SpanFacts AssertSpanShape(IReadOnlyList<string> pieces, long volumeSize, string context)
    {
        var facts = InspectSpan(pieces);
        Assert.True(pieces.Count >= 2, $"{context}: 조각이 하나뿐");
        Assert.Equal(0x08074b50u, facts.FirstSignature);   // 50 4B 07 08
        Assert.Equal(pieces.Count - 1, facts.ThisDisk);
        Assert.True(facts.HeaderProblems.Count == 0, context + ": " + string.Join("\n", facts.HeaderProblems));
        Assert.True(facts.EntriesOnLast >= 1, $"{context}: 마지막 조각(.zip)에 목록 기록이 하나도 없음(반디집이 못 엶)");
        Assert.Equal(pieces.Count - 1, facts.LastRecordPiece);
        foreach (var v in pieces)
            Assert.InRange(new FileInfo(v).Length, 1, volumeSize);
        return facts;
    }

    /// <summary>7z.dll 로 마지막 조각(.zip)을 열어 시험(Check)하고 항목 수를 돌려준다.</summary>
    private static int SevenZipDllCheck(string lastZip, string? password)
    {
        SevenZipLibrary.ThrowIfUnavailable();
        using var x = string.IsNullOrEmpty(password) ? new SharpSevenZipExtractor(lastZip) : new SharpSevenZipExtractor(lastZip, password);
        Assert.Equal(InArchiveFormat.Zip, x.Format);
        Assert.True(x.Check(), "7z.dll 시험(Check) 실패: " + Path.GetFileName(lastZip));
        return x.ArchiveFileData.Count;
    }

    // ───────────── 7Z 볼륨 ─────────────

    /// <summary>
    /// 계약: 7z 를 64KB 로 나누면 이름.7z.001 … 조각(마지막만 작음)이 생기고 고른 이름의 7z 는 없다. 첫 조각으로 열어 목록·풀기가 원본과 같고,
    /// 7z.dll 도 조각을 스스로 모은다(VolumeFileNames). 암호 + 파일 이름 암호화도 그대로(목록부터 암호).
    /// </summary>
    [Theory]
    [InlineData(null, false)]
    [InlineData(FixtureCatalog.KoreanPassword, true)]
    public async Task 칠지_볼륨은_001부터_조각이고_첫_조각으로_열고_푼다(string? password, bool encryptNames)
    {
        const long Vol = 64 * 1024;
        using var tmp = new TempDir("sp7");
        string src = SampleTree.Create(tmp.Dir("src"));
        AddRandom(src, "큰 무작위.bin", 300_000);
        string arc = tmp.P("나눔.7z");
        var progress = new ProgressRecorder();
        var result = await new SevenZipArchiveService().CompressAsync(new[] { src }, arc,
            new CompressionOptions { Password = password, EncryptFileNames = encryptNames, SplitBytes = Vol }, progress);

        var names = result.Volumes.Select(v => Path.GetFileName(v)!).ToList();
        Assert.True(names.Count >= 4, "조각 수: " + names.Count);
        Assert.Equal(Enumerable.Range(1, names.Count).Select(i => $"나눔.7z.{i:000}"), names);
        Assert.Equal(result.Volumes[0], result.OpenPath);
        Assert.False(File.Exists(arc), "고른 이름의 7z 가 따로 남음");
        var sizes = result.Volumes.Select(v => new FileInfo(v).Length).ToList();
        Assert.All(sizes.SkipLast(1), s => Assert.Equal(Vol, s));
        Assert.InRange(sizes[^1], 1, Vol);
        Assert.Equal(new[] { "src" }.Concat(names).OrderBy(n => n, StringComparer.Ordinal), TopNames(tmp.Root));
        ProgressTests.AssertWellFormed(progress.Reports, "7z 나누기");
        Assert.Contains(progress.Reports, r => r.CurrentFile == SplitArchiveOutput.StageSplitting);

        string first = result.Volumes[0];
        var svc = Svc.For(first);
        Assert.IsType<SevenZipArchiveService>(svc);
        var expected = SampleTree.ExpectedOf(src);
        if (encryptNames)
            Assert.IsType<ListPasswordRequiredException>(await Svc.Catch(() => svc.ListEntriesAsync(first)));
        TreeAssert.ListEquals(await svc.ListEntriesAsync(first, password), expected);
        string dest = tmp.P("out");
        await svc.ExtractAsync(first, dest, password);
        TreeAssert.ExtractedTreeEquals(dest, expected);

        using (var x = password is null ? new SharpSevenZipExtractor(first) : new SharpSevenZipExtractor(first, password))
        {
            Assert.Equal(names, x.VolumeFileNames.Select(v => Path.GetFileName(v)!).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(n => n, StringComparer.Ordinal));
            Assert.True(x.Check());
        }
        SevenZipCreateTests.AssertNoLeftovers(tmp.Root);
    }

    /// <summary>계약: 반디집이 일집 7z 조각(한글 암호 + 파일 이름 암호화)을 첫 조각으로 연다(bz.exe t 종료 코드 0).</summary>
    [BandizipCliFact]
    public async Task 칠지_볼륨을_반디집이_푼다()
    {
        using var tmp = new TempDir("sp7bz");
        string src = SampleTree.Create(tmp.Dir("src"));
        AddRandom(src, "큰 무작위.bin", 200_000);
        var result = await new SevenZipArchiveService().CompressAsync(new[] { src }, tmp.P("반디.7z"),
            new CompressionOptions { Password = FixtureCatalog.KoreanPassword, EncryptFileNames = true, SplitBytes = 64 * 1024 });
        Assert.True(result.Volumes.Count >= 3);
        Assert.Equal(0, await BandizipCli.TestAsync(result.Volumes[0], FixtureCatalog.KoreanPassword));
        Assert.NotEqual(0, await BandizipCli.TestAsync(result.Volumes[0], "틀린암호"));
    }

    // ───────────── ZIP 나눔: 픽스처 바꾸기 ─────────────

    /// <summary>바꿔 볼 ZIP 픽스처(반디집이 만든 것 — 한글 이름 CP949·UTF-8, 저장만, ZipCrypto·AES, 한글 암호 CP949).</summary>
    private static readonly string[] SpanFixtures =
    {
        "zip_default", "zip_cp949", "zip_utf8", "zip_store_l0", "zip_storeroot_no",
        "zip_zipcrypto", "zip_aes256", "zip_zipcrypto_korean_pw", "zip_aes256_korean_pw",
    };

    /// <summary>(픽스처, 조각 크기) — 크기는 1,000~5,000바이트에서 무작위(재현되게 씨앗 고정, 파일보다 작게).</summary>
    public static TheoryData<string, int> SpanCases()
    {
        var d = new TheoryData<string, int>();
        var rnd = new Random(20261013);
        foreach (var id in SpanFixtures)
        {
            long len = new FileInfo(FixtureCatalog.Get(id).FullPath).Length;
            for (int k = 0; k < 3; k++)
                d.Add(id, rnd.Next(1000, (int)Math.Min(5001, len)));
        }
        return d;
    }

    /// <summary>픽스처 ZIP 을 조각으로 바꾼다(원래 ZIP 은 비교용으로 따로 둠).</summary>
    private static (string Original, IReadOnlyList<string> Pieces) Span(TempDir tmp, string id, int volume)
    {
        var fx = FixtureCatalog.Get(id);
        string original = tmp.P("원본.zip");
        File.Copy(fx.FullPath, original);
        string work = tmp.P("작업.zip");
        File.Copy(fx.FullPath, work);
        var pieces = ZipSpanWriter.Split(work, tmp.P("나눔"), volume, CancellationToken.None);
        Assert.False(File.Exists(work), "조각으로 바꾼 뒤 원래 ZIP 이 남음");
        return (original, pieces);
    }

    /// <summary>
    /// 계약(속성 검사): 여러 픽스처를 무작위 크기로 나눠도 첫 조각은 50 4B 07 08, 모든 로컬 머리가 한 조각 안, 마지막 조각의 디스크 번호 = 조각 수 - 1,
    /// 조각 크기는 고른 값 이하. 일집(main ZIP 처리기)의 목록·풀기가 원래 ZIP 과 같고(한글 암호 CP949·AES 포함), 7z.dll 도 시험(Check)을 통과한다.
    /// </summary>
    [Theory, MemberData(nameof(SpanCases))]
    public async Task 픽스처_ZIP을_나눔으로_바꿔도_일집과_7z_dll이_같게_읽는다(string id, int volume)
    {
        using var tmp = new TempDir("spz");
        var (original, pieces) = Span(tmp, id, volume);
        string context = $"{id}@{volume}";
        AssertSpanShape(pieces, volume, context);
        Assert.Equal(ZipSpanWriter.VolumeNames(tmp.P("나눔"), pieces.Count), pieces);
        Assert.EndsWith("나눔.zip", pieces[^1]);

        string last = pieces[^1];
        var svc = Svc.For(last);
        Assert.IsType<ZipArchiveService>(svc);
        string? pw = FixtureCatalog.Get(id).Password;
        var want = await svc.ListEntriesAsync(original, pw);
        var got = await svc.ListEntriesAsync(last, pw);
        Assert.Equal(Shape(want), Shape(got));
        await svc.ExtractAsync(original, tmp.P("a"), pw);
        await svc.ExtractAsync(last, tmp.P("b"), pw);
        Assert.Equal(TreeAssert.Snapshot(tmp.P("a")), TreeAssert.Snapshot(tmp.P("b")));
        Assert.NotEmpty(TreeAssert.Files(tmp.P("b")));
        if (pw is not null)
            Assert.IsType<WrongPasswordException>(await Svc.Catch(() => svc.ExtractAsync(last, tmp.P("wrong"), "틀린암호")));

        // 7z.dll(7-Zip 계열) — 한글 ZipCrypto 암호는 7-Zip 이 암호를 다른 바이트로 바꿀 수 있어 영문 암호·암호 없음만
        if (IsAscii(pw))
            Assert.Equal(want.Count, SevenZipDllCheck(last, pw));
    }

    /// <summary>계약: 위와 같은 조각을 반디집(bz.exe t)이 연다 — 반디집 한글 암호(CP949) AES·ZipCrypto 픽스처도.</summary>
    [BandizipCliTheory, MemberData(nameof(SpanCases))]
    public async Task 픽스처_ZIP을_나눈_것을_반디집이_푼다(string id, int volume)
    {
        using var tmp = new TempDir("spzbz");
        var (_, pieces) = Span(tmp, id, volume);
        Assert.Equal(0, await BandizipCli.TestAsync(pieces[^1], FixtureCatalog.Get(id).Password ?? "unused"));
    }

    // ───────────── ZIP 나눔: 서비스로 만들기(암호·한글) ─────────────

    private static async Task<(TempDir Tmp, string Src, CompressResult Result)> MakeZipSet(
        string tag, CompressionOptions options, string name = "나눔 결과.zip", int extraRandom = 20_000)
    {
        var tmp = new TempDir(tag);
        string src = SampleTree.Create(tmp.Dir("src"));
        if (extraRandom > 0) AddRandom(src, "추가 무작위.bin", extraRandom);
        var result = await new ZipArchiveService().CompressAsync(new[] { src }, tmp.P(name), options);
        return (tmp, src, result);
    }

    /// <summary>
    /// 계약: ZIP 서비스에 나누기 크기를 주면 이름.z01 … 이름.zip 이 생기고(고른 이름이 마지막 조각), 일집이 마지막 조각으로 열어 원본과 같게 푼다 —
    /// AES·ZipCrypto, 한글 암호(ZipCrypto 는 CP949 로 씀 — 나눔이어도 판정·직접 해제가 됨). 암호 없음·틀림은 암호 예외. 7z.dll 은 영문 암호까지.
    /// </summary>
    [Theory]
    [InlineData(null, ZipEncryption.Aes256)]
    [InlineData(FixtureCatalog.DefaultPassword, ZipEncryption.Aes256)]
    [InlineData(FixtureCatalog.DefaultPassword, ZipEncryption.ZipCrypto)]
    [InlineData(FixtureCatalog.KoreanPassword, ZipEncryption.Aes256)]
    [InlineData(FixtureCatalog.KoreanPassword, ZipEncryption.ZipCrypto)]
    public async Task ZIP_서비스가_나눠_만들고_일집이_다시_푼다(string? password, ZipEncryption encryption)
    {
        const long Vol = 8000;
        var progress = new ProgressRecorder();
        var (tmp, src, result) = await MakeZipSet("spzs", new CompressionOptions { Password = password, Encryption = encryption, SplitBytes = Vol });
        using (tmp)
        {
            var names = result.Volumes.Select(v => Path.GetFileName(v)!).ToList();
            Assert.True(names.Count >= 3, "조각 수: " + names.Count);
            Assert.Equal(ZipSpanWriter.VolumeNames("나눔 결과", names.Count), names);
            Assert.Equal(tmp.P("나눔 결과.zip"), result.OpenPath);
            Assert.Equal(result.Volumes[^1], result.OpenPath);
            AssertSpanShape(result.Volumes, Vol, $"{password}/{encryption}");
            Assert.Equal(new[] { "src" }.Concat(names).OrderBy(n => n, StringComparer.Ordinal), TopNames(tmp.Root));

            string last = result.OpenPath!;
            var svc = Svc.For(last);
            var expected = SampleTree.ExpectedOf(src);
            TreeAssert.ListEquals(await svc.ListEntriesAsync(last, password), SampleTree.FilesAndEmptyDirs(expected));
            string dest = tmp.P("out");
            await svc.ExtractAsync(last, dest, password);
            TreeAssert.ExtractedTreeEquals(dest, expected);
            if (password is not null)
            {
                Assert.IsType<PasswordRequiredException>(await Svc.Catch(() => svc.ExtractAsync(last, tmp.P("none"))));
                Assert.IsType<WrongPasswordException>(await Svc.Catch(() => svc.ExtractAsync(last, tmp.P("bad"), "틀린암호")));
                TreeAssert.NoFiles(tmp.P("bad"));
            }
            if (IsAscii(password))
                Assert.Equal(SampleTree.FilesAndEmptyDirs(expected).Count, SevenZipDllCheck(last, password));
            SevenZipCreateTests.AssertNoLeftovers(tmp.Root);
        }
    }

    /// <summary>
    /// 계약: 반디집이 일집의 나눔 ZIP 을 연다 — 암호 없음, 영문 AES, 한글 ZipCrypto(CP949). 틀린 암호로는 못 연다(암호가 실제로 걸림).
    /// (일집 AES + 한글 암호는 나누지 않아도 반디집이 못 연다 — SharpZipLib 가 UTF-8 로만 씀, BandizipCliTests 의 기록. 반디집이 만든 한글 AES 를 나눈 것은
    /// <see cref="픽스처_ZIP을_나눈_것을_반디집이_푼다"/> 가 통과로 지킨다)
    /// </summary>
    [BandizipCliTheory]
    [InlineData(null, ZipEncryption.Aes256)]
    [InlineData(FixtureCatalog.DefaultPassword, ZipEncryption.Aes256)]
    [InlineData(FixtureCatalog.KoreanPassword, ZipEncryption.ZipCrypto)]
    public async Task ZIP_서비스가_나눈_것을_반디집이_푼다(string? password, ZipEncryption encryption)
    {
        var (tmp, _, result) = await MakeZipSet("spzsbz", new CompressionOptions { Password = password, Encryption = encryption, SplitBytes = 8000 });
        using (tmp)
        {
            Assert.True(result.Volumes.Count >= 3);
            Assert.Equal(0, await BandizipCli.TestAsync(result.OpenPath!, password ?? "unused"));
            if (password is not null)
                Assert.NotEqual(0, await BandizipCli.TestAsync(result.OpenPath!, "틀린암호"));
        }
    }

    // ───────────── 목록(중앙 디렉터리)이 조각을 넘는 경우·ZIP64 ─────────────

    /// <summary>작은 파일 2,000개(4KB 조각이면 목록만 수십 조각)를 만든다.</summary>
    private static string ManySmallFiles(TempDir tmp, int count)
    {
        string dir = tmp.Dir("src", "많은 파일");
        for (int i = 0; i < count; i++)
            File.WriteAllText(Path.Combine(dir, $"f{i:0000}.txt"), $"#{i}");
        return dir;
    }

    /// <summary>
    /// 계약: 작은 파일 2,000개를 4KB 로 나누면 목록(중앙 디렉터리)이 여러 조각에 걸친다(기록 경계에서 넘김, 마지막 .zip 에도 기록이 있음).
    /// 그래도 일집(main ZIP 처리기 — SharpCompress 는 목록을 마지막 조각에서만 읽어 <see cref="SpannedZipStream"/> 로 연다)과 7z.dll 이 모두 읽는다.
    /// </summary>
    [Fact]
    public async Task 목록이_조각을_넘어도_일집과_7z_dll이_읽는다()
    {
        using var tmp = new TempDir("spcd");
        string dir = ManySmallFiles(tmp, 2000);
        var result = await new ZipArchiveService().CompressAsync(new[] { dir }, tmp.P("많음.zip"), new CompressionOptions { SplitBytes = 4096 });
        var facts = AssertSpanShape(result.Volumes, 4096, "2000개");
        Assert.True(facts.CdDisk < facts.ThisDisk - 1, $"목록이 조각을 넘지 않음(시작 {facts.CdDisk}, 마지막 {facts.ThisDisk}) — 시험이 겨냥한 모양이 아님");
        Assert.Equal(2000, facts.TotalEntries);
        Assert.InRange(result.Volumes.Count, 10, ZipSpanWriter.MaxVolumes);

        string last = result.OpenPath!;
        var entries = await Svc.For(last).ListEntriesAsync(last);
        Assert.Equal(2000, entries.Count(e => !e.IsDirectory));
        string dest = tmp.P("out");
        await Svc.For(last).ExtractAsync(last, dest);
        var files = TreeAssert.Files(dest);
        Assert.Equal(2000, files.Count);
        Assert.Equal("#1234", File.ReadAllText(Path.Combine(dest, "많은 파일", "f1234.txt")));
        Assert.Equal(2000, SevenZipDllCheck(last, null));
    }

    /// <summary>계약: 위 모양(목록이 조각을 넘음)을 반디집도 연다(bz.exe t 0). 마지막 조각에 끝 머리만 있으면 반디집이 못 열어(실측) 기록을 하나 이상 둔다.</summary>
    [BandizipCliFact]
    public async Task 목록이_조각을_넘어도_반디집이_푼다()
    {
        using var tmp = new TempDir("spcdbz");
        string dir = ManySmallFiles(tmp, 2000);
        var result = await new ZipArchiveService().CompressAsync(new[] { dir }, tmp.P("많음.zip"), new CompressionOptions { SplitBytes = 4096 });
        Assert.Equal(0, await BandizipCli.TestAsync(result.OpenPath!, "unused"));
    }

    /// <summary>
    /// 계약(ZIP64): 항목 70,000개(65,535 초과)인 ZIP 을 나누면 ZIP64 끝 머리·위치 표에도 디스크 번호를 쓰고, 일집·7z.dll 이 70,000개를 모두 읽는다.
    /// (원본은 파일 7만 개를 디스크에 만들지 않으려고 SharpZipLib 로 바로 만든 ZIP — 나누기는 같은 <see cref="ZipSpanWriter"/>)
    /// </summary>
    [Fact]
    public async Task ZIP64_항목_7만개도_나누고_읽는다()
    {
        const int Count = 70_000;
        using var tmp = new TempDir("sp64");
        string work = tmp.P("큰목록.zip");
        using (var fs = File.Create(work))
        using (var zip = new ZipOutputStream(fs) { UseZip64 = UseZip64.Dynamic })
        {
            zip.SetLevel(0);
            var one = new byte[1];
            for (int i = 0; i < Count; i++)
            {
                zip.PutNextEntry(new ZipEntry($"d/{i:00000}.t") { DateTime = SampleTree.Mtime });
                one[0] = (byte)(i & 0xFF);
                zip.Write(one, 0, 1);
                zip.CloseEntry();
            }
        }
        var pieces = ZipSpanWriter.Split(work, tmp.P("큰목록나눔"), 1024 * 1024, CancellationToken.None);
        var facts = AssertSpanShape(pieces, 1024 * 1024, "ZIP64");
        Assert.True(facts.Zip64, "ZIP64 끝 머리·위치 표가 없음");
        Assert.Equal(Count, facts.TotalEntries);

        string last = pieces[^1];
        var entries = await Svc.For(last).ListEntriesAsync(last);
        Assert.Equal(Count, entries.Count);
        Assert.Equal(Count, SevenZipDllCheck(last, null));
        // 몇 개만 골라 풀어 내용 확인(7만 개를 다 풀면 오래 걸림)
        string dest = tmp.P("out");
        await Svc.For(last).ExtractEntriesAsync(last, dest, new[] { "d/00000.t", "d/65535.t", "d/69999.t" });
        Assert.Equal(new byte[] { 65535 & 0xFF }, File.ReadAllBytes(Path.Combine(dest, "d", "65535.t")));
        Assert.Equal(new byte[] { 69999 & 0xFF }, File.ReadAllBytes(Path.Combine(dest, "d", "69999.t")));
    }

    // ───────────── 거절·빠진 조각·취소·이름 겹침 ─────────────

    /// <summary>계약: 조각이 너무 많으면(ZIP 99·7Z 999 초과) 쉬운 말로 거절하고 조각·임시 폴더·고른 이름의 파일을 남기지 않는다.</summary>
    [Theory]
    [InlineData(".zip", 500L, 99)]
    [InlineData(".7z", 50L, 999)]
    public async Task 조각이_너무_많으면_거절하고_아무것도_남기지_않는다(string ext, long volume, int max)
    {
        using var tmp = new TempDir("spmany");
        string src = SampleTree.Create(tmp.Dir("src"));
        string arc = tmp.P("너무많음" + ext);
        var ex = await Svc.Catch(() => Svc.Locator.ResolveByExtension(arc)!.CompressAsync(new[] { src }, arc, new CompressionOptions { SplitBytes = volume }));
        var tooMany = Assert.IsType<TooManyVolumesException>(ex);
        Assert.Equal(max, tooMany.MaxVolumes);
        Assert.Equal($"조각이 너무 많아요({max}개까지). 나눌 크기를 키워 주세요.", tooMany.Message);
        Assert.Equal(tooMany.Message, ErrorMessages.ToUserMessage(tooMany));
        Assert.Equal(new[] { "src" }, TopNames(tmp.Root));
    }

    /// <summary>
    /// 계약(W2-01 과 같은 문구): 나눈 세트에서 가운데 조각을 지운 복사본은 목록·풀기 모두 그 조각 이름으로 <see cref="VolumeMissingException"/> —
    /// 손상이나 암호 문제로 뭉뚱그리지 않고, 풀 대상 폴더도 만들지 않는다. ZIP(.z02)·7Z(.7z.002)는 일집이 만든 세트, RAR(.part2.rar)는 합성 3조각.
    /// </summary>
    [Theory]
    [InlineData(".zip", "빠짐.z02")]
    [InlineData(".7z", "빠짐.7z.002")]
    [InlineData(".rar", "빠짐.part2.rar")]
    public async Task 가운데_조각이_빠지면_그_이름으로_알린다(string ext, string missing)
    {
        using var tmp = new TempDir("spmiss");
        string vols = tmp.Dir("vols");
        string open;
        if (ext == ".rar")
        {
            byte[] Rar(string name, int number, bool more) =>
                RawRar.Rar5(new[] { new RawRar.Item(name, Encoding.ASCII.GetBytes("part " + number)) }, volume: true,
                    volumeNumber: number == 0 ? null : number, moreVolumes: more).Bytes;   // 첫 조각은 번호 칸 없음(TruncationTests 와 같은 모양)
            File.WriteAllBytes(Path.Combine(vols, "빠짐.part1.rar"), Rar("p1.txt", 0, true));
            File.WriteAllBytes(Path.Combine(vols, "빠짐.part3.rar"), Rar("p3.txt", 2, false));
            open = Path.Combine(vols, "빠짐.part1.rar");
        }
        else
        {
            string src = SampleTree.Create(tmp.Dir("src"));
            AddRandom(src, "추가 무작위.bin", 60_000);
            string made = tmp.Dir("made");
            var result = await Svc.Locator.ResolveByExtension("x" + ext)!.CompressAsync(new[] { src }, Path.Combine(made, "빠짐" + ext),
                new CompressionOptions { SplitBytes = 16 * 1024 });
            Assert.True(result.Volumes.Count >= 4);
            foreach (var v in result.Volumes.Where(v => Path.GetFileName(v) != missing))
                File.Copy(v, Path.Combine(vols, Path.GetFileName(v)));
            Assert.Contains(result.Volumes, v => Path.GetFileName(v) == missing);
            open = Path.Combine(vols, Path.GetFileName(result.OpenPath!));
        }

        string dest = tmp.P("풀 곳");
        var listEx = await Svc.Catch(() => Svc.For(open).ListEntriesAsync(open));
        var extractEx = await Svc.Catch(() => Svc.For(open).ExtractAsync(open, dest));
        foreach (var e in new[] { listEx, extractEx })
        {
            var vm = Assert.IsType<VolumeMissingException>(e);
            Assert.Equal(missing, vm.MissingVolumeName);
            Assert.Equal($"'{missing}' 조각이 없어요. 나눠진 조각을 모두 같은 폴더에 두고 다시 해 주세요.", ErrorMessages.ToUserMessage(vm));
            Assert.False(Internals.IsPasswordRelated(vm));
        }
        Assert.False(Directory.Exists(dest), "조각이 빠졌는데 풀 대상 폴더가 생김");
    }

    /// <summary>계약: 나눔 ZIP 의 마지막 조각(.zip)만 있으면 첫 조각 이름(.z01)으로 알린다.</summary>
    [Fact]
    public async Task 마지막_조각만_있으면_z01이_없다고_알린다()
    {
        var (tmp, _, result) = await MakeZipSet("splast", new CompressionOptions { SplitBytes = 16 * 1024 }, name: "혼자.zip");
        using (tmp)
        {
            string alone = tmp.P("혼자 둔 곳", "혼자.zip");
            Directory.CreateDirectory(Path.GetDirectoryName(alone)!);
            File.Copy(result.OpenPath!, alone);
            var ex = Assert.IsType<VolumeMissingException>(await Svc.Catch(() => Svc.For(alone).ListEntriesAsync(alone)));
            Assert.Equal("혼자.z01", ex.MissingVolumeName);
        }
    }

    /// <summary>
    /// 계약: 압축 도중(첫 바이트)이든 조각으로 나누는 도중이든 취소하면 OCE 이고, 대상 폴더에 조각도 임시 폴더(.일집-임시-…)도 고른 이름의 파일도 없다.
    /// </summary>
    [Theory]
    [InlineData(".zip", false)]
    [InlineData(".zip", true)]
    [InlineData(".7z", false)]
    [InlineData(".7z", true)]
    public async Task 취소하면_조각도_임시_폴더도_남지_않는다(string ext, bool duringSplit)
    {
        using var tmp = new TempDir("spcancel");
        string src = SampleTree.Create(tmp.Dir("src"));
        AddRandom(src, "추가 무작위.bin", 200_000);
        string arc = tmp.P("취소" + ext);
        using var cts = new CancellationTokenSource();
        var progress = new ProgressRecorder(p =>
        {
            if (duringSplit ? p.CurrentFile == SplitArchiveOutput.StageSplitting : p.BytesProcessed > 0) cts.Cancel();
        });
        var ex = await Svc.Catch(() => Svc.Locator.ResolveByExtension(arc)!.CompressAsync(new[] { src }, arc,
            new CompressionOptions { SplitBytes = 32 * 1024 }, progress, cts.Token));
        Assert.IsAssignableFrom<OperationCanceledException>(ex);
        if (duringSplit) Assert.Contains(progress.Reports, r => r.CurrentFile == SplitArchiveOutput.StageSplitting);
        Assert.Equal(new[] { "src" }, TopNames(tmp.Root));
    }

    /// <summary>
    /// 계약(세트 단위 이름): 같은 이름의 조각이 하나라도 있으면 새 세트 전체가 '이름 (2)' 가 되고, 있던 파일은 바이트 그대로다
    /// (ZIP 은 고른 이름의 .zip 하나만 있어도 — 마지막 조각 이름과 겹침. 7Z 는 .001 이 있으면).
    /// </summary>
    [Theory]
    [InlineData(".zip", new[] { "겹침.z01", "겹침.zip" }, "겹침 (2).zip", "겹침 (2).z01")]
    [InlineData(".zip", new[] { "겹침.zip" }, "겹침 (2).zip", "겹침 (2).z01")]
    [InlineData(".7z", new[] { "겹침.7z.001" }, "겹침 (2).7z.001", "겹침 (2).7z.001")]
    public async Task 같은_이름_세트가_있으면_이름_2_세트로_만든다(string ext, string[] existing, string openName, string firstName)
    {
        using var tmp = new TempDir("spname");
        string src = SampleTree.Create(tmp.Dir("src"));
        AddRandom(src, "추가 무작위.bin", 60_000);
        var old = new Dictionary<string, byte[]>();
        foreach (var n in existing)
        {
            old[n] = Encoding.UTF8.GetBytes("있던 조각 " + n);
            File.WriteAllBytes(tmp.P(n), old[n]);
        }
        string arc = tmp.P("겹침" + ext);
        var result = await Svc.Locator.ResolveByExtension(arc)!.CompressAsync(new[] { src }, arc, new CompressionOptions { SplitBytes = 16 * 1024 });

        Assert.Equal(openName, Path.GetFileName(result.OpenPath));
        Assert.Equal(firstName, Path.GetFileName(result.Volumes[0]));
        Assert.All(result.Volumes, v => Assert.StartsWith("겹침 (2)", Path.GetFileName(v)));
        foreach (var (n, bytes) in old)
            Assert.Equal(bytes, File.ReadAllBytes(tmp.P(n)));
        string dest = tmp.P("out");
        await Svc.For(result.OpenPath!).ExtractAsync(result.OpenPath!, dest);
        TreeAssert.ExtractedTreeEquals(dest, SampleTree.ExpectedOf(src));
        SevenZipCreateTests.AssertNoLeftovers(tmp.Root);
    }

    /// <summary>
    /// 계약: 다 만든 것이 조각 크기 이하면 나누지 않고 고른 이름의 보통 파일 하나로 둔다(조각 목록 없음 — 조각 하나짜리 세트는 받는 사람만 헷갈림).
    /// 덮어쓰기는 나누지 않을 때와 같다(저장 창에서 덮어쓰기를 고른 기존 파일은 바뀜).
    /// </summary>
    [Theory]
    [InlineData(".zip")]
    [InlineData(".7z")]
    public async Task 한_조각에_들어가면_나누지_않는다(string ext)
    {
        using var tmp = new TempDir("spone");
        string src = SampleTree.Create(tmp.Dir("src"));
        string arc = tmp.P("작음" + ext);
        File.WriteAllText(arc, "예전 파일(덮어쓰기 확인을 누른 상태)");
        var result = await Svc.Locator.ResolveByExtension(arc)!.CompressAsync(new[] { src }, arc, new CompressionOptions { SplitBytes = 10 * 1024 * 1024 });
        Assert.Empty(result.Volumes);
        Assert.Null(result.OpenPath);
        Assert.Equal(new[] { "src", "작음" + ext }, TopNames(tmp.Root));
        string dest = tmp.P("out");
        await Svc.For(arc).ExtractAsync(arc, dest);
        TreeAssert.ExtractedTreeEquals(dest, SampleTree.ExpectedOf(src));
    }

    /// <summary>
    /// 계약: ZIP 나눔의 진행률도 모양이 맞다(줄지 않음, 끝 보고는 맨 끝 한 번) — 압축이 끝난 뒤 '조각으로 나누는 중'을 보이고 마지막에 끝 보고.
    /// 원본 폴더 안에 저장해도 고른 이름의 파일·임시 폴더는 담지 않는다.
    /// </summary>
    [Fact]
    public async Task ZIP_나누기_진행률과_원본_폴더_안_저장()
    {
        using var tmp = new TempDir("spprog");
        string src = SampleTree.Create(tmp.Dir("src"));
        AddRandom(src, "추가 무작위.bin", 60_000);
        string arc = Path.Combine(src, "안에 저장.zip");
        File.WriteAllText(arc, "예전 압축(원본 폴더 안)");
        var progress = new ProgressRecorder();
        var result = await new ZipArchiveService().CompressAsync(new[] { src }, arc, new CompressionOptions { SplitBytes = 16 * 1024 }, progress);
        ProgressTests.AssertWellFormed(progress.Reports, "ZIP 나누기", expectIntermediate: true);
        Assert.Contains(progress.Reports, r => r.CurrentFile == SplitArchiveOutput.StageSplitting);
        var names = (await Svc.For(result.OpenPath!).ListEntriesAsync(result.OpenPath!)).Select(e => e.Path).ToList();
        Assert.DoesNotContain(names, n => n.EndsWith("안에 저장.zip", StringComparison.Ordinal));
        Assert.DoesNotContain(names, n => n.Contains(StagingFolder.Prefix, StringComparison.Ordinal));
        SevenZipCreateTests.AssertNoLeftovers(src);
    }

    // ───────────── 화면(VM)·옵션 창 규칙 ─────────────

    /// <summary>
    /// 계약(화면): 옵션 창이 준 나누기 크기(가짜 창의 CompressionOptions.SplitBytes)가 서비스로 그대로 가고, 끝나면 실제 조각 이름으로
    /// '3개 조각으로 나눠 만들었어요(이름.z01 … 이름.zip). 받는 사람은 …' 안내 뒤 그 세트를 연다(ZIP 은 .zip, 7Z 는 .001).
    /// </summary>
    [Theory]
    [InlineData(".zip")]
    [InlineData(".7z")]
    public async Task 화면은_나눈_세트를_열고_조각_이름으로_알린다(string ext)
    {
        using var tmp = new TempDir("spvm");
        string src = SampleTree.Create(tmp.Dir("src"));
        AddRandom(src, "추가 무작위.bin", 60_000);
        string save = tmp.P("메일용" + ext);
        var ui = new FakeUserInteraction { SavePath = save, CompressionOptions = new CompressionOptions { SplitBytes = 16 * 1024 } };
        var vm = new MainViewModel(ui);
        vm.AddToStaging(new[] { src });
        await VmRunner.Run(() => vm.CompressCommand.ExecuteAsync(null), "나눠 압축");

        Assert.Empty(ui.Errors);
        var pieces = Directory.GetFiles(tmp.Root, "메일용*").Select(p => Path.GetFileName(p)!).OrderBy(n => n, StringComparer.Ordinal).ToList();
        Assert.True(pieces.Count >= 4, string.Join(", ", pieces));
        string openName = ext == ".zip" ? "메일용.zip" : "메일용.7z.001";
        Assert.Equal(tmp.P(openName), vm.CurrentArchivePath);
        Assert.NotEmpty(vm.Entries);
        string firstName = ext == ".zip" ? "메일용.z01" : "메일용.7z.001";
        string lastName = ext == ".zip" ? "메일용.zip" : $"메일용.7z.{pieces.Count:000}";
        var info = Assert.Single(ui.Infos);
        Assert.StartsWith($"{pieces.Count}개 조각으로 나눠 만들었어요({firstName} … {lastName}). 받는 사람은 모든 조각을 같은 폴더에 두고 {openName}을 열면 돼요.", info);
        Assert.Contains("Windows 탐색기로는 열리지 않아요", info);
    }

    /// <summary>조각이 둘이면 '…' 대신 쉼표로 둘 다 적는다.</summary>
    [Fact]
    public void 조각이_둘이면_쉼표로_적는다()
    {
        string m = MainViewModel.SplitDoneMessage(new[] { @"C:\a\x.z01", @"C:\a\x.zip" }, @"C:\a\x.zip");
        Assert.StartsWith("2개 조각으로 나눠 만들었어요(x.z01, x.zip). 받는 사람은 모든 조각을 같은 폴더에 두고 x.zip을 열면 돼요.", m);
    }

    /// <summary>계약(화면): 가짜 옵션 창의 SplitBytes 는 형식과 상관없이 서비스까지 그대로 간다(형식에 맞지 않는 값을 버리는 것은 옵션 창 규칙의 몫).</summary>
    [Fact]
    public async Task 화면은_나누기_크기를_서비스에_넘긴다()
    {
        using var tmp = new TempDir("spvmrec");
        string file = tmp.P("a.txt");
        File.WriteAllText(file, "a");
        var ui = new FakeUserInteraction { SavePath = tmp.P("결과.zip"), CompressionOptions = new CompressionOptions { SplitBytes = 12_345_678 } };
        var recorder = new RecordingService();
        var locator = new ArchiveServiceLocator();
        locator.Register(recorder);
        var vm = new MainViewModel(ui, locator);
        vm.AddToStaging(new[] { file });
        await VmRunner.Run(() => vm.CompressCommand.ExecuteAsync(null), "압축");
        Assert.Equal(12_345_678, Assert.Single(recorder.Options).SplitBytes);
    }

    /// <summary>계약: TAR 은 나누지 않는다 — 옵션 창 칸이 숨겨지고(아래 규칙), 서비스에 값이 와도 보통 tar 하나만 만든다.</summary>
    [Fact]
    public async Task TAR_는_나누기를_무시한다()
    {
        using var tmp = new TempDir("sptar");
        string src = SampleTree.Create(tmp.Dir("src"));
        string arc = tmp.P("묶음.tar");
        var result = await new TarArchiveService().CompressAsync(new[] { src }, arc, new CompressionOptions { SplitBytes = 4096 });
        Assert.Empty(result.Volumes);
        Assert.Equal(new[] { "src", "묶음.tar" }, TopNames(tmp.Root));
    }

    /// <summary>계약(옵션 창 규칙): '나누기' 칸은 ZIP·7Z 만 보이고 TAR·GZ·BZ2 는 숨긴다. 값도 ZIP·7Z 만 담는다.</summary>
    [Fact]
    public void 나누기_칸은_ZIP과_7Z만_보인다()
    {
        Assert.True(CompressionOptionsRules.LayoutFor(".zip").ShowSplit);
        Assert.True(CompressionOptionsRules.LayoutFor(".7z").ShowSplit);
        Assert.True(CompressionOptionsRules.LayoutFor(".ZIP").ShowSplit);
        foreach (var ext in new[] { ".tar", ".gz", ".bz2" })
        {
            Assert.False(CompressionOptionsRules.LayoutFor(ext).ShowSplit, ext);
            Assert.Null(CompressionOptionsRules.Build(2, null, ext, ZipEncryption.Aes256, true, false, splitBytes: 10 * CompressionOptionsRules.MegaByte).SplitBytes);
        }
        Assert.Equal(20 * CompressionOptionsRules.MegaByte,
            CompressionOptionsRules.Build(2, null, ".7z", ZipEncryption.Aes256, true, false, splitBytes: 20 * CompressionOptionsRules.MegaByte).SplitBytes);
        Assert.Null(CompressionOptionsRules.Build(2, null, ".zip", ZipEncryption.Aes256, true, false).SplitBytes);   // 기본은 나누지 않음
        Assert.Null(CompressionOptions.Default.SplitBytes);
    }

    /// <summary>계약(옵션 창 규칙): 나누지 않음·10MB·20MB(메일 첨부용)·직접 입력(MB). 직접 입력은 1MB 이상(소수 가능), 숫자가 아니면 안내.</summary>
    [Theory]
    [InlineData(0, null, true, null, null)]
    [InlineData(1, null, true, 10L * 1024 * 1024, null)]
    [InlineData(2, null, true, 20L * 1024 * 1024, null)]
    [InlineData(3, "15", true, 15L * 1024 * 1024, null)]
    [InlineData(3, " 9.5 MB ", true, 9_961_472L, null)]
    [InlineData(3, "1", true, 1L * 1024 * 1024, null)]
    [InlineData(3, "0.5", false, null, CompressionOptionsRules.SplitTooSmallMessage)]
    [InlineData(3, "0", false, null, CompressionOptionsRules.SplitTooSmallMessage)]
    [InlineData(3, "-3", false, null, CompressionOptionsRules.SplitTooSmallMessage)]
    [InlineData(3, "4096", false, null, CompressionOptionsRules.SplitTooLargeMessage)]
    [InlineData(3, "열", false, null, CompressionOptionsRules.SplitNotNumberMessage)]
    [InlineData(3, "", false, null, CompressionOptionsRules.SplitNotNumberMessage)]
    public void 나누기_크기_고르기(int index, string? custom, bool ok, long? bytes, string? error)
    {
        Assert.Equal(new[] { "나누지 않음", "10MB", "20MB (메일 첨부용)", "직접 입력 (MB)" }, CompressionOptionsRules.SplitLabels);
        Assert.Equal(ok, CompressionOptionsRules.TryGetSplitBytes(index, custom, out var got, out var message));
        Assert.Equal(bytes, got);
        Assert.Equal(error, message);
        Assert.Equal("1MB 이상으로 넣어 주세요.", CompressionOptionsRules.SplitTooSmallMessage);
    }

    /// <summary>계약(소스): 옵션 창 XAML 에 '나누기' 칸(처음엔 숨김 — 코드가 ZIP·7Z 에서만 보임)이 있고 코드가 규칙을 쓴다.</summary>
    [Fact]
    public void 창_XAML에_나누기_칸이_있다()
    {
        string xaml = File.ReadAllText(RepoFiles.PathOf("Iljip", "Views", "CompressionOptionsDialog.xaml"));
        Assert.Matches("x:Name=\"SplitPanel\"[^>]*Visibility=\"Collapsed\"", xaml);
        Assert.Contains("x:Name=\"SplitCombo\"", xaml);
        Assert.Contains("x:Name=\"SplitCustomBox\"", xaml);
        string code = File.ReadAllText(RepoFiles.PathOf("Iljip", "Views", "CompressionOptionsDialog.xaml.cs"));
        Assert.Contains("CompressionOptionsRules.SplitLabels", code);
        Assert.Contains("CompressionOptionsRules.TryGetSplitBytes", code);
        Assert.Contains("layout.ShowSplit", code);
        Assert.Contains("splitBytes: splitBytes", code);
    }

    /// <summary>가짜 압축 서비스 — 받은 옵션만 기록하고 빈 결과(화면이 결과를 여는 것은 실패해도 됨).</summary>
    private sealed class RecordingService : IArchiveService
    {
        public List<CompressionOptions> Options { get; } = new();
        public IReadOnlyList<string> SupportedExtensions { get; } = new[] { ".zip" };
        public Task<IReadOnlyList<ArchiveEntry>> ListEntriesAsync(string a, string? p = null, CancellationToken c = default) =>
            Task.FromResult<IReadOnlyList<ArchiveEntry>>(Array.Empty<ArchiveEntry>());
        public Task ExtractAsync(string a, string d, string? p = null, IProgress<ArchiveProgress>? pr = null, CancellationToken c = default) => throw new NotImplementedException();
        public Task ExtractEntriesAsync(string a, string d, IReadOnlyCollection<string> e, string? p = null, IProgress<ArchiveProgress>? pr = null, CancellationToken c = default) => throw new NotImplementedException();

        public Task<CompressResult> CompressAsync(IEnumerable<string> s, string a, CompressionOptions? o = null, IProgress<ArchiveProgress>? pr = null, CancellationToken c = default)
        {
            Options.Add(o!);
            return Task.FromResult(CompressResult.Empty);
        }
    }
}
