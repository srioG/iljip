using System.Formats.Tar;
using System.IO.Compression;
using System.Text;
using Iljip.Models;
using Iljip.Services;
using Iljip.Tests.TestSupport;

namespace Iljip.Tests.Contract;

/// <summary>
/// [범위 2] 일집이 만들 수 있는 형식(ZIP 레벨별·AES, TAR, GZ, BZ2)으로 압축 → 같은 서비스로 해제 → 원본과 sha256 일치.
/// 일집이 만든 파일을 다른 구현(System.IO.Compression, System.Formats.Tar, SharpCompress 직접)으로도 열어 교차 확인.
/// 계약 테스트는 '해제 내용 sha 일치'를 먼저 보고, 목록은 엔진과 무관한 범위(파일 항목 정확히 + 폴더 항목은 있어도 없어도)만 본다.
/// 목록의 정확한 모양·ZIP 방식 번호·EFS 플래그 같은 '현재 동작'은 '기록_' 테스트(Trait Kind=Record)로 따로 고정했다.
/// </summary>
public class RoundTripTests
{
    public static TheoryData<CompressionLevelOption> Levels => new()
    {
        CompressionLevelOption.Store, CompressionLevelOption.Fastest, CompressionLevelOption.Normal, CompressionLevelOption.Maximum
    };

    /// <summary>ZIP 4단계 수준 모두: 한글·공백·기호·빈 파일·빈 폴더·5단 깊은 폴더·바이너리가 그대로 돌아온다.</summary>
    [Theory]
    [MemberData(nameof(Levels))]
    public async Task ZIP_수준별_왕복이_원본과_같다(CompressionLevelOption level)
    {
        using var tmp = new TempDir("rtz");
        string src = SampleTree.Create(tmp.Dir("src"));
        var expected = SampleTree.ExpectedOf(src);
        string arc = tmp.P("왕복.zip");
        var svc = Svc.For(arc);

        await svc.CompressAsync(new[] { src }, arc, new CompressionOptions { Level = level });
        string dest = tmp.P("out");
        await svc.ExtractAsync(arc, dest);
        TreeAssert.ExtractedTreeEquals(dest, expected, context: level.ToString());
        AssertMtimes(dest, SampleTree.Mtime);
        TreeAssert.ListFilesEqual(await svc.ListEntriesAsync(arc), expected, context: level.ToString());
    }

    /// <summary>
    /// 현재 동작 기록: ZIP 처리기, 일집 ZIP 목록에는 '빈 폴더'만 폴더 항목이 있다(파일이 든 폴더는 항목이 없고 해제 때 경로로 생김)
    /// — 반디집 ZIP 은 모든 폴더 항목을 넣는 점이 다름. 표준 라이브러리로 봐도 '하위/' 항목이 없다.
    /// </summary>
    [Fact, Trait(Kinds.Key, Kinds.Record)]
    public async Task 기록_일집_ZIP_목록에는_빈폴더만_폴더항목()
    {
        using var tmp = new TempDir("rtzr");
        string src = SampleTree.Create(tmp.Dir("src"));
        string arc = tmp.P("왕복.zip");
        var svc = Svc.For(arc);
        Assert.IsType<ZipArchiveService>(svc);
        await svc.CompressAsync(new[] { src }, arc);
        TreeAssert.ListEquals(await svc.ListEntriesAsync(arc), SampleTree.FilesAndEmptyDirs(SampleTree.ExpectedOf(src)));
        using var za = ZipFile.OpenRead(arc);
        Assert.DoesNotContain(za.Entries, e => e.FullName == $"{SampleTree.RootName}/하위/");
    }

    /// <summary>수준이 높을수록 결과가 작거나 같다. '저장(Store)'은 원본 합계보다 크다(압축하지 않음).</summary>
    [Fact]
    public async Task ZIP_수준이_실제로_반영된다()
    {
        using var tmp = new TempDir("lvl");
        string src = SampleTree.Create(tmp.Dir("src"));
        long totalBytes = Directory.EnumerateFiles(src, "*", SearchOption.AllDirectories).Sum(f => new FileInfo(f).Length);
        var sizes = new Dictionary<CompressionLevelOption, long>();
        foreach (var level in Enum.GetValues<CompressionLevelOption>())
        {
            string arc = tmp.P($"{level}.zip");
            await new ZipArchiveService().CompressAsync(new[] { src }, arc, new CompressionOptions { Level = level });
            sizes[level] = new FileInfo(arc).Length;
        }
        Assert.True(sizes[CompressionLevelOption.Store] > totalBytes, "Store 인데 크기가 줄었음(=압축됨)");
        Assert.True(sizes[CompressionLevelOption.Store] > sizes[CompressionLevelOption.Fastest]);
        Assert.True(sizes[CompressionLevelOption.Fastest] >= sizes[CompressionLevelOption.Maximum]);
    }

