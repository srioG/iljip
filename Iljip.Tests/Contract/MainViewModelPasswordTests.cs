using Iljip.Models;
using Iljip.Services;
using Iljip.Tests.TestSupport;
using Iljip.ViewModels;

namespace Iljip.Tests.Contract;

/// <summary>
/// [W2-01, I1-7·B-067] 화면(MainViewModel)의 암호 흐름을 가짜 창(<see cref="FakeUserInteraction"/>)으로 창 없이 시험한다.
/// - 파일 이름까지 암호가 걸린 7z 를 '열기'하면 암호를 묻고, 틀리면 다시 묻고, 취소하면 아무것도 바꾸지 않는다(I1-7).
/// - 열 때 받은 암호는 같은 압축을 풀 때 다시 묻지 않고 쓴다.
/// - 풀 때 암호는 [취소]할 때까지 몇 번이고 다시 묻는다(예전엔 3번에서 끝 — B-067).
/// (실제 창 모양·포커스는 MANUAL_CHECKLIST 2절)
/// </summary>
public class MainViewModelPasswordTests
{
    private static readonly TimeSpan Limit = TimeSpan.FromSeconds(60);

    [Theory]
    [InlineData("7z_header_encrypted")]
    [InlineData("7z_header_encrypted_korean_pw")]
    public async Task 헤더암호_7z를_열면_암호를_묻고_목록을_보여준다(string id)
    {
        var fx = FixtureCatalog.Get(id);
        var ui = new FakeUserInteraction(fx.Password);
        var vm = new MainViewModel(ui);
        await vm.OpenArchiveAsync(fx.FullPath).WaitAsync(Limit);

        var ask = Assert.Single(ui.PasswordPrompts);
        Assert.Equal(PasswordPromptReason.ListLocked, ask.Reason);   // W2-02(I1-7): 목록까지 잠긴 압축 전용 이유
        Assert.Equal(PasswordRequiredException.HeaderMessage, ask.Message);
        Assert.Equal(Path.GetFileName(fx.FullPath), ask.ArchiveName);
        Assert.Equal(fx.FullPath, vm.CurrentArchivePath);
        Assert.Equal(15, vm.Entries.Count);
        Assert.Equal("15개 항목", vm.StatusText);
        Assert.Empty(ui.Errors);
        Assert.Empty(ui.Warnings);
    }

    [Fact]
    public async Task 틀린_암호면_다시_묻고_맞으면_열린다()
    {
        var fx = FixtureCatalog.Get("7z_header_encrypted");
        var ui = new FakeUserInteraction("틀린암호", fx.Password);
        var vm = new MainViewModel(ui);
        await vm.OpenArchiveAsync(fx.FullPath).WaitAsync(Limit);

        Assert.Equal(new[] { PasswordPromptReason.ListLocked, PasswordPromptReason.Wrong }, ui.PasswordPrompts.Select(p => p.Reason));
        Assert.Equal(WrongPasswordException.DefaultMessage + WrongPasswordException.HeaderNote, ui.PasswordPrompts[1].Message);
        // W2-02(I1-7): 틀리면 '암호가 맞지 않아요' 경고를 한 번 보이고 다시 묻는다
        Assert.Contains("맞지 않아요", Assert.Single(ui.Warnings));
        Assert.Empty(ui.Errors);
        Assert.Equal(15, vm.Entries.Count);
    }

    /// <summary>암호 창을 취소하면 열기를 그만두고, 이미 열려 있던 압축은 그대로 둔다.</summary>
    [Fact]
    public async Task 암호_창을_취소하면_아무것도_바뀌지_않는다()
    {
        var fx = FixtureCatalog.Get("7z_header_encrypted");
        var ui = new FakeUserInteraction();   // 답 없음 = 취소
        var vm = new MainViewModel(ui);
        await vm.OpenArchiveAsync(fx.FullPath).WaitAsync(Limit);
        Assert.Single(ui.PasswordPrompts);
        Assert.True(vm.IsEmptyState);
        Assert.Null(vm.CurrentArchivePath);
        Assert.Equal("열기를 취소했어요", vm.StatusText);
        Assert.Empty(ui.Errors);

        string zip = FixtureCatalog.Get("zip_default").FullPath;
        await vm.OpenArchiveAsync(zip).WaitAsync(Limit);
        await vm.OpenArchiveAsync(fx.FullPath).WaitAsync(Limit);
        Assert.Equal(zip, vm.CurrentArchivePath);
        Assert.Equal(15, vm.Entries.Count);
    }

