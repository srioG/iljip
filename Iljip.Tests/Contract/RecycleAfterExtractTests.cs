using Iljip.Services;
using Iljip.Tests.TestSupport;
using Iljip.ViewModels;

namespace Iljip.Tests.Contract;

/// <summary>
/// [W2-05 I2-5] '풀고 나서 압축 파일 삭제'(설정 extract.deleteArchiveAfter — 기본 꺼짐): 오류 0·건너뛴 파일 0·취소 아님일 때만 압축 파일을 휴지통으로
/// (나뉜 압축이면 조각 모두 — <see cref="ArchiveVolumes.AllParts"/>). 네트워크·이동식(휴지통 없는 곳)은 지우지 않고 알림, 휴지통으로 못 보내면 그대로 두고 알림.
/// 일집 임시 폴더 안의 압축·선택 항목만 풀기·창 없이 풀기(--extract-to, 재사용장치)는 지우지 않는다.
/// 휴지통은 가짜(<see cref="FakeRecycleBin"/> — 보낸 목록만 기록, 실제 파일은 그대로) — 진짜 휴지통·삭제는 시험에서 절대 부르지 않는다.
/// </summary>
public class RecycleAfterExtractTests
{
    private static Task RunShell(MainViewModel vm, params string[] args) =>
        VmRunner.Run(() => vm.ProcessShellCommandAsync(args), "우클릭 풀기");

    private static string Copy(string work, string fixtureId, string? name = null)
    {
        var fx = FixtureCatalog.Get(fixtureId);
        string dst = Path.Combine(work, name ?? Path.GetFileName(fx.FullPath));
        File.Copy(fx.FullPath, dst);
        return dst;
    }

    private static TestServices DeleteOn() => TestServices.With(e => e.DeleteArchiveAfter = true);

    /// <summary>켜 두고 다 풀면 그 압축 파일을 휴지통으로(한 번에 한 묶음).</summary>
    [Fact]
    public async Task 다_풀면_압축_파일을_휴지통으로_보낸다()
    {
        using var tmp = new TempDir("rbok");
        string zip = Copy(tmp.Dir("작업"), "zip_default");
        var ui = new FakeUserInteraction();
        var svc = DeleteOn();

        await RunShell(new MainViewModel(ui, null, svc.Services), "--extract-to-folder", zip);

        Assert.Equal(new[] { zip }, Assert.Single(svc.RecycleBin.Sent));
        Assert.Empty(ui.Warnings);
        Assert.Empty(ui.Errors);
    }

    /// <summary>기본(꺼짐)이면 지우지 않는다.</summary>
    [Fact]
    public async Task 기본은_지우지_않는다()
    {
        Assert.False(new AppSettings().Extract.DeleteArchiveAfter);
        using var tmp = new TempDir("rboff");
        string zip = Copy(tmp.Dir("작업"), "zip_default");
        var svc = new TestServices();

        await RunShell(new MainViewModel(new FakeUserInteraction(), null, svc.Services), "--extract-to-folder", zip);

        Assert.Empty(svc.RecycleBin.Sent);
    }

    /// <summary>나뉜 압축(7z.001~003·PKZIP .z01/.z02/.zip·zip.001~003)은 조각을 모두 보낸다.</summary>
    [Theory]
    [InlineData("7z_split")]
    [InlineData("zip_split")]
    [InlineData("zip_numbered_split")]
    public async Task 나뉜_압축은_조각을_모두_보낸다(string id)
    {
        var fx = FixtureCatalog.Get(id);
        using var tmp = new TempDir("rbsplit");
        string work = tmp.Dir("작업");
        var parts = new List<string>();
        foreach (var v in fx.SplitVolumes)
        {
            string dst = Path.Combine(work, Path.GetFileName(v));
            File.Copy(FixtureCatalog.PathOf(v), dst);
            parts.Add(dst);
        }
        string first = Path.Combine(work, Path.GetFileName(fx.File));
        var svc = DeleteOn();
        var ui = new FakeUserInteraction();

        await RunShell(new MainViewModel(ui, null, svc.Services), "--extract-to-folder", first);

        Assert.Empty(ui.Errors);
        Assert.Equal(parts.OrderBy(p => p, StringComparer.OrdinalIgnoreCase), svc.RecycleBin.AllSent.OrderBy(p => p, StringComparer.OrdinalIgnoreCase));
        Assert.Single(svc.RecycleBin.Sent);   // 한 번에(되돌릴 때도 한 묶음)
    }

