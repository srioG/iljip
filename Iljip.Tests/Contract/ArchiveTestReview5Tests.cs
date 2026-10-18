using System.Buffers.Binary;
using System.Text;
using Iljip.Services;
using Iljip.Tests.TestSupport;
using R3 = Iljip.Tests.Contract.ArchiveTestReview3Tests;
using R4 = Iljip.Tests.Contract.ArchiveTestReview4Tests;

namespace Iljip.Tests.Contract;

/// <summary>
/// [IJ3-02 — 5차 검증에서 막힌 것] 깨진 ZIP 고치기(I3-12)·압축 검사(I2-7)·오류 원인 문구(I2-8):
/// 로컬 머리의 이름 길이 칸 하나가 깨져 파일이 보고에서 사라지던 것((c)), 깨진 목록 이름을 그대로 쓰던 것(유니코드 이름 0x7075 의 증거),
/// 끝 머리의 디스크 번호·주석 길이 칸이 깨지면 구조 검사가 꺼지던 것((e)/(f)), 폴더를 파일로 세던 것, 빈 파일 뒤 머리가 깨지면 빈 파일을 버리던 것,
/// 방식 칸이 깨진 저장 문서를 '확인 못 함'으로 옮기던 것, 따옴표 없이 한글 경로·이름이 든 엔진 영어 문구.
/// </summary>
[Collection(DamageProbeCollection.Name)]
public class ArchiveTestReview5Tests
{
    private static readonly TimeSpan Limit = TimeSpan.FromSeconds(120);

    // ───────────── 도우미 ─────────────

    private static Dictionary<string, string> ExpectedFiles(FixtureInfo fx) =>
        fx.ExpectedFiles.ToDictionary(e => e.Path, e => e.Sha256!, StringComparer.Ordinal);

    /// <summary>압축을 일집으로 풀어 파일(이름 → sha256 — NFC)만.</summary>
    private static async Task<Dictionary<string, string>> ExtractFiles(string arc, string dest, string? password)
    {
        var svc = new ArchiveServiceLocator().ResolveByContent(arc) ?? throw new UnsupportedFormatException(".zip");
        await svc.ExtractAsync(arc, dest, password).WaitAsync(Limit);
        return TreeAssert.Snapshot(dest, nfc: true).Where(kv => kv.Value is not null).ToDictionary(kv => kv.Key, kv => kv.Value!, StringComparer.Ordinal);
    }

    private static string Nfc(string s) => s.Normalize(NormalizationForm.FormC);

    private static byte[] Zero(byte[] src, int at, int count)
    {
        var b = (byte[])src.Clone();
        Array.Clear(b, at, Math.Min(count, b.Length - at));
        return b;
    }

    /// <summary>
    /// 고치기 한 번(맞는 암호)을 <see cref="RepairOracle"/> 의 불변식으로 본다(I4-1 【5】): 살린 파일은 원래 이름·내용, 새 ZIP = 살린 것 + 확인 못 한 것,
    /// 목록 모드면 원래 수 = 목록의 파일 기록 수(폴더를 파일로 세지 않음)·M+U+L=N, '모두'는 원래 파일을 모두 살렸을 때만, 고친 ZIP 은 일집 검사에서 살린 파일에 문제가 없다.
    /// <paramref name="mayLose"/> 밖의 원래 파일은 꼭 살려야 하고, <paramref name="listMode"/> 를 주면 그 모드여야 한다. 목록 모드의 '못 살린 파일'에 폴더('/' 로 끝남)가 없어야 한다.
    /// 문제가 있으면 한 줄 설명, 없으면 null.
    /// </summary>
    private static async Task<string?> CheckKnownRepair(FixtureInfo fx, byte[] broken, string label, string dir, IReadOnlyCollection<string>? mayLose, bool? listMode = true)
    {
        string src = Path.Combine(dir, "망가짐.zip"), dest = Path.Combine(dir, "복구.zip");
        File.WriteAllBytes(src, broken);
        var lose = (mayLose ?? Array.Empty<string>()).Select(Nfc).ToHashSet(StringComparer.Ordinal);
        var expect = new RepairOracle.Expect
        {
            ListMode = listMode,
            MustSalvage = ExpectedFiles(fx).Keys.Select(Nfc).Where(k => !lose.Contains(k)).ToList(),
            TestOutput = true,
        };
        var (f, r, _) = await RepairOracle.RepairCheckAsync(RepairOracle.Of(fx), src, dest, fx.Password, expect, dir);
        if (f is not null) return $"{label}: {f}";
        if (r!.CountKnown && r.LostItems.FirstOrDefault(l => l.Name.EndsWith('/')) is { } folder) return $"{label}: 폴더 '{folder.Name}' 를 못 살린 파일로 셈 — {r.Headline}";
        return null;
    }

