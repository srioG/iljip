using System.Buffers.Binary;
using System.Text;
using Iljip.Services;
using Iljip.Tests.TestSupport;
using Iljip.ViewModels;

namespace Iljip.Tests.Contract;

/// <summary>
/// [W2-08 — I3-12, I4-1 정직한 보수 모드] 깨진 ZIP 고치기(<see cref="ZipRepairer"/>): 끝의 목록(중앙 디렉터리)을 믿을 수 있으면 목록 모드('전체 N개 중 M개'),
/// 없으면 맨 앞부터 끝이 데이터로 확인된 사슬만 따라가는 훑기 모드('원래 몇 개였는지 확실하지 않아요 — M개를 살렸어요'). 살릴 수 있는 항목만 새 ZIP('이름_복구.zip')으로 옮긴다.
/// 원본은 바이트 하나 바뀌지 않는다.
/// 결과는 일집(주 처리기 — SharpCompress), 7z.dll(.zipx 로 열어 봄), 반디집(bz.exe t — 있을 때만)이 모두 문제없이 푼다.
/// 재료는 반디집 픽스처(목록·데이터를 테스트가 직접 망가뜨림)와 데이터 서술자 ZIP(<see cref="ZipInspect.WriteWithDataDescriptors"/>).
/// </summary>
public class ZipRepairTests
{
    private static readonly TimeSpan Limit = TimeSpan.FromSeconds(60);

    private static Task<RepairReport> Repair(string src, string dest, string? password = null) =>
        ZipRepairer.Instance.RepairAsync(src, dest, password, null, CancellationToken.None).WaitAsync(Limit);

    // ───────────── 망가뜨리기 도우미 ─────────────

    /// <summary>원래 목록에서 읽은 항목 자리(로컬 머리 위치·데이터 시작·압축 크기).</summary>
    private sealed record Local(string Name, long Offset, long DataStart, long CSize, bool IsDir);

    /// <summary>중앙 디렉터리 시작 위치와 항목 자리들(로컬 머리 위치 순).</summary>
    private static (long CdOffset, List<Local> Entries) Layout(byte[] b)
    {
        int eocd = -1;
        for (int i = b.Length - 22; i >= 0; i--)
            if (U32(b, i) == 0x06054b50) { eocd = i; break; }
        Assert.True(eocd >= 0, "끝 머리 없음");
        int count = U16(b, eocd + 10);
        long cd = U32(b, eocd + 16);
        var list = new List<Local>();
        int p = (int)cd;
        for (int n = 0; n < count; n++)
        {
            Assert.Equal(0x02014b50u, U32(b, p));
            int flags = U16(b, p + 8);
            long csize = U32(b, p + 20);
            int nameLen = U16(b, p + 28), extraLen = U16(b, p + 30), commentLen = U16(b, p + 32);
            long offset = U32(b, p + 42);
            byte[] nameBytes = b.AsSpan(p + 46, nameLen).ToArray();
            string name = (flags & 0x0800) != 0 ? Encoding.UTF8.GetString(nameBytes) : KoreanFileNameDecoder.DecodeBytes(nameBytes, 0, nameBytes.Length);
            int lName = U16(b, (int)offset + 26), lExtra = U16(b, (int)offset + 28);
            list.Add(new Local(name, offset, offset + 30 + lName + lExtra, csize, name.EndsWith('/')));
            p += 46 + nameLen + extraLen + commentLen;
        }
        return (cd, list.OrderBy(e => e.Offset).ToList());
    }

    /// <summary>끝의 목록(중앙 디렉터리)과 끝 머리를 통째로 잘라 낸 사본 — 내려받다 끊긴 ZIP 의 흔한 모양.</summary>
    private static string WithoutCentral(string source, string to)
    {
        var b = File.ReadAllBytes(source);
        var (cd, _) = Layout(b);
        File.WriteAllBytes(to, b[..(int)cd]);
        return to;
    }

