using Iljip.Models;
using Iljip.Services;
using Iljip.Tests.TestSupport;
using Iljip.ViewModels;

namespace Iljip.Tests.Contract;

/// <summary>
/// [W2-11 I3-15] 최근 연 압축 파일(설정 recent.files — <see cref="RecentFilesService"/>): 열 때마다 맨 앞, 같은 파일(대소문자 무시)은 하나, 최대 10개,
/// 없는 파일은 '없음', 임시 폴더 안은 넣지 않음, 저장·복원(창을 닫을 때 한 번 — 창 두 개면 합침). 설정 저장은 가짜(<see cref="InMemorySettingsStore"/>).
/// </summary>
public class RecentFilesTests
{
    private static RecentFilesService New(AppSettings? s = null, Func<string, bool>? excluded = null, FakeClock? clock = null) =>
        new(s ?? new AppSettings(), excluded ?? (_ => false), clock ?? new FakeClock());

    private static string P(string name) => Path.Combine(@"C:\받은 파일", name);

    [Fact]
    public void 넣으면_맨_앞이고_같은_파일은_하나()
    {
        var clock = new FakeClock().SetLocal(2026, 10, 14, 9);
        var r = New(clock: clock);
        Assert.True(r.Add(P("가.zip")));
        clock.UtcNow = clock.UtcNow.AddMinutes(1);
        Assert.True(r.Add(P("나.7z")));
        clock.UtcNow = clock.UtcNow.AddMinutes(1);
        Assert.True(r.Add(P("가.ZIP").ToUpperInvariant()));   // 대소문자만 다른 같은 파일 → 앞으로, 하나만

        Assert.Equal(2, r.Files.Count);
        Assert.Equal(P("가.ZIP").ToUpperInvariant(), r.Files[0].Path);
        Assert.Equal(P("나.7z"), r.Files[1].Path);
        Assert.Equal(clock.UtcNow, r.Files[0].LastOpenedUtc);
        Assert.Equal(new[] { "가.ZIP".ToUpperInvariant(), "나.7z" }, r.Items(_ => true).Select(i => i.Name));
    }

    [Fact]
    public void 최대_10개_가장_오래된_것이_빠진다()
    {
        var r = New();
        for (int i = 1; i <= 12; i++) r.Add(P($"{i}.zip"));
        Assert.Equal(RecentFilesService.MaxFiles, r.Files.Count);
        Assert.Equal(10, RecentFilesService.MaxFiles);
        Assert.Equal(P("12.zip"), r.Files[0].Path);
        Assert.Equal(P("3.zip"), r.Files[^1].Path);
        Assert.DoesNotContain(r.Files, f => f.Path == P("1.zip") || f.Path == P("2.zip"));
    }

    /// <summary>없는 파일은 '없음'(IsMissing — 화면이 흐리게)으로 남고, 빼기·모두 지우기로만 사라진다. 상대 경로는 전체 경로로.</summary>
    [Fact]
    public void 없는_파일은_없음으로_보이고_빼면_사라진다()
    {
        using var t = new TempDir("rcmiss");
        string have = Path.Combine(t.Root, "있음.zip");
        File.WriteAllText(have, "x");
        string gone = Path.Combine(t.Root, "없음.zip");
        var r = New();
        r.Add(gone);
        r.Add(have);

        var items = r.Items();
        Assert.Equal(new[] { true, false }, items.Select(i => i.Exists));
        Assert.True(items[1].IsMissing);
        Assert.Equal("없음.zip", items[1].Name);
        Assert.Equal(t.Root, items[1].Folder);

        Assert.True(r.Remove(gone));
        Assert.False(r.Remove(gone));
        Assert.Equal(new[] { have }, r.Files.Select(f => f.Path));
        Assert.True(r.Clear());
        Assert.Empty(r.Files);
        Assert.False(r.Clear());
    }