    private static async Task RunKnownRepairCases(string tag, FixtureInfo fx, List<(string Label, byte[] Bytes, string[] MayLose)> cases, bool? listMode = true)
    {
        using var tmp = new TempDir(tag);
        var failures = new System.Collections.Concurrent.ConcurrentBag<string>();
        int n = 0;
        await Parallel.ForEachAsync(cases, new ParallelOptions { MaxDegreeOfParallelism = Math.Max(2, Environment.ProcessorCount / 2) }, async (c, _) =>
        {
            string dir = tmp.Dir($"경우{Interlocked.Increment(ref n)}");
            try
            {
                if (await CheckKnownRepair(fx, c.Bytes, c.Label, dir, c.MayLose, listMode) is { } f) failures.Add(f);
            }
            finally
            {
                try { Directory.Delete(dir, true); } catch { }
            }
        });
        R3.AssertNoFailures(failures.OrderBy(f => f, StringComparer.Ordinal).ToList(), cases.Count);
    }

    /// <summary>이름 바이트(UTF-8 표시가 없으면 CP949) — NFC.</summary>
    private static string NameOf(byte[] b, int flagsAt, int at, int length) =>
        ((R3.U16(b, flagsAt) & 0x0800) != 0 ? Encoding.UTF8 : Encoding.GetEncoding(949)).GetString(b, at, length).Normalize(NormalizationForm.FormC);

    /// <summary>로컬 머리 <paramref name="h"/> 의 파일 이름(폴더면 빈 배열) — 그 머리가 깨졌을 때 잃어도 되는 것.</summary>
    private static string[] FileAtHead(byte[] b, int h)
    {
        string name = NameOf(b, h + 6, h + 30, R3.U16(b, h + 26));
        return name.EndsWith('/') ? Array.Empty<string>() : new[] { name };
    }

    /// <summary>로컬 머리 <paramref name="h"/> 와 그 뒤의 파일 이름 — 목록이 없을 때 그 머리가 깨지면 잃어도 되는 것.</summary>
    /// <summary><paramref name="at"/> 이 든 로컬 머리(그 앞에서 가장 가까운 머리).</summary>
    private static int HeadAt(byte[] b, int at) => R3.HeadsOf(b).Where(h => h <= at).Max();

    private static string[] FilesFrom(byte[] b, int h) =>
        R3.HeadsOf(b).Where(x => x >= h).Select(x => NameOf(b, x + 6, x + 30, R3.U16(b, x + 26))).Where(n => !n.EndsWith('/')).ToArray();

    /// <summary>목록 기록들(차례대로): 기록 위치, 이름 바이트 위치·길이, 가리키는 로컬 머리 위치.</summary>
    private static List<(int At, int NameAt, int NameLength, int Local)> CentralRecords(byte[] b)
    {
        int e = R3.EocdOf(b);
        int p = R3.CdStartOf(b), n = R3.U16(b, e + 10);
        var list = new List<(int, int, int, int)>();
        for (int i = 0; i < n; i++)
        {
            int nl = R3.U16(b, p + 28), el = R3.U16(b, p + 30), cl = R3.U16(b, p + 32);
            list.Add((p, p + 46, nl, (int)R3.U32(b, p + 42)));
            p += 46 + nl + el + cl;
        }
        return list;
    }

    // ───────────── (c) 로컬 머리 칸 하나 — 파일이 보고에서 사라지지 않음 ─────────────

    /// <summary>
    /// 계약((c) 5차 — 재현 그대로): 목록·데이터가 멀쩡하고 로컬 머리의 이름 길이 칸 한 바이트만 깨지면(그 깨진 길이로 읽은 이름의 끝이 우연히 '/'·'\') 고치기는
    /// 그 파일을 폴더로 세어 빠뜨리지 않고 살린다(예전: 검사는 '9개 중 1개에 문제', 고치기는 sample.csv 를 말없이 빼고 '8개를 모두 살렸어요').
    /// </summary>
    [Theory]
    [InlineData("zip_default", 162, 0xFF)]
    [InlineData("zip_aes256_korean_pw", 2858, 0xFF)]
    public async Task 로컬_머리_이름_길이_칸이_깨진_파일을_빠뜨리고_모두_살렸다고_하지_않는다(string id, int at, int xor)
    {
        var fx = FixtureCatalog.Get(id);
        byte[] orig = File.ReadAllBytes(fx.FullPath);
        using var tmp = new TempDir("rv5c1");
        Assert.Null(await CheckKnownRepair(fx, R3.Flip(orig, at, (byte)xor), $"{id} {at}^{xor:X2}", tmp.Dir("1"), mayLose: null));
    }

