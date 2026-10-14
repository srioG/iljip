using System.Text;
using Iljip.Models;
using Iljip.Services;
using Iljip.Tests.TestSupport;
using Iljip.ViewModels;

namespace Iljip.Tests.Contract;

/// <summary>
/// [Wave 2 통합 점검(2026-10-14)] 묶음 사이 상호작용 — 묶음마다의 시험은 자기 기능만 보므로, 함께 쓰일 때 어긋나지 않는지 본다.
/// 화면은 가짜(<see cref="FakeUserInteraction"/>), 셸·휴지통·설정도 가짜(<see cref="TestServices"/> — 진짜 탐색기·휴지통은 절대 부르지 않음).
/// <list type="bullet">
/// <item>암호 흐름(W1-C·W2-02) × 우클릭 풀기(W2-05)·알아서 풀기(W2-12)·풀기 창(W2-09): 형식(ZIP·7z·EGG·RAR4·RAR5, 이름 보임·목록 잠김)과 상관없이 같은 차례.</item>
/// <item>여러 개 풀기(W2-05) × 같은 이름 묻기(W2-05) × 풀고 지우기(W2-05) × 7z.dll 해제 길(W2-01): ZIP·7z·EGG 를 한 폴더에 — 묻기는 한 번, 건너뛴 압축은 지우지 않음.</item>
/// <item>서로 다른 형식·같은 암호 여러 개 풀기: 한 번만 묻는다(목록 잠긴 7z·RAR 이 섞여도).</item>
/// <item>나눠 만들기(W2-03) × 다 만들고 시험하기·원본 지우기(W2-07) × 7z 만들기(W2-02): 진짜 시험기가 조각을 이어 시험하고, 망가진 조각은 잡는다.</item>
/// <item>압축 속 파일 열기·다시 넣기(W2-06) × 압축 고치기(W2-06): 연 채로 넣기·이름 바꾸기를 해도 다시 넣기가 그 고치기를 잃지 않는다.</item>
/// <item>내용으로 고르기(W2-01) × 창 없이 풀기: 이름이 바뀐 ZIP(CP949)·EGG·ALZ 첨부도 7z.dll 이 아니라 원래 처리기로 — 한글 이름 그대로.</item>
/// </list>
/// 설정 기억(W2-05·W2-07·W2-09)이 창 없이 풀기(재사용장치)에 새지 않는 것은 실제 exe 로 <see cref="ReuseDeviceContractTests"/>, 소스로 <see cref="RecycleAfterExtractTests"/>.
/// </summary>
public class Wave2IntegrationTests
{
    private sealed record Case(string Path, string? Password, bool HeaderEncrypted, List<ExpectedEntry> Expected);

    /// <summary>"fx:픽스처id" 는 픽스처 사본, "syn:종류" 는 <see cref="SyntheticArchives"/> 로 작업 폴더(압축 하나만 있는 폴더)에 만든다.</summary>
    private static Case Make(string spec, TempDir tmp)
    {
        string work = tmp.Dir("작업 폴더");
        if (spec.StartsWith("fx:", StringComparison.Ordinal))
        {
            var fx = FixtureCatalog.Get(spec[3..]);
            string dst = Path.Combine(work, Path.GetFileName(fx.FullPath));
            File.Copy(fx.FullPath, dst);
            return new Case(dst, fx.Password, fx.HeaderEncrypted, fx.Expected.ToList());
        }
        var b = SyntheticArchives.Build(spec[4..], work);
        return new Case(b.Path, b.Password, b.HeaderEncrypted, b.Expected);
    }

    private static Task RunShell(MainViewModel vm, params string[] args) =>
        VmRunner.Run(() => vm.ProcessShellCommandAsync(args), $"ProcessShellCommandAsync({string.Join(" ", args)})");

    private static Task RunVm(MainViewModel vm, params string[] args) =>
        VmRunner.Run(() => vm.ProcessCommandLineAsync(args), $"ProcessCommandLineAsync({string.Join(" ", args)})");

