using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;
using Iljip.Services;
using Iljip.Tests.TestSupport;
using R3 = Iljip.Tests.Contract.ArchiveTestReview3Tests;
using SharpZip = ICSharpCode.SharpZipLib.Zip;

namespace Iljip.Tests.Contract;

/// <summary>
/// [I4-1 검증 지적 2차] 남은 거짓 성공과 원인 틀림:
/// 폴더 기록을 빈 Deflate 흐름(방식 8·압축 크기 2)으로 쓴 ZIP(Java·SharpZipLib·Python)을 고치면 폴더를 파일로 세어 '7개를 모두 살렸어요'(파일 6개),
/// 맥 NFD 이름이 든 반디집 기본 ZIP 을 고치면 이름이 'ㅁㅐㄱ_ㅅㅐㅁㅍㅡㄹ/…' 로 바뀐 채 '모두 살렸어요', 목록 없는 서술자 ZIP 에서 이름 끝이 '/' 로 깨진 파일을
/// '살렸어요'에 넣고 고친 ZIP 에서는 폴더로 보여 내용이 사라짐, 0 으로만 채워진 .zip 을 검사 '문제없어요. 파일 0개'·풀기 종료 코드 0,
/// 끝 머리 '이 디스크 번호' 1바이트가 깨진 보통 ZIP 을 "'a.z01' 조각이 없어요"로 알림. 그리고 git 제외 픽스처 없이도 도는 검사 시험(G2·G3 — <see cref="ArchiveTesterTests"/>).
/// </summary>
public class ArchiveTestReview7Tests
{
    private static readonly TimeSpan Limit = TimeSpan.FromSeconds(120);

    private static Task<RepairReport> Repair(string src, string dest, string? password = null) =>
        ZipRepairer.Instance.RepairAsync(src, dest, password, null, CancellationToken.None).WaitAsync(Limit);

    private static Task<TestReport> Test(string path, string? password = null) =>
        ArchiveTestService.Instance.TestAsync(path, password, null, CancellationToken.None).WaitAsync(Limit);

    /// <summary>압축을 일집으로 풀어 파일(이름 → sha256 — NFC)만.</summary>
    private static async Task<Dictionary<string, string>> ExtractFiles(string arc, string dest, string? password = null)
    {
        var svc = new ArchiveServiceLocator().ResolveByContent(arc) ?? new ArchiveServiceLocator().ResolveForAutomation(arc)
            ?? throw new UnsupportedFormatException(".zip");
        await svc.ExtractAsync(arc, dest, password).WaitAsync(Limit);
        return TreeAssert.Snapshot(dest, nfc: true).Where(kv => kv.Value is not null).ToDictionary(kv => kv.Key, kv => kv.Value!, StringComparer.Ordinal);
    }