    /// <summary>
    /// 계약((c) 5차·I4-1): 목록은 두고 로컬 머리(고정 30바이트, <paramref name="span"/> 이 "whole" 이면 이름·추가 필드까지)를 한 바이트씩(^01·^FF) 깨도 고치기의 보고가 맞다 —
    /// 원래 수 = 목록의 파일 기록 수(폴더를 파일로 세지 않음), 살린 파일은 원래 이름·내용, '모두 살렸어요'면 원래 파일을 모두 살림. 깨진 머리의 파일 말고는 모두 살리고,
    /// 이름 길이 칸(+26·+27)만 깨진 것은 그 파일까지 모두 살린다(목록의 이름 길이로 다시 읽음).
    /// </summary>
    [Theory]
    [InlineData("zip_default", "whole")]
    [InlineData("zip_aes256_korean_pw", "fixed")]
    [InlineData("zip_store_l0", "lengths")]
    [InlineData("zip_zipcrypto_korean_pw", "lengths")]
    public Task 로컬_머리_칸이_하나씩_깨져도_보고가_맞다(string id, string span)
    {
        var fx = FixtureCatalog.Get(id);
        byte[] orig = File.ReadAllBytes(fx.FullPath);
        var cases = new List<(string, byte[], string[])>();
        foreach (int h in R3.HeadsOf(orig))
        {
            int whole = 30 + R3.U16(orig, h + 26) + R3.U16(orig, h + 28);
            var offsets = span switch
            {
                "lengths" => new[] { 26, 27, 28, 29 },
                "fixed" => Enumerable.Range(0, 30).ToArray(),
                _ => Enumerable.Range(0, whole).ToArray(),
            };
            foreach (int k in offsets)
                foreach (byte x in new byte[] { 0x01, 0xFF })
                    cases.Add(($"{id} 머리 {h}+{k} ^{x:X2}", R3.Flip(orig, h + k, x), k is 26 or 27 ? Array.Empty<string>() : FileAtHead(orig, h)));
        }
        return RunKnownRepairCases("rv5hdr", fx, cases);
    }

    // ───────────── (c) 깨진 목록 이름 — 유니코드 이름의 증거 ─────────────

    /// <summary>
    /// 계약((c) 5차·I4-1 ③): 반디집 ZIP 에서 목록 기록의 이름 바이트 하나만 깨지면(로컬 머리의 이름은 원래 이름) 깨진 이름을 쓰지 않는다 — 목록과 로컬 머리의 이름이 달라
    /// 그 파일은 '이름 정보가 서로 달라요'(어느 쪽이 맞는지 가르지 않음), 나머지는 원래 이름·내용으로 살린다(예전: 깨진 목록 이름을 그대로 써서 '견적서'가 '긋적서').
    /// 폴더 기록이면 파일은 모두 살린다.
    /// </summary>
    [Theory]
    [InlineData("zip_default")]
    [InlineData("zip_cp949")]
    public async Task 목록_이름_바이트가_깨지면_깨진_이름을_쓰지_않는다(string id)
    {
        var fx = FixtureCatalog.Get(id);
        byte[] orig = File.ReadAllBytes(fx.FullPath);
        var cases = CentralRecords(orig).SelectMany(r =>
            {
                string name = NameOf(orig, r.At + 8, r.NameAt, r.NameLength);
                string[] lose = name.EndsWith('/') ? Array.Empty<string>() : new[] { name };
                return Enumerable.Range(r.NameAt, r.NameLength).Select(at => ($"{id} 목록 이름 {at}바이트째 ^01", R3.Flip(orig, at, 0x01), lose));
            }).ToList();
        Assert.True(cases.Count > 100);
        await RunKnownRepairCases("rv5cdname", fx, cases);
    }

