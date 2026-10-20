using System.Diagnostics;
using System.IO.Compression;
using System.Text;
using Iljip.Models;
using Iljip.Services;
using Iljip.Services.Editing;
using Iljip.Tests.TestSupport;
using Iljip.ViewModels;

namespace Iljip.Tests.Contract;

/// <summary>
/// [5차 통합 점검(2026-10-20)] 5차 묶음 I5-A(압축 속 파일 고쳐 다시 넣기 — 이름 바뀜·일집 먼저 닫기, 7e0903d)·I5-B(안전·정직 5건, 875b9e6)가
/// 서로, 그리고 앞 기능과 함께 쓰일 때 어긋나지 않는지 본다. 묶음마다의 시험(<see cref="EntryRenameExitTests"/>·<see cref="EntryLeftoverTests"/>·
/// <see cref="ExtractSafetyNoticeTests"/>·<see cref="HeadlessNoticeTests"/>)은 자기 기능만 보므로 여기서는 겹치는 길만:
/// <list type="bullet">
/// <item>다시 넣기 추적(I5-A) × 여러 파일 열기·같은 이름(같은 이름 항목·다른 항목 이름으로 바꾸기·이름이 같은 두 압축)·암호 압축(ZIP·7Z)·나눔 압축(ZIP .z01·7Z .001).</item>
/// <item>자기 자신 덮어쓰기 막기(I5-B) × 알아서 풀기·여러 개 풀기(같이 푸는 다른 압축)·창 없이 풀기(실제 exe)·압축된 TAR(임시 폴더에서 옮기는 길).</item>
/// <item>tar 링크 알림(I5-B) × 검사 요약(여러 개 검사·bzip2·xz 로 감싼 tar)·여러 개 풀기 요약(자기 이름 비켜 풀기와 함께)·창 없이 풀기(실제 exe).</item>
/// </list>
/// 화면·셸·휴지통·파일 감시는 모두 가짜(창을 띄우지 않음 — 편집기는 파일을 잡는 FileStream·File.Move·File.WriteAllText 로 흉내).
/// 실제 exe 는 창 없이 풀기 판정일 때만, 시험 자료 폴더로(<see cref="ModuleInit.ApplyTo"/>) 띄운다.
/// </summary>
public class Wave5IntegrationTests
{
    private static Task Run(Func<Task> action, string what) => VmRunner.Run(action, what);

    private static readonly string Pw = FixtureCatalog.DefaultPassword;

    // ───────────────────────────── 재료·도우미 ─────────────────────────────

    /// <summary>
    /// 받은 자료 압축: 자료/{보고서.txt, 표.csv, 메모.txt}(+ <paramref name="withCopy"/> 면 사본/보고서.txt — 같은 이름 다른 폴더).
    /// <paramref name="split"/> 면 나눠 만든다(ZIP 은 .z01 … .zip, 7Z 는 .7z.001 …) — 큰 파일(자료/큰 파일.bin)을 넣어 조각이 셋 이상. 돌려주는 것은 열 경로.
    /// </summary>
    private static async Task<string> MakeArchive(string dir, string kind, string? password = null, bool withCopy = false, bool split = false, string name = "받은 자료")
    {
        string src = Path.Combine(dir, "원본_" + Guid.NewGuid().ToString("N")[..6]);
        string data = Path.Combine(src, "자료");
        Directory.CreateDirectory(data);
        File.WriteAllText(Path.Combine(data, "보고서.txt"), "보고서 처음");
        File.WriteAllText(Path.Combine(data, "표.csv"), "가,나");
        File.WriteAllText(Path.Combine(data, "메모.txt"), "메모 처음");
        var sources = new List<string> { data };
        if (withCopy)
        {
            string copy = Path.Combine(src, "사본");
            Directory.CreateDirectory(copy);
            File.WriteAllText(Path.Combine(copy, "보고서.txt"), "사본 보고서 처음");
            sources.Add(copy);
        }
        if (split)
        {
            var big = new byte[200_000];
            new Random(5).NextBytes(big);
            File.WriteAllBytes(Path.Combine(data, "큰 파일.bin"), big);
        }
        string arc = Path.Combine(dir, name + (kind == "7z" ? ".7z" : ".zip"));
        var options = new CompressionOptions
        {
            Password = password,
            Encryption = ZipEncryption.ZipCrypto,
            EncryptFileNames = kind == "7z" && password is not null,
            SplitBytes = split ? 64 * 1024 : null,
            Level = split ? CompressionLevelOption.Store : CompressionLevelOption.Normal,
        };
        IArchiveService svc = kind == "7z" ? new SevenZipArchiveService() : new ZipArchiveService();
        await svc.CompressAsync(sources, arc, options);
        Directory.Delete(src, recursive: true);
        if (!split) return arc;
        string open = kind == "7z" ? arc + ".001" : arc;
        Assert.True(File.Exists(open), "나눈 압축의 열 조각이 없음: " + open);
        Assert.True(Parts(open).Count >= 3, "조각이 셋 이상이어야 함: " + string.Join(", ", Parts(open).Select(Path.GetFileName)));
        return open;
    }

    /// <summary>나눈 압축의 조각 전부(열 조각 포함).</summary>
    private static List<string> Parts(string open) => ArchiveVolumes.AllParts(open).ToList();

    private static Dictionary<string, string> Shas(IEnumerable<string> files) => files.ToDictionary(f => f, TreeAssert.Sha256, StringComparer.OrdinalIgnoreCase);

    /// <summary>압축 안 파일마다 내용(글) — 큰 파일.bin 은 뺌.</summary>
    private static async Task<Dictionary<string, string>> Contents(string archive, string? password = null)
    {
        using var t = new TempDir("w5read");
        await Svc.For(archive).ExtractAsync(archive, t.Root, password);
        return TreeAssert.Files(t.Root).Where(f => !f.EndsWith(".bin", StringComparison.Ordinal))
            .ToDictionary(f => f, f => File.ReadAllText(Path.Combine(t.Root, f)), StringComparer.Ordinal);
    }

    /// <summary>압축 안 파일 이름(폴더 빼고, '/' 구분, 정렬).</summary>
    private static async Task<List<string>> Names(string archive, string? password = null) =>
        (await Svc.For(archive).ListEntriesAsync(archive, password)).Where(e => !e.IsDirectory)
            .Select(e => TreeAssert.NormalizeEntryPath(e.Path)).OrderBy(p => p, StringComparer.Ordinal).ToList();

    /// <summary>그 항목은 암호 없이는 꺼내지지 않는다(다시 넣은 항목도 암호로 잠김).</summary>
    private static async Task AssertLocked(string archive, string key)
    {
        using var t = new TempDir("w5lock");
        var ex = await Svc.Catch(() => Svc.For(archive).ExtractEntriesAsync(archive, t.Root, new[] { key }, null));
        Assert.True(ex is ArchivePasswordException, $"{key}: 암호 없이 꺼내짐(잠기지 않음) — {ex?.GetType().Name}: {ex?.Message}");
    }

    /// <summary>일집 하나(가짜 화면·가짜 셸·가짜 감시).</summary>
    private sealed class Desk : IDisposable
    {
        public Desk(params string?[] passwords)
        {
            Ts = new TestServices();
            Ui = new FakeUserInteraction(passwords);
            Vm = new MainViewModel(Ui, null, Ts.Services) { EntryChangeQuietPeriod = TimeSpan.FromMilliseconds(30) };
        }

        public TestServices Ts { get; }
        public FakeUserInteraction Ui { get; }
        public MainViewModel Vm { get; }

        public Task Open(string archive) => Run(() => Vm.OpenArchiveAsync(archive), "압축 열기");

        /// <summary>압축 안 항목을 연다(가짜 셸이 받은 경로를 돌려줌).</summary>
        public async Task<string> OpenEntry(string key)
        {
            var entry = Vm.Entries.Single(e => TreeAssert.NormalizeEntryPath(e.Path) == key);
            int before = Ts.Shell.OpenedFiles.Count;
            bool ok = false;
            await Run(async () => ok = await Vm.OpenEntryAsync(entry), "안의 파일 열기 " + key);
            Assert.True(ok, key + ": " + string.Join(" | ", Ui.Errors));
            Assert.Equal(before + 1, Ts.Shell.OpenedFiles.Count);
            return Ts.Shell.OpenedFiles[before];
        }

        /// <summary>편집기가 저장했다고 알리고(같은 순번 폴더 — 진짜 감시처럼) 확인·질문이 끝날 때까지.</summary>
        public async Task Notify(string path)
        {
            Assert.Equal(1, Ts.FileChanges.Raise(path));
            await Run(() => Vm.WhenEntryChecksIdleAsync(), "바뀜 확인");
        }

        public async Task<bool> ConfirmExit()
        {
            bool close = false;
            await Run(async () => close = await Vm.ConfirmExitAsync(), "닫기 확인");
            return close;
        }

