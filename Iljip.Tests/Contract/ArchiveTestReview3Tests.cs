using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;
using Iljip.Services;
using Iljip.Tests.TestSupport;
using SharpZip = ICSharpCode.SharpZipLib.Zip;

namespace Iljip.Tests.Contract;

/// <summary>
/// 망가뜨림 시험은 경우 수천 개를 나눠 동시에 돌려 CPU·스레드를 크게 쓴다 — 시간을 재는 다른 시험(잠깐 잠긴 파일 다시 해 보기 등)을 흔들지 않게 따로(다른 시험이 다 끝난 뒤) 돌린다.
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class DamageProbeCollection
{
    public const string Name = "망가뜨림 시험(따로)";
}

/// <summary>
/// [IJ3-02 — W2-08 3차 검증에서 막힌 것] 깨진 ZIP 고치기(I3-12)·압축 검사(I2-7)·오류 원인 문구(I2-8)를 바이트 하나하나 망가뜨려 보며 지킨다.
/// </summary>
[Collection(DamageProbeCollection.Name)]
public class ArchiveTestReview3Tests
{
    private static readonly TimeSpan Limit = TimeSpan.FromSeconds(120);

    // ───────────── 재료 ─────────────

    /// <summary>시험 재료 ZIP 하나: 바이트, 원래 파일(이름 → 내용), 로컬 머리 위치(차례대로), 목록 시작 위치(없으면 -1).</summary>
    internal sealed record Sample(string Id, byte[] Bytes, IReadOnlyDictionary<string, byte[]> Files, IReadOnlyList<int> Heads, int CdStart);

    /// <summary>'문서/견적서N.txt' N개(Deflate, System.IO.Compression — 목록 있음).</summary>
    internal static Sample Normal(int count = 5, int size = 6000)
    {
        var files = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        var rnd = new Random(7);
        for (int i = 0; i < count; i++)
        {
            var sb = new StringBuilder();
            while (sb.Length < size) sb.Append($"{i}번 견적 줄 {rnd.Next()} 한글 내용\n");
            files[$"문서/견적서{i}.txt"] = Encoding.UTF8.GetBytes(sb.ToString());
        }
        return Build("보통", files, stored: _ => false);
    }

    /// <summary>
    /// 저장(방식 0)으로 담긴 문서·압축(docx·xlsx·hwpx·zip — 속이 ZIP)과 보통 글 파일이 섞인 ZIP. 안쪽 ZIP 의 부품 이름('[Content_Types].xml' 등)은 바깥 파일이 아니다.
    /// </summary>
    internal static Sample Nested()
    {
        var files = new Dictionary<string, byte[]>(StringComparer.Ordinal)
        {
            ["가 메모.txt"] = Text("가", 4000),
            ["보고서.docx"] = InnerZip(("[Content_Types].xml", Text("ct", 900)), ("_rels/.rels", Text("rels", 600)), ("word/document.xml", Text("본문", 5000))),
            ["나 메모.txt"] = Text("나", 3000),
            ["표.xlsx"] = InnerZip(("[Content_Types].xml", Text("xct", 800)), ("xl/workbook.xml", Text("통합", 2500)), ("xl/worksheets/sheet1.xml", Text("시트", 6000))),
            ["한글.hwpx"] = InnerZip(("mimetype", Encoding.ASCII.GetBytes("application/hwp+zip")), ("Contents/section0.xml", Text("구역", 4000))),
            ["사진 모음.zip"] = InnerZip(("사진/1.txt", Text("하나", 2000)), ("사진/2.txt", Text("둘", 2000))),
            ["다 메모.txt"] = Text("다", 3500),
        };
        return Build("저장 문서", files, stored: n => !n.EndsWith(".txt", StringComparison.Ordinal));
    }

    /// <summary>SharpZipLib 가 되감지 못하는 스트림에 쓴 ZIP(기본 Dynamic — ZIP64 추가 필드·24바이트 서술자). <paramref name="storedInner"/> 면 둘째에 저장 방식 docx.</summary>
    internal static Sample SharpZipStreamed(bool storedInner = false)
    {
        var files = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        using var ms = new MemoryStream();
        using (var zos = new SharpZip.ZipOutputStream(new ForwardOnly(ms)) { IsStreamOwner = false })
        {
            zos.SetLevel(6);
            for (int i = 0; i < 4; i++)
            {
                string name = storedInner && i == 1 ? "폴더/보고서.docx" : $"폴더/파일{i}.txt";
                byte[] data = storedInner && i == 1
                    ? InnerZip(("[Content_Types].xml", Text("ct", 700)), ("word/document.xml", Text("본문", 3000)))
                    : Encoding.UTF8.GetBytes(string.Concat(Enumerable.Repeat($"{i} 한글 데이터 PK\x07\x08 ", 900)));
                var e = new SharpZip.ZipEntry(name) { IsUnicodeText = true, DateTime = new DateTime(2026, 10, 1, 9, 0, 0) };
                if (storedInner && i == 1) e.CompressionMethod = SharpZip.CompressionMethod.Stored;
                zos.PutNextEntry(e);
                zos.Write(data, 0, data.Length);
                zos.CloseEntry();
                files[name] = data;
            }
            zos.Finish();
        }
        byte[] b = ms.ToArray();
        return new Sample(storedInner ? "SharpZipLib 흘려쓰기(저장 docx)" : "SharpZipLib 흘려쓰기", b, files, HeadsOf(b), CdStartOf(b));
    }

    /// <summary>.NET 이 되감지 못하는 스트림에 쓴 ZIP(16바이트 서명 있는 서술자).</summary>
    internal static Sample NetStreamed()
    {
        var files = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        using var ms = new MemoryStream();
        using (var z = new ZipArchive(new ForwardOnly(ms), ZipArchiveMode.Create, leaveOpen: true))
        {
            for (int i = 0; i < 5; i++)
            {
                string name = $"흘림/파일{i}.txt";
                byte[] data = Encoding.UTF8.GetBytes(string.Concat(Enumerable.Repeat($"{i} 흘려 쓴 한글 {i * 3} ", 700)));
                using (var s = z.CreateEntry(name).Open()) s.Write(data);
                files[name] = data;
            }
        }
        byte[] b = ms.ToArray();
        return new Sample(".NET 흘려쓰기", b, files, HeadsOf(b), CdStartOf(b));
    }