    /// <summary>못 풀었으면(손상) 지우지 않는다.</summary>
    [Fact]
    public async Task 실패하면_지우지_않는다()
    {
        using var tmp = new TempDir("rbbad");
        string work = tmp.Dir("작업");
        string bad = Path.Combine(work, "망가진.zip");
        File.WriteAllBytes(bad, Enumerable.Range(0, 3000).Select(i => (byte)(i * 13 + 1)).ToArray());
        var svc = DeleteOn();

        await RunShell(new MainViewModel(new FakeUserInteraction(), null, svc.Services), "--extract-to-folder", bad);

        Assert.Empty(svc.RecycleBin.Sent);
    }

    /// <summary>같은 이름에서 '건너뛰기'를 골라 안 푼 파일이 있으면 압축 파일이 아직 필요할 수 있어 지우지 않는다.</summary>
    [Fact]
    public async Task 건너뛴_파일이_있으면_지우지_않는다()
    {
        using var tmp = new TempDir("rbskip");
        string work = tmp.Dir("작업");
        string zip = Copy(work, "zip_default");
        string mine = Path.Combine(work, "일집_샘플", "견적서_2026.txt");
        Directory.CreateDirectory(Path.GetDirectoryName(mine)!);
        File.WriteAllText(mine, "사용자 파일");
        var ui = new FakeUserInteraction { DefaultCollisionAnswer = new(CollisionChoice.Skip, false) };
        var svc = DeleteOn();

        await RunShell(new MainViewModel(ui, null, svc.Services), "--extract-here", zip);

        Assert.Single(ui.CollisionPrompts);
        Assert.Empty(svc.RecycleBin.Sent);
        Assert.Empty(ui.Errors);
    }

    /// <summary>같은 이름에서 '덮어쓰기'·'이름 바꾸기'는 다 푼 것이라 지운다.</summary>
    [Theory]
    [InlineData(CollisionChoice.Overwrite)]
    [InlineData(CollisionChoice.Rename)]
    public async Task 덮어쓰기나_이름_바꾸기로_다_풀었으면_지운다(CollisionChoice choice)
    {
        using var tmp = new TempDir("rbow");
        string work = tmp.Dir("작업");
        string zip = Copy(work, "zip_default");
        string mine = Path.Combine(work, "일집_샘플", "견적서_2026.txt");
        Directory.CreateDirectory(Path.GetDirectoryName(mine)!);
        File.WriteAllText(mine, "사용자 파일");
        var ui = new FakeUserInteraction { DefaultCollisionAnswer = new(choice, false) };
        var svc = DeleteOn();

        await RunShell(new MainViewModel(ui, null, svc.Services), "--extract-here", zip);

        Assert.Equal(new[] { zip }, svc.RecycleBin.AllSent);
    }

    /// <summary>진행 창 [취소]면 지우지 않는다.</summary>
    [Fact]
    public async Task 취소하면_지우지_않는다()
    {
        using var tmp = new TempDir("rbcan");
        string zip = Copy(tmp.Dir("작업"), "zip_default");
        var svc = DeleteOn();

        await RunShell(new MainViewModel(new FakeUserInteraction { CancelOnFirstBytes = true }, null, svc.Services), "--extract-to-folder", zip);

        Assert.Empty(svc.RecycleBin.Sent);
    }

