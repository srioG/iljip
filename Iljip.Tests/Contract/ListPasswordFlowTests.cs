using Iljip.Models;
using Iljip.Services;
using Iljip.Tests.TestSupport;
using Iljip.ViewModels;

namespace Iljip.Tests.Contract;

/// <summary>
/// [W2-02, I1-7] 목록(파일 이름)까지 암호로 잠긴 압축 — 일집이 만든 7z('파일 이름도 암호로 숨기기'), 다른 프로그램(py7zr)이 만든 헤더 암호 7z,
/// 머리 암호 RAR5·RAR4(합성) — 를 형식과 상관없이 같은 길로 연다:
/// <list type="bullet">
/// <item>서비스: 암호 없이 목록을 읽으면 <see cref="ListPasswordRequiredException"/>('파일 목록까지 암호로 잠긴…'), 틀린 암호면 <see cref="WrongPasswordException"/>(머리 암호 표시).</item>
/// <item>화면: 열 때 한 번 묻는다(<see cref="PasswordPromptReason.ListLocked"/>). 틀리면 '암호가 맞지 않아요' 경고 뒤 다시 묻고, 취소하면 보던 압축·목록·기억한 암호가 그대로다.
///   열 때 받은 암호로 풀면 다시 묻지 않고, 우클릭 풀기도 한 번만 묻는다.</item>
/// </list>
/// 반디집이 만든 헤더 암호 7z 는 MainViewModelPasswordTests·CommandLineTests·FormatFlowTests 가 같은 계약을 본다. 창 없이 풀기는 ReuseDeviceContractTests.
/// </summary>
public class ListPasswordFlowTests
{
    private sealed record Case(string Path, string Password, List<ExpectedEntry> Expected);

    /// <summary>시험할 목록 잠긴 압축의 종류.</summary>
    public static TheoryData<string> Kinds => new() { "iljip7z", "py7z", "py7z_ko", "rar5_hdr", "rar4_hdr" };

    public static TheoryData<string, string> KindsAndVerbs
    {
        get
        {
            var d = new TheoryData<string, string>();
            foreach (var k in new[] { "iljip7z", "py7z", "py7z_ko", "rar5_hdr", "rar4_hdr" })
                foreach (var v in new[] { "--extract-here", "--extract-to-folder" })
                    d.Add(k, v);
            return d;
        }
    }

    /// <summary>'작업 폴더'(압축 파일 하나만 있는 폴더)에 그 종류의 압축을 만든다.</summary>
    private static async Task<Case> Make(string kind, TempDir tmp)
    {
        string work = tmp.Dir("작업 폴더");
        switch (kind)
        {
            case "iljip7z":
                {
                    // 일집이 만든 7z: 한글 암호 + 파일 이름 암호화 + 빈 폴더(덧붙이기 뒤에도 이름 암호가 그대로여야 함)
                    string src = SampleTree.Create(tmp.Dir("원본"));
                    string arc = Path.Combine(work, "일집_이름암호.7z");
                    await new SevenZipArchiveService().CompressAsync(new[] { src }, arc,
                        new CompressionOptions { Password = FixtureCatalog.KoreanPassword, EncryptFileNames = true });
                    Assert.True(ArchiveHeaderProbe.IsSevenZipHeaderEncrypted(arc), "일집 7z 의 파일 이름이 암호화되지 않음");
                    return new Case(arc, FixtureCatalog.KoreanPassword, SampleTree.ExpectedOf(src));
                }
            case "py7z":
            case "py7z_ko":
                {
                    var (arc, pw) = Py7zrSamples.Write(work, koreanPassword: kind == "py7z_ko");
                    Assert.True(ArchiveHeaderProbe.IsSevenZipHeaderEncrypted(arc));
                    return new Case(arc, pw, Py7zrSamples.Expected());
                }
            default:
                {
                    var b = SyntheticArchives.Build(kind, work);
                    Assert.True(b.HeaderEncrypted);
                    return new Case(b.Path, b.Password!, b.Expected);
                }
        }
    }

    private static Task RunVm(MainViewModel vm, params string[] args) =>
        VmRunner.Run(() => vm.ProcessCommandLineAsync(args), $"ProcessCommandLineAsync({string.Join(" ", args)})");

    private static Task Run(Func<Task> action, string what) => VmRunner.Run(action, what);

