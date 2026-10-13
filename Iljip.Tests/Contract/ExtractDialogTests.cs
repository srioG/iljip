using System.Text;
using System.Windows;
using Iljip.Models;
using Iljip.Services;
using Iljip.Tests.TestSupport;
using Iljip.ViewModels;
using Iljip.Views;

namespace Iljip.Tests.Contract;

/// <summary>
/// [W2-09 · I2-13] 풀기 창(폴더 고르기 창 대신): 풀 곳 + 최근 푼 곳(가장 최근이 맨 앞, 최대 10개, 없는 폴더는 숨김, 대소문자 무시로 겹치지 않음)
/// + '압축파일명으로 폴더 만들기'(기본 켬, 기억) + '다 풀고 압축 파일을 휴지통으로'(기본 꺼짐, 이번에만) + '다 풀면 폴더 열기'(기억).
/// 최종 풀 곳 = 폴더 만들기면 고른 곳\압축 이름(이미 있으면 '이름 (2)'), 아니면 고른 곳. 다 풀었으면 고른 곳을 최근 목록 맨 앞에 — 설정은 끝에 한 번 저장.
/// 화면은 가짜(<see cref="FakeUserInteraction.ExtractTargetAnswer"/>), 창 XAML 은 띄우지 않고 만들기만 한다.
/// </summary>
public class ExtractDialogTests
{
    private static Task Run(Func<Task> action, string what) => VmRunner.Run(action, what);

    private static string Copy(string work, string fixtureId, string name)
    {
        string dst = Path.Combine(work, name);
        File.Copy(FixtureCatalog.Get(fixtureId).FullPath, dst);
        return dst;
    }

    private static string SmallZip(string work, string name, params string[] files)
    {
        string p = Path.Combine(work, name);
        RawZip.Write(p, files.Select((f, i) => RawZip.Utf8(f, "내용 " + i)).ToArray());
        return p;
    }

    // ───────────────────────────── 최근 푼 곳(설정) ─────────────────────────────

