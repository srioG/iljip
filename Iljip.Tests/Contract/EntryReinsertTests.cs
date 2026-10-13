using Iljip.Models;
using Iljip.Services;
using Iljip.Services.Editing;
using Iljip.Tests.TestSupport;
using Iljip.ViewModels;

namespace Iljip.Tests.Contract;

/// <summary>
/// [W2-06, I3-3] 압축 속 파일을 열어(I1-3 — 가짜 셸: 실제 프로그램을 띄우지 않음) 고쳐 저장하면 ‘‘보고서.txt’가 바뀌었어요. 압축 파일에 다시 넣을까요?’.
/// 파일 바뀜 알림은 가짜(<see cref="FakeFileChangeSource"/> — 시험이 직접 알림). 알림 뒤 조용히 기다리는 시간은 짧게 줄인다.
/// [넣기] = 같은 자리·같은 이름으로 바꿔 넣기(한 번의 고치기 — 연 뒤에 지웠으면 제자리에 새로), [나중에] = 압축을 닫을 때·일집을 닫을 때 다시 묻기, [넣지 않기] = 이번 내용은 넣지 않음.
/// 시각만 바뀐 저장(내용 같음)은 묻지 않는다. 고칠 수 없는 형식(TAR 등)이면 ‘다른 곳에 저장할까요?’ → 저장 창 → 복사.
/// </summary>
public class EntryReinsertTests
{
    private static Task Run(Func<Task> action, string what) => VmRunner.Run(action, what);

    private sealed class Ctx : IDisposable
    {
        public required TempDir Temp { get; init; }
        public required string Archive { get; init; }
        public required MainViewModel Vm { get; init; }
        public required FakeUserInteraction Ui { get; init; }
        public required TestServices Services { get; init; }
        public string OpenedPath { get; set; } = string.Empty;

        public void Dispose()
        {
            Vm.Shutdown();
            Temp.Dispose();
        }
    }

    /// <summary>자료/{보고서.txt, 표.csv} 압축(zip 또는 tar)을 열고 자료/보고서.txt 를 연다.</summary>
    private static async Task<Ctx> OpenReport(string kind = "zip", string? password = null, string tag = "i33")
    {
        var t = new TempDir(tag);
        string archive = await MakeReport(t, kind, password);
        return await OpenExisting(t, archive, password);
    }

    /// <summary>자료/{보고서.txt, 표.csv} 압축(zip·7z·tar)을 만든다.</summary>
    private static async Task<string> MakeReport(TempDir t, string kind, string? password = null)
    {
        string root = t.Dir("원본", "자료");
        File.WriteAllText(Path.Combine(root, "보고서.txt"), "처음 내용");
        File.WriteAllText(Path.Combine(root, "표.csv"), "가,나");
        string archive;
        if (kind == "zip")
        {
            archive = t.P("보관.zip");
            await new ZipArchiveService().CompressAsync(new[] { root }, archive,
                password is null ? null : new CompressionOptions { Password = password, Encryption = ZipEncryption.ZipCrypto });
        }
        else if (kind == "7z")
        {
            archive = SevenZipTestArchives.SevenZip(root, t.P("보관.7z"), password: password, encryptNames: password is not null);
        }
        else
        {
            archive = t.P("보관.tar");
            await new TarArchiveService().CompressAsync(new[] { root }, archive);
        }
        return archive;
    }

    /// <summary>만들어 둔 압축을 열고 자료/보고서.txt 를 연다.</summary>
    private static async Task<Ctx> OpenExisting(TempDir t, string archive, string? password = null)
    {
        var ts = new TestServices();
        var ui = new FakeUserInteraction(password is null ? Array.Empty<string?>() : new string?[] { password });
        var vm = new MainViewModel(ui, null, ts.Services) { EntryChangeQuietPeriod = TimeSpan.FromMilliseconds(30) };
        var ctx = new Ctx { Temp = t, Archive = archive, Vm = vm, Ui = ui, Services = ts };
        await Run(() => vm.OpenArchiveAsync(archive), "열기");
        var entry = vm.Entries.Single(e => TreeAssert.NormalizeEntryPath(e.Path) == "자료/보고서.txt");
        bool opened = false;
        await Run(async () => opened = await vm.OpenEntryAsync(entry), "안의 파일 열기");
        Assert.True(opened);
        ctx.OpenedPath = Assert.Single(ts.Shell.OpenedFiles);
        Assert.Equal("보고서.txt", Path.GetFileName(ctx.OpenedPath));
        Assert.Equal("처음 내용", File.ReadAllText(ctx.OpenedPath));
        Assert.Contains(Path.GetFullPath(ctx.OpenedPath), ts.FileChanges.Watched);
        return ctx;
    }

    /// <summary>파일을 고쳐 저장하고 알림을 보낸 뒤, 확인·질문이 끝날 때까지 기다린다.</summary>
    private static async Task SaveAndNotify(Ctx c, string content)
    {
        File.WriteAllText(c.OpenedPath, content);
        Assert.Equal(1, c.Services.FileChanges.Raise(c.OpenedPath));
        await Run(() => c.Vm.WhenEntryChecksIdleAsync(), "바뀜 확인");
    }

