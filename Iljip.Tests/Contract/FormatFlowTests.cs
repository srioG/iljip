using Iljip.Services;
using Iljip.Tests.TestSupport;
using Iljip.ViewModels;

namespace Iljip.Tests.Contract;

/// <summary>
/// 통합 점검(2026-10-12, W1-A·W1-C·W1-D 를 합친 뒤): 형식이 달라도 화면(MainViewModel) 흐름이 같은가를 한 표로 본다 — 가짜 창(<see cref="FakeUserInteraction"/>).
/// <list type="bullet">
/// <item>암호: ZIP(ZipCrypto·AES·반디집 한글 암호)·7z(이름 보임·헤더 암호)·EGG·RAR4·RAR5(파일 암호·머리 암호) 모두
///   '암호가 걸린…'(머리 암호면 '파일 이름까지…') → 틀리면 '암호가 맞지 않아요…' → 맞으면 풀림, 같은 암호 창 길.</item>
/// <item>한글 이름: 라우팅(확장자 → 서비스)을 거친 화면 목록·폴더 트리·풀린 이름이 ZIP(CP949·UTF-8)·7z·TAR·EGG(UTF-8)·ALZ(CP949)·RAR4(유니코드·CP949)·RAR5 모두 같다.</item>
/// <item>진행 막대: 위 모든 해제에서 진행 창이 도중 보고(0 &lt; 처리량 &lt; 총량)와 완료 보고를 받는다.</item>
/// </list>
/// 이 점검에서 찾은 것: RAR4 암호 파일은 암호를 묻지 않고 쓰레기를 '완료'로 풂(고침 — <see cref="RarTests"/>).
/// </summary>
public class FormatFlowTests
{
    private sealed record Case(string Path, string? Password, bool HeaderEncrypted, List<ExpectedEntry> Expected);

    /// <summary>"fx:픽스처id" 는 픽스처 사본, "syn:종류" 는 <see cref="SyntheticArchives"/> 로 작업 폴더에 만든다.</summary>
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

    private static Task RunVm(MainViewModel vm, params string[] args) =>
        VmRunner.Run(() => vm.ProcessCommandLineAsync(args), $"ProcessCommandLineAsync({string.Join(" ", args)})");

    /// <summary>
    /// 계약: 암호 압축은 형식과 상관없이 같은 암호 창 길로 풀린다 — 우클릭 '"이름"\ 에 풀기'에서 처음엔 '암호가 걸린 압축 파일이에요…'
    /// (머리 암호면 열 때 '파일 이름까지 암호가 걸려 있어요…'), 틀린 암호면 '암호가 맞지 않아요…' 로 다시 묻고, 맞는 암호면 풀린다.
    /// 틀린 시도는 아무것도 남기지 않고(이름 폴더 번호가 밀리지 않음), 진행 막대는 움직인다.
    /// RAR4 파일 암호는 이 점검 전엔 암호 창 없이 쓰레기를 '완료'로 풀었다.
    /// W2-02(I1-7): 목록까지 잠긴 압축(7z 헤더 암호·RAR 머리 암호)은 열 때 전용 이유(<see cref="PasswordPromptReason.ListLocked"/>)로 묻고,
    /// 틀리면 '암호가 맞지 않아요' 경고를 한 번 보인 뒤 다시 묻는다(형식과 상관없이 같음). 그 밖은 경고 없이 다시 묻는다(예전 그대로).
    /// </summary>
    [Theory]
    [InlineData("fx:zip_zipcrypto")]
    [InlineData("fx:zip_aes256")]
    [InlineData("fx:zip_zipcrypto_korean_pw")]
    [InlineData("fx:zip_aes256_korean_pw")]
    [InlineData("fx:py7z_pw_names_visible")]
    [InlineData("fx:7z_header_encrypted")]
    [InlineData("fx:7z_header_encrypted_korean_pw")]
    [InlineData("syn:egg_enc")]
    [InlineData("syn:rar5_enc")]
    [InlineData("syn:rar5_hdr")]
    [InlineData("syn:rar4_enc")]
    [InlineData("syn:rar4_hdr")]
    public async Task 암호_압축은_형식과_상관없이_같은_암호_창으로_풀린다(string spec)
    {
        using var tmp = new TempDir("ffpw");
        var c = Make(spec, tmp);
        string work = Path.GetDirectoryName(c.Path)!;
        string folder = ArchiveNaming.FolderNameFor(c.Path);
        var ui = new FakeUserInteraction("틀린암호", c.Password!);

        await RunVm(new MainViewModel(ui), "--extract-to-folder", c.Path);

        Assert.Empty(ui.Errors);
        if (c.HeaderEncrypted) Assert.Contains("맞지 않아요", Assert.Single(ui.Warnings));
        else Assert.Empty(ui.Warnings);
        var first = c.HeaderEncrypted ? PasswordPromptReason.ListLocked : PasswordPromptReason.Required;
        Assert.Equal(new[] { first, PasswordPromptReason.Wrong }, ui.PasswordPrompts.Select(p => p.Reason).ToArray());
        Assert.All(ui.PasswordPrompts, p => Assert.Equal(Path.GetFileName(c.Path), p.ArchiveName));
        Assert.Equal(c.HeaderEncrypted ? PasswordRequiredException.HeaderMessage : PasswordRequiredException.DefaultMessage, ui.PasswordPrompts[0].Message);
        Assert.StartsWith(WrongPasswordException.DefaultMessage, ui.PasswordPrompts[1].Message);
        TreeAssert.ExtractedTreeEquals(Path.Combine(work, folder), c.Expected, context: spec);
        Assert.False(Directory.Exists(Path.Combine(work, folder + " (2)")), $"{spec}: 틀린 시도가 폴더를 남겨 번호가 밀림");
        ProgressTests.AssertWellFormed(ui.ProgressSessions[^1].Reports, spec, expectIntermediate: true);
    }

