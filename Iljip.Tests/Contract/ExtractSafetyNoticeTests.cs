using System.Text;
using Iljip.Models;
using Iljip.Services;
using Iljip.Tests.TestSupport;
using Iljip.ViewModels;

namespace Iljip.Tests.Contract;

/// <summary>
/// [I5-B] 최종 감사의 안전·정직 지적 셋(풀기 쪽) — 시험은 가짜 화면(<see cref="FakeUserInteraction"/>)·가짜 휴지통만 쓴다(창을 띄우지 않음).
/// <list type="bullet">
/// <item>IA-SELFNAME: '여기에 풀기'에서 안쪽에 바깥 압축과 같은 이름의 파일이 있으면(이중 압축 첨부) [덮어쓰기]·설정 덮어쓰기로 풀고 있는 압축 파일 자신을 바꿨다
///   (7z.dll 길 — '성공', 설정 '다 푼 뒤 휴지통'까지면 방금 푼 안쪽 압축이 휴지통으로). ZIP 길은 실패 + 맞지 않는 '관리자 권한으로 다시 시도'.
///   → 풀고 있는 압축(과 조각)은 묻지도 덮지도 않고 '이름 (1)' 로 비켜 풀고 알린다.</item>
/// <item>IA-OPENFILE-OVERWRITE: 엑셀로 열어 둔(잠긴) 파일을 덮어쓰며 풀면 그 뒤 파일도 안 풀고 멈추고 '관리자 권한으로 다시 시도'를 권했다.
///   → 그 파일만 건너뛰고(원래 파일 그대로) 나머지는 계속 풀고 '다른 프로그램이 쓰는 중이라 못 바꾼 파일 N개(이름)', 관리자 권유 없음.</item>
/// <item>IX-F2: tar 하드 링크를 0바이트 파일로 풀고 말없이 성공·검사 '문제없어요'. 링크 복원은 결정 기록상 제외 → 결과 창·여러 개 풀기 요약·검사 결과에 알림.</item>
/// </list>
/// 창 없이 풀기(실제 exe)의 링크 알림·없는 경로는 <see cref="HeadlessNoticeTests"/>.
/// </summary>
public class ExtractSafetyNoticeTests
{
    private static readonly byte[] InnerText = Encoding.UTF8.GetBytes("안쪽 압축 속 본문 — 합성 데이터 0123456789");
    private static readonly byte[] OuterNote = Encoding.UTF8.GetBytes("바깥 설명 — 합성");

    private static Task RunShell(MainViewModel vm, params string[] args) =>
        VmRunner.Run(() => vm.ProcessShellCommandAsync(args), "우클릭 풀기");

    /// <summary>
    /// 이중 압축 첨부를 만든다: <paramref name="dir"/>\첨부.fmt(바깥) 안에 첨부.fmt(안쪽 — 본문.txt 하나) + 설명.txt.
    /// 돌려주는 것: 바깥 경로, 안쪽 압축 바이트.
    /// </summary>
    private static async Task<(string Outer, byte[] Inner)> MakeDoubleArchive(TempDir tmp, string fmt, string dir, string baseName = "첨부")
    {
        IArchiveService svc = fmt == "7z" ? new SevenZipArchiveService() : new ZipArchiveService();
        string innerDir = tmp.Dir("안쪽_" + fmt + Guid.NewGuid().ToString("N")[..4]);
        string body = Path.Combine(innerDir, "본문.txt");
        File.WriteAllBytes(body, InnerText);
        string innerArc = Path.Combine(tmp.Dir("안쪽압축_" + fmt + Guid.NewGuid().ToString("N")[..4]), $"{baseName}.{fmt}");
        await svc.CompressAsync(new[] { body }, innerArc);
        string noteDir = tmp.Dir("설명_" + fmt + Guid.NewGuid().ToString("N")[..4]);
        string note = Path.Combine(noteDir, "설명.txt");
        File.WriteAllBytes(note, OuterNote);
        Directory.CreateDirectory(dir);
        string outer = Path.Combine(dir, $"{baseName}.{fmt}");
        await svc.CompressAsync(new[] { innerArc, note }, outer);
        if (fmt == "7z") Assert.True(SevenZipLibrary.IsAvailable, "7z.dll 길(임시 폴더에서 옮기기)을 시험해야 함");
        return (outer, File.ReadAllBytes(innerArc));
    }