    /// <summary>
    /// 계약((c) 5차·I4-1 ③): 나눠진 ZIP(.z01·.z02·.zip)의 목록 이름 바이트가 깨지면 깨진 이름을 쓰지 않는다 — 그 파일은 '이름 정보가 서로 달라요', 나머지는 원래 이름·내용
    /// (예전: '난수_160KiB.bi' 처럼 확장자가 깨짐).
    /// </summary>
    [Fact]
    public async Task 나눠진_ZIP의_목록_이름_바이트가_깨지면_깨진_이름을_쓰지_않는다()
    {
        var fx = FixtureCatalog.Get("zip_split");
        string fixtureDir = Path.GetDirectoryName(fx.FullPath)!;
        var pieces = Directory.GetFiles(fixtureDir, "zip_split.*").Select(Path.GetFileName).ToList();
        byte[] last = File.ReadAllBytes(fx.FullPath);
        var want = ExpectedFiles(fx);
        var targets = CentralRecords(last).SelectMany(r => Enumerable.Range(r.NameAt, r.NameLength).Select(at => (At: at, Name: NameOf(last, r.At + 8, r.NameAt, r.NameLength)))).ToList();
        using var tmp = new TempDir("rv5split");
        var failures = new System.Collections.Concurrent.ConcurrentBag<string>();
        int n = 0;
        await Parallel.ForEachAsync(targets, new ParallelOptions { MaxDegreeOfParallelism = Math.Max(2, Environment.ProcessorCount / 2) }, async (t, _) =>
        {
            string dir = tmp.Dir($"경우{Interlocked.Increment(ref n)}");
            string label = $"zip_split 목록 이름 {t.At}바이트째 ^01";
            try
            {
                foreach (var p in pieces) File.Copy(Path.Combine(fixtureDir, p!), Path.Combine(dir, p!));
                File.WriteAllBytes(Path.Combine(dir, "zip_split.zip"), R3.Flip(last, t.At, 0x01));
                var expect = new RepairOracle.Expect
                {
                    ListMode = true,
                    MustSalvage = want.Keys.Select(Nfc).Where(k => k != t.Name).ToList(),
                    TestOutput = true,
                };
                var (f, r, _) = await RepairOracle.RepairCheckAsync(RepairOracle.Of(fx), Path.Combine(dir, "zip_split.zip"), Path.Combine(dir, "복구.zip"), null, expect, dir);
                if (f is not null) { failures.Add($"{label}: {f}"); return; }
                var wantLost = t.Name.EndsWith('/') ? Array.Empty<string>() : new[] { "이름 정보가 서로 달라요" };
                if (!r!.LostItems.Select(l => l.Reason).SequenceEqual(wantLost)) failures.Add($"{label}: 못 살린 파일 [{string.Join(", ", r.LostItems.Select(l => l.Display))}]");
            }
            catch (Exception ex) when (ex is not TimeoutException)
            {
                failures.Add($"{label}: 예외 {ex.GetType().Name} {ErrorCatalog.Describe(ex).Message}");
            }
            finally
            {
                try { Directory.Delete(dir, true); } catch { }
            }
        });
        R3.AssertNoFailures(failures.OrderBy(f => f, StringComparer.Ordinal).ToList(), targets.Count);
    }

    // ───────────── (e)/(f) 끝 머리의 디스크 번호·주석 길이 칸 ─────────────