    private static List<ExpectedEntry> Without(IEnumerable<ExpectedEntry> expected, params string[] paths) =>
        expected.Where(e => !paths.Contains(e.Path)).ToList();

    /// <summary>살린 항목(폴더 포함)과 살린 파일의 위 폴더만 남긴 기대(훑기 모드에서 앞쪽만 살렸을 때).</summary>
    private static List<ExpectedEntry> Kept(IEnumerable<ExpectedEntry> expected, IEnumerable<Local> kept)
    {
        var names = kept.Select(e => e.Name.TrimEnd('/')).ToHashSet(StringComparer.Ordinal);
        var files = kept.Where(e => !e.IsDir).Select(e => e.Name).ToList();
        return expected.Where(e => names.Contains(e.Path) || e.IsDir && files.Any(f => f.StartsWith(e.Path + "/", StringComparison.Ordinal))).ToList();
    }

    /// <summary>결과 ZIP 을 주 처리기(SharpCompress)·7z.dll(.zipx 로 복사해 열기)로 풀어 기대와 같은지, 7z.dll 시험도 통과하는지.</summary>
    /// <param name="nfc">NFD(자모 분리) 이름 ZIP — 이름을 NFC 로 바꿔 비교(이름 자체는 I2-10, BandizipFixtureTests 와 같게).</param>
    private static async Task AssertOpensEverywhere(TempDir tmp, string repaired, IEnumerable<ExpectedEntry> expected, string? password, string context, bool nfc = false)
    {
        var exp = expected.ToList();
        string main = tmp.Dir("풀어봄_주_" + Guid.NewGuid().ToString("N")[..6]);
        await Svc.For(repaired).ExtractAsync(repaired, main, password).WaitAsync(Limit);
        TreeAssert.ExtractedTreeEquals(main, exp, nfc, context: context + " (주 처리기)");

        string zipx = tmp.P(Path.GetFileNameWithoutExtension(repaired) + "_" + Guid.NewGuid().ToString("N")[..6] + ".zipx");
        File.Copy(repaired, zipx);
        Assert.IsType<SevenZipFormatsArchiveService>(Svc.For(zipx));
        string seven = tmp.Dir("풀어봄_7z_" + Guid.NewGuid().ToString("N")[..6]);
        await Svc.For(zipx).ExtractAsync(zipx, seven, password).WaitAsync(Limit);
        TreeAssert.ExtractedTreeEquals(seven, exp, nfc, context: context + " (7z.dll)");

        var report = await ArchiveTestService.Instance.TestAsync(zipx, password, null, CancellationToken.None).WaitAsync(Limit);
        Assert.True(report.Ok, $"{context}: 7z.dll 시험 실패 — {report.ToDisplayText()}");
        Assert.Equal(exp.Count(e => !e.IsDir), report.CheckedCount);
        File.Delete(zipx);
    }

    // ───────────── 목록이 없어진 ZIP ─────────────