    private static Sample Build(string id, Dictionary<string, byte[]> files, Func<string, bool> stored)
    {
        using var ms = new MemoryStream();
        using (var z = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
            foreach (var (name, data) in files)
            {
                var e = z.CreateEntry(name, stored(name) ? CompressionLevel.NoCompression : CompressionLevel.Optimal);
                e.LastWriteTime = new DateTimeOffset(2026, 10, 2, 10, 0, 0, TimeSpan.FromHours(9));
                using var s = e.Open();
                s.Write(data);
            }
        byte[] b = ms.ToArray();
        return new Sample(id, b, files, HeadsOf(b), CdStartOf(b));
    }

    internal static byte[] InnerZip(params (string Name, byte[] Data)[] parts)
    {
        using var ms = new MemoryStream();
        using (var z = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
            foreach (var (name, data) in parts)
            {
                using var s = z.CreateEntry(name, CompressionLevel.Optimal).Open();
                s.Write(data);
            }
        return ms.ToArray();
    }

    internal static byte[] Text(string seed, int size)
    {
        var sb = new StringBuilder();
        var rnd = new Random(seed.GetHashCode(StringComparison.Ordinal) & 0x7FFF);
        while (sb.Length < size) sb.Append($"{seed} 줄 {rnd.Next()} 내용\n");
        return Encoding.UTF8.GetBytes(sb.ToString());
    }

    /// <summary>바깥 로컬 머리 위치(목록이 가리키는 것 — 목록이 없으면 앞에서부터 머리 크기·서술자대로).</summary>
    internal static List<int> HeadsOf(byte[] b)
    {
        int e = EocdOf(b);
        int cd = CdStartOf(b);
        var heads = new List<int>();
        int n = U16(b, e + 10);
        int p = cd;
        for (int i = 0; i < n; i++)
        {
            Assert.Equal(0x02014b50u, U32(b, p));
            long off = U32(b, p + 42);
            int nl = U16(b, p + 28), el = U16(b, p + 30), cl = U16(b, p + 32);
            if (off == 0xFFFFFFFF)
            {
                // ZIP64 추가 필드(원래 크기·압축 크기·위치 중 가득 찬 칸만)
                for (int x = p + 46 + nl; x + 4 <= p + 46 + nl + el;)
                {
                    int id = U16(b, x), size = U16(b, x + 2);
                    if (id == 1)
                    {
                        int q = x + 4;
                        if (U32(b, p + 24) == 0xFFFFFFFF) q += 8;
                        if (U32(b, p + 20) == 0xFFFFFFFF) q += 8;
                        off = (long)BinaryPrimitives.ReadUInt64LittleEndian(b.AsSpan(q));
                    }
                    x += 4 + size;
                }
            }
            heads.Add((int)off);
            p += 46 + nl + el + cl;
        }
        heads.Sort();
        return heads;
    }

    internal static int EocdOf(byte[] b)
    {
        for (int i = b.Length - 22; i >= 0; i--)
            if (U32(b, i) == 0x06054b50) return i;
        throw new InvalidDataException("끝 머리 없음");
    }

    /// <summary>목록 시작(ZIP64 면 ZIP64 끝 머리의 값).</summary>
    internal static int CdStartOf(byte[] b)
    {
        int e = EocdOf(b);
        long cd = U32(b, e + 16);
        if (cd == 0xFFFFFFFF)
        {
            long z64 = (long)BinaryPrimitives.ReadUInt64LittleEndian(b.AsSpan(e - 20 + 8));
            cd = (long)BinaryPrimitives.ReadUInt64LittleEndian(b.AsSpan((int)z64 + 48));
        }
        return (int)cd;
    }

    internal static ushort U16(byte[] b, int at) => BinaryPrimitives.ReadUInt16LittleEndian(b.AsSpan(at));
    internal static uint U32(byte[] b, int at) => BinaryPrimitives.ReadUInt32LittleEndian(b.AsSpan(at));

    /// <summary>되감지 못하는 쓰기 전용 스트림(데이터 서술자를 쓰게).</summary>
    private sealed class ForwardOnly(Stream inner) : Stream
    {
        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => inner.Position; set => throw new NotSupportedException(); }
        public override void Flush() => inner.Flush();
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => inner.Write(buffer, offset, count);
    }

    // ───────────── 고치기 결과 확인 ─────────────

    /// <summary>
    /// 고친 ZIP 의 파일(이름 → 내용) — System.IO.Compression 으로 따로 읽음. 이름은 UTF-8 표시가 있으면 UTF-8,
    /// 없으면 바이트 그대로 받아(Latin1) UTF-8 로 읽히면 UTF-8, 아니면 CP949(나눠진 ZIP 픽스처처럼 표시 없는 한국어 이름).
    /// </summary>
    internal static Dictionary<string, byte[]> ReadOutput(string zip)
    {
        var got = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        using var z = new ZipArchive(File.OpenRead(zip), ZipArchiveMode.Read, leaveOpen: false, Encoding.Latin1);
        foreach (var e in z.Entries)
        {
            string name = NameOf(e.FullName);
            if (name.EndsWith('/')) continue;
            using var s = e.Open();
            using var m = new MemoryStream();
            s.CopyTo(m);
            got[name] = m.ToArray();
        }
        return got;
    }

    private static string NameOf(string read)
    {
        if (read.Any(c => c > 0xFF)) return read;   // UTF-8 표시가 있어 UTF-8 로 읽힘
        byte[] raw = Encoding.Latin1.GetBytes(read);
        try { return new UTF8Encoding(false, throwOnInvalidBytes: true).GetString(raw); }
        catch (DecoderFallbackException) { return Encoding.GetEncoding(949).GetString(raw); }
    }

    /// <summary>
    /// 고치기 한 번의 결과를 <see cref="RepairOracle"/> 의 불변식으로 본다(I4-1 【5】 — 경우마다 기대를 적지 않음): 새 ZIP = 살린 것 + 확인 못 한 것, 살린 파일은 원래 이름·내용
    /// (<paramref name="nameMayDiffer"/> 면 훑기 모드에서만 내용이 원래 것 하나와 같으면 됨 — 안쪽 부품·0바이트·틀린 내용은 걸림), 목록 모드면 원래 수 N·M+U+L=N,
    /// '모두'는 원래 파일을 모두 살렸을 때만, 훑기 모드 요약은 '원래 몇 개였는지 확실하지 않아요'. <paramref name="mayLose"/> 밖의 원래 파일은 꼭 살려야 하고(덜 살려도 되는 한계),
    /// <paramref name="listMode"/> 를 주면 그 모드여야 한다. 문제가 있으면 한 줄 설명, 없으면 null.
    /// </summary>
    internal static async Task<string?> CheckRepair(Sample s, byte[] broken, string label, TempDir tmp, int n,
        IReadOnlyCollection<string>? mayLose = null, bool? listMode = null, bool nameMayDiffer = false, bool testOutput = false)
    {
        string src = tmp.P($"망가짐{n}.zip");
        File.WriteAllBytes(src, broken);
        return await CheckRepairOf(s, src, tmp.P($"망가짐{n}_복구.zip"), label, mayLose, listMode, nameMayDiffer, testOutput);
    }

    /// <summary>
    /// <see cref="CheckRepair"/> 의 본체 — 이미 있는 원본(<paramref name="src"/> — 나눠진 ZIP 이면 그 조각)을 고쳐 본다. 끝나면 원본·결과를 지운다.
    /// <paramref name="testOutput"/> 면 고친 ZIP 을 일집 검사로도 본다(살린 파일에 문제가 없어야 — 확인 못 한 파일만 문제일 수 있음).
    /// </summary>
    private static async Task<string?> CheckRepairOf(Sample s, string src, string dest, string label,
        IReadOnlyCollection<string>? mayLose = null, bool? listMode = null, bool nameMayDiffer = false, bool testOutput = false, int folders = 0)
    {
        string work = dest + "_읽기";
        try
        {
            var expect = new RepairOracle.Expect
            {
                ListMode = listMode,
                MustSalvage = s.Files.Keys.Where(k => !(mayLose?.Contains(k) ?? false)).ToList(),
                NameMayDiffer = nameMayDiffer,
                TestOutput = testOutput,
            };
            Directory.CreateDirectory(work);
            return await RepairOracle.RepairAndCheckAsync(RepairOracle.Of(s.Files, folders), src, dest, null, expect, work) is { } f ? $"{label}: {f}" : null;
        }
        finally
        {
            try { File.Delete(src); } catch { }
            try { File.Delete(dest); } catch { }
            try { Directory.Delete(work, true); } catch { }
        }
    }

    /// <summary>바깥 항목 i 와 그 뒤 항목들의 이름 — 목록이 없을 때 i 가 깨지면 잃어도 되는 것(훑기 모드는 끝을 믿을 수 없는 항목 뒤를 조각으로만 알림).</summary>
    internal static string[] ThisAndAfter(Sample s, int i) => Enumerable.Range(i, s.Heads.Count - i).Select(k => NameAt(s, k)).ToArray();

    /// <summary>원래 바이트에서 <paramref name="at"/> 이 든 목록 기록의 이름(목록 기록 밖이면 null).</summary>
    internal static string? RecordFileAt(Sample s, int at)
    {
        for (int p = s.CdStart; p + 46 <= s.Bytes.Length && U32(s.Bytes, p) == 0x02014b50;)
        {
            int len = 46 + U16(s.Bytes, p + 28) + U16(s.Bytes, p + 30) + U16(s.Bytes, p + 32);
            if (at >= p && at < p + len) return Encoding.UTF8.GetString(s.Bytes, p + 46, U16(s.Bytes, p + 28));
            p += len;
        }
        return null;
    }

    /// <summary>
    /// 목록·끝 머리 바이트 <paramref name="at"/> 이 깨졌을 때 잃어도 되는 것: 그 바이트가 든 목록 기록의 파일(목록 모드 — 기록이 가리키는 머리가 없거나 이름이 다름),
    /// 첫 목록 기록의 서명이면 마지막 파일(목록을 믿지 않아 훑기 모드 — 마지막 파일 끝 뒤가 서명이 아니어서 끝을 믿을 수 없음).
    /// </summary>
    internal static string[] CentralDamageMayLose(Sample s, int at) =>
        new[] { RecordFileAt(s, at), at < s.CdStart + 4 ? NameAt(s, s.Heads.Count - 1) : null }.OfType<string>().ToArray();

    private static IEnumerable<Sample> RepairSamples() => new[] { Normal(), Nested(), SharpZipStreamed(), SharpZipStreamed(storedInner: true), NetStreamed() };

    internal static void AssertNoFailures(List<string> failures, int tried)
    {
        Assert.True(failures.Count == 0, $"{failures.Count}/{tried} 실패:\n  " + string.Join("\n  ", failures.Take(40)));
    }

    /// <summary>망가뜨린 경우 하나(설명, 바이트, 잃어도 되는 파일 — 나머지는 꼭 살려야, 목록 모드여야 하나, 이름이 달라도 되나(훑기 모드), 고친 ZIP 도 검사하나).</summary>
    internal sealed record Case(Sample S, string Label, byte[] Bytes, IReadOnlyCollection<string>? MayLose = null, bool? ListMode = null, bool NameMayDiffer = false,
        bool TestOutput = false);

    /// <summary>경우들을 나눠 동시에 돌려 실패를 모은다.</summary>
    internal static async Task RunRepairCases(string tag, IEnumerable<Case> cases)
    {
        using var tmp = new TempDir(tag);
        var list = cases.ToList();
        var failures = new System.Collections.Concurrent.ConcurrentBag<string>();
        int n = 0;
        await Parallel.ForEachAsync(list, new ParallelOptions { MaxDegreeOfParallelism = Math.Max(2, Environment.ProcessorCount / 2) }, async (c, _) =>
        {
            int k = Interlocked.Increment(ref n);
            if (await CheckRepair(c.S, c.Bytes, c.Label, tmp, k, c.MayLose, c.ListMode, c.NameMayDiffer, c.TestOutput) is { } f) failures.Add(f);
        });
        AssertNoFailures(failures.OrderBy(f => f, StringComparer.Ordinal).ToList(), list.Count);
    }

    internal static byte[] Flip(byte[] src, int at, byte x)
    {
        var b = (byte[])src.Clone();
        b[at] ^= x;
        return b;
    }

    /// <summary>바깥 항목 i 의 이름(원래 파일 이름 — 머리 차례).</summary>
    internal static string NameAt(Sample s, int i)
    {
        int h = s.Heads[i];
        int nl = U16(s.Bytes, h + 26);
        return Encoding.UTF8.GetString(s.Bytes, h + 30, nl);
    }

    // ───────────── 고치기: 목록·끝 머리 바이트 하나씩 ─────────────

    /// <summary>
    /// 계약((c)·I4-1): 목록·끝 머리의 바이트 하나가 깨져도(로컬 기록은 온전) 거짓 성공이 없다 — 살린 파일은 원래 이름·내용, 목록 모드면 원래 수 = 목록의 파일 기록 수,
    /// 빠뜨리고 '모두 살렸어요' 하지 않는다. 잃어도 되는 것은 깨진 기록의 파일(가리키는 머리가 없거나 이름이 다름)과, 첫 기록 서명이 깨져 훑기 모드가 될 때의
    /// 마지막 파일(끝 뒤가 서명이 아님)뿐 — 나머지는 모두 살린다.
    /// </summary>
    [Fact]
    public Task 목록_끝머리_바이트_하나가_깨져도_거짓_성공이_없다() =>
        RunRepairCases("rv3cd", RepairSamples().SelectMany(s =>
            Enumerable.Range(s.CdStart, s.Bytes.Length - s.CdStart).SelectMany(at => new byte[] { 0xFF, 0x01 }.Select(x =>
                new Case(s, $"{s.Id} 목록 {at - s.CdStart}바이트째 ^{x:X2}", Flip(s.Bytes, at, x), MayLose: CentralDamageMayLose(s, at))))));

    /// <summary>
    /// 계약((h)·(c)·I4-1): 로컬 머리 바이트 하나가 깨져도 저장 방식으로 담긴 문서·압축(docx·xlsx·hwpx·zip)의 안쪽 부품을 바깥 파일로 살리지 않는다.
    /// 목록이 있으면(목록 모드) 원래 수 = 목록 기록 수이고 깨진 항목 말고는 모두 살린다. 목록이 없으면(훑기 모드) 깨진 항목 앞의 파일은 모두 살리고,
    /// 깨진 항목과 그 뒤는 끝을 믿을 수 있을 때만(뒤 머리는 조각 수로만 알려도 됨).
    /// </summary>
    [Fact]
    public Task 로컬_머리_바이트_하나가_깨져도_다른_파일과_수가_맞다() =>
        RunRepairCases("rv3lh", RepairSamples().SelectMany(s => Enumerable.Range(0, s.Heads.Count).SelectMany(i =>
        {
            int h = s.Heads[i];
            int end = h + 30 + U16(s.Bytes, h + 26);
            string name = NameAt(s, i);
            return Enumerable.Range(h, end - h).SelectMany(at => new[] { true, false }.Select(withCd =>
            {
                var b = Flip(s.Bytes, at, 0xFF);
                if (!withCd) b = b[..s.CdStart];
                bool nameByte = at >= h + 30;
                return new Case(s, $"{s.Id} {(withCd ? "목록 있음" : "목록 없음")} 머리{i} {at - h}바이트째", b,
                    MayLose: withCd ? new[] { name } : ThisAndAfter(s, i), ListMode: withCd, NameMayDiffer: !withCd && nameByte);
            }));
        })));

    /// <summary>
    /// 계약((b)·(d)·I4-1): 흘려 쓴 ZIP(서술자)의 서술자 바이트 하나가 깨져도 목록이 있으면 목록 값으로 모두 살린다(서술자는 쓰지 않음).
    /// 목록이 없으면 그 항목 앞의 파일은 모두 살리고, 그 항목과 뒤는 끝을 믿을 수 있을 때만.
    /// </summary>
    [Fact]
    public Task 서술자_바이트_하나가_깨져도_다른_파일을_살린다() =>
        RunRepairCases("rv3dd", RepairSamples().Where(s => (U16(s.Bytes, s.Heads[0] + 6) & 8) != 0).SelectMany(s => Enumerable.Range(0, s.Heads.Count).SelectMany(i =>
        {
            int next = i + 1 < s.Heads.Count ? s.Heads[i + 1] : s.CdStart;
            int descLen = U32(s.Bytes, next - 24) == 0x08074b50 ? 24 : 16;
            return Enumerable.Range(next - descLen, descLen).SelectMany(at => new[] { true, false }.Select(withCd =>
            {
                var b = Flip(s.Bytes, at, 0xFF);
                if (!withCd) b = b[..s.CdStart];
                return new Case(s, $"{s.Id} {(withCd ? "목록 있음" : "목록 없음")} 서술자{i} {at - (next - descLen)}바이트째", b,
                    MayLose: withCd ? null : ThisAndAfter(s, i), ListMode: withCd);
            }));
        })));

    /// <summary>
    /// 계약((c)·(d)·(h)): 어디에서 잘려도 잘린 자리 앞에서 끝난 파일은 모두 원래대로 살리고, 원래 없던 파일(안쪽 부품)을 살리지 않으며,
    /// 다 찾지 못했을 수 있으면 '모두 살렸어요'라고 하지 않는다.
    /// </summary>
    [Fact]
    public Task 어디에서_잘려도_앞의_파일을_살리고_모두라고_하지_않는다() =>
        RunRepairCases("rv3cut", RepairSamples().SelectMany(s =>
        {
            var cuts = Enumerable.Range(1, 59).Select(k => s.Bytes.Length * k / 60)
                .Concat(s.Heads.SelectMany(h => new[] { h - 1, h, h + 1, h + 31 })).Concat(new[] { s.CdStart, s.CdStart + 1, s.Bytes.Length - 1 })
                .Where(c => c > 0 && c < s.Bytes.Length).Distinct();
            return cuts.Select(cut =>
            {
                // 잘린 자리를 넘는 항목(그다음 머리가 잘린 자리 뒤)은 잃어도 됨
                var mayLose = Enumerable.Range(0, s.Heads.Count)
                    .Where(i => (i + 1 < s.Heads.Count ? s.Heads[i + 1] : s.CdStart) > cut).Select(i => NameAt(s, i)).ToList();
                return new Case(s, $"{s.Id} {cut}바이트에서 잘림", s.Bytes[..cut], MayLose: mayLose, ListMode: false);
            });
        }));

    /// <summary>
    /// 계약((c) 나눠진 ZIP·I4-1): PKZIP 나눔('zip_split.z01·.z02·.zip')의 마지막 조각에 든 목록·끝 머리의 바이트 하나가 깨져도 '.zip' 을 골라 고치면 거짓 성공이 없고
    /// (살린 파일은 원래 이름·내용, 목록 모드면 원래 수 = 목록의 파일 기록 수, 빠뜨리고 '모두' 하지 않음), 깨진 기록의 파일·(첫 기록 서명이면) 마지막 파일 말고는 모두 살린다.
    /// </summary>
    [Fact]
    public async Task 나눠진_ZIP의_목록_바이트_하나가_깨져도_거짓_성공이_없다()
    {
        var fx = FixtureCatalog.Get("zip_split");
        using var tmp = new TempDir("rv3split");
        var volumes = fx.SplitVolumes.Select(v => (Name: Path.GetFileName(v), Bytes: File.ReadAllBytes(FixtureCatalog.PathOf(v)))).ToList();
        var last = volumes[^1];
        Assert.EndsWith(".zip", last.Name, StringComparison.OrdinalIgnoreCase);

        // 원래 파일(이름 → 내용) — 온전한 세트를 일집으로 풀어 읽는다
        string whole = tmp.Dir("온전");
        foreach (var (name, bytes) in volumes) File.WriteAllBytes(Path.Combine(whole, name), bytes);
        string extracted = tmp.P("풀림");
        await Svc.For(Path.Combine(whole, last.Name)).ExtractAsync(Path.Combine(whole, last.Name), extracted).WaitAsync(Limit);
        var files = Directory.EnumerateFiles(extracted, "*", SearchOption.AllDirectories)
            .ToDictionary(p => Path.GetRelativePath(extracted, p).Replace('\\', '/'), File.ReadAllBytes, StringComparer.Ordinal);
        Assert.Equal(fx.ExpectedFiles.Count(), files.Count);

        // 목록이 마지막 조각 안에 있어야(이 픽스처의 모양)
        int e = EocdOf(last.Bytes);
        Assert.Equal(U16(last.Bytes, e + 4), U16(last.Bytes, e + 6));
        int cdStart = (int)U32(last.Bytes, e + 16);
        var sample = new Sample("나눠진 ZIP", last.Bytes, files, Array.Empty<int>(), cdStart);

        // 목록 기록마다 [시작, 끝)·이름(표시가 없으면 CP949)·가리키는 머리(조각 번호, 위치)
        var records = new List<(int From, int To, string Name, int Disk, long Local)>();
        for (int p = cdStart; p + 46 <= e && U32(last.Bytes, p) == 0x02014b50;)
        {
            int nl = U16(last.Bytes, p + 28);
            int to = p + 46 + nl + U16(last.Bytes, p + 30) + U16(last.Bytes, p + 32);
            var enc = (U16(last.Bytes, p + 8) & 0x0800) != 0 ? Encoding.UTF8 : Encoding.GetEncoding(949);
            records.Add((p, to, enc.GetString(last.Bytes, p + 46, nl).Normalize(NormalizationForm.FormC), U16(last.Bytes, p + 34), U32(last.Bytes, p + 42)));
            p = to;
        }
        Assert.Equal(U16(last.Bytes, e + 10), records.Count);
        int folders = records.Count(r => r.Name.EndsWith('/'));
        string lastFile = records.Where(r => !r.Name.EndsWith('/')).OrderBy(r => r.Disk).ThenBy(r => r.Local).Last().Name;
        Assert.Contains(lastFile, files.Keys);
        string[] MayLose(int at) => records.Where(r => at >= r.From && at < r.To).Select(r => r.Name)
            .Concat(at < cdStart + 4 ? new[] { lastFile } : Array.Empty<string>()).ToArray();

        var cases = Enumerable.Range(cdStart, last.Bytes.Length - cdStart).SelectMany(at => new byte[] { 0xFF, 0x01 }.Select(x => (at, x))).ToList();
        var failures = new System.Collections.Concurrent.ConcurrentBag<string>();
        int n = 0;
        await Parallel.ForEachAsync(cases, new ParallelOptions { MaxDegreeOfParallelism = Math.Max(2, Environment.ProcessorCount / 2) }, async (c, _) =>
        {
            string dir = tmp.Dir($"세트{Interlocked.Increment(ref n)}");
            foreach (var (name, bytes) in volumes) File.WriteAllBytes(Path.Combine(dir, name), name == last.Name ? Flip(bytes, c.at, c.x) : bytes);
            string label = $"나눠진 ZIP 목록 {c.at - cdStart}바이트째 ^{c.x:X2}";
            if (await CheckRepairOf(sample, Path.Combine(dir, last.Name), Path.Combine(dir, "복구.zip"), label, mayLose: MayLose(c.at), folders: folders) is { } f)
                failures.Add(f);
            try { Directory.Delete(dir, true); } catch { }
        });
        AssertNoFailures(failures.OrderBy(f => f, StringComparer.Ordinal).ToList(), cases.Count);
    }

    // ───────────── 검사·풀기: 목록이 깨진 ZIP ─────────────

    /// <summary>
    /// 검사·풀기 한 번: 검사가 '문제없어요'면 풀기도 원래 파일을 모두 원래 내용으로 풀어야 하고, 풀기가 오류 없이 끝났으면 원래 파일이 모두 원래 내용으로 있어야 한다
    /// (말없이 파일을 빠뜨리거나 다른 내용으로 풀지 않음). 검사가 문제를 찾았으면 손상이면 고치기를 권해야. 문제가 있으면 한 줄 설명.
    /// </summary>
    internal static async Task<string?> CheckTestAndExtract(Sample s, byte[] broken, string label, TempDir tmp, int n)
    {
        string arc = tmp.P($"검사{n}.zip");
        string dest = tmp.P($"풀림{n}");
        File.WriteAllBytes(arc, broken);
        try
        {
            TestReport? report = null;
            try
            {
                report = await ArchiveTestService.Instance.TestAsync(arc, null, null, CancellationToken.None).WaitAsync(Limit);
            }
            catch (ArchiveException)
            {
                // 형식 미지원·암호 등 — 결과 대신 예외(검사가 '문제없어요'라고 하지 않음)
            }
            var svc = new ArchiveServiceLocator().ResolveByContent(arc);
            Exception? ex = null;
            if (svc is null) ex = new UnsupportedFormatException(".zip");
            else
            {
                try { await svc.ExtractAsync(arc, dest).WaitAsync(Limit); }
                catch (Exception e) { ex = e; }
            }
            var got = Directory.Exists(dest)
                ? TreeAssert.Snapshot(dest).Where(kv => kv.Value is not null).ToDictionary(kv => kv.Key, kv => kv.Value!, StringComparer.Ordinal)
                : new Dictionary<string, string>();
            bool complete = s.Files.Count == got.Count && s.Files.All(kv => got.TryGetValue(kv.Key, out var sha) && sha == TreeAssert.Sha256(kv.Value));
            if (report is { Ok: true } && !complete)
                return $"{label}: 검사 '{report.Headline}' 인데 풀면 {got.Count}개(원래 {s.Files.Count}개){(ex is null ? "" : $" — {ex.GetType().Name}")}";
            if (ex is null && !complete)
                return $"{label}: 풀기가 오류 없이 끝났는데 {got.Count}개(원래 {s.Files.Count}개)";
            foreach (var (name, sha) in got)
                if (!s.Files.TryGetValue(name, out var want) || TreeAssert.Sha256(want) != sha)
                    return $"{label}: 원래와 다른 파일이 풀림 '{name}'";
            if (report is { Ok: false } && !report.HasDamage && report.Problems.All(p => p.Kind is ProblemKind.Corrupt or ProblemKind.Truncated))
                return $"{label}: 손상인데 고치기를 권하지 않음 — {report.Headline}";
            return null;
        }
        finally
        {
            try { File.Delete(arc); } catch { }
            try { if (Directory.Exists(dest)) Directory.Delete(dest, true); } catch { }
        }
    }

    internal static async Task RunTestCases(string tag, IEnumerable<(Sample S, string Label, byte[] Bytes)> cases)
    {
        using var tmp = new TempDir(tag);
        var list = cases.ToList();
        var failures = new System.Collections.Concurrent.ConcurrentBag<string>();
        int n = 0;
        await Parallel.ForEachAsync(list, new ParallelOptions { MaxDegreeOfParallelism = Math.Max(2, Environment.ProcessorCount / 2) }, async (c, _) =>
        {
            int k = Interlocked.Increment(ref n);
            if (await CheckTestAndExtract(c.S, c.Bytes, c.Label, tmp, k) is { } f) failures.Add(f);
        });
        AssertNoFailures(failures.OrderBy(f => f, StringComparer.Ordinal).ToList(), list.Count);
    }

    /// <summary>
    /// 계약((e)): 목록·끝 머리의 바이트 하나가 깨진 ZIP 을 검사·풀 때, '문제없어요'·오류 없는 풀기는 원래 파일을 모두 원래 내용으로 풀 수 있을 때만 —
    /// 목록이 깨져 파일이 빠지면(0개여도) '문제가 있어요'(고치기 권함)·풀기는 오류.
    /// </summary>
    [Fact]
    public Task 목록이_깨진_ZIP을_검사_풀기가_말없이_넘기지_않는다() =>
        RunTestCases("rv3tcd", new[] { Normal(), Nested(), NetStreamed(), SharpZipStreamed() }.SelectMany(s =>
            Enumerable.Range(s.CdStart, s.Bytes.Length - s.CdStart).SelectMany(at => new byte[] { 0xFF, 0x01 }.Select(x =>
                (s, $"{s.Id} 목록 {at - s.CdStart}바이트째 ^{x:X2}", Flip(s.Bytes, at, x))))));

    /// <summary>
    /// 계약((f)): 목록이 통째로 없거나(끝 머리는 남음), 끝 머리의 목록 위치 칸이 끝 머리 안·파일 밖·데이터 한가운데를 가리키는 ZIP 은
    /// 검사가 '문제없어요. 파일 0개'로 통과시키지 않고, 풀기는 0개를 풀고 오류 없이 끝나지 않는다.
    /// </summary>
    [Fact]
    public Task 목록이_없거나_위치가_엉뚱한_ZIP은_0개로_통과시키지_않는다() =>
        RunTestCases("rv3tf", new[] { Normal(), Nested(), NetStreamed() }.SelectMany(s =>
        {
            int e = EocdOf(s.Bytes);
            byte[] eocd = s.Bytes[e..];
            var list = new List<(Sample, string, byte[])>();
            // 목록만 통째로 빠짐(끝 머리는 그대로 — 위치 칸은 이제 끝 머리를 가리킴)
            list.Add((s, $"{s.Id} 목록 통째로 빠짐", s.Bytes[..s.CdStart].Concat(eocd).ToArray()));
            // 위치 칸이 끝 머리 안(0~21바이트)·파일 밖·0·데이터 한가운데
            foreach (int k in Enumerable.Range(0, 22))
                list.Add((s, $"{s.Id} 위치 칸이 끝 머리 {k}바이트째", WithCdOffset(s.Bytes, e + k)));
            list.Add((s, $"{s.Id} 위치 칸이 파일 밖", WithCdOffset(s.Bytes, s.Bytes.Length + 100)));
            list.Add((s, $"{s.Id} 위치 칸이 0", WithCdOffset(s.Bytes, 0)));
            list.Add((s, $"{s.Id} 위치 칸이 데이터 한가운데", WithCdOffset(s.Bytes, s.Heads[1] + 77)));
            // 목록만 빠지고 끝 머리의 위치·크기 칸도 0
            var zeroed = s.Bytes[..s.CdStart].Concat(eocd).ToArray();
            BinaryPrimitives.WriteUInt32LittleEndian(zeroed.AsSpan(s.CdStart + 12), 0);
            BinaryPrimitives.WriteUInt32LittleEndian(zeroed.AsSpan(s.CdStart + 16), 0);
            list.Add((s, $"{s.Id} 목록 빠짐·크기 0·위치 0", zeroed));
            return list;
        }));

    // ───────────── 검사·풀기: 잘린 EGG·ALZ ─────────────

    private static List<RawEgg.Item> EggItems(string? password = null) => new()
    {
        new("자료/", null),
        new("자료/가.txt", Text("가", 1500), Password: password),
        new("자료/빈 파일.txt", Array.Empty<byte>(), Password: password),
        new("자료/나.txt", Text("나", 900), Password: password),
        new("다.txt", Text("다", 1200), Password: password),
    };

    /// <summary>잘린 압축 하나를 검사·풀어 본다: 검사가 '문제없어요'이거나 풀기가 오류 없이 끝나면 실패 한 줄.</summary>
    private static async Task<string?> CheckTruncated(string arc, string label, string? password, TempDir tmp, int n)
    {
        TestReport? report = null;
        Exception? testEx = null;
        try
        {
            report = await ArchiveTestService.Instance.TestAsync(arc, password, null, CancellationToken.None).WaitAsync(Limit);
        }
        catch (Exception ex) when (ex is not TimeoutException)
        {
            testEx = ex;
        }
        if (report is { Ok: true }) return $"{label}: 검사 '{report.Headline}'";
        string dest = tmp.P($"풀림{n}");
        var svc = new ArchiveServiceLocator().ResolveByContent(arc);
        if (svc is not null)
        {
            var ex = await Svc.Catch(() => svc.ExtractAsync(arc, dest, password).WaitAsync(Limit));
            if (ex is null) return $"{label}: 풀기가 오류 없이 끝남(파일 {TreeAssert.Snapshot(dest).Count(kv => kv.Value is not null)}개)";
            if (ex is TimeoutException) return $"{label}: 풀기가 끝나지 않음";
        }
        if (testEx is TimeoutException) return $"{label}: 검사가 끝나지 않음";
        return null;
    }

    /// <summary>
    /// 계약((a)): EGG(보통·암호)·ALZ 를 어느 바이트에서 잘라도 검사는 '문제없어요'가 아니고 풀기는 오류로 끝난다(말없이 뒤쪽 파일을 잃지 않음).
    /// </summary>
    [Fact]
    public async Task 잘린_EGG_ALZ는_어디서_잘라도_문제없다고_하지_않는다()
    {
        using var tmp = new TempDir("rv3egg");
        var whole = new List<(string Name, byte[] Bytes, string? Password)>();
        foreach (var (tag, pw) in new[] { ("보통", (string?)null), ("암호", "pass1234") })
        {
            string p = tmp.P($"원본_{tag}.egg");
            RawEgg.Write(p, EggItems(pw));
            whole.Add(($"EGG {tag}", File.ReadAllBytes(p), pw));
        }
        string alz = tmp.P("원본.alz");
        RawAlz.Write(alz, new RawAlz.Item(Encoding.ASCII.GetBytes("folder/"), null), new RawAlz.Item(Encoding.ASCII.GetBytes("folder/a.txt"), Text("a", 1300)),
            new RawAlz.Item(Encoding.ASCII.GetBytes("b.txt"), Text("b", 900)), new RawAlz.Item(Encoding.ASCII.GetBytes("empty.txt"), Array.Empty<byte>()));
        whole.Add(("ALZ", File.ReadAllBytes(alz), null));

        var failures = new System.Collections.Concurrent.ConcurrentBag<string>();
        int tried = 0, n = 0;
        var jobs = new List<(string Arc, string Label, string? Pw)>();
        foreach (var (name, bytes, pw) in whole)
        {
            string ext = name.StartsWith("ALZ", StringComparison.Ordinal) ? ".alz" : ".egg";
            // 원본은 문제없어야(기준)
            string ok = tmp.P($"{name}_온전{ext}");
            File.WriteAllBytes(ok, bytes);
            var baseReport = await ArchiveTestService.Instance.TestAsync(ok, pw, null, CancellationToken.None).WaitAsync(Limit);
            Assert.True(baseReport.Ok, $"{name}: 원본 — {baseReport.ToDisplayText()}");
            for (int cut = 1; cut < bytes.Length; cut++)
            {
                string arc = tmp.P($"{name}_{cut}{ext}");
                File.WriteAllBytes(arc, bytes[..cut]);
                jobs.Add((arc, $"{name} {cut}/{bytes.Length}바이트", pw));
            }
        }
        await Parallel.ForEachAsync(jobs, new ParallelOptions { MaxDegreeOfParallelism = Math.Max(2, Environment.ProcessorCount / 2) }, async (j, _) =>
        {
            Interlocked.Increment(ref tried);
            if (await CheckTruncated(j.Arc, j.Label, j.Pw, tmp, Interlocked.Increment(ref n)) is { } f) failures.Add(f);
        });
        AssertNoFailures(failures.OrderBy(f => f, StringComparer.Ordinal).ToList(), tried);
    }

    /// <summary>
    /// 계약((a)): 나눠진 EGG(.vol1~3)의 어느 조각이 잘려도(가운데 조각 포함) 검사는 '문제없어요'가 아니고 풀기는 오류로 끝난다.
    /// </summary>
    [Fact]
    public async Task 나눠진_EGG의_어느_조각이_잘려도_문제없다고_하지_않는다()
    {
        using var tmp = new TempDir("rv3eggsplit");
        string srcDir = tmp.Dir("원본");
        var vols = RawEgg.WriteSplit(Path.Combine(srcDir, "나눔"), EggItems(), 3);
        var baseReport = await ArchiveTestService.Instance.TestAsync(vols[0], null, null, CancellationToken.None).WaitAsync(Limit);
        Assert.True(baseReport.Ok, baseReport.ToDisplayText());
        var failures = new List<string>();
        int tried = 0;
        for (int v = 0; v < vols.Count; v++)
        {
            byte[] bytes = File.ReadAllBytes(vols[v]);
            for (int cut = 1; cut < bytes.Length; cut += Math.Max(1, bytes.Length / 80))
            {
                string dir = tmp.Dir($"조각{v}_{cut}");
                foreach (var p in vols) File.Copy(p, Path.Combine(dir, Path.GetFileName(p)));
                File.WriteAllBytes(Path.Combine(dir, Path.GetFileName(vols[v])), bytes[..cut]);
                tried++;
                if (await CheckTruncated(Path.Combine(dir, Path.GetFileName(vols[0])), $"vol{v + 1} {cut}/{bytes.Length}바이트", null, tmp, tried) is { } f) failures.Add(f);
            }
        }
        AssertNoFailures(failures, tried);
    }

    // ───────────── 오류 문구: 작은따옴표와 한글이 이어지는 경로 ─────────────

    /// <summary>작은따옴표(')가 든 경로들 — 폴더 '26년 견적(따옴표 뒤에 한글), 김's 자료, 끝이 따옴표인 폴더.</summary>
    public static TheoryData<string> QuotePaths => new()
    {
        @"C:\자료\'26년 견적\보고서.zip",
        @"D:\받은 파일\'26년 견적\'27년\자료 묶음.zip",
        @"C:\Users\김's 자료\견적.zip",
        @"C:\자료\견적서'\a.zip",
        @"\\nas\공유\'26년\보고.zip",
    };

    /// <summary>.NET·운영 체제가 경로를 작은따옴표로 싸서 내는 영어 예외들.</summary>
    private static IEnumerable<Exception> EnglishPathExceptions(string path) => new Exception[]
    {
        new FileNotFoundException($"Could not find file '{path}'.", path),
        new DirectoryNotFoundException($"Could not find a part of the path '{path}'."),
        new UnauthorizedAccessException($"Access to the path '{path}' is denied."),
        new IOException($"The process cannot access the file '{path}' because it is being used by another process.", unchecked((int)0x80070020)),
        new IOException($"The process cannot access the file '{path}' because another process has locked a portion of the file.", unchecked((int)0x80070021)),
        new IOException($"The file '{path}' already exists.", unchecked((int)0x80070050)),
        new IOException($"Cannot create '{path}' because a file or directory with the same name already exists.", unchecked((int)0x800700B7)),
        new PathTooLongException($"The path '{path}' is too long, or a component of the specified path is too long."),
        new IOException($"There is not enough space on the disk. : '{path}'", unchecked((int)0x80070070)),
        new IOException($"The network path was not found. : '{path}'", unchecked((int)0x80070035)),
        new IOException($"The semaphore timeout period has expired. : '{path}'", unchecked((int)0x80070079)),
        new IOException($"Unknown failure while reading '{path}'.", unchecked((int)0x80004005)),
        new InvalidOperationException($"Collection was modified; enumeration operation may not execute. '{path}'"),
        new IOException($"Could not open '{path}' because the volume is locked."),
    };

    /// <summary>
    /// 계약((g)): 경로에 작은따옴표와 한글이 이어져도(폴더 '26년 견적 등) 영어 예외 문구가 화면·창 없이 풀기 한 줄에 나오지 않는다 — 경로 말고는 한국어,
    /// 경로를 보이면 그 경로를 끝까지(따옴표에서 잘리지 않게). 여러 개 풀기·압축 실패 문구도 같다.
    /// </summary>
    [Theory]
    [MemberData(nameof(QuotePaths))]
    public void 경로에_작은따옴표와_한글이_있어도_영어_문구가_나오지_않는다(string path)
    {
        foreach (var ex in EnglishPathExceptions(path))
        {
            var p = ErrorCatalog.Describe(ex, path);
            foreach (var (what, text) in new[]
                     {
                         ("화면", (p with { Heading = "압축을 풀지 못했어요." }).ToDisplayText()),
                         ("창 없이 풀기", ErrorCatalog.HeadlessMessage(ex)),
                         ("여러 개 풀기", ErrorMessages.ToPlainMessage(ex)),
                         ("압축 실패", ErrorMessages.ToCompressMessage(ex)),
                     })
            {
                string rest = text.Replace(path, " ", StringComparison.Ordinal);
                Assert.True(ErrorCatalog.ContainsHangul(rest), $"{what}: 한글 없음 — {text} ({ex.GetType().Name}: {ex.Message})");
                Assert.False(System.Text.RegularExpressions.Regex.IsMatch(rest, "[A-Za-z]{3,}"), $"{what}: 영어가 남음 — {text} ({ex.GetType().Name}: {ex.Message})");
                // 경로를 보였다면 끝까지
                if (text.Contains(@":\", StringComparison.Ordinal) || text.Contains(@"\\", StringComparison.Ordinal))
                    Assert.True(text.Contains(path, StringComparison.Ordinal), $"{what}: 경로가 잘림 — {text}");
            }
            Assert.Contains(ex.Message, p.Details);   // 원문은 '자세히'에
        }
    }

    private static byte[] WithCdOffset(byte[] src, long offset)
    {
        var b = (byte[])src.Clone();
        BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(EocdOf(b) + 16), (uint)offset);
        return b;
    }
}