    /// <summary>
    /// 계약((e)/(f) 5차 — 재현 그대로): 끝 머리의 디스크 번호 칸이나 주석 길이 칸이 깨지고 목록 기록 서명까지 깨져도 검사는 '문제없어요. 파일 0개'가 아니라
    /// 목록 손상(고치기 권함), 풀기는 0개를 풀고 오류 없이 끝나지 않는다(예전: 그 칸이 깨지면 구조 검사를 건너뛰어 종료 코드 0). 끝 머리 칸만 깨진 것은 그대로 다 풀린다.
    /// </summary>
    [Theory]
    [InlineData("zip_default", 6, 0x01)]    // 목록이 시작하는 디스크 번호
    [InlineData("zip_default", 4, 0x01)]    // 이 디스크 번호를 크게(나눔 세트로 보지 않을 만큼)
    [InlineData("zip_default", 20, 0xD1)]   // 주석 길이(파일 끝을 넘음)
    [InlineData("zip_aes256", 20, 0xD1)]
    public async Task 끝머리_디스크_번호나_주석_길이_칸이_깨져도_빠진_파일을_알린다(string id, int field, int xor)
    {
        var fx = FixtureCatalog.Get(id);
        byte[] orig = File.ReadAllBytes(fx.FullPath);
        int eocd = R3.EocdOf(orig);
        byte[] endBroken = field == 4 ? With16(orig, eocd + 4, 0x7777) : R3.Flip(orig, eocd + field, (byte)xor);
        var records = CentralRecords(orig);
        byte[] bothBroken = R3.Flip(endBroken, records[2].At + 1, 0xFF);   // 셋째 목록 기록의 서명
        using var tmp = new TempDir("rv5eocd");

        // 끝 머리 칸만 깨짐: 엔진은 그 칸을 보지 않으므로 그대로 다 풀린다 — 검사도 문제없음
        string onlyEnd = tmp.P("끝머리만.zip");
        File.WriteAllBytes(onlyEnd, endBroken);
        var ok = await ArchiveTestService.Instance.TestAsync(onlyEnd, fx.Password, null, CancellationToken.None).WaitAsync(Limit);
        Assert.True(ok.Ok, ok.ToDisplayText());
        Assert.Null(DifferenceOf(ExpectedFiles(fx), await ExtractFiles(onlyEnd, tmp.P("풀림1"), fx.Password)));

        // 목록 기록 서명까지 깨짐: 검사는 목록 손상(고치기 권함), 풀기는 오류(한국어 — 영어 원문 없음)
        string arc = tmp.P("둘다.zip");
        File.WriteAllBytes(arc, bothBroken);
        var report = await ArchiveTestService.Instance.TestAsync(arc, fx.Password, null, CancellationToken.None).WaitAsync(Limit);
        Assert.False(report.Ok, report.ToDisplayText());
        Assert.True(report.HasDamage, report.ToDisplayText());
        Assert.Contains(ZipListDamagedException.MissingMessage, report.ToDisplayText());
        var svc = new ArchiveServiceLocator().ResolveByContent(arc)!;
        var ex = await Assert.ThrowsAsync<ZipListDamagedException>(() => svc.ExtractAsync(arc, tmp.P("풀림2"), fx.Password).WaitAsync(Limit));
        Assert.Equal(ZipListDamagedException.MissingMessage, ErrorCatalog.HeadlessMessage(ex));
        Assert.Equal(1, ErrorMessages.HeadlessExitCode(ex));
        Assert.Contains(ErrorCatalog.Describe(ex, arc).Actions, a => a.Id == ErrorCatalog.RepairZipActionId);
    }

    private static byte[] With16(byte[] src, int at, ushort value)
    {
        var b = (byte[])src.Clone();
        BinaryPrimitives.WriteUInt16LittleEndian(b.AsSpan(at), value);
        return b;
    }

    private static string? DifferenceOf(Dictionary<string, string> want, Dictionary<string, string> got)
    {
        foreach (var (name, sha) in want)
            if (!got.TryGetValue(name, out var g)) return $"'{name}' 없음";
            else if (g != sha) return $"'{name}' 내용 다름";
        return got.Keys.FirstOrDefault(k => !want.ContainsKey(k)) is { } extra ? $"원래 없던 '{extra}'" : null;
    }

    // ───────────── 폴더·깨진 목록 기록을 파일로 세지 않음 ─────────────

