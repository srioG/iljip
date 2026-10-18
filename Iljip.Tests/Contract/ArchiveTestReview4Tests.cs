using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;
using Iljip.Services;
using Iljip.Tests.TestSupport;
using Iljip.ViewModels;
using R3 = Iljip.Tests.Contract.ArchiveTestReview3Tests;

namespace Iljip.Tests.Contract;

/// <summary>
/// [IJ3-02 — 4차 검증에서 막힌 것] 압축 검사(I2-7)·오류 원인 구분(I2-8)·깨진 ZIP 고치기(I3-12):
/// 흘려 쓴 ZIP 의 끝 머리 칸이 0 인 것((f)), 저장해 담은 문서의 머리 손상이 안쪽까지 닿거나 안쪽에서 바이트가 빠진 것((h)), 그 꼬리가 깨진 목록 없는 ZIP((d)),
/// 길이 0 데이터를 '확인됨'으로 받던 것, 목록 기록의 추가 필드(AES·유니코드 이름)만 깨진 것, 목록 손상을 '암호가 맞지 않아요'로 알리던 것, 나눠진 ZIP 의 고치기 버튼.
/// </summary>
[Collection(DamageProbeCollection.Name)]
public class ArchiveTestReview4Tests
{
    private static readonly TimeSpan Limit = TimeSpan.FromSeconds(120);

    // ───────────── 재료 ─────────────