    /// <summary>암호 압축 종류(형식 × 이름 보임·목록 잠김 × 한글 암호).</summary>
    public static readonly string[] PasswordSpecs =
    {
        "fx:zip_zipcrypto", "fx:zip_aes256", "fx:zip_zipcrypto_korean_pw", "fx:zip_aes256_korean_pw",
        "fx:py7z_pw_names_visible", "fx:7z_header_encrypted", "fx:7z_header_encrypted_korean_pw",
        "syn:egg_enc", "syn:rar5_enc", "syn:rar5_hdr", "syn:rar4_enc", "syn:rar4_hdr",
    };

    public static TheoryData<string, string> PasswordSpecsAndShellVerbs
    {
        get
        {
            var d = new TheoryData<string, string>();
            foreach (var s in PasswordSpecs)
                foreach (var v in new[] { "--extract-to-folder", "--extract-here", "--extract-smart" })
                    d.Add(s, v);
            return d;
        }
    }

    public static TheoryData<string> PasswordSpecData
    {
        get
        {
            var d = new TheoryData<string>();
            foreach (var s in PasswordSpecs) d.Add(s);
            return d;
        }
    }

    /// <summary>
    /// 계약: 탐색기 우클릭 풀기(메인 창 없이 — '"이름"\ 에 풀기'·'여기에 풀기'·W2-12 '알아서 풀기')에서도 암호 흐름이 형식과 상관없이 같다 —
    /// 처음엔 '암호가 걸린…'(목록 잠김이면 '파일 목록까지…'), 틀린 암호면 '암호가 맞지 않아요…'로 다시 묻고, 맞으면 풀린다(오류·요약 창 없음).
    /// 틀린 시도는 아무것도 남기지 않는다('이름 (2)' 폴더·임시 파일 없음). 메인 창 흐름은 <see cref="FormatFlowTests"/> 가 같은 표로 본다.
    /// 알아서 풀기는 맨 위가 하나면 바로, 여럿이면 '이름' 폴더(<see cref="SmartExtractPlanner"/>).
    /// </summary>
    [Theory]
    [MemberData(nameof(PasswordSpecsAndShellVerbs))]
    public async Task 우클릭_풀기의_암호_흐름은_형식과_상관없이_같다(string spec, string verb)
    {
        using var tmp = new TempDir("w2ipw");
        var c = Make(spec, tmp);
        string work = Path.GetDirectoryName(c.Path)!;
        var ui = new FakeUserInteraction("틀린암호", c.Password!);
        var svc = new TestServices();

        await RunShell(new MainViewModel(ui, null, svc.Services), verb, c.Path);

        string context = $"{spec} {verb}";
        Assert.True(ui.Errors.Count == 0, $"{context}: 오류 창 — {string.Join(" / ", ui.Errors)}");
        Assert.Empty(ui.Summaries);
        var first = c.HeaderEncrypted ? PasswordPromptReason.ListLocked : PasswordPromptReason.Required;
        Assert.Equal(new[] { first, PasswordPromptReason.Wrong }, ui.PasswordPrompts.Select(p => p.Reason).ToArray());
        Assert.All(ui.PasswordPrompts, p => Assert.Equal(Path.GetFileName(c.Path), p.ArchiveName));
        Assert.Equal(c.HeaderEncrypted ? ListPasswordRequiredException.ListMessage : PasswordRequiredException.DefaultMessage, ui.PasswordPrompts[0].Message);
        Assert.StartsWith(WrongPasswordException.DefaultMessage, ui.PasswordPrompts[1].Message);
        Assert.Empty(ui.Warnings);   // 우클릭 풀기는 틀린 암호를 암호 창 문구로만 알린다(형식과 상관없이)
        Assert.Empty(svc.RecycleBin.Sent);

        var tops = c.Expected.Select(e => e.Path.Split('/')[0]).Distinct(StringComparer.OrdinalIgnoreCase).Count();
        bool here = verb == "--extract-here" || (verb == "--extract-smart" && tops == 1);
        string folder = ArchiveNaming.FolderNameFor(c.Path);
        if (here)
        {
            var self = new ExpectedEntry(Path.GetFileName(c.Path), new FileInfo(c.Path).Length, TreeAssert.Sha256(c.Path), false);
            TreeAssert.ExtractedTreeEquals(work, c.Expected.Append(self), context: context);
        }
        else
        {
            TreeAssert.ExtractedTreeEquals(Path.Combine(work, folder), c.Expected, context: context);
            Assert.False(Directory.Exists(Path.Combine(work, folder + " (2)")), $"{context}: 틀린 시도가 폴더를 남겨 번호가 밀림");
        }
        CollisionTests.NoLeftovers(work, context);
    }

