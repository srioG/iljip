using Iljip.Models;
using Iljip.Services;
using Iljip.Tests.TestSupport;
using SharpSevenZip;

namespace Iljip.Tests.Contract;

/// <summary>
/// [W2-02, I2-1] 7z 만들기(7z.dll): 수준·솔리드·암호(AES-256)·파일 이름 암호화·빈 폴더·원본 건너뛰기·임시 파일.
/// 만든 7z 는 일집으로 다시 풀어 원본과 비교하고, 일부는 다른 엔진(SharpCompress)으로도 읽어 '다른 프로그램에서도 열리는지'를 본다
/// (반디집은 BandizipCliTests). 1차 W1-B 보관분(<c>_보류_W2-02/SevenZipCreateTests.cs.txt</c>)을 되살려 W2-02 설계에 맞춘 것.
/// 기본 왕복은 KnownGapTests.압축_7z_왕복이_원본과_같다, 큰 원본(160MB)의 진행률·취소·메모리는 SevenZipCreateLargeTests(따로 실행).
/// </summary>
public class SevenZipCreateTests
{
    private static async Task<(TempDir Tmp, string Src, string Arc)> Make(CompressionOptions options, string tag)
    {
        var tmp = new TempDir(tag);
        string src = SampleTree.Create(tmp.Dir("src"));
        string arc = tmp.P("만든.7z");
        await new SevenZipArchiveService().CompressAsync(new[] { src }, arc, options);
        return (tmp, src, arc);
    }

    /// <summary>대상 폴더에 일집 7z 임시 파일(.~이름.8글자.tmp)·덧붙이기 임시 폴더(.일집-임시-…)가 남지 않았는지.</summary>
    internal static void AssertNoLeftovers(string folder)
    {
        var left = Directory.EnumerateFileSystemEntries(folder)
            .Select(Path.GetFileName)
            .Where(n => n!.StartsWith(SevenZipArchiveService.TempPrefix, StringComparison.Ordinal) || n.StartsWith(StagingFolder.Prefix, StringComparison.Ordinal))
            .ToList();
        Assert.True(left.Count == 0, "임시 파일·폴더가 남음: " + string.Join(", ", left));
    }

    /// <summary>솔리드를 켜든 끄든 왕복이 원본과 같고, 7z.dll·다른 엔진(SharpCompress)이 보는 솔리드 여부가 선택과 같다.</summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task 솔리드_켜고_끄기_모두_왕복된다(bool solid)
    {
        var (tmp, src, arc) = await Make(new CompressionOptions { Solid = solid }, "7zs");
        using (tmp)
        {
            var svc = Svc.For(arc);
            var expected = SampleTree.ExpectedOf(src);
            TreeAssert.ListEquals(await svc.ListEntriesAsync(arc), expected);
            string dest = tmp.P("out");
            await svc.ExtractAsync(arc, dest);
            TreeAssert.ExtractedTreeEquals(dest, expected);

            using (var x = new SharpSevenZipExtractor(arc))
                Assert.Equal(solid, x.IsSolid);
            using var other = SharpCompress.Archives.SevenZip.SevenZipArchive.OpenArchive(arc, new SharpCompress.Readers.ReaderOptions());
            Assert.Equal(solid, other.IsSolid);
            AssertNoLeftovers(tmp.Root);
        }
    }

    /// <summary>
    /// 암호 + 파일 이름 암호화: 머리가 암호화되고, 암호 없이는 목록도 안 보이며(<see cref="ListPasswordRequiredException"/> — I1-7),
    /// 틀린 암호는 '암호 틀림', 맞는 암호면 왕복(빈 폴더 덧붙이기 뒤에도 이름 암호 그대로).
    /// </summary>
    [Theory]
    [InlineData(FixtureCatalog.DefaultPassword)]
    [InlineData(FixtureCatalog.KoreanPassword)]
    public async Task 파일이름까지_암호화하면_암호없이는_목록도_안_보인다(string password)
    {
        var (tmp, src, arc) = await Make(new CompressionOptions { Password = password, EncryptFileNames = true }, "7ze");
        using (tmp)
        {
            Assert.True(ArchiveHeaderProbe.IsSevenZipHeaderEncrypted(arc), "머리(파일 이름)가 암호화되지 않음");
            var svc = Svc.For(arc);

            var none = await Svc.Catch(() => svc.ListEntriesAsync(arc));
            var required = Assert.IsType<ListPasswordRequiredException>(none);
            Assert.True(required.IsHeaderEncrypted);
            Assert.Equal(ListPasswordRequiredException.ListMessage, required.Message);

            var wrong = await Svc.Catch(() => svc.ListEntriesAsync(arc, "틀린암호"));
            Assert.True(Assert.IsType<WrongPasswordException>(wrong).IsHeaderEncrypted);

            var expected = SampleTree.ExpectedOf(src);
            TreeAssert.ListEquals(await svc.ListEntriesAsync(arc, password), expected);
            string bad = tmp.P("bad");
            Assert.IsType<WrongPasswordException>(await Svc.Catch(() => svc.ExtractAsync(arc, bad, "틀린암호")));
            TreeAssert.NoFiles(bad);
            string dest = tmp.P("out");
            await svc.ExtractAsync(arc, dest, password);
            TreeAssert.ExtractedTreeEquals(dest, expected);
        }
    }