    /// <summary>
    /// 계약: 끝의 목록이 통째로 없어진 ZIP(반디집 기본·CP949 이름·UTF-8 이름·저장 방식·뿌리 폴더 없음)에서 모두 살린다 — 못 살린 것 0, 확인 못 한 것 0, 조각 0.
    /// 결과는 원래 압축과 같게 풀린다(이름·내용·빈 폴더). 원본은 그대로. 기본 이름은 '이름_복구.zip'.
    /// 목록이 없으면 원래 파일 수를 알 수 없어(항목 경계에서 잘린 받다 만 파일과 겉모습이 같음) 요약은 I4-1 훑기 모드 문구
    /// '원래 몇 개였는지 확실하지 않아요 — N개를 살렸어요.' + 덧말(예전 '9개를 모두 살렸어요.' — 잘린 파일에서도 같은 말이라 거짓이 될 수 있음).
    /// </summary>
    [Theory]
    [InlineData("zip_default")]
    [InlineData("zip_cp949")]
    [InlineData("zip_utf8")]
    [InlineData("zip_store_l0")]
    [InlineData("zip_storeroot_no")]
    [InlineData("nfd_python_noflag")]
    public async Task 목록이_없어도_모두_살린다(string id)
    {
        var fx = FixtureCatalog.Get(id);
        using var tmp = new TempDir("rpcd");
        string broken = WithoutCentral(fx.FullPath, tmp.P("받다 만 자료.zip"));
        Assert.NotNull(await Svc.Catch(() => Svc.For(broken).ListEntriesAsync(broken)));   // 일집은 그대로는 못 연다
        string sha = TreeAssert.Sha256(broken);
        string dest = tmp.P(ZipRepairer.SuggestedName(broken));
        Assert.Equal("받다 만 자료_복구.zip", Path.GetFileName(dest));

        var report = await Repair(broken, dest);

        Assert.Empty(report.Lost);
        Assert.Empty(report.Unverified);
        int files = fx.ExpectedFiles.Count();
        Assert.False(report.CountKnown);
        Assert.Equal(0, report.OriginalCount);
        Assert.Equal(0, report.UnconfirmedPieces);
        Assert.Equal(files, report.Salvaged.Count);
        Assert.Equal(dest, report.OutputPath);
        Assert.Equal($"원래 몇 개였는지 확실하지 않아요 — {files}개를 살렸어요.", report.Headline);
        Assert.Contains(RepairReport.UncertainNote, report.ToDisplayText());
        Assert.Contains("원래 파일은 그대로예요.", report.ToDisplayText());
        Assert.Equal(sha, TreeAssert.Sha256(broken));
        await AssertOpensEverywhere(tmp, dest, fx.Expected, null, id, nfc: id.StartsWith("nfd_", StringComparison.Ordinal));
        Assert.Empty(Directory.GetFiles(tmp.Root, ".~*", SearchOption.AllDirectories));   // 임시 파일이 남지 않음
    }

    /// <summary>계약: 목록이 멀쩡한 ZIP 도 고치기는 그대로 다 옮긴다(목록 모드 — 원래 목록의 속성을 씀, 'N개를 모두 살렸어요.'). 원본은 그대로.</summary>
    [Fact]
    public async Task 멀쩡한_ZIP도_그대로_옮긴다()
    {
        var fx = FixtureCatalog.Get("zip_default");
        using var tmp = new TempDir("rpok");
        string src = tmp.P("멀쩡.zip");
        File.Copy(fx.FullPath, src);
        var report = await Repair(src, tmp.P("멀쩡_복구.zip"));
        Assert.Empty(report.Lost);
        Assert.Equal(fx.ExpectedFiles.Count(), report.Salvaged.Count);
        Assert.True(report.CountKnown);
        Assert.True(report.IsComplete);
        Assert.Equal($"{fx.ExpectedFiles.Count()}개를 모두 살렸어요.", report.Headline);
        Assert.Equal(TreeAssert.Sha256(fx.FullPath), TreeAssert.Sha256(src));
        await AssertOpensEverywhere(tmp, tmp.P("멀쩡_복구.zip"), fx.Expected, null, "멀쩡");
    }

    // ───────────── 데이터가 깨진 ZIP ─────────────

