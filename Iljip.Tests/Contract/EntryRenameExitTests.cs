using System.IO.Compression;
using System.Security.Cryptography;
using Iljip.Models;
using Iljip.Services;
using Iljip.Tests.TestSupport;
using Iljip.ViewModels;

namespace Iljip.Tests.Contract;

/// <summary>
/// [I5-A] 최종 감사 X3-F2(이름 바꾸기)·X3-F1(일집 먼저 닫기)·I3-3-close: 압축 속 파일을 열어(가짜 셸 — 실제 프로그램을 띄우지 않음) 고치는 동안
/// (A1) 편집기가 임시 폴더 안에서 그 파일 이름을 바꿔도(Light PDF 탭 '이름 바꾸기' — os.rename) 일집이 새 이름을 따라가 '다시 넣을까요?'를 묻고, [넣기]면 압축 안 <b>원래 항목</b>에 들어간다.
/// (A2) 연 파일을 다른 프로그램이 아직 잡고 있는 채 일집 창을 닫으면 묻는다(고친 것이 없어도 — 있으면 넣기/넣지 않기/취소).
/// (A3) 일집을 닫은 뒤 그 프로그램에서 저장한 파일을 다음 시작 청소가 말없이 지우지 않는다.
/// 편집기는 '파일을 잡고 있는 FileStream'(Light PDF·워드·엑셀처럼 연 동안 잡음 — 파이썬 open·MuPDF 와 같게 읽기·쓰기만 나누고 지우기는 막음)과
/// File.Move·File.WriteAllText(이름 바꾸기·저장)로 흉내 낸다.
/// 이 파일의 시험은 고치기 전 코드(f61b472)에도 있는 것만 쓴다 — 그 코드로 돌리면 실패한다(HANDOFF 변경 이력 'I5-A'). 알림 창의 단추 흐름은 EntryLeftoverTests.
/// </summary>
public class EntryRenameExitTests
{
    private static Task Run(Func<Task> action, string what) => VmRunner.Run(action, what);

    internal sealed class Ctx : IDisposable
    {
        public required string Archive { get; init; }
        public required MainViewModel Vm { get; init; }
        public required FakeUserInteraction Ui { get; init; }
        public required TestServices Services { get; init; }
        public string OpenedPath { get; set; } = string.Empty;
        public string Slot => Path.GetDirectoryName(OpenedPath)!;

        public void Dispose() => Vm.Shutdown();
    }

    /// <summary>받은 자료.zip = 자료/{보고서.txt, 표.csv}.</summary>
    internal static async Task<string> MakeZip(TempDir t)
    {
        string root = t.Dir("원본", "자료");
        File.WriteAllText(Path.Combine(root, "보고서.txt"), "처음 내용");
        File.WriteAllText(Path.Combine(root, "표.csv"), "가,나");
        string archive = t.P("받은 자료.zip");
        await new ZipArchiveService().CompressAsync(new[] { root }, archive);
        return archive;
    }

    /// <summary>압축을 열고 자료/보고서.txt 를 연다(가짜 셸). <paramref name="realWatcher"/> 면 진짜 FileSystemWatcher·2초 확인.</summary>
    internal static async Task<Ctx> OpenReport(TempDir t, bool realWatcher = false, string? archive = null)
    {
        archive ??= await MakeZip(t);
        var ts = new TestServices();
        var services = realWatcher ? WithRealWatcher(ts) : ts.Services;
        var ui = new FakeUserInteraction();
        var vm = new MainViewModel(ui, null, services) { EntryChangeQuietPeriod = TimeSpan.FromMilliseconds(30) };
        var c = new Ctx { Archive = archive, Vm = vm, Ui = ui, Services = ts };
        await Run(() => vm.OpenArchiveAsync(archive), "열기");
        var entry = vm.Entries.Single(e => TreeAssert.NormalizeEntryPath(e.Path) == "자료/보고서.txt");
        bool opened = false;
        await Run(async () => opened = await vm.OpenEntryAsync(entry), "안의 파일 열기");
        Assert.True(opened, string.Join(" | ", ui.Errors));
        c.OpenedPath = Assert.Single(ts.Shell.OpenedFiles);
        Assert.Equal("보고서.txt", Path.GetFileName(c.OpenedPath));
        return c;
    }