    /// <summary>
    /// 한글 암호 7z 를 다른 엔진(SharpCompress)도 같은 암호로 연다 — 7z 는 암호를 UTF-16 으로 쓰는 규격이라 반디집·7-Zip 과도 맞는다
    /// (ZIP 의 B3 같은 '암호 바이트 인코딩' 문제가 없음 — 그래서 옵션 창이 7z 에는 한글 암호 경고를 내지 않는다).
    /// </summary>
    [Fact]
    public async Task 한글_암호_7z를_다른_엔진도_연다()
    {
        var (tmp, src, arc) = await Make(new CompressionOptions { Password = FixtureCatalog.KoreanPassword }, "7zk");
        using (tmp)
        {
            using var other = SharpCompress.Archives.SevenZip.SevenZipArchive.OpenArchive(arc,
                new SharpCompress.Readers.ReaderOptions { Password = FixtureCatalog.KoreanPassword });
            var want = SampleTree.ExpectedOf(src).Where(e => !e.IsDir).ToDictionary(e => e.Path, e => e.Sha256);
            int n = 0;
            foreach (var e in other.Entries.Where(e => !e.IsDirectory))
            {
                using var s = e.OpenEntryStream();
                using var ms = new MemoryStream();
                s.CopyTo(ms);
                Assert.Equal(want[e.Key!.Replace('\\', '/')], TreeAssert.Sha256(ms.ToArray()));
                n++;
            }
            Assert.Equal(want.Count, n);
        }
    }

    /// <summary>암호만(이름은 보임): 암호 없이 목록은 보이고, 풀기는 암호가 필요하며(목록 잠김이 아닌 그냥 '암호 필요') 파일을 남기지 않는다.</summary>
    [Fact]
    public async Task 암호만_걸면_목록은_보이고_풀기는_암호가_필요하다()
    {
        var (tmp, src, arc) = await Make(new CompressionOptions { Password = FixtureCatalog.DefaultPassword }, "7zp");
        using (tmp)
        {
            Assert.False(ArchiveHeaderProbe.IsSevenZipHeaderEncrypted(arc));
            var svc = Svc.For(arc);
            var expected = SampleTree.ExpectedOf(src);
            TreeAssert.ListEquals(await svc.ListEntriesAsync(arc), expected);
            string bad = tmp.P("bad");
            var ex = Assert.IsType<PasswordRequiredException>(await Svc.Catch(() => svc.ExtractAsync(arc, bad)));
            Assert.False(ex.IsHeaderEncrypted);
            TreeAssert.NoFiles(bad);
            string dest = tmp.P("out");
            await svc.ExtractAsync(arc, dest, FixtureCatalog.DefaultPassword);
            TreeAssert.ExtractedTreeEquals(dest, expected);
        }
    }

    /// <summary>'파일 이름도 암호화'는 암호가 없으면 아무 일도 하지 않는다(머리 암호화 없음).</summary>
    [Fact]
    public async Task 암호없이_파일이름_암호화는_무시된다()
    {
        var (tmp, _, arc) = await Make(new CompressionOptions { EncryptFileNames = true }, "7zn");
        using (tmp)
        {
            Assert.False(ArchiveHeaderProbe.IsSevenZipHeaderEncrypted(arc));
            Assert.NotEmpty(await Svc.For(arc).ListEntriesAsync(arc));
        }
    }