    /// <summary>
    /// 계약: 한 항목의 압축된 데이터 가운데가 깨지면 — 목록이 있으면(목록 모드) 그 항목만 못 살리고 나머지는 모두 살린다
    /// '전체 9개 중 8개를 살렸어요. 못 살린 파일: - 이름 — 내용이 원래와 달라요'. 목록이 없으면(훑기 모드, 두 겹 손상) 그 앞의 파일만 살리고
    /// 그 뒤의 머리는 믿지 않아 '목록에 없는 조각 K개'로 수만 알린다(I4-1 — 확인 실패한 항목의 끝은 믿지 않음: 안쪽 부품을 바깥 파일로 넣던 갈래를 막는 값).
    /// </summary>
    [Theory]
    [InlineData("zip_default", true)]
    [InlineData("zip_default", false)]
    [InlineData("zip_cp949", false)]
    [InlineData("zip_store_l0", false)]
    public async Task 데이터가_깨진_항목만_못_살린다(string id, bool keepCentral)
    {
        var fx = FixtureCatalog.Get(id);
        using var tmp = new TempDir("rpdata");
        var b = File.ReadAllBytes(fx.FullPath);
        var (cd, entries) = Layout(b);
        var victim = entries.Where(e => !e.IsDir && e.CSize > 64).OrderByDescending(e => e.CSize).First();
        long mid = victim.DataStart + victim.CSize / 2;
        for (long i = mid; i < mid + 16; i++) b[i] ^= 0x5A;
        if (!keepCentral) b = b[..(int)cd];
        string broken = tmp.P("깨진 자료.zip");
        File.WriteAllBytes(broken, b);
        string sha = TreeAssert.Sha256(broken);

        var report = await Repair(broken, tmp.P("깨진 자료_복구.zip"));

        int files = fx.ExpectedFiles.Count();
        Assert.Equal(new[] { victim.Name }, report.Lost);
        Assert.DoesNotContain(victim.Name, report.Salvaged);
        Assert.Contains("못 살린 파일:\n- " + victim.Name + " — 내용이 원래와 달라요", report.ToDisplayText());
        Assert.Equal(sha, TreeAssert.Sha256(broken));
        if (keepCentral)
        {
            Assert.True(report.CountKnown);
            Assert.Equal(files, report.OriginalCount);
            Assert.Equal(files - 1, report.Salvaged.Count);
            Assert.Equal($"전체 {files}개 중 {files - 1}개를 살렸어요.", report.Headline);
            await AssertOpensEverywhere(tmp, tmp.P("깨진 자료_복구.zip"), Without(fx.Expected, victim.Name), null, $"{id} 깨짐");
            return;
        }
        var before = entries.Where(e => e.Offset < victim.Offset).ToList();
        var after = entries.Where(e => e.Offset > victim.Offset).ToList();
        Assert.False(report.CountKnown);
        Assert.Equal(before.Where(e => !e.IsDir).Select(e => e.Name).OrderBy(n => n, StringComparer.Ordinal), report.Salvaged.OrderBy(n => n, StringComparer.Ordinal));
        Assert.Equal(after.Count, report.UnconfirmedPieces);
        Assert.Equal($"원래 몇 개였는지 확실하지 않아요 — {report.Salvaged.Count}개를 살렸어요.", report.Headline);
        Assert.Contains(RepairReport.PiecesNote(after.Count), report.ToDisplayText());
        await AssertOpensEverywhere(tmp, tmp.P("깨진 자료_복구.zip"), Kept(fx.Expected, before), null, $"{id} 깨짐·목록 없음");
    }

    /// <summary>
    /// 계약: 파일 한가운데에서 잘린 ZIP(받다 만 파일 — 흔함) — 잘린 자리 앞의 항목은 모두 살리고, 잘린 항목은 '끝이 잘렸어요'로 못 살린 것으로 센다(그 뒤 항목은 남아 있지 않음).
    /// 원래 몇 개였는지는 모른다(I4-1 훑기 모드 요약).
    /// </summary>
    [Theory]
    [InlineData("zip_default")]
    [InlineData("zip_utf8")]
    public async Task 잘린_ZIP은_앞_항목을_살린다(string id)
    {
        var fx = FixtureCatalog.Get(id);
        using var tmp = new TempDir("rptrunc");
        var b = File.ReadAllBytes(fx.FullPath);
        var (_, entries) = Layout(b);
        var files = entries.Where(e => !e.IsDir && e.CSize > 64).ToList();
        var cutAt = files[files.Count / 2];   // 가운데 항목의 데이터 한가운데에서 자른다
        string broken = tmp.P("잘린 자료.zip");
        File.WriteAllBytes(broken, b[..(int)(cutAt.DataStart + cutAt.CSize / 2)]);

        var report = await Repair(broken, tmp.P("잘린 자료_복구.zip"));

        var before = entries.Where(e => e.Offset < cutAt.Offset).ToList();
        Assert.Equal(before.Where(e => !e.IsDir).Select(e => e.Name).OrderBy(n => n, StringComparer.Ordinal),
            report.Salvaged.OrderBy(n => n, StringComparer.Ordinal));
        Assert.Equal(new[] { new RepairLoss(cutAt.Name, "끝이 잘렸어요") }, report.LostItems);
        Assert.False(report.CountKnown);
        Assert.Equal(0, report.UnconfirmedPieces);
        Assert.Equal($"원래 몇 개였는지 확실하지 않아요 — {report.Salvaged.Count}개를 살렸어요.", report.Headline);
        await AssertOpensEverywhere(tmp, tmp.P("잘린 자료_복구.zip"), Kept(fx.Expected, before), null, $"{id} 잘림");
    }

