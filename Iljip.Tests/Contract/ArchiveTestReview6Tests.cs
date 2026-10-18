using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;
using Iljip.Services;
using Iljip.Tests.TestSupport;
using R3 = Iljip.Tests.Contract.ArchiveTestReview3Tests;
using SharpZip = ICSharpCode.SharpZipLib.Zip;

namespace Iljip.Tests.Contract;

/// <summary>
/// [I4-1 검증 지적 1차] 압축 검사(I2-7)·깨진 ZIP 고치기(I3-12)의 남은 거짓 성공:
/// 목록 이름 끝이 '/' 로 깨진 파일을 폴더로 보고 검사 '문제없어요'·풀기 종료 코드 0(파일이 빠짐), ZIPX(7z.dll 길)가 목록 이름 손상을 보지 않음,
/// 고치기 목록 모드가 끝 글자만 '/' 로 깨진 빈 파일을 세지 않아 'N-1개를 모두 살렸어요', 훑기 모드에서 빈 파일 머리의 이름 길이 칸이 깨지면 사슬이
/// 저장 문서 안으로 들어가 안쪽 부품을 바깥 파일로 '살렸어요'. 그리고 같은 검증의 작은 지적(강한 암호 표시, .z01 시험, 암호 머리 손상 이유, 깨진 이름 표시).
/// </summary>
public class ArchiveTestReview6Tests
{
    private static readonly TimeSpan Limit = TimeSpan.FromSeconds(120);

    // ───────────── 재료·도우미 ─────────────

    /// <summary>목록 기록(시작, 이름 시작·길이, 추가 필드 길이, 로컬 머리 위치) — 차례대로.</summary>
    private sealed record Rec(int At, int NameAt, int NameLength, int ExtraLength, int Local);

    private static List<Rec> Records(byte[] b)
    {
        int e = R3.EocdOf(b);
        int p = R3.CdStartOf(b), n = R3.U16(b, e + 10);
        var list = new List<Rec>();
        for (int i = 0; i < n; i++)
        {
            int nl = R3.U16(b, p + 28), el = R3.U16(b, p + 30), cl = R3.U16(b, p + 32);
            list.Add(new Rec(p, p + 46, nl, el, (int)R3.U32(b, p + 42)));
            p += 46 + nl + el + cl;
        }
        return list;
    }

    /// <summary>기록의 이름(UTF-8 로 읽히면 UTF-8, 아니면 CP949).</summary>
    private static string NameOf(byte[] b, Rec r)
    {
        try { return new UTF8Encoding(false, true).GetString(b, r.NameAt, r.NameLength); }
        catch (DecoderFallbackException) { return Encoding.GetEncoding(949).GetString(b, r.NameAt, r.NameLength); }
    }

    /// <summary>기록의 유니코드 이름(0x7075) 마지막 바이트 위치(없으면 -1).</summary>
    private static int UnicodeNameLastByte(byte[] b, Rec r)
    {
        int x = r.NameAt + r.NameLength, end = x + r.ExtraLength;
        while (x + 4 <= end)
        {
            int id = R3.U16(b, x), size = R3.U16(b, x + 2);
            if (id == 0x7075 && size > 5) return x + 4 + size - 1;
            x += 4 + size;
        }
        return -1;
    }

    private static byte[] With(byte[] src, Action<byte[]> change)
    {
        var b = (byte[])src.Clone();
        change(b);
        return b;
    }