    /// <summary>
    /// 현재 동작 기록: '저장(Store)'도 ZIP 방식 번호는 8(Deflate)이고 안을 '압축 안 한 Deflate 블록'으로 채운다
    /// (SharpZipLib SetLevel(0)) — 반디집 -l:0 은 방식 0(Store). 어느 프로그램이든 열리지만 방식 번호가 다르다.
    /// </summary>
    [Fact, Trait(Kinds.Key, Kinds.Record)]
    public async Task 기록_ZIP_모든_수준이_방식8()
    {
        using var tmp = new TempDir("lvlr");
        string src = SampleTree.Create(tmp.Dir("src"));
        foreach (var level in Enum.GetValues<CompressionLevelOption>())
        {
            string arc = tmp.P($"{level}.zip");
            await new ZipArchiveService().CompressAsync(new[] { src }, arc, new CompressionOptions { Level = level });
            var methods = ZipHeaders.CentralEntries(arc).Where(e => !e.Name.EndsWith('/')).Select(e => e.Method).Distinct().ToList();
            Assert.Equal(new[] { 8 }, methods);
        }
    }

    /// <summary>ZIP 암호(영문·한글 암호): 같은 암호로 풀면 원본과 같다.</summary>
    [Theory]
    [InlineData(FixtureCatalog.DefaultPassword)]
    [InlineData(FixtureCatalog.KoreanPassword)]
    public async Task ZIP_암호_왕복이_원본과_같다(string password)
    {
        using var tmp = new TempDir("rta");
        string src = SampleTree.Create(tmp.Dir("src"));
        var expected = SampleTree.ExpectedOf(src);
        string arc = tmp.P("암호.zip");
        var svc = new ZipArchiveService();
        await svc.CompressAsync(new[] { src }, arc, new CompressionOptions { Password = password });
        string dest = tmp.P("out");
        await svc.ExtractAsync(arc, dest, password);
        TreeAssert.ExtractedTreeEquals(dest, expected);
    }

    /// <summary>
    /// 현재 동작 기록: 암호 방식을 고르지 않으면(기본 AES-256) 일집의 암호 ZIP 은 모든 파일이 WinZip AES(방식 99)다.
    /// (W1-C 부터 ZipCrypto 도 고를 수 있음 — KnownGapTests.ZIP_암호를_ZipCrypto로_고를_수_있다, ZipEncryptionTests)
    /// 파일명은 UTF-8 + EFS 플래그(bit 11).
    /// </summary>
    [Fact, Trait(Kinds.Key, Kinds.Record)]
    public async Task ZIP_암호는_항상_AES_방식99_그리고_이름은_UTF8_EFS()
    {
        using var tmp = new TempDir("aes");
        string src = SampleTree.Create(tmp.Dir("src"));
        string arc = tmp.P("aes.zip");
        await new ZipArchiveService().CompressAsync(new[] { src }, arc, new CompressionOptions { Password = "p" });
        var entries = ZipHeaders.CentralEntries(arc);
        var files = entries.Where(e => !e.Name.EndsWith('/')).ToList();
        Assert.All(files, e => Assert.Equal(99, e.Method));
        Assert.All(files, e => Assert.True((e.Flags & 1) != 0, "암호 플래그(bit 0) 없음: " + e.Name));
        Assert.All(entries, e => Assert.True((e.Flags & 0x0800) != 0, "EFS(UTF-8) 플래그 없음: " + e.Name));
        Assert.Contains(entries, e => e.Name == $"{SampleTree.RootName}/한글 파일.txt");
    }