    /// <summary>
    /// 원본들이 서로 다른 폴더에 있어도 각자 이름(폴더는 그 폴더 이름부터)으로 담긴다(ZIP 과 같은 이름 규칙).
    /// 1차 W1-B 2차 검증 지적: 이때 빈 폴더가 아무 알림 없이 빠지던 것 — 파일을 담은 뒤 빈 폴더를 원본 위치(부모)별로 덧붙인다(파일 이름 암호화도 그대로).
    /// </summary>
    [Theory]
    [InlineData(null, false)]
    [InlineData("암호1234", true)]
    public async Task 서로_다른_폴더의_원본도_빈_폴더까지_묶는다(string? password, bool encryptNames)
    {
        using var tmp = new TempDir("7zm");
        string f1 = Path.Combine(tmp.Dir("a"), "첫째.txt");
        File.WriteAllText(f1, "하나");
        string f2 = Path.Combine(tmp.Dir("b", "c"), "둘째 파일.csv");
        File.WriteAllText(f2, "둘,셋");
        string folder = SampleTree.Create(tmp.Dir("d"));
        string lonely = tmp.Dir("e", "혼자 빈 폴더");
        string arc = tmp.P("섞음.7z");
        var options = new CompressionOptions { Password = password, EncryptFileNames = encryptNames };
        await new SevenZipArchiveService().CompressAsync(new[] { f1, f2, folder, lonely }, arc, options);

        if (encryptNames)
            Assert.IsType<ListPasswordRequiredException>(await Svc.Catch(() => Svc.For(arc).ListEntriesAsync(arc)));   // 덧붙인 뒤에도 이름 암호 그대로
        var entries = await Svc.For(arc).ListEntriesAsync(arc, password);
        var names = entries.Where(e => !e.IsDirectory).Select(e => e.Path).ToList();
        Assert.Contains("첫째.txt", names);
        Assert.Contains("둘째 파일.csv", names);
        Assert.Contains($"{SampleTree.RootName}/한글 파일.txt", names);
        Assert.Contains(entries, e => e.IsDirectory && e.Path == $"{SampleTree.RootName}/빈 폴더");
        Assert.Contains(entries, e => e.IsDirectory && e.Path == "혼자 빈 폴더");
        string dest = tmp.P("out");
        await Svc.For(arc).ExtractAsync(arc, dest, password);
        Assert.Equal(TreeAssert.Sha256(f2), TreeAssert.Sha256(Path.Combine(dest, "둘째 파일.csv")));
        Assert.Equal(TreeAssert.Sha256(Path.Combine(folder, "하위", "바이너리.bin")),
            TreeAssert.Sha256(Path.Combine(dest, SampleTree.RootName, "하위", "바이너리.bin")));
        Assert.True(Directory.Exists(Path.Combine(dest, SampleTree.RootName, "빈 폴더")), "빈 폴더가 빠짐");
        Assert.True(Directory.Exists(Path.Combine(dest, "혼자 빈 폴더")), "따로 고른 빈 폴더가 빠짐");
        AssertNoLeftovers(tmp.Root);
    }

    /// <summary>서로 다른 폴더의 빈 폴더만 골라도 7z 가 만들어지고 모두 들어간다(첫 묶음으로 만들고 나머지를 덧붙임 — 파일 이름 암호화도).</summary>
    [Theory]
    [InlineData(null, false)]
    [InlineData(FixtureCatalog.KoreanPassword, true)]
    public async Task 서로_다른_폴더의_빈_폴더만_골라도_묶는다(string? password, bool encryptNames)
    {
        using var tmp = new TempDir("7ze");
        string d1 = tmp.Dir("a", "빈A");
        string d2 = tmp.Dir("b", "빈B");
        string d3 = tmp.Dir("a", "빈C");   // d1 과 같은 부모(같은 묶음)
        string arc = tmp.P("빈것.7z");
        await new SevenZipArchiveService().CompressAsync(new[] { d1, d2, d3 }, arc, new CompressionOptions { Password = password, EncryptFileNames = encryptNames });
        Assert.Equal(encryptNames, ArchiveHeaderProbe.IsSevenZipHeaderEncrypted(arc));
        if (encryptNames)
            Assert.IsType<ListPasswordRequiredException>(await Svc.Catch(() => Svc.For(arc).ListEntriesAsync(arc)));
        var dirs = (await Svc.For(arc).ListEntriesAsync(arc, password)).Where(e => e.IsDirectory).Select(e => e.Path).OrderBy(x => x, StringComparer.Ordinal);
        Assert.Equal(new[] { "빈A", "빈B", "빈C" }, dirs);
        string dest = tmp.P("out");
        await Svc.For(arc).ExtractAsync(arc, dest, password);
        Assert.True(Directory.Exists(Path.Combine(dest, "빈B")));
        AssertNoLeftovers(tmp.Root);
    }

    /// <summary>
    /// 계약(1차 W1-B 2차 검증 지적): 원본 폴더 안에 같은 이름의 압축 파일을 덮어 저장해도 그 압축 파일 자신은 담지 않는다 —
    /// 예전 7z 는 출력을 만들며 비워진 자기 자신을 0바이트 항목('self/self.7z')으로 넣었다. ZIP·TAR 도 같은 규칙. 덮어쓴 결과가 남는다.
    /// </summary>
    [Theory]
    [InlineData(".7z")]
    [InlineData(".zip")]
    [InlineData(".tar")]
    public async Task 원본_폴더_안에_덮어_저장해도_자기_자신은_담지_않는다(string ext)
    {
        using var tmp = new TempDir("7zs");
        string folder = tmp.Dir("self");
        File.WriteAllText(Path.Combine(folder, "x.txt"), "내용");
        string arc = Path.Combine(folder, "self" + ext);
        File.WriteAllText(arc, "예전 압축 파일(덮어쓸 것)");
        var svc = Svc.Locator.ResolveByExtension(arc)!;
        await svc.CompressAsync(new[] { folder }, arc);
        var names = (await Svc.For(arc).ListEntriesAsync(arc)).Where(e => !e.IsDirectory).Select(e => e.Path).ToList();
        Assert.Equal(new[] { "self/x.txt" }, names);
        AssertNoLeftovers(folder);
    }