    private static async Task<string> ReadReport(Ctx c, string? password = null)
    {
        string dest = c.Temp.Dir("확인-" + Guid.NewGuid().ToString("N")[..6]);
        await Svc.For(c.Archive).ExtractAsync(c.Archive, dest, password);
        return File.ReadAllText(Path.Combine(dest, "자료", "보고서.txt"));
    }

    /// <summary>계약: 고쳐 저장 → 한 번 묻고 [넣기] → 압축 안 내용이 새 내용(SHA 같음), 다른 항목·항목 수는 그대로, 목록도 다시 읽힘.</summary>
    [Theory]
    [InlineData("zip")]
    [InlineData("7z")]
    public async Task 고쳐_저장하면_묻고_넣기를_고르면_다시_넣는다(string kind)
    {
        using var c = await OpenReport(kind);
        c.Ui.ChooseByLabel = "넣기";

        await SaveAndNotify(c, "고친 내용 — 두 번째 줄까지");

        var prompt = Assert.Single(c.Ui.ChoicePrompts);
        Assert.Equal("‘보고서.txt’가 바뀌었어요. 압축 파일에 다시 넣을까요?", prompt.Message);
        Assert.Equal(new[] { "넣기", "나중에", "넣지 않기" }, prompt.Choices);
        Assert.Empty(c.Ui.Errors);
        Assert.Equal("고친 내용 — 두 번째 줄까지", await ReadReport(c));
        var list = await Svc.For(c.Archive).ListEntriesAsync(c.Archive);
        Assert.Equal(new[] { "자료/보고서.txt", "자료/표.csv" },
            list.Where(e => !e.IsDirectory).Select(e => TreeAssert.NormalizeEntryPath(e.Path)).OrderBy(p => p, StringComparer.Ordinal).ToArray());
        Assert.Equal(TreeAssert.Sha256(c.OpenedPath), TreeAssert.Sha256(System.Text.Encoding.UTF8.GetBytes(await ReadReport(c))));

        // 넣은 뒤 또 고치면 다시 묻는다(새 기준)
        await SaveAndNotify(c, "세 번째 고침");
        Assert.Equal(2, c.Ui.ChoicePrompts.Count);
        Assert.Equal("세 번째 고침", await ReadReport(c));
    }

    /// <summary>계약: 시각만 바뀐 저장(내용 같음)은 묻지 않는다. 알림이 여러 번 와도(저장 한 번에 여러 번) 한 번만 묻는다.</summary>
    [Fact]
    public async Task 내용이_같으면_묻지_않고_여러_알림은_한_번만_묻는다()
    {
        using var c = await OpenReport(tag: "i33same");
        string sha = TreeAssert.Sha256(c.Archive);
        File.SetLastWriteTime(c.OpenedPath, DateTime.Now.AddMinutes(5));
        c.Services.FileChanges.Raise(c.OpenedPath);
        await Run(() => c.Vm.WhenEntryChecksIdleAsync(), "바뀜 확인");
        Assert.Empty(c.Ui.ChoicePrompts);
        Assert.Equal(sha, TreeAssert.Sha256(c.Archive));

        c.Ui.ChooseByLabel = "넣지 않기";
        File.WriteAllText(c.OpenedPath, "고침");
        for (int i = 0; i < 5; i++) c.Services.FileChanges.Raise(c.OpenedPath);
        await Run(() => c.Vm.WhenEntryChecksIdleAsync(), "바뀜 확인");
        Assert.Single(c.Ui.ChoicePrompts);
        Assert.Equal(sha, TreeAssert.Sha256(c.Archive));   // 넣지 않기 — 압축 그대로
    }

    /// <summary>계약: [나중에] → 그 뒤 저장에는 묻지 않고, 압축을 닫을 때(비우기) 다시 묻는다 — 그때 [넣기]면 넣는다.</summary>
    [Fact]
    public async Task 나중에를_고르면_닫을_때_다시_묻는다()
    {
        using var c = await OpenReport(tag: "i33later");
        c.Ui.ChooseByLabel = "나중에";
        await SaveAndNotify(c, "고침 1");
        await SaveAndNotify(c, "고침 2");
        Assert.Single(c.Ui.ChoicePrompts);
        Assert.Equal("처음 내용", await ReadReport(c));

        c.Ui.ChooseByLabel = "넣기";
        await Run(() => c.Vm.ClearCommand.ExecuteAsync(null), "비우기");

        Assert.Equal(2, c.Ui.ChoicePrompts.Count);
        Assert.Equal("‘보고서.txt’가 바뀌었는데 압축 파일에 아직 넣지 않았어요. 지금 넣을까요?", c.Ui.ChoicePrompts[1].Message);
        Assert.Equal("고침 2", await ReadReport(c));
        Assert.False(c.Vm.HasAnyContent);
    }