    /// <summary>
    /// 교차 확인: 일집이 만든 (암호 없는) ZIP 을 .NET 표준 System.IO.Compression 으로 열면
    /// 한글 이름·내용·빈 폴더 항목이 모두 맞다 → 일집 ZIP 은 '일집만 읽는 ZIP'이 아니다.
    /// ★ 이름은 '한국어 Windows 탐색기 방식'으로도 맞아야 한다(EFS 플래그가 있으면 UTF-8, 없으면 CP949 — <see cref="ZipHeaders.ExplorerNames"/>).
    /// .NET 기본값은 플래그가 없어도 UTF-8 로 읽어서, 플래그가 빠진(거래처 탐색기에서 한글이 깨지는) 회귀를 못 잡는다.
    /// UTF-8+EFS 든, 플래그 없는 CP949 든 탐색기에서 바르게 보이면 통과.
    /// </summary>
    [Fact]
    public async Task 일집_ZIP을_표준_라이브러리로_열어도_같다()
    {
        using var tmp = new TempDir("xz");
        string src = SampleTree.Create(tmp.Dir("src"));
        var expected = SampleTree.ExpectedOf(src);
        string arc = tmp.P("교차.zip");
        await new ZipArchiveService().CompressAsync(new[] { src }, arc);

        var explorer = ZipHeaders.ExplorerNames(arc).ToHashSet(StringComparer.Ordinal);
        foreach (var e in expected.Where(e => !e.IsDir))
            Assert.True(explorer.Contains(e.Path),
                "한국어 탐색기 방식으로 읽으면 이름이 다름/없음: " + e.Path + " — 탐색기가 보는 이름들: " + string.Join(" | ", explorer));

        using var za = ZipFile.OpenRead(arc);
        var byName = za.Entries.ToDictionary(e => e.FullName, StringComparer.Ordinal);
        foreach (var e in expected.Where(e => !e.IsDir))
        {
            Assert.True(byName.TryGetValue(e.Path, out var ze), "표준 라이브러리에서 이름이 다름/없음: " + e.Path);
            using var s = ze!.Open();
            using var ms = new MemoryStream();
            s.CopyTo(ms);
            Assert.Equal(e.Sha256, TreeAssert.Sha256(ms.ToArray()));
        }
        Assert.Contains($"{SampleTree.RootName}/빈 폴더/", byName.Keys);
    }

    /// <summary>
    /// 현재 동작 기록: 암호 없는 일집 ZIP 도 ASCII 가 아닌 이름의 모든 항목에 EFS(UTF-8) 플래그(bit 11)를 켠다
    /// (ZipArchiveService 의 IsUnicodeText=true). 한국어 탐색기에서 한글 이름이 깨지지 않게 하는 장치.
    /// </summary>
    [Fact, Trait(Kinds.Key, Kinds.Record)]
    public async Task 기록_암호없는_일집_ZIP도_한글이름에_EFS_플래그()
    {
        using var tmp = new TempDir("efs");
        string src = SampleTree.Create(tmp.Dir("src"));
        string arc = tmp.P("plain.zip");
        await new ZipArchiveService().CompressAsync(new[] { src }, arc);
        var nonAscii = ZipHeaders.CentralEntries(arc).Where(e => e.Name.Any(c => c > 0x7F)).ToList();
        Assert.NotEmpty(nonAscii);
        Assert.All(nonAscii, e => Assert.True((e.Flags & 0x0800) != 0, "EFS(UTF-8) 플래그 없음: " + e.Name));
    }

    /// <summary>교차 확인: 일집 AES ZIP 을 SharpCompress 로 직접(일집 해제 코드를 거치지 않고) 열어도 내용이 맞다.</summary>
    [Fact]
    public async Task 일집_AES_ZIP을_SharpCompress로_직접_열어도_같다()
    {
        using var tmp = new TempDir("xa");
        string src = SampleTree.Create(tmp.Dir("src"));
        var expected = SampleTree.ExpectedOf(src).Where(e => !e.IsDir).ToDictionary(e => e.Path, e => e.Sha256);
        string arc = tmp.P("aes.zip");
        await new ZipArchiveService().CompressAsync(new[] { src }, arc, new CompressionOptions { Password = FixtureCatalog.DefaultPassword });

        using var archive = SharpCompress.Archives.Zip.ZipArchive.OpenArchive(arc, new SharpCompress.Readers.ReaderOptions { Password = FixtureCatalog.DefaultPassword });
        int n = 0;
        foreach (var e in archive.Entries.Where(e => !e.IsDirectory))
        {
            using var s = e.OpenEntryStream();
            using var ms = new MemoryStream();
            s.CopyTo(ms);
            Assert.Equal(expected[e.Key!], TreeAssert.Sha256(ms.ToArray()));
            n++;
        }
        Assert.Equal(expected.Count, n);
    }