        public void Dispose() => Vm.Shutdown();
    }

    /// <summary>편집기가 연 채 잡고 있는 것처럼(Light PDF·워드·엑셀 — 읽기·쓰기는 나누고 지우기는 막음).</summary>
    private static FileStream Hold(string path) => new(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);

    private static List<LeftoverFile> NextStartJanitor()
    {
        var found = new List<LeftoverFile>();
        TempJanitor.CleanupStale(new FakeClock(), found);
        return found;
    }

    private static string Slot(string opened) => Path.GetDirectoryName(opened)!;

    private static string Workspace(string opened) => Path.GetDirectoryName(Slot(opened))!;

    // ───────────── 다시 넣기 추적(I5-A) × 여러 파일 열기 ─────────────

    /// <summary>
    /// 계약: 한 압축에서 파일 둘을 열고(순번 폴더 따로) 하나는 편집기가 이름을 바꾼 뒤 고치고, 다른 하나는 그대로 고치면 — 질문은 파일마다 하나씩, 주어가 맞고
    /// [넣기]면 각자 원래 항목에 들어간다(이름을 바꾼 것도 원래 이름으로 — 새 이름 항목이 생기지 않음). 다른 항목은 그대로, 다 넣었으면 닫을 때 묻지 않는다.
    /// </summary>
    [Fact]
    public async Task 여러_파일을_열고_하나는_이름을_바꿔도_각자_원래_항목에_넣는다()
    {
        using var tmp = new TempDir("w5multi");
        using var _ = TestServices.UseAppRoot(tmp);
        string archive = await MakeArchive(tmp.Root, "zip");
        using var app = new Desk();
        await app.Open(archive);
        string report = await app.OpenEntry("자료/보고서.txt");
        string table = await app.OpenEntry("자료/표.csv");
        Assert.NotEqual(Slot(report), Slot(table));
        Assert.Equal(Workspace(report), Workspace(table));

        app.Ui.ChooseByLabel = "넣기";
        string renamed = Path.Combine(Slot(report), "새 이름.txt");
        File.Move(report, renamed);
        File.WriteAllText(renamed, "보고서 고침(이름 바꾼 뒤)");
        File.WriteAllText(table, "다,라");
        await app.Notify(renamed);
        await app.Notify(table);

        Assert.Empty(app.Ui.Errors);
        Assert.Equal(2, app.Ui.ChoicePrompts.Count);
        Assert.Equal("‘새 이름.txt’(압축 안 이름 ‘보고서.txt’)가 바뀌었어요. 압축 파일에 다시 넣을까요? 압축 파일에는 원래 이름(‘보고서.txt’) 그대로 넣어요.", app.Ui.ChoicePrompts[0].Message);
        Assert.Equal("‘표.csv’가 바뀌었어요. 압축 파일에 다시 넣을까요?", app.Ui.ChoicePrompts[1].Message);
        Assert.Equal(new[] { "자료/메모.txt", "자료/보고서.txt", "자료/표.csv" }, await Names(archive));
        var c = await Contents(archive);
        Assert.Equal("보고서 고침(이름 바꾼 뒤)", c["자료/보고서.txt"]);
        Assert.Equal("다,라", c["자료/표.csv"]);
        Assert.Equal("메모 처음", c["자료/메모.txt"]);

        Assert.True(await app.ConfirmExit());
        Assert.Equal(2, app.Ui.ChoicePrompts.Count);   // 다 넣음 — 닫을 때 더 묻지 않음
    }

    /// <summary>
    /// 계약: 연 파일 둘(하나는 이름을 바꿈)을 편집기가 잡은 채 일집을 닫으면 '연 파일 2개가 아직 …' 질문 하나(둘 다 이름이 보임), [닫기] → 닫은 뒤 둘 다 저장 →
    /// 다음 시작 청소가 둘 다 지우지 않고 찾음 → 알림 하나 → [압축에 넣기] → 둘 다 원래 자리에(새 이름 항목 없음), 작업 폴더는 지워지고 다시 켜면 더 묻지 않는다.
    /// </summary>
    [Fact]
    public async Task 여러_파일을_연_채_닫으면_한_번_묻고_다음_시작에_모두_원래_자리에_넣는다()
    {
        using var tmp = new TempDir("w5multiexit");
        using var _ = TestServices.UseAppRoot(tmp);
        string archive = await MakeArchive(tmp.Root, "zip");
        string report, table, renamed;
        using (var app = new Desk())
        {
            await app.Open(archive);
            report = await app.OpenEntry("자료/보고서.txt");
            table = await app.OpenEntry("자료/표.csv");
            renamed = Path.Combine(Slot(report), "새 이름.txt");
            File.Move(report, renamed);
            using (Hold(renamed))
            using (Hold(table))
            {
                app.Ui.ChooseByLabel = "닫기";
                Assert.True(await app.ConfirmExit());
                var ask = Assert.Single(app.Ui.ChoicePrompts);
                Assert.StartsWith("압축 속에서 연 파일 2개가 아직 다른 프로그램에서 열려 있어요.\n" +
                                  "- ‘새 이름.txt’(압축 안 이름 ‘보고서.txt’) — 받은 자료.zip\n- ‘표.csv’ — 받은 자료.zip\n\n", ask.Message);
                Assert.Equal(new[] { "닫기", "취소" }, ask.Choices);
                app.Vm.Shutdown();
            }
        }
        File.WriteAllText(renamed, "닫은 뒤 보고서 저장");
        File.WriteAllText(table, "닫은 뒤 표 저장");

        var found = NextStartJanitor();
        Assert.Equal(new[] { "자료/보고서.txt", "자료/표.csv" }, found.Select(l => l.EntryKey).OrderBy(k => k, StringComparer.Ordinal));
        using var next = new Desk();
        next.Ui.ChooseByLabel = "압축에 넣기";
        await Run(() => next.Vm.OfferLeftoverChangesAsync(found), "다음 시작 알림");
        var offer = Assert.Single(next.Ui.ChoicePrompts);
        Assert.Contains("- ‘새 이름.txt’(압축 안 이름 ‘보고서.txt’) — 받은 자료.zip\n- ‘표.csv’ — 받은 자료.zip", offer.Message);
        Assert.Empty(next.Ui.Errors);
        Assert.Equal("고친 파일 2개를 압축 파일에 넣었어요", next.Vm.StatusText);
        Assert.Equal(new[] { "자료/메모.txt", "자료/보고서.txt", "자료/표.csv" }, await Names(archive));
        var c = await Contents(archive);
        Assert.Equal("닫은 뒤 보고서 저장", c["자료/보고서.txt"]);
        Assert.Equal("닫은 뒤 표 저장", c["자료/표.csv"]);
        Assert.Equal("메모 처음", c["자료/메모.txt"]);
        Assert.False(Directory.Exists(Workspace(table)), "다 넣은 뒤 작업 폴더가 남음");
        Assert.Empty(NextStartJanitor());
    }

    // ───────────── 다시 넣기 추적(I5-A) × 같은 이름 ─────────────

    /// <summary>
    /// 계약: 이름이 같은 두 항목(자료/보고서.txt·사본/보고서.txt)을 열면 — 둘째를 고쳐 [넣기]면 사본/보고서.txt 만 바뀌고, 첫째를 연 채 닫은 뒤 저장하면
    /// 다음 시작에 그 항목(자료/보고서.txt)으로 넣는다(같은 이름이라고 서로의 자리에 들어가지 않음).
    /// </summary>
    [Fact]
    public async Task 이름이_같은_두_항목은_각자_원래_자리에_넣는다()
    {
        using var tmp = new TempDir("w5same");
        using var _ = TestServices.UseAppRoot(tmp);
        string archive = await MakeArchive(tmp.Root, "zip", withCopy: true);
        string first;
        using (var app = new Desk())
        {
            await app.Open(archive);
            first = await app.OpenEntry("자료/보고서.txt");
            string second = await app.OpenEntry("사본/보고서.txt");
            Assert.Equal(Path.GetFileName(first), Path.GetFileName(second));
            Assert.NotEqual(Slot(first), Slot(second));

            app.Ui.ChooseByLabel = "넣기";
            File.WriteAllText(second, "사본 고침");
            await app.Notify(second);
            Assert.Equal("‘보고서.txt’가 바뀌었어요. 압축 파일에 다시 넣을까요?", Assert.Single(app.Ui.ChoicePrompts).Message);
            var c1 = await Contents(archive);
            Assert.Equal("사본 고침", c1["사본/보고서.txt"]);
            Assert.Equal("보고서 처음", c1["자료/보고서.txt"]);

            using (Hold(first))
            {
                app.Ui.ChooseByLabel = "닫기";
                Assert.True(await app.ConfirmExit());
                Assert.StartsWith("압축 속에서 연 파일 1개가", app.Ui.ChoicePrompts[^1].Message);
                app.Vm.Shutdown();
            }
        }
        File.WriteAllText(first, "자료 보고서 닫은 뒤 저장");

        var l = Assert.Single(NextStartJanitor());
        Assert.Equal("자료/보고서.txt", l.EntryKey);
        using var next = new Desk();
        next.Ui.ChooseByLabel = "압축에 넣기";
        await Run(() => next.Vm.OfferLeftoverChangesAsync(new[] { l }), "다음 시작 알림");
        Assert.Empty(next.Ui.Errors);
        var c = await Contents(archive);
        Assert.Equal("자료 보고서 닫은 뒤 저장", c["자료/보고서.txt"]);
        Assert.Equal("사본 고침", c["사본/보고서.txt"]);
        Assert.Equal(new[] { "사본/보고서.txt", "자료/메모.txt", "자료/보고서.txt", "자료/표.csv" }, await Names(archive));
    }