    /// <summary>
    /// 계약(W2-02): 7z 는 대상 옆 임시 파일에 쓰고 다 맞게 끝났을 때만 바꿔 넣는다 — 실패(같은 이름 겹침)면 덮어쓰려던 기존 파일이 바이트 그대로이고
    /// 임시 파일도 남지 않는다. 성공하면 기존 파일을 새 7z 로 바꾼다.
    /// </summary>
    [Fact]
    public async Task 실패하면_덮어쓰려던_파일이_그대로이고_성공하면_바뀐다()
    {
        using var tmp = new TempDir("7zow");
        string f1 = Path.Combine(tmp.Dir("a"), "같음.txt");
        string f2 = Path.Combine(tmp.Dir("b"), "같음.txt");
        File.WriteAllText(f1, "1");
        File.WriteAllText(f2, "2");
        string arc = tmp.P("결과.7z");
        var old = new byte[] { 1, 2, 3, 4, 5, 6, 7, 8, 9 };
        File.WriteAllBytes(arc, old);

        var ex = await Svc.Catch(() => new SevenZipArchiveService().CompressAsync(new[] { f1, f2 }, arc));
        Assert.IsType<InvalidOperationException>(ex);
        Assert.Equal(old, File.ReadAllBytes(arc));
        AssertNoLeftovers(tmp.Root);

        await new SevenZipArchiveService().CompressAsync(new[] { f1 }, arc);
        Assert.Equal(new[] { "같음.txt" }, (await Svc.For(arc).ListEntriesAsync(arc)).Select(e => e.Path).ToArray());
        AssertNoLeftovers(tmp.Root);
    }

    /// <summary>
    /// 파일만 고르면(폴더 없음) 폴더 항목 없이 이름표 방식으로 바로 쓴다 — 암호·파일 이름 암호화·한글 이름이 그대로 왕복되고 덧붙이기 임시 폴더를 쓰지 않는다.
    /// (폴더가 있으면 폴더만 든 7z 를 먼저 만들고 파일을 덧붙인다 — 나머지 시험들)
    /// </summary>
    [Theory]
    [InlineData(null, false)]
    [InlineData(FixtureCatalog.KoreanPassword, false)]
    [InlineData(FixtureCatalog.KoreanPassword, true)]
    public async Task 파일만_고르면_폴더_항목_없이_바로_쓴다(string? password, bool encryptNames)
    {
        using var tmp = new TempDir("7zf");
        string src = SampleTree.Create(tmp.Dir("src"));
        var files = new[] { "한글 파일.txt", "빈파일.txt", "기호 #&+~!'@ 파일.txt" }.Select(n => Path.Combine(src, n)).ToArray();
        string arc = tmp.P("파일만.7z");
        await new SevenZipArchiveService().CompressAsync(files, arc, new CompressionOptions { Password = password, EncryptFileNames = encryptNames });

        Assert.Equal(encryptNames, ArchiveHeaderProbe.IsSevenZipHeaderEncrypted(arc));
        if (encryptNames)
            Assert.IsType<ListPasswordRequiredException>(await Svc.Catch(() => Svc.For(arc).ListEntriesAsync(arc)));
        var entries = await Svc.For(arc).ListEntriesAsync(arc, password);
        Assert.Equal(files.Select(Path.GetFileName).OrderBy(x => x, StringComparer.Ordinal), entries.Select(e => e.Path).OrderBy(x => x, StringComparer.Ordinal));
        Assert.DoesNotContain(entries, e => e.IsDirectory);
        string dest = tmp.P("out");
        await Svc.For(arc).ExtractAsync(arc, dest, password);
        foreach (var f in files)
            Assert.Equal(TreeAssert.Sha256(f), TreeAssert.Sha256(Path.Combine(dest, Path.GetFileName(f))));
        AssertNoLeftovers(tmp.Root);
    }