    /// <summary>
    /// 계약: 라우팅을 거친 화면에서 한글 이름이 형식과 상관없이 같다 — 열기(목록·폴더 트리의 각 폴더 직속 항목 이름)와
    /// '"이름"\ 에 풀기'(풀린 이름·내용). 진행 막대도 움직인다.
    /// </summary>
    [Theory]
    [InlineData("fx:zip_default")]
    [InlineData("fx:zip_cp949")]
    [InlineData("fx:zip_utf8")]
    [InlineData("fx:7z_default")]
    [InlineData("fx:tar_default")]
    [InlineData("syn:egg")]
    [InlineData("syn:alz")]
    [InlineData("syn:rar5")]
    [InlineData("syn:rar4")]
    [InlineData("syn:rar4_cp949")]
    public async Task 한글_이름은_형식과_상관없이_화면과_풀린_이름이_같다(string spec)
    {
        using var tmp = new TempDir("ffko");
        var c = Make(spec, tmp);
        string work = Path.GetDirectoryName(c.Path)!;
        var ui = new FakeUserInteraction();
        var vm = new MainViewModel(ui);

        await RunVm(vm, "--open", c.Path);
        Assert.Empty(ui.Errors);
        Assert.Equal(c.Path, vm.CurrentArchivePath);
        TreeAssert.ListFilesEqual(vm.Entries.ToList(), c.Expected, spec);
        AssertFolderTree(vm, c.Expected, spec);

        await RunVm(vm, "--extract-to-folder", c.Path);
        Assert.True(ui.Errors.Count == 0, $"{spec}: 오류 창 — {string.Join(" / ", ui.Errors)}");
        Assert.Empty(ui.PasswordPrompts);
        TreeAssert.ExtractedTreeEquals(Path.Combine(work, ArchiveNaming.FolderNameFor(c.Path)), c.Expected, context: spec);
        ProgressTests.AssertWellFormed(ui.ProgressSessions[^1].Reports, spec, expectIntermediate: true);
    }