    // ───────────── IA-SELFNAME ─────────────

    /// <summary>
    /// 계약(B1): 바깥과 같은 이름 항목이 든 zip·7z 를 '여기에 풀기' + 덮어쓰기 고정(설정 덮어쓰기와 같은 처리기)으로 풀면 바깥 압축은 바이트 그대로,
    /// 안쪽 압축은 '첨부 (1).확장자'로 풀리고(내용 = 안쪽 압축), 설명.txt 도 풀리고, 결과에 알림(비켜 푼 이름). 예전 7z 는 바깥 압축이 안쪽 것으로 바뀌었고(성공),
    /// zip 은 '덮어쓰지 못했어요' 실패였다.
    /// </summary>
    [Theory]
    [InlineData("zip")]
    [InlineData("7z")]
    public async Task 여기에_풀기는_풀고_있는_압축_자신을_덮지_않고_비켜_푼다(string fmt)
    {
        using var tmp = new TempDir("selfname" + fmt);
        string d = tmp.Dir("받은 첨부");
        var (outer, inner) = await MakeDoubleArchive(tmp, fmt, d);
        string outerSha = TreeAssert.Sha256(outer);
        var ui = new FakeUserInteraction();

        var r = await new BatchExtractor(new ArchiveServiceLocator(), ui, new FixedResolver(CollisionChoice.Overwrite), ExtractTargetRule.Here, new[] { outer })
            .RunAsync(ui.ShowProgress("x"));

        var o = Assert.Single(r.Outcomes);
        Assert.True(o.Succeeded, $"{fmt}: 실패 — {o.Reason}");
        Assert.Equal(outerSha, TreeAssert.Sha256(outer));   // 바깥 압축 바이트 그대로
        string renamed = Path.Combine(d, $"첨부 (1).{fmt}");
        Assert.True(File.Exists(renamed), $"{fmt}: 안쪽 압축을 비켜 풀지 않음 — {string.Join(", ", TreeAssert.Files(d))}");
        Assert.Equal(TreeAssert.Sha256(inner), TreeAssert.Sha256(renamed));
        Assert.Equal(TreeAssert.Sha256(OuterNote), TreeAssert.Sha256(Path.Combine(d, "설명.txt")));
        Assert.NotNull(o.Notice);
        Assert.Contains("이름을 바꿔 풀었어요", o.Notice);
        Assert.Contains($"첨부.{fmt} → 첨부 (1).{fmt}", o.Notice);
        Assert.Equal(0, o.Skipped);   // 건너뛴 것이 아니다(다 풀었음 — 휴지통 설정이면 바깥 압축을 보내도 됨)
        CollisionTests.NoLeftovers(d, fmt);
    }

    /// <summary>
    /// 계약(B1 — 묻는 창): 같은 이름 창에서 [덮어쓰기]+'남은 파일도 똑같이'를 골라도 풀고 있는 압축 파일 자신은 묻지도 덮지도 않는다
    /// (그 경로로는 창이 뜨지 않음 — 창에는 '‘첨부.7z’ 파일이 이미 있어요'로만 보여 사용자가 알 수 없었다). 다른 같은 이름(설명.txt)은 그대로 묻고 덮어쓴다.
    /// </summary>
    [Theory]
    [InlineData("zip")]
    [InlineData("7z")]
    public async Task 묻는_창에서_덮어쓰기를_골라도_풀고_있는_압축은_묻지_않고_비켜_푼다(string fmt)
    {
        using var tmp = new TempDir("selfask" + fmt);
        string d = tmp.Dir("받은 첨부");
        var (outer, inner) = await MakeDoubleArchive(tmp, fmt, d);
        File.WriteAllText(Path.Combine(d, "설명.txt"), "예전 설명");
        string outerSha = TreeAssert.Sha256(outer);
        var ui = new FakeUserInteraction { DefaultCollisionAnswer = new CollisionAnswer(CollisionChoice.Overwrite, ApplyToAll: true) };

        var r = await new BatchExtractor(new ArchiveServiceLocator(), ui, new AskingResolver(ui), ExtractTargetRule.Here, new[] { outer })
            .RunAsync(ui.ShowProgress("x"));

        Assert.True(r.Outcomes[0].Succeeded, r.Outcomes[0].Reason);
        Assert.DoesNotContain(ui.CollisionPrompts, q => string.Equals(q.TargetPath, outer, StringComparison.OrdinalIgnoreCase));
        var q1 = Assert.Single(ui.CollisionPrompts);
        Assert.EndsWith("설명.txt", q1.TargetPath);
        Assert.Equal(outerSha, TreeAssert.Sha256(outer));
        Assert.Equal(TreeAssert.Sha256(inner), TreeAssert.Sha256(Path.Combine(d, $"첨부 (1).{fmt}")));
        Assert.Equal(TreeAssert.Sha256(OuterNote), TreeAssert.Sha256(Path.Combine(d, "설명.txt")));   // 다른 같은 이름은 고른 대로 덮어씀
    }

