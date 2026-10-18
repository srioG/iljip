using System.Buffers.Binary;
using System.Diagnostics;
using System.Formats.Tar;
using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Text;
using Iljip.Models;
using Iljip.Services;
using Iljip.Tests.TestSupport;
using Iljip.ViewModels;

namespace Iljip.Tests.Contract;

/// <summary>
/// [W2-08 검증 지적 1차] 압축 검사(I2-7)·오류 원인 문구(I2-8)·깨진 ZIP 고치기(I3-12)의 빈틈을 고친 것을 지킨다.
/// <list type="number">
/// <item>항목 경계·앞부분에서 잘린 EGG·ALZ — EggDotNet 이 뒤쪽 항목을 말없이 빼서 시험 '문제없어요'·창 없이 풀기 종료 코드 0(파일을 잃음).
///   이제 끝 표시까지 구조를 따라가 보고(<see cref="ArchiveCompleteness.CheckEggAlz"/>) 잘림으로 알린다. 풀기는 남은 온전한 파일을 다 푼 뒤 오류.</item>
/// <item>로컬 머리의 압축 크기가 커져 다음 항목을 삼킨 ZIP — 고치기가 다음 항목을 말없이 빼고 '모두 살렸어요'.</item>
/// <item>항목이 아주 많은 ZIP 의 '암호 항목이 있나' 훑기가 화면 스레드에서 '항목 수 × 1MB' 를 읽어 수십 초 멈춤.</item>
/// <item>한국어 Windows 의 운영 체제 문구('디스크 공간이 부족합니다. : …')가 설계 문구 대신 그대로 보임.</item>
/// <item>잘린 바깥 ZIP 의 마지막 항목이 저장 방식 안쪽 ZIP 이면 안쪽 항목을 바깥 것으로 살림.</item>
/// <item>엔진이 모르는 압축 방식(SharpCompress 'Unsupported compression method: 97')을 손상으로 보고 고치기를 권함.</item>
/// <item>고친 ZIP 의 저장 위치를 다른 프로그램이 열어 두면 '권한 없음'.</item>
/// <item>네트워크·USB 입출력 오류를 압축 문제로 봄(시험 '문제가 있어요'·고치기의 '못 살린 파일').</item>
/// <item>EGG·ALZ 시험 요약의 파일 수(잘려 목록에서 빠진 것을 모름).</item>
/// <item>압축된 TAR(gz·bz2) 시험이 안쪽 TAR 전체를 일집 임시 폴더에 씀.</item>
/// </list>
/// </summary>
public class ArchiveTestReviewTests
{
    private static readonly TimeSpan Limit = TimeSpan.FromSeconds(60);

    private static Task<TestReport> Test(string path, string? password = null, IProgress<ArchiveProgress>? progress = null, CancellationToken ct = default) =>
        ArchiveTestService.Instance.TestAsync(path, password, progress, ct).WaitAsync(Limit);

    private static Task<RepairReport> Repair(string src, string dest, string? password = null) =>
        ZipRepairer.Instance.RepairAsync(src, dest, password, null, CancellationToken.None).WaitAsync(Limit);

    // ───────────── 1·9. 잘린 EGG·ALZ ─────────────

    private static readonly byte[] EggFileMagic = { 0xE3, 0x90, 0x85, 0x0A };   // EGG 파일 머리 0x0A8590E3
    private static readonly byte[] AlzFileMagic = { 0x42, 0x4C, 0x5A, 0x01 };   // ALZ 'BLZ\1'

    /// <summary>합성 EGG·ALZ(<see cref="SyntheticArchives"/> — 마지막 항목 readme.txt)를 마지막 항목 머리 바로 앞에서 자른 사본 — 받다 만 파일이 항목 경계에서 끊긴 모양.</summary>
    private static (string Cut, SyntheticArchives.Built Whole) CutBeforeLastEntry(TempDir tmp, string kind)
    {
        var whole = SyntheticArchives.Build(kind, tmp.Dir("원본_" + kind));
        byte[] b = File.ReadAllBytes(whole.Path);
        int at = LastIndexOf(b, kind.StartsWith("egg", StringComparison.Ordinal) ? EggFileMagic : AlzFileMagic);
        Assert.True(at > 0, "마지막 항목 머리를 찾지 못함");
        string cut = tmp.P("받다 만 " + kind + Path.GetExtension(whole.Path));
        File.WriteAllBytes(cut, b[..at]);
        return (cut, whole);
    }

    private static List<ExpectedEntry> WithoutReadme(SyntheticArchives.Built whole) => whole.Expected.Where(e => e.Path != "readme.txt").ToList();

    /// <summary>
    /// 계약: 항목 경계에서 잘린 EGG·ALZ 는 시험에서 '문제가 있어요' — 압축 전체 문제 하나(잘림, '끝부분에 있던 파일은 목록에 보이지 않아…'),
    /// 남은 파일은 확인됨('그 밖의 파일 3개는 문제없었어요'). 손상의 한 종류(HasDamage).
    /// </summary>
    [Theory]
    [InlineData("egg")]
    [InlineData("egg_enc")]
    [InlineData("alz")]
    public async Task 항목_경계에서_잘린_EGG_ALZ는_시험에서_잘림으로_알린다(string kind)
    {
        using var tmp = new TempDir("rvcut");
        var (cut, whole) = CutBeforeLastEntry(tmp, kind);
        int remaining = WithoutReadme(whole).Count(e => !e.IsDir);

        var report = await Test(cut, whole.Password);

        Assert.False(report.Ok, $"{kind}: 잘렸는데 '{report.Headline}'");
        Assert.Equal(remaining, report.CheckedCount);
        var p = Assert.Single(report.Problems);
        Assert.Equal(string.Empty, p.Entry);
        Assert.Equal(ProblemKind.Truncated, p.Kind);
        Assert.StartsWith(CorruptArchiveException.TruncatedMessage, p.Reason);
        Assert.EndsWith(EggAlzArchiveService.TruncatedListNote, p.Reason);
        Assert.True(report.HasDamage);
        Assert.Equal("문제가 있어요.", report.Headline);
        Assert.Contains($"그 밖의 파일 {remaining}개는 문제없었어요.", report.ToDisplayText());
    }