    /// <summary>System.IO.Compression 으로 만든 ZIP(0x7075 없음 — 탐색기·.NET·Python 과 같은 모양). <paramref name="stored"/> 면 저장 방식.</summary>
    private static byte[] NetZip(IEnumerable<(string Name, byte[]? Data)> entries, Func<string, bool>? stored = null)
    {
        using var ms = new MemoryStream();
        using (var z = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
            foreach (var (name, data) in entries)
            {
                var e = z.CreateEntry(name, stored?.Invoke(name) == true ? CompressionLevel.NoCompression : CompressionLevel.Optimal);
                e.LastWriteTime = new DateTimeOffset(2026, 10, 2, 10, 0, 0, TimeSpan.FromHours(9));
                if (data is null) continue;   // 폴더(이름 끝 '/')
                using var s = e.Open();
                s.Write(data);
            }
        return ms.ToArray();
    }

    private static Dictionary<string, string> Sha(IReadOnlyDictionary<string, byte[]> files) =>
        files.ToDictionary(kv => RepairOracle.Nfc(kv.Key), kv => TreeAssert.Sha256(kv.Value), StringComparer.Ordinal);

    /// <summary>압축을 풀어 본다(실패하면 그 예외). 풀린 파일(NFC 이름 → sha256)과 폴더(상대 경로)도 돌려준다.</summary>
    private static async Task<(Exception? Error, Dictionary<string, string> Files, List<string> Dirs)> Extract(string arc, string dest, string? password = null)
    {
        Exception? error = null;
        try
        {
            var svc = new ArchiveServiceLocator().ResolveForAutomation(arc) ?? throw new UnsupportedFormatException(Path.GetExtension(arc));
            await svc.ExtractAsync(arc, dest, password).WaitAsync(Limit);
        }
        catch (Exception ex) when (ex is not TimeoutException)
        {
            error = ex;
        }
        var files = Directory.Exists(dest)
            ? TreeAssert.Snapshot(dest, nfc: true).Where(kv => kv.Value is not null).ToDictionary(kv => kv.Key, kv => kv.Value!, StringComparer.Ordinal)
            : new Dictionary<string, string>(StringComparer.Ordinal);
        var dirs = Directory.Exists(dest)
            ? Directory.EnumerateDirectories(dest, "*", SearchOption.AllDirectories).Select(d => RepairOracle.Nfc(Path.GetRelativePath(dest, d).Replace('\\', '/'))).ToList()
            : new List<string>();
        return (error, files, dirs);
    }

    // ───────────── 검사·풀기: 목록 이름 끝이 '/' 로 깨진 파일 ─────────────

    public static TheoryData<string> SlashCases => new()
    {
        "목록 이름 끝 바이트가 '/'",
        "마지막 기록의 이름 길이 칸이 줄어 '폴더/' 에서 끝남",
        "반디집 ZIP 의 유니코드 이름(0x7075) 끝 바이트가 '/'",
        "반디집 ZIP 의 폴더 기록 유니코드 이름이 '/' 로 시작",
    };

    /// <summary>
    /// 계약(I4-1 검증 지적 1차 [I2-7]): 목록 기록의 이름(또는 엔진이 쓰는 유니코드 이름 0x7075) 끝이 '/' 로 깨져 엔진이 그 파일을 폴더로 보아도, 로컬 머리와 맞지 않는
    /// 기록이므로 검사는 '문제가 있어요. 파일 N개 중 1개…'(원래 파일 수), 풀기는 그 파일 대신 빈 폴더를 만들지 않고 맞는 파일을 다 푼 뒤 손상으로 알린다(종료 코드 1).
    /// 고친 압축을 바꿔 넣기 전 시험(TestEntriesSync)도 실패한다. 예전: '문제없어요. 파일 N-1개를 확인했어요.'·종료 코드 0·빈 폴더 '견적서.tx'.
    /// 폴더 기록의 유니코드 이름이 '/…' 로 깨진 것도 검사가 '문제없어요'가 아니고 풀기가 '안전하지 않은 경로'가 아닌 손상으로 알린다.
    /// </summary>
    [Theory]
    [MemberData(nameof(SlashCases))]
    public async Task 이름_끝이_슬래시로_깨진_파일을_검사_풀기가_말없이_넘기지_않는다(string label)
    {
        using var tmp = new TempDir("rv6slash");
        Dictionary<string, string> want;
        byte[] broken;
        string? brokenFile;   // 빠지면 안 되는(풀리면 안 되는) 원래 파일 — 폴더 기록이 깨진 경우는 없음
        if (label.StartsWith("반디집", StringComparison.Ordinal))
        {
            var fx = FixtureCatalog.Get("zip_default");
            byte[] orig = File.ReadAllBytes(fx.FullPath);
            want = fx.ExpectedFiles.ToDictionary(e => RepairOracle.Nfc(e.Path), e => e.Sha256!, StringComparer.Ordinal);
            var recs = Records(orig);
            if (label.EndsWith("시작", StringComparison.Ordinal))
            {
                var dir = recs.First(r => orig[r.NameAt + r.NameLength - 1] == (byte)'/' && UnicodeNameLastByte(orig, r) > 0);
                int at = UnicodeNameLastByte(orig, dir) - (Encoding.UTF8.GetByteCount(NameOf(orig, dir)) - 1);   // 유니코드 이름 첫 바이트
                broken = With(orig, b => b[at] = (byte)'/');
                brokenFile = null;
            }
            else
            {
                var file = recs.First(r => NameOf(orig, r).EndsWith("견적서_2026.txt", StringComparison.Ordinal));
                broken = With(orig, b => b[UnicodeNameLastByte(orig, file)] = (byte)'/');
                brokenFile = RepairOracle.Nfc(NameOf(orig, file));
            }
        }
        else
        {
            var files = new Dictionary<string, byte[]>(StringComparer.Ordinal) { ["견적서.txt"] = R3.Text("견적", 4200), ["폴더/readme.txt"] = R3.Text("읽기", 1500) };
            byte[] orig = NetZip(files.Select(kv => (kv.Key, (byte[]?)kv.Value)));
            want = Sha(files);
            var recs = Records(orig);
            if (label.StartsWith("마지막", StringComparison.Ordinal))
            {
                var last = recs[^1];
                Assert.Equal("폴더/readme.txt", NameOf(orig, last));
                broken = With(orig, b => BinaryPrimitives.WriteUInt16LittleEndian(b.AsSpan(last.At + 28), (ushort)Encoding.UTF8.GetByteCount("폴더/")));
                brokenFile = "폴더/readme.txt";
            }
            else
            {
                var first = recs.Single(r => NameOf(orig, r) == "견적서.txt");
                broken = With(orig, b => b[first.NameAt + first.NameLength - 1] = (byte)'/');
                brokenFile = "견적서.txt";
            }
        }
        string arc = tmp.P("깨짐.zip");
        File.WriteAllBytes(arc, broken);

        // 검사: 손상 — 확인한 파일 + 문제 파일 = 원래 파일 수(폴더로 보인 파일도 하나로 셈)
        var report = await ArchiveTestService.Instance.TestAsync(arc, null, null, CancellationToken.None).WaitAsync(Limit);
        Assert.False(report.Ok, $"{label}: {report.ToDisplayText()}");
        Assert.True(report.HasDamage, report.ToDisplayText());
        if (brokenFile is not null)
        {
            Assert.Equal(want.Count, report.CheckedCount + report.ProblemEntryCount);
            Assert.Equal($"문제가 있어요. 파일 {want.Count}개 중 {report.ProblemEntryCount}개에 문제가 있어요.", report.Headline);
        }

        // 고친 압축을 바꿔 넣기 전의 시험(첫 문제에서 멈춤)도 실패
        Assert.ThrowsAny<CorruptArchiveException>(() => new ZipArchiveService().TestEntriesSync(arc, null, _ => true, CancellationToken.None));

        // 풀기: 손상(종료 코드 1) — 맞는 파일은 원래 이름·내용으로, 깨진 파일은 빈 폴더로도 만들지 않음
        var (error, got, dirs) = await Extract(arc, tmp.P("풀림"));
        Assert.True(error is CorruptArchiveException, $"{label}: 풀기 결과 {error?.GetType().Name ?? "성공(종료 코드 0)"} {error?.Message}");
        Assert.Equal(1, ErrorMessages.HeadlessExitCode(error!));
        foreach (var (name, sha) in got)
            Assert.True(want.TryGetValue(name, out var w) && w == sha, $"{label}: 원래 없던 이름·내용으로 풀림 '{name}'");
        if (brokenFile is not null)
        {
            Assert.DoesNotContain(brokenFile, got.Keys);
            Assert.Equal(want.Count - 1, got.Count);
            // 깨진 기록이 폴더로 보인 이름(끝 글자가 '/' 로 바뀐 이름, 이름이 '폴더/' 에서 잘림)으로 빈 폴더를 만들지 않는다
            string phantom = label.StartsWith("마지막", StringComparison.Ordinal) ? "폴더" : brokenFile[..^1];
            Assert.DoesNotContain(phantom, dirs);
        }
    }

    // ───────────── 검사·풀기: ZIPX(7z.dll) 의 목록 이름 손상 ─────────────

    public static TheoryData<string> ZipxCases => new() { "readme.txt 목록 이름 가운데 4바이트 0", "반복_압축잘됨.txt 유니코드 이름 끝 바이트" };

    /// <summary>
    /// 계약(I4-1 검증 지적 1차 [I2-7 — ZIPX]): 7z.dll 로 여는 ZIPX 도 ZIP 과 같이 목록 기록과 로컬 머리(이름·유니코드 이름)를 맞춰 본다 — 목록 이름이 깨지면 검사는 손상,
    /// 풀기는 그 파일을 틀린 이름으로 만들지 않고 맞는 파일을 다 푼 뒤 손상(종료 코드 1). 예전: '문제없어요. 파일 8개'(readme.txt 가 빠짐)·'반복_압축잘됨.tx�' 로 풀고 종료 코드 0.
    /// </summary>
    [Theory]
    [MemberData(nameof(ZipxCases))]
    public async Task ZIPX의_목록_이름_손상을_검사_풀기가_알린다(string label)
    {
        var fx = FixtureCatalog.Get("zipx_default");
        byte[] orig = File.ReadAllBytes(fx.FullPath);
        var want = fx.ExpectedFiles.ToDictionary(e => RepairOracle.Nfc(e.Path), e => e.Sha256!, StringComparer.Ordinal);
        var recs = Records(orig);
        byte[] broken;
        if (label.StartsWith("readme", StringComparison.Ordinal))
        {
            var r = recs.Single(x => NameOf(orig, x).EndsWith("/readme.txt", StringComparison.Ordinal));
            int slash = Array.LastIndexOf(orig, (byte)'/', r.NameAt + r.NameLength - 1, r.NameLength);
            broken = With(orig, b => Array.Clear(b, slash + 1, 4));
        }
        else
        {
            var r = recs.Single(x => NameOf(orig, x).EndsWith("반복_압축잘됨.txt", StringComparison.Ordinal));
            int at = UnicodeNameLastByte(orig, r);
            Assert.True(at > 0, "zipx_default 에 유니코드 이름(0x7075)이 없음");
            broken = With(orig, b => b[at] ^= 0x01);
        }
        using var tmp = new TempDir("rv6zipx");
        string arc = tmp.P("깨짐.zipx");
        File.WriteAllBytes(arc, broken);

        var report = await ArchiveTestService.Instance.TestAsync(arc, null, null, CancellationToken.None).WaitAsync(Limit);
        Assert.False(report.Ok, $"{label}: {report.ToDisplayText()}");
        Assert.True(report.HasDamage, report.ToDisplayText());
        Assert.True(report.CheckedCount < want.Count, report.ToDisplayText());

        var (error, got, _) = await Extract(arc, tmp.P("풀림"));
        Assert.True(error is CorruptArchiveException, $"{label}: 풀기 결과 {error?.GetType().Name ?? "성공(종료 코드 0)"} {error?.Message}");
        foreach (var (name, sha) in got)
            Assert.True(want.TryGetValue(name, out var w) && w == sha, $"{label}: 원래 없던 이름·내용으로 풀림 '{name}'");
        Assert.True(got.Count < want.Count, $"{label}: 깨진 기록의 파일까지 풀림");

        // 깨지지 않은 원본은 그대로 '문제없어요'·종료 코드 0
        var ok = await ArchiveTestService.Instance.TestAsync(fx.FullPath, null, null, CancellationToken.None).WaitAsync(Limit);
        Assert.True(ok.Ok, ok.ToDisplayText());
        Assert.Equal(want.Count, ok.CheckedCount);
    }

    // ───────────── 고치기 목록 모드: 끝 글자만 '/' 로 깨진 빈 파일 ─────────────

    public static TheoryData<string> EmptySlashCases => new()
    {
        "목록 이름 끝 바이트가 '/'",
        "로컬 이름 끝 바이트가 '/'",
        "목록 이름 끝 바이트가 '\\'",
        "반디집 ZIP 목록 이름 끝 바이트가 '/'",
    };

    /// <summary>
    /// 계약(I4-1 검증 지적 1차 [I3-12 목록 모드] — cut 8): 빈 파일 기록의 이름 끝 바이트(목록이나 로컬 머리 한쪽)가 '/'·'\' 로 깨지면 폴더인지 빈 파일인지 가를 수 없다 —
    /// 쓰지 않고 '못 살린 파일(이름 정보가 서로 달라요)'로 센다: '전체 N개 중 N-1개를 살렸어요.'(덜 세지 않고 더 셀 수 있음). 예전: 세지 않아 'N-1개를 모두 살렸어요.'.
    /// </summary>
    [Theory]
    [MemberData(nameof(EmptySlashCases))]
    public async Task 끝_글자가_슬래시로_깨진_빈_파일을_빠뜨리고_모두_살렸다고_하지_않는다(string label)
    {
        using var tmp = new TempDir("rv6empty");
        RepairOracle.Original original;
        byte[] broken;
        string target;
        if (label.StartsWith("반디집", StringComparison.Ordinal))
        {
            var fx = FixtureCatalog.Get("zip_default");
            byte[] orig = File.ReadAllBytes(fx.FullPath);
            original = RepairOracle.Of(fx);
            var r = Records(orig).Single(x => NameOf(orig, x).EndsWith("빈파일.txt", StringComparison.Ordinal));
            target = RepairOracle.Nfc(NameOf(orig, r));
            broken = With(orig, b => b[r.NameAt + r.NameLength - 1] = (byte)'/');
        }
        else
        {
            var src = new Dictionary<string, byte[]>(StringComparer.Ordinal)
            {
                ["보고서.txt"] = R3.Text("보고", 3000),
                ["빈파일.txt"] = Array.Empty<byte>(),
                ["자료/표.txt"] = R3.Text("표", 2000),
            };
            original = RepairOracle.Of(src);
            byte[] zip = NetZip(src.Select(kv => (kv.Key, (byte[]?)kv.Value)));
            var rec = Records(zip).Single(x => NameOf(zip, x) == "빈파일.txt");
            target = "빈파일.txt";
            byte slashByte = label.Contains('\\') ? (byte)'\\' : (byte)'/';
            broken = label.StartsWith("로컬", StringComparison.Ordinal)
                ? With(zip, b => b[rec.Local + 30 + rec.NameLength - 1] = slashByte)
                : With(zip, b => b[rec.NameAt + rec.NameLength - 1] = slashByte);
        }
        int files = original.Files.Count;
        var r1 = await ZipRepairer.Instance.RepairAsync(WriteTo(tmp.P("깨짐.zip"), broken), tmp.P("복구.zip"), null, null, CancellationToken.None).WaitAsync(Limit);
        Assert.True(r1.CountKnown, $"{label}: {r1.ToDisplayText()}");
        Assert.False(r1.IsComplete, $"{label}: {r1.ToDisplayText()}");
        Assert.Equal(files, r1.OriginalCount);
        Assert.Equal(files - 1, r1.Salvaged.Count);
        Assert.DoesNotContain(target, r1.Salvaged.Select(RepairOracle.Nfc));
        Assert.Single(r1.LostItems);
        Assert.Equal($"전체 {files}개 중 {files - 1}개를 살렸어요.", r1.Headline);
        // 불변식(새 ZIP = 살린 것, 살린 것은 원래 이름·내용, M+U+L=N, 고친 ZIP 검사도 문제없음)과 나머지 파일은 모두 살림
        Assert.Null(await RepairOracle.RepairAndCheckAsync(original, tmp.P("깨짐.zip"), tmp.P("복구2.zip"), null,
            new RepairOracle.Expect { ListMode = true, MustSalvage = original.Files.Keys.Where(k => k != target).ToList(), TestOutput = true }, tmp.Dir("읽기")));
    }

    private static string WriteTo(string path, byte[] bytes)
    {
        File.WriteAllBytes(path, bytes);
        return path;
    }

    // ───────────── 고치기 훑기 모드: 데이터 시작 자리를 믿을 수 없는 빈 항목 ─────────────

    public static TheoryData<string, string> ShiftCases => new()
    {
        { "빈.txt", "이름 길이" },
        { "빈.txt", "추가 필드 길이" },
        { "폴더/", "이름 길이" },
        { "폴더/", "추가 필드 길이" },
    };

    /// <summary>
    /// 계약(I4-1 검증 지적 1차 [I3-12 훑기 모드]): 목록이 없는 ZIP 에서 데이터가 없는 항목(빈 파일·폴더) 머리의 이름 길이·추가 필드 길이 칸이 깨져 데이터 시작이
    /// 바로 뒤 저장 문서(docx) 속 첫 부품 머리에 정확히 닿아도, 사슬이 문서 안으로 들어가지 않는다 — 안쪽 부품(word/document.xml 등)을 바깥 파일로 '살렸어요' 하지 않고,
    /// 그 앞 파일은 모두 살리며 요약은 '원래 몇 개였는지 확실하지 않아요'. 예전: 빈 파일의 '크기 0 + 바로 뒤 서명' 으로 끝을 믿어 'word/document.xml' 을 살림.
    /// </summary>
    [Theory]
    [MemberData(nameof(ShiftCases))]
    public async Task 빈_항목_머리의_길이_칸이_깨져도_저장_문서_안의_부품을_살리지_않는다(string emptyName, string field)
    {
        var docx = R3.InnerZip(("[Content_Types].xml", R3.Text("ct", 700)), ("_rels/.rels", R3.Text("rels", 400)), ("word/document.xml", R3.Text("본문", 3000)));
        var entries = new List<(string Name, byte[]? Data)>
        {
            ("보고서.txt", R3.Text("보고", 3000)),
            (emptyName, emptyName.EndsWith('/') ? null : Array.Empty<byte>()),
            ("문서.docx", docx),
            ("끝.txt", R3.Text("끝", 1500)),
        };
        byte[] zip = NetZip(entries, stored: n => n.EndsWith(".docx", StringComparison.Ordinal));
        var recs = Records(zip);
        var empty = recs.Single(r => NameOf(zip, r) == emptyName);
        var doc = recs.Single(r => NameOf(zip, r) == "문서.docx");
        int docData = doc.Local + 30 + R3.U16(zip, doc.Local + 26) + R3.U16(zip, doc.Local + 28);
        Assert.Equal(0x04034b50u, R3.U32(zip, docData));   // 저장된 문서의 데이터 = 안쪽 첫 부품 머리
        int nameLen = R3.U16(zip, empty.Local + 26), extraLen = R3.U16(zip, empty.Local + 28);
        int need = docData - empty.Local - 30;   // 이름 + 추가 필드 길이의 합이 이만큼이면 데이터 시작이 안쪽 머리
        byte[] broken = With(zip[..R3.CdStartOf(zip)], b =>
        {
            if (field == "이름 길이") BinaryPrimitives.WriteUInt16LittleEndian(b.AsSpan(empty.Local + 26), (ushort)(need - extraLen));
            else BinaryPrimitives.WriteUInt16LittleEndian(b.AsSpan(empty.Local + 28), (ushort)(need - nameLen));
        });

        var files = entries.Where(e => e.Data is not null).ToDictionary(e => e.Name, e => e.Data!, StringComparer.Ordinal);
        using var tmp = new TempDir("rv6shift");
        string src = WriteTo(tmp.P("깨짐.zip"), broken);
        var expect = new RepairOracle.Expect { ListMode = false, MustSalvage = new[] { "보고서.txt" } };
        var (failure, report, _) = await RepairOracle.RepairCheckAsync(RepairOracle.Of(files), src, tmp.P("복구.zip"), null, expect, tmp.Dir("읽기"));
        Assert.True(failure is null, $"{emptyName} {field}: {failure}");
        Assert.NotNull(report);
        foreach (var part in new[] { "[Content_Types].xml", "_rels/.rels", "word/document.xml" })
            Assert.DoesNotContain(part, report!.Salvaged.Concat(report.Unverified));
        Assert.StartsWith("원래 몇 개였는지 확실하지 않아요", report!.Headline, StringComparison.Ordinal);
        // 깨진 이름의 이진 바이트가 결과 글에 그대로 나오지 않는다(제어 문자는 '?', 길면 줄임)
        Assert.DoesNotContain(report.ToDisplayText(), c => char.IsControl(c) && c != '\n');
    }

    // ───────────── 작은 지적 ─────────────

    /// <summary>
    /// 계약(I4-1 검증 지적 1차 — 작은 지적): 암호 표시(bit 0)가 없는 항목의 목록 표시 칸에 깨져 켜진 강한 암호 표시(bit 6)를 고친 ZIP 에 옮기지 않는다
    /// (예전: 그대로 옮겨 Python zipfile 등이 '강한 암호'로 보고 살린 파일을 거절).
    /// </summary>
    [Fact]
    public async Task 고친_ZIP에_암호_없는_항목의_강한_암호_표시를_옮기지_않는다()
    {
        var files = new Dictionary<string, byte[]>(StringComparer.Ordinal) { ["가.txt"] = R3.Text("가", 2000), ["나.txt"] = R3.Text("나", 2500) };
        byte[] zip = NetZip(files.Select(kv => (kv.Key, (byte[]?)kv.Value)));
        var rec = Records(zip).Single(r => NameOf(zip, r) == "가.txt");
        byte[] broken = With(zip, b => b[rec.At + 8] |= 0x40);
        using var tmp = new TempDir("rv6bit6");
        var r = await ZipRepairer.Instance.RepairAsync(WriteTo(tmp.P("깨짐.zip"), broken), tmp.P("복구.zip"), null, null, CancellationToken.None).WaitAsync(Limit);
        Assert.Equal("2개를 모두 살렸어요.", r.Headline);
        byte[] fixedZip = File.ReadAllBytes(tmp.P("복구.zip"));
        foreach (var x in Records(fixedZip))
        {
            Assert.Equal(0, R3.U16(fixedZip, x.At + 8) & 0x41);
            Assert.Equal(0, R3.U16(fixedZip, x.Local + 6) & 0x41);
        }
        Assert.Equal(Sha(files), R3.ReadOutput(tmp.P("복구.zip")).ToDictionary(kv => RepairOracle.Nfc(kv.Key), kv => TreeAssert.Sha256(kv.Value), StringComparer.Ordinal));
    }

    /// <summary>
    /// 계약(I4-1 검증 지적 1차 — 작은 지적): 나눔 ZIP 의 앞 조각(.z01)을 골라 시험해도 열기처럼 세트(마지막 .zip)로 본다 — 멀쩡한 세트를 '손상'이라 하지 않는다.
    /// </summary>
    [Fact]
    public async Task 나눔_ZIP의_앞_조각을_골라_시험해도_세트로_본다()
    {
        var fx = FixtureCatalog.Get("zip_split");
        string fixtureDir = Path.GetDirectoryName(fx.FullPath)!;
        using var tmp = new TempDir("rv6z01");
        foreach (var p in Directory.GetFiles(fixtureDir, "zip_split.*")) File.Copy(p, tmp.P(Path.GetFileName(p)));
        Assert.True(File.Exists(tmp.P("zip_split.z01")));
        var report = await ArchiveTestService.Instance.TestAsync(tmp.P("zip_split.z01"), null, null, CancellationToken.None).WaitAsync(Limit);
        Assert.True(report.Ok, report.ToDisplayText());
        Assert.Equal(fx.ExpectedFiles.Count(), report.CheckedCount);
    }

    /// <summary>
    /// 계약(I4-1 검증 지적 1차 — 작은 지적): ZipCrypto 항목 하나의 암호 머리(12바이트)만 깨졌고 같은 암호로 다른 파일은 확인되면, 그 파일의 못 살린 이유에
    /// '손상됐을 수 있어요'를 붙인다(예전: 맞는 암호인데 '암호가 맞지 않아요'만). 다른 파일은 모두 살리고 암호를 다시 묻지 않는다.
    /// </summary>
    [Fact]
    public async Task 같은_암호로_다른_파일이_확인되면_암호_머리가_깨진_파일은_손상일_수_있다고_알린다()
    {
        const string pw = "1234";
        var files = new Dictionary<string, byte[]>(StringComparer.Ordinal)
        {
            ["가.txt"] = R3.Text("가", 3000), ["나.txt"] = R3.Text("나", 3000), ["다.txt"] = R3.Text("다", 3000),
        };
        using var ms = new MemoryStream();
        using (var zos = new SharpZip.ZipOutputStream(ms) { IsStreamOwner = false })
        {
            zos.Password = pw;
            foreach (var (name, data) in files)
            {
                zos.PutNextEntry(new SharpZip.ZipEntry(name) { IsUnicodeText = true, AESKeySize = 0, DateTime = new DateTime(2026, 10, 1, 9, 0, 0) });
                zos.Write(data, 0, data.Length);
                zos.CloseEntry();
            }
            zos.Finish();
        }
        byte[] zip = ms.ToArray();
        var rec = Records(zip).Single(r => NameOf(zip, r) == "나.txt");
        int dataStart = rec.Local + 30 + R3.U16(zip, rec.Local + 26) + R3.U16(zip, rec.Local + 28);
        byte[] broken = With(zip, b => b[dataStart + 5] ^= 0x5A);   // 암호 머리(12바이트) 가운데 한 바이트
        using var tmp = new TempDir("rv6pwhead");
        var r = await ZipRepairer.Instance.RepairAsync(WriteTo(tmp.P("깨짐.zip"), broken), tmp.P("복구.zip"), pw, null, CancellationToken.None).WaitAsync(Limit);
        Assert.Equal("전체 3개 중 2개를 살렸어요.", r.Headline);
        var lost = Assert.Single(r.LostItems);
        Assert.Equal("나.txt", lost.Name);
        Assert.Equal(ZipRepairer.PasswordMismatchMaybeDamaged, lost.Reason);
    }

    /// <summary>계약(I4-1 검증 지적 1차 — 작은 지적): 제어 문자가 섞인 깨진 이름은 결과 글에서 '?' 로 바뀌고 길면 줄어든다. 멀쩡한 이름은 그대로.</summary>
    [Fact]
    public void 깨진_이름은_결과_글에서_제어_문자를_감추고_줄인다()
    {
        string broken = "빈.txtPK\u0003\u0004\u0014\0\0\b" + new string('가', 300);
        var loss = new RepairLoss(broken, "이름이 깨졌어요");
        Assert.DoesNotContain(loss.Display, char.IsControl);
        Assert.True(loss.Display.Length < 100, loss.Display);
        Assert.StartsWith("빈.txtPK??", loss.Display, StringComparison.Ordinal);
        Assert.Equal("보고서 (최종) v2.txt — 이름이 깨졌어요", new RepairLoss("보고서 (최종) v2.txt", "이름이 깨졌어요").Display);

        var report = new TestReport { CheckedCount = 1, Problems = new[] { new TestProblem("\u0001eadme.txt", ZipListDamagedException.EntryReason) } };
        Assert.DoesNotContain(report.ToDisplayText(), c => char.IsControl(c) && c != '\n');
        Assert.Contains("?eadme.txt", report.ToDisplayText(), StringComparison.Ordinal);
    }
}