    /// <summary>
    /// TAR 왕복: 파일·내용·수정 시각이 그대로. 빈 폴더는 엔진마다 다를 수 있어 '있어도 없어도 됨'으로 둔다
    /// (지금은 담김 — KnownGapTests.TAR_압축이_빈폴더를_담는다).
    /// </summary>
    [Fact]
    public async Task TAR_왕복이_원본과_같다_빈폴더_제외()
    {
        using var tmp = new TempDir("rtt");
        string src = SampleTree.Create(tmp.Dir("src"));
        var expected = SampleTree.ExpectedOf(src);
        string emptyDir = $"{SampleTree.RootName}/빈 폴더";
        string arc = tmp.P("왕복.tar");
        var svc = Svc.For(arc);
        await svc.CompressAsync(new[] { src }, arc);

        string dest = tmp.P("out");
        await svc.ExtractAsync(arc, dest);
        TreeAssert.ExtractedTreeEquals(dest, expected, optional: new[] { emptyDir });
        AssertMtimes(dest, SampleTree.Mtime);
        TreeAssert.ListFilesEqual(await svc.ListEntriesAsync(arc), expected);
    }

    /// <summary>
    /// 현재 동작 기록: TAR 처리기, 일집 TAR 목록에는 파일과 '빈 폴더'만 폴더 항목으로 있다(일집 ZIP 과 같은 모양 —
    /// 파일이 든 폴더는 해제할 때 경로에서 생김). 예전(SharpCompress 0.38)엔 빈 폴더도 빠져 폴더 항목이 하나도 없었다.
    /// </summary>
    [Fact, Trait(Kinds.Key, Kinds.Record)]
    public async Task 기록_일집_TAR_목록에는_빈폴더만_폴더항목이다()
    {
        using var tmp = new TempDir("rttr");
        string src = SampleTree.Create(tmp.Dir("src"));
        string arc = tmp.P("왕복.tar");
        var svc = Svc.For(arc);
        Assert.IsType<TarArchiveService>(svc);
        await svc.CompressAsync(new[] { src }, arc);
        TreeAssert.ListEquals(await svc.ListEntriesAsync(arc), SampleTree.FilesAndEmptyDirs(SampleTree.ExpectedOf(src)));
    }

    /// <summary>교차 확인: 일집 TAR 를 .NET 표준 System.Formats.Tar 로 읽으면 한글 이름(UTF-8)·내용이 맞다.</summary>
    [Fact]
    public async Task 일집_TAR를_표준_라이브러리로_열어도_같다()
    {
        using var tmp = new TempDir("xt");
        string src = SampleTree.Create(tmp.Dir("src"));
        var expected = SampleTree.ExpectedOf(src).Where(e => !e.IsDir).ToDictionary(e => e.Path, e => e.Sha256);
        string arc = tmp.P("교차.tar");
        await new TarArchiveService().CompressAsync(new[] { src }, arc);

        await using var fs = File.OpenRead(arc);
        using var reader = new TarReader(fs);
        int n = 0;
        while (reader.GetNextEntry() is { } e)
        {
            if (e.EntryType is TarEntryType.Directory) continue;
            Assert.True(expected.TryGetValue(e.Name, out var sha), "표준 TAR 에서 이름이 다름: " + e.Name);
            using var ms = new MemoryStream();
            e.DataStream?.CopyTo(ms);
            Assert.Equal(sha, TreeAssert.Sha256(ms.ToArray()));
            n++;
        }
        Assert.Equal(expected.Count, n);
    }

    /// <summary>BZ2 단일 파일 왕복: 이름은 '압축파일명 - .bz2', 내용 같음. 표준 SharpCompress BZip2Stream 으로 직접 풀어도 같다.</summary>
    [Fact]
    public async Task BZ2_단일파일_왕복이_원본과_같다()
    {
        using var tmp = new TempDir("rtb");
        string src = SampleTree.Create(tmp.Dir("src"));
        string one = Path.Combine(src, "한글 파일.txt");
        string arc = tmp.P("한글 파일.txt.bz2");
        var svc = Svc.For(arc);
        await svc.CompressAsync(new[] { one }, arc);

        Assert.Equal("한글 파일.txt", Assert.Single(await svc.ListEntriesAsync(arc)).Path);
        string dest = tmp.P("out");
        await svc.ExtractAsync(arc, dest);
        Assert.Equal(TreeAssert.Sha256(one), TreeAssert.Sha256(Path.Combine(dest, "한글 파일.txt")));
        Assert.Single(TreeAssert.Files(dest));

        await using var fs = File.OpenRead(arc);
        using var bz = SharpCompress.Compressors.BZip2.BZip2Stream.Create(fs, SharpCompress.Compressors.CompressionMode.Decompress, true);
        using var ms = new MemoryStream();
        bz.CopyTo(ms);
        Assert.Equal(TreeAssert.Sha256(one), TreeAssert.Sha256(ms.ToArray()));
    }