    /// <summary>
    /// 계약: 항목 경계에서 잘린 EGG·ALZ 를 풀면 남아 있는 온전한 파일은 다 풀고(반쪽 파일 없음) 잘림 예외로 끝난다(암호 문제로 오해하지 않음 —
    /// 재사용장치는 종료 코드 1 로 보고 반디집으로 다시 푼다). 목록에 있는 파일 하나하나를 골라 풀면(항목 열기 등) 그 파일은 확인까지 끝났으니 알리지 않는다.
    /// </summary>
    [Theory]
    [InlineData("egg")]
    [InlineData("egg_enc")]
    [InlineData("alz")]
    public async Task 항목_경계에서_잘린_EGG_ALZ는_풀기가_잘림으로_끝나고_온전한_파일만_남는다(string kind)
    {
        using var tmp = new TempDir("rvcutx");
        var (cut, whole) = CutBeforeLastEntry(tmp, kind);

        string dest = tmp.P("풀린 곳");
        var ex = await Svc.Catch(() => Svc.For(cut).ExtractAsync(cut, dest, whole.Password).WaitAsync(Limit));
        var trunc = Assert.IsType<CorruptArchiveException>(ex);
        Assert.True(trunc.IsTruncated, trunc.Message);
        Assert.False(Internals.IsPasswordRelated(trunc));
        Assert.Equal(ProblemKind.Truncated, ErrorCatalog.Describe(trunc).Kind);
        TreeAssert.ExtractedTreeEquals(dest, WithoutReadme(whole), context: kind);

        string one = tmp.P("하나만");
        await Svc.For(cut).ExtractEntriesAsync(cut, one, new[] { SyntheticArchives.KoreanFile }, whole.Password).WaitAsync(Limit);
        Assert.Equal(new[] { SyntheticArchives.KoreanFile }, TreeAssert.Files(one));
    }

    /// <summary>
    /// 계약: EGG·ALZ 를 어디에서 자르든(앞부분 머리 안·항목 경계·데이터 안 — 처음 300바이트와 끝 300바이트는 1바이트씩, 그 사이는 듬성듬성) 시험이 '문제없어요'라고 하지 않는다.
    /// ALZ 의 마지막 끝 표시('CLZ\2', 마지막 4바이트) 안에서 잘린 것도 잘림이다(IJ3-02 (a) — 예전엔 'CLZ\1' 까지 있으면 문제없음으로 봐서 이 4바이트를 빼고 시험했다).
    /// </summary>
    [Theory]
    [InlineData("egg")]
    [InlineData("alz")]
    public async Task 어디에서_잘린_EGG_ALZ도_문제없다고_하지_않는다(string kind)
    {
        using var tmp = new TempDir("rvsweep");
        var whole = SyntheticArchives.Build(kind, tmp.Dir("원본"));
        byte[] b = File.ReadAllBytes(whole.Path);
        int last = b.Length - 1;
        var cuts = Enumerable.Range(1, 300)
            .Concat(Enumerable.Range(0, 60).Select(i => 300 + i * (b.Length - 600) / 60))
            .Concat(Enumerable.Range(b.Length - 300, 300))
            .Where(c => c >= 1 && c <= last).Distinct().ToList();
        string p = tmp.P("잘림" + Path.GetExtension(whole.Path));
        var ok = new List<int>();
        foreach (int c in cuts)
        {
            File.WriteAllBytes(p, b[..c]);
            try
            {
                if ((await Test(p)).Ok) ok.Add(c);
            }
            catch (ArchiveException)
            {
                // 압축 파일로 알아볼 수 없음 등 — '문제없어요'가 아니면 된다
            }
        }
        Assert.True(ok.Count == 0, $"{kind}({b.Length}B): 잘렸는데 '문제없어요' {ok.Count}번 — 자른 곳 {string.Join(",", ok.Take(20))}");
    }

    /// <summary>
    /// 계약: 데이터 안에서 잘린 EGG·ALZ 의 요약은 '확인한 파일 3개 중 1개에 문제가 있어요.' — 목록에서 빠진 뒤쪽 파일이 있을 수 있어
    /// 원래 파일 수처럼 보이는 '파일 3개 중'이라고 하지 않는다(검증 지적 9). 잘린 항목은 '이름 — 이유'로, 끝 표시가 없다는 전체 문제도 함께.
    /// </summary>
    [Theory]
    [InlineData("egg")]
    [InlineData("alz")]
    public async Task 데이터_안에서_잘린_EGG_ALZ의_요약은_확인한_파일_수로(string kind)
    {
        using var tmp = new TempDir("rvhead");
        var whole = SyntheticArchives.Build(kind, tmp.Dir("원본"));
        byte[] b = File.ReadAllBytes(whole.Path);
        string cut = tmp.P("반쯤" + Path.GetExtension(whole.Path));
        File.WriteAllBytes(cut, b[..(b.Length * 6 / 10)]);   // 난수.bin(200,000B) 데이터 한가운데

        var report = await Test(cut);

        Assert.False(report.Ok);
        var entry = Assert.Single(report.Problems, q => q.Entry.Length > 0);
        Assert.Equal("자료/하위/난수.bin", entry.Entry);
        Assert.Contains(report.Problems, q => q.Entry.Length == 0 && q.Kind == ProblemKind.Truncated && q.Reason.EndsWith(EggAlzArchiveService.TruncatedListNote, StringComparison.Ordinal));
        Assert.Equal(2, report.CheckedCount);   // 견적서.txt·빈파일.txt
        Assert.Equal("문제가 있어요. 확인한 파일 3개 중 1개에 문제가 있어요.", report.Headline);
    }

