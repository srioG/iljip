using System.Text;
using Iljip.Models;
using Iljip.Services;
using Iljip.Tests.TestSupport;
using Iljip.ViewModels;

namespace Iljip.Tests.Contract;

/// <summary>
/// [W2-08 — I2-7] 압축 검사(<see cref="ArchiveTestService"/> — ☰ ▸ 시험하기·목록 오른쪽 클릭·'--test'): 풀지 않고(디스크에 쓰지 않음) 모든 항목을 끝까지 읽어
/// 확인값(CRC)·크기를 본다. 결과 요약 '문제없어요. 파일 128개를 확인했어요.', 문제면 '이름 — 이유'(쉬운 한국어). 진행·취소가 된다.
/// 화면 흐름(암호 묻기·결과 창·문제 창의 'ZIP 고치기 시도')은 가짜 창(<see cref="FakeUserInteraction"/>)으로. 만든 압축의 자동 시험(TestAfter)도 같은 검사기를 쓴다.
/// </summary>
public class ArchiveTesterTests
{
    private static readonly TimeSpan Limit = TimeSpan.FromSeconds(60);

    /// <summary>재사용장치·화면에 나오면 안 되는 엔진 영어 원문(UiStringAuditTests 와 같은 목록).</summary>
    private static readonly string[] EngineEnglish = { "CRC", "Data error", "Unexpected end", "Headers Error", "Can not open", "Exception" };

    /// <summary>
    /// 매니페스트의 커밋되는 픽스처(반디집·파이썬이 만든 정상 압축 — 분할·암호 포함). git 제외 픽스처(local/sfx_default.exe)는 새 복제본에 없으므로
    /// 아래 <see cref="자동풀림_exe도_문제없고_파일_수가_맞다"/>(없으면 건너뜀)가 같은 계약을 본다(I4-1 G2·G3 — 예전엔 새 복제본에서 이 줄이 실패).
    /// </summary>
    public static TheoryData<string> AllFixtures => FixtureSets.Data(FixtureCatalog.All.Values.Where(f => f.CommitOk).Select(f => f.Id).OrderBy(k => k, StringComparer.Ordinal).ToArray());

    private static Task<TestReport> Test(string path, string? password = null, IProgress<ArchiveProgress>? progress = null, CancellationToken ct = default) =>
        ArchiveTestService.Instance.TestAsync(path, password, progress, ct).WaitAsync(Limit);

    private static void AssertKoreanOnly(string text, string context)
    {
        Assert.True(ErrorCatalog.ContainsHangul(text), $"{context}: 한글 없음 — {text}");
        foreach (var word in EngineEnglish)
            Assert.True(!text.Contains(word, StringComparison.Ordinal), $"{context}: 영어 원문 '{word}' 가 보임 — {text}");
    }

    // ───────────── 정상 압축 ─────────────

    /// <summary>계약: 정상 압축은 모두 '문제없어요', 확인한 파일 수 = 기대 파일 수(폴더 빼고). 암호 압축은 암호를 주면.</summary>
    [Theory]
    [MemberData(nameof(AllFixtures))]
    public Task 정상_압축은_문제없고_파일_수가_맞다(string id) => HealthyAndCounted(id);

    /// <summary>같은 계약 — 자동 풀림 exe(git 제외 픽스처: 없으면 건너뜀, I4-1 G2).</summary>
    [FixtureFact("local/sfx_default.exe")]
    public Task 자동풀림_exe도_문제없고_파일_수가_맞다() => HealthyAndCounted("sfx_default");

    private static async Task HealthyAndCounted(string id)
    {
        var fx = FixtureCatalog.Get(id);
        var report = await Test(fx.FullPath, fx.Password);
        Assert.True(report.Ok, $"{id}: {report.ToDisplayText()}\n{report.DetailText()}");
        int files = fx.ExpectedFiles.Count();
        Assert.Equal(files, report.CheckedCount);
        Assert.Equal($"문제없어요. 파일 {files}개를 확인했어요.", report.Headline);
        Assert.Equal(report.Headline, report.ToDisplayText());
    }