    /// <summary>
    /// 계약: 편집기가 연 파일 이름을 압축의 다른 항목과 같은 이름(메모.txt)으로 바꿔 고쳐도 — 넣는 자리는 원래 항목(자료/보고서.txt)이고 자료/메모.txt 는 그대로.
    /// </summary>
    [Fact]
    public async Task 다른_항목과_같은_이름으로_바꿔_고쳐도_그_항목은_건드리지_않는다()
    {
        using var tmp = new TempDir("w5samename");
        using var _ = TestServices.UseAppRoot(tmp);
        string archive = await MakeArchive(tmp.Root, "zip");
        using var app = new Desk();
        await app.Open(archive);
        string report = await app.OpenEntry("자료/보고서.txt");
        string renamed = Path.Combine(Slot(report), "메모.txt");
        File.Move(report, renamed);
        File.WriteAllText(renamed, "메모라는 이름으로 바꿔 고친 보고서");
        app.Ui.ChooseByLabel = "넣기";
        await app.Notify(renamed);

        Assert.Equal("‘메모.txt’(압축 안 이름 ‘보고서.txt’)가 바뀌었어요. 압축 파일에 다시 넣을까요? 압축 파일에는 원래 이름(‘보고서.txt’) 그대로 넣어요.",
            Assert.Single(app.Ui.ChoicePrompts).Message);
        Assert.Empty(app.Ui.Errors);
        Assert.Empty(app.Ui.CollisionPrompts);
        var c = await Contents(archive);
        Assert.Equal("메모라는 이름으로 바꿔 고친 보고서", c["자료/보고서.txt"]);
        Assert.Equal("메모 처음", c["자료/메모.txt"]);
        Assert.Equal(new[] { "자료/메모.txt", "자료/보고서.txt", "자료/표.csv" }, await Names(archive));
    }

    /// <summary>
    /// 계약: 이름이 같은 두 압축(다른 폴더의 '받은 자료.zip')에서 하나씩 열고 둘 다 연 채 닫은 뒤 저장 — 다음 시작에 각자의 압축(전체 경로)에 넣는다.
    /// </summary>
    [Fact]
    public async Task 이름이_같은_두_압축의_남은_파일은_각자의_압축에_넣는다()
    {
        using var tmp = new TempDir("w5twoarc");
        using var _ = TestServices.UseAppRoot(tmp);
        string a = await MakeArchive(tmp.Dir("가 부서"), "zip");
        string b = await MakeArchive(tmp.Dir("나 부서"), "zip");
        string inA, inB;
        using (var app = new Desk())
        {
            await app.Open(a);
            inA = await app.OpenEntry("자료/보고서.txt");
            await app.Open(b);   // 다른 압축으로 — 앞 압축의 작업 폴더는 쉬게 함(지우지 않음)
            inB = await app.OpenEntry("자료/보고서.txt");
            Assert.NotEqual(Workspace(inA), Workspace(inB));
            using (Hold(inA))
            using (Hold(inB))
            {
                app.Ui.ChooseByLabel = "닫기";
                Assert.True(await app.ConfirmExit());
                Assert.StartsWith("압축 속에서 연 파일 2개가", Assert.Single(app.Ui.ChoicePrompts).Message);
                app.Vm.Shutdown();
            }
        }
        File.WriteAllText(inA, "가 부서에서 저장");
        File.WriteAllText(inB, "나 부서에서 저장");

        var found = NextStartJanitor();
        Assert.Equal(2, found.Count);
        Assert.Equal(new[] { Path.GetFullPath(a), Path.GetFullPath(b) }.OrderBy(p => p, StringComparer.OrdinalIgnoreCase),
            found.Select(l => l.ArchivePath).OrderBy(p => p, StringComparer.OrdinalIgnoreCase));
        using var next = new Desk();
        next.Ui.ChooseByLabel = "압축에 넣기";
        await Run(() => next.Vm.OfferLeftoverChangesAsync(found), "다음 시작 알림");
        Assert.Empty(next.Ui.Errors);
        Assert.Equal("가 부서에서 저장", (await Contents(a))["자료/보고서.txt"]);
        Assert.Equal("나 부서에서 저장", (await Contents(b))["자료/보고서.txt"]);
        Assert.Empty(NextStartJanitor());
    }

    // ───────────── 다시 넣기 추적(I5-A) × 암호 압축 ─────────────

    /// <summary>
    /// 계약: 암호 압축(ZIP ZipCrypto·7Z 이름까지 잠김)에서 연 파일을 편집기가 이름을 바꾼 뒤 고쳐 [넣기]면 — 암호를 다시 묻지 않고(연 때의 암호), 원래 항목에
    /// 넣으며 그 항목도 암호로 잠긴 채다(새 이름 항목 없음, 다른 항목 그대로).
    /// </summary>
    [Theory]
    [InlineData("zip")]
    [InlineData("7z")]
    public async Task 암호_압축에서_이름을_바꿔_고쳐도_잠긴_채_원래_항목에_넣는다(string kind)
    {
        using var tmp = new TempDir("w5pw" + kind);
        using var _ = TestServices.UseAppRoot(tmp);
        string archive = await MakeArchive(tmp.Root, kind, Pw);
        using var app = new Desk(Pw);
        await app.Open(archive);
        string report = await app.OpenEntry("자료/보고서.txt");
        int asked = app.Ui.PasswordPrompts.Count;
        Assert.Equal(1, asked);
        string renamed = Path.Combine(Slot(report), "새 이름.txt");
        File.Move(report, renamed);
        File.WriteAllText(renamed, "암호 압축 — 이름 바꿔 고침");
        app.Ui.ChooseByLabel = "넣기";
        await app.Notify(renamed);

        Assert.Empty(app.Ui.Errors);
        Assert.Equal(asked, app.Ui.PasswordPrompts.Count);
        Assert.Equal(new[] { "자료/메모.txt", "자료/보고서.txt", "자료/표.csv" }, await Names(archive, Pw));
        var c = await Contents(archive, Pw);
        Assert.Equal("암호 압축 — 이름 바꿔 고침", c["자료/보고서.txt"]);
        Assert.Equal("메모 처음", c["자료/메모.txt"]);
        await AssertLocked(archive, "자료/보고서.txt");
    }

    /// <summary>
    /// 계약: 암호 ZIP 에서 연 파일 둘을 연 채 닫은 뒤 저장 → 다음 시작 [압축에 넣기] — 암호는 <b>한 번만</b> 묻고(같은 압축의 둘째 파일은 그 암호로),
    /// 둘 다 잠긴 채 원래 자리에 들어간다.
    /// </summary>
    [Fact]
    public async Task 암호_압축의_남은_파일_여럿은_다음_시작에_암호를_한_번만_묻는다()
    {
        using var tmp = new TempDir("w5pwleft");
        using var _ = TestServices.UseAppRoot(tmp);
        string archive = await MakeArchive(tmp.Root, "zip", Pw);
        string report, table;
        using (var app = new Desk(Pw))
        {
            await app.Open(archive);
            report = await app.OpenEntry("자료/보고서.txt");
            table = await app.OpenEntry("자료/표.csv");
            using (Hold(report))
            using (Hold(table))
            {
                app.Ui.ChooseByLabel = "닫기";
                Assert.True(await app.ConfirmExit());
                app.Vm.Shutdown();
            }
        }
        File.WriteAllText(report, "암호 압축 — 닫은 뒤 보고서");
        File.WriteAllText(table, "암호 압축 — 닫은 뒤 표");

        var found = NextStartJanitor();
        Assert.Equal(2, found.Count);
        using var next = new Desk(Pw);   // 암호 답은 하나뿐 — 두 번 물으면 둘째는 [취소]
        next.Ui.ChooseByLabel = "압축에 넣기";
        await Run(() => next.Vm.OfferLeftoverChangesAsync(found), "다음 시작 알림");

        Assert.Empty(next.Ui.Errors);
        Assert.Single(next.Ui.PasswordPrompts);
        Assert.Equal("고친 파일 2개를 압축 파일에 넣었어요", next.Vm.StatusText);
        var c = await Contents(archive, Pw);
        Assert.Equal("암호 압축 — 닫은 뒤 보고서", c["자료/보고서.txt"]);
        Assert.Equal("암호 압축 — 닫은 뒤 표", c["자료/표.csv"]);
        await AssertLocked(archive, "자료/보고서.txt");
        await AssertLocked(archive, "자료/표.csv");
        Assert.Empty(NextStartJanitor());
    }