    /// <summary>
    /// 계약: 분할 EGG 의 마지막 조각이 끝 표시 직전에서 잘렸으면(항목은 모두 온전) 시험·풀기 모두 '그 조각이 덜 받아졌어요'(조각 이름) —
    /// 고치기는 권하지 않는다(조각을 다시 받아야 함).
    /// </summary>
    [Fact]
    public async Task 분할_EGG의_마지막_조각이_잘리면_조각이_덜_받아졌다고_알린다()
    {
        using var tmp = new TempDir("rvsplit");
        var items = new List<RawEgg.Item>
        {
            new("자료/견적서.txt", Encoding.UTF8.GetBytes("분할 견적서 내용 " + new string('가', 2000))),
            new("자료/표.csv", Encoding.UTF8.GetBytes(string.Concat(Enumerable.Range(0, 400).Select(i => $"{i},{i * 3}\n")))),
            new("readme.txt", Encoding.ASCII.GetBytes("split readme")),
        };
        var parts = RawEgg.WriteSplit(Path.Combine(tmp.Dir("받은 것"), "분할"), items, parts: 3);
        Assert.True((await Test(parts[0])).Ok, "온전한 분할 EGG 가 문제없지 않음");
        byte[] last = File.ReadAllBytes(parts[^1]);
        File.WriteAllBytes(parts[^1], last[..^4]);   // 보관 끝 표시(4바이트)만 빠짐

        var report = await Test(parts[0]);
        Assert.False(report.Ok);
        Assert.Equal(items.Count, report.CheckedCount);
        var p = Assert.Single(report.Problems);
        Assert.Equal(ProblemKind.VolumeIncomplete, p.Kind);
        Assert.Contains(Path.GetFileName(parts[^1]), p.Reason);
        Assert.False(report.HasDamage);

        var ex = await Svc.Catch(() => Svc.For(parts[0]).ExtractAsync(parts[0], tmp.P("풀린 곳")).WaitAsync(Limit));
        var vi = Assert.IsType<VolumeIncompleteException>(ex);
        Assert.Contains(Path.GetFileName(parts[^1]), vi.Message);
    }

    // ───────────── 2·5. ZIP 고치기 — 머리 크기가 커진 항목·안쪽 ZIP ─────────────

    /// <summary>
    /// 계약(I4-1 정직한 보수 모드): 한 항목의 로컬 머리 압축 크기가 다음 항목까지 덮도록 커졌어도(Deflate 는 남는 바이트를 무시해 확인을 통과함) 다음 항목을 삼키지 않는다.
    /// 목록이 있으면 목록 값으로 모두 살린다(암호 ZIP 은 암호를 넣어 확인). 목록이 없으면 커진 항목은 '파일의 끝이 맞지 않아요'로 못 살린 것이 되고,
    /// 그 뒤 머리들은 목록이 없어 믿을 수 없으니 넣지 않고 조각 수로만 알린다(예전: 머리 크기를 고쳐 쓰며 모두 살림 — 추측). 암호 ZIP 을 암호 없이 옮길 때도 삼키지 않는다.
    /// </summary>
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task 머리_크기가_커진_항목이_다음_항목을_삼키지_않는다(bool encrypted, bool withCd)
    {
        using var tmp = new TempDir("rvcsize");
        string zip;
        List<ExpectedEntry> expected;
        string? password = null;
        if (encrypted)
        {
            var fx = FixtureCatalog.Get("zip_zipcrypto");
            zip = tmp.P("암호 원본.zip");
            File.Copy(fx.FullPath, zip);
            expected = fx.Expected.ToList();
            password = fx.Password;
        }
        else
        {
            zip = tmp.P("원본.zip");
            expected = new List<ExpectedEntry>();
            using (var fs = File.Create(zip))
            using (var z = new ZipArchive(fs, ZipArchiveMode.Create))
            {
                for (int i = 0; i < 4; i++)
                {
                    byte[] data = Encoding.UTF8.GetBytes(string.Concat(Enumerable.Range(0, 200).Select(n => $"{i}-{n} 내용\n")));
                    var e = z.CreateEntry($"문서{i}.txt", CompressionLevel.Optimal);
                    using (var s = e.Open()) s.Write(data);
                    expected.Add(new ExpectedEntry($"문서{i}.txt", data.Length, TreeAssert.Sha256(data), false));
                }
            }
        }
        byte[] whole = File.ReadAllBytes(zip);
        byte[] b = withCd ? whole.ToArray() : StripCentral(whole);
        var (enlarged, swallowed, before) = EnlargeFirstFileCsize(b);
        string broken = tmp.P("머리 깨짐.zip");
        File.WriteAllBytes(broken, b);

        int files = expected.Count(e => !e.IsDir);
        if (withCd)
        {
            var report = await Repair(broken, tmp.P("머리 깨짐_복구.zip"), password);
            Assert.True(report.CountKnown);
            Assert.Equal($"{files}개를 모두 살렸어요.", report.Headline);
            Assert.True(report.IsComplete);
            string dest = tmp.P("풀어봄");
            await Svc.For(report.OutputPath!).ExtractAsync(report.OutputPath!, dest, password).WaitAsync(Limit);
            TreeAssert.ExtractedTreeEquals(dest, expected, context: encrypted ? "암호" : "일반");
            var test = await Test(report.OutputPath!, password);
            Assert.True(test.Ok, test.ToDisplayText());
            Assert.Equal(files, test.CheckedCount);
            return;
        }

        // 목록 없음 — 암호 ZIP 은 암호 없이(확인 못 함으로 옮기는 길: 커진 크기를 그대로 믿기 쉬운 곳)와 암호를 넣고 둘 다
        foreach (var pw in encrypted ? new[] { null, password } : new string?[] { null })
        {
            var report = await Repair(broken, tmp.P($"머리 깨짐_복구{(pw is null ? "" : "_암호")}.zip"), pw);
            string ctx = encrypted ? (pw is null ? "암호 ZIP·암호 없이" : "암호 ZIP·암호 넣음") : "일반";
            Assert.False(report.CountKnown, ctx);
            Assert.Contains(new RepairLoss(enlarged, "파일의 끝이 맞지 않아요"), report.LostItems);
            var carried = report.Salvaged.Concat(report.Unverified).ToList();
            Assert.DoesNotContain(enlarged, carried);
            Assert.DoesNotContain(swallowed, carried);
            Assert.Empty(carried);   // 커진 항목이 첫 파일 — 그 앞엔 폴더뿐
            Assert.Null(report.OutputPath);
            Assert.Equal("살릴 수 있는 파일을 찾지 못했어요.", report.Headline);
            Assert.Equal(expected.Count - before - 1, report.UnconfirmedPieces);   // 커진 항목 뒤 머리(폴더 포함)는 조각으로만
            Assert.Contains(RepairReport.PiecesNote(report.UnconfirmedPieces), report.ToDisplayText());
        }
    }