    /// <summary>
    /// 폴더 항목: 7-Zip·반디집 7z 처럼 파일이 든 폴더도 항목으로 들어가고(중간 폴더 포함) 폴더의 수정 시각이 원본과 같다.
    /// 서로 다른 곳의 같은 이름 폴더 두 개를 고르면 7z 안에서 폴더 이름이 겹쳐 쉬운 말로 거절한다(7-Zip 도 'Duplicate filename').
    /// </summary>
    [Fact]
    public async Task 폴더도_항목으로_들어가고_같은_이름_폴더_둘은_알린다()
    {
        using var tmp = new TempDir("7zdir");
        string src = SampleTree.Create(tmp.Dir("src"));
        var stamp = new DateTime(2024, 5, 6, 7, 8, 10, DateTimeKind.Local);
        Directory.SetLastWriteTime(Path.Combine(src, "하위", "깊이2"), stamp);
        string arc = tmp.P("폴더.7z");
        await new SevenZipArchiveService().CompressAsync(new[] { src }, arc);
        var entries = await Svc.For(arc).ListEntriesAsync(arc);
        var dirs = entries.Where(e => e.IsDirectory).Select(e => e.Path).ToHashSet();
        foreach (var d in SampleTree.ExpectedOf(src).Where(e => e.IsDir))
            Assert.Contains(d.Path, dirs);
        var deep = Assert.Single(entries, e => e.IsDirectory && e.Path == $"{SampleTree.RootName}/하위/깊이2");
        var mtime = deep.LastModified!.Value;
        if (mtime.Kind == DateTimeKind.Utc) mtime = mtime.ToLocalTime();
        Assert.Equal(stamp, mtime);   // 틱만 비교(Kind 무시)

        string a = tmp.Dir("a", "자료");
        string b = tmp.Dir("b", "자료");
        File.WriteAllText(Path.Combine(a, "하나.txt"), "1");
        File.WriteAllText(Path.Combine(b, "둘.txt"), "2");
        string dup = tmp.P("겹침.7z");
        var ex = await Svc.Catch(() => new SevenZipArchiveService().CompressAsync(new[] { a, b }, dup));
        Assert.Contains("같은 이름", Assert.IsType<InvalidOperationException>(ex).Message);
        Assert.False(File.Exists(dup));
        AssertNoLeftovers(tmp.Root);
    }

    /// <summary>서로 다른 폴더에서 같은 이름의 파일을 고르면 7z 안에서 겹치므로 쉬운 말로 거절한다(압축 파일을 만들지 않음).</summary>
    [Fact]
    public async Task 같은_이름_원본이_여러_폴더에_있으면_알린다()
    {
        using var tmp = new TempDir("7zd");
        string f1 = Path.Combine(tmp.Dir("a"), "같음.txt");
        string f2 = Path.Combine(tmp.Dir("b"), "같음.txt");
        File.WriteAllText(f1, "1");
        File.WriteAllText(f2, "2");
        string arc = tmp.P("겹침.7z");
        var ex = await Svc.Catch(() => new SevenZipArchiveService().CompressAsync(new[] { f1, f2 }, arc));
        Assert.IsType<InvalidOperationException>(ex);
        Assert.Contains("같은 이름", ex!.Message);
        Assert.False(File.Exists(arc));
        AssertNoLeftovers(tmp.Root);
    }

    /// <summary>수준: '저장만'은 압축하지 않고(파일 합보다 큼), '빠름'·'보통'·'최대'는 그보다 작고 모두 왕복된다.</summary>
    [Fact]
    public async Task 저장은_압축하지_않고_나머지는_작다()
    {
        var (tmpStore, src, store) = await Make(new CompressionOptions { Level = CompressionLevelOption.Store }, "7zl0");
        using (tmpStore)
        {
            long total = Directory.EnumerateFiles(src, "*", SearchOption.AllDirectories).Sum(f => new FileInfo(f).Length);
            Assert.True(new FileInfo(store).Length >= total, "저장 수준인데 원본 합보다 작음(압축됨)");
            var expected = SampleTree.ExpectedOf(src);
            foreach (var level in new[] { CompressionLevelOption.Fastest, CompressionLevelOption.Normal, CompressionLevelOption.Maximum })
            {
                string arc = tmpStore.P($"{level}.7z");
                await new SevenZipArchiveService().CompressAsync(new[] { src }, arc, new CompressionOptions { Level = level });
                Assert.True(new FileInfo(arc).Length < new FileInfo(store).Length, $"{level} 이 저장만보다 작지 않음");
                string dest = tmpStore.P("out_" + level);
                await Svc.For(arc).ExtractAsync(arc, dest);
                TreeAssert.ExtractedTreeEquals(dest, expected, context: level.ToString());
            }
        }
    }