    /// <summary>임시 폴더(일집 temp\open 등) 안의 압축은 넣지 않는다 — 기본은 일집 임시 폴더, 화면은 Windows 임시 폴더·Outlook 첨부 임시 폴더까지(TempLocations).</summary>
    [Fact]
    public void 임시_폴더_안은_넣지_않는다()
    {
        var defaults = new RecentFilesService(new AppSettings(), null, new FakeClock());
        string inner = Path.Combine(AppPaths.TempRoot, "open", "0123456789abcdef", "1", "안의 압축.zip");
        Assert.False(defaults.Add(inner));
        Assert.Empty(defaults.Files);
        Assert.True(defaults.Add(P("보통.zip")));

        var temp = new TempLocations(new[] { @"C:\Users\나\AppData\Local\Temp" }, desktop: null);
        var r = New(excluded: temp.IsTemporary);
        Assert.False(r.Add(@"C:\Users\나\AppData\Local\Temp\메일 첨부.zip"));
        Assert.True(r.Add(P("보통.zip")));
        Assert.Single(r.Files);
    }

    /// <summary>저장과 복원: 설정 JSON(recent.files — 경로·마지막으로 연 시각)으로 왕복. 암호 같은 것은 없다.</summary>
    [Fact]
    public void 저장과_복원()
    {
        var store = new InMemorySettingsStore();
        var s = store.Load();
        var clock = new FakeClock().SetLocal(2026, 10, 14, 9);
        var r = New(s, clock: clock);
        r.Add(P("가.zip"));
        r.Add(P("나.7z"));
        store.Save(s);

        Assert.Contains("\"recent\"", store.Json);
        Assert.Contains("\"files\"", store.Json);
        Assert.Contains("\"lastOpenedUtc\"", store.Json);
        var back = new RecentFilesService(store.Load(), _ => false, clock);
        Assert.Equal(new[] { P("나.7z"), P("가.zip") }, back.Files.Select(f => f.Path));
        Assert.Equal(clock.UtcNow, back.Files[0].LastOpenedUtc);

        // 손으로 고친 설정(빈 경로·null)은 버린다
        var odd = new InMemorySettingsStore();
        odd.Save(new AppSettings());
        var raw = System.Text.Json.Nodes.JsonNode.Parse(odd.Json!)!;
        raw["recent"] = System.Text.Json.Nodes.JsonNode.Parse("{\"files\":[{\"path\":\"\"},null,{\"path\":\"C:\\\\a.zip\"}]}");
        var fixedUp = (System.Text.Json.JsonSerializer.Deserialize<AppSettings>(raw.ToJsonString(), JsonSettingsStore.Options)!).Normalize();
        Assert.Equal(new[] { @"C:\a.zip" }, fixedUp.Recent.Files.Select(f => f.Path));
    }

    /// <summary>
    /// 창 두 개: 닫는 창이 저장할 때 저장된 목록(다른 창이 넣은 것)과 합친다 — 마지막으로 연 시각 순, 이 실행에서 뺀 것은 빼고, 최대 10개.
    /// </summary>
    [Fact]
    public void 합치기는_다른_창이_넣은_것을_지우지_않는다()
    {
        var t0 = new DateTime(2026, 10, 14, 0, 0, 0, DateTimeKind.Utc);
        RecentFile F(string n, int min) => new() { Path = P(n), LastOpenedUtc = t0.AddMinutes(min) };
        var mine = new[] { F("내것.zip", 5), F("둘다.zip", 1) };
        var latest = new[] { F("다른창.zip", 3), F("둘다.zip", 4), F("뺀것.zip", 2) };
        var merged = RecentFilesService.Merge(mine, latest, new[] { P("뺀것.zip") });
        Assert.Equal(new[] { P("내것.zip"), P("둘다.zip"), P("다른창.zip") }, merged.Select(f => f.Path));
        Assert.Equal(t0.AddMinutes(4), merged[1].LastOpenedUtc);   // 더 늦게 연 시각

        var many = Enumerable.Range(0, 15).Select(i => F($"{i}.zip", i)).ToList();
        Assert.Equal(10, RecentFilesService.Merge(many.Take(7), many.Skip(7), Array.Empty<string>()).Count);
    }