    /// <summary>계약: 일집을 닫을 때(창 닫기) 아직 넣지 않은 고친 파일이 있으면 묻는다 — [취소]면 닫지 않고, [넣기]면 넣고 닫는다.</summary>
    [Fact]
    public async Task 닫을_때_넣지_않은_것을_묻는다()
    {
        using var c = await OpenReport(tag: "i33exit");
        c.Ui.ChooseByLabel = "나중에";
        await SaveAndNotify(c, "닫기 전 고침");
        Assert.True(c.Vm.HasOpenedEntries);

        c.Ui.ChooseByLabel = "취소";
        bool close = true;
        await Run(async () => close = await c.Vm.ConfirmExitAsync(), "닫기 확인(취소)");
        Assert.False(close);
        Assert.Contains("일집을 닫으면 바뀐 내용이 없어져요", c.Ui.ChoicePrompts[^1].Message);
        Assert.Equal(new[] { "넣기", "넣지 않기(닫기)", "취소" }, c.Ui.ChoicePrompts[^1].Choices);

        c.Ui.ChooseByLabel = "넣기";
        await Run(async () => close = await c.Vm.ConfirmExitAsync(), "닫기 확인(넣기)");
        Assert.True(close);
        Assert.Equal("닫기 전 고침", await ReadReport(c));

        // 다 넣었으면 더 묻지 않는다
        int asked = c.Ui.ChoicePrompts.Count;
        await Run(async () => close = await c.Vm.ConfirmExitAsync(), "닫기 확인(없음)");
        Assert.True(close);
        Assert.Equal(asked, c.Ui.ChoicePrompts.Count);
    }

    /// <summary>계약: 암호 압축에서 연 파일은 연 때의 암호로 다시 넣는다(다시 묻지 않음) — 넣은 파일도 암호로 잠김.</summary>
    [Fact]
    public async Task 암호_압축은_연_때의_암호로_다시_넣는다()
    {
        using var c = await OpenReport(password: FixtureCatalog.DefaultPassword, tag: "i33pw");
        Assert.Single(c.Ui.PasswordPrompts);   // 열 때 한 번
        c.Ui.ChooseByLabel = "넣기";

        await SaveAndNotify(c, "암호 압축 고침");

        Assert.Single(c.Ui.PasswordPrompts);
        Assert.Empty(c.Ui.Errors);
        Assert.Equal("암호 압축 고침", await ReadReport(c, FixtureCatalog.DefaultPassword));
        var ex = await Svc.Catch(() => Svc.For(c.Archive).ExtractAsync(c.Archive, c.Temp.P("암호없이"), null));
        Assert.IsAssignableFrom<ArchivePasswordException>(ex);
    }

    /// <summary>계약: 고칠 수 없는 형식(TAR)에서 연 파일을 고치면 ‘이 형식은 고칠 수 없어요. 바뀐 파일을 다른 곳에 저장할까요?’ → 저장 창 → 복사(압축은 그대로).</summary>
    [Fact]
    public async Task 고칠_수_없는_형식은_다른_곳에_저장한다()
    {
        using var c = await OpenReport("tar", tag: "i33tar");
        string sha = TreeAssert.Sha256(c.Archive);
        c.Ui.ChooseByLabel = "다른 곳에 저장";
        c.Ui.SavePath = c.Temp.P("저장한 보고서.txt");

        await SaveAndNotify(c, "TAR 에서 고침");

        var prompt = Assert.Single(c.Ui.ChoicePrompts);
        Assert.Equal("‘보고서.txt’가 바뀌었어요. 이 형식은 고칠 수 없어요. 바뀐 파일을 다른 곳에 저장할까요?", prompt.Message);
        Assert.Equal(new[] { "다른 곳에 저장", "나중에", "저장하지 않기" }, prompt.Choices);
        Assert.Equal(new[] { "보고서.txt" }, c.Ui.SaveDefaultNames);
        Assert.Equal("TAR 에서 고침", File.ReadAllText(c.Temp.P("저장한 보고서.txt")));
        Assert.Equal(sha, TreeAssert.Sha256(c.Archive));

        // 저장했으면 닫을 때 다시 묻지 않는다
        bool close = false;
        await Run(async () => close = await c.Vm.ConfirmExitAsync(), "닫기 확인");
        Assert.True(close);
        Assert.Single(c.Ui.ChoicePrompts);
    }

    // ───────────────────────────── W2-06 검증 지적 ─────────────────────────────