    /// <summary>
    /// 계약: 다음 시작 [압축에 넣기]에서 암호를 틀리면 다시 묻고, [취소]하면 넣지 않고 파일을 그대로 둔다(압축 그대로) — 다음 시작에 또 알린다.
    /// </summary>
    [Fact]
    public async Task 암호를_틀리거나_취소해도_남은_파일을_잃지_않는다()
    {
        using var tmp = new TempDir("w5pwcancel");
        using var _ = TestServices.UseAppRoot(tmp);
        string archive = await MakeArchive(tmp.Root, "zip", Pw);
        string report;
        using (var app = new Desk(Pw))
        {
            await app.Open(archive);
            report = await app.OpenEntry("자료/보고서.txt");
            using (Hold(report))
            {
                app.Ui.ChooseByLabel = "닫기";
                Assert.True(await app.ConfirmExit());
                app.Vm.Shutdown();
            }
        }
        File.WriteAllText(report, "넣지 못해도 잃으면 안 됨");
        string before = TreeAssert.Sha256(archive);

        using var next = new Desk("틀린 암호", null);
        next.Ui.ChooseByLabel = "압축에 넣기";
        await Run(() => next.Vm.OfferLeftoverChangesAsync(NextStartJanitor()), "다음 시작 알림");

        Assert.Equal(2, next.Ui.PasswordPrompts.Count);
        Assert.Equal(PasswordPromptReason.Wrong, next.Ui.PasswordPrompts[1].Reason);
        Assert.Equal(before, TreeAssert.Sha256(archive));
        Assert.Equal("넣지 못해도 잃으면 안 됨", File.ReadAllText(report));
        Assert.Contains("그대로 두었어요", next.Vm.StatusText);
        Assert.Single(NextStartJanitor());
    }

    // ───────────── 다시 넣기 추적(I5-A) × 나눔 압축 ─────────────

    /// <summary>
    /// 계약: 나눈 압축(ZIP .z01…·7Z .001…)은 고칠 수 없다 — 연 파일 이름을 편집기가 바꾼 뒤 고쳐도 ‘‘새 이름.txt’(압축 안 이름 ‘보고서.txt’)가 바뀌었어요.
    /// 이 형식은 고칠 수 없어요. 바뀐 파일을 다른 곳에 저장할까요?’('원래 이름 그대로 넣어요'는 붙이지 않음) → 저장 창(새 이름) → 복사. 조각은 바이트 그대로.
    /// </summary>
    [Theory]
    [InlineData("zip")]
    [InlineData("7z")]
    public async Task 나눈_압축에서_이름을_바꿔_고치면_다른_곳에_저장하게_한다(string kind)
    {
        using var tmp = new TempDir("w5split" + kind);
        using var _ = TestServices.UseAppRoot(tmp);
        string open = await MakeArchive(tmp.Dir("받은 첨부"), kind, split: true);
        var parts = Shas(Parts(open));
        using var app = new Desk();
        await app.Open(open);
        string report = await app.OpenEntry("자료/보고서.txt");
        string renamed = Path.Combine(Slot(report), "새 이름.txt");
        File.Move(report, renamed);
        File.WriteAllText(renamed, "나눈 압축에서 고침");
        app.Ui.ChooseByLabel = "다른 곳에 저장";
        app.Ui.SavePath = tmp.P("저장한 보고서.txt");
        await app.Notify(renamed);

        Assert.Equal("‘새 이름.txt’(압축 안 이름 ‘보고서.txt’)가 바뀌었어요. 이 형식은 고칠 수 없어요. 바뀐 파일을 다른 곳에 저장할까요?",
            Assert.Single(app.Ui.ChoicePrompts).Message);
        Assert.Equal(new[] { "새 이름.txt" }, app.Ui.SaveDefaultNames);
        Assert.Equal("나눈 압축에서 고침", File.ReadAllText(tmp.P("저장한 보고서.txt")));
        Assert.Empty(app.Ui.Errors);
        Assert.Empty(app.Ui.Warnings);
        foreach (var (p, sha) in parts) Assert.Equal(sha, TreeAssert.Sha256(p));
        Assert.True(await app.ConfirmExit());   // 저장했으면 닫을 때 묻지 않음
        Assert.Single(app.Ui.ChoicePrompts);
    }

    /// <summary>
    /// 계약: 나눈 압축에서 연 파일을 연 채 닫은 뒤 저장 → 다음 시작 [압축에 넣기] → ‘‘보고서.txt’를 압축 파일에 넣을 수 없어요. 이 형식은 고칠 수 없어요. 고친 내용을
    /// 잃지 않게 다른 곳에 저장할까요?’ → [다른 곳에 저장] → 복사·순번 폴더 지움. 조각은 바이트 그대로, 다시 켜면 더 묻지 않는다.
    /// </summary>
    [Theory]
    [InlineData("zip")]
    [InlineData("7z")]
    public async Task 나눈_압축의_남은_파일은_다음_시작에_다른_곳에_저장하게_한다(string kind)
    {
        using var tmp = new TempDir("w5splitleft" + kind);
        using var _ = TestServices.UseAppRoot(tmp);
        string open = await MakeArchive(tmp.Dir("받은 첨부"), kind, split: true);
        var parts = Shas(Parts(open));
        string report;
        using (var app = new Desk())
        {
            await app.Open(open);
            report = await app.OpenEntry("자료/보고서.txt");
            using (Hold(report))
            {
                app.Ui.ChooseByLabel = "닫기";
                Assert.True(await app.ConfirmExit());
                app.Vm.Shutdown();
            }
        }
        File.WriteAllText(report, "나눈 압축 — 닫은 뒤 저장");

        using var next = new Desk();
        next.Ui.ChoiceAnswers.Enqueue(0);   // 압축에 넣기
        next.Ui.ChoiceAnswers.Enqueue(0);   // 다른 곳에 저장
        next.Ui.SavePath = tmp.P("살린 보고서.txt");
        await Run(() => next.Vm.OfferLeftoverChangesAsync(NextStartJanitor()), "다음 시작 알림");

        Assert.Equal(2, next.Ui.ChoicePrompts.Count);
        Assert.Equal("‘보고서.txt’를 압축 파일에 넣을 수 없어요. 이 형식은 고칠 수 없어요. 고친 내용을 잃지 않게 다른 곳에 저장할까요?", next.Ui.ChoicePrompts[1].Message);
        Assert.Equal("나눈 압축 — 닫은 뒤 저장", File.ReadAllText(tmp.P("살린 보고서.txt")));
        Assert.Equal("고친 파일 1개를 다른 곳에 저장했어요", next.Vm.StatusText);
        Assert.False(File.Exists(report));
        Assert.Empty(next.Ui.Errors);
        foreach (var (p, sha) in parts) Assert.Equal(sha, TreeAssert.Sha256(p));
        Assert.Empty(NextStartJanitor());
    }

    /// <summary>
    /// 계약: 다음 시작 [압축에 넣기]를 누른 순간 그 파일을 다른 프로그램이 혼자 잡아 읽을 수 없으면(청소 뒤 다시 엶·백신 검사) — 압축의 원래 항목을 빼지 않고
    /// (압축 바이트 그대로) 고친 파일도 지우지 않는다. 다음 시작에 또 알린다.
    /// </summary>
    [Fact]
    public async Task 넣는_순간_파일을_못_읽으면_압축의_원래_항목도_고친_파일도_잃지_않는다()
    {
        using var tmp = new TempDir("w5leftlock");
        using var _ = TestServices.UseAppRoot(tmp);
        string archive = await MakeArchive(tmp.Root, "zip");
        string report;
        using (var app = new Desk())
        {
            await app.Open(archive);
            report = await app.OpenEntry("자료/보고서.txt");
            using (Hold(report))
            {
                app.Ui.ChooseByLabel = "닫기";
                Assert.True(await app.ConfirmExit());
                app.Vm.Shutdown();
            }
        }
        File.WriteAllText(report, "닫은 뒤 저장 — 넣는 순간 잠김");
        var found = NextStartJanitor();
        Assert.Single(found);
        string before = TreeAssert.Sha256(archive);

        using var next = new Desk();
        next.Ui.ChooseByLabel = "압축에 넣기";
        using (new FileStream(report, FileMode.Open, FileAccess.Read, FileShare.None))   // 혼자 잡음
            await Run(() => next.Vm.OfferLeftoverChangesAsync(found), "다음 시작 알림(잠김)");

        Assert.Equal(before, TreeAssert.Sha256(archive));
        Assert.Equal(new[] { "자료/메모.txt", "자료/보고서.txt", "자료/표.csv" }, await Names(archive));
        Assert.True(File.Exists(report), "넣지 못한 고친 파일을 지움");
        Assert.Equal("닫은 뒤 저장 — 넣는 순간 잠김", File.ReadAllText(report));
        Assert.Equal("고친 파일 1개는 그대로 두었어요 — 다음에 일집을 켤 때 다시 알려 드려요", next.Vm.StatusText);
        var problem = Assert.Single(next.Ui.Problems);
        Assert.StartsWith("‘보고서.txt’를 읽지 못해 압축 파일에 넣지 못했어요. 고친 파일은 그대로 두었어요", problem.Heading);
        Assert.Single(next.Ui.ChoicePrompts);   // '다른 곳에 저장' 은 권하지 않음(읽지 못하는 파일 — 그대로 두고 다음에)
        Assert.False(Directory.Exists(Slot(report) + MainViewModel.ReinsertSuffix), "넣을 내용 복사 폴더가 남음");
        Assert.Single(NextStartJanitor());
    }