    /// <summary>
    /// 계약: 데이터 서술자(플래그 bit 3 — 머리에 크기가 0, 데이터 뒤에 서술자; 서명 있는 것·없는 것 섞임) ZIP 의 목록이 없어져도 모두 살린다.
    /// 빈 파일·데이터 안에 ZIP 서명 바이트가 든 파일도 경계를 맞게 찾는다.
    /// </summary>
    [Fact]
    public async Task 데이터_서술자_ZIP도_목록_없이_살린다()
    {
        using var tmp = new TempDir("rpdd");
        var rnd = new Random(11);
        var big = new byte[70_000];
        rnd.NextBytes(big);
        var tricky = Encoding.UTF8.GetBytes("앞부분 PK\u0003\u0004 가짜 머리 PK\u0007\u0008 가짜 서술자 PK\u0001\u0002 끝").Concat(new byte[40]).ToArray();
        var items = new (string Name, byte[] Data)[]
        {
            ("견적서.txt", Encoding.UTF8.GetBytes("견적서 내용 " + new string('가', 300))),
            ("빈 파일.txt", Array.Empty<byte>()),
            ("자료/난수.bin", big),
            ("자료/서명 든 파일.dat", tricky),
            ("마지막.txt", Encoding.UTF8.GetBytes("끝 항목")),
        };
        string full = tmp.P("스트리밍.zip");
        ZipInspect.WriteWithDataDescriptors(full, items);
        string broken = WithoutCentral(full, tmp.P("스트리밍 끊김.zip"));

        var report = await Repair(broken, tmp.P("스트리밍_복구.zip"));

        Assert.Empty(report.Lost);
        Assert.Empty(report.Unverified);
        Assert.Equal(items.Select(i => i.Name), report.Salvaged);
        var expected = items.Select(i => new ExpectedEntry(i.Name, i.Data.Length, TreeAssert.Sha256(i.Data), false))
            .Append(new ExpectedEntry("자료", 0, null, true)).ToList();
        await AssertOpensEverywhere(tmp, tmp.P("스트리밍_복구.zip"), expected, null, "데이터 서술자");
    }

    // ───────────── 암호 ZIP ─────────────

    /// <summary>
    /// 계약: 암호 ZIP(ZipCrypto·AES-256, 영어·한글 암호 — 반디집 픽스처)의 목록이 없어져도 암호를 주면 내용까지 확인하고 모두 살린다.
    /// 결과는 같은 암호로 풀린다.
    /// </summary>
    [Theory]
    [InlineData("zip_zipcrypto")]
    [InlineData("zip_zipcrypto_korean_pw")]
    [InlineData("zip_aes256")]
    [InlineData("zip_aes256_korean_pw")]
    public async Task 암호_ZIP도_암호를_주면_모두_살린다(string id)
    {
        var fx = FixtureCatalog.Get(id);
        using var tmp = new TempDir("rppw");
        string broken = WithoutCentral(fx.FullPath, tmp.P("암호 자료.zip"));
        Assert.True(ZipRepairer.Instance.HasEncryptedEntries(broken));
        string sha = TreeAssert.Sha256(broken);

        var report = await Repair(broken, tmp.P("암호 자료_복구.zip"), fx.Password);

        Assert.Empty(report.Lost);
        Assert.Empty(report.Unverified);
        Assert.False(report.CountKnown);
        Assert.Equal(fx.ExpectedFiles.Count(), report.Salvaged.Count);
        Assert.Equal(sha, TreeAssert.Sha256(broken));
        await AssertOpensEverywhere(tmp, tmp.P("암호 자료_복구.zip"), fx.Expected, fx.Password, id);
    }