    /// <summary>
    /// 계약: 일집 안 [압축 풀기](W2-09 풀기 창 — '--extract')에서도 암호 흐름이 같다: 이름 보임이면 풀 때 '암호가 걸린…' → 틀리면 '맞지 않아요'로 다시,
    /// 목록 잠김이면 열 때 '파일 목록까지…' → 틀리면 경고 한 번 뒤 다시(<see cref="ListPasswordFlowTests"/> 와 같음) — 받은 암호로 풀어 다시 묻지 않는다.
    /// 풀기 창에서 고른 폴더에 바로 풀리고(압축 하나면 '이름' 폴더 없음 — 예전 폴더 고르기 창 흐름), 틀린 시도는 아무것도 남기지 않는다.
    /// </summary>
    [Theory]
    [MemberData(nameof(PasswordSpecData))]
    public async Task 풀기_창으로_풀어도_암호_흐름은_형식과_상관없이_같다(string spec)
    {
        using var tmp = new TempDir("w2ipwx");
        var c = Make(spec, tmp);
        string dest = tmp.Dir("고른 폴더");
        var ui = new FakeUserInteraction("틀린암호", c.Password!) { FolderToPick = dest };
        var svc = new TestServices();

        await RunVm(new MainViewModel(ui, null, svc.Services), "--extract", c.Path);

        Assert.True(ui.Errors.Count == 0, $"{spec}: 오류 창 — {string.Join(" / ", ui.Errors)}");
        Assert.Single(ui.ExtractTargetPrompts);
        var first = c.HeaderEncrypted ? PasswordPromptReason.ListLocked : PasswordPromptReason.Required;
        Assert.Equal(new[] { first, PasswordPromptReason.Wrong }, ui.PasswordPrompts.Select(p => p.Reason).ToArray());
        Assert.StartsWith(WrongPasswordException.DefaultMessage, ui.PasswordPrompts[1].Message);
        if (c.HeaderEncrypted) Assert.Equal(WrongPasswordException.DefaultMessage, Assert.Single(ui.Warnings));
        else Assert.Empty(ui.Warnings);
        TreeAssert.ExtractedTreeEquals(dest, c.Expected, context: spec);
        CollisionTests.NoLeftovers(dest, spec);
    }

    /// <summary>
    /// 계약: 서로 다른 형식인데 암호가 같은 압축 여럿(목록 잠긴 7z 가 맨 앞, ZIP AES·EGG·머리 암호 RAR5·이름 보이는 7z)을 우클릭으로 한꺼번에 풀면
    /// 처음 하나만 묻고(목록 잠김 — '파일 목록까지…') 나머지는 그 암호를 조용히 써 본다(W2-05 × W2-02). 모두 제 폴더에 맞게 풀린다.
    /// </summary>
    [Fact]
    public async Task 형식이_달라도_같은_암호면_여러_개_풀기는_한_번만_묻는다()
    {
        using var tmp = new TempDir("w2imix");
        string work = tmp.Dir("받은 파일");
        var cases = new List<(string Path, List<ExpectedEntry> Expected)>();
        foreach (var id in new[] { "7z_header_encrypted", "zip_aes256", "py7z_pw_names_visible" })
        {
            var fx = FixtureCatalog.Get(id);
            Assert.Equal(FixtureCatalog.DefaultPassword, fx.Password);
            string dst = Path.Combine(work, Path.GetFileName(fx.FullPath));
            File.Copy(fx.FullPath, dst);
            cases.Add((dst, fx.Expected.ToList()));
        }
        foreach (var kind in new[] { "egg_enc", "rar5_hdr" })
        {
            var b = SyntheticArchives.Build(kind, work);
            Assert.Equal(FixtureCatalog.DefaultPassword, b.Password);
            cases.Add((b.Path, b.Expected));
        }
        var ui = new FakeUserInteraction(FixtureCatalog.DefaultPassword);
        var svc = new TestServices();

        await RunShell(new MainViewModel(ui, null, svc.Services), new[] { "--extract-to-folder" }.Concat(cases.Select(x => x.Path)).ToArray());

        var p = Assert.Single(ui.PasswordPrompts);
        Assert.Equal((Path.GetFileName(cases[0].Path), PasswordPromptReason.ListLocked, ListPasswordRequiredException.ListMessage), p);
        Assert.Empty(ui.Errors);
        Assert.Empty(ui.Summaries);   // 모두 풀었고 폴더를 열었으면 요약 창 없음
        foreach (var (path, expected) in cases)
            TreeAssert.ExtractedTreeEquals(Path.Combine(work, ArchiveNaming.FolderNameFor(path)), expected, context: Path.GetFileName(path));
        Assert.Single(ui.ProgressSessions);
    }