    private static AppServices WithRealWatcher(TestServices ts) => new()
    {
        Shell = ts.Shell,
        Settings = ts.Settings,
        RecycleBin = ts.RecycleBin,
        FileChanges = new FileChangeSource(),
        Clock = ts.Clock,
        CompressTester = ts.CompressTester,
        TempLocations = ts.TempLocations,
        Processes = ts.Processes,
        MappedDrives = ts.MappedDrives,
        Registry = ts.Registry,
        Clipboard = ts.Clipboard,
        ImageDecoder = ts.Images,
    };

    /// <summary>알림을 보내고(같은 폴더 — 진짜 감시처럼) 확인·질문이 끝날 때까지.</summary>
    private static async Task Notify(Ctx c, string path)
    {
        Assert.Equal(1, c.Services.FileChanges.Raise(path));
        await Run(() => c.Vm.WhenEntryChecksIdleAsync(), "바뀜 확인");
    }

    internal sealed record EntryFacts(string Name, uint Crc, long Compressed, long Size, string Sha);

    /// <summary>압축 안 파일마다 (이름, CRC, 압축 크기, 크기, 내용 SHA-256).</summary>
    internal static List<EntryFacts> Facts(string zip)
    {
        using var z = ZipFile.OpenRead(zip);
        return z.Entries.Where(e => !e.FullName.EndsWith('/')).Select(e =>
        {
            using var s = e.Open();
            using var ms = new MemoryStream();
            s.CopyTo(ms);
            return new EntryFacts(e.FullName, e.Crc32, e.CompressedLength, e.Length, Convert.ToHexString(SHA256.HashData(ms.ToArray())));
        }).OrderBy(f => f.Name, StringComparer.Ordinal).ToList();
    }

    internal static string ReadEntry(string zip, string name)
    {
        using var z = ZipFile.OpenRead(zip);
        using var r = new StreamReader(z.GetEntry(name)!.Open());
        return r.ReadToEnd();
    }

    /// <summary>압축의 다른 항목(자료/표.csv)은 바이트 그대로(CRC·압축 크기·내용), 항목 이름은 원래 둘뿐(새 이름 항목 없음).</summary>
    private static void OthersUntouched(List<EntryFacts> before, string zip)
    {
        var after = Facts(zip);
        Assert.Equal(new[] { "자료/보고서.txt", "자료/표.csv" }, after.Select(f => f.Name));
        Assert.Equal(before.Single(f => f.Name == "자료/표.csv"), after.Single(f => f.Name == "자료/표.csv"));
    }

    // ───────────────────────────── A1 이름 바꾸기(X3-F2) ─────────────────────────────

    /// <summary>
    /// A1: 열어 둔 파일 이름을 임시 폴더 안에서 바꾼 뒤 내용을 바꾸면 묻고, [넣기]면 압축 안 원래 항목(자료/보고서.txt)이 고친 내용 — 다른 항목·이름은 그대로.
    /// 이름만 바꾼 것(내용 같음)은 묻지 않는다. 고치기 전 코드: 원래 경로에 파일이 없어 '지워짐'으로 보고 묻지 않았다(감사 s4: prompts_after_rename=[]).
    /// </summary>
    [Fact]
    public async Task 이름을_바꾼_뒤_고치면_묻고_원래_항목에_넣는다()
    {
        using var tmp = new TempDir("i5ren");
        using var _ = TestServices.UseAppRoot(tmp);
        using var c = await OpenReport(tmp);
        var before = Facts(c.Archive);
        string renamed = Path.Combine(c.Slot, "새 이름.txt");

        File.Move(c.OpenedPath, renamed);
        await Notify(c, renamed);
        Assert.Empty(c.Ui.ChoicePrompts);   // 이름만 바꿈 — 잃을 것 없음

        c.Ui.ChooseByLabel = "넣기";
        File.WriteAllText(renamed, "이름 바꾼 뒤 고친 내용");
        await Notify(c, renamed);

        var prompt = Assert.Single(c.Ui.ChoicePrompts);
        Assert.Equal("‘새 이름.txt’(압축 안 이름 ‘보고서.txt’)가 바뀌었어요. 압축 파일에 다시 넣을까요? 압축 파일에는 원래 이름(‘보고서.txt’) 그대로 넣어요.", prompt.Message);
        Assert.Equal(new[] { "넣기", "나중에", "넣지 않기" }, prompt.Choices);
        Assert.Empty(c.Ui.Errors);
        Assert.Equal("이름 바꾼 뒤 고친 내용", ReadEntry(c.Archive, "자료/보고서.txt"));
        OthersUntouched(before, c.Archive);
        Assert.Equal("‘보고서.txt’을(를) 압축 파일에 다시 넣었어요", c.Vm.StatusText);

        // 넣은 뒤엔 닫을 때 묻지 않는다(파일을 잡은 프로그램도 없음)
        bool close = false;
        await Run(async () => close = await c.Vm.ConfirmExitAsync(), "닫기 확인");
        Assert.True(close);
        Assert.Single(c.Ui.ChoicePrompts);
    }