    /// <summary>
    /// 계약(B1 — 우클릭 흐름 전체, 감사 재현 selfvm): 설정 '같은 이름이면 덮어쓰기' + '다 푼 뒤 압축 파일을 휴지통으로'(가짜 휴지통)로 우클릭 '여기에 풀기'를 하면
    /// 휴지통으로 가는 것은 바깥(원래) 압축이고 그 바이트 그대로이며, 방금 푼 안쪽 압축은 '자료 (1).확장자'로 결과 폴더에 남는다. 알림 창 하나(비켜 푼 이름), 오류 창·관리자 권유 없음.
    /// 예전 7z 는 바깥 압축이 안쪽 것으로 바뀐 뒤 그것이 휴지통으로 가서 결과 폴더에는 설명.txt 만 남았다(창 없음).
    /// </summary>
    [Theory]
    [InlineData("zip")]
    [InlineData("7z")]
    public async Task 우클릭_여기에_풀기와_휴지통_설정에서도_안쪽_압축이_남는다(string fmt)
    {
        using var tmp = new TempDir("selfvm" + fmt);
        string d = tmp.Dir("받은 첨부");
        var (outer, inner) = await MakeDoubleArchive(tmp, fmt, d, "자료");
        byte[] outerBytes = File.ReadAllBytes(outer);
        var svc = TestServices.With(e => { e.OnCollision = CollisionPolicy.Overwrite; e.DeleteArchiveAfter = true; });
        var ui = new FakeUserInteraction();
        svc.RecycleBin.OnSend = paths => Assert.All(paths, p => Assert.Equal(outerBytes, File.ReadAllBytes(p)));   // 보내는 순간에도 바깥(원래) 압축

        await RunShell(new MainViewModel(ui, null, svc.Services), "--extract-here", outer);

        Assert.Equal(new[] { outer }, svc.RecycleBin.AllSent);
        Assert.Equal(outerBytes, File.ReadAllBytes(outer));   // 가짜 휴지통은 지우지 않음 — 바이트 그대로
        Assert.Equal(TreeAssert.Sha256(inner), TreeAssert.Sha256(Path.Combine(d, $"자료 (1).{fmt}")));
        Assert.Empty(ui.Errors);
        Assert.DoesNotContain(ui.ChoicePrompts, c => c.Choices.Contains(ElevationService.RetryLabel));
        var w = Assert.Single(ui.Warnings);
        Assert.Contains("이름을 바꿔 풀었어요", w);
        Assert.Contains($"자료.{fmt} → 자료 (1).{fmt}", w);
    }

