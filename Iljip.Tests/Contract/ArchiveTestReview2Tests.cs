using System.Buffers.Binary;
using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Text;
using Iljip.Models;
using Iljip.Services;
using Iljip.Tests.TestSupport;
using Iljip.ViewModels;
using SharpZip = ICSharpCode.SharpZipLib.Zip;

namespace Iljip.Tests.Contract;

/// <summary>
/// [W2-08 검증 지적 2차] 깨진 ZIP 고치기(I3-12)·압축 검사(I2-7)·오류 원인 문구(I2-8)의 빈틈을 고친 것을 지킨다.
/// <list type="number">
/// <item>흘려 쓴 ZIP64 데이터 서술자(SharpZipLib 기본값 — 24바이트)를 16바이트로 잘못 읽어 하나도 못 살림.</item>
/// <item>읽히지만 깨진 목록(위치·디스크 번호 칸·서명·이름)을 그대로 믿어 멀쩡한 파일을 잃거나 빠뜨리고도 '모두 살렸어요'. 나눠진 ZIP 은 고른 조각 하나만 훑음.</item>
/// <item>목록 없는 ZIP 에서 끝을 모르는 항목(서술자가 깨짐) 하나에 멈춰 뒤의 멀쩡한 파일을 세지도 살리지도 않음.</item>
/// <item>목록이 깨져 파일이 빠지거나 다른 내용으로 풀리는 ZIP 을 검사가 '문제없어요'로, 풀기는 오류 없이(종료 코드 0) 끝냄.</item>
/// <item>잠긴 .bz2 를 '형식이 아니거나 손상'으로, 엔진이 감싼 환경 오류를 '손상'으로, 압축 실패에 풀기 문구.</item>
/// <item>그 밖의 운영 체제 오류(네트워크·장치·이름 — 이름 한 조각이 255자를 넘는 것 포함)의 운영 체제 문구가 화면에 그대로.</item>
/// <item>로컬 머리의 이름·크기만 깨지면 목록이 온전해도 깨진 이름을 쓰거나 그 항목을 잃음.</item>
/// <item>같은 .tar.xz 를 동시에 검사하면 목록 캐시가 쓰던 안쪽 TAR 를 지워 '찾을 수 없어요'.</item>
/// </list>
/// </summary>
public class ArchiveTestReview2Tests
{
    private static readonly TimeSpan Limit = TimeSpan.FromSeconds(90);

    /// <summary>재사용장치가 '압축 파일 아님'으로 분류하는 말(ErrorCatalogTests 와 같게).</summary>
    private static readonly string[] NotArchiveMarkers = { "압축 파일이 아닌", "압축파일이 아닌", "not archive", "not an archive", "unsupported archive" };

    private static Task<TestReport> Test(string path, string? password = null) =>
        ArchiveTestService.Instance.TestAsync(path, password, null, CancellationToken.None).WaitAsync(Limit);

    private static Task<RepairReport> Repair(string src, string dest, string? password = null) =>
        ZipRepairer.Instance.RepairAsync(src, dest, password, null, CancellationToken.None).WaitAsync(Limit);

    // ───────────── 1. 흘려 쓴 ZIP64 데이터 서술자 ─────────────

    /// <summary>
    /// 계약: SharpZipLib 가 되감지 못하는 스트림에 쓴 ZIP(기본값 Dynamic·ZIP64 강제 — 머리 크기 0·ZIP64 추가 필드·24바이트 서술자, '.NET 서버의 모두 내려받기' 모양)도
    /// 목록이 있든 없든 모두 살리고 원래 내용으로 풀린다(목록이 없으면 '원래 몇 개였는지 확실하지 않아요'). 60% 에서 잘렸으면 앞의 두 파일을 살리고 잘린 셋째만
    /// '못 살린 파일 — 파일의 끝을 찾지 못했어요'(예전: 하나도 못 살림).
    /// </summary>
    [Theory]
    [InlineData(SharpZip.UseZip64.Dynamic)]
    [InlineData(SharpZip.UseZip64.On)]
    public async Task 흘려_쓴_ZIP64_서술자도_모두_살린다(SharpZip.UseZip64 mode)
    {
        using var tmp = new TempDir("rv2z64");
        string whole = tmp.P("모두 내려받기.zip");
        var expected = new Dictionary<string, string>(StringComparer.Ordinal);
        using (var fs = File.Create(whole))
        using (var zos = new SharpZip.ZipOutputStream(new ForwardOnlyStream(fs)) { IsStreamOwner = false, UseZip64 = mode })
        {
            zos.SetLevel(6);
            for (int i = 0; i < 4; i++)
            {
                string name = $"폴더/파일{i}.txt";
                zos.PutNextEntry(new SharpZip.ZipEntry(name) { IsUnicodeText = true, DateTime = new DateTime(2026, 10, 1, 9, 0, 0) });
                byte[] data = Encoding.UTF8.GetBytes(string.Concat(Enumerable.Repeat($"{i} 한글 데이터 PK\x07\x08 ", 3000)));
                zos.Write(data, 0, data.Length);
                zos.CloseEntry();
                expected[name] = TreeAssert.Sha256(data);
            }
            zos.Finish();
        }
        byte[] b = File.ReadAllBytes(whole);
        Assert.True((U16(b, 6) & 0x08) != 0, "데이터 서술자 ZIP 이 아님");
        Assert.True(U16(b, 28) >= 20, "로컬 머리에 ZIP64 추가 필드가 없음");

        // 목록까지 온전한 것·목록이 없어진 것: 모두
        byte[] noCd = WithoutCentral(b);
        string cut = tmp.P("목록 없음.zip");
        File.WriteAllBytes(cut, noCd);
        foreach (var src in new[] { whole, cut })
        {
            var report = await Repair(src, tmp.P(Path.GetFileNameWithoutExtension(src) + "_복구.zip"));
            Assert.True(report.Lost.Count == 0, $"{Path.GetFileName(src)}: {report.ToDisplayText()}");
            Assert.Equal(4, report.Salvaged.Count);
            // 목록이 없으면 원래 파일 수를 몰라 '모두'라고 하지 않는다(I4-1 훑기 모드)
            Assert.Equal(src == whole ? "4개를 모두 살렸어요." : "원래 몇 개였는지 확실하지 않아요 — 4개를 살렸어요.", report.Headline);
            SameFiles(expected, await ExtractFiles(report.OutputPath!, tmp.P("풀어봄_" + Path.GetFileNameWithoutExtension(src))), Path.GetFileName(src));
        }

        // 60% 에서 잘림: 셋째 항목 데이터 안
        var heads = IndexesOf(noCd, new byte[] { 0x50, 0x4B, 0x03, 0x04 });
        Assert.Equal(4, heads.Count);
        int at = noCd.Length * 6 / 10;
        Assert.InRange(at, heads[2] + 1, heads[3] - 1);
        string truncated = tmp.P("받다 만.zip");
        File.WriteAllBytes(truncated, noCd[..at]);
        var part = await Repair(truncated, tmp.P("받다 만_복구.zip"));
        Assert.False(part.CountKnown);
        Assert.Equal(new[] { "폴더/파일0.txt", "폴더/파일1.txt" }, part.Salvaged);
        Assert.Equal(new[] { new RepairLoss("폴더/파일2.txt", "파일의 끝을 찾지 못했어요") }, part.LostItems);
        Assert.Equal(0, part.UnconfirmedPieces);
        SameFiles(expected.Where(kv => kv.Key != "폴더/파일2.txt" && kv.Key != "폴더/파일3.txt").ToDictionary(kv => kv.Key, kv => kv.Value),
            await ExtractFiles(part.OutputPath!, tmp.P("풀어봄_잘림")), "잘림");
    }