    /// <summary>
    /// A1(감사 s4 그대로): Light PDF 는 저장 안 한 변경이 있으면 [저장]한 뒤 곧바로(0.2초 안) 이름을 바꾼다 — 그 저장 내용을 묻고 넣는다.
    /// 그 뒤 새 이름으로 또 저장한 내용도 묻고 넣는다.
    /// </summary>
    [Fact]
    public async Task 저장하고_곧바로_이름을_바꿔도_그_저장과_그_뒤_저장을_넣는다()
    {
        using var tmp = new TempDir("i5ren2");
        using var _ = TestServices.UseAppRoot(tmp);
        using var c = await OpenReport(tmp);
        var before = Facts(c.Archive);
        c.Ui.ChooseByLabel = "넣기";
        string renamed = Path.Combine(c.Slot, "새 이름.txt");

        File.WriteAllText(c.OpenedPath, "이름 바꾸기 전에 저장한 내용");
        File.Move(c.OpenedPath, renamed);   // 알림이 조용해지기 전에 이름까지 바뀜
        await Notify(c, renamed);
        Assert.Single(c.Ui.ChoicePrompts);
        Assert.Equal("이름 바꾸기 전에 저장한 내용", ReadEntry(c.Archive, "자료/보고서.txt"));

        File.WriteAllText(renamed, "이름 바꾼 뒤 또 저장한 내용");
        await Notify(c, renamed);
        Assert.Equal(2, c.Ui.ChoicePrompts.Count);
        Assert.Equal("이름 바꾼 뒤 또 저장한 내용", ReadEntry(c.Archive, "자료/보고서.txt"));
        OthersUntouched(before, c.Archive);
        Assert.Empty(c.Ui.Errors);
    }

    /// <summary>
    /// A1: 이름을 바꾸고 고친 뒤 [나중에] → 일집을 닫을 때 그 파일(새 이름)을 묻고, [넣기]면 원래 항목에 넣는다(고치기 전: 닫을 때도 묻지 않고 지웠다 — 감사 s4 exit_prompts=[]).
    /// </summary>
    [Fact]
    public async Task 이름을_바꾸고_고친_파일은_닫을_때도_묻는다()
    {
        using var tmp = new TempDir("i5ren3");
        using var _ = TestServices.UseAppRoot(tmp);
        using var c = await OpenReport(tmp);
        var before = Facts(c.Archive);
        string renamed = Path.Combine(c.Slot, "새 이름.txt");
        c.Ui.ChooseByLabel = "나중에";
        File.Move(c.OpenedPath, renamed);
        File.WriteAllText(renamed, "닫기 전 고침");
        await Notify(c, renamed);
        Assert.Single(c.Ui.ChoicePrompts);
        Assert.Equal("처음 내용", ReadEntry(c.Archive, "자료/보고서.txt"));

        c.Ui.ChooseByLabel = "넣기";
        bool close = false;
        await Run(async () => close = await c.Vm.ConfirmExitAsync(), "닫기 확인");
        Assert.True(close);
        Assert.Equal(2, c.Ui.ChoicePrompts.Count);
        Assert.StartsWith("‘새 이름.txt’(압축 안 이름 ‘보고서.txt’)가 바뀌었는데 압축 파일에 아직 넣지 않았어요. 일집을 닫으면 바뀐 내용이 없어져요.", c.Ui.ChoicePrompts[1].Message);
        Assert.Equal(new[] { "넣기", "넣지 않기(닫기)", "취소" }, c.Ui.ChoicePrompts[1].Choices);
        Assert.Equal("닫기 전 고침", ReadEntry(c.Archive, "자료/보고서.txt"));
        OthersUntouched(before, c.Archive);
    }