    /// <summary>
    /// 계약(B1 — 나눠진 압축): 풀고 있는 압축의 다른 조각과 같은 이름 항목('자료.7z.002')도 덮지 않는다 — 조각 전부(<see cref="ArchiveVolumes.AllParts"/>)를 지킨다.
    /// </summary>
    [Fact]
    public async Task 나눠진_압축의_조각과_같은_이름도_덮지_않는다()
    {
        using var tmp = new TempDir("selfvol");
        string src = tmp.Dir("원본");
        var payload = new byte[200_000];
        new Random(15).NextBytes(payload);
        File.WriteAllBytes(Path.Combine(src, "자료.7z.002"), payload);
        string d = tmp.Dir("받은 첨부");
        await new SevenZipArchiveService().CompressAsync(new[] { Path.Combine(src, "자료.7z.002") }, Path.Combine(d, "자료.7z"),
            new CompressionOptions { SplitBytes = 64 * 1024, Level = CompressionLevelOption.Store });
        var parts = Directory.GetFiles(d).OrderBy(p => p, StringComparer.Ordinal).ToList();
        Assert.True(parts.Count >= 3, "조각이 셋 이상이어야 함: " + string.Join(", ", parts.Select(Path.GetFileName)));
        var before = parts.ToDictionary(p => p, TreeAssert.Sha256);
        var ui = new FakeUserInteraction();

        var r = await new BatchExtractor(new ArchiveServiceLocator(), ui, new FixedResolver(CollisionChoice.Overwrite), ExtractTargetRule.Here,
            new[] { Path.Combine(d, "자료.7z.001") }).RunAsync(ui.ShowProgress("x"));

        Assert.True(r.Outcomes[0].Succeeded, r.Outcomes[0].Reason);
        foreach (var (p, sha) in before) Assert.Equal(sha, TreeAssert.Sha256(p));
        Assert.Equal(TreeAssert.Sha256(payload), TreeAssert.Sha256(Path.Combine(d, "자료.7z (1).002")));
        Assert.Contains("자료.7z.002 → 자료.7z (1).002", r.Outcomes[0].Notice);
    }

    /// <summary>
    /// 계약: 일집 창 풀기(풀기 창으로 고른 폴더 = 압축이 있는 폴더 — ExtractToFolderAsync)도 같다: 바깥 압축 그대로, 안쪽은 비켜 풀고, 알림 창 하나.
    /// </summary>
    [Fact]
    public async Task 일집_창에서_같은_폴더에_풀어도_압축_자신을_덮지_않는다()
    {
        using var tmp = new TempDir("selfwin");
        string d = tmp.Dir("받은 첨부");
        var (outer, inner) = await MakeDoubleArchive(tmp, "7z", d);
        string outerSha = TreeAssert.Sha256(outer);
        var svc = TestServices.With(e => e.OnCollision = CollisionPolicy.Overwrite);
        var ui = new FakeUserInteraction();
        var vm = new MainViewModel(ui, null, svc.Services);

        bool ok = false;
        await VmRunner.Run(async () => ok = await vm.ExtractToFolderAsync(outer, d), "풀기");

        Assert.True(ok);
        Assert.Equal(outerSha, TreeAssert.Sha256(outer));
        Assert.Equal(TreeAssert.Sha256(inner), TreeAssert.Sha256(Path.Combine(d, "첨부 (1).7z")));
        Assert.Contains(ui.Warnings, w => w.Contains("첨부.7z → 첨부 (1).7z"));
    }

    // ───────────── IA-OPENFILE-OVERWRITE ─────────────

    public static TheoryData<string> Kinds => new() { "zip", "7z", "rar", "tar", "egg" };

    private static (string Name, byte[] Data)[] Report() => new[]
    {
        ("보고서/견적서 1.xlsx", Encoding.UTF8.GetBytes("새 견적서 — 압축 안 내용")),
        ("보고서/메모.txt", Encoding.UTF8.GetBytes("새 메모")),
        ("보고서/빈파일.txt", Array.Empty<byte>()),
        ("보고서/하위/표.csv", Encoding.UTF8.GetBytes("가,나,다")),
    };

    private static readonly byte[] Mine = Encoding.UTF8.GetBytes("사용자가 엑셀로 열어 둔 예전 견적서(지워지면 안 됨)");