    /// <summary>
    /// 계약: 잘린 바깥 ZIP 의 마지막 항목이 저장 방식으로 담긴 안쪽 ZIP 이면 안쪽 항목(같은 이름 readme.txt 포함)을 바깥 것으로 살리지 않는다 —
    /// 바깥 readme.txt 하나만 살리고 안쪽 ZIP 은 못 살린 것(2개 중 1개).
    /// </summary>
    [Fact]
    public async Task 잘린_ZIP_안의_안쪽_ZIP_항목을_바깥_것으로_살리지_않는다()
    {
        using var tmp = new TempDir("rvnest");
        var inner = new MemoryStream();
        using (var z = new ZipArchive(inner, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var n in new[] { "안쪽_견적서.txt", "readme.txt", "안쪽_보고서.txt" })
            {
                var e = z.CreateEntry(n, CompressionLevel.NoCompression);
                using var s = e.Open();
                s.Write(Encoding.UTF8.GetBytes("안쪽 내용 " + n + new string('x', 300)));
            }
            var big = z.CreateEntry("안쪽_난수.bin", CompressionLevel.NoCompression);
            using (var s = big.Open())
            {
                var data = new byte[300_000];
                new Random(9).NextBytes(data);
                s.Write(data);
            }
        }
        string outer = tmp.P("자료 모음.zip");
        byte[] readme = Encoding.UTF8.GetBytes("바깥 readme");
        using (var fs = File.Create(outer))
        using (var z = new ZipArchive(fs, ZipArchiveMode.Create))
        {
            using (var s = z.CreateEntry("readme.txt", CompressionLevel.Optimal).Open()) s.Write(readme);
            using (var s = z.CreateEntry("첨부/안쪽 묶음.zip", CompressionLevel.NoCompression).Open()) s.Write(inner.ToArray());
        }
        byte[] bytes = File.ReadAllBytes(outer);
        var last = ZipInspect.Read(outer).Last();
        int at = CorruptArchiveTests.IndexOf(bytes, last.Data);
        string cut = tmp.P("자료 모음 받다 만.zip");
        File.WriteAllBytes(cut, bytes[..(at + (int)last.CompressedSize - 1000)]);   // 안쪽 ZIP 끝 근처에서 잘림(안쪽 목록 없음)

        var report = await Repair(cut, tmp.P("자료 모음_복구.zip"));

        Assert.False(report.CountKnown);   // 잘려 목록이 없음 — 훑기 모드
        Assert.Equal("원래 몇 개였는지 확실하지 않아요 — 1개를 살렸어요.", report.Headline);
        Assert.Equal(new[] { "readme.txt" }, report.Salvaged);
        Assert.Empty(report.Unverified);
        Assert.Equal(new[] { new RepairLoss("첨부/안쪽 묶음.zip", "끝이 잘렸어요") }, report.LostItems);
        Assert.Equal(4, report.UnconfirmedPieces);   // 안쪽 ZIP 의 부품 머리 4개는 바깥 파일로 넣지 않고 조각 수로만 알림
        Assert.Equal(new[] { "readme.txt" }, ZipInspect.Read(report.OutputPath!).Select(e => e.Name));
        string dest = tmp.P("풀어봄");
        await Svc.For(report.OutputPath!).ExtractAsync(report.OutputPath!, dest).WaitAsync(Limit);
        Assert.Equal(TreeAssert.Sha256(readme), TreeAssert.Sha256(Path.Combine(dest, "readme.txt")));
    }

    // ───────────── 3. 항목이 아주 많은 ZIP ─────────────

