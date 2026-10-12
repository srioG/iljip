using ICSharpCode.SharpZipLib.GZip;
using Iljip.Models;
using Iljip.Services;
using Iljip.Tests.TestSupport;

namespace Iljip.Tests.Contract;

/// <summary>
/// [W1-B, I2-9] 압축된 tar(.tgz·.tar.gz·.tbz2·.txz)를 한 번에 펼치는 처리기(CompressedTarArchiveService)의 계약:
/// 선택 해제, 데이터 손상(gzip CRC)이면 이번에 쓴 파일을 모두 지움, 취소하면 끝난 파일만 남김, 진행률 모양.
/// (잘린 .tar.gz 는 CorruptArchiveTests, 목록·전체 해제는 KnownGapTests)
/// </summary>
public class CompressedTarTests
{
    public static TheoryData<string> All => FixtureSets.Data(new[] { "tgz_default", "targz_default", "txz_two_step", "tbz2_python" });

    /// <summary>폴더를 고르면 그 아래만 풀린다(다른 형식과 같은 선택 규칙).</summary>
    [Theory]
    [MemberData(nameof(All))]
    public async Task 폴더_선택_해제(string id)
    {
        var fx = FixtureCatalog.Get(id);
        using var tmp = new TempDir("tgzsel");
        string dest = tmp.P("out");
        await Svc.For(fx.FullPath).ExtractEntriesAsync(fx.FullPath, dest, new[] { "일집_샘플/하위폴더" });
        var want = fx.ExpectedFiles.Where(e => e.Path.StartsWith("일집_샘플/하위폴더/", StringComparison.Ordinal)).ToDictionary(e => e.Path, e => e.Sha256);
        var got = TreeAssert.Files(dest).ToDictionary(f => f, f => (string?)TreeAssert.Sha256(Path.Combine(dest, f)));
        Assert.Equal(want.OrderBy(kv => kv.Key, StringComparer.Ordinal), got.OrderBy(kv => kv.Key, StringComparer.Ordinal));
    }

    /// <summary>
    /// 계약: gzip 끝 확인값(CRC)이 틀리면(데이터 손상) 예외 — 어느 파일이 틀렸는지 알 수 없으므로 이번에 쓴 파일을 하나도 남기지 않는다.
    /// 화면이 암호 문제로 오해하지도 않는다.
    /// </summary>
    [Fact]
    public async Task gzip_확인값이_틀리면_쓴_파일을_모두_지운다()
    {
        var fx = FixtureCatalog.Get("targz_default");
        using var tmp = new TempDir("tgzcrc");
        byte[] b = File.ReadAllBytes(fx.FullPath);
        b[^8] ^= 0x01;   // 끝 8바이트 = CRC32(4) + 원래 크기(4)
        string arc = tmp.P("crc.tar.gz");
        File.WriteAllBytes(arc, b);
        string dest = tmp.P("out");
        var ex = await Svc.Catch(() => Svc.For(arc).ExtractAsync(arc, dest));
        Assert.IsType<CorruptArchiveException>(ex);
        Assert.False(Internals.IsPasswordRelated(ex!));
        TreeAssert.NoFiles(dest);
    }

    /// <summary>
    /// 계약(W2-01, 1차 검증 지적 (c)): 뒤가 잘린 .tgz·.tar.gz·.tbz2·.txz(받다 만 파일)는 어디서 잘려도 '끝부분이 잘려' 로 알리고
    /// 대상에는 아무것도 남기지 않는다 — 파일도, 임시 폴더도, 이번에 새로 만든 대상 폴더도(전부 아니면 전무).
    /// 예전(1차 W1-B)엔 잘린 지점 앞에서 풀린 파일을 대상에 바로 써서 남겼다.
    /// </summary>
    [Theory]
    [MemberData(nameof(All))]
    public async Task 잘린_압축_tar는_잘렸다고_알리고_아무것도_남기지_않는다(string id)
    {
        var fx = FixtureCatalog.Get(id);
        using var tmp = new TempDir("tgzcut");
        byte[] b = File.ReadAllBytes(fx.FullPath);
        string ext = fx.File.EndsWith(".tar.gz", StringComparison.OrdinalIgnoreCase) ? ".tar.gz" : Path.GetExtension(fx.File);
        foreach (int cut in new[] { 20, b.Length / 4, b.Length / 2, b.Length * 3 / 4, b.Length - 9, b.Length - 4, b.Length - 1 }.Distinct())
        {
            string arc = tmp.P($"cut{cut}{ext}");
            File.WriteAllBytes(arc, b[..cut]);
            string parent = tmp.P($"새 폴더{cut}");
            string dest = Path.Combine(parent, "out");
            var ex = await Svc.Catch(() => Svc.For(arc).ExtractAsync(arc, dest)).WaitAsync(TimeSpan.FromSeconds(30));
            var corrupt = Assert.IsAssignableFrom<CorruptArchiveException>(ex);
            Assert.True(corrupt.IsTruncated, $"{id} {cut}/{b.Length}: 잘림으로 알리지 않음 — {corrupt.Message}");
            Assert.False(Internals.IsPasswordRelated(ex!));
            Assert.DoesNotContain("압축 파일이 아닌", ErrorMessages.ToUserMessage(ex!));
            Assert.False(Directory.Exists(parent), $"{id} {cut}/{b.Length}: 대상에 무언가 남음(새로 만든 폴더 포함)");
        }
    }