    // ───────────────────────────── 화면(MainViewModel) ─────────────────────────────

    private static string CopyFixture(string dir, string id)
    {
        var fx = FixtureCatalog.Get(id);
        string dst = Path.Combine(dir, Path.GetFileName(fx.FullPath));
        File.Copy(fx.FullPath, dst);
        return dst;
    }

    /// <summary>
    /// 압축을 열면 최근 목록 맨 앞에(메모리 — 열 때마다 저장하지 않음), 창을 닫을 때(<see cref="MainViewModel.SaveOnExit"/>) 한 번 저장하고, 다음 실행에서 보인다.
    /// 바뀐 것이 없으면 닫을 때 저장하지 않는다.
    /// </summary>
    [Fact]
    public async Task 열면_기록하고_닫을_때_한번_저장하고_다음에_보인다()
    {
        using var t = new TempDir("rcvm");
        string a = CopyFixture(t.Root, "zip_default");
        string b = CopyFixture(t.Root, "7z_default");
        var svc = new TestServices();
        var vm = new MainViewModel(new FakeUserInteraction(), null, svc.Services);

        await VmRunner.Run(() => vm.OpenArchiveAsync(a), "열기 a");
        await VmRunner.Run(() => vm.OpenArchiveAsync(b), "열기 b");
        Assert.Equal(new[] { b, a }, vm.RecentFiles.Select(i => i.Path));
        Assert.Equal(0, svc.Settings.SaveCount);

        vm.SaveOnExit(double.NaN);
        Assert.Equal(1, svc.Settings.SaveCount);
        vm.SaveOnExit(double.NaN);
        Assert.Equal(1, svc.Settings.SaveCount);   // 그 뒤 바뀐 것이 없으면 저장하지 않음

        var next = new MainViewModel(new FakeUserInteraction(), null, svc.Services);
        Assert.Empty(next.RecentFiles);
        next.RefreshRecentFiles();
        Assert.Equal(new[] { b, a }, next.RecentFiles.Select(i => i.Path));
        Assert.True(next.HasRecentFiles);
    }

    /// <summary>닫을 때 트리 너비와 최근 파일이 함께 바뀌었어도 저장은 한 번(DLP 연속 저장 경합 교훈).</summary>
    [Fact]
    public async Task 닫을_때_트리_너비와_최근_파일을_한_번에_저장()
    {
        using var t = new TempDir("rcone");
        string a = CopyFixture(t.Root, "zip_default");
        var svc = new TestServices();
        var vm = new MainViewModel(new FakeUserInteraction(), null, svc.Services);
        await VmRunner.Run(() => vm.OpenArchiveAsync(a), "열기");

        vm.SaveOnExit(333);
        Assert.Equal(1, svc.Settings.SaveCount);
        var saved = svc.Settings.Load();
        Assert.Equal(333, saved.Ui.TreeWidth);
        Assert.Equal(a, Assert.Single(saved.Recent.Files).Path);
    }

    /// <summary>창 두 개: 먼저 닫은 창이 저장한 최근 파일을 나중에 닫는 창이 지우지 않는다(합침).</summary>
    [Fact]
    public async Task 창_두_개면_닫을_때_합친다()
    {
        using var t = new TempDir("rctwo");
        string a = CopyFixture(t.Root, "zip_default");
        string b = CopyFixture(t.Root, "7z_default");
        var svc = new TestServices();
        var first = new MainViewModel(new FakeUserInteraction(), null, svc.Services);
        var second = new MainViewModel(new FakeUserInteraction(), null, svc.Services);
        _ = first.Settings;
        _ = second.Settings;   // 둘 다 빈 목록을 읽어 둔 상태

        await VmRunner.Run(() => first.OpenArchiveAsync(a), "첫 창 열기");
        await VmRunner.Run(() => second.OpenArchiveAsync(b), "둘째 창 열기");
        first.SaveOnExit(double.NaN);
        second.SaveOnExit(double.NaN);

        Assert.Equal(new[] { b, a }.OrderBy(p => p), svc.Settings.Load().Recent.Files.Select(f => f.Path).OrderBy(p => p));
    }