    /// <summary>
    /// 계약(B5): 덮어쓰기로 정했는데 대상 폴더의 파일 하나를 다른 프로그램이 잡고 있으면(FileShare.None — 엑셀·DLP 흉내, 또는 엑셀처럼 쓰기 핸들 + 읽기 공유)
    /// 그 파일만 건너뛰고(원래 파일 바이트 그대로) 나머지 파일은 모두 풀린다. 알림 = 잠긴 파일 이름, 건너뛴 수 1, 덮어쓰기 임시 파일·풀기 임시 폴더가 남지 않는다.
    /// 형식마다 두 길: 직접 쓰는 길(zip·tar — SharpCompress, egg — EggDotNet), 7z.dll 임시 폴더에서 옮기는 길(7z·rar).
    /// 예전에는 그 파일에서 멈춰 뒤 파일을 풀지 않았다(7z 는 옮긴 것까지 되돌림).
    /// </summary>
    [Theory]
    [MemberData(nameof(Kinds))]
    public async Task 잠긴_파일은_건너뛰고_나머지는_다_풀고_알린다(string kind)
    {
        foreach (var excelLike in new[] { false, true })
        {
            using var tmp = new TempDir("inuse" + kind);
            var files = Report();
            string arc = await CollisionTests.MakeArchive(kind, tmp.P("in"), files);
            string dest = tmp.Dir("out");
            string locked = Path.Combine(dest, "보고서", "견적서 1.xlsx");
            Directory.CreateDirectory(Path.GetDirectoryName(locked)!);
            File.WriteAllBytes(locked, Mine);
            File.WriteAllText(Path.Combine(dest, "보고서", "메모.txt"), "예전 메모");   // 잠기지 않은 같은 이름은 덮어씀

            ExtractionScope? seen = null;
            Exception? ex;
            using (excelLike
                       ? new FileStream(locked, FileMode.Open, FileAccess.ReadWrite, FileShare.Read)
                       : new FileStream(locked, FileMode.Open, FileAccess.Read, FileShare.None))
            {
                ex = await Svc.Catch(async () =>
                {
                    using var scope = ExtractionScope.Begin(new FixedResolver(CollisionChoice.Overwrite), arc);
                    await Svc.For(arc).ExtractAsync(arc, dest);
                    seen = scope;
                });
            }

            string context = $"{kind}(엑셀 흉내={excelLike})";
            Assert.True(ex is null, $"{context}: 잠긴 파일 하나 때문에 풀기가 멈춤 — {ex?.Message}");
            Assert.Equal(TreeAssert.Sha256(Mine), TreeAssert.Sha256(locked));
            foreach (var (name, data) in files.Where(f => !f.Name.EndsWith("견적서 1.xlsx", StringComparison.Ordinal)))
                Assert.Equal(TreeAssert.Sha256(data), TreeAssert.Sha256(Path.Combine(dest, name.Replace('/', Path.DirectorySeparatorChar))));
            Assert.Equal(1, seen!.Skipped);
            Assert.Equal(new[] { locked }, seen.Notes.InUse);
            string? msg = seen.Notes.ToMessage();
            Assert.NotNull(msg);
            Assert.Contains("다른 프로그램이 쓰는 중이라 못 바꾼 파일 1개", msg);
            Assert.Contains("견적서 1.xlsx", msg);
            Assert.DoesNotContain("관리자", msg);
            Assert.DoesNotContain(TreeAssert.Files(dest), f => f.Contains(" (1)", StringComparison.Ordinal));
            CollisionTests.NoLeftovers(dest, context);
        }
    }