    /// <summary>
    /// 계약: 다음 시작 넣기의 상태 줄은 넣은 것·넣는 동안 또 저장돼 남긴 것·다른 곳에 저장한 것·그대로 둔 것을 따로 말한다(예전엔 하나도 못 넣거나 다른 곳에 저장만 해도
    /// '고친 파일 0개를 압축 파일에 넣었어요', 넣는 동안 또 저장된 하나는 '1개를 넣었어요. 1개는 그대로…'처럼 한 파일을 둘로 셌다). 넣은 것과 그대로 둔 것만 있으면 예전 문구 그대로.
    /// </summary>
    [Theory]
    [InlineData(2, 0, 0, 0, "고친 파일 2개를 압축 파일에 넣었어요")]
    [InlineData(0, 0, 0, 1, "고친 파일 1개는 그대로 두었어요 — 다음에 일집을 켤 때 다시 알려 드려요")]
    [InlineData(1, 0, 0, 1, "고친 파일 1개를 압축 파일에 넣었어요. 1개는 그대로 두었어요 — 다음에 일집을 켤 때 다시 알려 드려요")]
    [InlineData(1, 1, 0, 0, "고친 파일 1개를 압축 파일에 넣었어요. 그중 1개는 넣는 동안 또 저장돼서 지우지 않았어요 — 다음에 일집을 켤 때 다시 알려 드려요")]
    [InlineData(0, 0, 1, 0, "고친 파일 1개를 다른 곳에 저장했어요")]
    [InlineData(1, 0, 1, 1, "고친 파일 1개를 압축 파일에 넣었어요. 1개는 다른 곳에 저장했어요. 1개는 그대로 두었어요 — 다음에 일집을 켤 때 다시 알려 드려요")]
    public void 다음_시작_넣기의_상태_줄은_넣은_것과_남긴_것을_따로_말한다(int inserted, int savedAgain, int elsewhere, int kept, string expected) =>
        Assert.Equal(expected, MainViewModel.LeftoverStatus(inserted, savedAgain, elsewhere, kept));

