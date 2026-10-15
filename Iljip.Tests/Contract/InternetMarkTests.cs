using System.Text;
using Iljip.Models;
using Iljip.Services;
using Iljip.Tests.TestSupport;
using Iljip.ViewModels;

namespace Iljip.Tests.Contract;

/// <summary>
/// [IJ3-03 — 사용자 결정 I2-15] 인터넷에서 받은 압축(NTFS 대체 스트림 <c>:Zone.Identifier</c> — '인터넷에서 받은 파일' 표시, MOTW)을
/// 일집 창·탐색기 우클릭으로 풀면 풀린 파일에도 그 표시를 원본 내용 그대로(ZoneId·ReferrerUrl·HostUrl) 붙인다 — 오피스 '제한된 보기' 유지.
/// <list type="bullet">
/// <item>붙이는 것은 이번 풀기가 만든(덮어쓴) 파일뿐 — 풀 곳에 원래 있던 파일·같은 이름 [건너뛰기]한 파일·같은 폴더의 압축 파일은 그대로.</item>
/// <item>다 풀었을 때뿐 아니라 손상으로 멈춰 앞서 풀린 파일이 남아도 붙인다. 암호를 다시 물어 풀어도, 다 풀고 압축을 휴지통으로 보내도 붙는다.</item>
/// <item>모든 해제 길(SharpCompress 직접 쓰기·7z.dll 숨김 임시 폴더·압축된 tar·EGG/ALZ·RAR·raw gz/bz2)에서 같다.</item>
/// <item>표시를 쓰면 NTFS 가 수정 시각을 '지금'으로 바꾸므로 압축에 적힌 시각으로 되돌린다(읽기 전용도 그대로).</item>
/// <item>설정 extract.keepInternetMark(기본 켬)를 끄면 풀기·열기·Ctrl+C·끌기 모두 붙이지 않는다. 설정 창 칸은 <see cref="CompressDialogXamlTests"/>.</item>
/// <item>창 없이 풀기(--extract-to — 재사용장치)는 붙이지 않는다: 실제 exe 로 <see cref="ReuseDeviceContractTests"/>, 서비스 수준은 <see cref="KnownGapTests"/> 기록.</item>
/// </list>
/// 창 없이(가짜 <see cref="FakeUserInteraction"/>·가짜 셸·가짜 휴지통) — 진짜 탐색기·휴지통·오피스는 부르지 않는다(오피스 '제한된 보기'는 손 점검 MANUAL_CHECKLIST 21절).
/// </summary>
public class InternetMarkTests
{
    /// <summary>메일·웹에서 받은 파일에 Windows 가 붙이는 모양(한글 주소 포함 — 바이트 그대로 옮기는지 보려고).</summary>
    private static readonly byte[] Mark = Encoding.UTF8.GetBytes(
        "[ZoneTransfer]\r\nZoneId=3\r\nReferrerUrl=https://mail.example.com/inbox?id=7\r\nHostUrl=https://files.example.com/받은 자료/견적.zip\r\n");

    private static void SetMark(string path) => File.WriteAllBytes(path + ZoneIdentifier.StreamName, Mark);

    private static byte[]? MarkOf(string path) => ZoneIdentifier.Read(path);

