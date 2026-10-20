using System.Reflection;
using Iljip.Services;
using Iljip.Tests.TestSupport;
using Iljip.ViewModels;
using static Iljip.Tests.Contract.EntryRenameExitTests;

namespace Iljip.Tests.Contract;

/// <summary>
/// [I5-A] 일집을 닫은 뒤(또는 갑자기 꺼진 뒤) 다른 프로그램에서 고쳐 저장한, 압축 파일에 아직 넣지 않은 파일 — 다음 시작 때 청소(<see cref="TempJanitor"/>)가 지우지 않고 찾고,
/// 화면(<see cref="MainViewModel.OfferLeftoverChangesAsync"/>)이 ‘압축 파일에 아직 넣지 않은 고친 파일이 있어요’ [압축에 넣기][폴더 열기][버리기][나중에]를 묻는다(가짜 창·가짜 시계·가짜 셸·가짜 휴지통).
/// 바탕: 작업 폴더의 '연 파일 목록'(owner.lock 안), 일집을 닫을 때 잡힌 파일의 순번 폴더 남기기, 편집기가 바꾼 이름 따라가기(<see cref="OpenedEntry.FollowRename"/>), 폴더 전체 감시(<see cref="FileChangeSource"/>).
/// </summary>
public class EntryLeftoverTests
{
    private static Task Run(Func<Task> action, string what) => VmRunner.Run(action, what);