    /// <summary>교차 확인: 일집이 만든 .gz 자체는 정상 gzip 이다(.NET GZipStream 으로 풀면 원본과 같음).</summary>
    [Fact]
    public async Task 일집_GZ는_표준_gzip으로_풀면_원본과_같다()
    {
        using var tmp = new TempDir("xg");
        string src = SampleTree.Create(tmp.Dir("src"));
        string one = Path.Combine(src, "하위", "바이너리.bin");
        string arc = tmp.P("바이너리.bin.gz");
        await new GZipArchiveService().CompressAsync(new[] { one }, arc);

        await using var fs = File.OpenRead(arc);
        using var gz = new GZipStream(fs, CompressionMode.Decompress);
        using var ms = new MemoryStream();
        gz.CopyTo(ms);
        Assert.Equal(TreeAssert.Sha256(one), TreeAssert.Sha256(ms.ToArray()));
    }

    /// <summary>
    /// 현재 동작 기록: 일집 .gz 는 gzip 헤더의 FLG 바이트에 원본 이름(FNAME, 0x08)을 넣지 않는다 — 반디집과 같은 형식.
    /// 풀 때는 이름을 압축 파일 이름에서 정한다(B2 고침, GZ_단일파일_왕복이_원본과_같다).
    /// </summary>
    [Fact, Trait(Kinds.Key, Kinds.Record)]
    public async Task 기록_일집_GZ는_FNAME을_넣지_않는다()
    {
        using var tmp = new TempDir("xgr");
        string one = tmp.P("한 파일.txt");
        File.WriteAllText(one, "fname 기록");
        string arc = tmp.P("한 파일.txt.gz");
        await new GZipArchiveService().CompressAsync(new[] { one }, arc);
        Assert.Equal(0, File.ReadAllBytes(arc)[3] & 0x08);
    }

    /// <summary>[B2 고침] GZ 단일 파일 왕복 — 일집이 만든 .gz 를 일집이 다시 풀 수 있어야 한다.</summary>
    [Fact]
    public async Task GZ_단일파일_왕복이_원본과_같다()
    {
        using var tmp = new TempDir("rtg");
        string src = SampleTree.Create(tmp.Dir("src"));
        string one = Path.Combine(src, "한글 파일.txt");
        string arc = tmp.P("한글 파일.txt.gz");
        var svc = Svc.For(arc);
        await svc.CompressAsync(new[] { one }, arc);
        Assert.Equal("한글 파일.txt", Assert.Single(await svc.ListEntriesAsync(arc)).Path);
        string dest = tmp.P("out");
        await svc.ExtractAsync(arc, dest);
        Assert.Equal(TreeAssert.Sha256(one), TreeAssert.Sha256(Path.Combine(dest, "한글 파일.txt")));
    }

    /// <summary>원본 이름(FNAME)이 '있는' gzip(리눅스 gzip 방식)은 지금도 이름·내용이 맞게 풀린다.</summary>
    [Fact]
    public async Task FNAME_있는_gzip은_이름대로_풀린다()
    {
        using var tmp = new TempDir("gzn");
        byte[] data = Encoding.UTF8.GetBytes(string.Concat(Enumerable.Repeat("gzip with FNAME\n", 500)));
        string arc = tmp.P("named.gz");
        RawGz.Write(arc, Encoding.ASCII.GetBytes("hello.txt"), data);
        var svc = Svc.For(arc);
        var only = Assert.Single(await svc.ListEntriesAsync(arc));
        Assert.Equal("hello.txt", only.Path);
        Assert.Equal(data.Length, only.Size);
        string dest = tmp.P("out");
        await svc.ExtractAsync(arc, dest);
        Assert.Equal(TreeAssert.Sha256(data), TreeAssert.Sha256(Path.Combine(dest, "hello.txt")));
    }

