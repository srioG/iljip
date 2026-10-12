using System.Security.AccessControl;
using System.Security.Principal;
using Iljip.Models;
using Iljip.Services;
using Iljip.Tests.TestSupport;
using Iljip.ViewModels;

namespace Iljip.Tests.Contract;

/// <summary>
/// [I0-2] 다른 프로그램이 열어 둔 파일이 있어도 압축 전체가 실패하지 않는다.
/// ZIP·TAR: 공유를 허락한 파일은 담고, 공유를 막은 파일·파일의 한 부분을 잠근 파일(열기는 되는데 읽기가 막힘)·못 들어가는 폴더만 건너뛰어
/// <see cref="CompressResult.Skipped"/> 에 적는다. 하나도 못 담으면 빈 압축을 만들지 않고 <see cref="NothingToCompressException"/>.
/// GZ·BZ2(단일 파일): 그 파일을 못 읽으면 만들 것이 없으므로 <see cref="SourceUnreadableException"/>, 압축 파일은 만들지 않는다.
/// 화면은 '건너뛴 파일 N개'를 알리고 결과 압축 파일은 지우지 않는다.
/// (7z 만들기는 W1-B(7z.dll)가 들어오면 같은 테스트에 '.7z' 행을 더할 것 — 7z.dll 은 잠긴 파일이 하나라도 있으면 통째로 실패해 미리 걸러야 함)
/// </summary>
public class CompressSkipTests
{
    private const string LockedName = "한글 파일.txt";

    private static string LockedPath(string src) => Path.Combine(src, LockedName);

    private static IEnumerable<ExpectedEntry> Without(IEnumerable<ExpectedEntry> expected, string relativePrefix) =>
        expected.Where(e => e.Path != relativePrefix && !e.Path.StartsWith(relativePrefix + "/", StringComparison.Ordinal));