    private static string Sha(byte[] b) => Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(b)).ToLowerInvariant();

    private static string Nfc(string s) => s.Normalize(NormalizationForm.FormC);

    /// <summary>목록 기록마다 (이름 바이트, 방식, 압축 크기).</summary>
    private static List<(byte[] Name, int Method, long CSize)> CentralOf(byte[] b)
    {
        int e = R3.EocdOf(b), p = R3.CdStartOf(b), n = R3.U16(b, e + 10);
        var list = new List<(byte[], int, long)>();
        for (int i = 0; i < n; i++)
        {
            int nl = R3.U16(b, p + 28);
            list.Add((b.AsSpan(p + 46, nl).ToArray(), R3.U16(b, p + 10), R3.U32(b, p + 20)));
            p += 46 + nl + R3.U16(b, p + 30) + R3.U16(b, p + 32);
        }
        return list;
    }

    // ───────────── 재료: 폴더를 빈 Deflate 흐름으로 쓴 ZIP ─────────────

    /// <summary>되감지 못하는 쓰기 전용 스트림(데이터 서술자를 쓰게 — Java ZipOutputStream·서버가 흘려 쓴 모양).</summary>
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

    /// <summary>
    /// SharpZipLib 기본값으로 쓴 ZIP — 폴더 기록도 Deflate(방식 8, 압축 크기 2, 원래 크기 0). <paramref name="streamed"/> 면 되감지 못하는 스트림에(서술자).
    /// <paramref name="withFiles"/> 가 false 면 폴더만. 원래 파일(이름 → 내용)과 폴더 이름을 함께 준다.
    /// </summary>
    private static (byte[] Bytes, Dictionary<string, byte[]> Files, string[] Folders) SharpZipWithFolders(bool streamed, bool withFiles = true)
    {
        var entries = new List<(string Name, byte[]? Data, bool Stored)>
        {
            ("보고서.txt", R3.Text("보고서", 6000), false),
            ("사진/", null, false),
            ("사진/a.bin", R3.Text("사진", 4000), true),
            ("빈파일.txt", Array.Empty<byte>(), false),
            ("자료/", null, false),
            ("자료/견적서.txt", R3.Text("견적", 3000), false),
        };
        if (!withFiles) entries = entries.Where(e => e.Data is null).ToList();
        var files = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        using var ms = new MemoryStream();
        using (var zos = new SharpZip.ZipOutputStream(streamed ? new ForwardOnly(ms) : ms) { IsStreamOwner = false })
        {
            foreach (var (name, data, stored) in entries)
            {
                var e = new SharpZip.ZipEntry(name) { IsUnicodeText = true, DateTime = new DateTime(2026, 10, 16, 10, 30, 12) };
                if (stored && !streamed)
                {
                    e.CompressionMethod = SharpZip.CompressionMethod.Stored;
                    e.Size = data!.Length;
                    var crc = new ICSharpCode.SharpZipLib.Checksum.Crc32();
                    crc.Update(data);
                    e.Crc = crc.Value;
                }
                zos.PutNextEntry(e);
                if (data is not null) zos.Write(data, 0, data.Length);
                zos.CloseEntry();
                if (data is not null) files[name] = data;
            }
            zos.Finish();
        }
        byte[] b = ms.ToArray();
        // 이 시험의 전제: 폴더 기록이 빈 Deflate 흐름(압축 크기 > 0)으로 적혀 있다
        Assert.All(CentralOf(b).Where(r => r.Name[^1] == (byte)'/'), r => Assert.True(r.Method == 8 && r.CSize > 0, $"폴더 기록 모양: 방식 {r.Method}·압축 크기 {r.CSize}"));
        return (b, files, entries.Where(e => e.Data is null).Select(e => e.Name).ToArray());
    }

    /// <summary>고친 ZIP 의 폴더 이름(System.IO.Compression — 끝 '/' 이고 길이 0).</summary>
    private static List<string> FoldersOf(string zip)
    {
        using var z = new ZipArchive(File.OpenRead(zip), ZipArchiveMode.Read);
        return z.Entries.Where(e => e.FullName.EndsWith('/')).Select(e =>
        {
            Assert.Equal(0, e.Length);
            return e.FullName;
        }).OrderBy(n => n, StringComparer.Ordinal).ToList();
    }

    // ───────────── 폴더를 파일로 세지 않음 ─────────────

    /// <summary>
    /// 계약(2차 지적 1): 폴더 기록을 빈 Deflate 흐름(방식 8·압축 크기 2)으로 쓴 ZIP 을 고치면 폴더는 폴더 — 원래 수·'살렸어요'에 넣지 않고 고친 ZIP 에도 폴더로 남는다.
    /// 목록 모드는 'N개를 모두 살렸어요'(N = 파일 수 — 예전: 파일 6개인데 '7개를 모두'), 목록을 떼어 낸 훑기 모드는 폴더를 '못 살린 파일(이름이 깨졌어요)'로도
    /// '살린 파일'로도 세지 않는다(예전: 되감기 ZIP 은 '이름이 깨졌어요', 흘려 쓴 ZIP 은 '살렸어요'에 넣음). 고친 ZIP 은 검사 '문제없어요. 파일 N개'·내용 그대로.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task 빈_Deflate_흐름으로_쓴_폴더를_파일로_세지_않는다(bool streamed)
    {
        using var tmp = new TempDir("rv7dir");
        var (bytes, files, folders) = SharpZipWithFolders(streamed);
        var want = files.ToDictionary(kv => kv.Key, kv => Sha(kv.Value), StringComparer.Ordinal);

        // 목록 모드
        string src = tmp.P("폴더 있는 자료.zip");
        File.WriteAllBytes(src, bytes);
        var r = await Repair(src, tmp.P("폴더 있는 자료_복구.zip"));
        Assert.True(r.CountKnown);
        Assert.Equal(files.Count, r.OriginalCount);
        Assert.Equal($"{files.Count}개를 모두 살렸어요.", r.Headline);
        Assert.True(r.IsComplete, r.ToDisplayText());
        Assert.Equal(files.Keys.OrderBy(k => k, StringComparer.Ordinal), r.Salvaged.OrderBy(k => k, StringComparer.Ordinal));
        Assert.Equal(folders.OrderBy(k => k, StringComparer.Ordinal), FoldersOf(r.OutputPath!));
        var outTest = await Test(r.OutputPath!);
        Assert.True(outTest.Ok, outTest.ToDisplayText());
        Assert.Equal(files.Count, outTest.CheckedCount);
        Assert.Equal(want, await ExtractFiles(r.OutputPath!, tmp.P("풀림1")));

        // 훑기 모드(목록을 떼어 냄)
        string noCd = tmp.P("목록 없음.zip");
        File.WriteAllBytes(noCd, bytes[..R3.CdStartOf(bytes)]);
        var s = await Repair(noCd, tmp.P("목록 없음_복구.zip"));
        Assert.False(s.CountKnown);
        Assert.Equal($"원래 몇 개였는지 확실하지 않아요 — {files.Count}개를 살렸어요.", s.Headline);
        Assert.Empty(s.LostItems);
        Assert.Empty(s.Unverified);
        Assert.Equal(0, s.UnconfirmedPieces);
        Assert.Equal(files.Keys.OrderBy(k => k, StringComparer.Ordinal), s.Salvaged.OrderBy(k => k, StringComparer.Ordinal));
        Assert.Equal(folders.OrderBy(k => k, StringComparer.Ordinal), FoldersOf(s.OutputPath!));
        Assert.Equal(want, await ExtractFiles(s.OutputPath!, tmp.P("풀림2")));
    }

    /// <summary>계약(2차 지적 1): 폴더만 든 ZIP(폴더 기록이 빈 Deflate 흐름)은 '살릴 수 있는 파일을 찾지 못했어요' — 예전: 파일 0개인데 '2개를 모두 살렸어요'.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task 폴더만_든_ZIP을_파일을_살렸다고_하지_않는다(bool streamed)
    {
        using var tmp = new TempDir("rv7dironly");
        var (bytes, files, _) = SharpZipWithFolders(streamed, withFiles: false);
        Assert.Empty(files);
        string src = tmp.P("빈 폴더들.zip");
        File.WriteAllBytes(src, bytes);
        var r = await Repair(src, tmp.P("빈 폴더들_복구.zip"));
        Assert.Equal("살릴 수 있는 파일을 찾지 못했어요.", r.Headline);
        Assert.Empty(r.Salvaged);
        Assert.Empty(r.LostItems);
        Assert.Null(r.OutputPath);
        Assert.False(File.Exists(tmp.P("빈 폴더들_복구.zip")));
    }

    /// <summary>
    /// 계약(2차 지적 6): 목록이 없는 서술자(흘려 쓴) ZIP 에서 파일 이름의 마지막 바이트가 '/'·'\' 로 깨지면(머리의 CRC·크기 칸이 0 이라 이름 검사를 지나옴)
    /// 그 파일은 '못 살린 파일(이름이 깨졌어요)' — 데이터는 확인돼도 그 이름 그대로 쓰면 고친 ZIP 에서 폴더로 보여 내용이 사라진다
    /// (예전: '5개를 살렸어요'에 넣었는데 고친 ZIP 검사는 '파일 4개'). 끝이 데이터로 확인되므로 뒤 파일은 이어서 살린다.
    /// </summary>
    [Theory]
    [InlineData((byte)'/')]
    [InlineData((byte)'\\')]
    public async Task 목록_없는_서술자_ZIP에서_이름_끝이_슬래시로_깨진_파일을_살렸다고_하지_않는다(byte last)
    {
        using var tmp = new TempDir("rv7slash");
        var (bytes, files, _) = SharpZipWithFolders(streamed: true);
        byte[] b = bytes[..R3.CdStartOf(bytes)];
        byte[] name = Encoding.UTF8.GetBytes("보고서.txt");
        int at = b.AsSpan().IndexOf(name);
        Assert.True(at > 0 && R3.U32(b, at - 30) == 0x04034b50 && (R3.U16(b, at - 30 + 6) & 0x08) != 0, "전제: 서술자 방식 로컬 머리의 이름");
        b[at + name.Length - 1] = last;
        string src = tmp.P("목록 없음.zip");
        File.WriteAllBytes(src, b);

        var r = await Repair(src, tmp.P("목록 없음_복구.zip"));
        Assert.False(r.CountKnown);
        var lost = Assert.Single(r.LostItems);
        Assert.Equal("이름이 깨졌어요", lost.Reason);
        Assert.DoesNotContain(r.Salvaged, n => n.EndsWith('/') || n.EndsWith('\\'));
        var rest = files.Keys.Where(k => k != "보고서.txt").OrderBy(k => k, StringComparer.Ordinal).ToList();
        Assert.Equal(rest, r.Salvaged.OrderBy(k => k, StringComparer.Ordinal));
        Assert.Equal($"원래 몇 개였는지 확실하지 않아요 — {rest.Count}개를 살렸어요.", r.Headline);
        var outTest = await Test(r.OutputPath!);
        Assert.True(outTest.Ok, outTest.ToDisplayText());
        Assert.Equal(r.Salvaged.Count, outTest.CheckedCount);
        Assert.Equal(rest, (await ExtractFiles(r.OutputPath!, tmp.P("풀림"))).Keys.OrderBy(k => k, StringComparer.Ordinal));
    }

    // ───────────── 맥 NFD 이름 ─────────────

    /// <summary>
    /// 계약(2차 지적 2): 맥 NFD 이름이 든 반디집 기본 ZIP(이름 바이트는 CP949 호환 낱자 'ㅁㅐㄱ', 0x7075 에 NFD UTF-8)을 고치면 이름은 원본이 보여 주는 그대로
    /// ('맥_샘플/…' — 예전: 'ㅁㅐㄱ_ㅅㅐㅁㅍㅡㄹ/…' 로 바꾼 채 '3개를 모두 살렸어요'). 목록 모드·목록을 뗀 훑기 모드 모두. 고친 ZIP 을 풀면 원본과 같은 이름·내용.
    /// </summary>
    [Theory]
    [InlineData("nfd_bandizip_default", false)]
    [InlineData("nfd_bandizip_default", true)]
    [InlineData("nfd_bandizip_utf8", false)]
    [InlineData("nfd_python_efs", true)]
    public async Task 맥_NFD_이름_ZIP을_고쳐도_이름이_그대로다(string id, bool stripList)
    {
        var fx = FixtureCatalog.Get(id);
        using var tmp = new TempDir("rv7nfd");
        byte[] orig = File.ReadAllBytes(fx.FullPath);
        string src = tmp.P("맥에서 받음.zip");
        File.WriteAllBytes(src, stripList ? orig[..R3.CdStartOf(orig)] : orig);
        var want = fx.ExpectedFiles.ToDictionary(e => Nfc(e.Path), e => e.Sha256!, StringComparer.Ordinal);

        var r = await Repair(src, tmp.P("맥에서 받음_복구.zip"));
        Assert.Equal(want.Keys.OrderBy(k => k, StringComparer.Ordinal), r.Salvaged.Select(Nfc).OrderBy(k => k, StringComparer.Ordinal));
        Assert.Empty(r.LostItems);
        if (!stripList) Assert.Equal($"{want.Count}개를 모두 살렸어요.", r.Headline);
        Assert.Equal(want, await ExtractFiles(r.OutputPath!, tmp.P("풀림")));
    }

    // ───────────── 0 으로만 채운 파일 ─────────────

    /// <summary>
    /// 계약(2차 지적 3): 0 으로만 채운 .zip(받다 만 파일·복사 실패 — 엔진은 1,000바이트 이상이면 빈 tar 로 보고 오류 없이 0개)은 검사 '문제가 있어요'(고치기 권함),
    /// 풀기는 손상(창 없이 풀기 종료 코드 1·한국어) — 예전: 검사 '문제없어요. 파일 0개를 확인했어요.'·풀기 종료 코드 0. 진짜 빈 ZIP(끝 머리만)은 그대로 '문제없어요. 파일 0개'.
    /// </summary>
    [Theory]
    [InlineData(1000)]
    [InlineData(2048)]
    [InlineData(65536)]
    [InlineData(1 << 20)]
    public async Task 영으로만_채운_ZIP을_문제없다고_하지_않는다(int size)
    {
        using var tmp = new TempDir("rv7zero");
        string src = tmp.P("받다 만 파일.zip");
        File.WriteAllBytes(src, new byte[size]);

        var report = await Test(src);
        Assert.False(report.Ok, report.ToDisplayText());
        Assert.True(report.HasDamage, report.ToDisplayText());
        Assert.Equal(0, report.CheckedCount);

        var svc = new ArchiveServiceLocator().ResolveForAutomation(src)!;
        string dest = tmp.Dir("풀림");
        var ex = await Assert.ThrowsAnyAsync<CorruptArchiveException>(() => svc.ExtractAsync(src, dest, null).WaitAsync(Limit));
        Assert.Equal(1, ErrorMessages.HeadlessExitCode(ex));
        Assert.True(ErrorCatalog.ContainsHangul(ErrorCatalog.HeadlessMessage(ex)), ErrorCatalog.HeadlessMessage(ex));
        Assert.Empty(Directory.EnumerateFileSystemEntries(dest));
    }

    /// <summary>같은 계약의 반대쪽: 끝 머리만 있는 진짜 빈 ZIP 은 '문제없어요. 파일 0개를 확인했어요.'이고 풀기도 오류 없이 끝난다.</summary>
    [Fact]
    public async Task 끝_머리만_있는_빈_ZIP은_그대로_문제없다()
    {
        using var tmp = new TempDir("rv7empty");
        string src = tmp.P("빈 압축.zip");
        using (var z = ZipFile.Open(src, ZipArchiveMode.Create)) { }
        Assert.Equal(22, new FileInfo(src).Length);
        var report = await Test(src);
        Assert.True(report.Ok, report.ToDisplayText());
        Assert.Equal("문제없어요. 파일 0개를 확인했어요.", report.Headline);
        var svc = new ArchiveServiceLocator().ResolveForAutomation(src)!;
        await svc.ExtractAsync(src, tmp.Dir("풀림"), null).WaitAsync(Limit);
    }

    // ───────────── 끝 머리의 '이 디스크 번호' ─────────────

    /// <summary>
    /// 계약(2차 지적 4): 나눠지지 않은 ZIP 의 끝 머리 '이 디스크 번호'(+4·+5) 1바이트가 깨져도 "'….z01' 조각이 없어요"로 알리지 않는다 — 목록이 이 파일 안에 통째로 있고
    /// 목록 시작 디스크·기록의 디스크가 모두 0 이면 보통 ZIP(그 칸만 깨진 것 — 엔진은 그 칸을 보지 않음). 그래서 '이 디스크 번호를 크게'(0x7777 —
    /// <see cref="ArchiveTestReview5Tests"/>)와 같게 검사 '문제없어요'·모두 풀림. 예전: 조각 없음(손상 아님 — 고치기도 권하지 않음)·종료 코드 1.
    /// </summary>
    [Theory]
    [InlineData("zip_default", 4, 0x01)]
    [InlineData("zip_default", 4, 0xFF)]
    [InlineData("zip_default", 5, 0x01)]
    [InlineData("zip_aes256", 4, 0x01)]
    [InlineData("zip_aes256", 5, 0x01)]
    [InlineData("zip_zipcrypto_korean_pw", 4, 0x20)]
    public async Task 끝_머리_디스크_번호만_깨진_보통_ZIP을_조각이_없다고_하지_않는다(string id, int field, int xor)
    {
        var fx = FixtureCatalog.Get(id);
        byte[] orig = File.ReadAllBytes(fx.FullPath);
        int eocd = R3.EocdOf(orig);
        Assert.Equal(0, R3.U16(orig, eocd + 4));
        using var tmp = new TempDir("rv7disk");
        string src = tmp.P("끝머리디스크.zip");
        File.WriteAllBytes(src, R3.Flip(orig, eocd + field, (byte)xor));

        var report = await Test(src, fx.Password);
        Assert.True(report.Ok, report.ToDisplayText());
        Assert.Equal(fx.ExpectedFiles.Count(), report.CheckedCount);
        Assert.DoesNotContain("조각", report.ToDisplayText());
        var got = await ExtractFiles(src, tmp.P("풀림"), fx.Password);
        Assert.Equal(fx.ExpectedFiles.ToDictionary(e => e.Path, e => e.Sha256!, StringComparer.Ordinal), got);
    }

    /// <summary>같은 계약의 반대쪽: 진짜 나눔 세트의 앞 조각이 없으면 그대로 그 조각 이름으로 알린다(디스크 칸만 깨진 보통 ZIP 과 가름).</summary>
    [Fact]
    public async Task 진짜_나눔_세트의_앞_조각이_없으면_그대로_알린다()
    {
        var fx = FixtureCatalog.Get("zip_split");
        Assert.True(fx.SplitVolumes.Count >= 2);
        using var tmp = new TempDir("rv7span");
        foreach (var v in fx.SplitVolumes.Where(v => !v.EndsWith(".z01", StringComparison.OrdinalIgnoreCase)))
            File.Copy(FixtureCatalog.PathOf(v), tmp.P(Path.GetFileName(v)));
        string last = tmp.P(Path.GetFileName(fx.File));
        Assert.True(SpannedZipStream.IsLastVolume(last));
        var svc = new ArchiveServiceLocator().ResolveForAutomation(last)!;
        var ex = await Assert.ThrowsAsync<VolumeMissingException>(() => svc.ExtractAsync(last, tmp.Dir("풀림"), fx.Password).WaitAsync(Limit));
        Assert.Contains(".z01", ex.Message);
    }

    /// <summary>
    /// 같은 계약(엔진 바깥 도구): 끝 머리 디스크 칸만 깨진 보통 ZIP 의 조각 수는 0(보통 ZIP), 진짜 나눔 세트의 마지막 조각은 세트의 조각 수.
    /// </summary>
    [Fact]
    public void 조각_수는_목록이_이_파일에_통째로_있으면_보통_ZIP으로_본다()
    {
        var fx = FixtureCatalog.Get("zip_default");
        byte[] orig = File.ReadAllBytes(fx.FullPath);
        int eocd = R3.EocdOf(orig);
        using var tmp = new TempDir("rv7count");
        string one = tmp.P("한파일.zip");
        File.WriteAllBytes(one, R3.Flip(orig, eocd + 4, 0x01));
        Assert.Equal(0, SpannedZipStream.DiskCount(one));
        var split = FixtureCatalog.Get("zip_split");
        Assert.Equal(split.SplitVolumes.Count, SpannedZipStream.DiskCount(FixtureCatalog.PathOf(split.File)));
    }
}