    /// <summary>계약: 반디집(bz.exe t)도 고친 암호 ZIP·데이터가 깨졌던 ZIP 을 문제없이 푼다(있을 때만).</summary>
    [BandizipCliFact]
    public async Task 반디집도_고친_ZIP을_푼다()
    {
        using var tmp = new TempDir("rpbz");
        foreach (var id in new[] { "zip_default", "zip_cp949", "zip_zipcrypto", "zip_zipcrypto_korean_pw", "zip_aes256", "zip_aes256_korean_pw" })
        {
            var fx = FixtureCatalog.Get(id);
            string broken = WithoutCentral(fx.FullPath, tmp.P(id + "_끊김.zip"));
            string dest = tmp.P(id + "_복구.zip");
            var report = await Repair(broken, dest, fx.Password);
            Assert.Empty(report.Lost);
            Assert.Equal(0, await BandizipCli.TestAsync(dest, fx.Password ?? string.Empty));
        }

        var b = File.ReadAllBytes(FixtureCatalog.Get("zip_default").FullPath);
        var (_, entries) = Layout(b);
        var victim = entries.Where(e => !e.IsDir).OrderByDescending(e => e.CSize).First();
        b[(int)(victim.DataStart + victim.CSize / 2)] ^= 0xFF;
        string damaged = tmp.P("깨진.zip");
        File.WriteAllBytes(damaged, b);
        Assert.NotEqual(0, await BandizipCli.TestAsync(damaged, string.Empty));
        var r = await Repair(damaged, tmp.P("깨진_복구.zip"));
        Assert.Single(r.Lost);
        Assert.Equal(0, await BandizipCli.TestAsync(tmp.P("깨진_복구.zip"), string.Empty));
    }

    /// <summary>
    /// 계약: 암호를 주지 않으면 암호 항목은 내용을 확인하지 못하고 그대로 옮긴다('확인 못 함' 목록 — I4-1: '살렸어요' 수에는 넣지 않음) — 결과는 원래 암호로 풀린다.
    /// 틀린 암호를 주면(어느 암호 항목과도 맞지 않음) 암호 예외(화면이 다시 묻는다 — '손상일 수도' 덧말).
    /// </summary>
    [Fact]
    public async Task 암호_없이는_그대로_옮기고_틀리면_다시_묻게()
    {
        var fx = FixtureCatalog.Get("zip_aes256");
        using var tmp = new TempDir("rpnopw");
        string broken = WithoutCentral(fx.FullPath, tmp.P("암호.zip"));

        var report = await Repair(broken, tmp.P("암호_복구.zip"));
        // 반디집은 빈 파일에는 암호를 걸지 않는다 — 확인 못 한 것은 암호가 걸린 파일만(빈 파일은 확인됨)
        var encrypted = ZipInspect.Read(fx.FullPath).Where(e => e.Encrypted && !e.IsDir).Select(e => e.Name).OrderBy(n => n, StringComparer.Ordinal).ToList();
        Assert.NotEmpty(encrypted);
        Assert.Empty(report.Lost);
        Assert.Equal(fx.ExpectedFiles.Count() - encrypted.Count, report.Salvaged.Count);
        Assert.Equal(encrypted, report.Unverified.OrderBy(n => n, StringComparer.Ordinal));
        Assert.DoesNotContain(report.Salvaged, encrypted.Contains);
        Assert.Equal($"원래 몇 개였는지 확실하지 않아요 — {report.Salvaged.Count}개를 살렸어요.", report.Headline);
        Assert.Contains($"내용을 확인하지 못하고 그대로 옮긴 파일 {encrypted.Count}개", report.ToDisplayText());
        await AssertOpensEverywhere(tmp, tmp.P("암호_복구.zip"), fx.Expected, fx.Password, "암호 없이");

        var wrong = await Assert.ThrowsAsync<WrongPasswordException>(() => Repair(broken, tmp.P("암호_복구2.zip"), "틀린암호"));
        Assert.True(wrong.MayBeCorrupt);
        Assert.False(File.Exists(tmp.P("암호_복구2.zip")));
    }