    /// <summary>
    /// 계약(고치기 바탕 — 다시 넣기·다음 시작 넣기·일반 넣기의 [덮어쓰기]): 같은 이름을 덮어쓰기로 넣는데 넣을 원본을 다른 프로그램이 혼자 잡아 읽지 못하면
    /// 원래 항목을 지우지 않는다 — 그것 하나뿐이면 압축 바이트 그대로('넣을 수 있는 파일이 하나도 없어서…'), 같이 넣는 다른 파일이 있으면 그것만 넣고 원래 항목은 남는다.
    /// 예전엔 원래 항목은 지운 채 새 파일만 빠져 압축에서 그 항목이 사라졌다(I5-A 다음 시작 [압축에 넣기]에서 드러남).
    /// </summary>
    [Theory]
    [InlineData("zip")]
    [InlineData("7z")]
    public async Task 덮어쓰기로_넣을_원본을_못_읽으면_원래_항목을_지우지_않는다(string kind)
    {
        using var tmp = new TempDir("w5editlock" + kind);
        string archive = await MakeArchive(tmp.Root, kind);
        string sha = TreeAssert.Sha256(archive);
        string src = tmp.Dir("넣을 것");
        string locked = Path.Combine(src, "보고서.txt");
        File.WriteAllText(locked, "잠긴 새 보고서");
        string other = Path.Combine(src, "새 파일.txt");
        File.WriteAllText(other, "같이 넣는 새 파일");
        var editor = ArchiveServiceLocator.EditorFor(archive)!;
        EditResult r;
        using (new FileStream(locked, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            using (ExtractionScope.Begin(new FixedResolver(CollisionChoice.Overwrite)))
            {
                var ex = await Svc.Catch(() => editor.EditAsync(archive,
                    new EditOp[] { new EditOp.Add(new[] { locked }, "자료", AsName: "보고서.txt", ReplaceExisting: true) }, null, null, CancellationToken.None));
                Assert.True(ex is ArchiveEditException, $"{kind}: {ex?.GetType().Name} {ex?.Message}");
            }
            Assert.Equal(sha, TreeAssert.Sha256(archive));

            using (ExtractionScope.Begin(new FixedResolver(CollisionChoice.Overwrite)))
                r = await editor.EditAsync(archive, new EditOp[] { new EditOp.Add(new[] { locked, other }, "자료") }, null, null, CancellationToken.None);
        }
        Assert.Single(r.Skipped);
        Assert.Equal(new[] { "자료/메모.txt", "자료/보고서.txt", "자료/새 파일.txt", "자료/표.csv" }, await Names(archive));
        var c = await Contents(archive);
        Assert.Equal("보고서 처음", c["자료/보고서.txt"]);
        Assert.Equal("같이 넣는 새 파일", c["자료/새 파일.txt"]);
    }

    /// <summary>
    /// 계약(위의 이어짐): 한 번에 넣는 파일 둘이 같은 이름(다른 폴더의 '보고서.txt' 둘 — 둘째가 첫째를 [덮어쓰기])이고 마지막 것을 못 읽으면 —
    /// 엔진이 멈추거나 압축을 깨지 않고, 원래 항목(첫째가 대신했던 것)을 되살린다. 그것뿐이면 압축 바이트 그대로, 다른 새 파일이 있으면 그것만 들어간다.
    /// </summary>
    [Theory]
    [InlineData("zip")]
    [InlineData("7z")]
    public async Task 같은_이름_둘을_넣다가_뒤의_것을_못_읽어도_원래_항목이_남는다(string kind)
    {
        using var tmp = new TempDir("w5editlock2" + kind);
        string archive = await MakeArchive(tmp.Root, kind);
        string sha = TreeAssert.Sha256(archive);
        string first = Path.Combine(tmp.Dir("넣을 것 1"), "보고서.txt");
        File.WriteAllText(first, "앞의 새 보고서");
        string locked = Path.Combine(tmp.Dir("넣을 것 2"), "보고서.txt");
        File.WriteAllText(locked, "잠긴 새 보고서");
        string other = Path.Combine(tmp.Dir("넣을 것 3"), "새 파일.txt");
        File.WriteAllText(other, "같이 넣는 새 파일");
        var editor = ArchiveServiceLocator.EditorFor(archive)!;
        EditResult r;
        using (new FileStream(locked, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            using (ExtractionScope.Begin(new FixedResolver(CollisionChoice.Overwrite)))
            {
                var ex = await Svc.Catch(() => editor.EditAsync(archive,
                    new EditOp[] { new EditOp.Add(new[] { first, locked }, "자료") }, null, null, CancellationToken.None));
                Assert.True(ex is ArchiveEditException, $"{kind}: {ex?.GetType().Name} {ex?.Message}");
            }
            Assert.Equal(sha, TreeAssert.Sha256(archive));

            using (ExtractionScope.Begin(new FixedResolver(CollisionChoice.Overwrite)))
                r = await editor.EditAsync(archive, new EditOp[] { new EditOp.Add(new[] { first, locked, other }, "자료") }, null, null, CancellationToken.None);
        }
        Assert.Single(r.Skipped);
        Assert.Equal(new[] { "자료/메모.txt", "자료/보고서.txt", "자료/새 파일.txt", "자료/표.csv" }, await Names(archive));
        var c = await Contents(archive);
        Assert.Equal("보고서 처음", c["자료/보고서.txt"]);
        Assert.Equal("같이 넣는 새 파일", c["자료/새 파일.txt"]);
    }

    // ───────────── 자기 자신 덮어쓰기 막기(I5-B) × 알아서 풀기 ─────────────

    /// <summary>이중 압축 첨부: <paramref name="dir"/>\첨부.fmt(바깥) = { 첨부.fmt(안쪽 — 본문.txt) [+ 설명.txt] }. 돌려주는 것: 바깥 경로, 안쪽 바이트.</summary>
    private static async Task<(string Outer, byte[] Inner)> DoubleArchive(TempDir tmp, string fmt, string dir, bool withNote = true, string baseName = "첨부")
    {
        IArchiveService svc = fmt == "7z" ? new SevenZipArchiveService() : new ZipArchiveService();
        string work = tmp.Dir("재료_" + Guid.NewGuid().ToString("N")[..6]);
        string body = Path.Combine(work, "본문.txt");
        File.WriteAllText(body, "안쪽 압축 본문 — " + fmt);
        string innerDir = Path.Combine(work, "안쪽");
        Directory.CreateDirectory(innerDir);
        string inner = Path.Combine(innerDir, $"{baseName}.{fmt}");
        await svc.CompressAsync(new[] { body }, inner);
        var sources = new List<string> { inner };
        if (withNote)
        {
            string note = Path.Combine(work, "설명.txt");
            File.WriteAllText(note, "바깥 설명");
            sources.Add(note);
        }
        Directory.CreateDirectory(dir);
        string outer = Path.Combine(dir, $"{baseName}.{fmt}");
        await svc.CompressAsync(sources, outer);
        return (outer, File.ReadAllBytes(inner));
    }

    /// <summary>
    /// 계약: 알아서 풀기(우클릭 '--extract-smart')로 바깥과 같은 이름의 압축 하나만 든 첨부를 풀면(설정 '같은 이름이면 덮어쓰기' + '다 푼 뒤 휴지통으로') —
    /// '이름' 폴더(첨부\첨부.확장자)로 풀어 바깥을 덮을 일이 없고(알릴 것 없음), 휴지통으로 가는 것은 바깥(원래) 압축 바이트 그대로다.
    /// </summary>
    [Theory]
    [InlineData("zip")]
    [InlineData("7z")]
    public async Task 알아서_풀기는_자기_이름_하나뿐인_첨부를_이름_폴더로_풀고_바깥은_그대로(string fmt)
    {
        using var tmp = new TempDir("w5smart" + fmt);
        string d = tmp.Dir("받은 첨부");
        var (outer, inner) = await DoubleArchive(tmp, fmt, d, withNote: false);
        byte[] outerBytes = File.ReadAllBytes(outer);
        var svc = TestServices.With(e => { e.OnCollision = CollisionPolicy.Overwrite; e.DeleteArchiveAfter = true; });
        svc.RecycleBin.OnSend = paths => Assert.All(paths, p => Assert.Equal(outerBytes, File.ReadAllBytes(p)));
        var ui = new FakeUserInteraction();

        await Run(() => new MainViewModel(ui, null, svc.Services).ProcessShellCommandAsync(new[] { "--extract-smart", outer }), "알아서 풀기");

        Assert.Empty(ui.Errors);
        Assert.Empty(ui.Warnings);
        Assert.Equal(outerBytes, File.ReadAllBytes(outer));
        Assert.Equal(TreeAssert.Sha256(inner), TreeAssert.Sha256(Path.Combine(d, "첨부", $"첨부.{fmt}")));
        Assert.Equal(new[] { outer }, svc.RecycleBin.AllSent);
        Assert.Equal(new[] { $"첨부.{fmt}", $"첨부/첨부.{fmt}" }, TreeAssert.Files(d).OrderBy(f => f, StringComparer.Ordinal));
    }

    /// <summary>
    /// 계약: 알아서 풀기가 '바로 풀기'(맨 위가 하나)로 정했는데 그 하나가 풀고 있는 나눈 압축의 다른 조각 이름이면(자료.7z.001 세트 안의 '자료.7z.002') —
    /// 그 조각을 덮지 않고 '자료.7z (1).002' 로 비켜 풀고 알린다(조각 전부 바이트 그대로).
    /// </summary>
    [Fact]
    public async Task 알아서_풀기에서_맨_위_하나가_다른_조각_이름이어도_덮지_않는다()
    {
        using var tmp = new TempDir("w5smartvol");
        string src = tmp.Dir("원본");
        var payload = new byte[200_000];
        new Random(25).NextBytes(payload);
        File.WriteAllBytes(Path.Combine(src, "자료.7z.002"), payload);
        string d = tmp.Dir("받은 첨부");
        await new SevenZipArchiveService().CompressAsync(new[] { Path.Combine(src, "자료.7z.002") }, Path.Combine(d, "자료.7z"),
            new CompressionOptions { SplitBytes = 64 * 1024, Level = CompressionLevelOption.Store });
        string first = Path.Combine(d, "자료.7z.001");
        var parts = Shas(Parts(first));
        Assert.True(parts.Count >= 3);
        var ui = new FakeUserInteraction();

        var r = await new BatchExtractor(new ArchiveServiceLocator(), ui, new FixedResolver(CollisionChoice.Overwrite), ExtractTargetRule.Smart, new[] { first })
            .RunAsync(ui.ShowProgress("x"));

        var o = Assert.Single(r.Outcomes);
        Assert.True(o.Succeeded, o.Reason);
        Assert.Equal(d, o.Destination);   // 바로 풀기(맨 위 하나)
        foreach (var (p, sha) in parts) Assert.Equal(sha, TreeAssert.Sha256(p));
        Assert.Equal(TreeAssert.Sha256(payload), TreeAssert.Sha256(Path.Combine(d, "자료.7z (1).002")));
        Assert.Contains("자료.7z.002 → 자료.7z (1).002", o.Notice);
        Assert.Empty(ui.CollisionPrompts);
    }

    // ───────────── 자기 자신 덮어쓰기 막기(I5-B) × 여러 개 풀기 ─────────────

    /// <summary>
    /// 같이 푸는 두 압축: <paramref name="d"/>\가.zip = { 나.zip(안쪽 — 다른 내용: 안쪽.txt), 설명.txt }, <paramref name="d"/>\나.zip = { 진짜.txt }.
    /// </summary>
    private static async Task<(string A, string B, byte[] InnerB)> SiblingArchives(TempDir tmp, string d)
    {
        var zip = new ZipArchiveService();
        string work = tmp.Dir("재료_" + Guid.NewGuid().ToString("N")[..6]);
        string innerTxt = Path.Combine(work, "안쪽.txt");
        File.WriteAllText(innerTxt, "가.zip 안에 든 다른 나.zip 의 내용");
        string innerDir = Path.Combine(work, "안쪽");
        Directory.CreateDirectory(innerDir);
        string innerB = Path.Combine(innerDir, "나.zip");
        await zip.CompressAsync(new[] { innerTxt }, innerB);
        string note = Path.Combine(work, "설명.txt");
        File.WriteAllText(note, "가 설명");
        string realTxt = Path.Combine(work, "진짜.txt");
        File.WriteAllText(realTxt, "진짜 나.zip 의 내용");
        Directory.CreateDirectory(d);
        string a = Path.Combine(d, "가.zip");
        string b = Path.Combine(d, "나.zip");
        await zip.CompressAsync(new[] { innerB, note }, a);
        await zip.CompressAsync(new[] { realTxt }, b);
        return (a, b, File.ReadAllBytes(innerB));
    }

    /// <summary>
    /// 계약: 여러 개 '여기에 풀기'에서 한 압축(가.zip) 안에 같이 푸는 다른 압축(나.zip)과 같은 이름 항목이 있으면, 덮어쓰기 고정이어도 그 압축을 덮지 않고
    /// '나 (1).zip' 로 비켜 풀고 알린다 — 아직 안 푼 나.zip 은 원래 내용 그대로 풀리고(진짜.txt), 이미 푼 나.zip 도 덮지 않는다(순서 둘 다).
    /// 예전(I5-B)에는 풀고 있는 압축 하나만 지켜 나.zip 이 안쪽 것으로 바뀐 뒤 그 내용이 풀렸다.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task 여러_개_풀기에서_같이_푸는_다른_압축도_덮지_않는다(bool siblingFirst)
    {
        using var tmp = new TempDir("w5batch");
        string d = tmp.Dir("받은 첨부");
        var (a, b, innerB) = await SiblingArchives(tmp, d);
        string aSha = TreeAssert.Sha256(a), bSha = TreeAssert.Sha256(b);
        var ui = new FakeUserInteraction();
        var order = siblingFirst ? new[] { b, a } : new[] { a, b };

        var r = await new BatchExtractor(new ArchiveServiceLocator(), ui, new FixedResolver(CollisionChoice.Overwrite), ExtractTargetRule.Here, order)
            .RunAsync(ui.ShowProgress("x"));

        Assert.All(r.Outcomes, o => Assert.True(o.Succeeded, $"{o.Name}: {o.Reason}"));
        Assert.Equal(aSha, TreeAssert.Sha256(a));
        Assert.Equal(bSha, TreeAssert.Sha256(b));
        Assert.Equal(TreeAssert.Sha256(innerB), TreeAssert.Sha256(Path.Combine(d, "나 (1).zip")));
        Assert.Equal("진짜 나.zip 의 내용", File.ReadAllText(Path.Combine(d, "진짜.txt")));
        Assert.Equal("가 설명", File.ReadAllText(Path.Combine(d, "설명.txt")));
        Assert.False(File.Exists(Path.Combine(d, "안쪽.txt")), "안쪽 나.zip 의 내용이 풀림(같이 푸는 나.zip 을 덮은 뒤 풀었음)");
        var oa = r.Outcomes.Single(o => o.ArchivePath == a);
        Assert.Contains("나.zip → 나 (1).zip", oa.Notice);
        Assert.Null(r.Outcomes.Single(o => o.ArchivePath == b).Notice);
        CollisionTests.NoLeftovers(d, "여러 개");
    }

    /// <summary>
    /// 계약(우클릭 흐름 전체): 설정 '같은 이름이면 덮어쓰기' + '다 푼 뒤 휴지통으로'로 두 압축을 '여기에 풀기' — 휴지통으로 가는 것은 두 원래 압축(보내는 순간에도 원래 바이트),
    /// 요약 창 알림에 '‘가.zip’: … 나.zip → 나 (1).zip', 오류 창·관리자 권유 없음.
    /// </summary>
    [Fact]
    public async Task 우클릭으로_두_압축을_풀고_휴지통으로_보내도_원래_압축이_간다()
    {
        using var tmp = new TempDir("w5batchvm");
        string d = tmp.Dir("받은 첨부");
        var (a, b, innerB) = await SiblingArchives(tmp, d);
        var original = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase) { [a] = File.ReadAllBytes(a), [b] = File.ReadAllBytes(b) };
        var svc = TestServices.With(e => { e.OnCollision = CollisionPolicy.Overwrite; e.DeleteArchiveAfter = true; });
        svc.RecycleBin.OnSend = paths => Assert.All(paths, p => Assert.Equal(original[p], File.ReadAllBytes(p)));
        var ui = new FakeUserInteraction();

        await Run(() => new MainViewModel(ui, null, svc.Services).ProcessShellCommandAsync(new[] { "--extract-here", a, b }), "여기에 풀기 둘");

        Assert.Empty(ui.Errors);
        Assert.DoesNotContain(ui.ChoicePrompts, c => c.Choices.Contains(ElevationService.RetryLabel));
        Assert.Equal(new[] { a, b }.OrderBy(p => p, StringComparer.OrdinalIgnoreCase), svc.RecycleBin.AllSent.OrderBy(p => p, StringComparer.OrdinalIgnoreCase));
        Assert.Equal(TreeAssert.Sha256(innerB), TreeAssert.Sha256(Path.Combine(d, "나 (1).zip")));
        Assert.Equal("진짜 나.zip 의 내용", File.ReadAllText(Path.Combine(d, "진짜.txt")));
        var summary = Assert.Single(ui.Summaries);
        Assert.Equal(2, summary.Succeeded);
        Assert.Contains(summary.Notes, n => n.StartsWith("‘가.zip’: 풀고 있는 압축 파일과 이름이 같은 파일 1개는", StringComparison.Ordinal) && n.Contains("나.zip → 나 (1).zip"));
    }

    // ───────────── 자기 자신 덮어쓰기 막기(I5-B) × 압축된 TAR(임시 폴더에서 옮기는 길) ─────────────

    private static byte[] Gzip(byte[] data)
    {
        using var ms = new MemoryStream();
        using (var gz = new GZipStream(ms, CompressionLevel.Optimal, leaveOpen: true)) gz.Write(data);
        return ms.ToArray();
    }

    private static byte[] Bzip2(byte[] data)
    {
        using var ms = new MemoryStream();
        using (var bz = new ICSharpCode.SharpZipLib.BZip2.BZip2OutputStream(ms) { IsStreamOwner = false }) bz.Write(data);
        return ms.ToArray();
    }

    /// <summary>바깥 '첨부.tgz'·'첨부.tbz2' = 압축(tar{ 첨부.ext(안쪽 바이트), 설명.txt }). 돌려주는 것: 바깥 경로, 안쪽 바이트.</summary>
    private static (string Outer, byte[] Inner) DoubleTar(string dir, string ext)
    {
        byte[] innerTar = ExtractSafetyNoticeTests.GnuTar(("본문.txt", Encoding.UTF8.GetBytes("안쪽 tar 본문"), '0', ""));
        byte[] inner = ext == ".tgz" ? Gzip(innerTar) : Bzip2(innerTar);
        byte[] outerTar = ExtractSafetyNoticeTests.GnuTar(("첨부" + ext, inner, '0', ""), ("설명.txt", Encoding.UTF8.GetBytes("바깥 설명"), '0', ""));
        Directory.CreateDirectory(dir);
        string outer = Path.Combine(dir, "첨부" + ext);
        File.WriteAllBytes(outer, ext == ".tgz" ? Gzip(outerTar) : Bzip2(outerTar));
        return (outer, inner);
    }

    /// <summary>
    /// 계약: 압축된 TAR(tgz·tbz2 — 대상 폴더 안 임시 폴더에 다 푼 뒤 옮기는 길)도 풀고 있는 압축 자신을 덮지 않는다 — 덮어쓰기 고정이어도 '첨부 (1).tgz' 로 비켜 옮기고 알린다.
    /// </summary>
    [Theory]
    [InlineData(".tgz")]
    [InlineData(".tbz2")]
    public async Task 압축된_TAR_도_풀고_있는_압축_자신을_덮지_않는다(string ext)
    {
        using var tmp = new TempDir("w5tarself");
        string d = tmp.Dir("받은 첨부");
        var (outer, inner) = DoubleTar(d, ext);
        string outerSha = TreeAssert.Sha256(outer);
        var ui = new FakeUserInteraction();

        var r = await new BatchExtractor(new ArchiveServiceLocator(), ui, new FixedResolver(CollisionChoice.Overwrite), ExtractTargetRule.Here, new[] { outer })
            .RunAsync(ui.ShowProgress("x"));

        var o = Assert.Single(r.Outcomes);
        Assert.True(o.Succeeded, o.Reason);
        Assert.Equal(outerSha, TreeAssert.Sha256(outer));
        Assert.Equal(TreeAssert.Sha256(inner), TreeAssert.Sha256(Path.Combine(d, "첨부 (1)" + ext)));
        Assert.Equal("바깥 설명", File.ReadAllText(Path.Combine(d, "설명.txt")));
        Assert.Contains($"첨부{ext} → 첨부 (1){ext}", o.Notice);
        CollisionTests.NoLeftovers(d, ext);
    }

    // ───────────── 자기 자신 덮어쓰기 막기(I5-B) × 창 없이 풀기(실제 exe) ─────────────

    /// <summary>
    /// 계약(실제 exe — 재사용장치 약속): 창 없이 풀기를 압축이 있는 폴더로 해도(재사용장치는 늘 빈 임시 폴더라 평소엔 해당 없음) 압축 자신을 덮지 않는다 —
    /// 같은 이름 범위가 없어 예전 규칙 '이름 (1)' 로 비켜 쓰고 종료 코드 0·표준 오류 없음(알림 줄도 없음 — 창 없이 풀기는 링크만 알림).
    /// ZIP(직접 쓰기)·7Z(7z.dll 임시 폴더)·TGZ(압축된 TAR 임시 폴더) 세 길.
    /// </summary>
    [IljipExeFact]
    public async Task 창_없이_풀기는_압축이_있는_폴더에_풀어도_압축_자신을_덮지_않는다()
    {
        using var tmp = new TempDir("w5hlself");
        foreach (var fmt in new[] { "zip", "7z", "tgz" })
        {
            string d = tmp.Dir("받은 첨부 " + fmt);
            string outer;
            byte[] inner;
            if (fmt == "tgz") (outer, inner) = DoubleTar(d, ".tgz");
            else (outer, inner) = await DoubleArchive(tmp, fmt, d);
            string outerSha = TreeAssert.Sha256(outer);

            var (code, err) = await HeadlessNoticeTests.RunIljip(new[] { "--extract-to", outer, d });

            Assert.True(code == 0, $"{fmt}: 종료 코드 {code} — {err}");
            Assert.True(string.IsNullOrWhiteSpace(err), $"{fmt}: 표준 오류 — {err}");
            Assert.Equal(outerSha, TreeAssert.Sha256(outer));
            Assert.Equal(TreeAssert.Sha256(inner), TreeAssert.Sha256(Path.Combine(d, $"첨부 (1).{fmt}")));
            Assert.True(File.Exists(Path.Combine(d, "설명.txt")), fmt + ": 설명.txt 가 없음");
            CollisionTests.NoLeftovers(d, fmt);
        }
    }

    // ───────────── tar 링크 알림(I5-B) × 검사 요약·bzip2·xz ─────────────

    /// <summary>감사 재현과 같은 링크 tar(a.txt 12바이트 + b.txt → a.txt 하드 링크)를 tbz2(bzip2)·txz(xz — 7z.dll)로 감싼다.</summary>
    private static string MakeLinkTar(string dir, string ext)
    {
        string plain = ExtractSafetyNoticeTests.MakeHardLinkTar(Path.Combine(dir, "원본_" + Guid.NewGuid().ToString("N")[..6]));
        string path = Path.Combine(dir, "링크묶음" + ext);
        switch (ext)
        {
            case ".tar":
                File.Copy(plain, path);
                break;
            case ".tbz2":
                File.WriteAllBytes(path, Bzip2(File.ReadAllBytes(plain)));
                break;
            case ".txz":
                SevenZipTestArchives.Xz(plain, path);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(ext));
        }
        return path;
    }