    /// <summary>일집 임시 폴더(압축 안의 압축) 안의 압축을 열면 기록하지 않는다.</summary>
    [Fact]
    public async Task 일집_임시_폴더의_압축은_기록하지_않는다()
    {
        string dir = Path.Combine(AppPaths.TempRoot, "open", Guid.NewGuid().ToString("N")[..16], "1");
        Directory.CreateDirectory(dir);
        try
        {
            string inner = CopyFixture(dir, "zip_default");
            var svc = new TestServices();
            var vm = new MainViewModel(new FakeUserInteraction(), null, svc.Services);
            await VmRunner.Run(() => vm.OpenArchiveAsync(inner), "열기");
            Assert.True(vm.IsArchiveMode);
            Assert.Empty(vm.RecentFiles);
            vm.SaveOnExit(double.NaN);
            Assert.Equal(0, svc.Settings.SaveCount);
        }
        finally
        {
            try { Directory.Delete(Path.GetDirectoryName(dir)!, recursive: true); } catch { /* 무시 */ }
        }
    }

    // ───────────────────────────── W2-11 검증 지적: 창 두 개(새 창) — 저장마다 최신 설정에 바꾼 것만 얹기 ─────────────────────────────
    // 두 VM 이 같은 설정 저장소를 함께 쓴다(같은 settings.json 을 쓰는 일집 프로세스 둘 흉내). 둘 다 켜질 때 설정을 읽어 둔다.

    private static (MainViewModel First, MainViewModel Second) TwoWindows(TestServices svc, FakeUserInteraction? ui1 = null, FakeUserInteraction? ui2 = null)
    {
        var first = new MainViewModel(ui1 ?? new FakeUserInteraction(), null, svc.Services);
        var second = new MainViewModel(ui2 ?? new FakeUserInteraction(), null, svc.Services);
        _ = first.Settings;
        _ = second.Settings;
        return (first, second);
    }

    /// <summary>
    /// 검증 지적 재현(A18): 첫 창을 닫아 최근 파일을 저장한 뒤, 먼저 켜 둔 둘째 창이 풀기 창으로 풀어(최근 푼 곳 저장) 설정을 저장해도
    /// 첫 창의 최근 파일이 지워지지 않는다 — 둘째 창이 연 것도 그때 함께 저장되고, 둘째 창을 닫아도 둘 다 남는다.
    /// </summary>
    [Fact]
    public async Task 다른_창이_풀기_창으로_저장해도_먼저_닫은_창의_최근_파일이_남는다()
    {
        using var t = new TempDir("rc2ext");
        string a = CopyFixture(t.Root, "zip_default");
        string b = CopyFixture(t.Root, "7z_default");
        string dest = t.Dir("풀 곳");
        var svc = new TestServices();
        var ui2 = new FakeUserInteraction { ExtractTargetAnswer = _ => W211.Into(dest, makeFolder: true) };
        var (first, second) = TwoWindows(svc, ui2: ui2);

        await VmRunner.Run(() => first.OpenArchiveAsync(a), "첫 창 열기");
        first.SaveOnExit(double.NaN);   // 첫 창을 닫음

        await VmRunner.Run(() => second.OpenArchiveAsync(b), "둘째 창 열기");
        await VmRunner.Run(() => second.ExtractCommand.ExecuteAsync(null), "둘째 창 풀기 창으로 풀기");
        Assert.Empty(ui2.Errors);
        var mid = svc.Settings.Load();
        Assert.Equal(new[] { a, b }.OrderBy(p => p), mid.Recent.Files.Select(f => f.Path).OrderBy(p => p));
        Assert.Equal(dest, mid.Extract.RecentFolders[0]);
        Assert.Equal(new[] { a, b }.OrderBy(p => p), second.RecentFiles.Select(i => i.Path).OrderBy(p => p));   // 저장한 뒤 다른 창이 연 것도 보임

        second.SaveOnExit(double.NaN);
        var end = svc.Settings.Load();
        Assert.Equal(new[] { a, b }.OrderBy(p => p), end.Recent.Files.Select(f => f.Path).OrderBy(p => p));
        Assert.Equal(dest, end.Extract.RecentFolders[0]);
    }