    /// <summary>계약(형식): 목록 잠김 예외는 '암호 필요'의 하위 형식이고 늘 머리 암호 표시, 화면 문구는 쉬운 말이며 화면이 암호 문제로 알아본다.</summary>
    [Fact]
    public void 목록_잠김_예외는_암호_필요의_하위_형식이다()
    {
        var ex = new ListPasswordRequiredException();
        Assert.IsAssignableFrom<PasswordRequiredException>(ex);
        Assert.IsAssignableFrom<ArchivePasswordException>(ex);
        Assert.True(ex.IsHeaderEncrypted);
        Assert.Equal("파일 목록까지 암호로 잠긴 압축 파일이에요. 암호를 넣어 주세요.", ListPasswordRequiredException.ListMessage);
        Assert.Equal(ListPasswordRequiredException.ListMessage, ex.Message);
        Assert.Equal(PasswordRequiredException.HeaderMessage, ListPasswordRequiredException.ListMessage);
        Assert.Equal(ListPasswordRequiredException.ListMessage, ErrorMessages.ToUserMessage(ex));
        Assert.True(Internals.IsPasswordRelated(ex));
        // 머리 암호 여부로 고르는 공장: 머리 암호면 하위 형식, 아니면 그냥 '암호 필요'
        Assert.IsType<ListPasswordRequiredException>(PasswordRequiredException.For(true));
        var plain = Assert.IsType<PasswordRequiredException>(PasswordRequiredException.For(false));
        Assert.False(plain.IsHeaderEncrypted);
        Assert.Equal(PasswordRequiredException.DefaultMessage, plain.Message);
    }

    /// <summary>계약(서비스): 암호 없이 목록 → 목록 잠김 예외, 틀린 암호 → 틀림(머리 암호), 맞는 암호 → 목록. 암호 없이 풀어도 같은 예외이고 아무것도 안 남는다.</summary>
    [Theory]
    [MemberData(nameof(Kinds))]
    public async Task 서비스는_암호없이_목록을_읽으면_목록잠김_예외(string kind)
    {
        using var tmp = new TempDir("lpsvc");
        var c = await Make(kind, tmp);
        var svc = Svc.For(c.Path);

        var none = Assert.IsType<ListPasswordRequiredException>(await Svc.Catch(() => svc.ListEntriesAsync(c.Path, null)));
        Assert.True(none.IsHeaderEncrypted);
        Assert.Equal(ListPasswordRequiredException.ListMessage, none.Message);
        var wrong = Assert.IsType<WrongPasswordException>(await Svc.Catch(() => svc.ListEntriesAsync(c.Path, "틀린암호")));
        Assert.True(wrong.IsHeaderEncrypted);
        TreeAssert.ListFilesEqual(await svc.ListEntriesAsync(c.Path, c.Password), c.Expected, kind);

        string dest = tmp.P("새 폴더", "없음");
        Assert.IsType<ListPasswordRequiredException>(await Svc.Catch(() => svc.ExtractAsync(c.Path, dest, null)));
        Assert.False(Directory.Exists(tmp.P("새 폴더")), $"{kind}: 암호 없이 풀다 멈췄는데 새로 만든 폴더가 남음");
    }

    /// <summary>계약(화면): 열면 '파일 목록까지 암호로 잠긴…'으로 한 번 묻고, 맞는 암호면 목록을 보인다(오류·경고 창 없음).</summary>
    [Theory]
    [MemberData(nameof(Kinds))]
    public async Task 열면_한번_묻고_목록을_보인다(string kind)
    {
        using var tmp = new TempDir("lpopen");
        var c = await Make(kind, tmp);
        var ui = new FakeUserInteraction(c.Password);
        var vm = new MainViewModel(ui);
        bool opened = false;
        await Run(async () => opened = await vm.OpenArchiveAsync(c.Path), "열기");

        Assert.True(opened);
        var ask = Assert.Single(ui.PasswordPrompts);
        Assert.Equal(PasswordPromptReason.ListLocked, ask.Reason);
        Assert.Equal(ListPasswordRequiredException.ListMessage, ask.Message);
        Assert.Equal(Path.GetFileName(c.Path), ask.ArchiveName);
        Assert.Equal(c.Path, vm.CurrentArchivePath);
        TreeAssert.ListFilesEqual(vm.Entries.ToList(), c.Expected, kind);
        Assert.Equal($"{vm.Entries.Count}개 항목", vm.StatusText);
        Assert.Empty(ui.Errors);
        Assert.Empty(ui.Warnings);
    }