    /// <summary>계약: 시험은 아무것도 쓰지 않는다 — 압축 파일이 있는 폴더를 지켜봐도 만들어지거나 바뀐 파일이 없다(임시 파일도).</summary>
    [Theory]
    [InlineData("zip_default")]
    [InlineData("zip_aes256")]
    [InlineData("zip_zipcrypto_korean_pw")]
    [InlineData("7z_default")]
    [InlineData("7z_header_encrypted")]
    [InlineData("tar_default")]
    [InlineData("tgz_default")]
    [InlineData("gz_single")]
    [InlineData("bz2_single_python")]
    [InlineData("xz_single")]
    [InlineData("lzh_default")]
    [InlineData("iso_default")]
    [InlineData("zipx_default")]
    [InlineData("7z_split")]
    [InlineData("zip_split")]
    public Task 시험은_아무것도_쓰지_않는다(string id) => NothingWritten(id);

    /// <summary>같은 계약 — 자동 풀림 exe(git 제외 픽스처 local/sfx_default.exe: 없으면 건너뜀, I4-1 G2).</summary>
    [FixtureFact("local/sfx_default.exe")]
    public Task 자동풀림_exe_시험도_아무것도_쓰지_않는다() => NothingWritten("sfx_default");

    private static async Task NothingWritten(string id)
    {
        var fx = FixtureCatalog.Get(id);
        using var tmp = new TempDir("tnw");
        string folder = tmp.Dir("받은 파일");
        var volumes = fx.SplitVolumes.Count > 0 ? fx.SplitVolumes : new[] { fx.File };
        foreach (var v in volumes) File.Copy(FixtureCatalog.PathOf(v), Path.Combine(folder, Path.GetFileName(v)));
        string arc = Path.Combine(folder, Path.GetFileName(fx.File));
        var before = Listing(folder);

        var events = new List<string>();
        using (var watcher = new FileSystemWatcher(folder) { IncludeSubdirectories = true, NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite | NotifyFilters.Size })
        {
            void Record(object _, FileSystemEventArgs e) { lock (events) events.Add($"{e.ChangeType}: {e.Name}"); }
            watcher.Created += Record;
            watcher.Changed += Record;
            watcher.Deleted += Record;
            watcher.Renamed += (_, e) => { lock (events) events.Add($"Renamed: {e.OldName} → {e.Name}"); };
            watcher.EnableRaisingEvents = true;

            var report = await Test(arc, fx.Password);
            Assert.True(report.Ok, report.ToDisplayText());
            await Task.Delay(300);   // 알림이 늦게 올 수 있다
        }
        lock (events) Assert.True(events.Count == 0, "시험 중 압축 폴더에 쓰기:\n  " + string.Join("\n  ", events));
        Assert.Equal(before, Listing(folder));
    }

    private static List<string> Listing(string folder) =>
        Directory.EnumerateFileSystemEntries(folder, "*", SearchOption.AllDirectories)
            .Select(p => $"{Path.GetRelativePath(folder, p)}|{(File.Exists(p) ? new FileInfo(p).Length : -1)}|{File.GetLastWriteTimeUtc(p).Ticks}")
            .OrderBy(s => s, StringComparer.Ordinal).ToList();

    // ───────────── 손상·잘림 ─────────────

    /// <summary>(이름, 원본 픽스처, 망가뜨리는 방법)</summary>
    public static TheoryData<string, string, string> Damaged => new()
    {
        { "trunc_half.zip", "zip_default", "half" },
        { "trunc_tail.zip", "zip_default", "tail30" },
        { "deflate_broken.zip", "zip_default", "xor200" },
        { "garbage.zip", "zip_default", "garbage" },
        { "trunc_half.7z", "7z_default", "half" },
        { "data_broken.7z", "7z_default", "xor100" },
        { "garbage.7z", "7z_default", "garbage" },
        { "trunc_half.tar", "tar_default", "half" },
        { "trunc_half.tar.gz", "targz_default", "half" },
        { "trunc_half.gz", "gz_single", "half" },
        { "trunc_half.bz2", "bz2_single_python", "half" },
        { "garbage.bz2", "bz2_single_python", "garbage" },
        { "trunc_half.lzh", "lzh_default", "half" },
        { "trunc_half.zipx", "zipx_default", "half" },
        { "garbage.egg", "zip_default", "garbage" },
        { "garbage.alz", "zip_default", "garbage" },
        { "garbage.rar", "zip_default", "garbage" },
    };