    /// <summary>
    /// 검증 지적 재현(A1): 둘째 창의 ☰ ▸ 설정에서 바꾼 값(같은 이름 처리·풀고 나서 지우기)을 압축을 연 첫 창이 닫으며 옛 값으로 되돌리지 않는다.
    /// 첫 창이 바꾼 것(트리 너비·최근 파일)은 함께 저장된다.
    /// </summary>
    [Fact]
    public async Task 다른_창이_설정_창에서_바꾼_값을_닫는_창이_되돌리지_않는다()
    {
        using var t = new TempDir("rc2set");
        string a = CopyFixture(t.Root, "zip_default");
        var svc = new TestServices();
        var (first, second) = TwoWindows(svc);

        await VmRunner.Run(() => first.OpenArchiveAsync(a), "첫 창 열기");
        var changed = second.Settings.Clone();
        changed.Extract.OnCollision = CollisionPolicy.Overwrite;
        changed.Extract.DeleteArchiveAfter = true;
        second.ApplySettings(changed);

        first.SaveOnExit(333);
        var saved = svc.Settings.Load();
        Assert.Equal(CollisionPolicy.Overwrite, saved.Extract.OnCollision);
        Assert.True(saved.Extract.DeleteArchiveAfter);
        Assert.Equal(333, saved.Ui.TreeWidth);
        Assert.Equal(a, Assert.Single(saved.Recent.Files).Path);
        Assert.Equal(CollisionPolicy.Ask, first.Settings.Extract.OnCollision);   // 첫 창의 메모리는 그대로(다음에 켤 때부터)
    }

    /// <summary>압축 창의 '마지막 값' 기억(첫 창)도 다른 창이 설정 창에서 바꾼 값을 되돌리지 않는다.</summary>
    [Fact]
    public async Task 압축_창_기억도_다른_창의_설정을_되돌리지_않는다()
    {
        using var t = new TempDir("rc2cmp");
        string src = Path.Combine(t.Dir("자료"), "보고서.txt");
        File.WriteAllText(src, "내용");
        string outDir = t.Dir("만든 곳");
        var svc = new TestServices();
        var ui1 = new FakeUserInteraction
        {
            CompressionRequestAnswer = r => new CompressionRequest
            {
                Sources = r.Sources,
                OutputFolder = outDir,
                BaseName = "보고서",
                Format = CompressionFormat.Zip,
                Options = new CompressionOptions { Level = CompressionLevelOption.Maximum },
            },
        };
        var (first, second) = TwoWindows(svc, ui1: ui1);

        var changed = second.Settings.Clone();
        changed.Extract.DeleteArchiveAfter = true;
        changed.Compress.DeleteOriginalsAfter = true;
        second.ApplySettings(changed);

        first.AddToStaging(new[] { src });
        await VmRunner.Run(() => first.CompressCommand.ExecuteAsync(null), "첫 창 압축");

        Assert.Empty(ui1.Errors);
        var saved = svc.Settings.Load();
        Assert.True(saved.Extract.DeleteArchiveAfter);
        Assert.True(saved.Compress.DeleteOriginalsAfter);
        Assert.Equal(outDir, saved.Compress.LastFolder);
        Assert.Equal(CompressionLevelOption.Maximum, saved.Compress.Level);
    }

