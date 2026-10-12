using Iljip.Services;
using Iljip.Tests.TestSupport;

namespace Iljip.Tests.Contract;

/// <summary>
/// 고친 버그 — 다시 생기면 안 됨. 예전 BugEvidenceTests 의 '지금 버그가 있다' 근거 테스트를 같은 데이터로 옮겨
/// 기대를 '올바른 동작'으로 뒤집은 것이다(계약 테스트, Trait 없음).
/// 같은 버그의 다른 모습은 각 묶음의 계약 테스트가 지킨다(아래 요약 주석에 적음).
/// </summary>
public class FixedBugTests
{
    /// <summary>
    /// [B1] 반디집 7z 의 빈(0바이트) 파일: 예전엔 'File does not have a stream.' 으로 해제가 실패했다(SharpCompress 0.50 으로 고쳐짐).
    /// 빈파일.txt 가 0바이트로 생기고 나머지 파일 내용도 모두 같아야 한다.
    /// (전체 트리 비교는 BandizipFixtureTests.반디집_7z_전체_해제가_반디집과_같다·헤더암호_7z_전체_해제가_반디집과_같다)
    /// </summary>
    [Fact]
    public async Task 반디집_7z_빈파일은_0바이트로_풀린다()
    {
        var fx = FixtureCatalog.Get("7z_default");
        using var tmp = new TempDir("b1");
        string dest = tmp.P("out");
        await Svc.For(fx.FullPath).ExtractAsync(fx.FullPath, dest);
        string empty = Path.Combine(dest, "일집_샘플", "빈파일.txt");
        Assert.True(File.Exists(empty), "빈파일.txt 가 안 생김");
        Assert.Equal(0, new FileInfo(empty).Length);
        var snap = TreeAssert.Snapshot(dest);
        foreach (var e in fx.ExpectedFiles)
            Assert.True(snap.TryGetValue(e.Path, out var sha) && sha == e.Sha256, $"없음/내용 다름: {e.Path}");
    }

    /// <summary>
    /// [B2] 원본 이름(FNAME)이 없는 .gz — 반디집 산출물과 일집 자신의 산출물 모두 예전엔 목록 이름이 '' 이고 해제가
    /// '안전하지 않은 경로: ' 로 실패했다. 이제는 압축 파일 이름에서 .gz 를 뗀 이름으로 보이고 그 이름으로 풀린다(반디집 동작).
    /// </summary>
    [Theory]
    [InlineData("bandizip")]
    [InlineData("iljip")]
    public async Task FNAME_없는_gz는_파일명에서_이름을_정하고_풀린다(string maker)
    {
        using var tmp = new TempDir("b2");
        string arc, expectedName, expectedSha;
        if (maker == "bandizip")
        {
            var fx = FixtureCatalog.Get("gz_single");
            arc = fx.FullPath;
            expectedName = "견적서_2026.txt";
            expectedSha = Assert.Single(fx.ExpectedFiles).Sha256!;
        }
        else
        {
            string one = tmp.P("원본.txt");
            File.WriteAllText(one, "일집이 만든 gz");
            arc = tmp.P("원본.txt.gz");
            await new GZipArchiveService().CompressAsync(new[] { one }, arc);
            expectedName = "원본.txt";
            expectedSha = TreeAssert.Sha256(one);
        }
        var svc = Svc.For(arc);
        var only = Assert.Single(await svc.ListEntriesAsync(arc));
        Assert.Equal(expectedName, only.Path);
        Assert.False(only.IsDirectory);

        string dest = tmp.P("out");
        await svc.ExtractAsync(arc, dest);   // 예외 없음
        Assert.Equal(new[] { expectedName }, TreeAssert.Files(dest));
        Assert.Equal(expectedSha, TreeAssert.Sha256(Path.Combine(dest, expectedName)));
    }

    /// <summary>
    /// [B2 보강] 압축 파일 이름이 점뿐이라 이름을 정할 수 없는 FNAME 없는 .gz('.gz' → '', '..gz' → '.', '...gz' → '..')는
    /// '압축풀림'으로 풀린다. W1-A 첫 수정은 '.'·'..' 를 그대로 써서 '안전하지 않은 경로: ..' 오류로 멈췄다(밖에 쓰지는 않음).
    /// </summary>
    [Theory]
    [InlineData(".gz")]
    [InlineData("..gz")]
    [InlineData("...gz")]
    public async Task 이름이_점뿐인_gz는_압축풀림으로_풀린다(string fileName)
    {
        using var tmp = new TempDir("b2dot");
        byte[] data = System.Text.Encoding.UTF8.GetBytes("점 이름");
        string arc = Path.Combine(tmp.Dir("src"), fileName);
        RawGz.Write(arc, null, data);
        var svc = Svc.For(arc);
        Assert.Equal(ArchiveNaming.FallbackName, Assert.Single(await svc.ListEntriesAsync(arc)).Path);
        string dest = tmp.P("out");
        await svc.ExtractAsync(arc, dest);
        Assert.Equal(new[] { ArchiveNaming.FallbackName }, TreeAssert.Files(dest));
        Assert.Equal(TreeAssert.Sha256(data), TreeAssert.Sha256(Path.Combine(dest, ArchiveNaming.FallbackName)));
    }