    /// <summary>
    /// 계약(B5 — 우클릭 흐름, 감사 재현 openxl): 같은 이름 창에서 [덮어쓰기]+'남은 파일도 똑같이'를 골랐는데 그 파일이 열려 있으면 오류 창·'관리자 권한으로 다시 시도' 없이
    /// 알림 창 하나('다른 프로그램이 쓰는 중이라 못 바꾼 파일 1개' + 이름)이고 나머지는 다 풀린다. 압축 파일은 휴지통으로 보내지 않는다(건너뛴 파일이 있으니).
    /// </summary>
    [Theory]
    [InlineData("zip")]
    [InlineData("7z")]
    public async Task 우클릭_풀기에서_잠긴_파일은_관리자_권유_없이_알린다(string kind)
    {
        using var tmp = new TempDir("inusevm" + kind);
        string work = tmp.Dir("작업");
        string built = await CollisionTests.MakeArchive(kind, tmp.P("in"), Report());
        string arc = Path.Combine(work, "보고서." + kind);
        File.Copy(built, arc);
        string locked = Path.Combine(work, "보고서", "견적서 1.xlsx");
        Directory.CreateDirectory(Path.GetDirectoryName(locked)!);
        File.WriteAllBytes(locked, Mine);
        var ui = new FakeUserInteraction { DefaultCollisionAnswer = new CollisionAnswer(CollisionChoice.Overwrite, ApplyToAll: true) };
        var svc = TestServices.With(e => e.DeleteArchiveAfter = true);

        using (new FileStream(locked, FileMode.Open, FileAccess.ReadWrite, FileShare.Read))
            await RunShell(new MainViewModel(ui, null, svc.Services), "--extract-here", arc);

        Assert.Empty(ui.Errors);
        Assert.Empty(ui.ChoicePrompts);   // '관리자 권한으로 다시 시도|닫기' 질문 없음
        var w = Assert.Single(ui.Warnings);
        Assert.Contains("다른 프로그램이 쓰는 중이라 못 바꾼 파일 1개", w);
        Assert.Contains("견적서 1.xlsx", w);
        Assert.Equal(TreeAssert.Sha256(Mine), TreeAssert.Sha256(locked));
        Assert.True(File.Exists(Path.Combine(work, "보고서", "메모.txt")));
        Assert.True(File.Exists(Path.Combine(work, "보고서", "빈파일.txt")));
        Assert.True(File.Exists(Path.Combine(work, "보고서", "하위", "표.csv")));
        Assert.Empty(svc.RecycleBin.Sent);
        CollisionTests.NoLeftovers(work, kind);
    }

    /// <summary>
    /// 계약: 다른 프로그램이 연 파일을 바꾸지 못한 것(<see cref="TargetInUseException"/>)은 권한 오류가 아니다 — 관리자 권한으로 다시 해도 같아 권하지 않는다.
    /// 쉬운 한국어(파일 이름, '그 프로그램을 닫고'), 문제 창 종류는 '다른 프로그램이 쓰고 있어요'. 아무도 안 연 파일은 '열려 있지 않음'(권한 문제로 남김).
    /// </summary>
    [Fact]
    public void 열려_있는_파일은_권한_오류가_아니다()
    {
        using var tmp = new TempDir("inusecls");
        string f = tmp.P("견적서.xlsx");
        File.WriteAllText(f, "x");
        var ex = new TargetInUseException(f, new UnauthorizedAccessException("Access to the path is denied."));
        Assert.False(ElevationService.IsAccessDenied(ex));
        Assert.Empty(ElevationService.ActionsFor(ex, canElevate: true));
        Assert.Contains("견적서.xlsx", ex.Message);
        Assert.Contains("그 프로그램을 닫고", ex.Message);
        Assert.Equal(ProblemKind.InUse, ErrorCatalog.Describe(ex).Kind);
        Assert.False(OutputFiles.IsOpenElsewhere(f));
        using (new FileStream(f, FileMode.Open, FileAccess.ReadWrite, FileShare.Read))
            Assert.True(OutputFiles.IsOpenElsewhere(f));
        Assert.False(OutputFiles.IsOpenElsewhere(tmp.P("없는 파일.txt")));
    }

    // ───────────── IX-F2: tar 링크 ─────────────

    private static readonly byte[] LinkTargetData = Encoding.ASCII.GetBytes("AAAA-content");   // 12바이트(감사 재현과 같음)

    /// <summary>GNU tar 형식('ustar  \0' 표지) 최소 작성기 — 일반 파일('0')·하드 링크('1')·심볼릭 링크('2').</summary>
    internal static byte[] GnuTar(params (string Name, byte[] Data, char Type, string Link)[] items)
    {
        using var ms = new MemoryStream();
        foreach (var (name, data, type, link) in items)
        {
            var h = new byte[512];
            Encoding.UTF8.GetBytes(name).CopyTo(h, 0);
            Octal(h, 100, 8, Convert.ToInt32("644", 8));
            Octal(h, 108, 8, 0);
            Octal(h, 116, 8, 0);
            Octal(h, 124, 12, data.Length);
            Octal(h, 136, 12, FixtureCatalog.FixedMtime.ToUnixTimeSeconds());
            for (int i = 148; i < 156; i++) h[i] = 0x20;
            h[156] = (byte)type;
            Encoding.UTF8.GetBytes(link).CopyTo(h, 157);
            Encoding.ASCII.GetBytes("ustar  \0").CopyTo(h, 257);   // GNU 표지(ustar + 공백 둘 + NUL)
            int sum = h.Sum(x => x);
            Encoding.ASCII.GetBytes(Convert.ToString(sum, 8).PadLeft(6, '0')).CopyTo(h, 148);
            h[154] = 0;
            h[155] = 0x20;
            ms.Write(h);
            ms.Write(data);
            ms.Write(new byte[(512 - data.Length % 512) % 512]);
        }
        ms.Write(new byte[1024]);
        return ms.ToArray();
    }