    /// <summary>한 창에서 목록에서 뺀 최근 파일을, 그 전에 켜 둔 다른 창이 닫으며 되살리지 않는다(그 창이 연 것만 얹음).</summary>
    [Fact]
    public async Task 다른_창에서_뺀_최근_파일을_닫는_창이_되살리지_않는다()
    {
        using var t = new TempDir("rc2rm");
        string x = CopyFixture(t.Root, "zip_default");
        string y = CopyFixture(t.Root, "7z_default");
        var s = new AppSettings();
        s.Recent.Files.Add(new RecentFile { Path = x, LastOpenedUtc = DateTime.UtcNow.AddDays(-1) });
        var svc = new TestServices(s);
        var (first, second) = TwoWindows(svc);
        first.RefreshRecentFiles();
        second.RefreshRecentFiles();

        first.RemoveRecentCommand.Execute(first.RecentFiles[0]);   // 첫 창에서 x 를 뺌(바로 저장)
        await VmRunner.Run(() => second.OpenArchiveAsync(y), "둘째 창 열기");
        second.SaveOnExit(double.NaN);

        Assert.Equal(new[] { y }, svc.Settings.Load().Recent.Files.Select(f => f.Path));
    }

    /// <summary>'모두 지우기'는 다른 창이 그사이 저장한 줄까지 비운다(보이는 목록이 비어야 하므로).</summary>
    [Fact]
    public async Task 모두_지우기는_다른_창이_저장한_줄까지_비운다()
    {
        using var t = new TempDir("rc2clr");
        string a = CopyFixture(t.Root, "zip_default");
        string b = CopyFixture(t.Root, "7z_default");
        var svc = new TestServices();
        var ui2 = new FakeUserInteraction();
        var (first, second) = TwoWindows(svc, ui2: ui2);

        await VmRunner.Run(() => first.OpenArchiveAsync(a), "첫 창 열기");
        first.SaveOnExit(double.NaN);
        await VmRunner.Run(() => second.OpenArchiveAsync(b), "둘째 창 열기");
        ui2.ChoiceAnswers.Enqueue(0);
        second.ClearRecentCommand.Execute(null);

        Assert.Empty(svc.Settings.Load().Recent.Files);
        Assert.Empty(second.RecentFiles);
        second.SaveOnExit(double.NaN);
        Assert.Empty(svc.Settings.Load().Recent.Files);
    }

    /// <summary>
    /// 설정 파일을 못 읽으면(DLP 가 잡음 — 가짜 저장소 <see cref="InMemorySettingsStore.FailUpdates"/>) 덮어쓰지 않고, 이 창에는 적용한 채 들고 있다가
    /// 다음 저장(창을 닫을 때)에 다시 얹는다 — 그사이 다른 칸(트리 너비 등)은 그대로.
    /// </summary>
    [Fact]
    public async Task 설정을_못_읽으면_덮어쓰지_않고_다음_저장에_다시()
    {
        using var t = new TempDir("rcfail");
        string a = CopyFixture(t.Root, "zip_default");
        var start = new AppSettings();
        start.Ui.TreeWidth = 300;
        var svc = new TestServices(start);
        var vm = new MainViewModel(new FakeUserInteraction(), null, svc.Services);
        await VmRunner.Run(() => vm.OpenArchiveAsync(a), "열기");
        var changed = vm.Settings.Clone();
        changed.Extract.OnCollision = CollisionPolicy.Skip;

        svc.Settings.FailUpdates = true;
        vm.ApplySettings(changed);
        Assert.Equal(1, svc.Settings.UpdateCount);
        Assert.Equal(0, svc.Settings.SaveCount);
        Assert.Equal(CollisionPolicy.Ask, svc.Settings.Load().Extract.OnCollision);   // 덮어쓰지 않음
        Assert.Equal(CollisionPolicy.Skip, vm.Settings.Extract.OnCollision);          // 이 창에는 적용

        svc.Settings.FailUpdates = false;
        vm.SaveOnExit(double.NaN);
        var saved = svc.Settings.Load();
        Assert.Equal(1, svc.Settings.SaveCount);
        Assert.Equal(CollisionPolicy.Skip, saved.Extract.OnCollision);
        Assert.Equal(300, saved.Ui.TreeWidth);
        Assert.Equal(a, Assert.Single(saved.Recent.Files).Path);
    }