    // ───────────── 지키는 것 ─────────────

    /// <summary>계약: 원본과 같은 경로에는 저장하지 않는다(덮어써서 원본을 잃지 않게) — 쉬운 한국어 거절.</summary>
    [Fact]
    public async Task 원본에_덮어쓰지_않는다()
    {
        using var tmp = new TempDir("rpsame");
        string src = tmp.P("자료.zip");
        File.Copy(FixtureCatalog.Get("zip_default").FullPath, src);
        string sha = TreeAssert.Sha256(src);
        var ex = await Assert.ThrowsAsync<ArchiveEditException>(() => Repair(src, tmp.P("자료.ZIP")));
        Assert.Equal(ZipRepairer.SameAsSourceMessage, ex.Message);
        Assert.Equal(sha, TreeAssert.Sha256(src));
    }

    /// <summary>계약: 살릴 것이 하나도 없으면(ZIP 머리가 없는 쓰레기) 새 ZIP 을 만들지 않는다 — '살릴 수 있는 파일을 찾지 못했어요.', 임시 파일도 없음.</summary>
    [Fact]
    public async Task 살릴_것이_없으면_만들지_않는다()
    {
        using var tmp = new TempDir("rpnone");
        string src = tmp.P("쓰레기.zip");
        File.WriteAllBytes(src, ArchiveTesterTests.Damage(Array.Empty<byte>(), "garbage"));
        var report = await Repair(src, tmp.P("쓰레기_복구.zip"));
        Assert.Null(report.OutputPath);
        Assert.Empty(report.Salvaged);
        Assert.Empty(report.Unverified);
        Assert.False(report.CountKnown);
        Assert.Equal("살릴 수 있는 파일을 찾지 못했어요.", report.Headline);
        Assert.Contains("새 ZIP 은 만들지 않았어요. 원래 파일은 그대로예요.", report.ToDisplayText());
        Assert.Equal(new[] { src }, Directory.GetFiles(tmp.Root));
    }