    /// <summary>
    /// 계약(검증 지적): 넣는 동안(검사 단계) 또 저장한 내용은 말없이 사라지지 않는다 — 넣는 것은 [넣기]를 고른 때 복사해 둔 내용이고,
    /// 넣고 나서 바로 다시 묻는다([넣기]면 마지막 내용이 압축에 들어감). 예전엔 넣은 뒤 기준을 '지금 파일'로 잡아 또 저장한 내용이 넣은 것으로 여겨졌다.
    /// </summary>
    [Theory]
    [InlineData("zip")]
    [InlineData("7z")]
    public async Task 넣는_동안_또_저장하면_넣은_뒤_다시_묻는다(string kind)
    {
        using var c = await OpenReport(kind, tag: "i33race");
        c.Ui.ChooseByLabel = "넣기";
        int saved = 0;
        c.Ui.OnProgressReport = p =>
        {
            if (p.CurrentFile == EditSupport.StageTesting && Interlocked.CompareExchange(ref saved, 1, 0) == 0)
                File.WriteAllText(c.OpenedPath, "넣는 동안 또 고친 내용");
        };

        await SaveAndNotify(c, "처음 고친 내용");

        Assert.Equal(1, saved);
        Assert.Empty(c.Ui.Errors);
        Assert.Equal(2, c.Ui.ChoicePrompts.Count);
        Assert.All(c.Ui.ChoicePrompts, p => Assert.Equal("‘보고서.txt’가 바뀌었어요. 압축 파일에 다시 넣을까요?", p.Message));
        Assert.Equal("넣는 동안 또 고친 내용", await ReadReport(c));

        // 다 넣었으니 닫을 때 묻지 않는다
        bool close = false;
        await Run(async () => close = await c.Vm.ConfirmExitAsync(), "닫기 확인");
        Assert.True(close);
        Assert.Equal(2, c.Ui.ChoicePrompts.Count);
    }

    /// <summary>계약(검증 지적): [넣지 않기]는 물어본 그 내용만 넘긴다 — 묻는 동안 또 저장한 내용은 바뀐 것으로 남아 닫을 때 다시 묻는다.</summary>
    [Fact]
    public async Task 넣지_않기는_물어본_내용만_넘긴다()
    {
        using var c = await OpenReport(tag: "i33skip");
        string sha = TreeAssert.Sha256(c.Archive);
        c.Ui.ChooseByLabel = "넣지 않기";
        c.Ui.OnChoicePrompt = _ =>
        {
            c.Ui.OnChoicePrompt = null;
            File.WriteAllText(c.OpenedPath, "묻는 동안 또 고친 내용");
        };

        await SaveAndNotify(c, "넘길 내용");
        Assert.Single(c.Ui.ChoicePrompts);
        Assert.Equal(sha, TreeAssert.Sha256(c.Archive));

        c.Ui.ChooseByLabel = "넣기";
        bool close = false;
        await Run(async () => close = await c.Vm.ConfirmExitAsync(), "닫기 확인");
        Assert.True(close);
        Assert.Equal(2, c.Ui.ChoicePrompts.Count);
        Assert.Contains("일집을 닫으면 바뀐 내용이 없어져요", c.Ui.ChoicePrompts[1].Message);
        Assert.Equal("묻는 동안 또 고친 내용", await ReadReport(c));
    }

    /// <summary>
    /// 계약(검증 지적): 연 뒤에 압축에서 그 항목을 지웠어도 [넣기]는 실패하지 않고 원래 자리에 새로 넣는다
    /// (예전: '지우기 + 넣기'라 '찾지 못했어요' 오류로 고친 내용을 넣을 길이 없었음).
    /// </summary>
    [Theory]
    [InlineData("zip")]
    [InlineData("7z")]
    public async Task 연_뒤에_지운_항목도_제자리에_다시_넣는다(string kind)
    {
        using var c = await OpenReport(kind, tag: "i33gone");
        c.Ui.ChooseByLabel = "지우기";
        bool deleted = false;
        var report = c.Vm.Entries.Single(e => TreeAssert.NormalizeEntryPath(e.Path) == "자료/보고서.txt");
        await Run(async () => deleted = await c.Vm.DeleteEntriesAsync(new[] { report }), "지우기");
        Assert.True(deleted);
        Assert.DoesNotContain("자료/보고서.txt",
            (await Svc.For(c.Archive).ListEntriesAsync(c.Archive)).Select(e => TreeAssert.NormalizeEntryPath(e.Path)));

        c.Ui.ChooseByLabel = "넣기";
        await SaveAndNotify(c, "지운 뒤에 고친 내용");

        Assert.Empty(c.Ui.Errors);
        Assert.Equal(2, c.Ui.ChoicePrompts.Count);
        Assert.Equal("지운 뒤에 고친 내용", await ReadReport(c));
        var list = await Svc.For(c.Archive).ListEntriesAsync(c.Archive);
        Assert.Equal(new[] { "자료/보고서.txt", "자료/표.csv" },
            list.Where(e => !e.IsDirectory).Select(e => TreeAssert.NormalizeEntryPath(e.Path)).OrderBy(p => p, StringComparer.Ordinal).ToArray());
    }