    /// <summary>열 때 받은 암호로 바로 풀린다(다시 묻지 않음). 진행률도 잘 짜여 있다.</summary>
    [Fact]
    public async Task 열때_받은_암호로_풀면_다시_묻지_않는다()
    {
        var fx = FixtureCatalog.Get("7z_header_encrypted");
        var ui = new FakeUserInteraction(fx.Password);
        var vm = new MainViewModel(ui);
        await vm.OpenArchiveAsync(fx.FullPath).WaitAsync(Limit);
        using var tmp = new TempDir("vmx");
        string dest = tmp.P("out");
        await vm.ExtractToFolderAsync(fx.FullPath, dest).WaitAsync(Limit);

        Assert.Single(ui.PasswordPrompts);
        Assert.Equal($"압축 해제 완료: {dest}", vm.StatusText);
        TreeAssert.ExtractedTreeEquals(dest, fx.Expected);
        ProgressTests.AssertWellFormed(Assert.Single(ui.ProgressSessions).Reports, "화면 해제");
    }

    /// <summary>[B-067] 풀 때 암호가 4번 틀려도 계속 묻고, 5번째에 맞으면 풀린다. 틀린 동안 대상 폴더에 파일이 남지 않는다.</summary>
    [Fact]
    public async Task 풀_때_암호는_몇번_틀려도_취소할때까지_다시_묻는다()
    {
        var fx = FixtureCatalog.Get("py7z_pw_names_visible");
        var ui = new FakeUserInteraction("w1", "w2", "w3", "w4", fx.Password);
        var vm = new MainViewModel(ui);
        await vm.OpenArchiveAsync(fx.FullPath).WaitAsync(Limit);
        Assert.Empty(ui.PasswordPrompts);   // 이름이 보이는 암호 7z 는 열 때 묻지 않음
        using var tmp = new TempDir("vmr");
        string dest = tmp.P("out");
        await vm.ExtractToFolderAsync(fx.FullPath, dest).WaitAsync(Limit);

        Assert.Equal(
            new[] { PasswordPromptReason.Required, PasswordPromptReason.Wrong, PasswordPromptReason.Wrong, PasswordPromptReason.Wrong, PasswordPromptReason.Wrong },
            ui.PasswordPrompts.Select(p => p.Reason));
        Assert.Empty(ui.Errors);
        TreeAssert.ExtractedTreeEquals(dest, fx.Expected);
    }

    [Fact]
    public async Task 풀_때_암호_창을_취소하면_파일이_남지_않는다()
    {
        var fx = FixtureCatalog.Get("py7z_pw_names_visible");
        var ui = new FakeUserInteraction("틀린암호");   // 한 번 틀리고 그다음 취소
        var vm = new MainViewModel(ui);
        using var tmp = new TempDir("vmc");
        string dest = tmp.P("out");
        await vm.ExtractToFolderAsync(fx.FullPath, dest).WaitAsync(Limit);
        Assert.Equal(2, ui.PasswordPrompts.Count);
        Assert.Equal("압축 해제 취소됨", vm.StatusText);
        TreeAssert.NoFiles(dest);
    }