    /// <summary>
    /// 계약(5차 — 재현 그대로·I4-1): 폴더 머리·목록 기록이 깨져도 고치기는 폴더를 '못 살린 파일'·'살린 파일'로 세지 않는다 — 목록이 온전하면 실제 파일을 모두 살려
    /// 'N개를 모두 살렸어요'(예전: '3개 중 2개 … 못 살린 파일: 분할_샘플/', '10개를 모두 살렸어요'에 폴더 포함). 폴더 머리의 이름 바이트가 빠져 뒤가 모두 밀리면
    /// 목록을 믿을 수 없어 훑기 모드 — 그 앞의 파일은 모두 살린다. 목록 기록의 CRC·이름이 함께 깨진 파일은 그 파일만 못 살린다.
    /// </summary>
    [Fact]
    public async Task 폴더와_깨진_목록_기록을_파일로_세지_않는다()
    {
        using var tmp = new TempDir("rv5dir");
        var failures = new List<string>();
        void Add(string? f) { if (f is not null) failures.Add(f); }

        var aes = FixtureCatalog.Get("zip_aes256_korean_pw");
        byte[] aesBytes = File.ReadAllBytes(aes.FullPath);
        Add(await CheckKnownRepair(aes, R3.Flip(aesBytes, 26, 0x01), "AES 첫 폴더 머리 이름 길이 +1", tmp.Dir("1"), mayLose: null));

        var zc = FixtureCatalog.Get("zip_zipcrypto_korean_pw");
        Add(await CheckKnownRepair(zc, Zero(File.ReadAllBytes(zc.FullPath), 2296, 32), "ZipCrypto 폴더 머리 이름 32바이트 0", tmp.Dir("2"), mayLose: null));

        var def = FixtureCatalog.Get("zip_default");
        byte[] d = File.ReadAllBytes(def.FullPath);
        // 폴더 머리의 이름 첫 바이트가 빠짐(뒤가 모두 한 바이트씩 당겨져 목록을 믿을 수 없음 — 훑기 모드: 그 폴더 앞의 파일은 모두 살림)
        Add(await CheckKnownRepair(def, d[..2114].Concat(d[2115..]).ToArray(), "기본 폴더 머리 이름 바이트 빠짐", tmp.Dir("3"), mayLose: FilesFrom(d, HeadAt(d, 2114)), listMode: false));

        // 저장 문서 속 저장 문서: 중첩.zip 의 목록 기록 CRC·이름 바이트가 함께 깨짐(그 기록은 맞는 기록이 아니게 됨)
        var nested = R4.DeepNested();
        var rec = CentralRecords(nested.Bytes).Single(r => Encoding.UTF8.GetString(nested.Bytes, r.NameAt, r.NameLength) == "중첩.zip");
        foreach (byte x in new byte[] { 0x01, 0xFF })
        {
            var broken = R3.Flip(R3.Flip(nested.Bytes, rec.At + 19, x), rec.NameAt + 5, x);
            Add(await R3.CheckRepair(nested, broken, $"중첩.zip 목록 CRC·이름 ^{x:X2}", tmp, 10 + x, mayLose: new[] { "중첩.zip" }, listMode: true));
        }
        R3.AssertNoFailures(failures, 5);
    }

    /// <summary>
    /// 계약(5차): 나눠진 ZIP 첫 조각(.z01)의 첫 폴더 머리 이름 길이 칸이 깨져도(목록은 마지막 조각에 온전) 폴더를 '못 살린 파일'로 세지 않고 파일 2개를 모두 살린다
    /// (예전: '3개 중 2개를 살렸어요. 못 살린 파일: 분할_샘플/').
    /// </summary>
    [Fact]
    public async Task 나눠진_ZIP_첫_폴더_머리가_깨져도_폴더를_못_살린_파일로_세지_않는다()
    {
        var fx = FixtureCatalog.Get("zip_split");
        string fixtureDir = Path.GetDirectoryName(fx.FullPath)!;
        using var tmp = new TempDir("rv5split1");
        foreach (var p in Directory.GetFiles(fixtureDir, "zip_split.*")) File.Copy(p, tmp.P(Path.GetFileName(p)));
        byte[] first = File.ReadAllBytes(tmp.P("zip_split.z01"));
        File.WriteAllBytes(tmp.P("zip_split.z01"), R3.Flip(first, 30, 0xFF));   // 나눔 표시 4바이트 + 머리 +26(이름 길이)
        var r = await ZipRepairer.Instance.RepairAsync(tmp.P("zip_split.zip"), tmp.P("복구.zip"), null, null, CancellationToken.None).WaitAsync(Limit);
        Assert.Empty(r.Lost);
        Assert.Equal(2, r.OriginalCount);
        Assert.Equal("2개를 모두 살렸어요.", r.Headline);
        Assert.Null(DifferenceOf(ExpectedFiles(fx), await ExtractFiles(tmp.P("복구.zip"), tmp.P("풀림"), null)));
    }