    /// <summary>
    /// 계약(검증 지적): 다시 넣기에 실패하면(예: 압축 파일이 읽기 전용) 오류 창 뒤에 ‘‘보고서.txt’를 압축 파일에 넣지 못했어요. 고친 내용을 잃지 않게 다른 곳에 저장할까요?’.
    /// [나중에]면 남겨 두고, 일집을 닫을 때 또 실패하면 [닫지 않기]로 닫기를 멈출 수 있고, [다른 곳에 저장]이면 저장 창 → 복사. 압축은 바이트 그대로.
    /// </summary>
    [Fact]
    public async Task 넣지_못하면_고친_내용을_다른_곳에_저장하게_한다()
    {
        using var c = await OpenReport(tag: "i33fail");
        string sha = TreeAssert.Sha256(c.Archive);
        const string offer = "‘보고서.txt’를 압축 파일에 넣지 못했어요. 고친 내용을 잃지 않게 다른 곳에 저장할까요?";
        File.SetAttributes(c.Archive, FileAttributes.ReadOnly);
        try
        {
            // 저장 직후: [넣기] → 실패(오류 창) → [나중에]
            c.Ui.ChoiceAnswers.Enqueue(0);
            c.Ui.ChoiceAnswers.Enqueue(1);
            await SaveAndNotify(c, "못 넣은 고침");
            Assert.Contains(ArchiveSwap.ReadOnlyMessage, Assert.Single(c.Ui.Errors));
            Assert.Equal(2, c.Ui.ChoicePrompts.Count);
            Assert.Equal(offer, c.Ui.ChoicePrompts[1].Message);
            Assert.Equal(new[] { "다른 곳에 저장", "나중에" }, c.Ui.ChoicePrompts[1].Choices);
            Assert.Empty(c.Ui.SaveDefaultNames);

            // 닫을 때: [넣기] → 또 실패 → [닫지 않기] → 닫지 않음
            c.Ui.ChoiceAnswers.Enqueue(0);
            c.Ui.ChoiceAnswers.Enqueue(1);
            bool close = true;
            await Run(async () => close = await c.Vm.ConfirmExitAsync(), "닫기 확인(닫지 않기)");
            Assert.False(close);
            Assert.Equal(offer, c.Ui.ChoicePrompts[^1].Message);
            Assert.Equal(new[] { "다른 곳에 저장", "닫지 않기" }, c.Ui.ChoicePrompts[^1].Choices);

            // 닫을 때: [넣기] → 실패 → [다른 곳에 저장] → 저장하고 닫음
            c.Ui.ChoiceAnswers.Enqueue(0);
            c.Ui.ChoiceAnswers.Enqueue(0);
            c.Ui.SavePath = c.Temp.P("살린 보고서.txt");
            await Run(async () => close = await c.Vm.ConfirmExitAsync(), "닫기 확인(저장)");
            Assert.True(close);
            Assert.Equal(new[] { "보고서.txt" }, c.Ui.SaveDefaultNames);
            Assert.Equal("못 넣은 고침", File.ReadAllText(c.Temp.P("살린 보고서.txt")));
            Assert.Equal(3, c.Ui.Errors.Count);
            Assert.Equal(6, c.Ui.ChoicePrompts.Count);

            // 저장했으면 더 묻지 않는다
            await Run(async () => close = await c.Vm.ConfirmExitAsync(), "닫기 확인(없음)");
            Assert.True(close);
            Assert.Equal(6, c.Ui.ChoicePrompts.Count);
        }
        finally
        {
            File.SetAttributes(c.Archive, FileAttributes.Normal);
        }
        Assert.Equal(sha, TreeAssert.Sha256(c.Archive));
    }

    /// <summary>
    /// 계약(검증 지적): 7z.dll 을 못 불러온 PC 에서 7z 안의 파일을 고치면 '이 형식은 고칠 수 없어요'(7z 는 고칠 수 있는 형식 — 틀린 말)가 아니라
    /// ‘이 PC에서는 7Z 파일을 고칠 수 없어요.’ 로 묻고 다른 곳에 저장하게 한다.
    /// </summary>
    [Fact]
    public async Task 막힌_PC에서는_7z_안의_파일을_다른_곳에_저장하게_한다()
    {
        var t = new TempDir("i33no7z");
        string archive = await MakeReport(t, "7z");
        string sha = TreeAssert.Sha256(archive);
        using var blocked = SevenZipLibrary.SimulateUnavailableForTests();
        using var c = await OpenExisting(t, archive);
        c.Ui.ChooseByLabel = "다른 곳에 저장";
        c.Ui.SavePath = t.P("저장한 보고서.txt");

        await SaveAndNotify(c, "막힌 PC 에서 고침");

        var prompt = Assert.Single(c.Ui.ChoicePrompts);
        Assert.Equal("‘보고서.txt’가 바뀌었어요. 이 PC에서는 7Z 파일을 고칠 수 없어요. 바뀐 파일을 다른 곳에 저장할까요?", prompt.Message);
        Assert.Equal(new[] { "다른 곳에 저장", "나중에", "저장하지 않기" }, prompt.Choices);
        Assert.Equal("막힌 PC 에서 고침", File.ReadAllText(t.P("저장한 보고서.txt")));
        Assert.Empty(c.Ui.Errors);
        Assert.Equal(sha, TreeAssert.Sha256(archive));
    }

    /// <summary>일집을 닫으면(Shutdown) 감시를 멈추고 임시 작업 폴더를 지운다 — 그 뒤 알림은 묻지 않는다.</summary>
    [Fact]
    public async Task 닫으면_감시를_멈추고_임시_폴더를_지운다()
    {
        using var c = await OpenReport(tag: "i33down");
        string slotRoot = Path.GetDirectoryName(Path.GetDirectoryName(c.OpenedPath)!)!;
        Assert.True(Directory.Exists(slotRoot));
        c.Vm.Shutdown();
        Assert.False(Directory.Exists(slotRoot));
        Assert.Equal(0, c.Services.FileChanges.ActiveCount);
        Assert.Equal(0, c.Services.FileChanges.Raise(c.OpenedPath));
        await Run(() => c.Vm.WhenEntryChecksIdleAsync(), "바뀜 확인");
        Assert.Empty(c.Ui.ChoicePrompts);
    }