    [Theory]
    [InlineData(".zip")]
    [InlineData(".tar")]
    public async Task 공유를_막은_파일_하나는_건너뛰고_나머지는_그대로_압축된다(string ext)
    {
        using var tmp = new TempDir("skip");
        string src = SampleTree.Create(tmp.Dir("src"));
        var expected = SampleTree.ExpectedOf(src);
        string arc = tmp.P("결과" + ext);
        var svc = Svc.For(arc);

        CompressResult result;
        using (new FileStream(LockedPath(src), FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            result = await svc.CompressAsync(new[] { src }, arc);

        var skipped = Assert.Single(result.Skipped);
        Assert.Equal(LockedPath(src), skipped.Path);
        Assert.Equal(SourceFileOpener.ReasonInUse, skipped.Reason);

        Assert.True(File.Exists(arc));
        string dest = tmp.P("out");
        await svc.ExtractAsync(arc, dest);
        TreeAssert.ExtractedTreeEquals(dest, Without(expected, $"{SampleTree.RootName}/{LockedName}"));
    }

    /// <summary>다른 프로그램이 쓰기로 열었지만 공유를 허락한 파일(아웃룩 PST·로그 등)은 담기고 내용이 같다(예전엔 압축 전체 실패).</summary>
    [Theory]
    [InlineData(".zip")]
    [InlineData(".tar")]
    public async Task 쓰기_공유로_열린_파일도_담긴다(string ext)
    {
        using var tmp = new TempDir("skipw");
        string src = SampleTree.Create(tmp.Dir("src"));
        var expected = SampleTree.ExpectedOf(src);
        string arc = tmp.P("결과" + ext);
        var svc = Svc.For(arc);

        CompressResult result;
        using (new FileStream(LockedPath(src), FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite))
            result = await svc.CompressAsync(new[] { src }, arc);

        Assert.Empty(result.Skipped);
        string dest = tmp.P("out");
        await svc.ExtractAsync(arc, dest);
        TreeAssert.ExtractedTreeEquals(dest, expected);
    }

    // ───────────── 열기는 되는데 읽기가 막힌 파일(바이트 범위 잠금 — W1-D 1차 검증) ─────────────

    /// <summary>
    /// 다른 프로그램이 공유는 허락하고 파일의 한 부분을 잠근 것처럼(LockFileEx — DB 파일 등): 쓰기 공유로 열어 [start, start+length) 를 잠근다.
    /// 잠금은 핸들마다 걸려 같은 프로세스의 다른 핸들 읽기도 막힌다.
    /// </summary>
    private static FileStream LockRange(string path, long start, long length)
    {
        var fs = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite);
        fs.Lock(start, length);
        return fs;
    }

    private const string DbName = "업무.db";
    private const int DbSize = 300_000;   // 열 때 읽어 보는 첫 블록(80KB)보다 크게

    private static string AddDb(string src)
    {
        string db = Path.Combine(src, DbName);
        var data = new byte[DbSize];
        new Random(7).NextBytes(data);
        File.WriteAllBytes(db, data);
        return db;
    }

    /// <summary>
    /// 파일 전체가 잠겨 열기만 되고 읽기는 막힌 파일: 압축 전체가 실패하지 않고 그 파일만 건너뛴다(예전엔 '압축 실패' + 결과 삭제).
    /// 열 때 첫 블록을 읽어 보아 항목 머리를 쓰기 전에 거른다.
    /// </summary>
    [Theory]
    [InlineData(".zip")]
    [InlineData(".tar")]
    public async Task 통째로_잠긴_파일은_건너뛰고_나머지는_그대로_압축된다(string ext)
    {
        using var tmp = new TempDir("skipl");
        string src = SampleTree.Create(tmp.Dir("src"));
        var expected = SampleTree.ExpectedOf(src);
        string db = AddDb(src);
        string arc = tmp.P("결과" + ext);
        var svc = Svc.For(arc);

        CompressResult result;
        using (LockRange(db, 0, DbSize))
            result = await svc.CompressAsync(new[] { src }, arc);

        var skipped = Assert.Single(result.Skipped);
        Assert.Equal(db, skipped.Path);
        Assert.Equal(SourceFileOpener.ReasonInUse, skipped.Reason);
        string dest = tmp.P("out");
        await svc.ExtractAsync(arc, dest);
        TreeAssert.ExtractedTreeEquals(dest, expected);   // 업무.db 만 빠지고 나머지는 같다
    }

    /// <summary>
    /// 뒤쪽 한 부분만 잠긴 파일(첫 블록은 읽힘 → 항목 머리를 쓴 뒤 읽는 도중에 막힘): 그 파일을 뺀 채 처음부터 다시 써서
    /// 압축은 끝나고, 그 파일은 '건너뜀'으로 적히며, 반쯤 쓴 항목이 남지 않는다. 원본 차례상 그 파일 앞·뒤의 파일 모두 담긴다.
    /// </summary>
    [Theory]
    [InlineData(".zip")]
    [InlineData(".tar")]
    public async Task 읽는_도중_막힌_파일은_빼고_다시_써서_나머지는_그대로_압축된다(string ext)
    {
        using var tmp = new TempDir("skipr");
        string src = SampleTree.Create(tmp.Dir("src"));
        var expected = SampleTree.ExpectedOf(src);
        string db = AddDb(src);
        string arc = tmp.P("결과" + ext);
        var svc = Svc.For(arc);

        CompressResult result;
        using (LockRange(db, 200_000, 50_000))
        {
            // 첫 블록은 읽히는 잠금인지(테스트 전제) — 열기는 성공해야 '읽는 도중'을 시험한다
            Assert.True(SourceFileOpener.TryOpenRead(db, out var probe, out _, SourceOpenRetry.None), "첫 블록부터 막힘 — 시험 전제가 틀림");
            probe!.Dispose();
            result = await svc.CompressAsync(new[] { src }, arc);
        }

        var skipped = Assert.Single(result.Skipped);
        Assert.Equal(db, skipped.Path);
        Assert.Equal(SourceFileOpener.ReasonInUse, skipped.Reason);
        string dest = tmp.P("out");
        await svc.ExtractAsync(arc, dest);
        TreeAssert.ExtractedTreeEquals(dest, expected);
    }

    /// <summary>GZ·BZ2 는 읽는 도중 막히면 엔진 오류 문구가 아니라 쉬운 안내(<see cref="SourceUnreadableException"/>)로 실패한다.</summary>
    [Theory]
    [InlineData(".gz")]
    [InlineData(".bz2")]
    public async Task 단일파일_형식은_읽는_도중_막혀도_SourceUnreadable(string ext)
    {
        using var tmp = new TempDir("skiplg");
        string db = AddDb(tmp.Dir("src"));
        string arc = tmp.P(DbName + ext);

        Exception? ex;
        using (LockRange(db, 200_000, 50_000))
            ex = await Svc.Catch(() => Svc.For(arc).CompressAsync(new[] { db }, arc));

        var su = Assert.IsType<SourceUnreadableException>(ex);
        Assert.Equal(db, su.SourcePath);
        Assert.Equal(SourceFileOpener.ReasonInUse, su.Reason);
    }

    // ───────────── 하나도 못 담으면 빈 압축을 남기지 않는다(W1-D 1차 검증) ─────────────

    /// <summary>
    /// 고른 것을 모두 건너뛰게 되면(잠긴 PST 하나만 압축 등) 빈 zip·tar 를 만들지 않고 <see cref="NothingToCompressException"/>(GZ·BZ2 와 같은 처리).
    /// 예전엔 22바이트 빈 zip(tar 는 1024바이트)이 생기고 '압축을 마쳤어요'로 안내됐다. 덮어쓰려던 기존 파일도 그대로다.
    /// </summary>
    [Theory]
    [InlineData(".zip", false)]
    [InlineData(".zip", true)]
    [InlineData(".tar", false)]
    [InlineData(".tar", true)]
    public async Task 모두_못_읽으면_빈_압축을_만들지_않는다(string ext, bool existedBefore)
    {
        using var tmp = new TempDir("skipall");
        string pst = tmp.P("메일함.pst");
        File.WriteAllText(pst, "잠긴 내용");
        string arc = tmp.P("결과" + ext);
        if (existedBefore) File.WriteAllText(arc, "예전에 만든 압축");

        Exception? ex;
        using (new FileStream(pst, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            ex = await Svc.Catch(() => Svc.For(arc).CompressAsync(new[] { pst }, arc));

        var nothing = Assert.IsType<NothingToCompressException>(ex);
        var skipped = Assert.Single(nothing.Skipped);
        Assert.Equal(pst, skipped.Path);
        Assert.Equal(SourceFileOpener.ReasonInUse, skipped.Reason);
        if (existedBefore)
            Assert.Equal("예전에 만든 압축", File.ReadAllText(arc));
        else
            Assert.False(File.Exists(arc), "하나도 못 담았는데 빈 압축 파일이 생김");
    }

    /// <summary>파일은 모두 못 읽고 빈 하위 폴더만 남는 경우도 '하나도 못 담음'이다(빈 폴더뿐인 압축을 만들지 않음).</summary>
    [Fact]
    public async Task 빈_폴더만_남으면_못_담은_것으로_본다()
    {
        using var tmp = new TempDir("skipall2");
        string src = tmp.Dir("보낼 자료");
        Directory.CreateDirectory(Path.Combine(src, "빈 폴더"));
        string pst = Path.Combine(src, "메일함.pst");
        File.WriteAllText(pst, "잠긴 내용");
        string arc = tmp.P("결과.zip");

        Exception? ex;
        using (new FileStream(pst, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            ex = await Svc.Catch(() => Svc.For(arc).CompressAsync(new[] { src }, arc));

        Assert.IsType<NothingToCompressException>(ex);
        Assert.False(File.Exists(arc));
    }

    /// <summary>원래 담을 것이 없던 경우(원본 폴더 안의 압축 파일 자신뿐 — 건너뛴 것 없음)는 예전처럼 빈 압축을 만든다(오류 아님).</summary>
    [Fact]
    public async Task 건너뛴_것이_없으면_빈_압축은_예전처럼_만든다()
    {
        using var tmp = new TempDir("skipnone");
        string src = tmp.Dir("원본");
        string arc = Path.Combine(src, "원본.zip");
        File.WriteAllText(arc, "덮어쓸 예전 파일");

        var result = await Svc.For(arc).CompressAsync(new[] { arc }, arc);

        Assert.Empty(result.Skipped);
        Assert.Empty(await Svc.For(arc).ListEntriesAsync(arc));
    }

    /// <summary>하위 폴더 하나에 들어갈 권한이 없어도 나머지는 압축되고, 그 폴더가 '건너뜀'으로 적힌다(예전엔 압축 전체 실패).</summary>
    [Theory]
    [InlineData(".zip")]
    [InlineData(".tar")]
    public async Task 못_들어가는_하위폴더는_건너뛴다(string ext)
    {
        using var tmp = new TempDir("skipd");
        string src = SampleTree.Create(tmp.Dir("src"));
        string blocked = Path.Combine(src, "하위", "깊이2");
        var expected = Without(SampleTree.ExpectedOf(src), $"{SampleTree.RootName}/하위/깊이2").ToList();
        string arc = tmp.P("결과" + ext);
        var svc = Svc.For(arc);

        CompressResult result;
        using (DenyListing(blocked))
            result = await svc.CompressAsync(new[] { src }, arc);

        var skipped = Assert.Single(result.Skipped);
        Assert.Equal(blocked, skipped.Path);
        Assert.Equal(SourceFileOpener.ReasonDenied, skipped.Reason);
        string dest = tmp.P("out");
        await svc.ExtractAsync(arc, dest);
        TreeAssert.ExtractedTreeEquals(dest, expected);
    }

    /// <summary>
    /// 원본 모으기를 폴더별로 바꿨지만(권한 오류를 폴더 단위로 건너뛰려고) 담는 차례는 예전과 같다:
    /// 파일은 Directory.EnumerateFiles(AllDirectories), 빈 폴더는 [기준 폴더] + EnumerateDirectories(AllDirectories) 중 완전히 빈 것.
    /// </summary>
    [Fact]
    public void 원본_모으는_차례는_예전과_같다()
    {
        using var tmp = new TempDir("order");
        string src = SampleTree.Create(tmp.Dir("src"));
        Directory.CreateDirectory(Path.Combine(src, "하위", "또 빈 폴더"));
        File.WriteAllText(Path.Combine(src, "하위", "깊이2", "중간.txt"), "중간");
        string single = tmp.P("따로.txt");
        File.WriteAllText(single, "따로");

        var set = SourceCollector.Collect(new[] { src, single });

        var oldFiles = Directory.EnumerateFiles(src, "*", SearchOption.AllDirectories).Append(single).ToList();
        Assert.Equal(oldFiles, set.Files.Select(f => f.SourceFile).ToList());
        Assert.Equal(oldFiles.Select(f => Path.GetRelativePath(tmp.Root, f).Replace('\\', '/').Replace("src/", "")).ToList(),
            set.Files.Select(f => f.EntryName).ToList());

        var oldEmpty = new[] { src }.Concat(Directory.EnumerateDirectories(src, "*", SearchOption.AllDirectories))
            .Where(d => !Directory.EnumerateFileSystemEntries(d).Any()).ToList();
        Assert.Equal(oldEmpty, set.EmptyDirectories.Select(d => d.SourceDir).ToList());
        Assert.All(set.EmptyDirectories, d => Assert.EndsWith("/", d.EntryName));
        Assert.Empty(set.Skipped);
    }

    /// <summary>담아 둔 뒤에 지워진 원본은 말없이 빠지지 않고 '파일이 없어졌어요'로 적힌다.</summary>
    [Fact]
    public async Task 없어진_원본은_건너뜀으로_적힌다()
    {
        using var tmp = new TempDir("skipm");
        string a = tmp.P("있음.txt");
        File.WriteAllText(a, "있음");
        string gone = tmp.P("없어짐.txt");
        string arc = tmp.P("결과.zip");

        var result = await Svc.For(arc).CompressAsync(new[] { a, gone }, arc);

        var skipped = Assert.Single(result.Skipped);
        Assert.Equal(gone, skipped.Path);
        Assert.Equal(SourceFileOpener.ReasonMissing, skipped.Reason);
        string dest = tmp.P("out");
        await Svc.For(arc).ExtractAsync(arc, dest);
        Assert.Equal(new[] { "있음.txt" }, TreeAssert.Files(dest));
    }

    /// <summary>압축 파일을 원본 폴더 안에 (덮어)쓰면 지금 쓰는 자기 자신은 담지 않는다(못 열어 '건너뜀'으로 보이지 않게).</summary>
    [Fact]
    public async Task 원본_폴더_안의_압축_파일_자신은_담지_않는다()
    {
        using var tmp = new TempDir("skips");
        string src = tmp.Dir("원본");
        File.WriteAllText(Path.Combine(src, "a.txt"), "a");
        string arc = Path.Combine(src, "원본.zip");
        File.WriteAllText(arc, "덮어쓸 예전 파일");

        var result = await Svc.For(arc).CompressAsync(new[] { src }, arc);

        Assert.Empty(result.Skipped);
        string dest = tmp.P("out");
        await Svc.For(arc).ExtractAsync(arc, dest);
        Assert.Equal(new[] { "원본/a.txt" }, TreeAssert.Files(dest));
    }

    /// <summary>GZ·BZ2 는 파일 하나짜리라 그 파일을 못 읽으면 쉬운 안내 예외. 빈 압축 파일을 만들지 않는다.</summary>
    [Theory]
    [InlineData(".gz")]
    [InlineData(".bz2")]
    public async Task 단일파일_형식은_못_읽으면_SourceUnreadable(string ext)
    {
        using var tmp = new TempDir("skipg");
        string file = tmp.P("메일함.pst");
        File.WriteAllText(file, "잠긴 내용");
        string arc = tmp.P("메일함.pst" + ext);

        Exception? ex;
        using (new FileStream(file, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            ex = await Svc.Catch(() => Svc.For(arc).CompressAsync(new[] { file }, arc));

        var su = Assert.IsType<SourceUnreadableException>(ex);
        Assert.Equal(file, su.SourcePath);
        Assert.Equal(SourceFileOpener.ReasonInUse, su.Reason);
        Assert.Contains("'메일함.pst' 파일을 읽을 수 없어요", su.Message);
        Assert.Contains("다른 프로그램이 쓰고 있으면 닫고 다시 해 주세요", su.Message);
        Assert.False(File.Exists(arc), "못 읽었는데 압축 파일이 생김");
    }

    /// <summary>GZ·BZ2 도 쓰기 공유로 열린 파일은 압축된다.</summary>
    [Theory]
    [InlineData(".gz")]
    [InlineData(".bz2")]
    public async Task 단일파일_형식도_쓰기_공유_파일은_압축된다(string ext)
    {
        using var tmp = new TempDir("skipgw");
        string file = tmp.P("로그.txt");
        File.WriteAllText(file, string.Concat(Enumerable.Repeat("로그 한 줄\n", 500)));
        string sha = TreeAssert.Sha256(file);
        string arc = tmp.P("로그.txt" + ext);

        CompressResult result;
        using (new FileStream(file, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite))
            result = await Svc.For(arc).CompressAsync(new[] { file }, arc);

        Assert.Empty(result.Skipped);
        string dest = tmp.P("out");
        await Svc.For(arc).ExtractAsync(arc, dest);
        Assert.Equal(sha, TreeAssert.Sha256(Path.Combine(dest, "로그.txt")));
    }

    // ───────────── 화면(VM): 안내 문구·결과 파일 ─────────────

    /// <summary>건너뛴 파일이 있으면 '압축을 마쳤어요. 건너뛴 파일 1개' 안내, 결과 압축은 남고 열린다.</summary>
    [Fact]
    public async Task 화면은_건너뛴_파일을_알리고_결과를_남긴다()
    {
        using var tmp = new TempDir("skipvm");
        string src = SampleTree.Create(tmp.Dir("src"));
        string save = tmp.P("결과.zip");
        var ui = new FakeUserInteraction { SavePath = save, CompressionOptions = CompressionOptions.Default };
        var vm = new MainViewModel(ui);
        vm.AddToStaging(new[] { src });

        using (new FileStream(LockedPath(src), FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            await VmRunner.Run(() => vm.CompressCommand.ExecuteAsync(null), "압축");

        Assert.Empty(ui.Errors);
        var info = Assert.Single(ui.Infos);
        Assert.StartsWith("압축을 마쳤어요. 건너뛴 파일 1개:", info);
        Assert.Contains($"- {LockedName} (다른 프로그램이 쓰고 있어요)", info);
        Assert.True(File.Exists(save), "건너뛴 파일이 있다고 결과를 지움");
        Assert.Equal(save, vm.CurrentArchivePath);   // 예전처럼 결과를 열어 보여 줌
    }

    /// <summary>
    /// 하나도 못 담으면 화면은 '압축을 마쳤어요'가 아니라 오류로 알리고(무엇을 못 읽었는지 같이), 빈 압축 파일을 남기지 않으며 모으던 목록은 그대로다.
    /// (예전엔 빈 zip 이 생겨 '0개 항목'으로 열리고 '압축을 마쳤어요. 건너뛴 파일 1개' — 빈 zip 을 거래처에 보낼 위험)
    /// </summary>
    [Fact]
    public async Task 화면은_하나도_못_담으면_오류로_알리고_압축을_남기지_않는다()
    {
        using var tmp = new TempDir("skipvm0");
        string pst = tmp.P("메일함.pst");
        File.WriteAllText(pst, "잠긴 내용");
        string save = tmp.P("메일함.zip");
        var ui = new FakeUserInteraction { SavePath = save, CompressionOptions = CompressionOptions.Default };
        var vm = new MainViewModel(ui);
        vm.AddToStaging(new[] { pst });

        using (new FileStream(pst, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            await VmRunner.Run(() => vm.CompressCommand.ExecuteAsync(null), "압축");

        Assert.Empty(ui.Infos);
        var error = Assert.Single(ui.Errors);
        Assert.Equal(
            NothingToCompressException.DefaultMessage + "\n건너뛴 파일 1개:\n- 메일함.pst (다른 프로그램이 쓰고 있어요)\n\n다른 프로그램이 쓰고 있으면 닫고 다시 해 주세요.",
            error);
        Assert.False(File.Exists(save), "빈 압축 파일이 남음");
        Assert.Equal("오류 발생", vm.StatusText);
        Assert.True(vm.IsStagingMode, "모으던 목록이 비워짐");
    }

    /// <summary>
    /// 단일 파일 형식에서 원본을 못 읽어 실패하면 오류를 알리되, 덮어쓰려던 기존 파일은 지우지 않는다
    /// (압축 파일을 열기도 전에 끝났으므로 — 예전엔 실패하면 저장 경로를 무조건 지웠다).
    /// </summary>
    [Fact]
    public async Task 원본을_못_읽은_실패는_덮어쓰려던_파일을_지우지_않는다()
    {
        using var tmp = new TempDir("skipvm2");
        string file = tmp.P("메일함.pst");
        File.WriteAllText(file, "잠긴 내용");
        string save = tmp.P("예전.gz");
        File.WriteAllText(save, "예전에 만든 압축(덮어쓰기 확인을 누른 상태)");
        var ui = new FakeUserInteraction { SavePath = save, CompressionOptions = CompressionOptions.Default };
        var vm = new MainViewModel(ui);
        vm.AddToStaging(new[] { file });

        using (new FileStream(file, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            await VmRunner.Run(() => vm.CompressCommand.ExecuteAsync(null), "압축");

        var error = Assert.Single(ui.Errors);
        Assert.Contains("'메일함.pst' 파일을 읽을 수 없어요", error);
        Assert.Equal("오류 발생", vm.StatusText);
        Assert.Equal("예전에 만든 압축(덮어쓰기 확인을 누른 상태)", File.ReadAllText(save));
    }

    /// <summary>압축 도중 실패·취소(만들던 파일을 쓰기 시작한 뒤)는 예전처럼 부분 파일을 지운다 — 덮어쓰던 기존 파일이어도(이미 망가짐).</summary>
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task 쓰기_시작한_뒤의_실패와_취소는_부분_파일을_지운다(bool cancel, bool existedBefore)
    {
        using var tmp = new TempDir("skipvm3");
        string file = tmp.P("a.txt");
        File.WriteAllText(file, "a");
        string save = tmp.P("결과.zip");
        if (existedBefore) File.WriteAllText(save, "예전 파일");
        var ui = new FakeUserInteraction { SavePath = save, CompressionOptions = CompressionOptions.Default };
        var locator = new ArchiveServiceLocator();
        locator.Register(new HalfWritingService(cancel));
        var vm = new MainViewModel(ui, locator);
        vm.AddToStaging(new[] { file });

        await VmRunner.Run(() => vm.CompressCommand.ExecuteAsync(null), "압축");

        Assert.False(File.Exists(save), "쓰다 만 압축 파일이 남음");
        if (cancel)
        {
            Assert.Empty(ui.Errors);
            Assert.Equal("압축 취소됨", vm.StatusText);
        }
        else
        {
            Assert.Contains("쓰다가 멈춤", Assert.Single(ui.Errors));
            Assert.Equal("오류 발생", vm.StatusText);
        }
    }

    /// <summary>압축 파일에 조금 쓰고 나서 실패(또는 취소)하는 가짜 ZIP 서비스.</summary>
    private sealed class HalfWritingService(bool cancel) : IArchiveService
    {
        public IReadOnlyList<string> SupportedExtensions { get; } = new[] { ".zip" };
        public Task<IReadOnlyList<ArchiveEntry>> ListEntriesAsync(string a, string? p = null, CancellationToken c = default) => throw new NotImplementedException();
        public Task ExtractAsync(string a, string d, string? p = null, IProgress<ArchiveProgress>? pr = null, CancellationToken c = default) => throw new NotImplementedException();
        public Task ExtractEntriesAsync(string a, string d, IReadOnlyCollection<string> e, string? p = null, IProgress<ArchiveProgress>? pr = null, CancellationToken c = default) => throw new NotImplementedException();

        public async Task<CompressResult> CompressAsync(IEnumerable<string> s, string a, CompressionOptions? o = null, IProgress<ArchiveProgress>? pr = null, CancellationToken c = default)
        {
            await File.WriteAllBytesAsync(a, new byte[] { 0x50, 0x4B, 0x03, 0x04, 1, 2, 3 });
            if (cancel) throw new OperationCanceledException();
            throw new IOException("쓰다가 멈춤(디스크 가득 흉내)");
        }
    }

    [Fact]
    public void 안내는_10줄까지_나머지는_외_N개()
    {
        var skipped = Enumerable.Range(1, 12).Select(i => new SkippedSource($@"C:\없는 폴더\파일{i}.txt", SourceFileOpener.ReasonInUse)).ToList();
        string msg = MainViewModel.SkippedMessage(skipped);
        var lines = msg.Split('\n');
        Assert.Equal("압축을 마쳤어요. 건너뛴 파일 12개:", lines[0]);
        Assert.Equal(12, lines.Length);   // 머리 1 + 10줄 + '외 2개'
        Assert.Equal("- 파일1.txt (다른 프로그램이 쓰고 있어요)", lines[1]);
        Assert.Equal("외 2개", lines[^1]);
    }

    /// <summary>현재 사용자에게 그 폴더 목록 보기를 막는다(Dispose 때 되돌림). 시스템 설정이 아니라 테스트 임시 폴더의 권한만 바꾼다.</summary>
    private static IDisposable DenyListing(string dir)
    {
        var info = new DirectoryInfo(dir);
        var user = WindowsIdentity.GetCurrent().User!;
        var rule = new FileSystemAccessRule(user, FileSystemRights.ListDirectory, InheritanceFlags.None, PropagationFlags.None, AccessControlType.Deny);
        var acl = info.GetAccessControl();
        acl.AddAccessRule(rule);
        info.SetAccessControl(acl);
        return new Restore(() =>
        {
            var a = info.GetAccessControl();
            a.RemoveAccessRule(rule);
            info.SetAccessControl(a);
        });
    }

    private sealed class Restore(Action undo) : IDisposable
    {
        public void Dispose() => undo();
    }
}