    /// <summary>
    /// 계약: 손상·잘린 압축은 예외가 아니라 결과(Ok=false)로 — 이유는 쉬운 한국어(엔진 영어 원문 없음), 원문은 '자세히'(DetailText)에.
    /// 재사용장치가 '압축 아님'으로 오해할 말('압축 파일이 아닌')도 없다.
    /// </summary>
    [Theory]
    [MemberData(nameof(Damaged))]
    public async Task 손상되거나_잘린_압축은_문제로_알린다(string name, string sourceId, string how)
    {
        using var tmp = new TempDir("tdmg");
        string arc = tmp.P(name);
        File.WriteAllBytes(arc, Damage(File.ReadAllBytes(FixtureCatalog.Get(sourceId).FullPath), how));
        var report = await Test(arc);
        Assert.False(report.Ok, $"{name}: 손상인데 문제없다고 함 — {report.Headline}");
        Assert.NotEmpty(report.Problems);
        AssertKoreanOnly(report.ToDisplayText(), name);
        foreach (var p in report.Problems) AssertKoreanOnly(p.Reason, name);
        Assert.StartsWith("문제가 있어요.", report.Headline);
        Assert.DoesNotContain("압축 파일이 아닌", report.ToDisplayText());
        Assert.Empty(Directory.GetFiles(tmp.Root).Except(new[] { arc }));
    }

    /// <summary>
    /// 계약: 항목 하나의 내용만 바뀐 ZIP(저장 방식, 머리·CRC 는 그대로) — 그 항목만 '내용이 원래와 달라요(검사값 불일치)', 나머지는 확인됨.
    /// 요약 '문제가 있어요. 파일 9개 중 1개에 문제가 있어요.'
    /// </summary>
    [Fact]
    public async Task 항목_하나만_깨지면_그_항목만_알린다()
    {
        using var tmp = new TempDir("tcrc");
        string arc = CorruptArchiveTests.MakeCrcBroken(tmp);
        int files = FixtureCatalog.Get("zip_store_l0").ExpectedFiles.Count();
        var report = await Test(arc);
        Assert.False(report.Ok);
        var p = Assert.Single(report.Problems);
        Assert.EndsWith("readme.txt", p.Entry);
        Assert.Equal("내용이 원래와 달라요(검사값 불일치)", p.Reason);
        Assert.Equal(files - 1, report.CheckedCount);
        Assert.Equal($"문제가 있어요. 파일 {files}개 중 1개에 문제가 있어요.", report.Headline);
        Assert.Contains($"- {p.Entry} — 내용이 원래와 달라요(검사값 불일치)", report.ToDisplayText());
        AssertKoreanOnly(report.ToDisplayText(), "crc");
    }

    /// <summary>
    /// 계약: 솔리드 7z(여러 파일을 한 덩어리로 압축)의 데이터가 깨지면 어느 파일인지 가려낼 수 없어 '손상된 부분이 있어요 …(솔리드)' 하나로 알린다.
    /// 망가진 항목들의 이름은 '자세히'에.
    /// </summary>
    [Fact]
    public async Task 솔리드_7z가_깨지면_뭉뚱그려_알린다()
    {
        using var tmp = new TempDir("tsolid");
        string src = tmp.Dir("원본");
        for (int i = 0; i < 6; i++)
            File.WriteAllText(Path.Combine(src, $"문서{i}.txt"), string.Concat(Enumerable.Range(0, 3000).Select(n => $"{i}번 문서 {n} 줄 {n * 7919 % 977}\n")));
        string arc = SevenZipTestArchives.SevenZip(src, tmp.P("묶음.7z"));
        var good = await Test(arc);
        Assert.True(good.Ok, good.ToDisplayText());
        Assert.Equal(6, good.CheckedCount);

        string broken = SevenZipTestArchives.CorruptMiddle(arc, tmp.P("깨진 묶음.7z"));
        var report = await Test(broken);
        Assert.False(report.Ok);
        var p = Assert.Single(report.Problems);
        Assert.Equal(string.Empty, p.Entry);
        Assert.Equal(ArchiveTestService.SolidDamagedMessage, p.Reason);
        Assert.Equal("문제가 있어요.", report.Headline);
        AssertKoreanOnly(report.ToDisplayText(), "solid");
    }