    /// <summary>실행 파일(.exe·.bat …)은 열기 전에 묻고(기본 [취소]), 취소하면 열지 않는다(I1-3 — W2-04 와 같은 규칙).</summary>
    [Fact]
    public async Task 실행_파일은_열기_전에_묻는다()
    {
        using var t = new TempDir("i33exe");
        string root = t.Dir("원본", "자료");
        File.WriteAllText(Path.Combine(root, "설치.bat"), "@echo off");
        string zip = t.P("실행.zip");
        await new ZipArchiveService().CompressAsync(new[] { root }, zip);
        var ts = new TestServices();
        var ui = new FakeUserInteraction();
        var vm = new MainViewModel(ui, null, ts.Services);
        try
        {
            await Run(() => vm.OpenArchiveAsync(zip), "열기");
            bool opened = true;
            await Run(async () => opened = await vm.OpenEntryAsync(vm.Entries.Single(e => e.Name == "설치.bat")), "열기");
            Assert.False(opened);
            var prompt = Assert.Single(ui.ChoicePrompts);
            Assert.Equal(MainViewModel.ExecutableWarning("설치.bat"), prompt.Message);
            Assert.Equal("취소", prompt.Choices[prompt.DefaultIndex]);
            Assert.Empty(ts.Shell.OpenedFiles);
        }
        finally
        {
            vm.Shutdown();
        }
    }

    // ───────────────────────────── W2-06 검증 지적 2차 ─────────────────────────────

    /// <summary>다른 압축(다른/메모.txt) — 명령줄로 여는 쪽.</summary>
    private static async Task<string> MakeOther(TempDir t)
    {
        string root = t.Dir("다른 원본", "다른");
        File.WriteAllText(Path.Combine(root, "메모.txt"), "다른 압축");
        string other = t.P("다른.zip");
        await new ZipArchiveService().CompressAsync(new[] { root }, other);
        return other;
    }

    /// <summary>
    /// 계약(검증 지적 2차): ‘다시 넣을까요?’를 묻는 동안 명령줄(탐색기 우클릭 '일집으로 열기' = --open, 풀기 = --extract)로 다른 압축이 들어와도
    /// 서로 기다려 멈추지 않는다 — 다른 압축이 열리고, [넣기]는 원래 압축에 들어가며, 같은 파일을 두 번 묻지 않는다.
    /// (예전: 명령줄 열기가 명령 차례를 쥔 채 묻기·넣기가 끝나기를 기다리고, [넣기]는 그 차례를 기다려 둘 다 영영 멈춤 — 그 뒤 명령도 모두 막힘)
    /// 실제로는 묻는 창(모달)이 떠 있는 동안 두 번째 실행의 파이프 명령이 창의 메시지 처리 안에서 돈다 — 가짜 창이 답하기 전에 명령줄을 부르는 것이 그 모양.
    /// </summary>
    [Theory]
    [InlineData("--open")]
    [InlineData("--extract")]
    public async Task 묻는_동안_명령줄로_다른_압축을_열어도_멈추지_않는다(string verb)
    {
        using var c = await OpenReport(tag: "i33dl");
        string other = await MakeOther(c.Temp);
        c.Ui.ChooseByLabel = "넣기";
        Task? command = null;
        c.Ui.OnChoicePrompt = _ =>
        {
            c.Ui.OnChoicePrompt = null;
            command = c.Vm.ProcessCommandLineAsync(new[] { verb, other });
        };

        await SaveAndNotify(c, "명령줄과 겹친 고침");
        Assert.NotNull(command);
        await Run(() => command!, "명령줄 열기");

        Assert.Equal(other, c.Vm.CurrentArchivePath);
        Assert.Single(c.Ui.ChoicePrompts);
        Assert.Empty(c.Ui.Errors);
        Assert.Equal("명령줄과 겹친 고침", await ReadReport(c));

        // 그 뒤의 명령·고치기도 막히지 않는다(명령 차례가 풀림)
        await Run(() => c.Vm.ProcessCommandLineAsync(new[] { "--open", c.Archive }), "다시 열기");
        Assert.Equal(c.Archive, c.Vm.CurrentArchivePath);
        bool close = false;
        await Run(async () => close = await c.Vm.ConfirmExitAsync(), "닫기 확인");
        Assert.True(close);
        Assert.Single(c.Ui.ChoicePrompts);
    }