    /// <summary>
    /// 계약: bzip2·xz 로 감싼 tar(압축된 TAR — 임시 폴더에 다 푼 뒤 옮기는 길·xz 는 7z.dll)의 하드 링크도 풀기 알림('링크 1개는 내용 없이(0바이트) 만들었어요')과
    /// 검사('압축 파일은 멀쩡해요(파일 2개 확인). 다만 알아 둘 것이 있어요.' + 링크 줄)에 나온다 — tar·tgz 와 같다.
    /// </summary>
    [Theory]
    [InlineData(".tbz2")]
    [InlineData(".txz")]
    public async Task bzip2_xz로_감싼_tar의_링크도_풀기와_검사에_알린다(string ext)
    {
        using var tmp = new TempDir("w5tarlink");
        string arc = MakeLinkTar(tmp.Dir("in"), ext);
        string dest = tmp.Dir("out");

        using (var notes = ExtractionNotes.Begin())
        {
            await Svc.For(arc).ExtractAsync(arc, dest);
            Assert.Equal(new[] { new LinkNote("b.txt", "a.txt") }, notes.Links);
            Assert.Equal("링크 1개는 내용 없이(0바이트) 만들었어요(b.txt → a.txt). 일집은 링크를 되살리지 않아요.", notes.HeadlessLine());
        }
        Assert.Equal(12, new FileInfo(Path.Combine(dest, "a.txt")).Length);
        Assert.Equal(0, new FileInfo(Path.Combine(dest, "b.txt")).Length);
        CollisionTests.NoLeftovers(dest, ext);

        var report = await ArchiveTestService.Instance.TestAsync(arc, null, null, CancellationToken.None);
        Assert.True(report.Ok, report.ToDisplayText());
        Assert.Equal("압축 파일은 멀쩡해요(파일 2개 확인). 다만 알아 둘 것이 있어요.", report.Headline);
        Assert.Contains("- b.txt (원래 파일: a.txt)", report.ToDisplayText());
    }