    /// <summary>
    /// A1(진짜 감시 — FileSystemWatcher·2초 확인): 이름을 바꾸고 새 이름으로 저장하면 알림이 와 묻고 넣는다. 예전 감시는 그 파일 이름의 알림만 받아 새 이름의 저장을 놓쳤다.
    /// </summary>
    [Fact]
    public async Task 진짜_감시로도_이름을_바꾼_파일의_저장을_알아챈다()
    {
        using var tmp = new TempDir("i5renfsw");
        using var _ = TestServices.UseAppRoot(tmp);
        using var c = await OpenReport(tmp, realWatcher: true);
        c.Ui.ChooseByLabel = "넣기";
        string renamed = Path.Combine(c.Slot, "새 이름.txt");
        File.Move(c.OpenedPath, renamed);
        await Task.Delay(300);
        File.WriteAllText(renamed, "진짜 감시 — 이름 바꾼 뒤 고침");

        var until = DateTime.UtcNow.AddSeconds(30);
        while (c.Ui.ChoicePrompts.Count == 0 && DateTime.UtcNow < until) await Task.Delay(100);
        await Run(() => c.Vm.WhenEntryChecksIdleAsync(), "바뀜 확인");
        Assert.Contains(c.Ui.ChoicePrompts, p => p.Message.StartsWith("‘새 이름.txt’(압축 안 이름 ‘보고서.txt’)가 바뀌었어요.", StringComparison.Ordinal));
        Assert.Equal("진짜 감시 — 이름 바꾼 뒤 고침", ReadEntry(c.Archive, "자료/보고서.txt"));
    }

    // ───────────────────────────── A2 일집 먼저 닫기(X3-F1·I3-3-close) ─────────────────────────────

    /// <summary>
    /// A2(고친 것 없음): 연 파일을 다른 프로그램이 아직 잡고 있으면 닫을 때 알린다 — [취소]면 닫지 않고, [닫기]면 닫는다.
    /// 고치기 전 코드: 묻지 않고 닫았다(감사 s6a: exit_prompts=[]). 잡은 프로그램이 없으면 예전처럼 묻지 않는다(파일을 잡지 않는 메모장 등은 알 수 없음 — A3 이 받침).
    /// </summary>
    [Fact]
    public async Task 연_파일을_다른_프로그램이_잡고_있으면_닫을_때_묻는다()
    {
        using var tmp = new TempDir("i5exit");
        using var _ = TestServices.UseAppRoot(tmp);
        using var c = await OpenReport(tmp);
        bool close = false;
        await Run(async () => close = await c.Vm.ConfirmExitAsync(), "닫기 확인(잡은 것 없음)");
        Assert.True(close);
        Assert.Empty(c.Ui.ChoicePrompts);

        using (new FileStream(c.OpenedPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))   // 편집기가 연 채
        {
            c.Ui.ChooseByLabel = "취소";
            await Run(async () => close = await c.Vm.ConfirmExitAsync(), "닫기 확인(취소)");
            Assert.False(close);
            var ask = Assert.Single(c.Ui.ChoicePrompts);
            Assert.StartsWith("압축 속에서 연 파일 1개가 아직 다른 프로그램에서 열려 있어요.\n- ‘보고서.txt’ — 받은 자료.zip", ask.Message);
            Assert.Contains("일집을 닫은 뒤 그 프로그램에서 저장한 내용은 압축 파일에 바로 들어가지 않아요", ask.Message);
            Assert.Contains("다음에 일집을 켤 때 압축 파일에 넣을지 물어볼게요", ask.Message);
            Assert.Equal(new[] { "닫기", "취소" }, ask.Choices);
            Assert.Equal(0, ask.DefaultIndex);

            c.Ui.ChooseByLabel = null;   // 창 닫기(X) = 닫지 않음
            await Run(async () => close = await c.Vm.ConfirmExitAsync(), "닫기 확인(창 닫기)");
            Assert.False(close);

            c.Ui.ChooseByLabel = "닫기";
            await Run(async () => close = await c.Vm.ConfirmExitAsync(), "닫기 확인(닫기)");
            Assert.True(close);
            Assert.Equal(3, c.Ui.ChoicePrompts.Count);
        }
        Assert.Equal("처음 내용", ReadEntry(c.Archive, "자료/보고서.txt"));
    }