    /// <summary>
    /// 계약(W2-01 (c)): 데이터 한가운데가 깨진 압축 tar 도 손상으로 알리고 아무것도 남기지 않는다(바깥 확인값 — gzip CRC32·bzip2 블록 CRC·xz 검사값).
    /// 이미 있던 대상 폴더와 그 안의 파일은 그대로 둔다.
    /// </summary>
    [Theory]
    [MemberData(nameof(All))]
    public async Task 깨진_압축_tar는_원래_있던_것만_남긴다(string id)
    {
        var fx = FixtureCatalog.Get(id);
        using var tmp = new TempDir("tgzbad");
        byte[] b = File.ReadAllBytes(fx.FullPath);
        b[b.Length / 2] ^= 0x55;
        string arc = tmp.P("bad" + Path.GetExtension(fx.File));
        if (fx.File.EndsWith(".tar.gz", StringComparison.OrdinalIgnoreCase)) arc = tmp.P("bad.tar.gz");
        File.WriteAllBytes(arc, b);
        string dest = tmp.Dir("out");
        File.WriteAllText(Path.Combine(dest, "원래 파일.txt"), "그대로");
        var ex = await Svc.Catch(() => Svc.For(arc).ExtractAsync(arc, dest)).WaitAsync(TimeSpan.FromSeconds(30));
        Assert.IsAssignableFrom<CorruptArchiveException>(ex);
        Assert.Equal(new[] { "원래 파일.txt" }, TreeAssert.Files(dest));
        Assert.Empty(Directory.EnumerateDirectories(dest, StagingFolder.Prefix + "*"));
    }

    /// <summary>계약: 큰 .tgz 를 푸는 도중(큰 파일 한가운데) 취소 → OCE, 끝난 앞 파일만 남고 쓰던 큰 파일은 지워진다.</summary>
    [Fact]
    public async Task 해제_도중_취소하면_끝난_파일만_남는다()
    {
        using var tmp = new TempDir("tgzc");
        string src = CancellationTests.MakeSource(tmp);
        string tar = tmp.P("big.tar");
        await new TarArchiveService().CompressAsync(new[] { src }, tar);
        string tgz = tmp.P("big.tgz");
        using (var input = File.OpenRead(tar))
        using (var output = File.Create(tgz))
        using (var gz = new GZipOutputStream(output))
        {
            gz.SetLevel(1);
            input.CopyTo(gz);
        }
        using var cts = new CancellationTokenSource();
        var progress = new ProgressRecorder(p =>
        {
            if (p.CurrentFile.EndsWith("z_큰.bin", StringComparison.Ordinal) && p.BytesProcessed > 8L * 1024 * 1024) cts.Cancel();
        });
        string dest = tmp.P("out");
        var ex = await Svc.Catch(() => Svc.For(tgz).ExtractAsync(tgz, dest, null, progress, cts.Token));
        Assert.IsAssignableFrom<OperationCanceledException>(ex);
        Assert.Equal(new[] { "취소 원본/a_작은.txt" }, TreeAssert.Files(dest));
        Assert.Empty(Directory.EnumerateDirectories(dest, StagingFolder.Prefix + "*"));
    }