    /// <summary>☰ ▸ 설정을 적용한 뒤에 연 압축도 닫을 때 최근 파일에 저장된다(설정 창이 설정 객체를 바꿔 끼워 최근 파일이 옛 객체에 쌓이던 문제).</summary>
    [Fact]
    public async Task 설정_창을_적용한_뒤에_연_압축도_닫을_때_저장된다()
    {
        using var t = new TempDir("rcapply");
        string a = CopyFixture(t.Root, "zip_default");
        string b = CopyFixture(t.Root, "7z_default");
        var svc = new TestServices();
        var vm = new MainViewModel(new FakeUserInteraction(), null, svc.Services);

        await VmRunner.Run(() => vm.OpenArchiveAsync(a), "열기 a");
        var changed = vm.Settings.Clone();
        changed.Extract.OpenFolderAfter = false;
        vm.ApplySettings(changed);
        await VmRunner.Run(() => vm.OpenArchiveAsync(b), "열기 b");
        Assert.Equal(new[] { b, a }, vm.RecentFiles.Select(i => i.Path));

        vm.SaveOnExit(double.NaN);
        var saved = svc.Settings.Load();
        Assert.Equal(new[] { b, a }, saved.Recent.Files.Select(f => f.Path));
        Assert.False(saved.Extract.OpenFolderAfter);
    }

    /// <summary>
    /// 진짜 settings.json(<see cref="JsonSettingsStore.Update"/>): 저장 직전에 다시 읽어 바꾼 칸만 얹는다(다른 일집이 쓴 칸은 그대로, .tmp 가 남지 않음).
    /// 있는 설정 파일을 못 읽으면(다른 프로그램이 잡음) 쓰지 않고 null — 기본값으로 덮어쓰지 않는다.
    /// </summary>
    [Fact]
    public void 설정_파일은_저장_직전에_다시_읽어_바꾼_칸만_얹는다()
    {
        using var t = new TempDir("rcjson");
        string path = t.P("settings.json");
        var one = new JsonSettingsStore(path);
        var two = new JsonSettingsStore(path);
        var s = one.Load();
        s.Extract.OnCollision = CollisionPolicy.Overwrite;
        one.Save(s);

        Assert.NotNull(two.Update(x => x.Ui.TreeWidth = 321));
        var back = one.Load();
        Assert.Equal(CollisionPolicy.Overwrite, back.Extract.OnCollision);
        Assert.Equal(321, back.Ui.TreeWidth);
        Assert.False(File.Exists(path + ".tmp"));

        string before = File.ReadAllText(path);
        using (new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None))
            Assert.Null(two.Update(x => x.Extract.OnCollision = CollisionPolicy.Skip));
        Assert.Equal(before, File.ReadAllText(path));
    }

    /// <summary>설정 창의 차이(<see cref="SettingsPatch"/>)는 바꾼 칸만 — 다른 창이 그사이 바꾼 칸·최근 파일은 그대로 두고 얹는다.</summary>
    [Fact]
    public void 설정_창_차이는_바꾼_칸만_얹는다()
    {
        var before = new AppSettings();
        var after = before.Clone();
        after.Extract.OnCollision = CollisionPolicy.Skip;
        after.Compress.ExcludePatterns = new List<string> { "*.tmp" };
        after.Recent.Files.Add(new RecentFile { Path = @"C:\a.zip" });
        var patch = SettingsPatch.Between(before, after, "recent");
        Assert.Equal(new[] { "compress.excludePatterns", "extract.onCollision" }, patch.ChangedKeys.OrderBy(k => k, StringComparer.Ordinal));

        var other = new AppSettings();
        other.Extract.DeleteArchiveAfter = true;
        other.Ui.TreeWidth = 400;
        other.Recent.Files.Add(new RecentFile { Path = @"C:\b.zip" });
        patch.ApplyTo(other);
        Assert.Equal(CollisionPolicy.Skip, other.Extract.OnCollision);
        Assert.Equal(new[] { "*.tmp" }, other.Compress.ExcludePatterns);
        Assert.True(other.Extract.DeleteArchiveAfter);
        Assert.Equal(400, other.Ui.TreeWidth);
        Assert.Equal(@"C:\b.zip", Assert.Single(other.Recent.Files).Path);
        Assert.True(SettingsPatch.Between(before, before.Clone()).IsEmpty);
    }
}