    // ───────────── 여러 개 풀기 × 같은 이름 × 풀고 지우기 × 7z.dll 해제 길 ─────────────

    /// <summary>
    /// 같은 이름·다른 내용의 압축 셋(ZIP — SharpCompress 직접 쓰기, 7z — 7z.dll 임시 폴더에서 옮기기, EGG — EggDotNet)을 <paramref name="dir"/> 에 만든다.
    /// 내용: '자료/견적서.txt'(한글)·'자료/하위/표.csv'·'자료/빈파일.txt' — 맨 위가 '자료' 하나라 알아서 풀기도 바로 푼다. 내용 글자는 압축마다 'A'·'B'·'C'.
    /// </summary>
    private static async Task<List<string>> MakeSameNamed(TempDir tmp, string dir)
    {
        var made = new List<string>();
        foreach (var (tag, ext) in new[] { ("A", ".zip"), ("B", ".7z"), ("C", ".egg") })
        {
            string root = tmp.Dir("원본_" + tag, "자료");
            File.WriteAllText(Path.Combine(root, "견적서.txt"), $"{tag} 견적 — 합성 데이터", new UTF8Encoding(false));
            Directory.CreateDirectory(Path.Combine(root, "하위"));
            File.WriteAllText(Path.Combine(root, "하위", "표.csv"), $"{tag},가,나", new UTF8Encoding(false));
            File.WriteAllBytes(Path.Combine(root, "빈파일.txt"), Array.Empty<byte>());
            string arc = Path.Combine(dir, $"묶음{tag}{ext}");
            switch (ext)
            {
                case ".zip": await new ZipArchiveService().CompressAsync(new[] { root }, arc); break;
                case ".7z": SevenZipTestArchives.SevenZip(root, arc); break;
                default: RawEgg.Write(arc, RawEgg.FromFolder(root)); break;
            }
            made.Add(arc);
        }
        return made;
    }

    /// <summary>
    /// 계약(I1-5 × I2-6 × I2-5): 같은 폴더에 바로 푸는 여러 개 풀기('여기에 풀기'·알아서 풀기)에서 뒤 압축의 같은 이름은 한 번만 묻고('남은 파일도 똑같이' —
    /// 묶음 전체), 형식(ZIP·7z.dll 임시 폴더·EGG)과 상관없이 그 답대로다. 풀고 지우기를 켜 두었으면:
    /// [건너뛰기]면 건너뛴 파일이 있는 압축(뒤의 둘)은 지우지 않고 처음 것만, [덮어쓰기]면 셋 다 휴지통으로(가짜). 임시 파일·'이름 (1)' 없음.
    /// </summary>
    [Theory]
    [InlineData("--extract-here", CollisionChoice.Skip)]
    [InlineData("--extract-here", CollisionChoice.Overwrite)]
    [InlineData("--extract-smart", CollisionChoice.Skip)]
    [InlineData("--extract-smart", CollisionChoice.Overwrite)]
    public async Task 여러_개를_한_폴더에_풀면_같은_이름은_한_번만_묻고_건너뛴_압축은_지우지_않는다(string verb, CollisionChoice choice)
    {
        using var tmp = new TempDir("w2icol");
        string work = tmp.Dir("받은 파일");
        var arcs = await MakeSameNamed(tmp, work);
        var ui = new FakeUserInteraction();
        ui.CollisionAnswers.Enqueue(new CollisionAnswer(choice, ApplyToAll: true));
        var svc = TestServices.With(e => e.DeleteArchiveAfter = true);

        await RunShell(new MainViewModel(ui, null, svc.Services), new[] { verb }.Concat(arcs).ToArray());

        string context = $"{verb} {choice}";
        Assert.True(ui.Errors.Count == 0, $"{context}: 오류 창 — {string.Join(" / ", ui.Errors)}");
        var q = Assert.Single(ui.CollisionPrompts);   // 묶음 전체에 한 번(두 번째 압축의 첫 겹침)
        Assert.StartsWith(Path.Combine(work, "자료"), q.TargetPath, StringComparison.OrdinalIgnoreCase);
        string winner = choice == CollisionChoice.Skip ? "A" : "C";
        Assert.Equal($"{winner} 견적 — 합성 데이터", File.ReadAllText(Path.Combine(work, "자료", "견적서.txt")));
        Assert.Equal($"{winner},가,나", File.ReadAllText(Path.Combine(work, "자료", "하위", "표.csv")));
        var files = TreeAssert.Files(work);
        Assert.DoesNotContain(files, f => f.Contains(" (1)", StringComparison.Ordinal));
        Assert.Equal(arcs.Select(a => Path.GetFileName(a)).Concat(new[] { "자료/견적서.txt", "자료/빈파일.txt", "자료/하위/표.csv" }).OrderBy(x => x, StringComparer.Ordinal).ToList(),
            files);
        CollisionTests.NoLeftovers(work, context);

        var expectedRecycled = choice == CollisionChoice.Skip ? new[] { arcs[0] } : arcs.ToArray();
        Assert.Equal(expectedRecycled, svc.RecycleBin.AllSent);
    }