    private static void Octal(byte[] h, int off, int len, long v)
    {
        string s = Convert.ToString(v, 8).PadLeft(len - 1, '0');
        Encoding.ASCII.GetBytes(s).CopyTo(h, off);
        h[off + len - 1] = 0;
    }

    /// <summary>감사 재현과 같은 tar(a.txt 12바이트 + b.txt → a.txt 하드 링크). tgz 면 gzip 으로 감싼다.</summary>
    internal static string MakeHardLinkTar(string dir, string ext = ".tar", bool withSymlink = false)
    {
        var items = new List<(string, byte[], char, string)>
        {
            ("a.txt", LinkTargetData, '0', ""),
            ("b.txt", Array.Empty<byte>(), '1', "a.txt"),
        };
        if (withSymlink) items.Add(("c_sym.txt", Array.Empty<byte>(), '2', "a.txt"));
        byte[] tar = GnuTar(items.ToArray());
        Directory.CreateDirectory(dir);
        string path = Path.Combine(dir, "링크묶음" + ext);
        if (ext == ".tgz")
        {
            using var fs = File.Create(path);
            using var gz = new System.IO.Compression.GZipStream(fs, System.IO.Compression.CompressionLevel.Optimal);
            gz.Write(tar);
        }
        else File.WriteAllBytes(path, tar);
        return path;
    }

    /// <summary>
    /// 계약(B4 — 서비스): GNU tar·tgz 의 하드 링크(그리고 심볼릭 링크)는 예전처럼 내용 없는 파일로 풀되(링크 복원은 결정 기록상 제외) 알릴 것에 적는다 —
    /// '링크 2개는 내용 없이(0바이트) 만들었어요 … - b.txt (원래 파일: a.txt)'. 같은 압축 안 원래 파일(a.txt)은 12바이트 그대로.
    /// </summary>
    [Theory]
    [InlineData(".tar")]
    [InlineData(".tgz")]
    public async Task tar_링크를_내용_없이_만들면_알릴_것에_적는다(string ext)
    {
        using var tmp = new TempDir("tarlink" + ext.TrimStart('.'));
        string arc = MakeHardLinkTar(tmp.Dir("in"), ext, withSymlink: true);
        string dest = tmp.Dir("out");

        using var notes = ExtractionNotes.Begin();
        await Svc.For(arc).ExtractAsync(arc, dest);

        Assert.Equal(LinkTargetData, File.ReadAllBytes(Path.Combine(dest, "a.txt")));
        Assert.Equal(0, new FileInfo(Path.Combine(dest, "b.txt")).Length);
        Assert.Equal(new[] { new LinkNote("b.txt", "a.txt"), new LinkNote("c_sym.txt", "a.txt") }, notes.Links);
        string msg = notes.ToMessage()!;
        Assert.Contains("링크 2개는 내용 없이(0바이트) 만들었어요", msg);
        Assert.Contains("- b.txt (원래 파일: a.txt)", msg);
        Assert.Contains("링크 2개는 내용 없이(0바이트) 만들었어요(b.txt → a.txt, c_sym.txt → a.txt)", notes.HeadlessLine());
    }

    /// <summary>링크가 없는 압축은 알릴 것이 없다(창 없이 풀기 한 줄도 없음 — 종료 코드 0 그대로).</summary>
    [Fact]
    public async Task 링크가_없으면_알릴_것도_없다()
    {
        using var tmp = new TempDir("tarnolink");
        var fx = FixtureCatalog.Get("tar_default");
        using var notes = ExtractionNotes.Begin();
        await Svc.For(fx.FullPath).ExtractAsync(fx.FullPath, tmp.Dir("out"));
        Assert.False(notes.HasAny);
        Assert.Null(notes.ToMessage());
        Assert.Null(notes.HeadlessLine());
    }