    /// <summary>
    /// 계약(1차 W1-B 검증 지적 → W2-01): 대상 폴더 쪽 문제(풀 폴더 자리에 같은 이름의 '파일'이 있음)는 압축 파일 손상이 아니다 →
    /// 그 입출력 예외를 그대로(화면은 쉬운 한국어) 알린다. 예전엔 '압축 파일이 손상됐어요. 다시 받아 보세요.'로 멀쩡한 파일을 다시 받으라 했다.
    /// W2-01 부터 임시 폴더에 다 푼 뒤 옮기므로, 옮기다 막히면 이번에 옮긴 파일을 되돌린다(설계 §2 '이동 실패 → 장부로 되돌림' — 반쪽 결과 없음).
    /// 원래 있던 파일(폴더 자리의 파일)은 건드리지 않는다.
    /// </summary>
    [Fact]
    public async Task 대상_폴더_쪽_오류는_손상이_아니고_반쪽_결과를_남기지_않는다()
    {
        using var tmp = new TempDir("tgzdst");
        string src = tmp.Dir("묶음");
        File.WriteAllText(Path.Combine(src, "가_먼저.txt"), "먼저 풀리는 파일");
        File.WriteAllText(Path.Combine(tmp.Dir("묶음", "한글폴더"), "나중.txt"), "폴더 안 파일");
        string tar = tmp.P("묶음.tar");
        await new TarArchiveService().CompressAsync(new[] { src }, tar);
        string tgz = tmp.P("묶음.tgz");
        using (var input = File.OpenRead(tar))
        using (var output = File.Create(tgz))
        using (var gz = new GZipOutputStream(output))
            input.CopyTo(gz);

        string dest = tmp.Dir("out", "묶음");
        File.WriteAllText(Path.Combine(dest, "한글폴더"), "폴더 자리에 있는 파일");   // 여기에 폴더를 만들 수 없다
        var ex = await Svc.Catch(() => Svc.For(tgz).ExtractAsync(tgz, tmp.P("out")));

        Assert.IsAssignableFrom<IOException>(ex);
        Assert.IsNotType<CorruptArchiveException>(ex);
        string m = ErrorMessages.ToUserMessage(ex!);
        Assert.DoesNotContain("손상", m);
        Assert.DoesNotContain("Cannot create", m);
        Assert.Contains("같은 이름", m);
        Assert.Equal(new[] { "한글폴더" }, TreeAssert.Files(dest));   // 원래 있던 파일만 — 옮겼던 '가_먼저.txt' 는 되돌림
        Assert.Equal("폴더 자리에 있는 파일", File.ReadAllText(Path.Combine(dest, "한글폴더")));
        Assert.Empty(Directory.EnumerateDirectories(tmp.P("out"), StagingFolder.Prefix + "*"));
    }

    /// <summary>
    /// 진행률(현재 동작 기록, W2-01): 총량 = 압축 크기 × 2. 앞 절반(0~50%)은 바깥 압축을 읽은 위치(현재 파일 = 압축 파일 이름, 파일 수 0/0),
    /// 뒤 절반(50~100%)은 안쪽 tar 를 푸는 진행(현재 파일 = 항목 이름, 파일 수 n/9). 줄어들지 않고 마지막은 완료 보고 한 번(9/9).
    /// </summary>
    [Fact, Trait(Kinds.Key, Kinds.Record)]
    public async Task 기록_진행률은_압축파일_읽은_위치_기준()
    {
        var fx = FixtureCatalog.Get("tgz_default");
        using var tmp = new TempDir("tgzp");
        // 새 경로로 복사 — 같은 압축을 앞서 목록으로 본 테스트가 있으면 안쪽 tar 캐시를 다시 써서 앞 절반(바깥 풀기)이 없을 수 있다
        string arc = tmp.P("진행.tgz");
        File.Copy(fx.FullPath, arc);
        var rec = new ProgressRecorder();
        await Svc.For(arc).ExtractAsync(arc, tmp.P("out"), null, rec);
        ProgressTests.AssertWellFormed(rec.Reports, "tgz", expectIntermediate: true);
        long len = new FileInfo(arc).Length;
        var last = rec.Reports[^1];
        Assert.Equal(len * 2, last.TotalBytes);
        Assert.Equal(9, last.TotalFiles);
        var outer = rec.Reports.Where(r => r.CurrentFile == Path.GetFileName(arc)).ToList();
        var inner = rec.Reports.Take(rec.Reports.Count - 1).Except(outer).ToList();
        Assert.NotEmpty(outer);
        Assert.NotEmpty(inner);
        Assert.All(outer, r => { Assert.Equal(0, r.TotalFiles); Assert.InRange(r.BytesProcessed, 0, len); });
        Assert.All(inner, r => { Assert.Equal(9, r.TotalFiles); Assert.InRange(r.BytesProcessed, len, len * 2 - 1); });
    }

    /// <summary>현재 동작 기록: 압축된 tar 는 만들 수 없다(풀기만) — 정해진 예외로 거절하고 파일을 만들지 않는다.</summary>
    [Fact, Trait(Kinds.Key, Kinds.Record)]
    public async Task 기록_압축된_tar는_만들지_않는다()
    {
        using var tmp = new TempDir("tgzmk");
        string one = tmp.P("a.txt");
        File.WriteAllText(one, "x");
        string arc = tmp.P("a.tar.gz");
        var ex = await Svc.Catch(() => Svc.For(arc).CompressAsync(new[] { one }, arc));
        Assert.IsType<NotSupportedException>(ex);
        Assert.False(File.Exists(arc));
    }
}