    /// <summary>
    /// 탐색기 우클릭 '여기에 풀기'(--extract-here / --extract-to): 헤더 암호 7z 는 열 때 한 번만 묻고 그 암호로 푼다.
    /// 열 때 암호 창을 취소하면 풀기도 하지 않는다(같은 압축에 창을 또 띄우지 않음).
    /// </summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task 명령줄_풀기는_열때_한번만_묻는다(bool answer)
    {
        var fx = FixtureCatalog.Get("7z_header_encrypted");
        var ui = answer ? new FakeUserInteraction(fx.Password) : new FakeUserInteraction();
        var vm = new MainViewModel(ui);
        using var tmp = new TempDir("vmcli");
        string dest = tmp.P("풀 곳");
        await vm.ProcessCommandLineAsync(new[] { "--extract-to", fx.FullPath, dest }).WaitAsync(Limit);

        Assert.Single(ui.PasswordPrompts);
        Assert.Empty(ui.Errors);
        if (answer)
            TreeAssert.ExtractedTreeEquals(dest, fx.Expected);
        else
            Assert.False(Directory.Exists(dest), "열기를 취소했는데 풀기를 시작함");
    }

    /// <summary>손상된 압축은 암호를 묻지 않고 쉬운 한국어 오류를 보인다.</summary>
    [Fact]
    public async Task 손상_파일은_암호를_묻지_않고_쉬운_오류를_보인다()
    {
        using var tmp = new TempDir("vmbad");
        string arc = tmp.P("깨짐.7z");
        File.WriteAllBytes(arc, File.ReadAllBytes(FixtureCatalog.Get("7z_default").FullPath)[..500]);
        var ui = new FakeUserInteraction("쓰면안됨");
        var vm = new MainViewModel(ui);
        await vm.OpenArchiveAsync(arc).WaitAsync(Limit);
        Assert.Empty(ui.PasswordPrompts);
        var err = Assert.Single(ui.Errors);
        Assert.Contains(CorruptArchiveException.DefaultMessage, err);
        Assert.Equal("오류 발생", vm.StatusText);
    }

    /// <summary>드롭은 확장자로만 분류한다 — .docx(속은 ZIP)를 끌어 놓으면 열지 않고 압축 대기에 넣는다.</summary>
    [Fact]
    public async Task 드롭한_docx는_열지_않고_압축대기에_넣는다()
    {
        using var tmp = new TempDir("vmdrop");
        string docx = tmp.P("보고서.docx");
        File.Copy(FixtureCatalog.Get("zip_default").FullPath, docx);
        var vm = new MainViewModel(new FakeUserInteraction());
        await vm.HandleDroppedPathsAsync(new[] { docx }).WaitAsync(Limit);
        Assert.True(vm.IsStagingMode);
        Assert.Null(vm.CurrentArchivePath);
        Assert.Equal("보고서.docx", Assert.Single(vm.Entries).Path);
    }

    /// <summary>
    /// 계약(W2-01): 파일 이름까지 암호화한 7z 의 데이터가 손상됐을 때 — 열 때 맞는 암호를 한 번 넣으면 목록이 열리고,
    /// 풀기는 암호를 다시 묻지 않고 손상 오류 한 번으로 끝난다(예전엔 맞는 암호를 넣어도 '암호가 맞지 않아요'를 [취소]까지 되물었다).
    /// </summary>
    [Fact]
    public async Task 손상된_헤더암호_7z는_맞는_암호를_한번만_묻고_손상으로_알린다()
    {
        using var tmp = new TempDir("vm7zbad");
        string src = SampleTree.Create(tmp.Dir("src"));
        string arc = SevenZipTestArchives.SevenZip(src, tmp.P("원본.7z"), FixtureCatalog.KoreanPassword, encryptNames: true);
        Assert.True(ArchiveHeaderProbe.IsSevenZipHeaderEncrypted(arc));
        string bad = SevenZipTestArchives.CorruptMiddle(arc, tmp.P("손상.7z"));

        var ui = new FakeUserInteraction(FixtureCatalog.KoreanPassword, FixtureCatalog.KoreanPassword, FixtureCatalog.KoreanPassword);
        var vm = new MainViewModel(ui);
        await vm.OpenArchiveAsync(bad).WaitAsync(Limit);
        Assert.Equal(bad, vm.CurrentArchivePath);
        Assert.Single(ui.PasswordPrompts);

        await vm.ExtractToFolderAsync(bad, tmp.P("out")).WaitAsync(Limit);
        Assert.Single(ui.PasswordPrompts);
        Assert.Contains("손상", Assert.Single(ui.Errors));
    }