    /// <summary>
    /// 계약(B4 — 검사): 하드 링크 든 tar·tgz 를 검사하면 손상은 없으니 문제 목록은 비지만, '문제없어요'로 끝내지 않고
    /// '압축 파일은 멀쩡해요(파일 2개 확인). 다만 알아 둘 것이 있어요.' + '링크 1개는 일집으로 풀면 내용 없이(0바이트) 만들어져요 …'. 화면은 알림 창(정보 창 아님).
    /// </summary>
    [Theory]
    [InlineData(".tar")]
    [InlineData(".tgz")]
    public async Task 검사도_링크를_알린다(string ext)
    {
        using var tmp = new TempDir("tarlinktest" + ext.TrimStart('.'));
        string arc = MakeHardLinkTar(tmp.Dir("in"), ext);

        var report = await ArchiveTestService.Instance.TestAsync(arc, null, null, CancellationToken.None);

        Assert.True(report.Ok, report.ToDisplayText());
        Assert.Equal(2, report.CheckedCount);
        Assert.DoesNotContain("문제없어요", report.Headline);
        Assert.Equal("압축 파일은 멀쩡해요(파일 2개 확인). 다만 알아 둘 것이 있어요.", report.Headline);
        string text = report.ToDisplayText();
        Assert.Contains("링크 1개는 일집으로 풀면 내용 없이(0바이트) 만들어져요", text);
        Assert.Contains("- b.txt (원래 파일: a.txt)", text);

        var ui = new FakeUserInteraction();
        var vm = new MainViewModel(ui, null, new TestServices().Services);
        await VmRunner.Run(() => vm.TestArchiveFileAsync(arc), "검사");
        Assert.Empty(ui.Infos);
        var w = Assert.Single(ui.Warnings);
        Assert.Contains("b.txt (원래 파일: a.txt)", w);
    }

    /// <summary>
    /// 계약(B4 — 결과 창·여러 개 풀기 요약): 우클릭 '"이름"\ 에 풀기' 하나면 알림 창 하나('링크 1개는 내용 없이…'), 여럿이면 요약 창의 알림에 그 압축 이름과 함께.
    /// 링크가 있으면 '다 푼 뒤 압축 파일을 휴지통으로'를 하지 않는다(압축 파일에만 온전한 것이 있음).
    /// </summary>
    [Fact]
    public async Task 우클릭_풀기_결과와_여러_개_요약에_링크를_알린다()
    {
        using var tmp = new TempDir("tarlinkvm");
        string work = tmp.Dir("작업");
        string one = MakeHardLinkTar(work);
        var svc = TestServices.With(e => e.DeleteArchiveAfter = true);
        var ui = new FakeUserInteraction();

        await RunShell(new MainViewModel(ui, null, svc.Services), "--extract-to-folder", one);

        var w = Assert.Single(ui.Warnings);
        Assert.Contains("‘링크묶음.tar’을 풀었어요.", w);
        Assert.Contains("링크 1개는 내용 없이(0바이트) 만들었어요", w);
        Assert.Contains("- b.txt (원래 파일: a.txt)", w);
        Assert.Empty(svc.RecycleBin.Sent);
        Assert.Empty(ui.Errors);

        // 여럿: 요약 창
        string other = Path.Combine(work, "보통.tar");
        File.Copy(FixtureCatalog.Get("tar_default").FullPath, other);
        var ui2 = new FakeUserInteraction();
        var svc2 = new TestServices();
        await RunShell(new MainViewModel(ui2, null, svc2.Services), "--extract-to-folder", one, other);
        var summary = Assert.Single(ui2.Summaries);
        Assert.Equal(2, summary.Succeeded);
        Assert.Contains(summary.Notes, n => n.StartsWith("‘링크묶음.tar’: 링크 1개는 내용 없이", StringComparison.Ordinal) && n.Contains("b.txt (원래 파일: a.txt)"));
        Assert.Contains("링크 1개는 내용 없이", summary.ToDisplayText());
    }
}