    /// <summary>
    /// 저장(방식 0)으로 담은 문서·압축 안에 또 저장해 담은 문서가 든 ZIP(4차 검증의 'net-nested' 모양 — 중첩.zip 안의 안쪽.docx).
    /// </summary>
    internal static R3.Sample DeepNested()
    {
        var files = new Dictionary<string, byte[]>(StringComparer.Ordinal)
        {
            ["가 메모.txt"] = R3.Text("가", 4000),
            ["보고서.docx"] = R3.InnerZip(("[Content_Types].xml", R3.Text("ct", 900)), ("_rels/.rels", R3.Text("rels", 600)), ("word/document.xml", R3.Text("본문", 5000))),
            ["폴더/나 메모.txt"] = R3.Text("나", 3000),
            ["표.xlsx"] = R3.InnerZip(("[Content_Types].xml", R3.Text("xct", 800)), ("xl/workbook.xml", R3.Text("통합", 2500))),
            ["중첩.zip"] = StoredZip(("안쪽.docx", R3.InnerZip(("[Content_Types].xml", R3.Text("deep", 700)))), ("안쪽.txt", R3.Text("안", 1000))),
            ["다 메모.txt"] = R3.Text("다", 3500),
            ["빈 파일.txt"] = Array.Empty<byte>(),
        };
        using var ms = new MemoryStream();
        using (var z = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
            foreach (var (name, data) in files)
            {
                var e = z.CreateEntry(name, name.EndsWith(".txt", StringComparison.Ordinal) ? CompressionLevel.Optimal : CompressionLevel.NoCompression);
                e.LastWriteTime = new DateTimeOffset(2026, 10, 2, 10, 0, 0, TimeSpan.FromHours(9));
                using var s = e.Open();
                s.Write(data);
            }
        byte[] b = ms.ToArray();
        return new R3.Sample("저장 문서 속 저장 문서", b, files, R3.HeadsOf(b), R3.CdStartOf(b));
    }

    /// <summary>항목을 저장(방식 0)으로 담은 ZIP(안쪽 압축 안에 또 저장해 담은 문서).</summary>
    private static byte[] StoredZip(params (string Name, byte[] Data)[] parts)
    {
        using var ms = new MemoryStream();
        using (var z = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
            foreach (var (name, data) in parts)
            {
                using var s = z.CreateEntry(name, CompressionLevel.NoCompression).Open();
                s.Write(data);
            }
        return ms.ToArray();
    }

    /// <summary>바깥 항목 i 의 데이터 시작(로컬 머리 + 이름 + 추가 필드 뒤).</summary>
    private static int DataStartOf(R3.Sample s, int i)
    {
        int h = s.Heads[i];
        return h + 30 + R3.U16(s.Bytes, h + 26) + R3.U16(s.Bytes, h + 28);
    }

    /// <summary>바깥 항목 i 의 기록 끝(다음 머리 또는 목록 시작).</summary>
    private static int RecordEndOf(R3.Sample s, int i) => i + 1 < s.Heads.Count ? s.Heads[i + 1] : s.CdStart;

    private static byte[] Zero(byte[] src, int at, int count)
    {
        var b = (byte[])src.Clone();
        Array.Clear(b, at, Math.Min(count, b.Length - at));
        return b;
    }

    /// <summary>저장해 담은 안쪽 압축(이름이 .txt 가 아닌 항목)의 차례.</summary>
    private static IEnumerable<int> InnerArchives(R3.Sample s) =>
        Enumerable.Range(0, s.Heads.Count).Where(i => !R3.NameAt(s, i).EndsWith(".txt", StringComparison.Ordinal));

    // ───────────── (f) 흘려 쓴 ZIP 의 끝 머리 칸이 0 ─────────────

    /// <summary>
    /// 계약((f) 4차): 흘려 쓴 ZIP(데이터 서술자 — 웹 내려받기·Java·맥에서 흔함)을 포함해, 끝 머리의 항목 수·목록 크기·위치 칸이 0 이 되거나 목록이 통째로 빠지고
    /// 그 칸들이 0 이어도 검사는 '문제없어요. 파일 0개'로 통과시키지 않고 풀기는 0개를 풀고 오류 없이 끝나지 않는다(항목 수 칸만 0 이면 목록은 온전 — 모두 풀림).
    /// </summary>
    [Fact]
    public Task 끝머리_칸이_0인_ZIP을_0개로_통과시키지_않는다() =>
        R3.RunTestCases("rv4f", new[] { R3.NetStreamed(), R3.SharpZipStreamed(), R3.SharpZipStreamed(storedInner: true), R3.Normal(), R3.Nested() }.SelectMany(s =>
        {
            int e = R3.EocdOf(s.Bytes);
            var list = new List<(R3.Sample, string, byte[])>();
            foreach (var (from, n) in new[] { (4, 4), (8, 2), (8, 10), (8, 12), (10, 2), (10, 10), (12, 4), (12, 8), (16, 4), (4, 16) })
                list.Add((s, $"{s.Id} 끝 머리 +{from}부터 {n}바이트 0", Zero(s.Bytes, e + from, n)));
            // 목록이 통째로 빠지고 끝 머리의 항목 수·크기·위치 칸도 0
            var nocd = s.Bytes[..s.CdStart].Concat(s.Bytes[e..]).ToArray();
            list.Add((s, $"{s.Id} 목록 빠짐·끝 머리 칸 모두 0", Zero(nocd, s.CdStart + 8, 12)));
            return list;
        }));

    /// <summary>계약((f) 4차 — 재현 그대로): SharpZipLib 가 흘려 쓴 ZIP 의 끝 머리 +8 부터 12바이트가 0 이면 검사는 문제(고치기 권함), 풀기는 목록 손상 오류(한국어).</summary>
    [Fact]
    public async Task 흘려_쓴_ZIP의_끝머리_칸이_모두_0이면_검사와_풀기가_목록_손상을_알린다()
    {
        using var tmp = new TempDir("rv4f1");
        var s = R3.SharpZipStreamed();
        string arc = tmp.P("흘림.zip");
        File.WriteAllBytes(arc, Zero(s.Bytes, R3.EocdOf(s.Bytes) + 8, 12));

        var report = await ArchiveTestService.Instance.TestAsync(arc, null, null, CancellationToken.None).WaitAsync(Limit);
        Assert.False(report.Ok, report.ToDisplayText());
        Assert.True(report.HasDamage);
        Assert.Contains(ZipListDamagedException.MissingMessage, report.ToDisplayText());

        var svc = new ArchiveServiceLocator().ResolveByContent(arc)!;
        var ex = await Assert.ThrowsAsync<ZipListDamagedException>(() => svc.ExtractAsync(arc, tmp.P("풀림")).WaitAsync(Limit));
        Assert.Equal(ZipListDamagedException.MissingMessage, ErrorCatalog.HeadlessMessage(ex));
        Assert.Contains(ErrorCatalog.Describe(ex, arc).Actions, a => a.Id == ErrorCatalog.RepairZipActionId);
    }

    // ───────────── (h) 저장해 담은 문서의 안쪽 부품을 바깥 파일로 살리지 않음 ─────────────

    /// <summary>
    /// 계약((h) 4차·I4-1): 저장해 담은 문서·압축(docx·xlsx·zip)의 바깥 머리 손상이 안쪽 압축의 첫 바이트들까지 닿아도(섹터 손상처럼 여러 바이트),
    /// 안쪽 부품('[Content_Types].xml'·'xl/workbook.xml' 등)을 바깥 파일로 살리지 않는다. 목록이 있으면 원래 수 = 목록 기록 수이고 그 항목 말고는 모두 살린다.
    /// 목록이 없으면 그 항목 앞의 파일은 모두 살리고 그 뒤는 조각 수로만 알려도 된다(안쪽 부품 머리는 조각으로 세어도 넣지 않음).
    /// </summary>
    [Fact]
    public Task 저장해_담은_문서의_바깥_머리_손상이_안쪽까지_닿아도_안쪽_부품을_살리지_않는다() =>
        R3.RunRepairCases("rv4h", new[] { R3.Nested(), DeepNested() }.SelectMany(s => InnerArchives(s).SelectMany(i =>
        {
            int h = s.Heads[i], dataStart = DataStartOf(s, i);
            string name = R3.NameAt(s, i);
            var cases = new List<R3.Case>();
            foreach (int from in new[] { h, h + 8, h + 16, h + 24, h + 30 })
                foreach (int inside in new[] { 1, 2, 5, 9, 13, 31 })
                    foreach (bool withCd in new[] { true, false })
                    {
                        var b = Zero(s.Bytes, from, dataStart + inside - from);
                        if (!withCd) b = b[..s.CdStart];
                        cases.Add(new R3.Case(s, $"{s.Id} {(withCd ? "목록 있음" : "목록 없음")} '{name}' 머리+{from - h}부터 안쪽 {inside}바이트까지 0", b,
                            MayLose: withCd ? new[] { name } : R3.ThisAndAfter(s, i), ListMode: withCd, TestOutput: true));
                    }
            return cases;
        })));

    /// <summary>
    /// 계약((h)·(d) 4차·I4-1): 저장해 담은 문서·압축 안에서 바이트 하나가 빠지거나 끼어도(목록이 있든 없든) 안쪽 부품을 바깥 파일로 살리지 않고, 그 앞의 파일은 모두 살린다.
    /// 뒤의 위치가 모두 한 바이트 밀려 목록을 믿을 수 없으므로(목록 위치 칸이 목록을 가리키지 않음) 훑기 모드 — 그 항목과 뒤는 조각 수로만 알려도 된다.
    /// </summary>
    [Fact]
    public Task 저장해_담은_문서_안에서_바이트가_빠지거나_끼어도_안쪽_부품은_살리지_않는다() =>
        R3.RunRepairCases("rv4hdi", new[] { R3.Nested(), DeepNested(), R3.SharpZipStreamed(storedInner: true) }.SelectMany(s => InnerArchives(s).SelectMany(i =>
        {
            int dataStart = DataStartOf(s, i), end = RecordEndOf(s, i);
            string name = R3.NameAt(s, i);
            var positions = Enumerable.Range(0, 30).Select(k => dataStart + 1 + (end - dataStart - 3) * k / 29).Distinct();
            return positions.SelectMany(at => new[] { true, false }.SelectMany(withCd => new[] { "빠짐", "낌" }.Select(op =>
            {
                byte[] b = withCd ? s.Bytes : s.Bytes[..s.CdStart];
                b = op == "빠짐" ? b[..at].Concat(b[(at + 1)..]).ToArray() : b[..at].Concat(new byte[] { 0x5A }).Concat(b[at..]).ToArray();
                return new R3.Case(s, $"{s.Id} {(withCd ? "목록 있음" : "목록 없음")} '{name}' {at}바이트 {op}", b, MayLose: R3.ThisAndAfter(s, i), TestOutput: true);
            })));
        })));

    // ───────────── (d) 끝을 모르는 안쪽 압축 뒤의 파일 ─────────────

    /// <summary>
    /// 계약((d) 4차·I4-1): 저장해 담은 문서·압축의 꼬리(안쪽 목록·끝 머리, 흘려 쓴 ZIP 이면 바깥 서술자까지)가 깨져도 목록이 있으면 그 항목 말고는 모두 살린다.
    /// 목록이 없으면 그 항목 앞의 파일은 모두 살리고, 끝을 데이터로 확인할 수 없는 그 항목 뒤 머리는 넣지 않고 조각 수로만 알린다(안쪽 부품과 가를 수 없음).
    /// </summary>
    [Fact]
    public Task 저장해_담은_문서의_꼬리가_깨져도_거짓_성공이_없다() =>
        R3.RunRepairCases("rv4d", new[] { R3.SharpZipStreamed(storedInner: true), DeepNested(), R3.Nested() }.SelectMany(s => InnerArchives(s).SelectMany(i =>
        {
            int end = RecordEndOf(s, i);
            string name = R3.NameAt(s, i);
            var cases = new List<R3.Case>();
            for (int from = end - 70; from < end; from += 5)
                foreach (int n in new[] { 4, 10, 20, 30, 45 })
                    foreach (bool withCd in new[] { true, false })
                    {
                        var b = Zero(s.Bytes, from, Math.Min(n, end - from));
                        if (!withCd) b = b[..s.CdStart];
                        cases.Add(new R3.Case(s, $"{s.Id} {(withCd ? "목록 있음" : "목록 없음")} '{name}' 꼬리 {from}부터 {Math.Min(n, end - from)}바이트 0", b,
                            MayLose: withCd ? new[] { name } : R3.ThisAndAfter(s, i), ListMode: withCd, TestOutput: true));
                    }
            return cases;
        })));

    /// <summary>
    /// 계약((d) 4차 무작위 시험·I4-1): 저장해 담은 문서·압축의 안쪽 목록 기록에서 이름·추가 필드·주석 길이 칸이 커져 그 기록의 끝이 뒤 바깥 머리를 넘어가도
    /// 목록이 있으면 그 항목 말고는 모두 살리고, 목록이 없으면 그 항목 앞은 모두 살리고 뒤는 조각 수로만 — 안쪽 부품을 바깥 파일로 살리지 않는다.
    /// </summary>
    [Fact]
    public Task 안쪽_목록_기록의_길이_칸이_커져도_거짓_성공이_없다() =>
        R3.RunRepairCases("rv4il", new[] { R3.SharpZipStreamed(storedInner: true), DeepNested(), R3.Nested() }.SelectMany(s => InnerArchives(s).SelectMany(i =>
        {
            int dataStart = DataStartOf(s, i), end = RecordEndOf(s, i);
            string name = R3.NameAt(s, i);
            var cases = new List<R3.Case>();
            for (int p = dataStart; p + 46 <= end; p++)
            {
                if (R3.U32(s.Bytes, p) != 0x02014b50) continue;
                foreach (var (field, value) in new[] { (28, 0x0300), (30, 0x00FF), (30, 0x0FF0), (32, 0x0100) })
                    foreach (bool withCd in new[] { true, false })
                    {
                        var b = (byte[])s.Bytes.Clone();
                        BinaryPrimitives.WriteUInt16LittleEndian(b.AsSpan(p + field), (ushort)value);
                        if (!withCd) b = b[..s.CdStart];
                        cases.Add(new R3.Case(s, $"{s.Id} {(withCd ? "목록 있음" : "목록 없음")} '{name}' 안쪽 목록 기록({p - dataStart}) +{field} 칸 {value}", b,
                            MayLose: withCd ? new[] { name } : R3.ThisAndAfter(s, i), ListMode: withCd, TestOutput: true));
                    }
            }
            return cases;
        })));

    /// <summary>
    /// 계약(4차 무작위 시험): 목록의 압축 크기 칸이 커져(Deflate 는 남는 바이트를 무시해 그 크기로도 확인이 맞음) 그 끝이 다음 항목 데이터 안의 머리(저장해 담은 문서의
    /// 첫 머리)에 닿아도 다음 항목을 삼키지 않고 모두 살린다(예전: '7개 중 6개를 살렸어요' — 바로 뒤 '표.xlsx' 를 못 살림).
    /// </summary>
    [Fact]
    public Task 목록의_압축_크기가_다음_항목_안쪽_머리까지_커져도_다음_항목을_살린다() =>
        R3.RunRepairCases("rv4sw", new[] { DeepNested(), R3.Nested() }.SelectMany(s => InnerArchives(s).Where(i => i > 0).SelectMany(i =>
        {
            // 앞 항목의 목록 기록에서 압축 크기를 '다음 항목(안쪽 압축)의 데이터 시작'까지로 늘린다
            int prev = i - 1, prevData = DataStartOf(s, prev), inner = DataStartOf(s, i);
            string prevName = R3.NameAt(s, prev);
            var cases = new List<R3.Case>();
            for (int p = s.CdStart; p + 46 <= s.Bytes.Length && R3.U32(s.Bytes, p) == 0x02014b50; p += 46 + R3.U16(s.Bytes, p + 28) + R3.U16(s.Bytes, p + 30) + R3.U16(s.Bytes, p + 32))
            {
                if (R3.U32(s.Bytes, p + 42) != s.Heads[prev] || R3.U16(s.Bytes, p + 10) != 8) continue;
                var b = (byte[])s.Bytes.Clone();
                BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(p + 20), (uint)(inner - prevData));
                cases.Add(new R3.Case(s, $"{s.Id} '{prevName}' 목록 압축 크기 → 다음 항목 안쪽 머리까지", b, ListMode: true, TestOutput: true));
            }
            return cases;
        })));

    // ───────────── 길이 0 데이터를 '확인됨'으로 받지 않음 ─────────────

    /// <summary>
    /// 계약(4차·I4-1): 머리의 서술자 표시(bit 3)·암호 표시(bit 0)가 바뀌거나 검사값·크기 칸이 0 이 돼도 멀쩡한 파일을 빈 파일로 '살리지' 않는다 —
    /// 목록이 있으면 목록 값으로 모두 원래 내용으로 살린다. 목록이 없으면 그 항목 앞은 모두 살리고, 그 항목은 원래 내용으로 살리거나 '확인 못 함'(암호 표시)·
    /// '못 살린 파일'로, 뒤는 조각 수로만 알려도 된다. 고친 ZIP 은 일집 검사에서 살린 파일에 문제가 없다.
    /// </summary>
    [Fact]
    public Task 머리의_표시나_크기_칸이_깨져도_빈_파일로_살리지_않는다() =>
        R3.RunRepairCases("rv4z", new[] { R3.NetStreamed(), R3.SharpZipStreamed(), R3.SharpZipStreamed(storedInner: true), R3.Normal(), R3.Nested() }.SelectMany(s =>
            Enumerable.Range(0, s.Heads.Count).SelectMany(i =>
            {
                int h = s.Heads[i];
                string name = R3.NameAt(s, i);
                var damage = new (string Label, byte[] Bytes)[]
                {
                    ("서술자 표시 바뀜", R3.Flip(s.Bytes, h + 6, 0x08)),
                    ("암호 표시 켜짐", R3.Flip(s.Bytes, h + 6, 0x01)),
                    ("검사값·크기 칸 0", Zero(s.Bytes, h + 14, 12)),
                    ("크기 칸 0", Zero(s.Bytes, h + 18, 8)),
                };
                return damage.SelectMany(d => new[] { true, false }.Select(withCd => new R3.Case(s,
                    $"{s.Id} {(withCd ? "목록 있음" : "목록 없음")} '{name}' {d.Label}", withCd ? d.Bytes : d.Bytes[..s.CdStart],
                    MayLose: withCd ? null : R3.ThisAndAfter(s, i), ListMode: withCd, TestOutput: true)));
            })));

    /// <summary>계약(4차 — 재현 그대로): 흘려 쓴 ZIP 의 첫 머리 서술자 표시만 지워지고 목록이 없어도 첫 파일을 원래 내용으로 살린다(빈 파일 아님).</summary>
    [Fact]
    public async Task 흘려_쓴_ZIP의_서술자_표시가_지워져도_원래_내용으로_살린다()
    {
        using var tmp = new TempDir("rv4z1");
        var s = R3.SharpZipStreamed();
        string src = tmp.P("흘림.zip"), dest = tmp.P("흘림_복구.zip");
        File.WriteAllBytes(src, R3.Flip(s.Bytes, s.Heads[0] + 6, 0x08)[..s.CdStart]);
        var r = await ZipRepairer.Instance.RepairAsync(src, dest, null, null, CancellationToken.None).WaitAsync(Limit);
        Assert.Empty(r.Lost);
        var got = R3.ReadOutput(dest);
        string first = R3.NameAt(s, 0);
        Assert.Equal(s.Files[first], got[first]);
        Assert.NotEmpty(got[first]);
    }

    // ───────────── 목록 기록의 추가 필드(AES·유니코드 이름)만 깨진 ZIP ─────────────

    /// <summary>목록 기록마다 추가 필드 블록(id → [시작, 끝)) — 파일·폴더 기록 모두.</summary>
    private static List<(int Id, int Start, int End)> CentralExtraBlocks(byte[] b)
    {
        int cd = R3.CdStartOf(b), e = R3.EocdOf(b);
        var blocks = new List<(int, int, int)>();
        for (int p = cd; p + 46 <= e && R3.U32(b, p) == 0x02014b50;)
        {
            int nl = R3.U16(b, p + 28), el = R3.U16(b, p + 30), cl = R3.U16(b, p + 32);
            for (int x = p + 46 + nl, end = x + el; x + 4 <= end;)
            {
                int id = R3.U16(b, x), size = R3.U16(b, x + 2);
                blocks.Add((id, x, x + 4 + size));
                x += 4 + size;
            }
            p += 46 + nl + el + cl;
        }
        return blocks;
    }

    /// <summary>원래 바이트에서 <paramref name="at"/> 이 든 목록 기록의 이름(UTF-8 표시가 없으면 CP949 — NFC, 목록 기록 밖이면 null).</summary>
    private static string? RecordNameAt(byte[] b, int at)
    {
        int cd = R3.CdStartOf(b), e = R3.EocdOf(b);
        for (int p = cd; p + 46 <= e && R3.U32(b, p) == 0x02014b50;)
        {
            int nl = R3.U16(b, p + 28);
            int len = 46 + nl + R3.U16(b, p + 30) + R3.U16(b, p + 32);
            if (at >= p && at < p + len)
                return ((R3.U16(b, p + 8) & 0x0800) != 0 ? Encoding.UTF8 : Encoding.GetEncoding(949)).GetString(b, p + 46, nl).Normalize(NormalizationForm.FormC);
            p += len;
        }
        return null;
    }

    /// <summary>바깥 로컬 머리(차례대로)와 그 이름(UTF-8 표시가 없으면 CP949 — NFC, 폴더는 끝 '/').</summary>
    private static List<(int Head, string Name)> LocalEntries(byte[] b) =>
        R3.HeadsOf(b).Select(h => (h, ((R3.U16(b, h + 6) & 0x0800) != 0 ? Encoding.UTF8 : Encoding.GetEncoding(949))
            .GetString(b, h + 30, R3.U16(b, h + 26)).Normalize(NormalizationForm.FormC))).ToList();

    /// <summary><paramref name="head"/> 머리보다 앞에 있는 파일(폴더 빼고) — 목록이 없을 때 그 머리가 깨져도 꼭 살려야 하는 것.</summary>
    private static List<string> FilesBefore(byte[] b, int head) =>
        LocalEntries(b).Where(x => x.Head < head && !x.Name.EndsWith('/')).Select(x => x.Name).ToList();

    /// <summary><paramref name="at"/> 이 든 로컬 머리(그 앞에서 가장 가까운 머리).</summary>
    private static int HeadOf(byte[] b, int at) => R3.HeadsOf(b).Where(h => h <= at).Max();

    /// <summary>압축을 풀어 파일(이름 → sha256 — NFC)만.</summary>
    private static async Task<Dictionary<string, string>> ExtractFiles(string arc, string dest, string? password)
    {
        var svc = new ArchiveServiceLocator().ResolveByContent(arc) ?? throw new UnsupportedFormatException(".zip");
        await svc.ExtractAsync(arc, dest, password).WaitAsync(Limit);
        return TreeAssert.Snapshot(dest, nfc: true).Where(kv => kv.Value is not null).ToDictionary(kv => kv.Key, kv => kv.Value!, StringComparer.Ordinal);
    }

    private static Dictionary<string, string> ExpectedFiles(FixtureInfo fx) =>
        fx.ExpectedFiles.ToDictionary(e => e.Path, e => e.Sha256!, StringComparer.Ordinal);

    private static string? Difference(Dictionary<string, string> want, Dictionary<string, string> got)
    {
        foreach (var (name, sha) in want)
            if (!got.TryGetValue(name, out var g)) return $"'{name}' 없음";
            else if (g != sha) return $"'{name}' 내용 다름";
        foreach (var name in got.Keys)
            if (!want.ContainsKey(name)) return $"원래 없던 '{name}'";
        return null;
    }

    public static TheoryData<string> ExtraFixtures => new() { "zip_default", "zip_cp949", "zip_store_l0", "zip_aes256", "zip_aes256_korean_pw" };

    /// <summary>
    /// 계약(4차·I4-1): 목록 기록의 AES 정보(0x9901)·유니코드 이름(0x7075) 바이트가 깨져도(로컬 기록은 온전) 거짓 성공이 없다 — AES 정보는 로컬 머리의 것으로 확인해 살리고,
    /// 유니코드 이름이 로컬 것과 달라지면 그 파일만 '이름 정보가 서로 달라요'(어느 쪽이 맞는지 가르지 않음). 고친 ZIP 은 맞는 암호로 일집 검사에서 살린 파일에 문제가 없고
    /// 풀면 원래 이름·내용 그대로다(예전: 깨진 기록을 그대로 옮겨 '모두 살렸어요' 인데 고친 ZIP 이 맞는 암호로도 안 열리거나 이름이 틀림).
    /// </summary>
    [Theory]
    [MemberData(nameof(ExtraFixtures))]
    public async Task 목록_기록의_추가_필드만_깨져도_고친_ZIP이_원래대로_풀린다(string id)
    {
        var fx = FixtureCatalog.Get(id);
        byte[] orig = File.ReadAllBytes(fx.FullPath);
        var want = ExpectedFiles(fx);
        var targets = CentralExtraBlocks(orig).Where(x => x.Id is 0x9901 or 0x7075)
            .SelectMany(x => x.Id == 0x9901 ? Enumerable.Range(x.Start, x.End - x.Start)
                : Enumerable.Range(x.Start, 9).Concat(Enumerable.Range(x.Start + 9, Math.Min(3, x.End - x.Start - 9))).Concat(Enumerable.Range(x.End - 3, 3)))
            .Distinct().SelectMany(at => new byte[] { 0x01, 0xFF }.Select(xr => (At: at, X: xr))).ToList();
        Assert.NotEmpty(targets);

        using var tmp = new TempDir("rv4ex");
        var failures = new System.Collections.Concurrent.ConcurrentBag<string>();
        int n = 0;
        await Parallel.ForEachAsync(targets, new ParallelOptions { MaxDegreeOfParallelism = Math.Max(2, Environment.ProcessorCount / 2) }, async (t, _) =>
        {
            string dir = tmp.Dir($"경우{Interlocked.Increment(ref n)}");
            string label = $"{id} 목록 {t.At - R3.CdStartOf(orig)}바이트째 ^{t.X:X2}";
            string src = Path.Combine(dir, "망가짐.zip"), dest = Path.Combine(dir, "복구.zip");
            File.WriteAllBytes(src, R3.Flip(orig, t.At, t.X));
            try
            {
                string? owner = RecordNameAt(orig, t.At);
                var expect = new RepairOracle.Expect
                {
                    ListMode = true,
                    MustSalvage = want.Keys.Select(RepairOracle.Nfc).Where(k => k != owner).ToList(),
                    TestOutput = true,
                };
                if (await RepairOracle.RepairAndCheckAsync(RepairOracle.Of(fx), src, dest, fx.Password, expect, dir) is { } f) failures.Add($"{label}: {f}");
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

    /// <summary>
    /// 계약(4차·I4-1): 목록에서 바이트 하나가 빠져(뒤 기록이 밀림) 목록을 믿을 수 없어도 고친 ZIP 에 원래 없던 파일(빈 파일 등)이 생기지 않고 거짓 성공이 없다
    /// (예전: 폴더 기록의 유니코드 이름이 'ata/P' 로 밀려 '9개를 모두 살렸어요' 인데 고친 ZIP 에 빈 파일이 하나 더 생김). 원래 파일은 모두 원래 이름·내용으로 살린다 —
    /// 첫 목록 기록의 서명에서 빠지면 마지막 파일만 잃어도 된다(끝 뒤가 서명이 아님).
    /// </summary>
    [Theory]
    [InlineData("zip_cp949")]
    [InlineData("zip_default")]
    public async Task 목록에서_바이트가_빠져도_고친_ZIP에_원래_없던_파일이_생기지_않는다(string id)
    {
        var fx = FixtureCatalog.Get(id);
        byte[] orig = File.ReadAllBytes(fx.FullPath);
        var want = ExpectedFiles(fx);
        int cd = R3.CdStartOf(orig);
        var cases = Enumerable.Range(cd, orig.Length - cd).Where(at => (at - cd) % 3 == 0).ToList();
        string lastFile = LocalEntries(orig).Last(x => !x.Name.EndsWith('/')).Name;
        using var tmp = new TempDir("rv4del");
        var failures = new System.Collections.Concurrent.ConcurrentBag<string>();
        int n = 0;
        await Parallel.ForEachAsync(cases, new ParallelOptions { MaxDegreeOfParallelism = Math.Max(2, Environment.ProcessorCount / 2) }, async (at, _) =>
        {
            string dir = tmp.Dir($"경우{Interlocked.Increment(ref n)}");
            string label = $"{id} 목록 {at - cd}바이트째 빠짐";
            string src = Path.Combine(dir, "망가짐.zip"), dest = Path.Combine(dir, "복구.zip");
            File.WriteAllBytes(src, orig[..at].Concat(orig[(at + 1)..]).ToArray());
            try
            {
                var expect = new RepairOracle.Expect
                {
                    MustSalvage = want.Keys.Select(RepairOracle.Nfc).Where(k => !(at < cd + 4 && k == lastFile)).ToList(),
                    TestOutput = true,
                };
                if (await RepairOracle.RepairAndCheckAsync(RepairOracle.Of(fx), src, dest, fx.Password, expect, dir) is { } f) failures.Add($"{label}: {f}");
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
        R3.AssertNoFailures(failures.OrderBy(f => f, StringComparer.Ordinal).ToList(), cases.Count);
    }

    /// <summary>로컬 머리마다 추가 필드 블록(id → [시작, 끝)) 과 그 항목 이름(UTF-8 표시가 있으면 UTF-8, 없으면 CP949 — NFC).</summary>
    private static List<(int Id, int Start, int End, string Name)> LocalExtraBlocks(byte[] b)
    {
        var blocks = new List<(int, int, int, string)>();
        foreach (int h in R3.HeadsOf(b))
        {
            int nl = R3.U16(b, h + 26), el = R3.U16(b, h + 28);
            var enc = (R3.U16(b, h + 6) & 0x0800) != 0 ? Encoding.UTF8 : Encoding.GetEncoding(949);
            string name = enc.GetString(b, h + 30, nl).Normalize(NormalizationForm.FormC);
            for (int x = h + 30 + nl, end = x + el; x + 4 <= end;)
            {
                int id = R3.U16(b, x), size = R3.U16(b, x + 2);
                blocks.Add((id, x, x + 4 + size, name));
                x += 4 + size;
            }
        }
        return blocks;
    }

    /// <summary>고치기 한 번(암호 <paramref name="password"/>)과 고친 ZIP 의 검사·풀기(맞는 암호) — 기대와 다르면 한 줄 설명.</summary>
    private static async Task<string?> RepairAndRead(FixtureInfo fx, byte[] broken, string label, string dir, string? password,
        Dictionary<string, string> want, IReadOnlyCollection<string> lost, bool allVerified)
    {
        string src = Path.Combine(dir, "망가짐.zip"), dest = Path.Combine(dir, "복구.zip");
        File.WriteAllBytes(src, broken);
        try
        {
            var r = await ZipRepairer.Instance.RepairAsync(src, dest, password, null, CancellationToken.None).WaitAsync(Limit);
            var gotLost = r.Lost.Select(n => n.Normalize(NormalizationForm.FormC)).OrderBy(n => n, StringComparer.Ordinal).ToList();
            if (!gotLost.SequenceEqual(lost.OrderBy(n => n, StringComparer.Ordinal))) return $"{label}: 못 살린 파일 [{string.Join(", ", gotLost)}] — {r.Headline}";
            if (allVerified && r.Unverified.Count > 0) return $"{label}: 확인하지 못한 파일 [{string.Join(", ", r.Unverified)}] — {r.Headline}";
            if (r.OutputPath is null) return $"{label}: 새 ZIP 없음 — {r.Headline}";
            var tr = await ArchiveTestService.Instance.TestAsync(dest, fx.Password, null, CancellationToken.None).WaitAsync(Limit);
            if (!tr.Ok) return $"{label}: 고친 ZIP 검사 '{tr.Headline}' — {r.Headline}";
            var expected = want.Where(kv => !lost.Contains(kv.Key)).ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.Ordinal);
            if (Difference(expected, await ExtractFiles(dest, Path.Combine(dir, "풀림"), fx.Password)) is { } d) return $"{label}: 고친 ZIP 을 풀면 {d} — {r.Headline}";
            return null;
        }
        catch (Exception ex) when (ex is not TimeoutException)
        {
            return $"{label}: 예외 {ex.GetType().Name} {ErrorCatalog.Describe(ex).Message}";
        }
    }

    /// <summary>
    /// 계약(4차 무작위 시험·I4-1): 목록이 없는 AES ZIP 에서 로컬 머리의 AES 정보(0x9901 블록 — 머리·길이·판·세기·안쪽 방식)가 깨지면(맞는 암호) 그 앞의 파일은 모두
    /// 원래 내용으로 살리고, 그 파일은 AES 정보를 짐작해 고쳐 쓰지 않는다(확인되면 살리고, 아니면 '못 살린 파일' — 뒤는 조각 수로만). 고친 ZIP 은 일집 검사에서 살린 파일에
    /// 문제가 없다(예전: AES 정보 없는 항목을 '확인 못 함'으로 옮겨 고친 ZIP 풀기가 통째로 실패). 첫 암호 파일의 AES 정보가 깨져 암호 확인값이 안 맞으면 틀린 암호와
    /// 가를 수 없어 '손상일 수도' 덧말과 함께 다시 묻는 것도 받는다.
    /// </summary>
    [Theory]
    [InlineData("zip_aes256")]
    [InlineData("zip_aes256_korean_pw")]
    public async Task 목록_없는_AES_ZIP의_로컬_AES_정보가_깨져도_거짓_성공이_없다(string id)
    {
        var fx = FixtureCatalog.Get(id);
        byte[] orig = File.ReadAllBytes(fx.FullPath);
        int cd = R3.CdStartOf(orig);
        var want = ExpectedFiles(fx);
        var aes = LocalExtraBlocks(orig).Where(x => x.Id == 0x9901).ToList();
        Assert.NotEmpty(aes);
        var targets = new[] { aes[0], aes[^1] }.Distinct()
            .SelectMany(x => Enumerable.Range(x.Start, x.End - x.Start)).SelectMany(at => new byte[] { 0x01, 0xFF }.Select(xr => (At: at, X: xr))).ToList();

        using var tmp = new TempDir("rv4laes");
        var failures = new System.Collections.Concurrent.ConcurrentBag<string>();
        int n = 0;
        await Parallel.ForEachAsync(targets, new ParallelOptions { MaxDegreeOfParallelism = Math.Max(2, Environment.ProcessorCount / 2) }, async (t, _) =>
        {
            string dir = tmp.Dir($"경우{Interlocked.Increment(ref n)}");
            try
            {
                string label = $"{id} 목록 없음·로컬 {t.At}바이트째 ^{t.X:X2}";
                string src = Path.Combine(dir, "망가짐.zip");
                File.WriteAllBytes(src, R3.Flip(orig, t.At, t.X)[..cd]);
                var before = FilesBefore(orig, HeadOf(orig, t.At));
                var (f, _, error) = await RepairOracle.RepairCheckAsync(RepairOracle.Of(fx), src, Path.Combine(dir, "복구.zip"), fx.Password,
                    new RepairOracle.Expect { ListMode = false, MustSalvage = before, TestOutput = true }, dir);
                bool firstEncrypted = before.Count == 0 && error is WrongPasswordException { MayBeCorrupt: true };
                if (f is not null && !firstEncrypted) failures.Add($"{label}: {f}");
            }
            finally
            {
                try { Directory.Delete(dir, true); } catch { }
            }
        });
        R3.AssertNoFailures(failures.OrderBy(f => f, StringComparer.Ordinal).ToList(), targets.Count);
    }

    /// <summary>
    /// 계약(4차·I4-1): 목록이 없는 AES ZIP 에서 로컬 AES 블록을 읽을 수 없으면(블록 머리가 깨짐·블록 전체가 0) 암호가 있든 없든 그 파일을 풀 수 없게 옮기지 않고
    /// '못 살린 파일 — 암호 정보가 깨졌어요'로 센다(AES 정보를 짐작해 고쳐 쓰지 않음). 그 앞의 파일만 옮기고(암호가 없으면 '확인 못 함'), 뒤 머리는 조각 수로만 알린다.
    /// </summary>
    [Fact]
    public async Task 암호_없이_고칠_때_AES_정보를_못_읽은_항목은_못_살린_것으로_센다()
    {
        var fx = FixtureCatalog.Get("zip_aes256");
        byte[] orig = File.ReadAllBytes(fx.FullPath);
        int cd = R3.CdStartOf(orig);
        var want = ExpectedFiles(fx);
        var aes = LocalExtraBlocks(orig).First(x => x.Id == 0x9901);
        Assert.Contains(aes.Name, want.Keys);
        using var tmp = new TempDir("rv4laes0");

        var before = FilesBefore(orig, HeadOf(orig, aes.Start));
        int after = LocalEntries(orig).Count(x => x.Head > HeadOf(orig, aes.Start));
        var failures = new List<string>();
        int n = 0;
        foreach (var (label, bytes, pw) in new[]
        {
            ("블록 머리 깨짐·암호 없음", R3.Flip(orig, aes.Start, 0xFF)[..cd], (string?)null),
            ("블록 전체 0·암호 없음", Zero(orig, aes.Start, aes.End - aes.Start)[..cd], null),
            ("블록 전체 0·맞는 암호", Zero(orig, aes.Start, aes.End - aes.Start)[..cd], fx.Password),
        })
        {
            string dir = tmp.Dir($"{++n}");
            string src = Path.Combine(dir, "망가짐.zip");
            File.WriteAllBytes(src, bytes);
            // 암호가 없으면 앞의 파일은 '확인 못 함'(살린 수에 넣지 않음) — 꼭 살려야 하는 것은 암호를 넣었을 때만
            var (f, r, _) = await RepairOracle.RepairCheckAsync(RepairOracle.Of(fx), src, Path.Combine(dir, "복구.zip"), pw,
                new RepairOracle.Expect { ListMode = false, MustSalvage = pw is null ? null : before, TestOutput = pw is not null }, dir);
            if (f is not null) { failures.Add($"{label}: {f}"); continue; }
            if (!r!.LostItems.SequenceEqual(new[] { new RepairLoss(aes.Name, "암호 정보가 깨졌어요") })) failures.Add($"{label}: 못 살린 파일 [{string.Join(", ", r.LostItems.Select(l => l.Display))}]");
            if (!r.Salvaged.Concat(r.Unverified).Select(RepairOracle.Nfc).OrderBy(x => x, StringComparer.Ordinal).SequenceEqual(before.OrderBy(x => x, StringComparer.Ordinal)))
                failures.Add($"{label}: 옮긴 파일 [{string.Join(", ", r.Salvaged.Concat(r.Unverified))}] — 앞의 파일 [{string.Join(", ", before)}]");
            if (r.UnconfirmedPieces != after) failures.Add($"{label}: 조각 {r.UnconfirmedPieces}개(뒤 머리 {after}개)");
        }
        R3.AssertNoFailures(failures, 3);
    }

    /// <summary>
    /// 계약(4차 무작위 시험): 로컬 머리나 목록 기록의 유니코드 이름 블록(0x7075)의 길이 칸이 0 이 되거나 블록이 통째로 0 이 돼도, 고친 ZIP 에 그 블록을 옮기지 않아
    /// 일집 검사 '문제없어요'·풀면 원래 이름·내용 그대로다(예전: '9개를 모두 살렸어요' 인데 고친 ZIP 검사가 그 파일에서 '알 수 없는 문제').
    /// </summary>
    [Theory]
    [InlineData("zip_zipcrypto")]
    [InlineData("zip_cp949")]
    [InlineData("zip_default")]
    public async Task 짧아진_유니코드_이름_블록을_고친_ZIP에_옮기지_않는다(string id)
    {
        var fx = FixtureCatalog.Get(id);
        byte[] orig = File.ReadAllBytes(fx.FullPath);
        var want = ExpectedFiles(fx);
        var blocks = LocalExtraBlocks(orig).Select(x => (x.Id, x.Start, x.End, Where: "로컬"))
            .Concat(CentralExtraBlocks(orig).Select(x => (x.Id, x.Start, x.End, Where: "목록"))).Where(x => x.Id == 0x7075).ToList();
        if (blocks.Count == 0) return;   // 유니코드 이름 블록이 없는 픽스처
        var cases = blocks.SelectMany(x => new[]
        {
            ($"{id} {x.Where} {x.Start}바이트 블록 길이 칸 0", Zero(orig, x.Start + 2, 2)),
            ($"{id} {x.Where} {x.Start}바이트 블록 내용까지 0", Zero(orig, x.Start + 2, x.End - x.Start - 2)),
        }).ToList();

        using var tmp = new TempDir("rv4u0");
        var failures = new System.Collections.Concurrent.ConcurrentBag<string>();
        int n = 0;
        await Parallel.ForEachAsync(cases, new ParallelOptions { MaxDegreeOfParallelism = Math.Max(2, Environment.ProcessorCount / 2) }, async (c, _) =>
        {
            string dir = tmp.Dir($"경우{Interlocked.Increment(ref n)}");
            try
            {
                if (await RepairAndRead(fx, c.Item2, c.Item1, dir, fx.Password, want, Array.Empty<string>(), allVerified: true) is { } f) failures.Add(f);
            }
            finally
            {
                try { Directory.Delete(dir, true); } catch { }
            }
        });
        R3.AssertNoFailures(failures.OrderBy(f => f, StringComparer.Ordinal).ToList(), cases.Count);
    }

    // ───────────── 검사·풀기: 목록의 추가 필드·끝 머리 손상과 암호 ─────────────

    /// <summary>검사·풀기 한 번(맞는 암호를 줌): 문제가 있으면 한 줄 설명, 없으면 null.</summary>
    private static async Task<string?> CheckTestWithPassword(FixtureInfo fx, byte[] broken, string label, string dir)
    {
        string arc = Path.Combine(dir, "검사.zip");
        File.WriteAllBytes(arc, broken);
        var want = ExpectedFiles(fx);
        TestReport? report = null;
        try
        {
            report = await ArchiveTestService.Instance.TestAsync(arc, fx.Password, null, CancellationToken.None).WaitAsync(Limit);
        }
        catch (ArchivePasswordException ex)
        {
            return $"{label}: 맞는 암호인데 검사가 암호 오류 {ex.GetType().Name} '{ErrorCatalog.Describe(ex).Message}'";
        }
        catch (Exception ex) when (ex is not TimeoutException)
        {
            return $"{label}: 검사 예외 {ex.GetType().Name} {ex.Message}";
        }
        if (report is { Ok: false, HasDamage: false })
            return $"{label}: 문제가 있다는데 고치기를 권하지 않음 — {report.Headline}";
        Dictionary<string, string>? got = null;
        Exception? extractError = null;
        try
        {
            got = await ExtractFiles(arc, Path.Combine(dir, "풀림"), fx.Password);
        }
        catch (Exception ex) when (ex is not TimeoutException)
        {
            extractError = ex;
        }
        if (extractError is ArchivePasswordException)
            return $"{label}: 맞는 암호인데 풀기가 암호 오류 '{ErrorCatalog.Describe(extractError).Message}'";
        if (extractError is not null && ErrorCatalog.Describe(extractError, arc).Actions.All(a => a.Id != ErrorCatalog.RepairZipActionId))
            return $"{label}: 풀기 오류에 고치기를 권하지 않음 {extractError.GetType().Name} '{ErrorCatalog.Describe(extractError).Message}'";
        if (got is not null && Difference(want, got) is { } d)
            return $"{label}: 풀기가 오류 없이 끝났는데 {d}";
        if (report is { Ok: true } && extractError is not null)
            return $"{label}: 검사 '{report.Headline}' 인데 풀기 오류 {extractError.GetType().Name}";
        return null;
    }

    private static async Task RunPasswordCases(string tag, FixtureInfo fx, List<(string Label, byte[] Bytes)> cases)
    {
        using var tmp = new TempDir(tag);
        var failures = new System.Collections.Concurrent.ConcurrentBag<string>();
        int n = 0;
        await Parallel.ForEachAsync(cases, new ParallelOptions { MaxDegreeOfParallelism = Math.Max(2, Environment.ProcessorCount / 2) }, async (c, _) =>
        {
            string dir = tmp.Dir($"경우{Interlocked.Increment(ref n)}");
            try
            {
                if (await CheckTestWithPassword(fx, c.Bytes, c.Label, dir) is { } f) failures.Add(f);
            }
            finally
            {
                try { Directory.Delete(dir, true); } catch { }
            }
        });
        R3.AssertNoFailures(failures.OrderBy(f => f, StringComparer.Ordinal).ToList(), cases.Count);
    }

    public static TheoryData<string> PasswordFixtures => new() { "zip_aes256", "zip_aes256_korean_pw", "zip_zipcrypto", "zip_zipcrypto_korean_pw", "zip_default", "zip_store_l0", "zip_cp949" };

    /// <summary>
    /// 계약(I2-8 4차·유니코드 이름): 목록 기록의 AES 정보·유니코드 이름 바이트가 깨지면(맞는 암호를 줌) 검사·풀기가 '암호가 맞지 않아요'로 끝나지 않고,
    /// '문제없어요'는 원래 이름·내용으로 다 풀릴 때만, 문제가 있으면 고치기를 권한다(예전: 맞는 암호에도 '암호가 맞지 않아요', 유니코드 이름 손상은 '문제없어요'인데
    /// 'sample.csw'·빈 파일로 풀림).
    /// </summary>
    [Theory]
    [MemberData(nameof(PasswordFixtures))]
    public Task 목록의_AES_정보나_유니코드_이름이_깨지면_암호_오류가_아니라_손상으로_알린다(string id)
    {
        var fx = FixtureCatalog.Get(id);
        byte[] orig = File.ReadAllBytes(fx.FullPath);
        int cd = R3.CdStartOf(orig);
        var cases = new List<(string, byte[])>();
        foreach (var (blockId, start, end) in CentralExtraBlocks(orig).Where(x => x.Id is 0x9901 or 0x7075))
        {
            foreach (int at in Enumerable.Range(start, end - start))
                foreach (byte x in new byte[] { 0x01, 0xFF })
                    cases.Add(($"{id} 목록 {at - cd}바이트째(0x{blockId:X4}) ^{x:X2}", R3.Flip(orig, at, x)));
            cases.Add(($"{id} 목록 {start - cd}바이트째(0x{blockId:X4})부터 8바이트 0", Zero(orig, start, 8)));
        }
        Assert.NotEmpty(cases);
        return RunPasswordCases("rv4pw", fx, cases);
    }

    /// <summary>
    /// 계약(I2-8 4차): 암호 ZIP 의 끝 머리 칸(항목 수·목록 크기·위치)이 깨져도 맞는 암호를 넣으면 '암호가 맞지 않아요'가 아니다 — 항목 수 칸만 0 이면 그대로 풀리고,
    /// 목록을 찾을 수 없으면 목록 손상(고치기 권함)으로 알린다.
    /// </summary>
    [Theory]
    [InlineData("zip_aes256")]
    [InlineData("zip_aes256_korean_pw")]
    [InlineData("zip_zipcrypto")]
    [InlineData("zip_zipcrypto_korean_pw")]
    public Task 암호_ZIP의_끝머리가_깨져도_맞는_암호면_암호_오류가_아니다(string id)
    {
        var fx = FixtureCatalog.Get(id);
        byte[] orig = File.ReadAllBytes(fx.FullPath);
        int e = R3.EocdOf(orig);
        var cases = new List<(string, byte[])>();
        foreach (var (from, n) in new[] { (4, 4), (8, 2), (8, 10), (8, 12), (10, 2), (12, 4), (12, 8), (16, 4) })
            cases.Add(($"{id} 끝 머리 +{from}부터 {n}바이트 0", Zero(orig, e + from, n)));
        foreach (int k in Enumerable.Range(8, 12))
            cases.Add(($"{id} 끝 머리 {k}바이트째 ^FF", R3.Flip(orig, e + k, 0xFF)));
        return RunPasswordCases("rv4eocdpw", fx, cases);
    }

    /// <summary>
    /// 계약(I2-8 4차 — 재현 그대로): AES ZIP 의 목록 기록 하나에서 AES 세기 칸만 깨지면(맞는 암호) 검사는 그 파일을 '목록 손상'으로 알리고 고치기를 권하며,
    /// 풀기는 다른 파일을 다 푼 뒤 목록 손상 오류(암호 오류 아님). 고치면 맞는 암호로 모두 풀린다.
    /// </summary>
    [Fact]
    public async Task AES_세기_칸이_깨진_목록은_암호_오류가_아니라_목록_손상이다()
    {
        var fx = FixtureCatalog.Get("zip_aes256");
        byte[] orig = File.ReadAllBytes(fx.FullPath);
        var aes = CentralExtraBlocks(orig).First(x => x.Id == 0x9901);
        using var tmp = new TempDir("rv4aes");
        string arc = tmp.P("세기.zip");
        File.WriteAllBytes(arc, R3.Flip(orig, aes.Start + 4 + 4, 0x02));   // 세기 3 → 1

        var report = await ArchiveTestService.Instance.TestAsync(arc, fx.Password, null, CancellationToken.None).WaitAsync(Limit);
        Assert.False(report.Ok);
        Assert.True(report.HasDamage);
        var problem = Assert.Single(report.Problems);
        Assert.Equal(ZipListDamagedException.EntryReason, problem.Reason);
        Assert.DoesNotContain("암호", report.ToDisplayText());

        var svc = new ArchiveServiceLocator().ResolveByContent(arc)!;
        string dest = tmp.P("풀림");
        var ex = await Assert.ThrowsAsync<ZipListDamagedException>(() => svc.ExtractAsync(arc, dest, fx.Password).WaitAsync(Limit));
        Assert.DoesNotContain("암호", ErrorCatalog.Describe(ex).Message);
        Assert.Equal(fx.ExpectedFiles.Count() - 1, TreeAssert.Snapshot(dest).Count(kv => kv.Value is not null));

        string fixedZip = tmp.P("세기_복구.zip");
        var r = await ZipRepairer.Instance.RepairAsync(arc, fixedZip, fx.Password, null, CancellationToken.None).WaitAsync(Limit);
        Assert.Empty(r.Lost);
        Assert.Empty(r.Unverified);
        Assert.Null(Difference(ExpectedFiles(fx), await ExtractFiles(fixedZip, tmp.P("고친 것 풀림"), fx.Password)));
    }

    // ───────────── 나눠진 ZIP 의 'ZIP 고치기 시도' ─────────────

    /// <summary>계약(4차): 'ZIP 고치기 시도'는 나눠진 ZIP 의 조각 이름('.zip.001'·'.z01')에도 — 다른 형식의 숫자 분할('.7z.001')에는 없다.</summary>
    [Theory]
    [InlineData(@"C:\받은 파일\자료.zip.001", true)]
    [InlineData(@"C:\받은 파일\자료.zip.002", true)]
    [InlineData(@"C:\받은 파일\자료.ZIP.001", true)]
    [InlineData(@"C:\받은 파일\자료.z01", true)]
    [InlineData(@"C:\받은 파일\자료.Z03", true)]
    [InlineData(@"C:\받은 파일\자료.zip", true)]
    [InlineData(@"C:\받은 파일\자료.7z.001", false)]
    [InlineData(@"C:\받은 파일\자료.001", false)]
    [InlineData(@"C:\받은 파일\자료.zipx", false)]
    public void 나눠진_ZIP의_조각에도_고치기를_권한다(string archive, bool expected)
    {
        Assert.Equal(expected, ZipRepairer.IsRepairableName(archive));
        var p = ErrorCatalog.Describe(CorruptArchiveException.Truncated(), archive);
        Assert.Equal(expected, p.Actions.Any(a => a.Id == ErrorCatalog.RepairZipActionId));
    }

    /// <summary>
    /// 계약(4차): 화면에서 나눠진 ZIP('자료.zip.001'·'.002')을 시험해 문제가 있으면 문제 창에 'ZIP 고치기 시도'가 있고, 고르면 세트 이름으로('자료_복구.zip') 고친다.
    /// </summary>
    [Fact]
    public async Task 화면에서_나눠진_ZIP을_시험하면_고치기를_고를_수_있다()
    {
        using var tmp = new TempDir("rv4split");
        var s = R3.Normal();
        // 둘째 파일의 데이터 한가운데 한 바이트 → 검사값 불일치
        int at = (DataStartOf(s, 1) + RecordEndOf(s, 1)) / 2;
        byte[] broken = R3.Flip(s.Bytes, at, 0x55);
        int half = broken.Length / 2;
        File.WriteAllBytes(tmp.P("자료.zip.001"), broken[..half]);
        File.WriteAllBytes(tmp.P("자료.zip.002"), broken[half..]);

        var ui = new FakeUserInteraction { SavePath = tmp.P("자료_복구.zip") };
        ui.ProblemActionAnswers.Enqueue(ErrorCatalog.RepairZipActionId);
        var vm = new MainViewModel(ui);
        await VmRunner.Run(() => vm.TestArchiveFileAsync(tmp.P("자료.zip.001")), "시험");

        var problem = Assert.Single(ui.Problems);
        Assert.Equal(ErrorCatalog.RepairZipActionId, Assert.Single(problem.Actions).Id);
        Assert.Equal("자료_복구.zip", Assert.Single(ui.SaveDefaultNames));
        Assert.Empty(ui.Infos);   // 다 살리지 못했으면 안내 창이 아니라 주의 창(I4-1 F2)
        string info = Assert.Single(ui.Warnings);
        Assert.Contains($"전체 {s.Files.Count}개 중 {s.Files.Count - 1}개를 살렸어요.", info);
        Assert.Contains("- 문서/견적서1.txt — 내용이 원래와 달라요", info);
        Assert.Contains("나눠진 조각 2개를 이어서 살폈어요.", info);
        Assert.True(File.Exists(tmp.P("자료_복구.zip")));
    }
}