    /// <summary>
    /// 화면의 7z 만들기 흐름(W2-02 — 1차 W1-B 보관분을 되살림): 저장 경로 .7z + 암호·파일 이름 암호화 → 만든 뒤 자동으로 열 때 방금 넣은 암호를 써서
    /// 다시 묻지 않고 목록을 보여 주고, 이어서 풀 때도 묻지 않는다(1차 W1-B 검증 지적 — 예전엔 열 때 같은 암호를 곧바로 다시 물었다).
    /// 진행 막대는 도중 보고가 있고 '완료'로 끝난다.
    /// </summary>
    [Fact]
    public async Task 화면에서_파일이름까지_암호화한_7z를_만들고_다시_연다()
    {
        using var tmp = new TempDir("vm7z");
        string src = SampleTree.Create(tmp.Dir("src"));
        string arc = tmp.P("결과.7z");
        var ui = new FakeUserInteraction()
        {
            SavePath = arc,
            CompressionOptions = new CompressionOptions { Password = FixtureCatalog.KoreanPassword, EncryptFileNames = true },
        };
        var vm = new MainViewModel(ui);
        vm.AddToStaging(new[] { src });
        await vm.CompressCommand.ExecuteAsync(null).WaitAsync(Limit);

        Assert.Empty(ui.Errors);
        Assert.Empty(ui.Warnings);
        Assert.True(ArchiveHeaderProbe.IsSevenZipHeaderEncrypted(arc));
        Assert.Empty(ui.PasswordPrompts);
        Assert.Equal(arc, vm.CurrentArchivePath);
        TreeAssert.ListEquals(vm.Entries.ToList(), SampleTree.ExpectedOf(src));
        ProgressTests.AssertWellFormed(ui.ProgressSessions[0].Reports, "화면 7z 압축", expectIntermediate: true);

        string dest = tmp.P("out");
        await vm.ExtractToFolderAsync(arc, dest).WaitAsync(Limit);
        Assert.Empty(ui.PasswordPrompts);
        Assert.Empty(ui.Errors);
        TreeAssert.ExtractedTreeEquals(dest, SampleTree.ExpectedOf(src));
    }

    /// <summary>
    /// 화면의 7z 만들기를 진행 창 [취소]로 멈추면(처음 바이트를 읽자마자) '압축 취소됨'이고 오류 창이 없으며,
    /// 덮어쓰려던 기존 파일은 바이트 그대로, 임시 파일도 남지 않는다(W2-02 — 7z 는 임시 파일에 쓰고 끝에 바꿔 넣음).
    /// </summary>
    [Fact]
    public async Task 화면에서_7z_만들기를_취소하면_기존_파일이_그대로다()
    {
        using var tmp = new TempDir("vm7zc");
        string src = CancellationTests.MakeSource(tmp);
        string arc = tmp.P("결과.7z");
        var old = new byte[] { 0x37, 0x7A, 0xBC, 0xAF, 0x27, 0x1C, 9, 9, 9 };
        File.WriteAllBytes(arc, old);
        var ui = new FakeUserInteraction { SavePath = arc, CompressionOptions = CompressionOptions.Default, CancelOnFirstBytes = true };
        var vm = new MainViewModel(ui);
        vm.AddToStaging(new[] { src });
        await vm.CompressCommand.ExecuteAsync(null).WaitAsync(Limit);

        Assert.Equal("압축 취소됨", vm.StatusText);
        Assert.Empty(ui.Errors);
        Assert.Equal(old, File.ReadAllBytes(arc));
        SevenZipCreateTests.AssertNoLeftovers(tmp.Root);
        Assert.True(vm.IsStagingMode, "취소했는데 모으던 목록이 비워짐");
    }
}