    private static IEnumerable<string> FilesUnder(string dir) => Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories);

    private static void AssertAllMarked(string dir, string context)
    {
        var files = FilesUnder(dir).ToList();
        Assert.NotEmpty(files);
        foreach (var f in files)
            Assert.True(MarkOf(f) is { } m && m.SequenceEqual(Mark), $"{context}: 표시가 없거나 원본과 다름: {f}");
    }

    private static void AssertNoneMarked(string dir, string context)
    {
        foreach (var f in FilesUnder(dir))
            Assert.True(MarkOf(f) is null, $"{context}: 표시가 붙음: {f}");
    }

    private static string CopyFixture(string folder, string id, string? name = null)
    {
        var fx = FixtureCatalog.Get(id);
        string dst = Path.Combine(folder, name ?? Path.GetFileName(fx.FullPath));
        File.Copy(fx.FullPath, dst);
        return dst;
    }

    private static Task<bool> Extract(MainViewModel vm, string archive, string dest, IReadOnlyCollection<string>? selected = null)
    {
        bool ok = false;
        return VmRunner.Run(async () => ok = await vm.ExtractToFolderAsync(archive, dest, selected), "풀기").ContinueWith(t =>
        {
            t.GetAwaiter().GetResult();
            return ok;
        });
    }

    private static bool HasMarkingStage(FakeUserInteraction ui) =>
        ui.ProgressSessions.SelectMany(s => s.Reports).Any(r => r.IsStage && r.CurrentFile == ZoneIdentifier.StageMarking);

    // ───────────────────────────── 일집 창에서 풀기 ─────────────────────────────

    /// <summary>
    /// 계약: 형식과 상관없이(해제 길마다) 일집 창에서 풀면 풀린 파일마다 압축의 표시가 바이트 그대로 붙는다. 내용은 그대로이고 압축 파일의 표시도 그대로.
    /// 진행 창에 '인터넷에서 받은 파일 표시를 붙이는 중' 단계가 보인다.
    /// </summary>
    [Theory]
    [InlineData("zip_default")]        // SharpCompress 직접 쓰기
    [InlineData("zip_cp949")]
    [InlineData("7z_default")]         // 7z.dll 숨김 임시 폴더에서 옮기기
    [InlineData("lzh_default")]        // 7z.dll 의 다른 형식
    [InlineData("tgz_default")]        // 압축된 tar(안쪽 tar 를 펼쳐 옮기기)
    [InlineData("tar_default")]
    [InlineData("gz_single")]
    [InlineData("bz2_single_python")]  // raw bzip2(장부 없이 쓰는 길 — OutputTarget.Commit)
    [InlineData("xz_single")]
    public async Task 창에서_풀면_풀린_파일마다_표시를_원본_그대로_붙인다(string id)
    {
        var fx = FixtureCatalog.Get(id);
        using var tmp = new TempDir("im");
        string arc = CopyFixture(tmp.Dir("받은 첨부"), id);
        SetMark(arc);
        string dest = tmp.Dir("풀 곳");
        var ui = new FakeUserInteraction();
        var vm = new MainViewModel(ui, null, new TestServices().Services);

        Assert.True(await Extract(vm, arc, dest), $"{id}: 풀기 실패 — {string.Join(" | ", ui.Errors)}");

        Assert.Empty(ui.Errors);
        TreeAssert.ExtractedTreeEquals(dest, fx.Expected, context: id);
        AssertAllMarked(dest, id);
        Assert.Equal(Mark, MarkOf(arc));   // 압축 파일의 표시는 그대로
        Assert.True(HasMarkingStage(ui), $"{id}: 진행 창에 표시 붙이는 단계가 없음");
    }

    /// <summary>계약: EGG·ALZ(EggDotNet)·RAR5·RAR4(7z.dll)도 같다.</summary>
    [Theory]
    [InlineData("egg")]
    [InlineData("alz")]
    [InlineData("rar5")]
    [InlineData("rar4_cp949")]
    public async Task EGG_ALZ_RAR도_표시를_붙인다(string kind)
    {
        using var tmp = new TempDir("imk");
        var b = SyntheticArchives.Build(kind, tmp.Dir("받은 첨부"));
        SetMark(b.Path);
        string dest = tmp.Dir("풀 곳");
        var ui = new FakeUserInteraction();
        var vm = new MainViewModel(ui, null, new TestServices().Services);

        Assert.True(await Extract(vm, b.Path, dest), $"{kind}: 풀기 실패 — {string.Join(" | ", ui.Errors)}");
        TreeAssert.ExtractedTreeEquals(dest, b.Expected, context: kind);
        AssertAllMarked(dest, kind);
    }

    /// <summary>계약: 표시가 없는 압축(내 PC 에서 만든 것)은 아무것도 붙이지 않고 단계 알림도 없다.</summary>
    [Fact]
    public async Task 표시가_없는_압축은_아무것도_붙이지_않는다()
    {
        using var tmp = new TempDir("imnone");
        string arc = CopyFixture(tmp.Dir("내 자료"), "zip_default");
        string dest = tmp.Dir("풀 곳");
        var ui = new FakeUserInteraction();
        Assert.True(await Extract(new MainViewModel(ui, null, new TestServices().Services), arc, dest));
        AssertNoneMarked(dest, "표시 없는 압축");
        Assert.False(HasMarkingStage(ui));
    }

    /// <summary>계약: 설정(extract.keepInternetMark)을 끄면 붙이지 않는다. 기본은 켬이고, 예전 settings.json(칸 없음)을 읽어도 켬.</summary>
    [Fact]
    public async Task 설정을_끄면_붙이지_않는다_기본은_켬()
    {
        Assert.True(new AppSettings().Extract.KeepInternetMark);
        var old = System.Text.Json.JsonSerializer.Deserialize<AppSettings>("{\"schemaVersion\":1,\"extract\":{\"onCollision\":\"Ask\"}}", JsonSettingsStore.Options)!.Normalize();
        Assert.True(old.Extract.KeepInternetMark);

        using var tmp = new TempDir("imoff");
        string arc = CopyFixture(tmp.Dir("받은 첨부"), "7z_default");
        SetMark(arc);
        string dest = tmp.Dir("풀 곳");
        var ui = new FakeUserInteraction();
        var svc = TestServices.With(e => e.KeepInternetMark = false);
        Assert.True(await Extract(new MainViewModel(ui, null, svc.Services), arc, dest));
        TreeAssert.ExtractedTreeEquals(dest, FixtureCatalog.Get("7z_default").Expected);
        AssertNoneMarked(dest, "설정 끔");
        Assert.False(HasMarkingStage(ui));
    }

    /// <summary>계약: 설정 파일에는 extract.keepInternetMark 로 저장되고 끈 값이 그대로 돌아온다.</summary>
    [Fact]
    public void 설정은_extract_keepInternetMark_로_저장된다()
    {
        var s = new AppSettings();
        s.Extract.KeepInternetMark = false;
        string json = System.Text.Json.JsonSerializer.Serialize(s, JsonSettingsStore.Options);
        var node = System.Text.Json.Nodes.JsonNode.Parse(json)!;
        Assert.False(node["extract"]!["keepInternetMark"]!.GetValue<bool>());
        Assert.False(System.Text.Json.JsonSerializer.Deserialize<AppSettings>(json, JsonSettingsStore.Options)!.Extract.KeepInternetMark);
        Assert.False(s.Clone().Extract.KeepInternetMark);
    }

    /// <summary>
    /// 계약: 풀 곳에 원래 있던 파일(압축과 상관없는 것·같은 이름 [건너뛰기]한 것)에는 붙이지 않고, 이번에 만든 파일과 [덮어쓰기]로 새 내용이 된 파일에만 붙인다.
    /// SharpCompress 길(ZIP)과 7z.dll 길(7Z — 임시 폴더에서 옮기며 덮어씀) 모두.
    /// </summary>
    [Theory]
    [InlineData("zip")]
    [InlineData("7z")]
    public async Task 원래_있던_파일과_건너뛴_파일에는_붙이지_않는다(string format)
    {
        using var tmp = new TempDir("imcol");
        string src = tmp.Dir("원본");
        foreach (var n in new[] { "a.txt", "b.txt", "c.txt" }) File.WriteAllText(Path.Combine(src, n), "압축 속 " + n);
        string arc = Path.Combine(tmp.Dir("받은 첨부"), "받은." + format);
        var files = new[] { "a.txt", "b.txt", "c.txt" }.Select(n => Path.Combine(src, n)).ToArray();
        if (format == "zip") await new ZipArchiveService().CompressAsync(files, arc);
        else await new SevenZipArchiveService().CompressAsync(files, arc);
        SetMark(arc);

        string dest = tmp.Dir("풀 곳");
        File.WriteAllText(Path.Combine(dest, "a.txt"), "사용자 a");
        File.WriteAllText(Path.Combine(dest, "b.txt"), "사용자 b");
        File.WriteAllText(Path.Combine(dest, "상관없는 문서.txt"), "그대로");
        var ui = new FakeUserInteraction();
        ui.CollisionAnswers.Enqueue(new CollisionAnswer(CollisionChoice.Skip, false));
        ui.CollisionAnswers.Enqueue(new CollisionAnswer(CollisionChoice.Overwrite, false));

        Assert.True(await Extract(new MainViewModel(ui, null, new TestServices().Services), arc, dest));

        Assert.Equal(2, ui.CollisionPrompts.Count);
        Assert.Equal("사용자 a", File.ReadAllText(Path.Combine(dest, "a.txt")));        // 건너뜀 — 사용자 파일 그대로
        Assert.Null(MarkOf(Path.Combine(dest, "a.txt")));
        Assert.Equal("압축 속 b.txt", File.ReadAllText(Path.Combine(dest, "b.txt")));   // 덮어씀 — 인터넷에서 온 내용
        Assert.Equal(Mark, MarkOf(Path.Combine(dest, "b.txt")));
        Assert.Equal(Mark, MarkOf(Path.Combine(dest, "c.txt")));
        Assert.Null(MarkOf(Path.Combine(dest, "상관없는 문서.txt")));
        Assert.Equal(4, FilesUnder(dest).Count());
        CollisionTests.NoLeftovers(dest, format);
    }

    /// <summary>
    /// 계약: 표시를 붙여도 풀린 파일의 수정 시각은 압축에 적힌 시각 그대로다(NTFS 는 대체 스트림을 쓰면 수정 시각을 '지금'으로 바꿈 — 되돌림).
    /// ZIP(SharpCompress)·7Z(7z.dll) 모두.
    /// </summary>
    [Theory]
    [InlineData("zip")]
    [InlineData("7z")]
    public async Task 표시를_붙여도_수정_시각은_압축에_적힌_그대로다(string format)
    {
        using var tmp = new TempDir("imtime");
        string src = tmp.Dir("원본");
        var when = new DateTime(2024, 5, 6, 7, 8, 10, DateTimeKind.Local);
        foreach (var n in new[] { "보고서.docx", "표.xlsx" })
        {
            string f = Path.Combine(src, n);
            File.WriteAllText(f, "내용 " + n);
            File.SetLastWriteTime(f, when);
        }
        string arc = Path.Combine(tmp.Dir("받은 첨부"), "받은." + format);
        var files = Directory.GetFiles(src);
        if (format == "zip") await new ZipArchiveService().CompressAsync(files, arc);
        else await new SevenZipArchiveService().CompressAsync(files, arc);
        SetMark(arc);
        string dest = tmp.Dir("풀 곳");

        Assert.True(await Extract(new MainViewModel(new FakeUserInteraction(), null, new TestServices().Services), arc, dest));

        AssertAllMarked(dest, format);
        foreach (var f in FilesUnder(dest))
            Assert.Equal(when, File.GetLastWriteTime(f));
    }

    /// <summary>계약: 고른 항목만 풀어도 그 파일들에 붙인다(고르지 않은 것은 풀리지 않음).</summary>
    [Fact]
    public async Task 고른_항목만_풀어도_붙인다()
    {
        using var tmp = new TempDir("imsel");
        string arc = CopyFixture(tmp.Dir("받은 첨부"), "zip_default");
        SetMark(arc);
        string dest = tmp.Dir("풀 곳");
        var vm = new MainViewModel(new FakeUserInteraction(), null, new TestServices().Services);
        Assert.True(await Extract(vm, arc, dest, new[] { "일집_샘플/readme.txt" }));
        string readme = Assert.Single(FilesUnder(dest));
        Assert.EndsWith("readme.txt", readme);
        Assert.Equal(Mark, MarkOf(readme));
    }

    /// <summary>계약: 암호를 물어 다시 풀어도(처음 시도는 되돌림) 마지막으로 푼 파일에 붙는다.</summary>
    [Fact]
    public async Task 암호를_다시_물어_풀어도_붙인다()
    {
        var fx = FixtureCatalog.Get("zip_aes256");
        using var tmp = new TempDir("impw");
        string arc = CopyFixture(tmp.Dir("받은 첨부"), "zip_aes256");
        SetMark(arc);
        string dest = tmp.Dir("풀 곳");
        var ui = new FakeUserInteraction("틀린 암호", fx.Password);
        Assert.True(await Extract(new MainViewModel(ui, null, new TestServices().Services), arc, dest));
        Assert.Equal(2, ui.PasswordPrompts.Count);
        TreeAssert.ExtractedTreeEquals(dest, fx.Expected);
        AssertAllMarked(dest, "암호 ZIP");
    }

    /// <summary>
    /// 계약: 손상으로 중간에 멈추면(쓰다 만 파일은 지움 — 기존 계약) 앞서 다 풀려 남은 파일에도 붙인다(인터넷에서 온 내용이 그대로 남으므로).
    /// </summary>
    [Fact]
    public async Task 손상으로_멈춰도_앞서_풀린_파일에는_붙인다()
    {
        using var tmp = new TempDir("imbad");
        string arc = TestZip.Create(Path.Combine(tmp.Dir("받은 첨부"), "깨진.zip"),
            TestZip.Text("먼저.txt", "앞 파일"),
            TestZip.Bytes("뒤.bin", 300_000, seed: 3));
        var bytes = File.ReadAllBytes(arc);
        bytes[bytes.Length / 2] ^= 0xFF;   // 뒤 파일의 내용 한가운데
        File.WriteAllBytes(arc, bytes);
        SetMark(arc);
        string dest = tmp.Dir("풀 곳");
        var ui = new FakeUserInteraction();

        Assert.False(await Extract(new MainViewModel(ui, null, new TestServices().Services), arc, dest));

        Assert.Single(ui.Errors);
        string first = Path.Combine(dest, "먼저.txt");
        Assert.Equal("앞 파일", File.ReadAllText(first));
        Assert.False(File.Exists(Path.Combine(dest, "뒤.bin")), "쓰다 만 파일이 남음");
        Assert.Equal(Mark, MarkOf(first));
    }

    /// <summary>계약: 다 풀고 압축 파일을 휴지통으로 보내도(설정) 풀린 파일에는 붙어 있다 — 표시는 풀기 전에 읽어 둔다.</summary>
    [Fact]
    public async Task 다_풀고_압축을_휴지통으로_보내도_붙는다()
    {
        using var tmp = new TempDir("imrb");
        string arc = CopyFixture(tmp.Dir("받은 첨부"), "zip_default");
        SetMark(arc);
        string dest = tmp.Dir("풀 곳");
        var svc = TestServices.With(e => e.DeleteArchiveAfter = true);
        svc.RecycleBin.OnSend = paths => { foreach (var p in paths) File.Delete(p); };   // 진짜 휴지통처럼 사라지게

        Assert.True(await Extract(new MainViewModel(new FakeUserInteraction(), null, svc.Services), arc, dest));

        Assert.Equal(new[] { arc }, Assert.Single(svc.RecycleBin.Sent));
        Assert.False(File.Exists(arc));
        AssertAllMarked(dest, "휴지통으로 보낸 압축");
    }

    // ───────────────────────────── 탐색기 우클릭(여러 개 풀기 길) ─────────────────────────────

    /// <summary>
    /// 계약: 탐색기 우클릭 풀기('여기에 풀기'·'"이름"\ 에 풀기'·알아서 풀기)도 붙인다 — 압축마다 자기 표시를: 표시가 있는 압축에서 나온 파일에만,
    /// 표시가 없는 압축에서 나온 파일과 같은 폴더의 압축 파일 자체는 그대로. 진행 창에 단계 알림.
    /// </summary>
    [Theory]
    [InlineData("--extract-here")]
    [InlineData("--extract-to-folder")]
    [InlineData("--extract-smart")]
    public async Task 우클릭으로_풀어도_표시가_있는_압축에서_나온_파일에만_붙인다(string verb)
    {
        using var tmp = new TempDir("imsh");
        string work = tmp.Dir("다운로드");
        string marked = CopyFixture(work, "zip_default", "받은.zip");
        SetMark(marked);
        // 표시 없는 압축(내 PC 에서 만든 7z — 맨 위 폴더 '내 메모' 하나, 받은 압축과 이름이 겹치지 않게)
        string memo = tmp.Dir("원본", "내 메모");
        File.WriteAllText(Path.Combine(memo, "메모.txt"), "내가 쓴 메모");
        File.WriteAllText(Path.Combine(memo, "할 일.txt"), "내가 쓴 할 일");
        string plain = Path.Combine(work, "내것.7z");
        await new SevenZipArchiveService().CompressAsync(new[] { memo }, plain);
        var ui = new FakeUserInteraction();
        var vm = new MainViewModel(ui, null, new TestServices().Services);

        await VmRunner.Run(() => vm.ProcessShellCommandAsync(new[] { verb, marked, plain }), verb);

        Assert.Empty(ui.Errors);
        // 둘 다 맨 위가 폴더 하나 — 여기에·알아서는 그 폴더가 압축 옆에 그대로, '이름' 폴더 풀기는 압축 이름 폴더 안에
        string zipRoot = verb == "--extract-to-folder" ? Path.Combine(work, "받은") : work;
        string sevenRoot = verb == "--extract-to-folder" ? Path.Combine(work, "내것") : work;
        var fromZip = FixtureCatalog.Get("zip_default").Expected.Where(e => !e.IsDir).Select(e => Path.Combine(zipRoot, e.Path)).ToList();
        var from7z = new[] { "내 메모/메모.txt", "내 메모/할 일.txt" }.Select(p => Path.Combine(sevenRoot, p)).ToList();
        Assert.All(fromZip, f => Assert.True(MarkOf(f) is { } m && m.SequenceEqual(Mark), $"{verb}: 표시가 없음: {f}"));
        Assert.All(from7z, f => Assert.True(File.Exists(f) && MarkOf(f) is null, $"{verb}: 표시 없는 압축에서 나온 파일에 붙음(또는 없음): {f}"));
        Assert.Equal(Mark, MarkOf(marked));
        Assert.Null(MarkOf(plain));   // 같은 폴더의 압축 파일 자체는 건드리지 않음
        Assert.True(HasMarkingStage(ui), $"{verb}: 진행 창에 단계 알림이 없음");
    }

    /// <summary>계약: 우클릭 풀기도 설정을 끄면 붙이지 않는다.</summary>
    [Fact]
    public async Task 우클릭_풀기도_설정을_끄면_붙이지_않는다()
    {
        using var tmp = new TempDir("imshoff");
        string work = tmp.Dir("다운로드");
        string arc = CopyFixture(work, "zip_default", "받은.zip");
        SetMark(arc);
        var svc = TestServices.With(e => e.KeepInternetMark = false);
        var vm = new MainViewModel(new FakeUserInteraction(), null, svc.Services);
        await VmRunner.Run(() => vm.ProcessShellCommandAsync(new[] { "--extract-to-folder", arc }), "우클릭 풀기");
        string dest = Path.Combine(work, "받은");
        TreeAssert.ExtractedTreeEquals(dest, FixtureCatalog.Get("zip_default").Expected);
        AssertNoneMarked(dest, "설정 끔");
    }

    /// <summary>계약: 여러 개 풀기(BatchExtractor)는 기본으로 붙이고, 화면이 설정을 넘긴다.</summary>
    [Fact]
    public void 여러_개_풀기는_화면이_설정을_넘긴다()
    {
        var batch = new BatchExtractor(new ArchiveServiceLocator(), new FakeUserInteraction(), new FixedResolver(CollisionChoice.Rename), ExtractTargetRule.Here, Array.Empty<string>());
        Assert.True(batch.KeepInternetMark);
        string code = File.ReadAllText(RepoFiles.PathOf("Iljip", "ViewModels", "MainViewModel.CommandLine.cs"));
        Assert.Contains("KeepInternetMark = Settings.Extract.KeepInternetMark", code);
    }

    // ───────────────────────────── 서비스 단 · 범위 ─────────────────────────────

    /// <summary>
    /// 계약: 같은 이름 처리 범위가 '이번에 만든 파일'을 모든 해제 길에서 적는다 — raw bzip2(장부 없이 쓰는 길)도.
    /// 범위가 없으면(창 없이 풀기) 서비스는 표시를 붙이지 않는다.
    /// </summary>
    [Fact]
    public async Task 범위는_raw_bzip2로_만든_파일도_적고_서비스는_표시를_붙이지_않는다()
    {
        var fx = FixtureCatalog.Get("bz2_single_python");
        using var tmp = new TempDir("imscope");
        string arc = CopyFixture(tmp.Dir("받은 첨부"), "bz2_single_python");
        SetMark(arc);
        string dest = tmp.Dir("풀 곳");
        var service = new ArchiveServiceLocator().ResolveByContent(arc);
        Assert.IsType<BZip2ArchiveService>(service);   // 화면이 쓰는 처리기 — 컨테이너가 아닌 bz2 는 raw 길(장부 없이 OutputTarget 만)
        IReadOnlyList<string> created;
        using (var scope = ExtractionScope.Begin(new FixedResolver(CollisionChoice.Rename)))
        {
            await service!.ExtractAsync(arc, dest);
            created = scope.CreatedFiles;
        }
        string only = Assert.Single(FilesUnder(dest));
        Assert.Equal(new[] { Path.GetFullPath(only) }, created);
        Assert.Null(MarkOf(only));   // 서비스는 붙이지 않음 — 화면이 붙임
        TreeAssert.ExtractedTreeEquals(dest, fx.Expected);
    }

    /// <summary>계약: 붙이는 도우미는 표시가 없으면(null) 아무것도 하지 않고, 범위에 적힌 파일 중 지금 있는 것에만 붙인다(지워진 것은 건너뜀).</summary>
    [Fact]
    public async Task 붙이는_도우미는_범위의_있는_파일에만_붙인다()
    {
        using var tmp = new TempDir("imhelp");
        string a = tmp.P("a.txt"), gone = tmp.P("gone.txt"), other = tmp.P("other.txt");
        File.WriteAllText(a, "a");
        File.WriteAllText(other, "o");
        using var scope = ExtractionScope.Begin(new FixedResolver(CollisionChoice.Rename));
        scope.NoteCreated(a);
        scope.NoteCreated(gone);
        Assert.Equal(0, await ZoneIdentifier.MarkExtractedAsync(null, scope));
        Assert.Null(MarkOf(a));
        var progress = new ProgressRecorder();
        Assert.Equal(1, await ZoneIdentifier.MarkExtractedAsync(Mark, scope, progress));
        Assert.Equal(Mark, MarkOf(a));
        Assert.Null(MarkOf(other));
        Assert.Contains(progress.Reports, r => r.IsStage && r.CurrentFile == ZoneIdentifier.StageMarking);
    }

    // ───────────────────────────── 열기·Ctrl+C·끌기도 설정을 따른다 ─────────────────────────────

    /// <summary>계약: 압축 속 파일 열기는 기본으로 붙이고(IJ3-01), 설정을 끄면 붙이지 않는다.</summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task 열기는_설정을_따른다(bool keep)
    {
        using var tmp = new TempDir("imopen");
        using var _ = TestServices.UseAppRoot(tmp);
        string arc = CopyFixture(tmp.Dir("받은 첨부"), "zip_default", "메일첨부.zip");
        SetMark(arc);
        var ts = TestServices.With(e => e.KeepInternetMark = keep);
        var vm = new MainViewModel(new FakeUserInteraction(), services: ts.Services);
        await VmRunner.Run(async () =>
        {
            await vm.OpenArchiveAsync(arc);
            Assert.True(await vm.OpenEntryAsync(vm.Entries.Single(e => TreeAssert.NormalizeEntryPath(e.Path) == "일집_샘플/readme.txt")));
        }, "열기");
        string opened = Assert.Single(ts.Shell.OpenedFiles);
        if (keep) Assert.Equal(Mark, MarkOf(opened));
        else Assert.Null(MarkOf(opened));
        vm.Shutdown();
    }

    /// <summary>계약: Ctrl+C 로 꺼낸 사본은 기본으로 붙이고(IJ3-01), 설정을 끄면 붙이지 않는다.</summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Ctrl_C_는_설정을_따른다(bool keep)
    {
        using var tmp = new TempDir("imclip");
        using var _ = TestServices.UseAppRoot(tmp);
        string arc = CopyFixture(tmp.Dir("받은 첨부"), "zip_default", "받은.zip");
        SetMark(arc);
        var ts = TestServices.With(e => e.KeepInternetMark = keep);
        var vm = new MainViewModel(new FakeUserInteraction(), services: ts.Services);
        await VmRunner.Run(async () =>
        {
            await vm.OpenArchiveAsync(arc);
            vm.NavigateToFolder("일집_샘플");
            Assert.True(await vm.CopyEntriesToClipboardAsync(vm.VisibleEntries.Where(e => e.Name is "하위폴더" or "readme.txt").ToList()));
        }, "Ctrl+C");
        var paths = Assert.Single(ts.Clipboard.Calls).Paths;
        var copied = paths.SelectMany(p => Directory.Exists(p) ? FilesUnder(p) : new[] { p }).ToList();
        Assert.True(copied.Count >= 2);
        foreach (var f in copied)
        {
            if (keep) Assert.Equal(Mark, MarkOf(f));
            else Assert.Null(MarkOf(f));
        }
    }

    /// <summary>
    /// 계약: 끌어 꺼내기는 기본으로 끌기 데이터에 영역(3)을 알리고 꺼낸 임시 파일에 붙이며(IJ3-01), 설정을 끄면 영역을 알리지 않고(형식 자체를 주지 않음) 붙이지도 않는다.
    /// </summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task 끌기는_설정을_따른다(bool keep)
    {
        using var tmp = new TempDir("imdrag");
        using var _ = TestServices.UseAppRoot(tmp);
        string arc = CopyFixture(tmp.Dir("받은 첨부"), "zip_default", "받은.zip");
        SetMark(arc);
        var ts = TestServices.With(e => e.KeepInternetMark = keep);
        var vm = new MainViewModel(new FakeUserInteraction(), services: ts.Services);
        VirtualFileDataObject? data = null;
        await VmRunner.Run(async () =>
        {
            await vm.OpenArchiveAsync(arc);
            vm.NavigateToFolder("일집_샘플");
            data = vm.CreateDragData(vm.VisibleEntries.Where(e => e.Name == "readme.txt").ToList());
        }, "끌기 데이터");
        Assert.NotNull(data);
        try
        {
            Assert.Equal(keep ? 3 : (int?)null, data!.ZoneId);
            var f = VirtualFileDataObject.Format(VirtualFileDataObject.CfZoneIdentifier, System.Runtime.InteropServices.ComTypes.TYMED.TYMED_HGLOBAL, -1);
            Assert.Equal(keep ? 0 : VirtualFileDataObject.DV_E_FORMATETC, data.QueryGetData(ref f));
            int readme = data.Items.ToList().FindIndex(i => i.RelativePath == "readme.txt");
            string path = await Task.Run(() => data.Extractor.GetContent(readme)).WaitAsync(TimeSpan.FromSeconds(60));
            if (keep) Assert.Equal(Mark, MarkOf(path));
            else Assert.Null(MarkOf(path));
        }
        finally
        {
            vm.Shutdown();
        }
    }
}