    /// <summary>휴지통이 없는 곳(네트워크 드라이브·USB 등 — 지우면 영구 삭제)이면 지우지 않고 쉬운 말로 알린다.</summary>
    [Fact]
    public async Task 휴지통이_없는_곳이면_지우지_않고_알린다()
    {
        using var tmp = new TempDir("rbnet");
        string work = tmp.Dir("작업");
        string zip = Copy(work, "zip_default");
        var svc = DeleteOn();
        svc.RecycleBin.NotRecyclable.Add(work);
        var ui = new FakeUserInteraction();

        await RunShell(new MainViewModel(ui, null, svc.Services), "--extract-to-folder", zip);

        Assert.Empty(svc.RecycleBin.Sent);
        string w = Assert.Single(ui.Warnings);
        Assert.Contains("zip_default.zip", w);
        Assert.Contains("휴지통이 없는 곳", w);
        Assert.Contains("그대로 두었어요", w);
        Assert.True(Directory.Exists(Path.Combine(work, "zip_default")));   // 풀기는 그대로 됨
    }

    /// <summary>나뉜 압축의 조각 하나라도 휴지통이 없는 곳이면 하나도 지우지 않는다.</summary>
    [Fact]
    public async Task 조각_하나라도_휴지통이_없는_곳이면_하나도_안_지운다()
    {
        var fx = FixtureCatalog.Get("7z_split");
        using var tmp = new TempDir("rbnet2");
        string work = tmp.Dir("작업");
        foreach (var v in fx.SplitVolumes) File.Copy(FixtureCatalog.PathOf(v), Path.Combine(work, Path.GetFileName(v)));
        var svc = DeleteOn();
        svc.RecycleBin.NotRecyclable.Add(Path.Combine(work, "7z_split.7z.003"));
        var ui = new FakeUserInteraction();

        await RunShell(new MainViewModel(ui, null, svc.Services), "--extract-to-folder", Path.Combine(work, "7z_split.7z.001"));

        Assert.Empty(svc.RecycleBin.Sent);
        Assert.Single(ui.Warnings);
    }

    /// <summary>휴지통으로 못 보내면(잠김 등) 그대로 두고 알린다(풀기 자체는 성공).</summary>
    [Fact]
    public async Task 휴지통으로_못_보내면_알린다()
    {
        using var tmp = new TempDir("rbfail");
        string zip = Copy(tmp.Dir("작업"), "zip_default");
        var svc = DeleteOn();
        svc.RecycleBin.Result = new RecycleResult(false, "0x80070020");
        var ui = new FakeUserInteraction();

        await RunShell(new MainViewModel(ui, null, svc.Services), "--extract-to-folder", zip);

        Assert.Single(svc.RecycleBin.Sent);
        string w = Assert.Single(ui.Warnings);
        Assert.Equal("‘zip_default.zip’ 압축 파일을 휴지통으로 옮기지 못해 그대로 두었어요.", w);
        Assert.DoesNotContain("0x8007", w);   // 날 코드는 로그에만
        Assert.Empty(ui.Errors);
    }