    /// <summary>계약(화면): 틀린 암호면 '암호가 맞지 않아요' 경고를 한 번 보이고 다시 묻는다(이유 '틀림'), 맞으면 열린다.</summary>
    [Theory]
    [MemberData(nameof(Kinds))]
    public async Task 틀린_암호면_경고하고_다시_묻는다(string kind)
    {
        using var tmp = new TempDir("lpwrong");
        var c = await Make(kind, tmp);
        var ui = new FakeUserInteraction("틀린암호", c.Password);
        var vm = new MainViewModel(ui);
        await Run(() => vm.OpenArchiveAsync(c.Path), "열기");

        Assert.Equal(new[] { PasswordPromptReason.ListLocked, PasswordPromptReason.Wrong }, ui.PasswordPrompts.Select(p => p.Reason).ToArray());
        Assert.StartsWith(WrongPasswordException.DefaultMessage, ui.PasswordPrompts[1].Message);
        var warning = Assert.Single(ui.Warnings);
        Assert.Contains("맞지 않아요", warning);
        Assert.Equal(WrongPasswordException.DefaultMessage, warning);
        Assert.Empty(ui.Errors);
        Assert.Equal(c.Path, vm.CurrentArchivePath);
        TreeAssert.ListFilesEqual(vm.Entries.ToList(), c.Expected, kind);
    }

    /// <summary>
    /// 계약(화면): 암호 창을 취소하면 아무것도 바뀌지 않는다 — 보던 압축(목록 잠긴 7z, 암호로 연 것)·목록 개수·기억한 암호가 그대로이고 오류 창이 없다.
    /// 바로 취소(경고 없음)와 틀린 뒤 취소(경고 한 번) 둘 다. 그 뒤 보던 압축을 풀면 암호를 다시 묻지 않는다(기억한 암호가 지워지지 않음).
    /// </summary>
    [Theory]
    [MemberData(nameof(Kinds))]
    public async Task 취소하면_보던_압축과_기억한_암호가_그대로다(string kind)
    {
        using var tmp = new TempDir("lpcancel");
        var c = await Make(kind, tmp);
        var before = FixtureCatalog.Get("7z_header_encrypted");
        string first = Path.Combine(tmp.Dir("먼저 연 것"), Path.GetFileName(before.FullPath));
        File.Copy(before.FullPath, first);
        var ui = new FakeUserInteraction(before.Password, null, "틀린암호", null);
        var vm = new MainViewModel(ui);
        await Run(() => vm.OpenArchiveAsync(first), "먼저 열기");
        Assert.Equal(first, vm.CurrentArchivePath);
        int count = vm.Entries.Count;
        string status = vm.StatusText;

        // 1) 바로 취소
        bool opened = true;
        await Run(async () => opened = await vm.OpenArchiveAsync(c.Path), "열기(취소)");
        Assert.False(opened);
        Assert.Equal(2, ui.PasswordPrompts.Count);
        Assert.Equal(PasswordPromptReason.ListLocked, ui.PasswordPrompts[1].Reason);
        Assert.Equal(first, vm.CurrentArchivePath);
        Assert.Equal(count, vm.Entries.Count);
        Assert.Empty(ui.Errors);
        Assert.Empty(ui.Warnings);

        // 2) 틀린 뒤 취소
        await Run(async () => opened = await vm.OpenArchiveAsync(c.Path), "열기(틀린 뒤 취소)");
        Assert.False(opened);
        Assert.Equal(new[] { PasswordPromptReason.ListLocked, PasswordPromptReason.ListLocked, PasswordPromptReason.ListLocked, PasswordPromptReason.Wrong },
            ui.PasswordPrompts.Select(p => p.Reason).ToArray());
        Assert.Contains("맞지 않아요", Assert.Single(ui.Warnings));
        Assert.Equal(first, vm.CurrentArchivePath);
        Assert.Equal(count, vm.Entries.Count);
        Assert.Empty(ui.Errors);
        Assert.NotEqual(status, vm.StatusText);   // 상태 줄만 '열기를 취소했어요'
        Assert.Equal("열기를 취소했어요", vm.StatusText);

        // 3) 보던 압축은 기억한 암호로 풀린다(다시 묻지 않음)
        string dest = tmp.P("풀린 것");
        await Run(() => vm.ExtractToFolderAsync(first, dest), "풀기");
        Assert.Equal(4, ui.PasswordPrompts.Count);
        Assert.Empty(ui.Errors);
        TreeAssert.ExtractedTreeEquals(dest, before.Expected, context: "먼저 연 7z");
    }

    /// <summary>계약(화면): 열 때 받은 암호로 풀면 다시 묻지 않는다(전체 풀기). 진행 막대도 끝까지 간다.</summary>
    [Theory]
    [MemberData(nameof(Kinds))]
    public async Task 열고_나서_풀면_다시_묻지_않는다(string kind)
    {
        using var tmp = new TempDir("lpext");
        var c = await Make(kind, tmp);
        var ui = new FakeUserInteraction(c.Password);
        var vm = new MainViewModel(ui);
        await Run(() => vm.OpenArchiveAsync(c.Path), "열기");
        string dest = tmp.P("풀린 것");
        await Run(() => vm.ExtractToFolderAsync(c.Path, dest), "풀기");

        Assert.Single(ui.PasswordPrompts);
        Assert.Empty(ui.Errors);
        Assert.Empty(ui.Warnings);
        TreeAssert.ExtractedTreeEquals(dest, c.Expected, context: kind);
        ProgressTests.AssertWellFormed(Assert.Single(ui.ProgressSessions).Reports, kind + " 화면 풀기");
    }