    // ───────────── 2. 읽히지만 깨진 목록 — 고치기 ─────────────

    /// <summary>목록(중앙 디렉터리)을 망가뜨리는 방법들(5개 ZIP — 셋째 '문서/견적서2.txt' 의 기록을 주로).</summary>
    private static void Damage(byte[] b, List<CdRec> cds, string variant)
    {
        switch (variant)
        {
            case "위치가 파일 밖": BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(cds[2].RecPos + 42), 0x7FFFFF00); break;
            case "위치가 다른 항목과 같음": BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(cds[2].RecPos + 42), (uint)cds[1].LocalOffset); break;
            case "위치가 데이터 한가운데": BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(cds[2].RecPos + 42), (uint)cds[0].LocalOffset + 40); break;
            case "모든 위치가 1씩 밀림":
                foreach (var c in cds) BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(c.RecPos + 42), (uint)c.LocalOffset + 1);
                break;
            case "디스크 번호 칸 깨짐": b[cds[2].RecPos + 34] ^= 0xFF; break;
            case "첫 목록 서명 깨짐": b[cds[0].RecPos] ^= 0xFF; break;
            case "목록 서명 하나 깨짐": b[cds[3].RecPos] ^= 0xFF; break;
            case "목록 이름 깨짐": b[cds[2].RecPos + 46 + Encoding.UTF8.GetByteCount("문서/견적서")] ^= 0xFF; break;   // '2' → 글자로 읽히지 않는 바이트
            case "항목 수 칸만 틀림":
                int e = Eocd(b);
                BinaryPrimitives.WriteUInt16LittleEndian(b.AsSpan(e + 8), 9);
                BinaryPrimitives.WriteUInt16LittleEndian(b.AsSpan(e + 10), 9);
                break;
            default: throw new ArgumentException(variant);
        }
    }

    /// <summary>
    /// 계약(I4-1 정직한 보수 모드): 목록이 읽히지만 깨진 ZIP(위치가 파일 밖·다른 항목과 같음·데이터 한가운데·모두 밀림, 디스크 번호 칸, 기록 서명, 이름, 항목 수 칸).
    /// 목록을 믿을 수 있으면(목록 모드) 원래 수 5개를 지키고, 기록이 가리키는 머리가 없거나 이름이 다른 파일만 이유와 함께 '못 살린 파일' — 로컬 머리를 훑어 대신 찾지 않는다
    /// (예전: 로컬 머리로 '모두 살렸어요' — 어느 쪽이 맞는지 추측). 목록을 믿을 수 없으면(모두 밀려 첫 항목 앞에 머리가 있음·기록 서명이 깨져 끝 머리에 정확히 닿지 않음)
    /// 훑기 모드로 앞에서부터 확인되는 파일만 살리고 '원래 몇 개였는지 확실하지 않아요'. 어느 쪽이든 살린 파일은 원래 이름·내용 그대로이고 일집·7z.dll 이 문제없이 푼다.
    /// </summary>
    [Theory]
    [InlineData("위치가 파일 밖", true, "문서/견적서2.txt", "파일 머리를 찾지 못했어요")]
    [InlineData("위치가 다른 항목과 같음", true, "문서/견적서2.txt", "이름 정보가 서로 달라요")]
    [InlineData("위치가 데이터 한가운데", true, "문서/견적서2.txt", "파일 머리를 찾지 못했어요")]
    [InlineData("모든 위치가 1씩 밀림", false, null, null)]
    [InlineData("디스크 번호 칸 깨짐", true, null, null)]
    [InlineData("첫 목록 서명 깨짐", false, null, null)]   // 마지막 파일 끝 뒤가 서명이 아니지만 검사값으로 끝이 정확히 확인됨
    [InlineData("목록 서명 하나 깨짐", false, null, null)]
    [InlineData("목록 이름 깨짐", true, "(이름을 읽을 수 없는 파일 — 목록 3번째)", "이름 정보가 서로 달라요")]
    [InlineData("항목 수 칸만 틀림", true, null, null)]
    public async Task 목록이_깨진_ZIP을_고치면_믿을_수_있는_만큼만_살린다(string variant, bool listMode, string? lost, string? reason)
    {
        using var tmp = new TempDir("rv2cd");
        string orig = tmp.P("원본.zip");
        var expected = MakeZip(orig, 5, size: 20000);
        byte[] b = File.ReadAllBytes(orig);
        Damage(b, ReadCd(b), variant);
        string broken = tmp.P("목록 깨짐.zip");
        File.WriteAllBytes(broken, b);
        string before = TreeAssert.Sha256(broken);

        var report = await Repair(broken, tmp.P("목록 깨짐_복구.zip"));

        string ctx = $"{variant}: {report.ToDisplayText()}";
        Assert.True(listMode == report.CountKnown, ctx);
        // 못 살린 파일이 이름을 읽을 수 없는 기록이면 원래 이름은 견적서2(셋째)
        string? lostOriginal = lost is null ? null : lost.StartsWith('(') ? "문서/견적서2.txt" : lost;
        var kept = expected.Where(kv => kv.Key != lostOriginal).ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.Ordinal);
        Assert.True((lost is null ? Array.Empty<RepairLoss>() : new[] { new RepairLoss(lost, reason) }).SequenceEqual(report.LostItems), ctx);
        Assert.Equal(kept.Keys.OrderBy(k => k, StringComparer.Ordinal), report.Salvaged.OrderBy(k => k, StringComparer.Ordinal));
        Assert.Empty(report.Unverified);
        Assert.Equal(listMode ? (lost is null ? "5개를 모두 살렸어요." : "전체 5개 중 4개를 살렸어요.") : $"원래 몇 개였는지 확실하지 않아요 — {kept.Count}개를 살렸어요.", report.Headline);
        Assert.Equal(before, TreeAssert.Sha256(broken));
        SameFiles(kept, await ExtractFiles(report.OutputPath!, tmp.P("풀어봄")), variant);

        string zipx = tmp.P("7z로 열어봄.zipx");
        File.Copy(report.OutputPath!, zipx);
        var seven = await Test(zipx);
        Assert.True(seven.Ok, $"{variant}: 7z.dll 시험 — {seven.ToDisplayText()}");
        Assert.Equal(kept.Count, seven.CheckedCount);
    }

    /// <summary>
    /// 계약: 목록에는 있는데 로컬 머리를 찾지 못한 파일(머리 서명 4바이트가 모두 깨짐)은 '못 살린 파일'로 센다 —
    /// '전체 5개 중 4개를 살렸어요. 못 살린 파일: - 문서/견적서2.txt — 파일 머리를 찾지 못했어요'. 나머지 넷은 원래 내용 그대로.
    /// 서명이 한 바이트만 깨졌으면 목록이 가리키는 바로 그 자리의 같은 이름 머리라 목록 값으로 확인해 살린다(I4-1 ③).
    /// </summary>
    [Theory]
    [InlineData(4)]
    [InlineData(1)]
    public async Task 목록에만_있고_찾지_못한_파일은_못_살린_파일로_센다(int brokenBytes)
    {
        using var tmp = new TempDir("rv2lost");
        string orig = tmp.P("원본.zip");
        var expected = MakeZip(orig, 5, size: 20000);
        byte[] b = File.ReadAllBytes(orig);
        var cds = ReadCd(b);
        for (int k = 0; k < brokenBytes; k++) b[cds[2].LocalOffset + k] ^= 0xFF;
        string broken = tmp.P("머리 서명 깨짐.zip");
        File.WriteAllBytes(broken, b);

        var report = await Repair(broken, tmp.P("머리 서명 깨짐_복구.zip"));

        Assert.True(report.CountKnown);
        Assert.Equal(5, report.OriginalCount);
        if (brokenBytes == 1)
        {
            Assert.Equal("5개를 모두 살렸어요.", report.Headline);
            SameFiles(expected, await ExtractFiles(report.OutputPath!, tmp.P("풀어봄")), "서명 한 바이트 깨짐");
            return;
        }
        Assert.Equal(new[] { new RepairLoss("문서/견적서2.txt", "파일 머리를 찾지 못했어요") }, report.LostItems);
        Assert.Equal(4, report.Salvaged.Count);
        Assert.Equal("전체 5개 중 4개를 살렸어요.", report.Headline);
        Assert.Contains("- 문서/견적서2.txt — 파일 머리를 찾지 못했어요", report.ToDisplayText());
        SameFiles(expected.Where(kv => kv.Key != "문서/견적서2.txt").ToDictionary(kv => kv.Key, kv => kv.Value),
            await ExtractFiles(report.OutputPath!, tmp.P("풀어봄")), "머리 서명 깨짐");
    }

    // ───────────── 2(c). 나눠진 ZIP ─────────────

    /// <summary>
    /// 계약: 나눠진 ZIP(PKZIP 나눔 '.z01·.z02·.zip', 숫자 분할 '.zip.001~003')은 어느 조각을 골라도 조각을 이어서 살핀다 — 파일을 모두 살리고
    /// '나눠진 조각 N개를 이어서 살폈어요.', 제안 이름은 세트 이름('zip_split_복구.zip'). 결과는 원래대로 풀린다(예전: 고른 조각 하나만 훑고 '1개를 모두 살렸어요').
    /// </summary>
    [Theory]
    [InlineData("zip_split", "zip_split.zip")]
    [InlineData("zip_split", "zip_split.z01")]
    [InlineData("zip_numbered_split", "zip_numbered_split.zip.001")]
    [InlineData("zip_numbered_split", "zip_numbered_split.zip.002")]
    public async Task 나눠진_ZIP은_조각을_이어서_살핀다(string id, string pick)
    {
        var fx = FixtureCatalog.Get(id);
        using var tmp = new TempDir("rv2split");
        string dir = tmp.Dir("받은 조각");
        foreach (var v in fx.SplitVolumes) File.Copy(FixtureCatalog.PathOf(v), Path.Combine(dir, Path.GetFileName(v)));
        string picked = Path.Combine(dir, pick);
        Assert.Equal(id + "_복구.zip", ZipRepairer.SuggestedName(picked));

        var report = await Repair(picked, tmp.P(id + "_복구.zip"));

        int files = fx.ExpectedFiles.Count();
        Assert.True(report.Lost.Count == 0, report.ToDisplayText());
        Assert.Equal($"{files}개를 모두 살렸어요.", report.Headline);
        Assert.Equal(fx.SplitVolumes.Count, report.VolumeCount);
        Assert.Contains($"나눠진 조각 {fx.SplitVolumes.Count}개를 이어서 살폈어요.", report.ToDisplayText());
        string dest = tmp.P("풀어봄");
        await Svc.For(report.OutputPath!).ExtractAsync(report.OutputPath!, dest).WaitAsync(Limit);
        TreeAssert.ExtractedTreeEquals(dest, fx.Expected, context: pick);
        foreach (var v in fx.SplitVolumes)
            Assert.Equal(TreeAssert.Sha256(FixtureCatalog.PathOf(v)), TreeAssert.Sha256(Path.Combine(dir, Path.GetFileName(v))));
    }

    /// <summary>
    /// 계약: 나눠진 ZIP 의 조각이 빠졌으면(가운데·앞 조각) 그 조각 이름으로 '조각이 없어요'(<see cref="VolumeMissingException"/>) — 새 ZIP·임시 파일을 만들지 않는다.
    /// </summary>
    [Theory]
    [InlineData("zip_split", "zip_split.z02", "zip_split.zip")]
    [InlineData("zip_split", "zip_split.z01", "zip_split.z02")]
    [InlineData("zip_numbered_split", "zip_numbered_split.zip.002", "zip_numbered_split.zip.001")]
    [InlineData("zip_numbered_split", "zip_numbered_split.zip.001", "zip_numbered_split.zip.003")]
    public async Task 나눠진_ZIP의_조각이_빠지면_그_조각을_알리고_만들지_않는다(string id, string remove, string pick)
    {
        var fx = FixtureCatalog.Get(id);
        using var tmp = new TempDir("rv2gap");
        string dir = tmp.Dir("받은 조각");
        foreach (var v in fx.SplitVolumes)
            if (Path.GetFileName(v) != remove) File.Copy(FixtureCatalog.PathOf(v), Path.Combine(dir, Path.GetFileName(v)));
        string dest = tmp.P(id + "_복구.zip");

        var ex = await Svc.Catch(() => Repair(Path.Combine(dir, pick), dest));

        var missing = Assert.IsType<VolumeMissingException>(ex);
        Assert.Contains(remove, missing.Message);
        Assert.Equal(ProblemKind.VolumeMissing, ErrorCatalog.Describe(missing).Kind);
        Assert.False(File.Exists(dest));
        Assert.Empty(Directory.GetFiles(tmp.Root, ".~*", SearchOption.AllDirectories));
    }

    // ───────────── 3. 끝을 모르는 항목 뒤의 파일 ─────────────

    /// <summary>
    /// 계약(I4-1 훑기 모드): .NET 이 흘려 쓴 목록 없는 ZIP(데이터 서술자 5개)에서 둘째 서술자의 서명·압축 크기·CRC 가 깨지면 둘째의 끝을 믿을 수 없다 —
    /// 첫째만 살리고 둘째는 '파일의 끝을 찾지 못했어요', 그 뒤 머리 셋은 목록이 없어 믿을 수 없으니 넣지 않고 '목록에 없는 조각 3개'로 알린다
    /// (예전: 뒤 머리를 다시 찾아 '찾은 파일'로 살림 — 저장해 담은 문서 속 부품과 가를 수 없음). 새 ZIP 에는 원래와 다른 내용이 없다.
    /// </summary>
    [Theory]
    [InlineData(0)]   // 서명
    [InlineData(8)]   // 압축 크기
    [InlineData(4)]   // CRC
    public async Task 서술자_하나가_깨진_흘려_쓴_ZIP은_앞의_파일만_살리고_뒤는_조각으로_알린다(int at)
    {
        using var tmp = new TempDir("rv2desc");
        string whole = tmp.P("흘려쓰기.zip");
        var expected = new Dictionary<string, string>(StringComparer.Ordinal);
        using (var fs = File.Create(whole))
        using (var z = new ZipArchive(new ForwardOnlyStream(fs), ZipArchiveMode.Create))
        {
            for (int i = 0; i < 5; i++)
            {
                string name = $"폴더/파일{i}.txt";
                byte[] data = Encoding.UTF8.GetBytes(string.Concat(Enumerable.Repeat($"{i} 한글 데이터 {i * 7} ", 2000)));
                using (var s = z.CreateEntry(name).Open()) s.Write(data);
                expected[name] = TreeAssert.Sha256(data);
            }
        }
        byte[] b = File.ReadAllBytes(whole);
        var cds = ReadCd(b);
        byte[] noCd = WithoutCentral(b);
        int desc = (int)cds[2].LocalOffset - 16;   // 둘째 항목의 서술자(서명 있는 16바이트)
        Assert.Equal(0x08074b50u, U32(noCd, desc));
        noCd[desc + at] ^= 0xFF;
        string broken = tmp.P("서술자 깨짐.zip");
        File.WriteAllBytes(broken, noCd);

        var report = await Repair(broken, tmp.P("서술자 깨짐_복구.zip"));

        Assert.False(report.CountKnown);
        Assert.Equal(new[] { "폴더/파일0.txt" }, report.Salvaged);
        Assert.Equal(new[] { new RepairLoss("폴더/파일1.txt", "파일의 끝을 찾지 못했어요") }, report.LostItems);
        Assert.Equal(3, report.UnconfirmedPieces);
        Assert.Equal("원래 몇 개였는지 확실하지 않아요 — 1개를 살렸어요.", report.Headline);
        Assert.Contains(RepairReport.PiecesNote(3), report.ToDisplayText());
        var got = await ExtractFiles(report.OutputPath!, tmp.P("풀어봄"));
        Assert.Equal(report.Salvaged.Count, got.Count);
        foreach (var (name, sha) in got)
            Assert.True(expected.TryGetValue(name, out var want) && want == sha, $"원래와 다른 파일: {name}");
    }

    /// <summary>
    /// 계약(I4-1 훑기 모드): 목록 가운데서 잘린 보통 ZIP(5개)은 앞에서부터 끝이 확인되는 파일을 모두 살린다 — 마지막 파일 끝 뒤가 목록 서명이라 5개 모두
    /// (예전: 사슬이 목록 서명에서 끊겨 '2개 중 1개'). 둘째 머리의 압축 크기 칸이 파일 끝을 넘게 깨졌으면 둘째는 '끝이 잘렸어요', 그 뒤 머리 셋은
    /// 목록이 없어 믿을 수 없으니 조각 수로만 알린다(예전: 다음 머리까지로 크기를 추측해 살림).
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task 목록_가운데서_잘린_ZIP은_앞에서부터_확인되는_파일을_살린다(bool damageSize)
    {
        using var tmp = new TempDir("rv2cdcut");
        string orig = tmp.P("원본.zip");
        var expected = MakeZip(orig, 5, size: 3000);
        byte[] b = File.ReadAllBytes(orig);
        var cds = ReadCd(b);
        byte[] cut = b[..(cds[0].RecPos + 60)];
        if (damageSize) BinaryPrimitives.WriteUInt32LittleEndian(cut.AsSpan((int)cds[1].LocalOffset + 18), 0x7FFFFFF0);
        string broken = tmp.P("목록 중간 잘림.zip");
        File.WriteAllBytes(broken, cut);

        var report = await Repair(broken, tmp.P("목록 중간 잘림_복구.zip"));

        Assert.False(report.CountKnown);
        if (!damageSize)
        {
            Assert.True(report.Lost.Count == 0, report.ToDisplayText());
            Assert.Equal("원래 몇 개였는지 확실하지 않아요 — 5개를 살렸어요.", report.Headline);
            SameFiles(expected, await ExtractFiles(report.OutputPath!, tmp.P("풀어봄")), "잘림만");
            return;
        }
        Assert.Equal(new[] { "문서/견적서0.txt" }, report.Salvaged);
        Assert.Equal(new[] { new RepairLoss("문서/견적서1.txt", "끝이 잘렸어요") }, report.LostItems);
        Assert.Equal(3, report.UnconfirmedPieces);
        SameFiles(expected.Where(kv => kv.Key == "문서/견적서0.txt").ToDictionary(kv => kv.Key, kv => kv.Value),
            await ExtractFiles(report.OutputPath!, tmp.P("풀어봄")), "크기 깨짐");
    }

    // ───────────── 7. 로컬 머리만 깨진 항목 ─────────────

    /// <summary>
    /// 계약: 로컬 머리의 압축 크기만 작게 깨졌으면(목록은 온전 — 다른 프로그램은 목록 값으로 멀쩡히 풂) 목록 값으로 확인해 그 항목도 살린다('4개를 모두 살렸어요').
    /// 목록이 없으면 그 항목의 끝을 믿을 수 없다 — 첫째만 살리고 둘째는 '파일의 끝이 맞지 않아요', 뒤 머리 둘은 조각 수로만(I4-1 — 예전: 다음 머리까지로 추측해 살림).
    /// </summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task 로컬_머리_크기만_작게_깨지면_목록이_있을_때_살린다(bool withCentral)
    {
        using var tmp = new TempDir("rv2small");
        string orig = tmp.P("원본.zip");
        var expected = MakeZip(orig, 4, size: 20000);
        byte[] b = File.ReadAllBytes(orig);
        int lh = (int)ReadCd(b)[1].LocalOffset;
        BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(lh + 18), U32(b, lh + 18) - 50);
        if (!withCentral) b = WithoutCentral(b);
        string broken = tmp.P("머리 크기 작음.zip");
        File.WriteAllBytes(broken, b);

        var report = await Repair(broken, tmp.P("머리 크기 작음_복구.zip"));

        if (withCentral)
        {
            Assert.True(report.Lost.Count == 0, report.ToDisplayText());
            Assert.Equal("4개를 모두 살렸어요.", report.Headline);
            SameFiles(expected, await ExtractFiles(report.OutputPath!, tmp.P("풀어봄")), "목록 있음");
            return;
        }
        Assert.False(report.CountKnown);
        Assert.Equal(new[] { "문서/견적서0.txt" }, report.Salvaged);
        Assert.Equal(new[] { new RepairLoss("문서/견적서1.txt", "파일의 끝이 맞지 않아요") }, report.LostItems);
        Assert.Equal(2, report.UnconfirmedPieces);
        SameFiles(expected.Where(kv => kv.Key == "문서/견적서0.txt").ToDictionary(kv => kv.Key, kv => kv.Value),
            await ExtractFiles(report.OutputPath!, tmp.P("풀어봄")), "목록 없음");
    }

    /// <summary>
    /// 계약(I4-1 ③): 로컬 머리의 이름만 깨졌으면(목록은 온전) 목록과 로컬 머리 가운데 어느 이름이 맞는지 가르지 않는다 — 그 파일은 '이름 정보가 서로 달라요'로
    /// 못 살린 파일, 나머지 셋은 원래대로('전체 4개 중 3개를 살렸어요.'). 깨진 이름으로 쓰지 않고 '모두'라고 하지 않는다. 새 ZIP 의 목록·로컬 이름은 서로 같다.
    /// </summary>
    [Fact]
    public async Task 로컬_머리_이름만_깨지면_이름이_다르다고_알린다()
    {
        using var tmp = new TempDir("rv2name");
        string orig = tmp.P("원본.zip");
        var expected = MakeZip(orig, 4, size: 20000);
        byte[] b = File.ReadAllBytes(orig);
        int lh = (int)ReadCd(b)[1].LocalOffset;
        b[lh + 30 + Encoding.UTF8.GetByteCount("문서/견적서")] ^= 0xFF;
        string broken = tmp.P("머리 이름 깨짐.zip");
        File.WriteAllBytes(broken, b);

        var report = await Repair(broken, tmp.P("머리 이름 깨짐_복구.zip"));

        Assert.True(report.CountKnown);
        Assert.Equal(new[] { new RepairLoss("문서/견적서1.txt", "이름 정보가 서로 달라요") }, report.LostItems);
        Assert.Equal("전체 4개 중 3개를 살렸어요.", report.Headline);
        var kept = expected.Where(kv => kv.Key != "문서/견적서1.txt").ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.Ordinal);
        Assert.Equal(kept.Keys.OrderBy(k => k, StringComparer.Ordinal), report.Salvaged.OrderBy(k => k, StringComparer.Ordinal));
        SameFiles(kept, await ExtractFiles(report.OutputPath!, tmp.P("풀어봄")), "머리 이름 깨짐");
        foreach (var e in ZipInspect.Read(report.OutputPath!))
            Assert.Equal(Convert.ToHexString(e.NameBytes), Convert.ToHexString(e.LocalNameBytes));
    }

    // ───────────── 4. 목록이 깨진 ZIP — 검사·풀기 ─────────────

    /// <summary>
    /// 계약: 목록이 깨져 파일이 빠지거나(기록 서명) 다른 파일 내용이 그 이름으로 풀릴(위치가 다른 항목과 같음·이름 깨짐) ZIP 은
    /// 검사가 '문제가 있어요'(손상 — 'ZIP 고치기 시도'를 권함), 풀기는 맞는 파일만 풀고 손상 오류로 끝난다(<see cref="ZipListDamagedException"/> —
    /// 창 없이 풀기 종료 코드 1, '압축 아님' 말 없음). 원래와 다른 내용의 파일은 남지 않는다(예전: '문제없어요. 파일 0개'·종료 코드 0).
    /// </summary>
    [Theory]
    [InlineData("첫 목록 서명 깨짐")]
    [InlineData("목록 서명 하나 깨짐")]
    [InlineData("위치가 다른 항목과 같음")]
    [InlineData("목록 이름 깨짐")]
    public async Task 목록이_깨진_ZIP은_검사가_문제로_알리고_풀기는_오류로_끝난다(string variant)
    {
        using var tmp = new TempDir("rv2cdtest");
        string orig = tmp.P("원본.zip");
        var expected = MakeZip(orig, 5, size: 3000);
        byte[] b = File.ReadAllBytes(orig);
        Damage(b, ReadCd(b), variant);
        string broken = tmp.P("목록 깨짐.zip");
        File.WriteAllBytes(broken, b);

        var report = await Test(broken);
        Assert.False(report.Ok, $"{variant}: '{report.Headline}'");
        Assert.True(report.HasDamage, report.ToDisplayText());
        Assert.All(report.Problems, p => Assert.Equal(ProblemKind.Corrupt, p.Kind));
        Assert.Contains(report.Problems, p => p.Reason.StartsWith(ZipListDamagedException.MissingMessage, StringComparison.Ordinal) || p.Reason == ZipListDamagedException.EntryReason);
        Assert.True(report.CheckedCount < 5, report.ToDisplayText());
        foreach (var marker in NotArchiveMarkers)
            Assert.DoesNotContain(marker, report.ToDisplayText(), StringComparison.OrdinalIgnoreCase);

        string dest = tmp.P("풀린 곳");
        var ex = await Svc.Catch(() => Svc.For(broken).ExtractAsync(broken, dest).WaitAsync(Limit));
        var damaged = Assert.IsType<ZipListDamagedException>(ex);
        var described = ErrorCatalog.Describe(damaged, broken);
        Assert.Equal(ProblemKind.Corrupt, described.Kind);
        Assert.Contains(described.Actions, a => a.Id == ErrorCatalog.RepairZipActionId);
        Assert.Equal(1, ErrorMessages.HeadlessExitCode(damaged));
        foreach (var marker in NotArchiveMarkers)
            Assert.DoesNotContain(marker, ErrorCatalog.HeadlessMessage(damaged), StringComparison.OrdinalIgnoreCase);
        var got = TreeAssert.Snapshot(dest).Where(kv => kv.Value is not null).ToList();
        Assert.True(got.Count < 5, $"{variant}: 풀린 파일 {got.Count}개");
        foreach (var (name, sha) in got)
            Assert.True(expected.TryGetValue(name, out var want) && want == sha, $"{variant}: 원래와 다른 파일이 풀림 — {name}");

        var ui = new FakeUserInteraction();
        await VmRunner.Run(() => new MainViewModel(ui).TestArchiveFileAsync(broken), "시험");
        var problem = Assert.Single(ui.Problems);
        Assert.Contains(problem.Actions, a => a.Id == ErrorCatalog.RepairZipActionId);
    }

    /// <summary>계약: 끝 머리의 항목 수 칸만 틀리고 목록은 온전하면 손상으로 보지 않는다 — 검사 '문제없어요'(5개), 풀기도 5개 그대로.</summary>
    [Fact]
    public async Task 항목_수_칸만_틀린_ZIP은_문제없다()
    {
        using var tmp = new TempDir("rv2count");
        string orig = tmp.P("원본.zip");
        var expected = MakeZip(orig, 5, size: 3000);
        byte[] b = File.ReadAllBytes(orig);
        Damage(b, ReadCd(b), "항목 수 칸만 틀림");
        string arc = tmp.P("항목 수 틀림.zip");
        File.WriteAllBytes(arc, b);

        var report = await Test(arc);
        Assert.True(report.Ok, report.ToDisplayText());
        Assert.Equal(5, report.CheckedCount);
        SameFiles(expected, await ExtractFiles(arc, tmp.P("풀린 곳")), "항목 수 틀림");
    }

    // ───────────── 5. 잠긴 .bz2·엔진이 감싼 환경 오류·압축 실패 문구 ─────────────

    /// <summary>
    /// 계약: 다른 프로그램이 잡고 있는 .bz2 를 검사·풀면 '다른 프로그램(보안 프로그램 포함)이 파일을 쓰고 있어요'(다른 형식과 같게 — 예전: '올바른 BZIP2 형식이 아니거나 손상').
    /// 검사는 '문제가 있어요'(손상)로 적지 않고 그 예외로 멈춘다.
    /// </summary>
    [Fact]
    public async Task 잠긴_bz2는_다른_프로그램이_쓴다고_알린다()
    {
        var fx = FixtureCatalog.Get("bz2_single_python");
        using var tmp = new TempDir("rv2bz2");
        string arc = tmp.P(Path.GetFileName(fx.File));
        File.Copy(fx.FullPath, arc);
        var svc = Svc.For(arc);
        Assert.IsType<BZip2ArchiveService>(svc);
        using (new FileStream(arc, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            var testEx = await Svc.Catch(() => Test(arc));
            Assert.NotNull(testEx);
            Assert.Equal(ProblemKind.InUse, ErrorCatalog.Describe(testEx!).Kind);

            var extractEx = await Svc.Catch(() => svc.ExtractAsync(arc, tmp.P("풀린 곳")).WaitAsync(Limit));
            Assert.NotNull(extractEx);
            var p = ErrorCatalog.Describe(extractEx!, arc);
            Assert.Equal(ProblemKind.InUse, p.Kind);
            Assert.StartsWith(ErrorCatalog.InUseMessage, p.Message);
        }
    }

    /// <summary>
    /// 계약: 7z.dll 엔진(SharpSevenZip)의 예외가 안쪽에 사용자 환경 문제(공간 부족·잠금·권한)를 담고 있으면 그 원인으로 알린다 — 손상 문구('…다시 받아서 풀어 보세요')가 아니다.
    /// 압축 실패 문구도 같다. 검사는 압축 문제로 적지 않는다.
    /// </summary>
    [Fact]
    public void 엔진이_감싼_환경_오류는_그_원인으로_알린다()
    {
        var types = typeof(SharpSevenZip.SharpSevenZipExtractor).Assembly.GetTypes()
            .Where(t => typeof(Exception).IsAssignableFrom(t) && !t.IsAbstract && t.IsPublic && t.GetConstructor(new[] { typeof(string), typeof(Exception) }) is not null)
            .ToList();
        Assert.True(types.Count >= 3, $"SharpSevenZip 예외 형식 {types.Count}개");
        var inners = new (Exception Inner, ProblemKind Kind)[]
        {
            (new IOException("디스크 공간이 부족합니다.", unchecked((int)0x80070070)), ProblemKind.DiskFull),
            (new IOException("The process cannot access the file because it is being used by another process.", unchecked((int)0x80070020)), ProblemKind.InUse),
            (new UnauthorizedAccessException(@"Access to the path 'C:\Windows\자료.7z' is denied."), ProblemKind.AccessDenied),
        };
        foreach (var t in types)
            foreach (var (inner, kind) in inners)
            {
                var ex = (Exception)Activator.CreateInstance(t, "Execution has failed due to an internal SharpSevenZip exception", inner)!;
                var p = ErrorCatalog.Describe(ex);
                Assert.True(p.Kind == kind, $"{t.Name}({inner.GetType().Name}): {p.Kind} — {p.Message}");
                Assert.DoesNotContain("Execution has failed", p.ToDisplayText());
                Assert.True(ErrorCatalog.IsEnvironmentProblem(ex), t.Name);
                Assert.False(ArchiveTestService.IsArchiveProblem(ex), t.Name);
                Assert.Equal(kind, ErrorCatalog.DescribeCompressFailure(ex).Kind);
                Assert.Equal(p.Message, ErrorMessages.ToCompressMessage(ex));
            }
    }

    /// <summary>
    /// 계약: 압축(만들기)하다 엔진이 낸 데이터 오류·모르는 오류는 '압축하는 중에 알 수 없는 문제가 생겼어요.'(+ 로그 안내) — 풀기 문구('…다시 받아서 풀어 보세요')·
    /// 'ZIP 고치기 시도'가 아니다. 환경 문제는 그 문구 그대로.
    /// </summary>
    [Fact]
    public void 압축_실패에는_풀기_문구가_나오지_않는다()
    {
        foreach (var ex in new Exception[]
                 {
                     new SharpSevenZip.Exceptions.SharpSevenZipException("Execution has failed due to an internal SharpSevenZip exception"),
                     new SharpCompress.Common.ArchiveException("Failed to locate the Zip Header"),
                     new InvalidDataException("Data error"),
                     new InvalidOperationException("boom"),
                 })
        {
            var p = ErrorCatalog.DescribeCompressFailure(ex);
            Assert.Equal(ProblemKind.Unknown, p.Kind);
            Assert.Equal(ErrorCatalog.CompressFailedMessage, p.Message);
            Assert.Equal(ErrorCatalog.UnknownAdvice, p.Advice);
            Assert.Empty(p.Actions);
            Assert.Equal(ErrorCatalog.CompressFailedMessage, ErrorMessages.ToCompressMessage(ex));
            Assert.DoesNotContain("풀어", p.ToDisplayText());
            Assert.Contains(ex.Message, p.Details);
        }
        Assert.Equal(ErrorCatalog.DiskFullMessage, ErrorMessages.ToCompressMessage(new IOException("x", unchecked((int)0x80070070))));
        Assert.StartsWith(ErrorCatalog.InUseMessage, ErrorMessages.ToCompressMessage(new IOException("x", unchecked((int)0x80070020))));
    }

    // ───────────── 6. 그 밖의 운영 체제 오류 ─────────────

    /// <summary>(Win32 코드, 기대 종류, 기대 문구의 시작)</summary>
    public static TheoryData<int, ProblemKind, string> OtherOsCodes => new()
    {
        { 53, ProblemKind.Other, ErrorCatalog.NetworkMessage },     // 네트워크 경로를 찾지 못함
        { 64, ProblemKind.Other, ErrorCatalog.NetworkMessage },     // 네트워크 이름을 더 이상 사용할 수 없음
        { 59, ProblemKind.Other, ErrorCatalog.NetworkMessage },
        { 1231, ProblemKind.Other, ErrorCatalog.NetworkMessage },
        { 21, ProblemKind.Other, ErrorCatalog.DeviceMessage },      // 장치가 준비되지 않음
        { 23, ProblemKind.Other, ErrorCatalog.DeviceMessage },      // 데이터 오류(CRC)
        { 1117, ProblemKind.Other, ErrorCatalog.DeviceMessage },    // 장치 입출력 오류
        { 1392, ProblemKind.Other, ErrorCatalog.DeviceMessage },
        { 123, ProblemKind.Other, ErrorCatalog.BadNameMessage },    // 이름 구문이 잘못됨(이름 조각이 짧음)
        { 161, ProblemKind.Other, ErrorCatalog.BadNameMessage },
        { 121, ProblemKind.Unknown, ErrorCatalog.UnknownMessage },  // 묶음에 없는 번호 — 예전 계약대로 '알 수 없는 문제'
    };

    /// <summary>
    /// 계약: 운영 체제가 알린 그 밖의 입출력 오류(네트워크·장치·이름)는 운영 체제 문구(한국어 Windows 면 한국어, 영어 Windows 면 영어 — 'CRC' 같은 말 포함)를
    /// 그대로 보이지 않고 쉬운 말로(원문은 '자세히'에). 우클릭·여러 개 풀기의 문구도 '파일이 손상됐거나…'가 아니다(압축 파일 탓이 아님).
    /// </summary>
    [Theory]
    [MemberData(nameof(OtherOsCodes))]
    public void 그_밖의_운영_체제_오류도_운영_체제_문구를_보이지_않는다(int code, ProblemKind kind, string message)
    {
        string os = Marshal.GetPInvokeErrorMessage(code).TrimEnd();
        foreach (string text in new[] { os, $"{os} : 'D:\\받은 파일\\자료.zip'" })
        {
            var ex = new IOException(text, unchecked((int)0x80070000) | code);
            var p = ErrorCatalog.Describe(ex);
            Assert.True(p.Kind == kind, $"{code}: {p.Kind}(기대 {kind}) — {p.Message}");
            Assert.StartsWith(message, p.Message);
            string shown = p.ToDisplayText();
            Assert.DoesNotContain(os, shown);
            Assert.DoesNotContain("CRC", shown);
            Assert.Contains(os, p.Details);
            Assert.True(ErrorCatalog.IsEnvironmentProblem(ex), $"코드 {code}");
            string plain = ErrorMessages.ToPlainMessage(ex);
            Assert.DoesNotContain(os, plain);
            Assert.NotEqual(ErrorCatalog.EngineDataMessage, plain);
            Assert.DoesNotContain(os, ErrorCatalog.HeadlessMessage(ex));
        }
    }

    /// <summary>
    /// 계약: 이름 한 조각이 255자를 넘는 항목을 풀다 난 오류(긴 경로를 켠 PC 는 '이름 구문이 잘못됨'(123), 아니면 '경로가 너무 김')는
    /// '경로가 너무 길어요. 더 짧은 위치를 골라 주세요.'(예전: 운영 체제 문구 그대로·종류 '그 밖'). 문구 속 경로가 짧은 123 은 '쓸 수 없는 글자'.
    /// </summary>
    [Fact]
    public async Task 이름_한_조각이_너무_길면_경로가_너무_길다고_알린다()
    {
        string os = Marshal.GetPInvokeErrorMessage(123).TrimEnd();
        var longName = new IOException($"{os} : 'C:\\받은 파일\\{new string('가', 300)}.txt'", unchecked((int)0x8007007B));
        Assert.Equal(ProblemKind.PathTooLong, ErrorCatalog.Describe(longName).Kind);
        Assert.Equal(ErrorCatalog.PathTooLongMessage, ErrorCatalog.Describe(longName).Message);
        var shortName = new IOException($"{os} : 'C:\\받은 파일\\a.txt'", unchecked((int)0x8007007B));
        Assert.StartsWith(ErrorCatalog.BadNameMessage, ErrorCatalog.Describe(shortName).Message);

        using var tmp = new TempDir("rv2long");
        string arc = tmp.P("긴 이름.zip");
        using (var fs = File.Create(arc))
        using (var z = new ZipArchive(fs, ZipArchiveMode.Create))
        using (var s = z.CreateEntry(new string('가', 300) + ".txt").Open())
            s.Write(Encoding.UTF8.GetBytes("긴 이름 내용"));
        var ex = await Svc.Catch(() => Svc.For(arc).ExtractAsync(arc, tmp.P("풀린 곳")).WaitAsync(Limit));
        Assert.NotNull(ex);
        var p = ErrorCatalog.Describe(ex!);
        Assert.True(p.Kind == ProblemKind.PathTooLong, $"{p.Kind} — {p.Message} / {ex!.GetType().Name}: {ex.Message}");
        Assert.Equal(ErrorCatalog.PathTooLongMessage, p.Message);
    }

    // ───────────── 8. 압축된 TAR 목록 캐시 ─────────────

    /// <summary>
    /// 계약: .tar.xz 여러 개를 동시에(같은 것도 여러 번) 검사해도 모두 '문제없어요' — 목록 캐시가 다른 검사가 쓰는 안쪽 TAR 를 지우지 않는다
    /// (예전: 캐시 개수 상한을 넘으면 가장 오래된 폴더를 지워 '파일이나 폴더를 찾을 수 없어요: …\inner.tar').
    /// </summary>
    [Fact]
    public async Task 압축된_TAR를_동시에_검사해도_목록_캐시가_엉키지_않는다()
    {
        var fx = FixtureCatalog.Get("txz_two_step");
        using var tmp = new TempDir("rv2txz");
        using (AppPaths.OverrideRootForTests(tmp.Dir("일집 자료")))
        {
            var copies = Enumerable.Range(0, 6).Select(i =>
            {
                string p = tmp.P($"사본{i}.tar.xz");
                File.Copy(fx.FullPath, p);
                return p;
            }).ToList();
            var tasks = Enumerable.Range(0, 6).SelectMany(_ => copies.Select(c => Task.Run(async () =>
            {
                try
                {
                    var r = await Test(c);
                    return r.Ok ? "OK" : $"{Path.GetFileName(c)}: {r.ToDisplayText()}";
                }
                catch (Exception ex)
                {
                    return $"{Path.GetFileName(c)}: {ex.GetType().Name} {ex.Message}";
                }
            }))).ToList();
            var results = await Task.WhenAll(tasks);
            var bad = results.Where(r => r != "OK").ToList();
            Assert.True(bad.Count == 0, $"{bad.Count}/{results.Length} 실패: {string.Join(" | ", bad.Take(3))}");
        }
    }

    // ───────────── 도우미 ─────────────

    private sealed record CdRec(int RecPos, long LocalOffset, string Name);

    private static ushort U16(byte[] b, int at) => BinaryPrimitives.ReadUInt16LittleEndian(b.AsSpan(at));
    private static uint U32(byte[] b, int at) => BinaryPrimitives.ReadUInt32LittleEndian(b.AsSpan(at));

    private static int Eocd(byte[] b)
    {
        for (int i = b.Length - 22; i >= 0; i--)
            if (U32(b, i) == 0x06054b50) return i;
        throw new InvalidDataException("끝 머리 없음");
    }

    /// <summary>목록 기록들(기록 위치·로컬 머리 위치·이름 — ZIP64 아님).</summary>
    private static List<CdRec> ReadCd(byte[] b)
    {
        int e = Eocd(b);
        int p = (int)U32(b, e + 16);
        int n = U16(b, e + 10);
        var list = new List<CdRec>();
        for (int i = 0; i < n; i++)
        {
            Assert.Equal(0x02014b50u, U32(b, p));
            int nl = U16(b, p + 28), el = U16(b, p + 30), cl = U16(b, p + 32);
            list.Add(new CdRec(p, U32(b, p + 42), Encoding.UTF8.GetString(b, p + 46, nl)));
            p += 46 + nl + el + cl;
        }
        return list;
    }

    /// <summary>끝의 목록과 끝 머리(ZIP64 끝 머리 포함)를 잘라 낸 사본.</summary>
    private static byte[] WithoutCentral(byte[] b)
    {
        int e = Eocd(b);
        long cd = U32(b, e + 16);
        if (cd == 0xFFFFFFFF)
        {
            long z64 = (long)BinaryPrimitives.ReadUInt64LittleEndian(b.AsSpan(e - 20 + 8));
            cd = (long)BinaryPrimitives.ReadUInt64LittleEndian(b.AsSpan((int)z64 + 48));
        }
        return b[..(int)cd];
    }

    private static List<int> IndexesOf(byte[] hay, byte[] needle)
    {
        var list = new List<int>();
        for (int i = 0; i + needle.Length <= hay.Length; i++)
            if (hay.AsSpan(i, needle.Length).SequenceEqual(needle)) list.Add(i);
        return list;
    }

    /// <summary>'문서/견적서N.txt' N개(한글 내용)의 보통 ZIP(System.IO.Compression). 이름 → sha256.</summary>
    private static Dictionary<string, string> MakeZip(string path, int count, int size)
    {
        var rnd = new Random(11);
        var expected = new Dictionary<string, string>(StringComparer.Ordinal);
        using var fs = File.Create(path);
        using var z = new ZipArchive(fs, ZipArchiveMode.Create);
        for (int i = 0; i < count; i++)
        {
            var sb = new StringBuilder();
            while (sb.Length < size) sb.Append($"{i}번 견적 줄 {rnd.Next()} 한글 내용\n");
            byte[] data = Encoding.UTF8.GetBytes(sb.ToString());
            string name = $"문서/견적서{i}.txt";
            using (var s = z.CreateEntry(name, CompressionLevel.Optimal).Open()) s.Write(data);
            expected[name] = TreeAssert.Sha256(data);
        }
        return expected;
    }

    /// <summary>일집으로 풀어 파일(상대 경로 '/' → sha256)을 돌려준다.</summary>
    private static async Task<Dictionary<string, string>> ExtractFiles(string zip, string dest)
    {
        await Svc.For(zip).ExtractAsync(zip, dest).WaitAsync(Limit);
        return TreeAssert.Snapshot(dest).Where(kv => kv.Value is not null).ToDictionary(kv => kv.Key, kv => kv.Value!, StringComparer.Ordinal);
    }

    private static void SameFiles(IReadOnlyDictionary<string, string> expected, IReadOnlyDictionary<string, string> actual, string context)
    {
        var missing = expected.Keys.Where(k => !actual.ContainsKey(k)).ToList();
        var extra = actual.Keys.Where(k => !expected.ContainsKey(k)).ToList();
        var differ = expected.Where(kv => actual.TryGetValue(kv.Key, out var v) && v != kv.Value).Select(kv => kv.Key).ToList();
        Assert.True(missing.Count == 0 && extra.Count == 0 && differ.Count == 0,
            $"{context}: 없음 [{string.Join(", ", missing)}] / 더 있음 [{string.Join(", ", extra)}] / 내용 다름 [{string.Join(", ", differ)}]");
    }

    /// <summary>되감지 못하는 쓰기 전용 스트림(데이터 서술자를 쓰게).</summary>
    private sealed class ForwardOnlyStream : Stream
    {
        private readonly Stream _inner;

        public ForwardOnlyStream(Stream inner) => _inner = inner;

        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => _inner.Position; set => throw new NotSupportedException(); }
        public override void Flush() => _inner.Flush();
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => _inner.Write(buffer, offset, count);
    }
}