    // ───────────── 나눠 만들기 × 시험하기 × 원본 지우기 ─────────────

    /// <summary>
    /// 계약(W2-03 × W2-07 × W2-02): 나눠 만든 압축(ZIP AES — PKZIP 나눔 'name.z01 … name.zip', 7Z 한글 암호·파일 이름까지 암호 — 'name.7z.001 …')도
    /// '다 만들고 시험하기'·'원본을 휴지통으로'가 진짜 시험기로 조각을 이어 끝까지 시험하고, 통과하면 묻고(옮기기) 원본(폴더)을 휴지통으로(가짜) 보낸다.
    /// 만든 것을 다시 풀면 원본과 같다. 가운데 조각 하나가 망가지면 진짜 시험기가 잡는다(취소가 아닌 오류).
    /// </summary>
    [Theory]
    [InlineData(".zip")]
    [InlineData(".7z")]
    public async Task 나눠_만든_압축도_시험하고_원본을_지운다(string ext)
    {
        using var tmp = new TempDir("w2isplit" + ext.TrimStart('.'));
        string src = tmp.Dir("원본", "분할 자료");
        var rnd = new Random(20261014);
        var big = new byte[300_000];
        rnd.NextBytes(big);
        File.WriteAllBytes(Path.Combine(src, "난수 자료.bin"), big);
        File.WriteAllText(Path.Combine(src, "견적서.txt"), "한글 견적 — 합성 데이터", new UTF8Encoding(false));
        Directory.CreateDirectory(Path.Combine(src, "빈 폴더"));
        var expected = SampleTree.ExpectedOf(src);
        string outDir = tmp.Dir("만든곳");
        bool sevenZip = ext == ".7z";
        string password = sevenZip ? FixtureCatalog.KoreanPassword : FixtureCatalog.DefaultPassword;
        var options = new CompressionOptions
        {
            Password = password,
            Encryption = ZipEncryption.Aes256,
            EncryptFileNames = sevenZip,
            SplitBytes = 100_000,
        };
        var svc = new TestServices();
        var ui = new FakeUserInteraction
        {
            ChooseByLabel = "옮기기",
            CompressionRequestAnswer = r => new CompressionRequest
            {
                Sources = r.Sources,
                OutputFolder = outDir,
                BaseName = "분할 자료",
                Format = sevenZip ? CompressionFormat.SevenZip : CompressionFormat.Zip,
                Options = options,
                ExcludeEnabled = false,
                ExcludePatterns = r.Defaults.ExcludePatterns,
                TestAfter = true,
                DeleteOriginalsAfter = true,
            },
        };
        var vm = new MainViewModel(ui, null, svc.Services);
        vm.AddToStaging(new[] { src });

        await VmRunner.Run(() => vm.CompressCommand.ExecuteAsync(null), "나눠 압축");

        Assert.True(ui.Errors.Count == 0, "오류 창 — " + string.Join(" / ", ui.Errors));
        Assert.True(ui.Warnings.Count == 0, "경고 창 — " + string.Join(" / ", ui.Warnings));
        var made = Directory.EnumerateFiles(outDir).Select(Path.GetFileName).OrderBy(n => n, StringComparer.Ordinal).ToList();
        string openPath = Path.Combine(outDir, sevenZip ? "분할 자료.7z.001" : "분할 자료.zip");
        Assert.Contains(Path.GetFileName(openPath), made);
        Assert.Contains(sevenZip ? "분할 자료.7z.002" : "분할 자료.z01", made);
        Assert.True(made.Count >= 3, "조각이 셋 이상이어야 함: " + string.Join(", ", made));
        Assert.Contains(ui.Infos.Concat(ui.Summaries.SelectMany(s => s.Notes)), m => m.Contains("문제없어요", StringComparison.Ordinal));
        Assert.Contains(ui.ChoicePrompts, p => p.Message.Contains("휴지통으로 옮길까요?", StringComparison.Ordinal));
        Assert.Equal(new[] { src }, svc.RecycleBin.AllSent);
        Assert.Equal(openPath, vm.CurrentArchivePath);   // 만든 뒤 여는 조각을 받은 암호로 연다(다시 묻지 않음)
        Assert.Empty(ui.PasswordPrompts);

        string back = tmp.Dir("다시 풂");
        await Svc.For(openPath).ExtractAsync(openPath, back, password);
        TreeAssert.ExtractedTreeEquals(back, expected, context: ext);

        // 가운데 조각(데이터) 하나가 망가지면 진짜 시험기가 잡는다
        vm.Shutdown();
        string middle = Path.Combine(outDir, sevenZip ? "분할 자료.7z.002" : "분할 자료.z01");
        var bytes = File.ReadAllBytes(middle);
        for (int i = bytes.Length / 2; i < bytes.Length / 2 + 16; i++) bytes[i] ^= 0x5A;
        File.WriteAllBytes(middle, bytes);
        var ex = Assert.ThrowsAny<Exception>(() => CreatedArchiveTester.Instance.Test(openPath, password, CancellationToken.None));
        Assert.IsNotAssignableFrom<OperationCanceledException>(ex);
    }