    /// <summary>
    /// 계약(검사 요약): '--test' 로 여러 압축을 차례로 검사하면 링크가 든 tar·tbz2 는 '압축 파일은 멀쩡해요 … 다만 알아 둘 것이 있어요'(주의 창 — 이름과 링크 줄),
    /// 링크 없는 ZIP 은 그대로 '문제없어요'(안내 창). 문제 창·오류 창 없음, 마지막 상태 줄은 마지막 압축 것.
    /// </summary>
    [Fact]
    public async Task 여러_개_검사에서_링크_든_것만_알림_창이고_나머지는_문제없어요()
    {
        using var tmp = new TempDir("w5testsum");
        string work = tmp.Dir("받은 첨부");
        string tar = MakeLinkTar(work, ".tar");
        string tbz = MakeLinkTar(work, ".tbz2");
        string zip = Path.Combine(work, "보통.zip");
        File.Copy(FixtureCatalog.Get("zip_default").FullPath, zip);
        var ui = new FakeUserInteraction();
        var vm = new MainViewModel(ui, null, new TestServices().Services);

        await Run(() => vm.ProcessShellCommandAsync(new[] { "--test", tar, tbz, zip }), "여러 개 검사");

        Assert.Empty(ui.Errors);
        Assert.Empty(ui.Problems);
        Assert.Equal(2, ui.Warnings.Count);
        Assert.StartsWith("‘링크묶음.tar’ 시험 결과\n\n압축 파일은 멀쩡해요(파일 2개 확인). 다만 알아 둘 것이 있어요.", ui.Warnings[0]);
        Assert.StartsWith("‘링크묶음.tbz2’ 시험 결과\n\n압축 파일은 멀쩡해요(파일 2개 확인). 다만 알아 둘 것이 있어요.", ui.Warnings[1]);
        Assert.All(ui.Warnings, w => Assert.Contains("- b.txt (원래 파일: a.txt)", w));
        var info = Assert.Single(ui.Infos);
        Assert.StartsWith("‘보통.zip’ 시험 결과\n\n문제없어요.", info);
        Assert.Equal("시험 완료: 문제없음 (보통.zip)", vm.StatusText);
    }

    /// <summary>
    /// 계약(여러 개 풀기 요약): 이중 압축 첨부(자기 이름 비켜 풀기)와 링크 tar 를 같이 우클릭 '여기에 풀기'(설정 덮어쓰기 + 다 푼 뒤 휴지통) —
    /// 요약 창 하나에 압축마다 따로 알림(‘첨부.zip’: 이름을 바꿔 풀었어요 / ‘링크묶음.tar’: 링크 1개는 내용 없이…). 휴지통으로는 첨부.zip 만(링크 tar 는 압축 파일에만 온전한 것이 있어 남김).
    /// </summary>
    [Fact]
    public async Task 여러_개_풀기_요약에_자기_이름_비켜_풀기와_링크_알림이_압축마다_보인다()
    {
        using var tmp = new TempDir("w5sumnotes");
        string d = tmp.Dir("받은 첨부");
        var (outer, inner) = await DoubleArchive(tmp, "zip", d);
        string tar = MakeLinkTar(d, ".tar");
        var svc = TestServices.With(e => { e.OnCollision = CollisionPolicy.Overwrite; e.DeleteArchiveAfter = true; });
        var ui = new FakeUserInteraction();

        await Run(() => new MainViewModel(ui, null, svc.Services).ProcessShellCommandAsync(new[] { "--extract-here", outer, tar }), "여기에 풀기 둘");

        Assert.Empty(ui.Errors);
        var summary = Assert.Single(ui.Summaries);
        Assert.Equal(2, summary.Succeeded);
        Assert.Contains(summary.Notes, n => n.StartsWith("‘첨부.zip’: 풀고 있는 압축 파일과 이름이 같은 파일 1개는", StringComparison.Ordinal) && n.Contains("첨부.zip → 첨부 (1).zip"));
        Assert.Contains(summary.Notes, n => n.StartsWith("‘링크묶음.tar’: 링크 1개는 내용 없이(0바이트) 만들었어요", StringComparison.Ordinal));
        Assert.DoesNotContain(summary.Notes, n => n.StartsWith("‘첨부.zip’", StringComparison.Ordinal) && n.Contains("링크"));
        Assert.Equal(new[] { outer }, svc.RecycleBin.AllSent);
        Assert.Equal(TreeAssert.Sha256(inner), TreeAssert.Sha256(Path.Combine(d, "첨부 (1).zip")));
    }

    /// <summary>
    /// 계약(실제 exe — 재사용장치 약속): bzip2·xz 로 감싼 tar 의 링크도 창 없이 풀기에서 표준 오류 한 줄·종료 코드 1(재사용장치는 반디집으로 다시 풂) — tar·tgz 와 같다.
    /// </summary>
    [IljipExeFact]
    public async Task 창_없이_풀기는_bzip2_xz로_감싼_tar의_링크도_1로_끝난다()
    {
        using var tmp = new TempDir("w5hllink");
        foreach (var ext in new[] { ".tbz2", ".txz" })
        {
            string arc = MakeLinkTar(tmp.Dir("in" + ext.TrimStart('.')), ext);
            string dest = tmp.Dir("out" + ext.TrimStart('.'));
            var (code, err) = await HeadlessNoticeTests.RunIljip(new[] { "--extract-to", arc, dest });
            Assert.True(code == 1, $"{ext}: 종료 코드 {code}(1 이어야 함) — {err}");
            Assert.Equal("링크 1개는 내용 없이(0바이트) 만들었어요(b.txt → a.txt). 일집은 링크를 되살리지 않아요.", err.Trim());
            Assert.Equal(12, new FileInfo(Path.Combine(dest, "a.txt")).Length);
            Assert.Equal(0, new FileInfo(Path.Combine(dest, "b.txt")).Length);
            CollisionTests.NoLeftovers(dest, ext);
        }
    }
}