    [Fact]
    public void 최근_푼_곳은_맨_앞에_겹치지_않게_10개까지()
    {
        var x = new ExtractSettings();
        for (int i = 1; i <= 12; i++) x.PushRecentFolder($@"C:\풀 곳\{i}");
        Assert.Equal(ExtractSettings.MaxRecentFolders, x.RecentFolders.Count);
        Assert.Equal(@"C:\풀 곳\12", x.RecentFolders[0]);
        Assert.Equal(@"C:\풀 곳\3", x.RecentFolders[^1]);

        // 같은 폴더(대소문자·끝 '\' 다름)는 하나만 — 맨 앞으로 옮김
        x.PushRecentFolder(@"c:\풀 곳\5\");
        Assert.Equal(@"c:\풀 곳\5", x.RecentFolders[0]);
        Assert.Single(x.RecentFolders, f => string.Equals(f, @"C:\풀 곳\5", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(ExtractSettings.MaxRecentFolders, x.RecentFolders.Count);

        // 드라이브 뿌리는 'C:\' 그대로, 빈 글자는 넣지 않음
        x.PushRecentFolder(@"D:\");
        Assert.Equal(@"D:\", x.RecentFolders[0]);
        x.PushRecentFolder("  ");
        Assert.Equal(@"D:\", x.RecentFolders[0]);
    }

    [Fact]
    public void 창에_보이는_최근_폴더는_지금_있는_것만_차례대로()
    {
        using var tmp = new TempDir("recent");
        string a = tmp.Dir("가"), b = tmp.Dir("나");
        var x = new ExtractSettings { RecentFolders = new List<string> { a, tmp.P("없음"), b.ToUpperInvariant(), b, "" } };
        Assert.Equal(new[] { a, b.ToUpperInvariant() }, x.VisibleRecentFolders());
        // 있는지 보다가 오류(네트워크 드라이브 등)가 나면 숨김
        Assert.Empty(x.VisibleRecentFolders(_ => throw new IOException("접근 안 됨")));
    }

    /// <summary>새 칸(extract.makeFolder·recentFolders·dialogOpenFolderAfter, ui.treeWidth)이 JSON 으로 오가고, 옛 설정(칸 없음)은 기본값으로 읽힌다.</summary>
    [Fact]
    public void 새_설정_칸이_JSON으로_오가고_옛_설정은_기본값()
    {
        var store = new InMemorySettingsStore();
        var s = store.Load();
        Assert.True(s.Extract.MakeFolder);
        Assert.Empty(s.Extract.RecentFolders);
        Assert.False(s.Extract.DialogOpenFolderAfter);
        Assert.Equal(260, s.Ui.TreeWidth);

        s.Extract.MakeFolder = false;
        s.Extract.DialogOpenFolderAfter = true;
        s.Extract.PushRecentFolder(@"D:\받은 파일");
        s.Ui.TreeWidth = 333;
        store.Save(s);
        Assert.Contains("\"makeFolder\": false", store.Json);
        Assert.Contains("\"recentFolders\"", store.Json);
        Assert.Contains("D:\\\\받은 파일", store.Json);   // 한글 그대로(\uXXXX 아님)
        Assert.Contains("\"treeWidth\": 333", store.Json);

        var back = store.Load();
        Assert.False(back.Extract.MakeFolder);
        Assert.True(back.Extract.DialogOpenFolderAfter);
        Assert.Equal(new[] { @"D:\받은 파일" }, back.Extract.RecentFolders);
        Assert.Equal(333, back.Ui.TreeWidth);

        using var tmp = new TempDir("oldset");
        string path = tmp.P("settings.json");
        File.WriteAllText(path, "{ \"schemaVersion\": 1, \"extract\": { \"onCollision\": \"Rename\", \"recentFolders\": null }, \"ui\": null }", new UTF8Encoding(false));
        var old = new JsonSettingsStore(path).Load();
        Assert.True(old.Extract.MakeFolder);
        Assert.NotNull(old.Extract.RecentFolders);
        Assert.Equal(260, old.Ui.TreeWidth);
    }

    // ───────────────────────────── 창 VM(규칙) ─────────────────────────────

    [Fact]
    public void 창_VM_처음_값과_풀리는_곳_미리_보기()
    {
        using var tmp = new TempDir("xvm");
        string work = tmp.Dir("받은 파일");
        string zip = Path.Combine(work, "보고서.zip");
        var vm = new ExtractOptionsViewModel(new ExtractTargetRequest
        {
            Archives = new[] { zip },
            InitialFolder = work,
            RecentFolders = new[] { tmp.Dir("최근") },
            MakeFolder = true,
            DeleteArchiveAfter = true,
            OpenFolderAfter = true,
        });
        Assert.Equal(work, vm.Folder);
        Assert.True(vm.MakeFolder);
        Assert.True(vm.DeleteArchiveAfter);
        Assert.True(vm.OpenFolderAfter);
        Assert.True(vm.ShowDeleteOption);
        Assert.Equal("압축 파일: 보고서.zip", vm.ArchiveText);
        Assert.Contains("‘보고서’ 폴더", vm.MakeFolderLabel);
        Assert.Equal("풀리는 곳: " + Path.Combine(work, "보고서"), vm.PreviewText);

        // 이미 있으면 '보고서 (2)'
        Directory.CreateDirectory(Path.Combine(work, "보고서"));
        vm.Folder = work + @"\";   // 끝 '\' 도 받아 줌
        Assert.Equal("풀리는 곳: " + Path.Combine(work, "보고서 (2)"), vm.PreviewText);

        vm.MakeFolder = false;
        Assert.Equal("풀리는 곳: " + work, vm.PreviewText);
        var r = vm.BuildResult();
        Assert.Equal(new ExtractTarget(work, false, true, true), r);
        Assert.Equal(work, r!.DestinationFor(zip));
        Assert.Equal(Path.Combine(work, "보고서 (2)"), (r with { MakeFolder = true }).DestinationFor(zip));
    }

    [Theory]
    [InlineData("", "풀 곳(폴더)을 골라 주세요.")]
    [InlineData("받은 파일", "경로가 올바르지 않아요")]
    [InlineData("?", "경로가 올바르지 않아요")]
    public void 창_VM_풀_곳이_올바르지_않으면_알리고_답이_없다(string folder, string expected)
    {
        var vm = new ExtractOptionsViewModel(new ExtractTargetRequest { Archives = new[] { @"C:\a.zip" }, InitialFolder = @"C:\" }) { Folder = folder };
        Assert.Contains(expected, vm.Validate());
        Assert.Null(vm.BuildResult());
        Assert.Equal(string.Empty, vm.PreviewText);
    }

    [Fact]
    public void 창_VM_없는_폴더면_알린다()
    {
        using var tmp = new TempDir("xvm2");
        string missing = tmp.P("없는 폴더");
        var vm = new ExtractOptionsViewModel(new ExtractTargetRequest { Archives = new[] { tmp.P("a.zip") }, InitialFolder = missing });
        Assert.Equal($"‘{missing}’ 폴더가 없어요. [찾아보기]로 골라 주세요.", vm.Validate());
        Assert.Null(vm.BuildResult());
    }

    /// <summary>고른 항목만 풀 때: 휴지통 칸은 숨기고, 처음 값이 켜져 있어도 답은 '지우지 않음'.</summary>
    [Fact]
    public void 창_VM_고른_항목만이면_휴지통_칸이_없다()
    {
        using var tmp = new TempDir("xvm3");
        var vm = new ExtractOptionsViewModel(new ExtractTargetRequest
        {
            Archives = new[] { tmp.P("a.zip") }, InitialFolder = tmp.Root, SelectedOnly = true, DeleteArchiveAfter = true,
        });
        Assert.False(vm.ShowDeleteOption);
        vm.DeleteArchiveAfter = true;
        Assert.False(vm.BuildResult()!.DeleteArchiveAfter);
    }

    [Fact]
    public void 창_VM_여러_개면_압축마다_폴더()
    {
        using var tmp = new TempDir("xvm4");
        var vm = new ExtractOptionsViewModel(new ExtractTargetRequest
        {
            Archives = new[] { tmp.P("a.zip"), tmp.P("b.7z") }, InitialFolder = tmp.Root, MakeFolder = true,
        });
        Assert.Equal("압축 파일 2개", vm.ArchiveText);
        Assert.Contains("압축 파일마다", vm.MakeFolderLabel);
        Assert.Equal("풀리는 곳: " + Path.Combine(tmp.Root, "(압축 파일 이름)"), vm.PreviewText);
    }

    // ───────────────────────────── 화면 흐름(가짜 창) ─────────────────────────────

    /// <summary>
    /// [압축 풀기] → 풀기 창(처음 값: 압축 파일이 있는 폴더·폴더 만들기 켬) → 답대로 '고른 곳\이름' 에 풀고, 폴더 열기를 골랐으면 그 폴더를 연다.
    /// 다 풀면 고른 곳이 최근 목록 맨 앞, 고른 칸 값이 기억된다(한 번 저장). 다음 창에는 최근 목록이 보이고, 같은 곳에 또 풀면 '이름 (2)'.
    /// </summary>
    [Fact]
    public async Task 풀기_창의_답대로_이름_폴더에_풀고_기억한다()
    {
        var fx = FixtureCatalog.Get("zip_default");
        using var tmp = new TempDir("xflow");
        string work = tmp.Dir("받은 파일");
        string zip = Copy(work, "zip_default", "가.zip");
        string dest = tmp.Dir("풀 곳");
        var ui = new FakeUserInteraction { ExtractTargetAnswer = r => new ExtractTarget(dest, MakeFolder: true, DeleteArchiveAfter: false, OpenFolderAfter: true) };
        var svc = new TestServices();
        var vm = new MainViewModel(ui, null, svc.Services);

        await Run(async () =>
        {
            Assert.True(await vm.OpenArchiveAsync(zip));
            await vm.ExtractCommand.ExecuteAsync(null);
        }, "열기 → 풀기 창 → 풀기");

        var asked = Assert.Single(ui.ExtractTargetPrompts);
        Assert.Equal(new[] { zip }, asked.Archives);
        Assert.Equal(work, asked.InitialFolder);
        Assert.Empty(asked.RecentFolders);
        Assert.True(asked.MakeFolder);
        Assert.False(asked.DeleteArchiveAfter);
        Assert.False(asked.OpenFolderAfter);
        Assert.False(asked.SelectedOnly);

        string made = Path.Combine(dest, "가");
        TreeAssert.ExtractedTreeEquals(made, fx.Expected, context: "가.zip");
        Assert.Equal(new[] { (made, false) }, svc.Shell.OpenedFolders);
        Assert.Empty(ui.Errors);
        Assert.Equal(1, svc.Settings.SaveCount);
        Assert.Equal(new[] { dest }, vm.Settings.Extract.RecentFolders);
        Assert.True(vm.Settings.Extract.DialogOpenFolderAfter);

        // 다음 창: 최근 목록에 그 곳, 폴더 열기는 기억한 값. 같은 곳에 또 풀면 '가 (2)'(합치지 않음)
        await Run(() => vm.ExtractCommand.ExecuteAsync(null), "두 번째 풀기");
        Assert.Equal(2, ui.ExtractTargetPrompts.Count);
        Assert.Equal(new[] { dest }, ui.ExtractTargetPrompts[1].RecentFolders);
        Assert.True(ui.ExtractTargetPrompts[1].OpenFolderAfter);
        TreeAssert.ExtractedTreeEquals(Path.Combine(dest, "가 (2)"), fx.Expected, context: "두 번째");
        Assert.Equal(1, svc.Settings.SaveCount);   // 바뀐 것이 없으면 다시 저장하지 않음
    }

    /// <summary>폴더 만들기를 끄면 고른 곳에 바로 풀고, 끈 것을 기억한다(다음 창의 처음 값).</summary>
    [Fact]
    public async Task 폴더_만들기를_끄면_고른_곳에_바로_풀고_기억한다()
    {
        var fx = FixtureCatalog.Get("zip_default");
        using var tmp = new TempDir("xflat");
        string zip = Copy(tmp.Dir("받은 파일"), "zip_default", "가.zip");
        string dest = tmp.Dir("풀 곳");
        int asked = 0;
        // 처음에는 폴더 만들기를 끄고 풀고, 두 번째 창은 처음 값만 보고 [취소]
        var ui = new FakeUserInteraction { ExtractTargetAnswer = r => ++asked == 1 ? new ExtractTarget(dest, false, false, false) : null };
        var svc = new TestServices();
        var vm = new MainViewModel(ui, null, svc.Services);

        await Run(async () =>
        {
            Assert.True(await vm.OpenArchiveAsync(zip));
            await vm.ExtractCommand.ExecuteAsync(null);
            await vm.ExtractCommand.ExecuteAsync(null);
        }, "폴더 만들기 끄고 풀기");

        TreeAssert.ExtractedTreeEquals(dest, fx.Expected, context: "가.zip");
        Assert.False(vm.Settings.Extract.MakeFolder);
        Assert.False(ui.ExtractTargetPrompts[1].MakeFolder);
        Assert.Empty(svc.Shell.OpenedFolders);
        Assert.Contains("\"makeFolder\": false", svc.Settings.Json);
    }

    /// <summary>창에서 [취소]하면 아무것도 풀지 않고(진행 창도 없음) 설정도 저장하지 않는다.</summary>
    [Fact]
    public async Task 풀기_창을_취소하면_아무것도_안_한다()
    {
        using var tmp = new TempDir("xcancel");
        string zip = Copy(tmp.Dir("받은 파일"), "zip_default", "가.zip");
        string dest = tmp.Dir("풀 곳");
        var ui = new FakeUserInteraction { ExtractTargetAnswer = _ => null, FolderToPick = dest };
        var svc = new TestServices();
        var vm = new MainViewModel(ui, null, svc.Services);

        await Run(async () =>
        {
            Assert.True(await vm.OpenArchiveAsync(zip));
            await vm.ExtractCommand.ExecuteAsync(null);
        }, "풀기 창 취소");

        Assert.Single(ui.ExtractTargetPrompts);
        Assert.Empty(ui.ProgressSessions);
        Assert.Empty(Directory.EnumerateFileSystemEntries(dest));
        Assert.Equal(0, svc.Settings.SaveCount);
    }

    /// <summary>창에서 '다 풀고 휴지통으로'를 켜면(설정은 꺼짐) 이번에만 압축 파일을 휴지통으로 — 설정에는 기억하지 않는다.</summary>
    [Fact]
    public async Task 창에서_켠_휴지통은_이번에만()
    {
        using var tmp = new TempDir("xrecycle");
        string zip = Copy(tmp.Dir("받은 파일"), "zip_default", "가.zip");
        string dest = tmp.Dir("풀 곳");
        var ui = new FakeUserInteraction { ExtractTargetAnswer = r => new ExtractTarget(dest, true, DeleteArchiveAfter: true, OpenFolderAfter: false) };
        var svc = new TestServices();
        var vm = new MainViewModel(ui, null, svc.Services);

        await Run(async () =>
        {
            Assert.True(await vm.OpenArchiveAsync(zip));
            await vm.ExtractCommand.ExecuteAsync(null);
        }, "휴지통 켜고 풀기");

        Assert.Equal(new[] { zip }, svc.RecycleBin.AllSent);
        Assert.False(vm.Settings.Extract.DeleteArchiveAfter);
        Assert.DoesNotContain("\"deleteArchiveAfter\": true", svc.Settings.Json);
        Assert.Null(vm.CurrentArchivePath);   // 지운 압축은 계속 보여 주지 않음(W2-05 규칙 그대로)
    }

    /// <summary>
    /// 고른 항목만 풀기: 창에 '고른 항목만'으로 넘기고 휴지통 처음 값은 꺼짐(설정이 켜져 있어도) — 풀어도 압축 파일은 지우지 않는다.
    /// 폴더 만들기를 고르면 '고른 곳\이름' 에 그 항목만.
    /// </summary>
    [Fact]
    public async Task 고른_항목만_풀기도_풀기_창으로()
    {
        using var tmp = new TempDir("xsel");
        string zip = Copy(tmp.Dir("받은 파일"), "zip_default", "가.zip");
        string dest = tmp.Dir("풀 곳");
        var ui = new FakeUserInteraction { ExtractTargetAnswer = r => new ExtractTarget(dest, true, r.DeleteArchiveAfter, false) };
        var svc = TestServices.With(e => e.DeleteArchiveAfter = true);
        var vm = new MainViewModel(ui, null, svc.Services);

        await Run(async () =>
        {
            Assert.True(await vm.OpenArchiveAsync(zip));
            var one = vm.Entries.First(e => e.Path == "일집_샘플/견적서_2026.txt");
            await vm.ExtractSelectedAsync(new[] { one });
        }, "고른 항목 풀기");

        var asked = Assert.Single(ui.ExtractTargetPrompts);
        Assert.True(asked.SelectedOnly);
        Assert.False(asked.DeleteArchiveAfter);
        Assert.Equal(new[] { "일집_샘플/견적서_2026.txt" }, TreeAssert.Files(Path.Combine(dest, "가")));
        Assert.Empty(svc.RecycleBin.Sent);
    }

    /// <summary>
    /// 폴더 만들기로 풀다가 암호 창을 [취소]하면: 새로 만든 '이름' 폴더를 남기지 않고(빈 폴더), 최근 목록에도 넣지 않는다(다 풀지 못함).
    /// 고른 '폴더 만들기' 값은 기억한다(창에서 고른 것).
    /// </summary>
    [Fact]
    public async Task 못_풀면_만든_폴더를_지우고_최근에_넣지_않는다()
    {
        using var tmp = new TempDir("xfail");
        string zip = Copy(tmp.Dir("받은 파일"), "zip_zipcrypto", "암호.zip");
        string dest = tmp.Dir("풀 곳");
        var ui = new FakeUserInteraction { ExtractTargetAnswer = r => new ExtractTarget(dest, true, false, true) };   // 암호 창 답 없음 = 취소
        var svc = new TestServices();
        var vm = new MainViewModel(ui, null, svc.Services);

        await Run(async () =>
        {
            Assert.True(await vm.OpenArchiveAsync(zip));
            await vm.ExtractCommand.ExecuteAsync(null);
        }, "암호 취소");

        Assert.NotEmpty(ui.PasswordPrompts);
        Assert.Empty(Directory.EnumerateFileSystemEntries(dest));
        Assert.Empty(vm.Settings.Extract.RecentFolders);
        Assert.Empty(svc.Shell.OpenedFolders);
        Assert.True(vm.Settings.Extract.DialogOpenFolderAfter);
    }

    // ───────────────────────────── '잠깐 두는' 폴더(W2-09 검증 지적) ─────────────────────────────

    /// <summary>
    /// 압축 안의 압축(일집 임시 폴더에 꺼내 연 것)을 [압축 풀기] 하면 처음 풀 곳은 바탕 화면 — 예전엔 일집 임시 폴더가 처음 값이라 그대로 [풀기] 하면
    /// 그 안에 풀려 일집을 닫을 때 말없이 지워졌다. 창의 처음 값 그대로 풀어도 일집을 닫은 뒤에 파일이 남는다. 최근 푼 곳에는 바탕 화면이 들어간다.
    /// </summary>
    [Fact]
    public async Task 압축_안의_압축은_일집_임시_폴더_대신_바탕_화면에_권한다()
    {
        using var tmp = new TempDir("xnested");
        string innerSrc = tmp.P("inner_src.zip");
        RawZip.Write(innerSrc, RawZip.Utf8("중요한 결과.txt", "내용"));
        string outer = Path.Combine(tmp.Dir("받은 메일"), "outer.zip");
        RawZip.Write(outer, new RawZip.Item(Encoding.UTF8.GetBytes("inner.zip"), File.ReadAllBytes(innerSrc), true));
        string desktop = tmp.Dir("바탕 화면");
        var ui = new FakeUserInteraction { ExtractTargetAnswer = r => new ExtractTarget(r.InitialFolder, r.MakeFolder, false, r.OpenFolderAfter) };   // 처음 값 그대로 [풀기]
        var svc = new TestServices(tempLocations: new TempLocations(Array.Empty<string>(), desktop));
        var vm = new MainViewModel(ui, null, svc.Services);
        string? innerTemp = null;

        await Run(async () =>
        {
            Assert.True(await vm.OpenArchiveAsync(outer));
            Assert.True(await vm.OpenEntryAsync(vm.Entries.First(e => e.Path == "inner.zip")));
            innerTemp = svc.Shell.OpenedFiles.Last();
            Assert.True(TempLocations.IsIljipTemp(innerTemp));
            Assert.True(await vm.OpenArchiveAsync(innerTemp));   // 파일 연결로 일집이 그 파일을 연 것처럼
            await vm.ExtractCommand.ExecuteAsync(null);
        }, "압축 안의 압축 풀기");

        var asked = Assert.Single(ui.ExtractTargetPrompts);
        Assert.Equal(desktop, asked.InitialFolder);
        Assert.Empty(ui.Errors);
        vm.Shutdown();   // 일집을 닫음 — 작업 폴더(임시)가 지워진다
        Assert.False(File.Exists(innerTemp));
        Assert.Equal(new[] { "중요한 결과.txt" }, TreeAssert.Files(Path.Combine(desktop, "inner")));
        Assert.Equal(new[] { desktop }, vm.Settings.Extract.RecentFolders);
    }

    /// <summary>
    /// Windows 임시 폴더(%TEMP%·Outlook 첨부의 INetCache)의 압축: 처음 풀 곳은 바탕 화면, 바탕 화면이 없으면 최근에 푼 곳, 그것도 없으면 빈 칸([찾아보기]로 고름).
    /// 그런 곳에 풀었어도 최근 푼 곳으로 기억하지 않고, 예전에 기억된 그런 곳도 목록에 보이지 않는다.
    /// </summary>
    [Fact]
    public async Task 임시_폴더의_압축은_바탕_화면을_권하고_임시_폴더는_기억하지_않는다()
    {
        using var tmp = new TempDir("xsystemp");
        string systemTemp = tmp.Dir("시스템 임시");
        string outlook = tmp.Dir("시스템 임시", "Content.Outlook", "AB12CD34");
        string zip = SmallZip(outlook, "첨부.zip", "가.txt");
        string desktop = tmp.Dir("바탕 화면");
        string recent = tmp.Dir("최근");
        var settings = new AppSettings();
        settings.Extract.RecentFolders = new List<string> { systemTemp, recent };   // 예전에 임시 폴더에 푼 기록
        var ui = new FakeUserInteraction { ExtractTargetAnswer = r => new ExtractTarget(outlook, false, false, false) };   // 그래도 임시 폴더에 풀기를 고름

        foreach (var (desk, expected) in new[] { (desktop, desktop), ((string?)null, recent) })
        {
            ui.ExtractTargetPrompts.Clear();
            var svc = new TestServices(settings, new TempLocations(new[] { systemTemp }, desk));
            var vm = new MainViewModel(ui, null, svc.Services);
            await Run(async () =>
            {
                Assert.True(await vm.OpenArchiveAsync(zip));
                await vm.ExtractCommand.ExecuteAsync(null);
            }, "임시 폴더의 압축 풀기");
            var asked = Assert.Single(ui.ExtractTargetPrompts);
            Assert.Equal(expected, asked.InitialFolder);
            Assert.Equal(new[] { recent }, asked.RecentFolders);
            Assert.Contains("가.txt", TreeAssert.Files(outlook));
            Assert.DoesNotContain(vm.Settings.Extract.RecentFolders, f => f.StartsWith(outlook, StringComparison.OrdinalIgnoreCase));
            File.Delete(Path.Combine(outlook, "가.txt"));
        }

        // 바탕 화면도 최근도 없으면 빈 칸 — 창이 '풀 곳(폴더)을 골라 주세요.'
        var empty = new TestServices(null, new TempLocations(new[] { systemTemp }, null));
        ui.ExtractTargetPrompts.Clear();
        ui.ExtractTargetAnswer = _ => null;
        var vm2 = new MainViewModel(ui, null, empty.Services);
        await Run(async () =>
        {
            Assert.True(await vm2.OpenArchiveAsync(zip));
            await vm2.ExtractCommand.ExecuteAsync(null);
        }, "빈 칸");
        var none = Assert.Single(ui.ExtractTargetPrompts);
        Assert.Equal(string.Empty, none.InitialFolder);
        Assert.Equal("풀 곳(폴더)을 골라 주세요.", new ExtractOptionsViewModel(none).Validate());
    }

    /// <summary>창에서 일집 임시 폴더(일집을 닫으면 지워짐)를 골라도 풀지 않고 쉬운 말로 알린다.</summary>
    [Fact]
    public void 창_VM_일집_임시_폴더에는_풀지_않는다()
    {
        string inTemp = Path.Combine(AppPaths.TempRoot, "open", "abcd", "1");
        Directory.CreateDirectory(inTemp);
        foreach (var folder in new[] { inTemp, AppPaths.TempRoot, AppPaths.TempRoot + @"\" })
        {
            var vm = new ExtractOptionsViewModel(new ExtractTargetRequest { Archives = new[] { Path.Combine(inTemp, "inner.zip") }, InitialFolder = folder });
            Assert.Equal(ExtractOptionsViewModel.TempFolderMessage, vm.Validate());
            Assert.Null(vm.BuildResult());
            Assert.Equal(string.Empty, vm.PreviewText);
        }
    }

    /// <summary>
    /// 네트워크 폴더(NAS·공유 폴더 '\\서버\공유')는 창이 열릴 때(최근 푼 곳)·칠 때(미리 보기) 디스크를 보지 않는다(W2-09 검증 지적 — 끊긴 NAS 면 창이 몇 초씩 굳음).
    /// 없는 이름의 서버라도 목록에 보이고 미리 보기가 나온다(있는지 보러 갔으면 '없음'이라 빠졌을 것). 있는지는 [풀기] 때 정해진 시간까지만 기다려 본다.
    /// </summary>
    [Fact]
    public void 네트워크_폴더는_창을_열거나_칠_때_디스크를_보지_않는다()
    {
        const string nas = @"\\iljip-test-nas-없음\공유\받은 파일";
        Assert.True(FolderProbe.IsNetwork(nas));
        Assert.True(FolderProbe.IsNetwork(@"\\?\UNC\server\share"));
        Assert.True(FolderProbe.IsNetwork("//server/share"));
        Assert.False(FolderProbe.IsNetwork(Path.GetTempPath()));
        Assert.False(FolderProbe.IsNetwork(@"\\?\C:\Windows"));
        Assert.False(FolderProbe.IsNetwork("받은 파일"));

        using var tmp = new TempDir("xnas");
        string local = tmp.Dir("로컬");
        var x = new ExtractSettings { RecentFolders = new List<string> { nas, local, tmp.P("없음") } };
        var sw = System.Diagnostics.Stopwatch.StartNew();
        Assert.Equal(new[] { nas, local }, x.VisibleRecentFolders());

        var vm = new ExtractOptionsViewModel(new ExtractTargetRequest { Archives = new[] { Path.Combine(local, "보고서.zip") }, InitialFolder = nas, MakeFolder = true });
        Assert.Equal("풀리는 곳: " + Path.Combine(nas, "보고서"), vm.PreviewText);
        vm.MakeFolder = false;
        Assert.Equal("풀리는 곳: " + nas, vm.PreviewText);
        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(2), $"네트워크를 본 것 같음: {sw.Elapsed}");

        // 로컬 폴더는 예전처럼 바로 본다(없으면 미리 보기 없음)
        vm.Folder = tmp.P("없음");
        Assert.Equal(string.Empty, vm.PreviewText);
        Assert.False(FolderProbe.Exists(tmp.P("없음")));
        Assert.True(FolderProbe.Exists(local));
    }

    /// <summary>명령줄 '--extract a b'(여러 개): 풀기 창 한 번 — 폴더 만들기를 끄면 고른 곳에 모두 바로, 폴더 열기를 고르면 고른 곳을 한 번 연다.</summary>
    [Fact]
    public async Task 명령줄_여러_개도_풀기_창_폴더_만들기_끄면_한_곳에()
    {
        using var tmp = new TempDir("xcli");
        string work = tmp.Dir("받은 파일");
        string a = SmallZip(work, "a.zip", "가.txt");
        string b = SmallZip(work, "b.zip", "나.txt");
        string dest = tmp.Dir("풀 곳");
        var ui = new FakeUserInteraction { ExtractTargetAnswer = r => new ExtractTarget(dest, false, false, true) };
        var svc = new TestServices();
        var vm = new MainViewModel(ui, null, svc.Services);

        await Run(() => vm.ProcessCommandLineAsync(new[] { "--extract", a, b }), "--extract a b");

        var asked = Assert.Single(ui.ExtractTargetPrompts);
        Assert.Equal(new[] { a, b }, asked.Archives);
        Assert.Equal(new[] { "가.txt", "나.txt" }, TreeAssert.Files(dest));
        Assert.Equal(new[] { (dest, false) }, svc.Shell.OpenedFolders);
        Assert.Empty(ui.Summaries);   // 모두 풀었고 폴더를 열었으면 요약 창은 띄우지 않음
        Assert.Equal(new[] { dest }, vm.Settings.Extract.RecentFolders);
    }

    /// <summary>명령줄 '--extract a b' + 폴더 만들기: 고른 곳 안의 '이름' 폴더마다.</summary>
    [Fact]
    public async Task 명령줄_여러_개_폴더_만들기면_이름_폴더마다()
    {
        using var tmp = new TempDir("xcli2");
        string work = tmp.Dir("받은 파일");
        string a = SmallZip(work, "a.zip", "가.txt");
        string b = SmallZip(work, "b.zip", "가.txt");
        string dest = tmp.Dir("풀 곳");
        var ui = new FakeUserInteraction { ExtractTargetAnswer = r => new ExtractTarget(dest, true, false, false) };
        var svc = new TestServices();
        var vm = new MainViewModel(ui, null, svc.Services);

        await Run(() => vm.ProcessCommandLineAsync(new[] { "--extract", a, b }), "--extract a b");

        Assert.Equal(new[] { "a/가.txt", "b/가.txt" }, TreeAssert.Files(dest));
        Assert.Empty(svc.Shell.OpenedFolders);
        Assert.Single(ui.Summaries);
    }

    /// <summary>명령줄 '--extract a'(하나): 열고 풀기 창 → 답대로.</summary>
    [Fact]
    public async Task 명령줄_하나도_풀기_창으로()
    {
        var fx = FixtureCatalog.Get("zip_default");
        using var tmp = new TempDir("xcli1");
        string zip = Copy(tmp.Dir("받은 파일"), "zip_default", "가.zip");
        string dest = tmp.Dir("풀 곳");
        var ui = new FakeUserInteraction { ExtractTargetAnswer = r => new ExtractTarget(dest, true, false, false) };
        var vm = new MainViewModel(ui, null, new TestServices().Services);

        await Run(() => vm.ProcessCommandLineAsync(new[] { "--extract", zip }), "--extract a");

        Assert.Single(ui.ExtractTargetPrompts);
        TreeAssert.ExtractedTreeEquals(Path.Combine(dest, "가"), fx.Expected, context: "--extract");
    }
}

/// <summary>[W2-09 · I2-13] 풀기 창 XAML — 띄우지 않고 만들기만(STA, <see cref="WpfAppScope"/>). 앱 객체를 잠깐 만들어 다른 시험과 따로(순차) 돈다.</summary>
[Collection(WpfApplicationCollection.Name)]
public class ExtractDialogXamlTests
{
    /// <summary>풀기 창이 읽히고 VM 과 양쪽으로 묶인다(풀 곳·폴더 만들기·휴지통·폴더 열기·미리 보기). 고른 항목만이면 휴지통 칸이 숨는다. 묶기 오류 0.</summary>
    [Fact]
    public void 풀기_창이_읽히고_VM과_양쪽으로_묶인다()
    {
        using var tmp = new TempDir("xxaml");
        string work = tmp.Dir("받은 파일");
        string other = tmp.Dir("다른 곳");
        var request = new ExtractTargetRequest
        {
            Archives = new[] { Path.Combine(work, "보고서.zip") },
            InitialFolder = work,
            RecentFolders = new[] { other },
            MakeFolder = true,
        };
        WpfAppScope.Run(log =>
        {
            var vm = new ExtractOptionsViewModel(request);
            var w = new ExtractOptionsDialog(vm);
            try
            {
                WpfAppScope.Pump();
                Assert.Equal(work, w.FolderCombo.Text);
                Assert.Equal(new[] { other }, w.FolderCombo.Items.Cast<string>());
                Assert.True(w.MakeFolderCheck.IsChecked);
                Assert.Equal(Visibility.Visible, w.DeleteArchiveCheck.Visibility);
                Assert.False(w.DeleteArchiveCheck.IsChecked);
                Assert.False(w.OpenFolderCheck.IsChecked);
                Assert.Equal("풀리는 곳: " + Path.Combine(work, "보고서"), w.PreviewTextBlock.Text);

                // 칸 → VM
                w.FolderCombo.Text = other;
                w.MakeFolderCheck.IsChecked = false;
                w.OpenFolderCheck.IsChecked = true;
                w.DeleteArchiveCheck.IsChecked = true;
                WpfAppScope.Pump();
                Assert.Equal(other, vm.Folder);
                Assert.False(vm.MakeFolder);
                Assert.Equal("풀리는 곳: " + other, w.PreviewTextBlock.Text);
                Assert.Equal(new ExtractTarget(other, false, true, true), vm.BuildResult());
                Assert.Empty(log.Errors);
            }
            finally
            {
                w.Close();
            }

            var sel = new ExtractOptionsDialog(new ExtractOptionsViewModel(request with { SelectedOnly = true }));
            try
            {
                WpfAppScope.Pump();
                Assert.Equal(Visibility.Collapsed, sel.DeleteArchiveCheck.Visibility);
                Assert.Empty(log.Errors);
            }
            finally
            {
                sel.Close();
            }
        });
    }
}