    // ───────────── 압축 속 파일 열기 × 압축 고치기 ─────────────

    /// <summary>
    /// 계약(W2-06 I3-3 × I2-3): 압축 속 파일(자료/보고서.txt)을 열어 둔 채로 그 압축에 파일을 넣고 다른 항목의 이름을 바꾼 뒤, 연 파일을 고쳐 저장하고 [넣기]를 고르면
    /// 다시 넣기가 앞의 고치기(넣은 파일·바뀐 이름)를 잃지 않는다 — 최종 압축에는 고친 보고서·바뀐 이름·넣은 파일이 모두 있다(ZIP·7Z).
    /// 연 파일은 일집 임시 폴더(temp\open)에 그대로이고, 압축 파일이 있는 폴더에 임시 파일이 남지 않는다.
    /// </summary>
    [Theory]
    [InlineData("zip")]
    [InlineData("7z")]
    public async Task 압축_속_파일을_연_채로_고쳐도_다시_넣기가_앞의_고치기를_잃지_않는다(string kind)
    {
        using var t = new TempDir("w2iedit" + kind);
        string root = t.Dir("원본", "자료");
        File.WriteAllText(Path.Combine(root, "보고서.txt"), "처음 내용");
        File.WriteAllText(Path.Combine(root, "표.csv"), "가,나");
        string folder = t.Dir("보관 폴더");
        string archive = Path.Combine(folder, "보관." + kind);
        if (kind == "zip") await new ZipArchiveService().CompressAsync(new[] { root }, archive);
        else SevenZipTestArchives.SevenZip(root, archive);
        string extra = Path.Combine(t.Dir("넣을 것"), "새 메모.txt");
        File.WriteAllText(extra, "넣은 파일");

        var ts = new TestServices();
        var ui = new FakeUserInteraction();
        var vm = new MainViewModel(ui, null, ts.Services) { EntryChangeQuietPeriod = TimeSpan.FromMilliseconds(30) };
        try
        {
            await VmRunner.Run(() => vm.OpenArchiveAsync(archive), "열기");
            var report = vm.Entries.Single(e => TreeAssert.NormalizeEntryPath(e.Path) == "자료/보고서.txt");
            bool opened = false;
            await VmRunner.Run(async () => opened = await vm.OpenEntryAsync(report), "안의 파일 열기");
            Assert.True(opened);
            string openedPath = Assert.Single(ts.Shell.OpenedFiles);
            Assert.True(AppPaths.IsUnderTemp(openedPath), "연 파일이 일집 임시 폴더 밖: " + openedPath);

            bool added = false, renamed = false;
            await VmRunner.Run(async () => added = await vm.AddPathsToArchiveAsync(new[] { extra }, "자료"), "넣기");
            Assert.True(added);
            var table = vm.Entries.Single(e => TreeAssert.NormalizeEntryPath(e.Path) == "자료/표.csv");
            await VmRunner.Run(async () => renamed = await vm.RenameEntryAsync(table, "표 2026.csv"), "이름 바꾸기");
            Assert.True(renamed);

            ui.ChooseByLabel = "넣기";
            File.WriteAllText(openedPath, "연 채로 고친 내용");
            Assert.Equal(1, ts.FileChanges.Raise(openedPath));
            await VmRunner.Run(() => vm.WhenEntryChecksIdleAsync(), "바뀜 확인");

            Assert.True(ui.Errors.Count == 0, "오류 창 — " + string.Join(" / ", ui.Errors));
            Assert.Contains(ui.ChoicePrompts, p => p.Message == "‘보고서.txt’가 바뀌었어요. 압축 파일에 다시 넣을까요?");
            string dest = t.Dir("확인");
            await Svc.For(archive).ExtractAsync(archive, dest);
            Assert.Equal(new[] { "자료/보고서.txt", "자료/새 메모.txt", "자료/표 2026.csv" }, TreeAssert.Files(dest));
            Assert.Equal("연 채로 고친 내용", File.ReadAllText(Path.Combine(dest, "자료", "보고서.txt")));
            Assert.Equal("넣은 파일", File.ReadAllText(Path.Combine(dest, "자료", "새 메모.txt")));
            Assert.Equal("가,나", File.ReadAllText(Path.Combine(dest, "자료", "표 2026.csv")));
            Assert.Equal(new[] { "보관." + kind }, Directory.EnumerateFileSystemEntries(folder).Select(Path.GetFileName).ToArray());
        }
        finally
        {
            vm.Shutdown();
        }
    }