    /// <summary>
    /// 계약(검증 지적 2차): ‘다시 넣을까요?’를 묻는 동안 일집을 닫으려 하면 닫지 않는다(‘…’를 압축 파일에 다시 넣는 일이 아직 끝나지 않았어요…) —
    /// 예전엔 묻는 중인 파일을 건너뛰고 닫기를 허락해, 작업 폴더가 지워지며 고친 내용이 사라졌다. 답하면 넣기가 끝나고, 그 뒤에는 묻지 않고 닫힌다.
    /// </summary>
    [Fact]
    public async Task 묻는_동안_일집을_닫으려_하면_닫지_않는다()
    {
        using var c = await OpenReport(tag: "i33exitask");
        c.Ui.ChooseByLabel = "넣기";
        Task<bool>? exit = null;
        c.Ui.OnChoicePrompt = _ =>
        {
            c.Ui.OnChoicePrompt = null;
            exit = c.Vm.ConfirmExitAsync();
        };

        await SaveAndNotify(c, "묻는 중 닫기");

        Assert.NotNull(exit);
        Assert.False(await exit!);
        Assert.Equal(MainViewModel.ExitWhileReinsertingMessage("보고서.txt"), Assert.Single(c.Ui.Infos));
        Assert.Equal("‘보고서.txt’를 압축 파일에 다시 넣는 일이 아직 끝나지 않았어요. 끝난 뒤에 닫아 주세요.", c.Ui.Infos[0]);
        Assert.Single(c.Ui.ChoicePrompts);
        Assert.True(File.Exists(c.OpenedPath));
        Assert.Equal("묻는 중 닫기", await ReadReport(c));

        bool close = false;
        await Run(async () => close = await c.Vm.ConfirmExitAsync(), "닫기 확인");
        Assert.True(close);
        Assert.Single(c.Ui.ChoicePrompts);
    }

    /// <summary>
    /// 계약(검증 지적 2차): 다시 넣는 도중(진행 창은 모달이 아님) 일집을 닫으려 하면 닫지 않는다 — 넣기는 끝까지 되고 고친 파일도 남는다.
    /// (예전: 묻지 않고 닫기를 허락 → 작업 폴더가 지워지고 앱이 끝나 넣기도 끊김)
    /// </summary>
    [Theory]
    [InlineData("zip")]
    [InlineData("7z")]
    public async Task 넣는_동안_일집을_닫으려_하면_닫지_않고_넣기를_마친다(string kind)
    {
        using var c = await OpenReport(kind, tag: "i33exitrun");
        c.Ui.ChooseByLabel = "넣기";
        Task<bool>? exit = null;
        c.Ui.OnProgressReport = p =>
        {
            if (p.CurrentFile == EditSupport.StageRewriting && exit is null)
                exit = c.Vm.ConfirmExitAsync();
        };

        await SaveAndNotify(c, "넣는 중 닫기");

        Assert.NotNull(exit);
        Assert.True(exit!.IsCompleted);
        Assert.False(await exit);
        Assert.Equal(MainViewModel.ExitWhileReinsertingMessage("보고서.txt"), Assert.Single(c.Ui.Infos));
        Assert.Empty(c.Ui.Errors);
        Assert.True(File.Exists(c.OpenedPath));
        Assert.Equal("넣는 중 닫기", await ReadReport(c));

        bool close = false;
        await Run(async () => close = await c.Vm.ConfirmExitAsync(), "닫기 확인");
        Assert.True(close);
        Assert.Single(c.Ui.ChoicePrompts);
    }

    /// <summary>
    /// 계약(검증 지적 2차): 맥·리눅스에서 만든 압축의 Windows 에 못 쓰는 이름(‘회의:메모.txt’·끝 점)의 항목을 열어 고치고 [넣기]를 고르면 원래 이름 그대로 바꿔 넣는다
    /// (예전: 사용자가 바꾼 적 없는 이름 때문에 '이름에 쓸 수 없는 글자가 있어요' 오류 → '다른 곳에 저장' 권하기).
    /// </summary>
    [Theory]
    [InlineData("자료/회의:메모.txt")]
    [InlineData("자료/끝점.")]
    public async Task 윈도우에_못_쓰는_이름의_항목도_고쳐서_다시_넣는다(string key)
    {
        using var t = new TempDir("i33odd");
        string arc = t.P("맥에서.zip");
        using (var za = System.IO.Compression.ZipFile.Open(arc, System.IO.Compression.ZipArchiveMode.Create))
        {
            using (var w = new StreamWriter(za.CreateEntry(key).Open())) w.Write("처음 내용");
            using (var w = new StreamWriter(za.CreateEntry("자료/표.csv").Open())) w.Write("가,나");
        }
        var ts = new TestServices();
        var ui = new FakeUserInteraction { ChooseByLabel = "넣기" };
        var vm = new MainViewModel(ui, null, ts.Services) { EntryChangeQuietPeriod = TimeSpan.FromMilliseconds(30) };
        try
        {
            await Run(() => vm.OpenArchiveAsync(arc), "열기");
            var entry = vm.Entries.Single(e => TreeAssert.NormalizeEntryPath(e.Path) == key);
            bool opened = false;
            await Run(async () => opened = await vm.OpenEntryAsync(entry), "안의 파일 열기");
            Assert.True(opened);
            string path = Assert.Single(ts.Shell.OpenedFiles);
            File.WriteAllText(path, "고친 내용");
            Assert.Equal(1, ts.FileChanges.Raise(path));
            await Run(() => vm.WhenEntryChecksIdleAsync(), "바뀜 확인");

            Assert.Empty(ui.Errors);
            Assert.Single(ui.ChoicePrompts);
            using var z = System.IO.Compression.ZipFile.OpenRead(arc);
            Assert.Equal(new[] { key, "자료/표.csv" }.OrderBy(k => k, StringComparer.Ordinal),
                z.Entries.Where(e => !e.FullName.EndsWith('/')).Select(e => e.FullName).OrderBy(k => k, StringComparer.Ordinal));
            using var r = new StreamReader(z.Entries.Single(e => e.FullName == key).Open());
            Assert.Equal("고친 내용", r.ReadToEnd());
        }
        finally
        {
            vm.Shutdown();
        }
    }