    /// <summary>계약(우클릭 '여기에 풀기'·'"이름"\ 에 풀기'): 목록 잠긴 압축도 한 번만 묻고(열 때) 그 암호로 푼다. 내용이 맞다.</summary>
    [Theory]
    [MemberData(nameof(KindsAndVerbs))]
    public async Task 우클릭_풀기는_한번만_묻고_푼다(string kind, string verb)
    {
        using var tmp = new TempDir("lpcli");
        var c = await Make(kind, tmp);
        string work = Path.GetDirectoryName(c.Path)!;
        var ui = new FakeUserInteraction(c.Password);
        var vm = new MainViewModel(ui);

        await RunVm(vm, verb, c.Path);

        var ask = Assert.Single(ui.PasswordPrompts);
        Assert.Equal(PasswordPromptReason.ListLocked, ask.Reason);
        Assert.Empty(ui.Errors);
        Assert.Empty(ui.Warnings);
        if (verb == "--extract-here")
        {
            var self = new ExpectedEntry(Path.GetFileName(c.Path), new FileInfo(c.Path).Length, TreeAssert.Sha256(c.Path), false);
            TreeAssert.ExtractedTreeEquals(work, c.Expected.Append(self), context: kind);
        }
        else
        {
            TreeAssert.ExtractedTreeEquals(Path.Combine(work, ArchiveNaming.FolderNameFor(c.Path)), c.Expected, context: kind);
        }
    }

    /// <summary>
    /// 계약: 자동 풀림 exe 의 안쪽이 목록까지 잠긴 7z·RAR 이어도 같다 — 암호 없이 목록이면 목록 잠김 예외, 틀리면 틀림(머리 암호), 맞으면 목록.
    /// 7z.dll 을 못 쓰는 PC(SharpCompress 로 대신 엶)에서도 같은 예외다. 화면은 열 때 한 번 묻는다.
    /// </summary>
    [Theory]
    [InlineData("7z", false)]
    [InlineData("7z", true)]
    [InlineData("rar5_hdr", false)]
    [InlineData("rar5_hdr", true)]
    public async Task 자동풀림_exe_안쪽이_목록까지_잠겨도_열때_묻는다(string inner, bool blocked)
    {
        using var tmp = new TempDir("lpsfx");
        byte[] innerBytes;
        string password;
        List<ExpectedEntry> expected;
        if (inner == "7z")
        {
            string src = SampleTree.Create(tmp.Dir("원본"));
            string arc = tmp.P("안쪽.7z");
            await new SevenZipArchiveService().CompressAsync(new[] { src }, arc,
                new CompressionOptions { Password = FixtureCatalog.DefaultPassword, EncryptFileNames = true });
            innerBytes = File.ReadAllBytes(arc);
            password = FixtureCatalog.DefaultPassword;
            expected = SampleTree.ExpectedOf(src);
        }
        else
        {
            var b = SyntheticArchives.Build(inner, tmp.Dir("안쪽"));
            innerBytes = File.ReadAllBytes(b.Path);
            password = b.Password!;
            expected = b.Expected;
        }
        string exe = Path.Combine(tmp.Dir("작업 폴더"), "자동풀림.exe");
        File.WriteAllBytes(exe, SfxTests.FakeSfxModule(100_000).Concat(innerBytes).ToArray());

        using (blocked ? SevenZipLibrary.SimulateUnavailableForTests() : null)
        {
            var svc = Svc.For(exe);
            Assert.IsType<SfxArchiveService>(svc);
            var none = Assert.IsType<ListPasswordRequiredException>(await Svc.Catch(() => svc.ListEntriesAsync(exe, null)));
            Assert.True(none.IsHeaderEncrypted);
            Assert.True(Assert.IsType<WrongPasswordException>(await Svc.Catch(() => svc.ListEntriesAsync(exe, "틀린암호"))).IsHeaderEncrypted);
            TreeAssert.ListFilesEqual(await svc.ListEntriesAsync(exe, password), expected, $"{inner} sfx");

            var ui = new FakeUserInteraction(password);
            var vm = new MainViewModel(ui);
            await Run(() => vm.OpenArchiveAsync(exe), "자동 풀림 열기");
            Assert.Equal(PasswordPromptReason.ListLocked, Assert.Single(ui.PasswordPrompts).Reason);
            Assert.Equal(exe, vm.CurrentArchivePath);
            Assert.Empty(ui.Errors);
        }
    }
}