    /// <summary>
    /// 계약(5차·I4-1 훑기 모드): 목록이 없는 ZIP 에서 폴더 머리의 이름 길이 칸이 바뀌거나 이름에서 바이트가 빠지거나 끼면 그 머리 뒤의 끝을 믿을 수 없다 —
    /// 그 앞의 파일은 모두 원래 이름·내용으로 살리고, 머리를 짐작으로 바로잡지 않으며(예전: 유니코드 이름으로 바로잡음), 거짓 성공이 없다.
    /// </summary>
    [Theory]
    [InlineData("zip_default")]
    [InlineData("zip_aes256_korean_pw")]
    public Task 목록_없는_ZIP의_폴더_머리_이름이_밀리면_앞의_파일을_살린다(string id)
    {
        var fx = FixtureCatalog.Get(id);
        byte[] orig = File.ReadAllBytes(fx.FullPath);
        int cd = R3.CdStartOf(orig);
        var heads = R3.HeadsOf(orig);
        var folders = CentralRecords(orig).Where(r => orig[r.NameAt + r.NameLength - 1] == (byte)'/').Select(r => r.Local).ToList();
        Assert.NotEmpty(folders);
        var cases = new List<(string, byte[], string[])>();
        foreach (int h in folders)
        {
            var lose = FilesFrom(orig, h);
            cases.Add(($"{id} 목록 없음·폴더 {h} 이름 길이 칸 ^01", R3.Flip(orig, h + 26, 0x01)[..cd], lose));
            cases.Add(($"{id} 목록 없음·폴더 {h} 이름 첫 바이트 빠짐", orig[..(h + 30)].Concat(orig[(h + 31)..cd]).ToArray(), lose));
            cases.Add(($"{id} 목록 없음·폴더 {h} 이름에 바이트 낌", orig[..(h + 32)].Concat(new byte[] { 0x41 }).Concat(orig[(h + 32)..cd]).ToArray(), lose));
        }
        return RunKnownRepairCases("rv5nocd", fx, cases, listMode: false);
    }

    // ───────────── 빈 파일 바로 뒤 머리가 깨짐 ─────────────

    /// <summary>
    /// 계약(5차 — 재현 그대로·전수): 머리와 목록 기록이 멀쩡한 항목은 바로 뒤 항목의 머리가 깨져도(그 자리를 목록 기록이 가리킴) 살린다 — 빈 파일도
    /// (예전: 빈파일.txt 뒤 '빈폴더/' 머리가 깨지면 '9개 중 8개 … 못 살린 파일: 빈파일.txt').
    /// </summary>
    [Fact]
    public async Task 바로_뒤_머리가_깨져도_멀쩡한_빈_파일을_버리지_않는다()
    {
        var fx = FixtureCatalog.Get("zip_default");
        byte[] orig = File.ReadAllBytes(fx.FullPath);
        var records = CentralRecords(orig).OrderBy(r => r.Local).ToList();
        var cases = new List<(string Label, byte[] Bytes, string Keep, string[] Lose)>();
        for (int i = 0; i + 1 < records.Count; i++)
        {
            string name = NameOf(orig, records[i].At + 8, records[i].NameAt, records[i].NameLength);
            if (name.EndsWith('/')) continue;
            // 깨진 것은 바로 뒤 항목의 머리 — 그 항목(파일이면)만 잃어도 되고, 멀쩡한 이 파일(빈 파일 포함)은 꼭 살린다
            cases.Add(($"{name} 뒤 머리 {records[i + 1].Local}+1 부터 32바이트 0", Zero(orig, records[i + 1].Local + 1, 32), name, FileAtHead(orig, records[i + 1].Local)));
        }
        Assert.Contains(cases, c => c.Keep.EndsWith("빈파일.txt", StringComparison.Ordinal));
        using var tmp = new TempDir("rv5empty");
        var failures = new List<string>();
        int n = 0;
        foreach (var (label, bytes, keep, lose) in cases)
        {
            Assert.DoesNotContain(keep, lose);
            if (await CheckKnownRepair(fx, bytes, label, tmp.Dir($"경우{++n}"), lose) is { } f) failures.Add(f);
        }
        R3.AssertNoFailures(failures, cases.Count);
    }

    // ───────────── 방식 칸이 깨진 항목 ─────────────

    /// <summary>
    /// 계약(5차·I4-1 훑기 모드): 목록이 없는 ZIP 에서 머리의 방식 칸이 깨져도(저장 0→1·Deflate 8→9) 방식을 짐작해 고쳐 쓰지 않는다 — 그 앞의 파일은 모두 원래 내용으로
    /// 살리고, 그 파일은 '알 수 없는 압축 방식이에요'로 못 살리거나 일집이 확인할 수 없는 방식이면 '확인 못 함'으로 옮기며(살린 수에 넣지 않음), 고친 ZIP 은 일집 검사에서
    /// 살린 파일에 문제가 없다(예전: 저장·Deflate 로 맞춰 보아 머리를 고쳐 씀 — 추측).
    /// </summary>
    [Fact]
    public Task 목록_없는_ZIP의_방식_칸이_깨져도_거짓_성공이_없다()
    {
        var s = R3.Nested();
        return R3.RunRepairCases("rv5method", Enumerable.Range(0, s.Heads.Count)
            .Select(i => new R3.Case(s, $"목록 없음·{R3.NameAt(s, i)} 방식 칸 ^01", R3.Flip(s.Bytes, s.Heads[i] + 8, 0x01)[..s.CdStart],
                MayLose: R3.ThisAndAfter(s, i), ListMode: false, TestOutput: true)));
    }