    /// <summary>편집기가 연 채 일집을 닫고([닫기]) 일집이 꺼진 뒤 편집기가 <paramref name="saved"/> 로 저장한 상태를 만든다. 연 파일 경로를 돌려준다.</summary>
    private static async Task<(string Archive, string Opened)> CloseThenSave(TempDir tmp, string saved, Func<string, string>? renameTo = null)
    {
        var c = await OpenReport(tmp);
        string path = c.OpenedPath;
        if (renameTo is not null)
        {
            string to = Path.Combine(c.Slot, renameTo(path));
            File.Move(path, to);
            path = to;
        }
        using (new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
        {
            c.Ui.ChooseByLabel = "닫기";
            bool close = false;
            await Run(async () => close = await c.Vm.ConfirmExitAsync(), "닫기 확인");
            Assert.True(close);
            c.Vm.Shutdown();
        }
        File.WriteAllText(path, saved);
        return (c.Archive, path);
    }

    private static List<LeftoverFile> NextStartJanitor(FakeClock? clock = null)
    {
        var found = new List<LeftoverFile>();
        TempJanitor.CleanupStale(clock ?? new FakeClock(), found);
        return found;
    }

    private static (MainViewModel Vm, FakeUserInteraction Ui, TestServices Ts) NextStartVm(string? choose)
    {
        var ts = new TestServices();
        var ui = new FakeUserInteraction { ChooseByLabel = choose };
        return (new MainViewModel(ui, null, ts.Services), ui, ts);
    }

    // ───────────────────────────── 다음 시작 알림(A3) ─────────────────────────────

    /// <summary>
    /// A3: 일집을 닫은 뒤 저장한 파일 → 다음 시작 청소가 지우지 않고 찾음 → ‘압축 파일에 아직 넣지 않은 고친 파일이 있어요’ → [압축에 넣기] → 압축 안 원래 항목이 그 내용(다른 항목 그대로),
    /// 작업 폴더는 지워짐. 다시 켜면 더 묻지 않는다.
    /// </summary>
    [Fact]
    public async Task 다음_시작에_알리고_압축에_넣기를_고르면_원래_자리에_넣는다()
    {
        using var tmp = new TempDir("i5lo");
        using var _ = TestServices.UseAppRoot(tmp);
        var (archive, opened) = await CloseThenSave(tmp, "닫은 뒤 저장한 내용");
        var before = Facts(archive);
        string ws = Path.GetDirectoryName(Path.GetDirectoryName(opened)!)!;

        var found = NextStartJanitor();
        var l = Assert.Single(found);
        Assert.Equal(opened, l.FilePath);
        Assert.Equal(archive, l.ArchivePath);
        Assert.Equal("자료/보고서.txt", l.EntryKey);
        Assert.True(File.Exists(opened));

        var (vm, ui, _) = NextStartVm("압축에 넣기");
        await Run(() => vm.OfferLeftoverChangesAsync(found), "다음 시작 알림");
        var ask = Assert.Single(ui.ChoicePrompts);
        Assert.Equal("압축 속에서 열어 고친 파일 중 압축 파일에 아직 넣지 않은 것이 있어요(일집을 닫은 뒤에 저장한 것).\n- ‘보고서.txt’ — 받은 자료.zip\n\n" +
                     "[압축에 넣기]를 누르면 원래 압축 파일의 원래 자리에 넣어요. 어떻게 할까요?", ask.Message);
        Assert.Equal(new[] { "압축에 넣기", "폴더 열기", "버리기", "나중에" }, ask.Choices);
        Assert.Equal(0, ask.DefaultIndex);
        Assert.Empty(ui.Errors);
        Assert.Equal("닫은 뒤 저장한 내용", ReadEntry(archive, "자료/보고서.txt"));
        var after = Facts(archive);
        Assert.Equal(before.Select(f => f.Name), after.Select(f => f.Name));
        Assert.Equal(before.Single(f => f.Name == "자료/표.csv"), after.Single(f => f.Name == "자료/표.csv"));
        Assert.Equal("고친 파일 1개를 압축 파일에 넣었어요", vm.StatusText);
        Assert.False(Directory.Exists(ws), "넣은 뒤 작업 폴더가 남음");
        Assert.Empty(NextStartJanitor());
        vm.Shutdown();
    }

    /// <summary>A3: 일집이 꺼진 동안 이름까지 바꿔 저장했어도(Light PDF 탭 '이름 바꾸기') 찾아 알리고, 넣을 때는 압축 안 원래 이름으로.</summary>
    [Fact]
    public async Task 꺼진_동안_이름을_바꿔_저장한_것도_찾아_원래_이름으로_넣는다()
    {
        using var tmp = new TempDir("i5lo2");
        using var _ = TestServices.UseAppRoot(tmp);
        var c = await OpenReport(tmp);
        string held = c.OpenedPath;
        using (new FileStream(held, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
        {
            c.Ui.ChooseByLabel = "닫기";
            await Run(() => c.Vm.ConfirmExitAsync(), "닫기 확인");
            c.Vm.Shutdown();
        }
        string renamed = Path.Combine(c.Slot, "새 이름.txt");
        File.Move(held, renamed);   // 일집이 꺼진 뒤 이름 바꾸기
        File.WriteAllText(renamed, "꺼진 동안 이름 바꿔 저장");

        var l = Assert.Single(NextStartJanitor());
        Assert.Equal(renamed, l.FilePath);
        Assert.True(l.IsRenamed);
        var (vm, ui, _) = NextStartVm("압축에 넣기");
        await Run(() => vm.OfferLeftoverChangesAsync(new[] { l }), "다음 시작 알림");
        Assert.Contains("- ‘새 이름.txt’(압축 안 이름 ‘보고서.txt’) — 받은 자료.zip", Assert.Single(ui.ChoicePrompts).Message);
        Assert.Equal("꺼진 동안 이름 바꿔 저장", ReadEntry(c.Archive, "자료/보고서.txt"));
        Assert.Equal(new[] { "자료/보고서.txt", "자료/표.csv" }, Facts(c.Archive).Select(f => f.Name));
        vm.Shutdown();
    }

    /// <summary>A3: [폴더 열기] — 그 파일을 고른 채 탐색기(가짜 셸), 파일은 그대로 두고 다음 시작 때 또 알린다.</summary>
    [Fact]
    public async Task 폴더_열기는_파일을_두고_다음에_다시_알린다()
    {
        using var tmp = new TempDir("i5lo3");
        using var _ = TestServices.UseAppRoot(tmp);
        var (archive, opened) = await CloseThenSave(tmp, "폴더 열기");
        var (vm, ui, ts) = NextStartVm("폴더 열기");
        await Run(() => vm.OfferLeftoverChangesAsync(NextStartJanitor()), "다음 시작 알림");
        Assert.Equal(new[] { (opened, true) }, ts.Shell.OpenedFolders);
        Assert.True(File.Exists(opened));
        Assert.Equal("처음 내용", ReadEntry(archive, "자료/보고서.txt"));
        Assert.Single(NextStartJanitor());   // 또 켜면 또 알림
        vm.Shutdown();
    }

    /// <summary>A3: [버리기] — 휴지통으로(가짜 휴지통에 보냄) 보내고 작업 폴더를 지운다. 휴지통이 실패하면 그대로 두고 알린다.</summary>
    [Fact]
    public async Task 버리기는_휴지통으로_보내고_실패하면_그대로_둔다()
    {
        using var tmp = new TempDir("i5lo4");
        using var _ = TestServices.UseAppRoot(tmp);
        var (archive, opened) = await CloseThenSave(tmp, "버릴 내용");
        string ws = Path.GetDirectoryName(Path.GetDirectoryName(opened)!)!;

        var (vm1, ui1, ts1) = NextStartVm("버리기");
        ts1.RecycleBin.Result = new RecycleResult(false, "휴지통이 꽉 찼어요(가짜).");
        await Run(() => vm1.OfferLeftoverChangesAsync(NextStartJanitor()), "버리기(실패)");
        Assert.Contains("휴지통으로 옮기지 못했어요. 파일은 그대로 있어요", Assert.Single(ui1.Warnings));
        Assert.True(File.Exists(opened));

        var (vm2, ui2, ts2) = NextStartVm("버리기");
        await Run(() => vm2.OfferLeftoverChangesAsync(NextStartJanitor()), "버리기");
        Assert.Equal(new[] { opened }, ts2.RecycleBin.AllSent);
        Assert.False(Directory.Exists(ws));
        Assert.Equal("처음 내용", ReadEntry(archive, "자료/보고서.txt"));
        Assert.Empty(NextStartJanitor());
        vm1.Shutdown();
        vm2.Shutdown();
    }

    /// <summary>A3: [나중에]·창 닫기 — 그대로 두고 다음 시작 때 또 알린다.</summary>
    [Theory]
    [InlineData("나중에")]
    [InlineData(null)]
    public async Task 나중에는_그대로_두고_다음에_다시_알린다(string? choose)
    {
        using var tmp = new TempDir("i5lo5");
        using var _ = TestServices.UseAppRoot(tmp);
        var (archive, opened) = await CloseThenSave(tmp, "나중에");
        var (vm, ui, _) = NextStartVm(choose);
        await Run(() => vm.OfferLeftoverChangesAsync(NextStartJanitor()), "다음 시작 알림");
        Assert.Single(ui.ChoicePrompts);
        Assert.True(File.Exists(opened));
        Assert.Equal("고친 파일은 그대로 두었어요 — 다음에 일집을 켤 때 다시 알려 드려요", vm.StatusText);
        Assert.Single(NextStartJanitor());
        Assert.Equal("처음 내용", ReadEntry(archive, "자료/보고서.txt"));
        vm.Shutdown();
    }

    /// <summary>A3: 그 압축 파일이 없어졌으면 넣지 못한다고 말하고 다른 곳에 저장하게 한다 — 저장하면 지우고, 안 하면 남긴다.</summary>
    [Fact]
    public async Task 압축_파일이_없어졌으면_다른_곳에_저장하게_한다()
    {
        using var tmp = new TempDir("i5lo6");
        using var _ = TestServices.UseAppRoot(tmp);
        var (archive, opened) = await CloseThenSave(tmp, "압축이 없어진 고침");
        File.Delete(archive);
        var found = NextStartJanitor();

        var (vm1, ui1, _) = NextStartVm(null);
        ui1.ChoiceAnswers.Enqueue(0);   // 압축에 넣기
        ui1.ChoiceAnswers.Enqueue(1);   // 나중에
        await Run(() => vm1.OfferLeftoverChangesAsync(found), "넣기(압축 없음) → 나중에");
        Assert.Contains("받은 자료.zip(압축 파일을 찾을 수 없어요)", ui1.ChoicePrompts[0].Message);
        Assert.Equal("‘보고서.txt’를 압축 파일에 넣을 수 없어요. 압축 파일 ‘받은 자료.zip’를 찾을 수 없어요. 고친 내용을 잃지 않게 다른 곳에 저장할까요?", ui1.ChoicePrompts[1].Message);
        Assert.Equal(new[] { "다른 곳에 저장", "나중에" }, ui1.ChoicePrompts[1].Choices);
        Assert.True(File.Exists(opened));

        var (vm2, ui2, _) = NextStartVm(null);
        ui2.ChoiceAnswers.Enqueue(0);
        ui2.ChoiceAnswers.Enqueue(0);   // 다른 곳에 저장
        ui2.SavePath = tmp.P("살린 보고서.txt");
        await Run(() => vm2.OfferLeftoverChangesAsync(NextStartJanitor()), "넣기(압축 없음) → 저장");
        Assert.Equal("압축이 없어진 고침", File.ReadAllText(tmp.P("살린 보고서.txt")));
        Assert.False(File.Exists(opened));
        Assert.Empty(NextStartJanitor());
        vm1.Shutdown();
        vm2.Shutdown();
    }

    // ───────────────────────────── 청소 규칙 ─────────────────────────────

    /// <summary>닫은 뒤 바뀌지 않았으면 알리지 않고 예전처럼 지운다. 아직 편집기에 열려 있으면 지우지도 알리지도 않고 다음에 다시 본다.</summary>
    [Fact]
    public async Task 안_바뀌었으면_지우고_아직_열려_있으면_다음에_본다()
    {
        using var tmp = new TempDir("i5lo7");
        using var _ = TestServices.UseAppRoot(tmp);
        var c = await OpenReport(tmp);
        string ws = Path.GetDirectoryName(c.Slot)!;
        var editor = new FileStream(c.OpenedPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        try
        {
            c.Ui.ChooseByLabel = "닫기";
            await Run(() => c.Vm.ConfirmExitAsync(), "닫기 확인");
            c.Vm.Shutdown();
            Assert.Empty(NextStartJanitor());   // 아직 열려 있음 — 이번엔 그대로
            Assert.True(File.Exists(c.OpenedPath));
        }
        finally
        {
            editor.Dispose();
        }
        // 편집기를 고치지 않고 닫음
        Assert.Empty(NextStartJanitor());
        Assert.False(Directory.Exists(ws), "바뀌지 않은 작업 폴더가 남음");
    }

    /// <summary>
    /// 일집을 닫을 때 편집기가 잡은 파일의 순번 폴더는 통째로 남긴다(Light PDF 의 숨김 복구본 .autosave 등 — 예전엔 잡힌 PDF 만 남고 지워짐: 감사 s3 autosave_after_exit=false).
    /// 잡히지 않은 다른 순번 폴더는 지운다.
    /// </summary>
    [Fact]
    public async Task 닫을_때_잡힌_파일의_순번_폴더는_통째로_남긴다()
    {
        using var tmp = new TempDir("i5lo8");
        using var _ = TestServices.UseAppRoot(tmp);
        var c = await OpenReport(tmp);
        var table = c.Vm.Entries.Single(e => TreeAssert.NormalizeEntryPath(e.Path) == "자료/표.csv");
        await Run(() => c.Vm.OpenEntryAsync(table), "두 번째 파일 열기");
        string other = c.Services.Shell.OpenedFiles[1];
        string autosave = c.OpenedPath + ".autosave";
        File.WriteAllText(autosave, "복구본");
        File.SetAttributes(autosave, FileAttributes.Hidden);
        using (new FileStream(c.OpenedPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
        {
            c.Ui.ChooseByLabel = "닫기";
            await Run(() => c.Vm.ConfirmExitAsync(), "닫기 확인");
            c.Vm.Shutdown();
        }
        Assert.True(File.Exists(c.OpenedPath));
        Assert.True(File.Exists(autosave), "편집기의 복구본을 지움");
        Assert.False(Directory.Exists(Path.GetDirectoryName(other)), "잡히지 않은 순번 폴더가 남음");
    }

    /// <summary>
    /// 남은 작업 폴더가 있는 압축을 다시 열어도 그 폴더를 '찌꺼기'로 지우지 않는다 — 옆 이름(key16-2) 폴더를 쓰고, 남은 고친 파일은 다음 시작 알림으로 그대로.
    /// </summary>
    [Fact]
    public async Task 남은_작업_폴더가_있는_압축을_다시_열어도_지우지_않는다()
    {
        using var tmp = new TempDir("i5lo9");
        using var _ = TestServices.UseAppRoot(tmp);
        var (archive, opened) = await CloseThenSave(tmp, "남은 고침");
        using var again = await OpenReport(tmp, archive: archive);
        Assert.NotEqual(Path.GetDirectoryName(Path.GetDirectoryName(opened)), Path.GetDirectoryName(again.Slot));
        Assert.EndsWith("-2", Path.GetDirectoryName(again.Slot));
        Assert.Equal("남은 고침", File.ReadAllText(opened));
        Assert.Equal("처음 내용", File.ReadAllText(again.OpenedPath));
        var l = Assert.Single(NextStartJanitor());
        Assert.Equal(opened, l.FilePath);
    }

    /// <summary>일집이 갑자기 꺼져도(닫기 정리 없음 — 잠금만 풀림) 연 파일 목록이 남아, 고쳐 놓고 넣지 않은 파일을 다음 시작 청소가 지우지 않고 알린다.</summary>
    [Fact]
    public void 갑자기_꺼져도_목록이_남아_고친_파일을_지우지_않는다()
    {
        using var tmp = new TempDir("i5lo10");
        using var _ = TestServices.UseAppRoot(tmp);
        string archive = tmp.P("받은 자료.zip");
        File.WriteAllText(archive, "가짜 압축 — 열쇠만 씀");
        var ws = ArchiveWorkspace.Open(archive);
        string slot = ws.NewSlot();
        string file = Path.Combine(slot, "보고서.txt");
        File.WriteAllText(file, "처음");
        var rec = ws.Record("자료/보고서.txt", file);
        File.WriteAllText(file, "고쳤는데 일집이 꺼짐");
        // 프로세스가 끝나 잠금만 풀린 것처럼(Dispose 의 정리 없이)
        ((FileStream)typeof(ArchiveWorkspace).GetField("_lock", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(ws)!).Dispose();

        var l = Assert.Single(NextStartJanitor());
        Assert.Equal(file, l.FilePath);
        Assert.Equal("자료/보고서.txt", l.EntryKey);
        Assert.Equal(Path.GetFullPath(archive), l.ArchivePath);
        Assert.True(File.Exists(file));
        Assert.True(rec.IsModified());
    }

    /// <summary>목록이 없는 작업 폴더(예전 판·끌기 폴더·잠금 파일이 빈 것)는 예전처럼 지운다 — 청소 기존 계약 그대로(EntryWorkspaceTests).</summary>
    [Fact]
    public void 목록이_없는_작업_폴더는_예전처럼_지운다()
    {
        using var tmp = new TempDir("i5lo11");
        using var _ = TestServices.UseAppRoot(tmp);
        string d = Path.Combine(AppPaths.OpenRoot, "aaaa");
        Directory.CreateDirectory(Path.Combine(d, "1"));
        File.WriteAllText(Path.Combine(d, "1", "고친 듯한 파일.txt"), "x");
        File.WriteAllText(Path.Combine(d, ArchiveWorkspace.LockFileName), "");
        Assert.Empty(NextStartJanitor());
        Assert.False(Directory.Exists(d));
    }

    // ───────────────────────────── 이름 따라가기·폴더 감시(바탕) ─────────────────────────────

    /// <summary>
    /// 이름 따라가기: 원래 이름이 없으면 같은 폴더의 같은 확장자 파일(편집기 임시·잠금·숨김 파일은 빼고)을 따라간다. 원래 이름이 다시 생기면 돌아간다.
    /// 오피스가 '임시 파일에 써서 바꿔 저장'하는 도중(원래 이름이 잠깐 없고 ~WRL·.tmp·~$ 만 있음)에는 따라가지 않는다.
    /// </summary>
    [Fact]
    public void 이름_따라가기는_편집기_임시_파일을_무시한다()
    {
        using var tmp = new TempDir("i5follow");
        string slot = tmp.Dir("1");
        string original = Path.Combine(slot, "보고서.docx");
        File.WriteAllText(original, "처음");
        var e = OpenedEntry.Capture("자료/보고서.docx", original, tmp.P("x.zip"));
        Assert.False(e.FollowRename());

        // 오피스 저장 도중: 원래 이름 → ~WRL0001.tmp, 소유자 파일 ~$보고서.docx(숨김), 엑셀식 확장자 없는 임시
        File.Move(original, Path.Combine(slot, "~WRL0001.tmp"));
        File.WriteAllText(Path.Combine(slot, "~$보고서.docx"), "owner");
        File.SetAttributes(Path.Combine(slot, "~$보고서.docx"), FileAttributes.Hidden);
        File.WriteAllText(Path.Combine(slot, "보고서.docx.tmp"), "쓰는 중");
        File.WriteAllText(Path.Combine(slot, "숨김.docx"), "숨김");
        File.SetAttributes(Path.Combine(slot, "숨김.docx"), FileAttributes.Hidden);
        File.WriteAllText(Path.Combine(slot, "다른 형식.pdf"), "pdf");
        Assert.False(e.FollowRename());
        Assert.Equal(original, e.TempPath);
        Assert.False(e.IsModified());   // 지금은 없음 — 잃을 것 없음

        // 진짜 이름 바꾸기
        File.Move(Path.Combine(slot, "~WRL0001.tmp"), Path.Combine(slot, "새 이름.docx"));
        Assert.True(e.FollowRename());
        Assert.Equal(Path.Combine(slot, "새 이름.docx"), e.TempPath);
        Assert.True(e.IsRenamed);
        Assert.Equal("새 이름.docx", e.Name);
        Assert.Equal("보고서.docx", e.ExtractedName);

        // 이름을 되돌림
        File.Move(Path.Combine(slot, "새 이름.docx"), original);
        Assert.True(e.FollowRename());
        Assert.Equal(original, e.TempPath);
        Assert.False(e.IsRenamed);
    }

    /// <summary>진짜 폴더 감시: 그 파일 이름을 바꾸면 알리고, 새 이름으로 저장해도 알린다(예전엔 그 파일 이름의 알림만 받아 놓쳤다).</summary>
    [Fact]
    public async Task 폴더_감시는_이름_바꾸기와_새_이름의_저장을_알린다()
    {
        using var tmp = new TempDir("i5fsw");
        string slot = tmp.Dir("1");
        string file = Path.Combine(slot, "한글 이름.pdf");
        File.WriteAllText(file, "처음");
        int raised = 0;
        using var sub = new FileChangeSource().Watch(file, () => Interlocked.Increment(ref raised));

        async Task<bool> WaitRaise(int beyond)
        {
            var until = DateTime.UtcNow.AddSeconds(10);
            while (Volatile.Read(ref raised) <= beyond && DateTime.UtcNow < until) await Task.Delay(50);
            return Volatile.Read(ref raised) > beyond;
        }

        await Task.Delay(200);
        int n0 = Volatile.Read(ref raised);
        string renamed = Path.Combine(slot, "새 이름.pdf");
        File.Move(file, renamed);
        Assert.True(await WaitRaise(n0), "이름 바꾸기를 알리지 않음");
        await Task.Delay(2500);   // 2초 확인까지 가라앉게
        int n1 = Volatile.Read(ref raised);
        File.WriteAllText(renamed, "새 이름으로 저장 — 길이가 다름");
        Assert.True(await WaitRaise(n1), "새 이름의 저장을 알리지 않음");
    }

    /// <summary>계약(소스): 일반 시작은 청소가 찾은 남은 고친 파일을 메인 창을 띄운 뒤 묻는다 — 창 없이 풀기·두 번째 실행·메인 창 없는 우클릭 풀기에서는 묻지 않는다.</summary>
    [Fact]
    public void 시작은_청소가_찾은_것을_메인_창을_띄운_뒤_묻는다()
    {
        string src = RepoFiles.Read("Iljip", "App.xaml.cs");
        int start = src.IndexOf("protected override async void OnStartup", StringComparison.Ordinal);
        int end = src.IndexOf("private static async Task OfferLeftoversAsync", StringComparison.Ordinal);
        string body = src[start..end];
        int janitor = body.IndexOf("TempJanitor.CleanupStale(SystemClock.Instance, leftovers)", StringComparison.Ordinal);
        int windowless = body.IndexOf("await RunWindowlessAsync(shellVm, startArgs);", StringComparison.Ordinal);
        int show = body.IndexOf("win.Show();", StringComparison.Ordinal);
        int offer = body.IndexOf("OfferLeftoversAsync(leftoverVm, janitor, leftovers)", StringComparison.Ordinal);
        Assert.True(janitor > 0 && windowless > janitor && show > windowless && offer > show, "청소 → (우클릭 풀기면 끝) → 창 띄움 → 알림 차례가 아님");
        Assert.Contains("await vm.OfferLeftoverChangesAsync(", src);
    }
}