    /// <summary>
    /// 계약(W1-C × W1-D): 암호 ZIP(AES·반디집 호환 한글 ZipCrypto)을 만들 때 읽는 도중 막힌 파일(DB 등 바이트 범위 잠금)이 있어도,
    /// 그 파일만 빼고 처음부터 다시 쓴 결과가 같은 암호·같은 방식으로 풀린다(다시 쓸 때 암호를 잃거나 방식이 바뀌지 않음). 화면은 '건너뛴 파일 1개'.
    /// </summary>
    [Theory]
    [InlineData(Iljip.Models.ZipEncryption.Aes256, FixtureCatalog.DefaultPassword)]
    [InlineData(Iljip.Models.ZipEncryption.ZipCrypto, FixtureCatalog.KoreanPassword)]
    public async Task 암호_ZIP도_읽는_도중_막힌_파일만_빼고_다시_써서_같은_암호로_풀린다(Iljip.Models.ZipEncryption method, string password)
    {
        using var tmp = new TempDir("ffcs");
        string src = SampleTree.Create(tmp.Dir("src"));
        var expected = SampleTree.ExpectedOf(src);
        string db = Path.Combine(src, "업무.db");
        var data = new byte[300_000];
        new Random(11).NextBytes(data);
        File.WriteAllBytes(db, data);
        string zip = tmp.P("결과.zip");
        var ui = new FakeUserInteraction
        {
            SavePath = zip,
            CompressionOptions = new Iljip.Models.CompressionOptions { Password = password, Encryption = method },
        };
        var vm = new MainViewModel(ui);

        using (var locker = new FileStream(db, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite))
        {
            locker.Lock(200_000, 50_000);   // 첫 블록(80KB)은 읽히고 뒤쪽에서 막힘 → 머리를 쓴 뒤 막혀 처음부터 다시 씀
            await RunVm(vm, "--compress", src);
        }

        Assert.Empty(ui.Errors);
        Assert.Contains(ui.Infos, m => m.StartsWith("압축을 마쳤어요. 건너뛴 파일 1개:", StringComparison.Ordinal) && m.Contains("업무.db"));
        Assert.Equal(zip, vm.CurrentArchivePath);   // 다 만든 뒤 그 zip 을 연다
        var files = ZipHeaders.CentralEntries(zip).Where(e => !e.Name.EndsWith('/')).ToList();
        Assert.Equal(expected.Count(e => !e.IsDir), files.Count);
        Assert.All(files, e => Assert.True((e.Flags & 1) == 1, $"{e.Name}: 다시 쓴 결과에 암호가 빠진 항목"));

        var wrong = await Svc.Catch(() => new ZipArchiveService().ExtractAsync(zip, tmp.P("틀림"), "wrong-password"));
        Assert.IsType<WrongPasswordException>(wrong);
        string dest = tmp.P("out");
        await new ZipArchiveService().ExtractAsync(zip, dest, password);
        TreeAssert.ExtractedTreeEquals(dest, expected);   // 업무.db 만 빠지고 나머지는 같다(expected 는 db 를 넣기 전에 계산)
    }

    /// <summary>
    /// 계약: 목록의 수정 시각은 형식과 상관없이 UTC 로 오지 않는다(화면이 받은 값을 그대로 찍음 — UTC 면 한국 PC 에서 9시간 이르게 보임).
    /// 예전엔 RAR5 유닉스 시각·gzip 머리 시각(.gz·.tar.gz)이 UTC 로 보였다(통합 점검에서 베이스가 현지 시각으로 바꾸게 함).
    /// </summary>
    [Theory]
    [InlineData("fx:zip_default")]
    [InlineData("fx:7z_default")]
    [InlineData("fx:tar_default")]
    [InlineData("fx:gz_single")]
    [InlineData("fx:targz_default")]
    [InlineData("syn:rar5_unix")]
    [InlineData("syn:rar4")]
    [InlineData("syn:egg")]
    [InlineData("syn:alz")]
    public async Task 목록의_수정시각은_형식과_상관없이_UTC로_오지_않는다(string spec)
    {
        using var tmp = new TempDir("fftm");
        var c = Make(spec, tmp);
        var list = await Svc.For(c.Path).ListEntriesAsync(c.Path, c.Password);
        Assert.NotEmpty(list);
        Assert.All(list.Where(e => e.LastModified is not null), e => Assert.NotEqual(DateTimeKind.Utc, e.LastModified!.Value.Kind));
    }

    /// <summary>폴더 트리의 각 폴더(맨 위 포함)를 골랐을 때 오른쪽 목록 이름이 기대의 직속 항목(파일 + 하위 폴더)과 같다.</summary>
    private static void AssertFolderTree(MainViewModel vm, List<ExpectedEntry> expected, string context)
    {
        static string Parent(string p) => p.Contains('/') ? p[..p.LastIndexOf('/')] : string.Empty;
        static string Leaf(string p) => p.Contains('/') ? p[(p.LastIndexOf('/') + 1)..] : p;

        foreach (var dir in expected.Where(e => e.IsDir).Select(e => e.Path).Prepend(string.Empty))
        {
            vm.NavigateToFolder(dir);
            Assert.Equal(dir, vm.SelectedFolder?.FullPath ?? "(없음)");
            var want = expected.Where(e => Parent(e.Path) == dir).Select(e => (Leaf(e.Path), e.IsDir)).OrderBy(x => x.Item1, StringComparer.Ordinal).ToList();
            var got = vm.VisibleEntries.Select(e => (e.Name, e.IsDirectory)).OrderBy(x => x.Name, StringComparer.Ordinal).ToList();
            Assert.True(want.SequenceEqual(got), $"{context} '{dir}' 폴더 목록이 다름\n  기대: {string.Join(" | ", want)}\n  실제: {string.Join(" | ", got)}");
        }
    }
}