    /// <summary>
    /// 현재 동작 기록: 수준 → 7z.dll 압축 수준(저장만=None·빠름=Fast·보통=Normal·최대=Ultra)과 추가 설정(CustomParameters).
    /// 솔리드(s)는 늘, 스레드 mt=min(4, 논리 CPU)(최대는 2), 사전 d·LZMA2 블록 c 는 빠름 4m/4m·보통 16m/8m·최대 32m/16m, 저장만은 s 만.
    /// 7-Zip 기본(블록 수백MB·스레드 전부)이면 진행률이 몇 초 만에 100%·취소가 끝까지 안 먹음·메모리 1.2~3.4GB(1차 W1-B 검증 지적) — 값을 바꾸면
    /// SevenZipCreateLargeTests(메모리 700MB·취소 3초)를 다시 돌려 볼 것.
    /// </summary>
    [Fact, Trait(Kinds.Key, Kinds.Record)]
    public void 기록_수준별_7z_dll_설정()
    {
        string mt = Math.Clamp(Environment.ProcessorCount, 1, 4).ToString(System.Globalization.CultureInfo.InvariantCulture);
        (string, string)[] Params(CompressionLevelOption level, bool solid = true) =>
            SevenZipArchiveService.EngineParameters(new CompressionOptions { Level = level, Solid = solid }).ToArray();

        Assert.Equal(new[] { ("s", "on") }, Params(CompressionLevelOption.Store));
        Assert.Equal(new[] { ("s", "on"), ("mt", mt), ("d", "4m"), ("c", "4m") }, Params(CompressionLevelOption.Fastest));
        Assert.Equal(new[] { ("s", "on"), ("mt", mt), ("d", "16m"), ("c", "8m") }, Params(CompressionLevelOption.Normal));
        Assert.Equal(new[] { ("s", "on"), ("mt", "2"), ("d", "32m"), ("c", "16m") }, Params(CompressionLevelOption.Maximum));
        Assert.Equal(new[] { ("s", "off"), ("mt", mt), ("d", "16m"), ("c", "8m") }, Params(CompressionLevelOption.Normal, solid: false));

        Assert.Equal(CompressionLevel.None, SevenZipArchiveService.ToSevenZipLevel(CompressionLevelOption.Store));
        Assert.Equal(CompressionLevel.Fast, SevenZipArchiveService.ToSevenZipLevel(CompressionLevelOption.Fastest));
        Assert.Equal(CompressionLevel.Normal, SevenZipArchiveService.ToSevenZipLevel(CompressionLevelOption.Normal));
        Assert.Equal(CompressionLevel.Ultra, SevenZipArchiveService.ToSevenZipLevel(CompressionLevelOption.Maximum));
        Assert.Equal(CompressionLevelOption.Normal, CompressionOptions.Default.Level);
        Assert.True(CompressionOptions.Default.Solid, "솔리드 기본값이 꺼짐");
        Assert.False(CompressionOptions.Default.EncryptFileNames);
    }

    /// <summary>
    /// 계약(W2-02): 7z 압축 도중(큰 파일 한가운데, 진행률 8 MiB 지점) 취소 → OCE, 대상 파일도 임시 파일도 남지 않는다
    /// (반쪽 파일 없음 — 1차 W1-B 는 쓰다 만 7z 를 남기고 지우기를 화면에 맡겼다). 큰 원본의 시간 맞춘 취소는 SevenZipCreateLargeTests.
    /// </summary>
    [Fact]
    public async Task 압축_도중_취소하면_OCE_그리고_아무것도_남지_않는다()
    {
        using var tmp = new TempDir("7zc");
        string src = CancellationTests.MakeSource(tmp);
        string arc = tmp.P("big.7z");
        using var cts = new CancellationTokenSource();
        var progress = new ProgressRecorder(p => { if (p.BytesProcessed > 8L * 1024 * 1024) cts.Cancel(); });
        var ex = await Svc.Catch(() => Svc.For(arc).CompressAsync(new[] { src }, arc,
            new CompressionOptions { Level = CompressionLevelOption.Store }, progress, cts.Token));
        Assert.IsAssignableFrom<OperationCanceledException>(ex);
        Assert.False(File.Exists(arc), "취소했는데 7z 파일이 남음");
        AssertNoLeftovers(tmp.Root);
        Assert.True(progress.Reports.Max(r => r.BytesProcessed) < CancellationTests.BigSize, "취소가 큰 파일을 다 읽은 뒤에야 먹힘");
    }

    /// <summary>
    /// 계약(W2-02, I0-2 와 같은 규칙): 7z.dll 이 읽는 도중 원본 크기가 바뀌면(다른 프로그램이 덧붙여 씀) 그 파일을 빼고 처음부터 다시 써서
    /// 압축은 끝나고, 그 파일은 '압축하는 동안 파일이 바뀌었어요'로 건너뜀에 적힌다(반쪽 항목이 들어가지 않음).
    /// </summary>
    [Fact]
    public async Task 읽는_도중_크기가_바뀐_파일은_빼고_다시_쓴다()
    {
        using var tmp = new TempDir("7zchg");
        string src = CancellationTests.MakeSource(tmp);
        string big = Path.Combine(src, "z_큰.bin");
        string small = Path.Combine(src, "a_작은.txt");
        string arc = tmp.P("바뀜.7z");
        int grown = 0;
        var progress = new ProgressRecorder(p =>
        {
            if (p.CurrentFile.EndsWith("z_큰.bin", StringComparison.Ordinal) && p.BytesProcessed > 8L * 1024 * 1024 && Interlocked.Exchange(ref grown, 1) == 0)
            {
                using var w = new FileStream(big, FileMode.Append, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete);
                w.Write(new byte[] { 1, 2, 3 });
            }
        });
        var result = await Svc.For(arc).CompressAsync(new[] { src }, arc, new CompressionOptions { Level = CompressionLevelOption.Store }, progress);

        Assert.Equal(1, grown);
        var skipped = Assert.Single(result.Skipped);
        Assert.Equal(big, skipped.Path);
        Assert.Equal(SourceFileOpener.ReasonChanged, skipped.Reason);
        string dest = tmp.P("out");
        await Svc.For(arc).ExtractAsync(arc, dest);
        Assert.Equal(new[] { "취소 원본/a_작은.txt" }, TreeAssert.Files(dest));
        Assert.Equal(TreeAssert.Sha256(small), TreeAssert.Sha256(Path.Combine(dest, "취소 원본", "a_작은.txt")));
        ProgressTests.AssertWellFormed(progress.Reports, "다시 쓴 7z");
        AssertNoLeftovers(tmp.Root);
    }