    /// <summary>
    /// A2(고친 것 있음 + 아직 열려 있음): 한 번만 묻는다 — 고친 파일 질문에 '아직 열려 있어요' 말을 덧붙임. [넣기]면 넣고 닫음, [넣지 않기(닫기)]면 압축 그대로 닫음, [취소]면 닫지 않음.
    /// </summary>
    [Theory]
    [InlineData("넣기")]
    [InlineData("넣지 않기")]
    [InlineData("취소")]
    public async Task 고친_파일이_아직_열려_있으면_넣기_넣지_않기_취소가_뜻대로(string answer)
    {
        using var tmp = new TempDir("i5exit2");
        using var _ = TestServices.UseAppRoot(tmp);
        using var c = await OpenReport(tmp);
        var before = Facts(c.Archive);
        c.Ui.ChooseByLabel = "나중에";
        File.WriteAllText(c.OpenedPath, "닫기 전 고친 내용");
        await Notify(c, c.OpenedPath);
        Assert.Single(c.Ui.ChoicePrompts);

        bool close;
        using (new FileStream(c.OpenedPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
        {
            c.Ui.ChooseByLabel = answer;
            close = !(answer == "취소");
            bool got = !close;
            await Run(async () => got = await c.Vm.ConfirmExitAsync(), "닫기 확인");
            Assert.Equal(close, got);
        }
        Assert.Equal(2, c.Ui.ChoicePrompts.Count);   // 고친 파일 질문 하나(따로 '열려 있어요' 창을 또 띄우지 않음)
        var ask = c.Ui.ChoicePrompts[1];
        Assert.StartsWith("‘보고서.txt’가 바뀌었는데 압축 파일에 아직 넣지 않았어요. 일집을 닫으면 바뀐 내용이 없어져요. 압축 파일에 넣을까요?", ask.Message);
        Assert.Contains("이 파일은 아직 다른 프로그램에서 열려 있어요.", ask.Message);
        Assert.Equal(new[] { "넣기", "넣지 않기(닫기)", "취소" }, ask.Choices);
        if (answer == "넣기")
        {
            Assert.Equal("닫기 전 고친 내용", ReadEntry(c.Archive, "자료/보고서.txt"));
            OthersUntouched(before, c.Archive);
        }
        else
        {
            Assert.Equal(before, Facts(c.Archive));   // 압축 그대로
        }
    }

    // ───────────────────────────── A3 닫은 뒤 저장한 파일(X3-F1·I3-3-close) ─────────────────────────────

    /// <summary>
    /// A3: 편집기가 연 채 일집을 닫고(질문에 [닫기]) → 편집기가 고쳐 저장 → 다음 시작 청소가 그 파일을 지우지 않는다(바뀌지 않은 다른 작업 폴더는 예전처럼 지움).
    /// 고치기 전 코드: 청소가 지웠다(감사 s6a: after_janitor_ws.exists=false·zip_has_edit=false).
    /// </summary>
    [Fact]
    public async Task 일집을_닫은_뒤_저장한_파일은_다음_시작_청소가_지우지_않는다()
    {
        using var tmp = new TempDir("i5after");
        using var _ = TestServices.UseAppRoot(tmp);
        var c = await OpenReport(tmp);
        using (new FileStream(c.OpenedPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
        {
            c.Ui.ChooseByLabel = "닫기";
            bool close = false;
            await Run(async () => close = await c.Vm.ConfirmExitAsync(), "닫기 확인");
            Assert.True(close);
            c.Vm.Shutdown();
            Assert.True(File.Exists(c.OpenedPath), "편집기가 잡은 파일이 남아야 함");
        }
        // 일집은 꺼짐 — 편집기가 고쳐 저장(탭을 닫으며 [저장])
        File.WriteAllText(c.OpenedPath, "일집을 닫은 뒤 저장한 내용");

        // 다음 시작 청소
        TempJanitor.CleanupStale(new FakeClock());
        Assert.True(File.Exists(c.OpenedPath), "닫은 뒤 저장한 고친 파일을 청소가 말없이 지움");
        Assert.Equal("일집을 닫은 뒤 저장한 내용", File.ReadAllText(c.OpenedPath));
        TempJanitor.CleanupStale(new FakeClock());   // 몇 번 켜도 그대로
        Assert.True(File.Exists(c.OpenedPath));
        Assert.Equal("처음 내용", ReadEntry(c.Archive, "자료/보고서.txt"));   // 압축에 넣었다고 하지 않음(넣기는 다음 시작 알림 — EntryLeftoverTests)
    }

    /// <summary>A3(이름까지 바꿈 — 감사 s4): 이름을 바꿔 저장한 파일도 일집을 닫을 때·다음 시작 청소에서 지우지 않는다.</summary>
    [Fact]
    public async Task 이름을_바꿔_저장하고_닫아도_지우지_않는다()
    {
        using var tmp = new TempDir("i5after2");
        using var _ = TestServices.UseAppRoot(tmp);
        var c = await OpenReport(tmp);
        string renamed = Path.Combine(c.Slot, "새 이름.txt");
        File.Move(c.OpenedPath, renamed);
        using (new FileStream(renamed, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
        {
            c.Ui.ChooseByLabel = "닫기";
            bool close = false;
            await Run(async () => close = await c.Vm.ConfirmExitAsync(), "닫기 확인");
            Assert.True(close);
            Assert.Contains("‘새 이름.txt’(압축 안 이름 ‘보고서.txt’)", Assert.Single(c.Ui.ChoicePrompts).Message);
            c.Vm.Shutdown();
        }
        File.WriteAllText(renamed, "새 이름으로 닫은 뒤 저장");
        TempJanitor.CleanupStale(new FakeClock());
        Assert.True(File.Exists(renamed), "이름을 바꿔 저장한 파일을 청소가 지움");
    }

    /// <summary>
    /// A3(닫기 질문을 거치지 않고 끝남 — 로그오프 등): 고쳐 놓고 넣지도 넘기지도 않은 파일은 일집을 닫을 때(Shutdown) 지우지 않고, 다음 시작 청소도 지우지 않는다.
    /// 고치기 전 코드: Shutdown 이 지웠다.
    /// </summary>
    [Fact]
    public async Task 묻지_않고_끝나도_넣지_않은_고친_파일은_지우지_않는다()
    {
        using var tmp = new TempDir("i5after3");
        using var _ = TestServices.UseAppRoot(tmp);
        var c = await OpenReport(tmp);
        c.Ui.ChooseByLabel = "나중에";
        File.WriteAllText(c.OpenedPath, "넣지 않은 고침");
        await Notify(c, c.OpenedPath);
        c.Vm.Shutdown();
        Assert.True(File.Exists(c.OpenedPath), "넣지 않은 고친 파일을 닫으면서 지움");
        TempJanitor.CleanupStale(new FakeClock());
        Assert.True(File.Exists(c.OpenedPath), "넣지 않은 고친 파일을 다음 시작 청소가 지움");
    }

    /// <summary>
    /// 지킨 것: 닫을 때 [넣지 않기(닫기)]로 넘긴 내용·이미 넣은 내용·안 고친 파일은 예전처럼 지운다(다음 시작에 알릴 것 없음).
    /// </summary>
    [Fact]
    public async Task 넘기거나_넣었거나_안_고친_파일은_예전처럼_지운다()
    {
        using var tmp = new TempDir("i5after4");
        using var _ = TestServices.UseAppRoot(tmp);
        var c = await OpenReport(tmp);
        string ws = Path.GetDirectoryName(c.Slot)!;
        c.Ui.ChooseByLabel = "나중에";
        File.WriteAllText(c.OpenedPath, "넘길 고침");
        await Notify(c, c.OpenedPath);
        c.Ui.ChooseByLabel = "넣지 않기";
        bool close = false;
        await Run(async () => close = await c.Vm.ConfirmExitAsync(), "닫기 확인");
        Assert.True(close);
        c.Vm.Shutdown();
        Assert.False(Directory.Exists(ws), "넘긴 파일의 작업 폴더가 남음");
        Assert.Equal("처음 내용", ReadEntry(c.Archive, "자료/보고서.txt"));
    }
}