    // ───────────── I2-8: 따옴표 없이 한글 경로·이름이 든 엔진 영어 문구 ─────────────

    /// <summary>엔진(SharpCompress·SharpSevenZip 등)이 내는 모양의 영어 문구 — 경로·이름이 따옴표 없이(또는 큰따옴표로) 붙음.</summary>
    public static TheoryData<string, Exception> EngineShapedExceptions => new()
    {
        { "SharpCompress 모르는 머리", new SharpCompress.Common.ArchiveException(@"Unknown header D:\업무\'26년 견적\거래처's 자료\견적서.zip") },
        { "SharpCompress 항목 손상", new SharpCompress.Common.ArchiveException("Entry 견적서's 요약.xlsx is corrupt") },
        { "SharpCompress 잘림", new SharpCompress.Common.IncompleteArchiveException(@"Unexpected end of 견적서 묶음.zip") },
        { "SharpSevenZip 파일 없음", new SharpSevenZip.Exceptions.SharpSevenZipException("The file \"D:\\업무\\견적서.7z\" does not exist.") },
        { "SharpSevenZip 폴더 없음", new SharpSevenZip.Exceptions.SharpSevenZipException("Directory \"D:\\업무\\자료\" does not exist!") },
        { "엔진 원본 없음(일반 예외)", new Exception(@"Source does not exist: D:\업무\자료 묶음.zip") },
        { "엔진 항목(일반 예외)", new Exception("Unknown Stream - File Entry: 자료/견적.txt") },
        { "이름만 한국어인 영어 문장(일반 예외)", new Exception("Entry 견적서's 요약.xlsx is corrupt") },
        { "이름만 한국어(큰따옴표)", new InvalidOperationException("Could not open \"견적서 최종.zip\" for reading") },
    };

    /// <summary>
    /// 계약(I2-8 5차): 따옴표 없이 한글 경로·이름이 든 짧은 영어 엔진 문구도 화면·창 없이 풀기·여러 개 풀기·압축 실패 문구에 그대로 나오지 않는다 —
    /// 한국어 문구(영어 낱말 없음), 원문은 '자세히'에만(예전: 따옴표 밖 한글을 보고 '서비스가 쓴 한국어'로 여겨 원문 그대로).
    /// </summary>
    [Theory]
    [MemberData(nameof(EngineShapedExceptions))]
    public void 한글_경로가_붙은_엔진_영어_문구도_그대로_나오지_않는다(string name, Exception ex)
    {
        var p = ErrorCatalog.Describe(ex, @"D:\업무\견적서.zip");
        foreach (var (what, text) in new[]
                 {
                     ("화면", (p with { Heading = "압축을 풀지 못했어요." }).ToDisplayText()),
                     ("창 없이 풀기", ErrorCatalog.HeadlessMessage(ex)),
                     ("여러 개 풀기", ErrorMessages.ToPlainMessage(ex)),
                     ("압축 실패", ErrorMessages.ToCompressMessage(ex)),
                 })
        {
            Assert.True(ErrorCatalog.ContainsHangul(text), $"{name} {what}: 한글 없음 — {text}");
            Assert.False(System.Text.RegularExpressions.Regex.IsMatch(text, "[A-Za-z]{3,}"), $"{name} {what}: 영어가 남음 — {text} ({ex.Message})");
            Assert.DoesNotContain(ex.Message, text);
        }
        Assert.Contains(ex.Message, p.Details);
    }

    /// <summary>계약(I2-8 5차): 서비스가 쓴 한국어 문구는 경로·이름(영어 낱말이 든 것도)이 붙어 있어도 그대로 보인다.</summary>
    [Theory]
    [InlineData(@"임시 폴더를 만들지 못했어요: C:\Users\Kim\Documents of the team\작업")]
    [InlineData("ZIP 로컬 머리를 찾지 못함: 견적서 최종.txt")]
    [InlineData("‘Report of the year.txt’의 내용이 원래와 달라요.")]
    [InlineData("저장할 공간이 부족해요. 풀려면 1.2GB 정도가 필요해요.")]
    public void 서비스가_쓴_한국어_문구는_그대로_보인다(string message)
    {
        Assert.True(ErrorCatalog.LooksKorean(message), message);
        Assert.Equal(message, ErrorCatalog.Describe(new IOException(message)).Message);
    }
}