    /// <summary>
    /// 계약: 항목 30,000개 ZIP(목록 있음·없음)의 '암호 항목이 있나' 훑기는 작업 스레드에서 빨리 끝나고(예전: 항목마다 1MB 를 새로 읽어 수십 초) 진행을 알리며
    /// 취소된다. 고치기도 같은 창(읽은 구간)을 써 모두 살린다. 화면은 훑는 동안 진행 창(취소 가능)을 띄운다 — [취소]하면 고치지 않는다.
    /// </summary>
    [Fact]
    public async Task 항목이_아주_많은_ZIP도_빨리_훑고_취소된다()
    {
        using var tmp = new TempDir("rvmany");
        string full = tmp.P("작은 파일 많음.zip");
        using (var fs = File.Create(full))
        using (var z = new ZipArchive(fs, ZipArchiveMode.Create))
        {
            for (int i = 0; i < 30_000; i++)
            {
                using var s = z.CreateEntry($"폴더{i / 1000}/작은파일{i:D6}.txt", CompressionLevel.Fastest).Open();
                s.Write(Encoding.UTF8.GetBytes($"내용 {i} " + new string('가', i % 50)));
            }
        }
        string noCd = tmp.P("작은 파일 많음 끊김.zip");
        File.WriteAllBytes(noCd, StripCentral(File.ReadAllBytes(full)));

        foreach (var path in new[] { full, noCd })
        {
            var reports = new ProgressRecorder();
            var sw = Stopwatch.StartNew();
            bool encrypted = await ZipRepairer.Instance.HasEncryptedEntriesAsync(path, reports, CancellationToken.None).WaitAsync(Limit);
            sw.Stop();
            Assert.False(encrypted);
            Assert.True(sw.Elapsed < TimeSpan.FromSeconds(10), $"{Path.GetFileName(path)}: 암호 항목 훑기 {sw.Elapsed.TotalSeconds:F1}초");
            Assert.NotEmpty(reports.Reports);
        }
        using (var cancelled = new CancellationTokenSource())
        {
            cancelled.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => ZipRepairer.Instance.HasEncryptedEntriesAsync(noCd, null, cancelled.Token));
        }