    /// <summary>
    /// [B6] TAR 압축: 마지막 큰 파일을 '쓰는 도중'(파일 안 진행률이 8 MiB 를 넘은 순간) 누른 취소도 OCE 로 끝나야 한다.
    /// 예전엔 파일 사이에서만 취소를 확인해 무시되고 '완료'로 끝났다(큰 파일이 다 들어간 tar 가 남음).
    /// 남은 부분 tar 는 서비스가 지우지 않는다(ZIP 과 같은 규칙 — 지우는 건 MainViewModel) → 없거나, 큰 파일이 다 들어가지 않은 길이.
    /// </summary>
    [Fact]
    public async Task TAR_마지막_큰파일_쓰는중_취소하면_OCE()
    {
        using var tmp = new TempDir("b6");
        string src = CancellationTests.MakeSource(tmp);
        string arc = tmp.P("big.tar");
        using var cts = new CancellationTokenSource();
        var progress = new ProgressRecorder(p =>
        {
            if (p.CurrentFile.EndsWith("z_큰.bin", StringComparison.Ordinal) && p.BytesProcessed > 8L * 1024 * 1024) cts.Cancel();
        });
        var ex = await Svc.Catch(() => Svc.For(arc).CompressAsync(new[] { src }, arc, null, progress, cts.Token));
        Assert.IsAssignableFrom<OperationCanceledException>(ex);
        Assert.True(cts.IsCancellationRequested);
        Assert.True(!File.Exists(arc) || new FileInfo(arc).Length < CancellationTests.BigSize, "취소했는데 큰 파일이 다 들어감");
    }

    /// <summary>
    /// [B7] 솎아내기 간격 0 이면 중간 보고 50건이 모두 통과하고 마지막은 완료 보고(총 51건).
    /// 예전엔 'now - long.MinValue' 오버플로로 중간 보고가 하나도 통과하지 않아(완료 1건만) 진행 막대가 끝까지 0 이었다.
    /// </summary>
    [Fact]
    public void 솎아내기_간격0이면_중간보고가_모두_통과한다()
    {
        var seen = ProgressTests.RunThrottled(intervalMs: 0, intermediateCount: 50);
        Assert.Equal(51, seen.Count);
        Assert.Equal(Enumerable.Range(1, 50).Select(i => (long)i), seen.Take(50).Select(p => p.BytesProcessed));
        Assert.Equal(string.Empty, seen[^1].CurrentFile);
    }

    /// <summary>
    /// [B9] 폴더 항목만 앞에 더 있는 EGG(크기 0 항목에 블록 머리 없음): 예전엔 목록부터 NullReferenceException('파일을 열 수 없어요').
    /// 이제 목록이 열리고 폴더 항목은 폴더로, 압축 크기는 0 으로 보인다.
    /// </summary>
    [Fact]
    public async Task 폴더항목만_있는_EGG도_목록이_열린다()
    {
        using var tmp = new TempDir("b9");
        string arc = tmp.P("dirs.egg");
        RawEgg.Write(arc, new[]
        {
            new RawEgg.Item(SampleTree.RootName + "/", null),
            new RawEgg.Item(SampleTree.RootName + "/하위/", null),
        });
        var entries = await Svc.For(arc).ListEntriesAsync(arc);
        Assert.Equal(2, entries.Count);
        Assert.All(entries, e =>
        {
            Assert.True(e.IsDirectory, "폴더가 아님: " + e.Path);
            Assert.Equal(0, e.CompressedSize);
        });
    }

    /// <summary>
    /// [B9] 빈 파일이 든 EGG 해제: 예전엔 빈 파일에서 NullReferenceException(Open()) 으로 멈춰 앞 파일만 풀렸다.
    /// 이제 빈파일.txt 가 0바이트로 생기고 나머지 파일도 모두 풀린다.
    /// </summary>
    [Fact]
    public async Task 빈파일이_든_EGG_해제는_빈파일까지_만든다()
    {
        using var tmp = new TempDir("b9e");
        var (items, files, _) = EggAlzTests.EggSample(tmp);
        string emptyName = SampleTree.RootName + "/빈파일.txt";
        string arc = tmp.P("empty.egg");
        RawEgg.Write(arc, items.Take(1).Append(new RawEgg.Item(emptyName, Array.Empty<byte>())).Concat(items.Skip(1)));
        var svc = Svc.For(arc);
        var listed = await svc.ListEntriesAsync(arc);
        Assert.Equal(items.Count + 1, listed.Count);
        Assert.Equal(0, listed.Single(e => e.Path == emptyName).Size);

        string dest = tmp.P("out");
        await svc.ExtractAsync(arc, dest);
        var snap = TreeAssert.Snapshot(dest);
        Assert.True(snap.TryGetValue(emptyName, out var emptySha) && emptySha == TreeAssert.Sha256(Array.Empty<byte>()), "빈파일.txt 가 0바이트로 안 생김");
        foreach (var f in files)
            Assert.True(snap.TryGetValue(f.Path, out var sha) && sha == f.Sha256, "없음/내용 다름: " + f.Path);
        Assert.Equal(files.Count + 1, TreeAssert.Files(dest).Count);
    }

    // [B8] 손상 7z·egg·alz 열기 실패 후 파일 잠김 → FileLockTests.손상_7z_egg_alz도_열기_실패후_바로_지울_수_있다(같은 데이터)·분할 EGG 테스트.
    // [B10] ALZ CP949 한글 이름 깨짐 → EggAlzTests.ALZ_CP949_한글_이름이_바르게_보인다 + AlzNameScannerTests.
}