    /// <summary>일집 임시 폴더 안의 압축(압축 안의 압축을 꺼내 연 것 등)은 지우지 않는다(일집이 알아서 치움).</summary>
    [Fact]
    public async Task 일집_임시_폴더_안의_압축은_지우지_않는다()
    {
        string dir = Path.Combine(AppPaths.TempRoot, "w205-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(dir);
        try
        {
            Assert.StartsWith(ModuleInit.TestAppDataRoot, AppPaths.TempRoot);   // 시험 중엔 시험 전용 자료 폴더
            string zip = Copy(dir, "zip_default");
            var svc = DeleteOn();
            await RunShell(new MainViewModel(new FakeUserInteraction(), null, svc.Services), "--extract-to-folder", zip);
            Assert.True(Directory.Exists(Path.Combine(dir, "zip_default")));
            Assert.Empty(svc.RecycleBin.Sent);
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); } catch { /* 무시 */ }
        }
    }

    /// <summary>메인 창에서 연 압축을 [풀기]로 다 풀면 지우고, 지운 압축을 계속 보여 주지 않는다(화면 비움).</summary>
    [Fact]
    public async Task 메인_창에서_다_풀면_지우고_화면을_비운다()
    {
        using var tmp = new TempDir("rbmain");
        string zip = Copy(tmp.Dir("작업"), "zip_default");
        string dest = tmp.Dir("풀 곳");
        var ui = new FakeUserInteraction { FolderToPick = dest };
        var svc = DeleteOn();
        var vm = new MainViewModel(ui, null, svc.Services);

        await VmRunner.Run(async () =>
        {
            Assert.True(await vm.OpenArchiveAsync(zip));
            await vm.ExtractCommand.ExecuteAsync(null);
        }, "열기 → 풀기");

        Assert.Equal(new[] { zip }, svc.RecycleBin.AllSent);
        Assert.Null(vm.CurrentArchivePath);
        Assert.Empty(ui.Errors);
    }

    /// <summary>목록에서 고른 것만 풀면 압축 파일은 아직 필요하다 — 지우지 않는다.</summary>
    [Fact]
    public async Task 고른_항목만_풀면_지우지_않는다()
    {
        using var tmp = new TempDir("rbsel");
        string zip = Copy(tmp.Dir("작업"), "zip_default");
        string dest = tmp.Dir("풀 곳");
        var ui = new FakeUserInteraction { FolderToPick = dest };
        var svc = DeleteOn();
        var vm = new MainViewModel(ui, null, svc.Services);

        await VmRunner.Run(async () =>
        {
            Assert.True(await vm.OpenArchiveAsync(zip));
            Assert.True(await vm.ExtractToFolderAsync(zip, dest, new[] { "일집_샘플/견적서_2026.txt" }));
        }, "고른 항목만 풀기");

        Assert.True(File.Exists(Path.Combine(dest, "일집_샘플", "견적서_2026.txt")));
        Assert.Empty(svc.RecycleBin.Sent);
        Assert.Equal(zip, vm.CurrentArchivePath);
    }

    /// <summary>
    /// 여러 개 풀기: 다 풀린 것만 지운다(못 푼 것·암호를 넣지 않아 건너뛴 것은 그대로).
    /// </summary>
    [Fact]
    public async Task 여러_개_풀기는_다_풀린_것만_지운다()
    {
        using var tmp = new TempDir("rbbatch");
        string work = tmp.Dir("작업");
        string a = Copy(work, "zip_default", "a.zip");
        string b = Copy(work, "zip_aes256", "b.zip");   // 암호 창 취소 → 건너뜀
        string c = Path.Combine(work, "c.zip");
        File.WriteAllBytes(c, Enumerable.Range(0, 3000).Select(i => (byte)(i * 13 + 1)).ToArray());
        string d = Copy(work, "7z_default", "d.7z");
        var svc = DeleteOn();
        var ui = new FakeUserInteraction();

        await RunShell(new MainViewModel(ui, null, svc.Services), "--extract-to-folder", a, b, c, d);

        Assert.Equal(new[] { a, d }, svc.RecycleBin.AllSent);
        Assert.Single(ui.Summaries);
    }

    /// <summary>계약(소스): 창 없이 풀기(재사용장치 --extract-to)는 설정·휴지통·같은 이름 묻기를 쓰지 않는다 — 늘 같은 동작('이름 (1)').</summary>
    [Fact]
    public void 창_없이_풀기는_설정과_휴지통을_쓰지_않는다()
    {
        string src = RepoFiles.Read("Iljip", "App.xaml.cs");
        int start = src.IndexOf("private static async Task<int> RunHeadlessExtractAsync", StringComparison.Ordinal);
        int end = src.IndexOf("protected override void OnExit", StringComparison.Ordinal);
        Assert.True(start >= 0 && end > start, "RunHeadlessExtractAsync 를 찾지 못함");
        string body = src[start..end];
        foreach (var word in new[] { "ExtractionScope", "Settings", "RecycleBin", "AppServices", "Resolver", "MainViewModel", "ShowSummary" })
            Assert.DoesNotContain(word, body);
    }
}