        var watch = Stopwatch.StartNew();
        var repaired = await Repair(noCd, tmp.P("작은 파일 많음_복구.zip"));
        watch.Stop();
        Assert.False(repaired.CountKnown);   // 목록을 떼어 낸 사본 — 훑기 모드
        Assert.Equal(30_000, repaired.Salvaged.Count);
        Assert.Empty(repaired.Lost);
        Assert.Equal(0, repaired.UnconfirmedPieces);
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(45), $"고치기 {watch.Elapsed.TotalSeconds:F1}초");

        // 화면: 훑는 진행 창에서 [취소] → 저장할 곳만 묻고 고치지 않음
        var ui = new FakeUserInteraction { SavePath = tmp.P("화면_복구.zip"), CancelProgress = true };
        var vm = new MainViewModel(ui);
        RepairReport? r = null;
        await VmRunner.Run(async () => r = await vm.RepairZipFileAsync(noCd), "고치기");
        Assert.Null(r);
        Assert.Single(ui.ProgressSessions);
        Assert.Empty(ui.PasswordPrompts);
        Assert.Empty(ui.Infos);
        Assert.Equal("ZIP 고치기를 취소했어요", vm.StatusText);
        Assert.False(File.Exists(tmp.P("화면_복구.zip")));
    }

    // ───────────── 4·7·8. 환경 문제 ─────────────

    /// <summary>(Win32 코드, 기대 종류, 기대 문구의 시작)</summary>
    public static TheoryData<int, ProblemKind, string> OsMessages => new()
    {
        { 112, ProblemKind.DiskFull, ErrorCatalog.DiskFullMessage },
        { 39, ProblemKind.DiskFull, ErrorCatalog.DiskFullMessage },
        { 33, ProblemKind.InUse, ErrorCatalog.InUseMessage },
        { 32, ProblemKind.InUse, ErrorCatalog.InUseMessage },
        { 206, ProblemKind.PathTooLong, "경로가 너무 길어요." },
        { 5, ProblemKind.AccessDenied, ErrorCatalog.AccessDeniedMessage },
    };

    /// <summary>
    /// 계약: .NET 이 운영 체제 문구로 만든 입출력 예외('운영 체제 문구 : '경로'' — 한국어 Windows 면 한국어)는 설계 문구로 바꾼다(검증 지적 4:
    /// 한국어 Windows 에서 '디스크 공간이 부족합니다. : …'가 그대로 보임). 서비스가 쓴 쉬운 한국어 문구는 그대로 살린다(ErrorCatalogTests).
    /// </summary>
    [Theory]
    [MemberData(nameof(OsMessages))]
    public void 운영_체제_문구는_설계_문구로_바꾼다(int code, ProblemKind kind, string message)
    {
        string os = Marshal.GetPInvokeErrorMessage(code);
        foreach (string text in new[] { os, $"{os} : 'D:\\받은 파일\\큰 자료.bin'" })
        {
            var ex = new IOException(text, unchecked((int)0x80070000) | code);
            var p = ErrorCatalog.Describe(ex);
            Assert.Equal(kind, p.Kind);
            Assert.StartsWith(message, p.Message);
            Assert.DoesNotContain(os.TrimEnd(), p.Message);
            Assert.True(ErrorCatalog.IsEnvironmentProblem(ex));
            Assert.False(ArchiveTestService.IsArchiveProblem(ex));
        }
    }

    /// <summary>계약: 진짜 잠금(다른 손잡이가 파일 일부를 잠금 — 보안 프로그램이 검사 중인 것처럼)을 읽다 난 예외는 '다른 프로그램이 쓰고 있어요'(설계 문구).</summary>
    [Fact]
    public void 진짜_잠금_예외는_다른_프로그램이_쓴다고_알린다()
    {
        using var tmp = new TempDir("rvlock");
        string f = tmp.P("잠금 시험.bin");
        File.WriteAllBytes(f, new byte[4096]);
        using var holder = new FileStream(f, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite);
        holder.Lock(0, 4096);
        try
        {
            using var reader = new FileStream(f, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            var ex = Assert.ThrowsAny<IOException>(() => reader.ReadExactly(new byte[100]));
            var p = ErrorCatalog.Describe(ex);
            Assert.Equal(ProblemKind.InUse, p.Kind);
            Assert.StartsWith(ErrorCatalog.InUseMessage, p.Message);
            Assert.Equal(ErrorCatalog.InUseAdvice, p.Advice);
        }
        finally
        {
            holder.Unlock(0, 4096);
        }
    }

    /// <summary>
    /// 계약: 운영 체제가 알린 그 밖의 입출력 오류(네트워크 경로가 끊김·장치 입출력 오류·USB 를 뺌·시간 초과 — 코드 0x8007xxxx 인 IOException)는
    /// 압축 문제가 아니라 환경 문제다(검증 지적 8). 스트림 끝·데이터 오류·코드 없는 입출력 오류는 그대로 압축 문제.
    /// </summary>
    [Fact]
    public void 운영_체제_입출력_오류는_압축_문제가_아니다()
    {
        foreach (int code in new[] { 53, 64, 121, 1117, 31 })
        {
            var ex = new IOException(Marshal.GetPInvokeErrorMessage(code), unchecked((int)0x80070000) | code);
            Assert.True(ErrorCatalog.IsEnvironmentProblem(ex), $"코드 {code}");
            Assert.False(ArchiveTestService.IsArchiveProblem(ex), $"코드 {code}");
        }
        foreach (var ex in new Exception[] { new EndOfStreamException(), new InvalidDataException("Data error"), new IOException("boom"), CorruptArchiveException.Truncated() })
        {
            Assert.False(ErrorCatalog.IsEnvironmentProblem(ex), ex.GetType().Name);
            Assert.True(ArchiveTestService.IsArchiveProblem(ex), ex.GetType().Name);
        }
    }

    /// <summary>
    /// 계약: 시험·고치기 도중 원본의 일부를 읽을 수 없으면(다른 손잡이가 데이터 한가운데를 잠금 — 운영 체제 입출력 오류) '문제가 있어요'·'못 살린 파일'로
    /// 적지 않고 그 예외로 멈춘다 — 반쪽 결과 ZIP·임시 파일을 남기지 않는다.
    /// </summary>
    [Fact]
    public async Task 원본을_읽다_입출력_오류가_나면_반쪽_결과를_만들지_않는다()
    {
        using var tmp = new TempDir("rvioerr");
        var fx = FixtureCatalog.Get("zip_store_l0");
        string src = tmp.P("잠긴 자료.zip");
        File.Copy(fx.FullPath, src);
        var victim = ZipInspect.Read(src).Where(e => !e.IsDir).OrderByDescending(e => e.CompressedSize).First();
        long at = CorruptArchiveTests.IndexOf(File.ReadAllBytes(src), victim.Data);
        Assert.True(at > 0);

        using var holder = new FileStream(src, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        holder.Lock(at + victim.CompressedSize / 2, 16);
        try
        {
            var testEx = await Assert.ThrowsAnyAsync<IOException>(() => Test(src));
            Assert.True(ErrorCatalog.IsEnvironmentProblem(testEx), testEx.ToString());

            string dest = tmp.P("잠긴 자료_복구.zip");
            var repairEx = await Assert.ThrowsAnyAsync<IOException>(() => Repair(src, dest));
            Assert.True(ErrorCatalog.IsEnvironmentProblem(repairEx), repairEx.ToString());
            Assert.False(File.Exists(dest));
            Assert.Equal(new[] { src }, Directory.GetFiles(tmp.Root));
        }
        finally
        {
            holder.Unlock(at + victim.CompressedSize / 2, 16);
        }
    }

    /// <summary>
    /// 계약: 고친 ZIP 의 저장 위치를 다른 프로그램이 열어 두었으면 '다른 프로그램(보안 프로그램 포함)이 파일을 쓰고 있어요'(예전: '권한이 없어요') —
    /// 그 파일은 그대로, 임시 파일도 남지 않는다.
    /// </summary>
    [Fact]
    public async Task 저장_위치를_다른_프로그램이_열어_두면_다른_프로그램이_쓴다고_알린다()
    {
        using var tmp = new TempDir("rvdest");
        string src = tmp.P("끊김.zip");
        File.WriteAllBytes(src, StripCentral(File.ReadAllBytes(FixtureCatalog.Get("zip_default").FullPath)));
        string dest = tmp.P("열린 결과.zip");
        File.WriteAllText(dest, "다른 프로그램이 연 파일");
        using (new FileStream(dest, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            var ex = await Assert.ThrowsAnyAsync<IOException>(() => Repair(src, dest));
            var p = ErrorCatalog.Describe(ex);
            Assert.Equal(ProblemKind.InUse, p.Kind);
            Assert.StartsWith(ErrorCatalog.InUseMessage, p.Message);
        }
        Assert.Equal("다른 프로그램이 연 파일", File.ReadAllText(dest));
        Assert.Empty(Directory.GetFiles(tmp.Root, ".~*", SearchOption.AllDirectories));
    }

    // ───────────── 6. 풀 수 없는 압축 방식 ─────────────

    /// <summary>
    /// 계약: 일집(SharpCompress)이 모르는 압축 방식(97 WavPack — 'Unsupported compression method: 97')의 ZIP 은 손상이 아니다 —
    /// 시험은 항목마다 '이 압축 방식(WavPack)은 일집이 아직 풀 수 없어 확인하지 못했어요', 풀기 오류는 '풀 수 없는 압축 방식', 어느 쪽도 'ZIP 고치기 시도'를 권하지 않는다.
    /// </summary>
    [Fact]
    public async Task 모르는_압축_방식은_손상이_아니고_고치기를_권하지_않는다()
    {
        using var tmp = new TempDir("rvmethod");
        var fx = FixtureCatalog.Get("zip_store_l0");
        byte[] b = File.ReadAllBytes(fx.FullPath);
        int patched = 0;
        for (int i = 0; i + 30 < b.Length; i++)
        {
            uint sig = BinaryPrimitives.ReadUInt32LittleEndian(b.AsSpan(i));
            if (sig == 0x04034b50 && BinaryPrimitives.ReadUInt16LittleEndian(b.AsSpan(i + 8)) == 0 && BinaryPrimitives.ReadUInt32LittleEndian(b.AsSpan(i + 18)) > 0)
            {
                BinaryPrimitives.WriteUInt16LittleEndian(b.AsSpan(i + 8), 97);
                patched++;
            }
            else if (sig == 0x02014b50 && BinaryPrimitives.ReadUInt16LittleEndian(b.AsSpan(i + 10)) == 0 && BinaryPrimitives.ReadUInt32LittleEndian(b.AsSpan(i + 20)) > 0)
            {
                BinaryPrimitives.WriteUInt16LittleEndian(b.AsSpan(i + 10), 97);
            }
        }
        Assert.True(patched > 0);
        string arc = tmp.P("방식97.zip");
        File.WriteAllBytes(arc, b);

        var report = await Test(arc);
        Assert.False(report.Ok);
        Assert.Equal(patched, report.ProblemEntryCount);
        Assert.Equal(fx.ExpectedFiles.Count(), report.CheckedCount + report.ProblemEntryCount);
        Assert.All(report.Problems, p =>
        {
            Assert.Equal(ProblemKind.UnsupportedMethod, p.Kind);
            Assert.Equal("이 압축 방식(WavPack)은 일집이 아직 풀 수 없어 확인하지 못했어요", p.Reason);
        });
        Assert.False(report.HasDamage);

        var ex = await Svc.Catch(() => Svc.For(arc).ExtractAsync(arc, tmp.P("풀린 곳")).WaitAsync(Limit));
        Assert.NotNull(ex);
        var described = ErrorCatalog.Describe(ex!, arc);
        Assert.Equal(ProblemKind.UnsupportedMethod, described.Kind);
        Assert.Equal("이 압축 방식(WavPack)은 아직 풀 수 없어요.", described.Message);
        Assert.Empty(described.Actions);

        var ui = new FakeUserInteraction();
        await VmRunner.Run(() => new MainViewModel(ui).TestArchiveFileAsync(arc), "시험");
        var problem = Assert.Single(ui.Problems);
        Assert.Empty(problem.Actions);
        Assert.Contains("WavPack", problem.Message);
        Assert.Empty(ui.SaveDefaultNames);
    }

    // ───────────── 10. 압축된 TAR 시험은 디스크에 쓰지 않음 ─────────────

    /// <summary>
    /// 계약: .tgz·.tar.gz·.tbz2 의 시험은 바깥 압축을 흘려 풀며 안쪽 TAR 를 읽는다 — 일집 임시 폴더(목록 캐시 temp\list)에도 아무것도 쓰지 않는다
    /// (예전: 안쪽 TAR 전체를 거기에 풀어 큰 tgz 는 C: 에 그만큼 자리가 필요했다). 결과는 예전과 같다(문제없어요·파일 수).
    /// </summary>
    [Theory]
    [InlineData("tgz_default")]
    [InlineData("targz_default")]
    [InlineData("tbz2_python")]
    public async Task 압축된_TAR_시험은_임시_폴더에도_쓰지_않는다(string id)
    {
        var fx = FixtureCatalog.Get(id);
        using var tmp = new TempDir("rvtgz");
        string arc = tmp.P("새로 받은 " + Path.GetFileName(fx.File));   // 목록 캐시가 없는 새 경로
        File.Copy(fx.FullPath, arc);
        string appRoot = tmp.Dir("일집 자료");
        using (AppPaths.OverrideRootForTests(appRoot))
        {
            var report = await Test(arc);
            Assert.True(report.Ok, report.ToDisplayText());
            Assert.Equal(fx.ExpectedFiles.Count(), report.CheckedCount);
            if (Directory.Exists(AppPaths.TempRoot))
                Assert.Fail("시험이 일집 임시 폴더에 씀: " + string.Join(", ", Directory.EnumerateFileSystemEntries(AppPaths.TempRoot, "*", SearchOption.AllDirectories)));
        }
    }

    /// <summary>
    /// 계약: 흘려 읽는 시험도 큰 압축된 TAR(수 MB — 형식 알아보기 버퍼보다 큼)의 파일 수를 맞게 세고, 바깥 압축의 데이터가 깨지면 '문제가 있어요'(손상 — 고치기 대상),
    /// 끝이 잘리면 '잘려 있어요'. 일집 임시 폴더에 쓰지 않는다.
    /// </summary>
    [Theory]
    [InlineData("gz")]
    [InlineData("bz2")]
    public async Task 큰_압축된_TAR도_흘려_읽어_검사한다(string outer)
    {
        using var tmp = new TempDir("rvbigtgz");
        var rnd = new Random(21);
        var files = new (string Name, byte[] Data)[]
        {
            ("big/random.bin", Bytes(rnd, 3_000_000)),
            ("zeros.bin", new byte[1_500_000]),
            ("hello.txt", Encoding.ASCII.GetBytes("hello streaming tar")),
        };
        var tar = new MemoryStream();
        using (var w = new TarWriter(tar, TarEntryFormat.Ustar, leaveOpen: true))
        {
            foreach (var (name, data) in files)
                w.WriteEntry(new UstarTarEntry(TarEntryType.RegularFile, name) { DataStream = new MemoryStream(data) });
        }
        string arc = tmp.P(outer == "gz" ? "큰 묶음.tar.gz" : "큰 묶음.tar.bz2");
        using (var fs = File.Create(arc))
        {
            if (outer == "gz")
            {
                using var gz = new GZipStream(fs, CompressionLevel.Fastest);
                gz.Write(tar.ToArray());
            }
            else
            {
                using var bz = new ICSharpCode.SharpZipLib.BZip2.BZip2OutputStream(fs);
                bz.Write(tar.ToArray());
            }
        }
        string appRoot = tmp.Dir("일집 자료");
        using (AppPaths.OverrideRootForTests(appRoot))
        {
            var good = await Test(arc, null, new ProgressRecorder());
            Assert.True(good.Ok, good.ToDisplayText());
            Assert.Equal(files.Length, good.CheckedCount);

            byte[] b = File.ReadAllBytes(arc);
            string broken = tmp.P("깨진" + Path.GetFileName(arc));
            var damaged = (byte[])b.Clone();
            for (int i = b.Length / 2; i < b.Length / 2 + 32; i++) damaged[i] ^= 0x5A;
            File.WriteAllBytes(broken, damaged);
            var bad = await Test(broken);
            Assert.False(bad.Ok);
            Assert.True(bad.HasDamage, bad.ToDisplayText());

            string cut = tmp.P("잘린" + Path.GetFileName(arc));
            File.WriteAllBytes(cut, b[..(b.Length * 2 / 3)]);
            var truncated = await Test(cut);
            Assert.False(truncated.Ok);
            Assert.Equal(ProblemKind.Truncated, Assert.Single(truncated.Problems).Kind);

            Assert.False(Directory.Exists(AppPaths.TempRoot), "시험이 일집 임시 폴더에 씀");
        }
    }

    // ───────────── 도우미 ─────────────

    private static byte[] Bytes(Random rnd, int n)
    {
        var b = new byte[n];
        rnd.NextBytes(b);
        return b;
    }

    private static int LastIndexOf(byte[] hay, byte[] needle)
    {
        for (int i = hay.Length - needle.Length; i >= 0; i--)
            if (hay.AsSpan(i, needle.Length).SequenceEqual(needle)) return i;
        return -1;
    }

    /// <summary>끝의 목록(중앙 디렉터리)과 끝 머리를 잘라 낸 사본(ZIP64 아님).</summary>
    internal static byte[] StripCentral(byte[] b)
    {
        for (int i = b.Length - 22; i >= 0; i--)
        {
            if (BinaryPrimitives.ReadUInt32LittleEndian(b.AsSpan(i)) != 0x06054b50) continue;
            long cd = BinaryPrimitives.ReadUInt32LittleEndian(b.AsSpan(i + 16));
            return b[..(int)cd];
        }
        throw new InvalidDataException("끝 머리 없음");
    }

    /// <summary>
    /// ZIP 바이트에서 첫 '파일' 항목(폴더·크기 0 빼고)의 로컬 머리 압축 크기를 다음 항목 기록 하나만큼 키운다 — 다음 항목을 삼키는 모양.
    /// 커진 항목과 삼켜지는 항목의 이름, 커진 항목 앞의 항목(폴더·빈 파일) 수를 돌려준다(원래 목록 기준 이름 — CP949·UTF-8 은 이름 바이트로 판단).
    /// </summary>
    private static (string Enlarged, string Swallowed, int Before) EnlargeFirstFileCsize(byte[] b)
    {
        int before = 0;
        static string NameOf(byte[] b, int at)
        {
            int flags = BinaryPrimitives.ReadUInt16LittleEndian(b.AsSpan(at + 6));
            byte[] nb = b.AsSpan(at + 30, BinaryPrimitives.ReadUInt16LittleEndian(b.AsSpan(at + 26))).ToArray();
            return (flags & 0x0800) != 0 ? Encoding.UTF8.GetString(nb) : KoreanFileNameDecoder.DecodeBytes(nb, 0, nb.Length);
        }
        int at = 0;
        while (true)
        {
            Assert.Equal(0x04034b50u, BinaryPrimitives.ReadUInt32LittleEndian(b.AsSpan(at)));
            int nameLen = BinaryPrimitives.ReadUInt16LittleEndian(b.AsSpan(at + 26));
            int extraLen = BinaryPrimitives.ReadUInt16LittleEndian(b.AsSpan(at + 28));
            uint cs = BinaryPrimitives.ReadUInt32LittleEndian(b.AsSpan(at + 18));
            bool dir = b[at + 30 + nameLen - 1] == (byte)'/';
            int next = at + 30 + nameLen + extraLen + (int)cs;
            if (!dir && cs > 0)
            {
                int nNameLen = BinaryPrimitives.ReadUInt16LittleEndian(b.AsSpan(next + 26));
                int nExtraLen = BinaryPrimitives.ReadUInt16LittleEndian(b.AsSpan(next + 28));
                uint ncs = BinaryPrimitives.ReadUInt32LittleEndian(b.AsSpan(next + 18));
                BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(at + 18), cs + (uint)(30 + nNameLen + nExtraLen) + ncs);
                return (NameOf(b, at), NameOf(b, next), before);
            }
            at = next;
            before++;
        }
    }
}