    /// <summary>계약: 취소하면 결과 파일·임시 파일을 남기지 않는다.</summary>
    [Fact]
    public async Task 취소하면_아무것도_남기지_않는다()
    {
        using var tmp = new TempDir("rpcancel");
        string broken = WithoutCentral(FixtureCatalog.Get("zip_default").FullPath, tmp.P("자료.zip"));
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => ZipRepairer.Instance.RepairAsync(broken, tmp.P("자료_복구.zip"), null, null, cts.Token));
        Assert.Equal(new[] { broken }, Directory.GetFiles(tmp.Root));
    }

    // ───────────── 화면 흐름(가짜 창) ─────────────

    /// <summary>
    /// 계약: 잘린 ZIP 을 열면 문제 창('파일을 열 수 없어요.' — 쉬운 한국어, 원문은 '자세히')에 'ZIP 고치기 시도'가 있고, 고르면 저장할 곳(기본 '이름_복구.zip')을 묻고
    /// 고친 뒤 결과 창. 목록이 없는 ZIP 이라 원래 개수를 몰라 '확실하지 않아요' — 안내 창이 아니라 주의 창(I4-1 F2). 원본은 그대로.
    /// </summary>
    [Fact]
    public async Task 화면에서_열다_실패하면_ZIP_고치기를_고를_수_있다()
    {
        using var tmp = new TempDir("rpvm");
        string broken = WithoutCentral(FixtureCatalog.Get("zip_default").FullPath, tmp.P("메일 첨부.zip"));
        string sha = TreeAssert.Sha256(broken);
        var ui = new FakeUserInteraction { SavePath = tmp.P("메일 첨부_복구.zip") };
        ui.ProblemActionAnswers.Enqueue(ErrorCatalog.RepairZipActionId);
        var vm = new MainViewModel(ui);
        await VmRunner.Run(() => vm.OpenArchiveAsync(broken), "열기");

        var problem = Assert.Single(ui.Problems);
        Assert.Equal("파일을 열 수 없어요.", problem.Heading);
        Assert.True(ErrorCatalog.ContainsHangul(problem.Message));
        Assert.DoesNotContain("Exception", problem.ToDisplayText());
        Assert.False(string.IsNullOrEmpty(problem.Details));
        Assert.Equal("ZIP 고치기 시도", Assert.Single(problem.Actions).Label);
        Assert.Equal("메일 첨부_복구.zip", Assert.Single(ui.SaveDefaultNames));
        Assert.Empty(ui.Infos);
        string warning = Assert.Single(ui.Warnings);
        Assert.Contains("‘메일 첨부.zip’ 고치기 결과", warning);
        // 목록이 없는 ZIP — 원래 파일 수를 몰라 '모두'라고 하지 않는다
        Assert.Contains($"원래 몇 개였는지 확실하지 않아요 — {FixtureCatalog.Get("zip_default").ExpectedFiles.Count()}개를 살렸어요.", warning);
        Assert.Contains(RepairReport.UncertainNote, warning);
        Assert.True(File.Exists(tmp.P("메일 첨부_복구.zip")));
        Assert.Equal(sha, TreeAssert.Sha256(broken));
    }

    /// <summary>계약: 문제 창에서 고치기를 고르지 않으면(창만 닫음) 아무것도 만들지 않는다. 암호 ZIP 은 고치기 전에 암호를 한 번 묻는다.</summary>
    [Fact]
    public async Task 화면에서_고르지_않으면_고치지_않고_암호_ZIP은_암호를_묻는다()
    {
        using var tmp = new TempDir("rpvm2");
        string broken = WithoutCentral(FixtureCatalog.Get("zip_default").FullPath, tmp.P("첨부.zip"));
        var ui = new FakeUserInteraction();
        await VmRunner.Run(() => new MainViewModel(ui).OpenArchiveAsync(broken), "열기");
        Assert.Single(ui.Problems);
        Assert.Empty(ui.SaveDefaultNames);
        Assert.Equal(new[] { broken }, Directory.GetFiles(tmp.Root));

        var fx = FixtureCatalog.Get("zip_zipcrypto_korean_pw");
        string pwBroken = WithoutCentral(fx.FullPath, tmp.P("암호 첨부.zip"));
        var ui2 = new FakeUserInteraction(fx.Password) { SavePath = tmp.P("암호 첨부_복구.zip") };
        RepairReport? report = null;
        await VmRunner.Run(async () => report = await new MainViewModel(ui2).RepairZipFileAsync(pwBroken), "고치기");
        var ask = Assert.Single(ui2.PasswordPrompts);
        Assert.Equal(PasswordPromptReason.Required, ask.Reason);
        Assert.Equal(MainViewModel.RepairPasswordMessage, ask.Message);
        Assert.NotNull(report);
        Assert.Empty(report!.Lost);
        Assert.Empty(report.Unverified);
    }

    private static ushort U16(byte[] b, int at) => BinaryPrimitives.ReadUInt16LittleEndian(b.AsSpan(at));
    private static uint U32(byte[] b, int at) => BinaryPrimitives.ReadUInt32LittleEndian(b.AsSpan(at));
}