    // ───────────── 취소·암호·환경 ─────────────

    /// <summary>계약: 진행을 알리고, 도중에 취소하면 OperationCanceledException(결과 없음).</summary>
    [Theory]
    [InlineData("zip_default")]
    [InlineData("7z_default")]
    [InlineData("tar_default")]
    [InlineData("tgz_default")]
    public async Task 진행을_알리고_취소된다(string id)
    {
        var fx = FixtureCatalog.Get(id);
        var reports = new ProgressRecorder();
        var ok = await Test(fx.FullPath, null, reports);
        Assert.True(ok.Ok);
        Assert.NotEmpty(reports.Reports);

        using var cts = new CancellationTokenSource();
        var cancelOnFirst = new CancelOnReport(cts);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Test(fx.FullPath, null, cancelOnFirst, cts.Token));
        Assert.True(cancelOnFirst.Count > 0);

        using var already = new CancellationTokenSource();
        already.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Test(fx.FullPath, null, null, already.Token));
    }

    private sealed class CancelOnReport(CancellationTokenSource cts) : IProgress<ArchiveProgress>
    {
        public int Count;

        public void Report(ArchiveProgress value)
        {
            Interlocked.Increment(ref Count);
            cts.Cancel();
        }
    }

    /// <summary>계약: 암호가 필요하면(없거나 틀리면) 결과가 아니라 일집 암호 예외 — 화면이 암호를 묻고 다시 부른다.</summary>
    [Theory]
    [InlineData("zip_aes256")]
    [InlineData("zip_zipcrypto")]
    [InlineData("7z_header_encrypted")]
    [InlineData("py7z_pw_names_visible")]
    public async Task 암호가_없거나_틀리면_암호_예외(string id)
    {
        var fx = FixtureCatalog.Get(id);
        var none = await Assert.ThrowsAnyAsync<ArchivePasswordException>(() => Test(fx.FullPath));
        Assert.True(none is PasswordRequiredException, none.GetType().Name);
        await Assert.ThrowsAsync<WrongPasswordException>(() => Test(fx.FullPath, "틀린암호-1"));
    }

    /// <summary>계약: 없는 파일·조각이 빠진 분할은 결과·예외로 쉬운 한국어(조각 이름을 알림).</summary>
    [Fact]
    public async Task 없는_파일과_빠진_조각()
    {
        using var tmp = new TempDir("tmiss");
        await Assert.ThrowsAsync<FileNotFoundException>(() => Test(tmp.P("없음.zip")));

        var fx = FixtureCatalog.Get("7z_split");
        File.Copy(FixtureCatalog.PathOf(fx.SplitVolumes[0]), tmp.P(Path.GetFileName(fx.SplitVolumes[0])));
        var report = await Test(tmp.P(Path.GetFileName(fx.SplitVolumes[0])));
        Assert.False(report.Ok);
        var p = Assert.Single(report.Problems);
        Assert.Contains("조각", p.Reason);
        AssertKoreanOnly(report.ToDisplayText(), "split");
    }

    // ───────────── 화면 흐름(가짜 창) ─────────────

    /// <summary>
    /// 계약: 화면에서 암호 압축을 시험하면 암호를 한 번 묻고(맞으면 끝), 결과 창 '‘이름’ 시험 결과 — 문제없어요. 파일 9개를 확인했어요.'. 진행 창 하나·오류 없음.
    /// </summary>
    [Theory]
    [InlineData("zip_aes256", PasswordPromptReason.Required)]
    [InlineData("zip_zipcrypto_korean_pw", PasswordPromptReason.Required)]
    [InlineData("7z_header_encrypted", PasswordPromptReason.ListLocked)]
    public async Task 화면에서_암호_압축을_시험하면_한_번_묻는다(string id, PasswordPromptReason reason)
    {
        var fx = FixtureCatalog.Get(id);
        var ui = new FakeUserInteraction(fx.Password);
        var vm = new MainViewModel(ui);
        TestReport? report = null;
        await VmRunner.Run(async () => report = await vm.TestArchiveFileAsync(fx.FullPath), "시험");

        var ask = Assert.Single(ui.PasswordPrompts);
        Assert.Equal(reason, ask.Reason);
        Assert.NotNull(report);
        Assert.True(report!.Ok);
        string info = Assert.Single(ui.Infos);
        Assert.Contains($"‘{Path.GetFileName(fx.FullPath)}’ 시험 결과", info);
        Assert.Contains($"문제없어요. 파일 {fx.ExpectedFiles.Count()}개를 확인했어요.", info);
        Assert.Empty(ui.Errors);
        Assert.Empty(ui.Problems);
        Assert.Contains("시험 완료: 문제없음", vm.StatusText);
    }

    /// <summary>계약: 틀린 암호면 다시 묻고(이유 '틀림'), [취소]하면 조용히 그만둔다(결과 창 없음).</summary>
    [Fact]
    public async Task 화면에서_틀린_암호면_다시_묻고_취소하면_그만()
    {
        var fx = FixtureCatalog.Get("zip_aes256");
        var ui = new FakeUserInteraction("틀린암호", fx.Password);
        var vm = new MainViewModel(ui);
        await VmRunner.Run(() => vm.TestArchiveFileAsync(fx.FullPath), "시험");
        Assert.Equal(new[] { PasswordPromptReason.Required, PasswordPromptReason.Wrong }, ui.PasswordPrompts.Select(p => p.Reason));
        Assert.Single(ui.Infos);

        var ui2 = new FakeUserInteraction();   // 답 없음 = 취소
        var vm2 = new MainViewModel(ui2);
        TestReport? r = null;
        await VmRunner.Run(async () => r = await vm2.TestArchiveFileAsync(fx.FullPath), "시험");
        Assert.Null(r);
        Assert.Single(ui2.PasswordPrompts);
        Assert.Empty(ui2.Infos);
        Assert.Empty(ui2.Problems);
        Assert.Equal("시험을 취소했어요", vm2.StatusText);
    }

    /// <summary>계약: 진행 창에서 [취소]하면 결과 창 없이 '시험을 취소했어요'.</summary>
    [Fact]
    public async Task 화면에서_진행_창을_취소하면_조용히()
    {
        var ui = new FakeUserInteraction { CancelProgress = true };
        var vm = new MainViewModel(ui);
        TestReport? r = null;
        await VmRunner.Run(async () => r = await vm.TestArchiveFileAsync(FixtureCatalog.Get("7z_default").FullPath), "시험");
        Assert.Null(r);
        Assert.Empty(ui.Infos);
        Assert.Empty(ui.Problems);
        Assert.Empty(ui.Errors);
        Assert.Equal("시험을 취소했어요", vm.StatusText);
    }

    /// <summary>
    /// 계약: 문제가 있으면 문제 창 — 큰 글씨 '‘이름’을 시험했더니 문제가 있어요.', 문구는 '이름 — 이유', 원문은 '자세히'. ZIP 이면 'ZIP 고치기 시도'를 고를 수 있고,
    /// 고르면 저장할 곳(기본 '이름_복구.zip')을 묻고 살릴 수 있는 파일만 새 ZIP 으로(원본 그대로).
    /// </summary>
    [Fact]
    public async Task 화면에서_깨진_ZIP을_시험하면_고치기를_고를_수_있다()
    {
        using var tmp = new TempDir("tvmfix");
        string arc = CorruptArchiveTests.MakeCrcBroken(tmp);
        string sha = TreeAssert.Sha256(arc);
        var ui = new FakeUserInteraction { SavePath = tmp.P("고친 것.zip") };
        ui.ProblemActionAnswers.Enqueue(ErrorCatalog.RepairZipActionId);
        var vm = new MainViewModel(ui);
        await VmRunner.Run(() => vm.TestArchiveFileAsync(arc), "시험");

        var problem = Assert.Single(ui.Problems);
        Assert.Equal("‘crc_broken.zip’을 시험했더니 문제가 있어요.", problem.Heading);
        Assert.Contains("내용이 원래와 달라요(검사값 불일치)", problem.Message);
        Assert.Equal(ErrorCatalog.RepairZipActionId, Assert.Single(problem.Actions).Id);
        Assert.StartsWith("ZIP 고치기 완료: ", vm.StatusText);   // '시험 완료: 문제 있음' 다음에 고치기까지 함

        Assert.Equal("crc_broken_복구.zip", Assert.Single(ui.SaveDefaultNames));
        int files = FixtureCatalog.Get("zip_store_l0").ExpectedFiles.Count();
        Assert.Empty(ui.Infos);   // 다 살리지 못했으면 안내 창이 아니라 주의 창(I4-1 F2)
        string info = Assert.Single(ui.Warnings);
        Assert.Contains($"전체 {files}개 중 {files - 1}개를 살렸어요.", info);
        Assert.Contains("내용이 원래와 달라요", info);
        Assert.Contains("원래 파일은 그대로예요.", info);
        Assert.True(File.Exists(tmp.P("고친 것.zip")));
        Assert.Equal(sha, TreeAssert.Sha256(arc));
    }

    /// <summary>계약: 7z 처럼 ZIP 이 아니면 문제 창에 'ZIP 고치기 시도'가 없다. 고르지 않으면(창만 닫음) 고치지 않는다.</summary>
    [Fact]
    public async Task 화면에서_ZIP이_아니면_고치기가_없다()
    {
        using var tmp = new TempDir("tvm7z");
        string arc = tmp.P("깨진.7z");
        File.WriteAllBytes(arc, Damage(File.ReadAllBytes(FixtureCatalog.Get("7z_default").FullPath), "half"));
        var ui = new FakeUserInteraction();
        var vm = new MainViewModel(ui);
        await VmRunner.Run(() => vm.TestArchiveFileAsync(arc), "시험");
        var problem = Assert.Single(ui.Problems);
        Assert.Empty(problem.Actions);
        Assert.Empty(ui.SaveDefaultNames);
        Assert.Equal("시험 완료: 문제 있음 (깨진.7z)", vm.StatusText);
    }

    /// <summary>
    /// 계약: '--test 압축…'(탐색기·명령줄)은 메인 창 없이 차례로 검사 — 결과 창이 압축마다 하나. 없는 파일이면 쉬운 한국어로 알린다.
    /// </summary>
    [Fact]
    public async Task test_명령은_압축마다_결과를_알린다()
    {
        Assert.False(CommandLineParser.Parse(new[] { "--test", "a.zip" }).ShowMainWindow);
        var a = FixtureCatalog.Get("zip_default").FullPath;
        var b = FixtureCatalog.Get("tar_default").FullPath;
        var ui = new FakeUserInteraction();
        var vm = new MainViewModel(ui);
        await VmRunner.Run(() => vm.ProcessShellCommandAsync(new[] { "--test", a, b }), "--test");
        Assert.Equal(2, ui.Infos.Count);
        Assert.Contains("‘zip_default.zip’ 시험 결과", ui.Infos[0]);
        Assert.Contains("‘tar_default.tar’ 시험 결과", ui.Infos[1]);
        Assert.Null(vm.CurrentArchivePath);   // 보던 압축을 바꾸지 않음

        var ui2 = new FakeUserInteraction();
        await VmRunner.Run(() => new MainViewModel(ui2).ProcessShellCommandAsync(new[] { "--test", @"C:\없는 폴더\없음.zip" }), "--test");
        Assert.Contains("시험할 압축 파일을 찾을 수 없어요", Assert.Single(ui2.Errors));
    }

    // ───────────── 만든 압축 자동 시험(TestAfter) ─────────────

    /// <summary>
    /// 계약: '다 만들고 시험하기'(TestAfter)가 켜져 있으면 만든 압축을 시험기로 한 번 시험하고 결과를 알린다(진짜 시험기 — 디스크에 쓰지 않음).
    /// 꺼져 있으면 시험하지 않는다.
    /// </summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task 다_만들고_시험하기는_한_번_시험한다(bool testAfter)
    {
        using var tmp = new TempDir("tafter");
        var services = new TestServices();   // 시험기는 진짜에 맡기며 부른 기록만(FakeCompressTester.Mode = Real)
        var ui = new FakeUserInteraction();
        var vm = new MainViewModel(ui, null, services.Services);
        string src = Path.Combine(tmp.Dir("원본"), "보고서.txt");
        File.WriteAllText(src, string.Concat(Enumerable.Range(0, 500).Select(i => $"줄 {i}\n")), new UTF8Encoding(false));
        string outDir = tmp.Dir("만든곳");
        ui.CompressionRequestAnswer = r => new CompressionRequest
        {
            Sources = r.Sources,
            OutputFolder = outDir,
            BaseName = "보고서",
            Format = CompressionFormat.SevenZip,
            Options = CompressionOptions.Default,
            EachSeparately = false,
            DateInName = DateNameFormat.None,
            ExcludeEnabled = true,
            ExcludePatterns = r.Defaults.ExcludePatterns,
            TestAfter = testAfter,
            DeleteOriginalsAfter = false,
            OverwriteExisting = false,
        };
        vm.AddToStaging(new[] { src });
        await VmRunner.Run(() => vm.CompressCommand.ExecuteAsync(null), "압축");

        string arc = Path.Combine(outDir, "보고서.7z");
        Assert.True(File.Exists(arc));
        Assert.Empty(ui.Errors);
        Assert.Empty(ui.Warnings);
        if (testAfter)
        {
            Assert.Equal(new[] { arc }, services.CompressTester.Tested);
            Assert.Contains(ui.Infos, i => i.Contains("만든 압축 파일을 시험해 봤어요 — 문제없어요."));
        }
        else
        {
            Assert.Empty(services.CompressTester.Tested);
            Assert.DoesNotContain(ui.Infos, i => i.Contains("시험해 봤어요"));
        }
        Assert.Equal(new[] { "보고서.7z" }, Directory.GetFiles(outDir).Select(Path.GetFileName));   // 시험이 남긴 파일 없음
    }

    /// <summary>계약: 만든 압축 시험(CreatedArchiveTester)이 문제를 찾으면 일집 손상 예외 — 문구는 '파일 N개에 문제가 있어요: ‘이름’(이유).'(쉬운 한국어).</summary>
    [Fact]
    public void 만든_압축_시험이_문제를_찾으면_한국어_예외()
    {
        using var tmp = new TempDir("tcreated");
        string arc = CorruptArchiveTests.MakeCrcBroken(tmp);
        var ex = Assert.Throws<ArchiveTestFailedException>(() => CreatedArchiveTester.Instance.Test(arc, null, CancellationToken.None));
        Assert.False(ex.Report.Ok);
        Assert.Contains("파일 1개에 문제가 있어요: ‘", ex.Message);
        Assert.Contains("readme.txt’(내용이 원래와 달라요(검사값 불일치))", ex.Message);
        AssertKoreanOnly(ErrorMessages.ToPlainMessage(ex), "created");
        Assert.Equal(ProblemKind.Corrupt, ErrorCatalog.Describe(ex).Kind);   // 손상의 한 종류(화면이 '시험에서 문제가 있어 원본을 지우지 않았어요'로 가름)
    }

    // ───────────── 도우미 ─────────────

    internal static byte[] Damage(byte[] b, string how) => how switch
    {
        "half" => b[..(b.Length / 2)],
        "tail30" => b[..(b.Length - 30)],
        "xor200" => Xor(b, 200, 260),
        "xor100" => Xor(b, 100, 140),
        "garbage" => Enumerable.Range(0, 5000).Select(i => (byte)(i * 7 + 3)).ToArray(),
        _ => throw new ArgumentException(how),
    };

    private static byte[] Xor(byte[] b, int from, int to)
    {
        var c = (byte[])b.Clone();
        for (int i = from; i < to && i < c.Length; i++) c[i] ^= 0x5A;
        return c;
    }
}