    /// <summary>
    /// 감시기(검증 지적 2차): '가라앉음'(<see cref="OpenedEntryWatcher.WhenSettledAsync"/>)은 조용히 기다림·내용 재기만 기다리고 묻기·넣기(부르는 쪽)는 기다리지 않는다 —
    /// 묻는 동안 또 저장해 앞 처리를 기다리는 확인도 기다리지 않는다. <see cref="OpenedEntryWatcher.WhenIdleAsync"/> 는 묻기까지 기다린다.
    /// </summary>
    [Fact]
    public async Task 감시기의_가라앉음은_묻기를_기다리지_않는다()
    {
        using var t = new TempDir("i33settle");
        string file = t.P("a.txt");
        File.WriteAllText(file, "처음");
        var entry = OpenedEntry.Capture("a.txt", file, t.P("x.zip"));
        var source = new FakeFileChangeSource();
        var asking = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var answer = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        int calls = 0;
        using var watcher = new OpenedEntryWatcher(source, async _ =>
        {
            Interlocked.Increment(ref calls);
            asking.TrySetResult();
            await answer.Task;
        })
        {
            QuietPeriod = TimeSpan.FromMilliseconds(300),
        };
        watcher.Watch(entry);

        File.WriteAllText(file, "고침");
        source.Raise(file);
        var settled = watcher.WhenSettledAsync();
        Assert.False(settled.IsCompleted);   // 조용히 기다리는 중은 기다린다
        await Run(() => settled, "가라앉음");
        await Run(() => asking.Task, "묻기 시작");

        File.WriteAllText(file, "묻는 동안 또 고침");
        source.Raise(file);
        await Run(() => watcher.WhenSettledAsync(), "묻는 동안의 가라앉음");
        var idle = watcher.WhenIdleAsync();
        await Task.Delay(150);
        Assert.False(idle.IsCompleted);   // 묻기가 끝나지 않았다
        Assert.Equal(1, calls);

        answer.TrySetResult();
        await Run(() => idle, "모두 끝남");
        Assert.Equal(2, calls);   // 앞 묻기가 끝난 뒤 아직 바뀐 것으로 남아 다시 부름
    }

    // ───────────────────────────── 감시기(순수) ─────────────────────────────

    /// <summary>감시기: 알림이 몰려와도 조용해진 뒤 한 번만, 내용이 같으면 부르지 않는다, 그만 보면 부르지 않는다.</summary>
    [Fact]
    public async Task 감시기는_조용해진_뒤_내용으로_판단한다()
    {
        using var t = new TempDir("i33w");
        string file = t.P("a.txt");
        File.WriteAllText(file, "처음");
        var entry = OpenedEntry.Capture("a.txt", file, t.P("x.zip"));
        var source = new FakeFileChangeSource();
        int calls = 0;
        using var watcher = new OpenedEntryWatcher(source, _ => { Interlocked.Increment(ref calls); return Task.CompletedTask; })
        {
            QuietPeriod = TimeSpan.FromMilliseconds(40),
        };
        watcher.Watch(entry);

        File.SetLastWriteTime(file, DateTime.Now.AddMinutes(1));   // 시각만
        source.Raise(file);
        await watcher.WhenIdleAsync();
        Assert.Equal(0, calls);

        File.WriteAllText(file, "고침");
        for (int i = 0; i < 4; i++) source.Raise(file);
        await watcher.WhenIdleAsync();
        Assert.Equal(1, calls);

        entry.Rebase();
        Assert.False(entry.IsModified());
        File.WriteAllText(file, "또 고침");
        watcher.Unwatch(entry);
        Assert.Equal(0, source.Raise(file));
        await watcher.WhenIdleAsync();
        Assert.Equal(1, calls);
    }

    /// <summary>받침 따라 '이/가': 한글 받침·영문(L·M·N·R)·숫자 읽는 소리, 그 밖은 '이(가)'.</summary>
    [Theory]
    [InlineData("보고서.xlsx", "가")]
    [InlineData("자료", "가")]
    [InlineData("사진", "이")]
    [InlineData("표.html", "이")]
    [InlineData("v1", "이")]
    [InlineData("v2", "가")]
    [InlineData("메모 (1)", "이")]
    [InlineData("기호#", "이(가)")]
    public void 이_가_고르기(string word, string expected)
    {
        Assert.Equal(expected, Josa.IGa(word));
    }
}