    // ───────────── 내용으로 고르기 × 창 없이 풀기(한글 이름) ─────────────

    /// <summary>
    /// 계약(W2-01 × 재사용장치): 확장자가 바뀐 첨부(ZIP CP949·EGG·ALZ CP949·RAR4 CP949)도 창 없이 풀기의 처리기 고르기(<see cref="ArchiveServiceLocator.ResolveForAutomation"/>)가
    /// 7z.dll 쪽이 아니라 원래 처리기(ZIP·EGG/ALZ·RAR)를 골라 한글 이름이 그대로 풀린다. 확장자가 맞는 것과 같은 처리기다.
    /// </summary>
    [Theory]
    [InlineData("fx:zip_cp949", ".zip", typeof(ZipArchiveService))]
    [InlineData("syn:egg", ".egg", typeof(EggAlzArchiveService))]
    [InlineData("syn:alz", ".alz", typeof(EggAlzArchiveService))]
    [InlineData("syn:rar4_cp949", ".rar", typeof(RarArchiveService))]
    public async Task 확장자가_바뀐_첨부도_원래_처리기로_한글_이름이_그대로(string spec, string realExt, Type serviceType)
    {
        using var tmp = new TempDir("w2iren");
        var c = Make(spec, tmp);
        Assert.Equal(realExt, Path.GetExtension(c.Path), StringComparer.OrdinalIgnoreCase);
        string renamed = Path.Combine(Path.GetDirectoryName(c.Path)!, "첨부파일.dat");
        File.Move(c.Path, renamed);
        var locator = new ArchiveServiceLocator();

        var service = locator.ResolveForAutomation(renamed);
        Assert.NotNull(service);
        Assert.IsType(serviceType, service);
        string dest = tmp.Dir("풂");
        await service!.ExtractAsync(renamed, dest);
        TreeAssert.ExtractedTreeEquals(dest, c.Expected, context: spec);
    }
}