    /// <summary>여러 원본(파일 2개 + 폴더)을 한 ZIP 에: 파일은 루트에 이름 그대로, 폴더는 폴더 이름이 최상위가 된다(반디집 기본과 같음).</summary>
    [Fact]
    public async Task ZIP_여러_원본은_각자_이름으로_루트에_들어간다()
    {
        using var tmp = new TempDir("multi");
        string srcParent = tmp.Dir("src");
        string folder = SampleTree.Create(srcParent);
        string f1 = Path.Combine(srcParent, "첫째.txt");
        string f2 = Path.Combine(srcParent, "둘째 파일.csv");
        File.WriteAllText(f1, "1");
        File.WriteAllText(f2, "2");
        string arc = tmp.P("multi.zip");
        var svc = new ZipArchiveService();
        await svc.CompressAsync(new[] { f1, f2, folder }, arc);
        var names = (await svc.ListEntriesAsync(arc)).Select(e => e.Path).ToList();
        Assert.Contains("첫째.txt", names);
        Assert.Contains("둘째 파일.csv", names);
        Assert.Contains($"{SampleTree.RootName}/한글 파일.txt", names);
        Assert.DoesNotContain(names, n => n.StartsWith("src/", StringComparison.Ordinal));
    }

    /// <summary>
    /// 현재 동작 기록: 단일 파일 형식·해제 전용 형식의 '못 하는 압축'은 정해진 예외로 거절되고 압축 파일이 생기지 않는다.
    /// (7z 생성이 생기면(I2-1) '.7z' 행은 지울 것)
    /// </summary>
    [Theory, Trait(Kinds.Key, Kinds.Record)]
    [InlineData(".gz", "folder", typeof(InvalidOperationException), "단일 파일만")]
    [InlineData(".gz", "two", typeof(InvalidOperationException), "파일 하나만")]
    [InlineData(".gz", "password", typeof(NotSupportedException), "암호화를 지원하지")]
    [InlineData(".gz", "none", typeof(InvalidOperationException), "압축할 파일이 없습니다")]
    [InlineData(".bz2", "folder", typeof(InvalidOperationException), "단일 파일만")]
    [InlineData(".bz2", "two", typeof(InvalidOperationException), "파일 하나만")]
    [InlineData(".bz2", "password", typeof(NotSupportedException), "암호화를 지원하지")]
    [InlineData(".tar", "password", typeof(NotSupportedException), "암호화를 지원하지")]
    [InlineData(".7z", "one", typeof(NotSupportedException), "7Z 압축 생성은")]
    [InlineData(".rar", "one", typeof(NotSupportedException), "RAR 압축은")]
    [InlineData(".egg", "one", typeof(NotSupportedException), "EGG/ALZ 압축")]
    [InlineData(".alz", "one", typeof(NotSupportedException), "EGG/ALZ 압축")]
    public async Task 못하는_압축은_정해진_예외로_거절된다(string ext, string kind, Type exType, string msgPart)
    {
        using var tmp = new TempDir("rej");
        string src = SampleTree.Create(tmp.Dir("src"));
        string one = Path.Combine(src, "English.TXT");
        string arc = tmp.P("out" + ext);
        var svc = Svc.For(arc);
        (string[] sources, CompressionOptions? opt) = kind switch
        {
            "folder" => (new[] { src }, (CompressionOptions?)null),
            "two" => (new[] { one, one }, null),
            "password" => (new[] { one }, new CompressionOptions { Password = "x" }),
            "none" => (Array.Empty<string>(), null),
            _ => (new[] { one }, null)
        };
        var ex = await Svc.Catch(() => svc.CompressAsync(sources, arc, opt));
        Assert.NotNull(ex);
        Assert.IsType(exType, ex);
        Assert.Contains(msgPart, ex!.Message);
        Assert.False(File.Exists(arc) && new FileInfo(arc).Length > 0, "거절했는데 압축 파일이 만들어짐");
    }

    private static void AssertMtimes(string dest, DateTime expected)
    {
        foreach (var f in Directory.EnumerateFiles(dest, "*", SearchOption.AllDirectories))
        {
            var diff = (File.GetLastWriteTime(f) - expected).Duration();
            Assert.True(diff <= TimeSpan.FromSeconds(2), $"수정 시각 다름: {Path.GetRelativePath(dest, f)} = {File.GetLastWriteTime(f):yyyy-MM-dd HH:mm:ss}");
        }
    }
}