    /// <summary>
    /// 계약(1차 W1-B 검증 지적, W2-02 에서 I0-2 규칙으로 바뀜): 원본 하나를 다른 프로그램이 잠가 두면 7z·ZIP 모두 그 파일만 건너뛰고(이유 '다른 프로그램이 쓰고 있어요'),
    /// 잠긴 파일만 골랐으면 압축 파일을 만들지 않고 그 파일 이름이 든 쉬운 안내로 끝난다 — 영어 'The process cannot access the file …' 이 나가지 않는다.
    /// (예전 보관분은 7z 만 잠긴 파일 하나로 압축 전체가 <see cref="SourceUnreadableException"/> 으로 실패했다)
    /// </summary>
    [Theory]
    [InlineData(".7z")]
    [InlineData(".zip")]
    public async Task 원본이_잠겨_있으면_그_파일_이름으로_알린다(string ext)
    {
        using var tmp = new TempDir("7zlock");
        string dir = tmp.Dir("잠김 원본");
        string locked = Path.Combine(dir, "견적서 (최종).txt");
        File.WriteAllText(locked, "잠긴 파일");
        File.WriteAllText(Path.Combine(dir, "다른 파일.txt"), "보통 파일");
        string arc = tmp.P("결과" + ext);
        string only = tmp.P("잠긴 것만" + ext);
        CompressResult result;
        Exception? nothing;
        using (new FileStream(locked, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            result = await Svc.Locator.ResolveByExtension(arc)!.CompressAsync(new[] { dir }, arc);
            nothing = await Svc.Catch(() => Svc.Locator.ResolveByExtension(only)!.CompressAsync(new[] { locked }, only));
        }
        var skipped = Assert.Single(result.Skipped);
        Assert.Equal(Path.GetFullPath(locked), Path.GetFullPath(skipped.Path), ignoreCase: true);
        Assert.Equal(SourceFileOpener.ReasonInUse, skipped.Reason);
        Assert.Equal(new[] { "잠김 원본/다른 파일.txt" }, (await Svc.For(arc).ListEntriesAsync(arc)).Where(e => !e.IsDirectory).Select(e => e.Path).ToArray());

        var none = Assert.IsType<NothingToCompressException>(nothing);
        string m = Iljip.ViewModels.MainViewModel.NothingCompressedMessage(none.Skipped);
        Assert.Contains("견적서 (최종).txt", m);
        Assert.Contains(SourceFileOpener.ReasonInUse, m);
        Assert.DoesNotContain("process", m, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("process", ErrorMessages.ToUserMessage(none), StringComparison.OrdinalIgnoreCase);
        Assert.False(File.Exists(only));
        AssertNoLeftovers(tmp.Root);
    }

    /// <summary>
    /// 계약(1차 W1-B 검증 지적): 파일 이름까지 암호화한 7z 의 데이터가 손상되면, 그 암호로 목록까지 열렸으니 암호는 맞다 → 풀 때
    /// '암호가 맞지 않아요'(암호 창 다시)가 아니라 손상으로 알린다. 예전엔 맞는 암호를 넣어도 '암호가 맞지 않아요'를 끝없이 되물었다.
    /// </summary>
    [Fact]
    public async Task 파일이름까지_암호화한_7z가_손상되면_맞는_암호에_손상으로_알린다()
    {
        var (tmp, _, arc) = await Make(new CompressionOptions { Password = FixtureCatalog.KoreanPassword, EncryptFileNames = true }, "7zeb");
        using (tmp)
        {
            string bad = SevenZipTestArchives.CorruptMiddle(arc, tmp.P("손상.7z"));
            var svc = Svc.For(bad);
            Assert.NotEmpty(await svc.ListEntriesAsync(bad, FixtureCatalog.KoreanPassword));   // 목록은 열린다(머리는 멀쩡)
            var ex = await Svc.Catch(() => svc.ExtractAsync(bad, tmp.P("out"), FixtureCatalog.KoreanPassword));
            Assert.IsType<CorruptArchiveException>(ex);
            Assert.False(Internals.IsPasswordRelated(ex!), "손상인데 화면이 암호를 다시 물음");
        }
    }

    /// <summary>
    /// 계약(1차 W1-B 검증 지적): 이름이 보이는 암호 7z — 손상돼도 같은 암호로 앞 파일이 확인값까지 맞게 풀렸으면 암호는 맞다 → 손상.
    /// 그냥 틀린 암호는 7z 에 암호 확인값이 없어 손상과 구분이 안 되므로 '손상일 수도' 덧말을 붙인다.
    /// </summary>
    [Fact]
    public async Task 이름이_보이는_암호_7z도_손상과_틀린_암호를_가려_알린다()
    {
        var (tmp, _, arc) = await Make(new CompressionOptions { Password = FixtureCatalog.DefaultPassword }, "7zpb");
        using (tmp)
        {
            var wrong = await Svc.Catch(() => Svc.For(arc).ExtractAsync(arc, tmp.P("wrong"), "틀린암호"));
            Assert.Equal(WrongPasswordException.DefaultMessage + WrongPasswordException.HeaderNote, Assert.IsType<WrongPasswordException>(wrong).Message);
            TreeAssert.NoFiles(tmp.P("wrong"));

            string bad = SevenZipTestArchives.CorruptMiddle(arc, tmp.P("손상.7z"));
            var ex = await Svc.Catch(() => Svc.For(bad).ExtractAsync(bad, tmp.P("out"), FixtureCatalog.DefaultPassword));
            Assert.IsType<CorruptArchiveException>(ex);
        }
    }

    /// <summary>원본이 하나도 없으면 다른 형식과 같은 문구로 거절한다(파일·임시 파일을 만들지 않음).</summary>
    [Fact]
    public async Task 원본이_없으면_거절한다()
    {
        using var tmp = new TempDir("7z0");
        var ex = await Svc.Catch(() => new SevenZipArchiveService().CompressAsync(Array.Empty<string>(), tmp.P("x.7z")));
        Assert.IsType<InvalidOperationException>(ex);
        Assert.Contains("압축할 파일이 없습니다", ex!.Message);
        Assert.False(File.Exists(tmp.P("x.7z")));
        AssertNoLeftovers(tmp.Root);
    }

    /// <summary>
    /// 계약(W2-02): 원본 스트림(<see cref="LazySourceStream"/>)은 처음 읽을 때 열고 끝까지 읽으면 닫는다(원본 핸들을 압축 내내 쥐지 않음).
    /// 모을 때 잰 길이와 다르면(줄었거나 늘었으면) 끝에서 '파일이 바뀌었어요'로 알리고, 취소 토큰을 읽을 때마다 본다.
    /// </summary>
    [Fact]
    public void 원본_스트림은_읽는_동안만_열고_길이가_바뀌면_알린다()
    {
        using var tmp = new TempDir("7zlazy");
        string f = tmp.P("원본.bin");
        File.WriteAllBytes(f, new byte[1000]);
        var buf = new byte[4096];

        int bytes = 0, finished = 0;
        using (var s = new LazySourceStream(f, 1000, CancellationToken.None, onBytes: n => bytes += n, onFinished: () => finished++))
        {
            Assert.False(s.IsOpen);
            Assert.Equal(1000, s.Length);
            Assert.Equal(1000, s.Read(buf, 0, buf.Length));
            Assert.True(s.IsOpen);
            Assert.Equal(0, s.Read(buf, 0, buf.Length));
            Assert.False(s.IsOpen);
            Assert.Equal(1000, bytes);
            Assert.Equal(1, finished);
        }

        SourceUnreadableException? reported = null;
        using (var s = new LazySourceStream(f, 900, CancellationToken.None, onFailure: e => reported = e))   // 모을 때보다 늘어난 파일
        {
            Assert.Equal(900, s.Read(buf, 0, buf.Length));
            var ex = Assert.Throws<SourceUnreadableException>(() => s.Read(buf, 0, buf.Length));
            Assert.Equal(SourceFileOpener.ReasonChanged, ex.Reason);
            Assert.Same(ex, reported);
            Assert.False(s.IsOpen);
        }

        using (var s = new LazySourceStream(f, 1200, CancellationToken.None))   // 줄어든 파일
        {
            Assert.Equal(1000, s.Read(buf, 0, buf.Length));
            Assert.Equal(SourceFileOpener.ReasonChanged, Assert.Throws<SourceUnreadableException>(() => s.Read(buf, 0, buf.Length)).Reason);
        }

        using var cts = new CancellationTokenSource();
        cts.Cancel();
        using (var s = new LazySourceStream(f, 1000, cts.Token))
            Assert.ThrowsAny<OperationCanceledException>(() => s.Read(buf, 0, buf.Length));

        using (var s = new LazySourceStream(tmp.P("없음.bin"), 10, CancellationToken.None))
            Assert.Equal(SourceFileOpener.ReasonMissing, Assert.Throws<SourceUnreadableException>(() => s.Read(buf, 0, buf.Length)).Reason);
    }
}
