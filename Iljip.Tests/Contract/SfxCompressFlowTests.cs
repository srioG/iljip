using System.ComponentModel;
using System.Text;
using Iljip.Models;
using Iljip.Services;
using Iljip.Tests.TestSupport;
using Iljip.ViewModels;
using Iljip.Views;

namespace Iljip.Tests.Contract;

/// <summary>
/// [W2-10, I3-1] 자동 풀림 EXE 를 압축 창으로 만드는 흐름(MainViewModel.Compress.cs) — 창은 가짜(<see cref="FakeUserInteraction"/>), 휴지통·설정도 가짜.
/// 계약: 만들기 직전 '회사 메일은 실행 파일(.exe)을 막는 경우가 많아요…' 를 한 번 묻는다([그래도 만들기][ZIP으로 만들기][취소] + '다시 묻지 않기'),
/// [ZIP으로 만들기]면 같은 이름의 .zip, [취소]·창 닫기면 아무것도 만들지 않고 설정도 저장하지 않는다, 설정(sfx.skipMailWarning)으로 끄면 묻지 않는다.
/// 압축 창의 칸 규칙(형식 칸 아래 안내·ZIP 암호 방식·나누기 없음), 마지막 형식으로 기억하지 않음, 다 만들고 시험하기·원본 지우기·각각 따로도 된다.
/// (안내 창·압축 창의 실제 모양은 MANUAL_CHECKLIST 16절)
/// </summary>
public class SfxCompressFlowTests
{
    // ───────────── 도우미 ─────────────

    private sealed class Ctx : IDisposable
    {
        public Ctx(string tag, AppSettings? settings = null)
        {
            Temp = new TempDir(tag);
            Services = new TestServices(settings);
            Services.Clock.SetLocal(2026, 10, 14);
            Ui = new FakeUserInteraction();
            Vm = new MainViewModel(Ui, null, Services.Services);
            Src = Temp.Dir("원본");
            Out = Temp.Dir("만든곳");
        }

        public TempDir Temp { get; }
        public TestServices Services { get; }
        public FakeUserInteraction Ui { get; }
        public MainViewModel Vm { get; }
        public string Src { get; }
        public string Out { get; }

        public string File(string relative, string content)
        {
            string p = Path.Combine(Src, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(p)!);
            System.IO.File.WriteAllText(p, content, new UTF8Encoding(false));
            return p;
        }

        public void Dispose() => Temp.Dispose();
    }

    /// <summary>창의 답: 자동 풀림 EXE(나머지는 인자대로, 빼기 목록은 설정 그대로).</summary>
    private static Func<CompressionDialogRequest, CompressionRequest?> Answer(
        string folder, CompressionFormat format = CompressionFormat.Sfx, string? baseName = null, bool each = false,
        bool test = false, bool delete = false, CompressionOptions? options = null, bool overwrite = false) =>
        r => new CompressionRequest
        {
            Sources = r.Sources,
            OutputFolder = folder,
            BaseName = baseName ?? r.SuggestedBaseName,
            Format = format,
            Options = options ?? CompressionOptions.Default,
            EachSeparately = each,
            DateInName = DateNameFormat.None,
            ExcludeEnabled = true,
            ExcludePatterns = r.Defaults.ExcludePatterns,
            TestAfter = test,
            DeleteOriginalsAfter = delete,
            OverwriteExisting = overwrite,
        };

    private static async Task Compress(Ctx c, params string[] sources)
    {
        c.Vm.AddToStaging(sources);
        await VmRunner.Run(() => c.Vm.CompressCommand.ExecuteAsync(null), "압축");
    }

    private static async Task CompressAgain(Ctx c, params string[] sources)
    {
        c.Vm.StartNewStaging(sources);
        await VmRunner.Run(() => c.Vm.CompressCommand.ExecuteAsync(null), "압축");
    }

    private static string[] NamesIn(string folder) =>
        Directory.GetFiles(folder).Select(Path.GetFileName).OrderBy(n => n, StringComparer.Ordinal).ToArray()!;

    /// <summary>설계(W2-10)가 정한 안내 첫 문장 — 압축 창의 형식 칸 아래와 만들기 직전 질문 모두.</summary>
    /// <summary>재사용장치가 '압축이 아님'으로 읽는 표지 — 손상 문구에 들어가면 안 됨(ReuseDeviceContractTests 와 같은 목록).</summary>
    private static readonly string[] NotArchiveMarkers = { "압축 파일이 아닌", "압축파일이 아닌", "not archive", "not an archive", "unsupported archive" };

    private const string DesignNotice = "회사 메일은 실행 파일(.exe)을 막는 경우가 많아요. 받는 사람이 못 받을 수 있어요. 메일로 보낼 거면 ZIP을 권해요.";

    // ───────────── 만들기 직전 메일 안내 ─────────────

    /// <summary>
    /// 계약: 처음 만들 때 메일 안내를 한 번 묻는다 — 문구(설계 첫 문장), 버튼 [그래도 만들기][ZIP으로 만들기][취소], 확인란 '다시 묻지 않기', 기본은 [그래도 만들기].
    /// [그래도 만들기]면 '이름.exe' 가 생기고(ZIP 은 없음) 일집이 그 exe 를 열어 보인다(안의 목록). 확인란을 켜지 않았으면 다음에도 묻는다.
    /// </summary>
    [Fact]
    public async Task 처음_만들_때_메일_안내를_한_번_묻고_그래도_만들기면_exe가_생긴다()
    {
        using var c = new Ctx("sfxask");
        string file = c.File("보고서.txt", "보고서 내용");
        c.Ui.ChoiceAnswers.Enqueue(0);
        c.Ui.CompressionRequestAnswer = Answer(c.Out);

        await Compress(c, file);

        var prompt = Assert.Single(c.Ui.CheckChoicePrompts);
        Assert.StartsWith(DesignNotice, prompt.Message);
        Assert.Equal(new[] { "그래도 만들기", "ZIP으로 만들기", "취소" }, prompt.Choices);
        Assert.Equal("다시 묻지 않기", prompt.CheckText);
        Assert.Equal(0, prompt.DefaultIndex);
        Assert.Single(c.Ui.ChoicePrompts);   // 창은 하나뿐
        Assert.Empty(c.Ui.Errors);
        Assert.Empty(c.Ui.Warnings);

        string exe = Path.Combine(c.Out, "보고서.exe");
        Assert.Equal(new[] { "보고서.exe" }, NamesIn(c.Out));
        Assert.True(c.Vm.IsArchiveMode, "만든 exe 를 열어 보이지 않음");
        Assert.Equal(Path.GetFullPath(exe), Path.GetFullPath(c.Vm.CurrentArchivePath!));
        var listed = await Svc.For(exe).ListEntriesAsync(exe);
        Assert.Equal(new[] { "보고서.txt" }, listed.Where(e => !e.IsDirectory).Select(e => e.Path).ToArray());

        // 확인란을 켜지 않았으면 다음에도 묻는다
        Assert.False(c.Services.Settings.Load().Sfx.SkipMailWarning);
        c.Ui.ChoiceAnswers.Enqueue(0);
        await CompressAgain(c, c.File("견적.txt", "견적"));
        Assert.Equal(2, c.Ui.CheckChoicePrompts.Count);
        Assert.True(System.IO.File.Exists(Path.Combine(c.Out, "견적.exe")));
    }

    /// <summary>
    /// 계약: [ZIP으로 만들기]면 같은 이름·같은 옵션(암호 포함)의 .zip 을 만들고 exe 는 만들지 않는다. 같은 이름 ZIP 이 이미 있으면 바꾸지 않고 '이름 (2).zip'
    /// (압축 창에서 [바꾸기]를 고른 것은 exe 이름에 대한 답이라서). 마지막 형식은 ZIP 으로 기억한다.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ZIP으로_만들기면_zip이_생기고_exe는_없다(bool zipExists)
    {
        using var c = new Ctx("sfxzip" + (zipExists ? "x" : "n"));
        string file = c.File("보고서.txt", "보고서 내용");
        string existing = Path.Combine(c.Out, "보고서.zip");
        if (zipExists) System.IO.File.WriteAllText(existing, "예전 압축");
        c.Ui.ChoiceAnswers.Enqueue(1);
        c.Ui.CompressionRequestAnswer = Answer(c.Out, overwrite: true,
            options: new CompressionOptions { Password = FixtureCatalog.KoreanPassword, Encryption = ZipEncryption.ZipCrypto });

        await Compress(c, file);

        Assert.Single(c.Ui.CheckChoicePrompts);
        Assert.Single(c.Ui.ChoicePrompts);   // ZipCrypto 한글 암호는 반디집과 맞으므로 한글 암호 질문은 없음(검증 지적 1차)
        Assert.Empty(c.Ui.Errors);
        string made = Path.Combine(c.Out, zipExists ? "보고서 (2).zip" : "보고서.zip");
        Assert.True(System.IO.File.Exists(made), "ZIP 이 안 생김: " + made);
        Assert.False(System.IO.File.Exists(Path.Combine(c.Out, "보고서.exe")));
        Assert.Empty(Directory.GetFiles(c.Out, "*.exe"));
        if (zipExists) Assert.Equal("예전 압축", System.IO.File.ReadAllText(existing));
        // 같은 옵션: 암호 그대로(ZipCrypto + 한글 암호 — CP949 로 씀, 반디집·알집과 같은 방식)
        Assert.IsType<PasswordRequiredException>(
            await Svc.Catch(() => Svc.For(made).ExtractAsync(made, c.Temp.Dir("암호없이"), null)));
        Assert.Equal(PasswordBytes.Cp949, ZipPasswordProbe.Probe(made, FixtureCatalog.KoreanPassword).Match);
        string dest = c.Temp.Dir("풀어봄");
        await Svc.For(made).ExtractAsync(made, dest, FixtureCatalog.KoreanPassword);
        Assert.Equal("보고서 내용", System.IO.File.ReadAllText(Path.Combine(dest, "보고서.txt")));
        Assert.Equal(CompressionFormat.Zip, c.Services.Settings.Load().Compress.LastFormat);
    }

    /// <summary>
    /// 계약(W2-10 검증 지적 1차): [ZIP으로 만들기]인데 암호가 AES-256 + 한글이면 보통 ZIP 이 반디집·알집에서 'Invalid Password' 가 되므로
    /// 압축 창의 ZIP 과 같은 내용을 한 번 더 묻는다 — [ZipCrypto 방식으로 만들기](기본)·[AES-256 그대로]·[취소].
    /// ZipCrypto 를 고르면 ZIP 은 ZipCrypto(AES 아님)·암호는 CP949 바이트(반디집·알집과 같은 방식)로 써지고 그 방식을 기억한다. AES 그대로면 AES ZIP.
    /// 어느 쪽이든 일집에서는 그 한글 암호로 풀리고 exe 는 없다.
    /// </summary>
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public async Task ZIP으로_만들기에서_AES_한글_암호면_호환을_한_번_더_묻는다(int answer)
    {
        using var c = new Ctx("sfxzipaes" + answer);
        string file = c.File("보고서.txt", "보고서 내용");
        c.Ui.ChoiceAnswers.Enqueue(1);        // 메일 안내: ZIP으로 만들기
        c.Ui.ChoiceAnswers.Enqueue(answer);   // 한글 암호 호환
        c.Ui.CompressionRequestAnswer = Answer(c.Out,
            options: new CompressionOptions { Password = FixtureCatalog.KoreanPassword, Encryption = ZipEncryption.Aes256 });

        await Compress(c, file);

        Assert.Single(c.Ui.CheckChoicePrompts);
        Assert.Equal(2, c.Ui.ChoicePrompts.Count);
        var compat = c.Ui.ChoicePrompts[1];
        Assert.Equal(MainViewModel.SfxZipKoreanAesQuestion, compat.Message);
        Assert.Contains("반디집·알집", compat.Message);
        Assert.Equal(3, compat.Choices.Count);
        Assert.StartsWith("ZipCrypto 방식으로 만들기", compat.Choices[0], StringComparison.Ordinal);
        Assert.StartsWith("AES-256 그대로", compat.Choices[1], StringComparison.Ordinal);
        Assert.StartsWith("취소", compat.Choices[2], StringComparison.Ordinal);
        Assert.Equal(0, compat.DefaultIndex);
        Assert.Empty(c.Ui.Errors);

        string made = Path.Combine(c.Out, "보고서.zip");
        Assert.Equal(new[] { "보고서.zip" }, NamesIn(c.Out));
        var entry = Assert.Single(ZipInspect.Read(made));
        Assert.True(entry.Encrypted);
        if (answer == 0)
        {
            Assert.NotEqual(99, entry.Method);   // AES 가 아님(ZipCrypto)
            Assert.Equal(PasswordBytes.Cp949, ZipPasswordProbe.Probe(made, FixtureCatalog.KoreanPassword).Match);
            Assert.Equal(ZipEncryption.ZipCrypto, c.Services.Settings.Load().Compress.ZipEncryption);
        }
        else
        {
            Assert.Equal(99, entry.Method);
            Assert.Equal(ZipEncryption.Aes256, c.Services.Settings.Load().Compress.ZipEncryption);
        }
        string dest = c.Temp.Dir("풀어봄");
        await Svc.For(made).ExtractAsync(made, dest, FixtureCatalog.KoreanPassword);
        Assert.Equal("보고서 내용", System.IO.File.ReadAllText(Path.Combine(dest, "보고서.txt")));
    }

    /// <summary>
    /// 계약(W2-10 검증 지적 1차): 그 한글 암호 질문에서 [취소]·창 닫기면 아무것도 만들지 않고('압축 취소됨') 설정도 저장하지 않는다 —
    /// 메일 안내에서 '다시 묻지 않기'를 켰어도 기억하지 않는다. 압축 대기 목록은 그대로.
    /// </summary>
    [Theory]
    [InlineData(2)]
    [InlineData(-1)]
    public async Task ZIP으로_만들기의_한글_암호_질문에서_취소하면_아무것도_만들지_않는다(int answer)
    {
        using var c = new Ctx("sfxzipaesx" + (answer + 1));
        c.Ui.ChoiceAnswers.Enqueue(1);
        c.Ui.ChoiceAnswers.Enqueue(answer);
        c.Ui.CheckAnswer = true;
        c.Ui.CompressionRequestAnswer = Answer(c.Out,
            options: new CompressionOptions { Password = FixtureCatalog.KoreanPassword, Encryption = ZipEncryption.Aes256 });

        await Compress(c, c.File("보고서.txt", "보고서 내용"));

        Assert.Equal(2, c.Ui.ChoicePrompts.Count);
        Assert.Empty(Directory.GetFileSystemEntries(c.Out));
        Assert.Equal("압축 취소됨", c.Vm.StatusText);
        Assert.Equal(0, c.Services.Settings.SaveCount);
        Assert.False(c.Vm.Settings.Sfx.SkipMailWarning);
        Assert.True(c.Vm.IsStagingMode, "취소했는데 압축 대기 목록이 비었음");
        Assert.Empty(c.Ui.Errors);
    }

    /// <summary>
    /// 계약(W2-10 검증 지적 1차): [ZIP으로 만들기]인데 암호에 CP949 로 못 바꾸는 글자(이모지)가 있으면 어느 방식으로도 반디집·알집과 맞출 수 없으므로
    /// [취소](기본)·[그대로 만들기] 만 묻는다(ZipCrypto 로 바꾸기는 권하지 않음). 그대로면 ZIP 이 생기고 일집에서는 풀린다.
    /// </summary>
    [Theory]
    [InlineData(1, true)]
    [InlineData(0, false)]
    public async Task ZIP으로_만들기에서_다른_프로그램이_못_읽는_암호면_묻는다(int answer, bool expectZip)
    {
        const string emoji = "암호😀abc123";
        using var c = new Ctx("sfxzipemoji" + answer);
        c.Ui.ChoiceAnswers.Enqueue(1);
        c.Ui.ChoiceAnswers.Enqueue(answer);
        c.Ui.CompressionRequestAnswer = Answer(c.Out,
            options: new CompressionOptions { Password = emoji, Encryption = ZipEncryption.ZipCrypto });

        await Compress(c, c.File("보고서.txt", "보고서 내용"));

        Assert.Equal(2, c.Ui.ChoicePrompts.Count);
        var q = c.Ui.ChoicePrompts[1];
        Assert.Equal(MainViewModel.SfxZipUnreadablePasswordQuestion, q.Message);
        Assert.Equal(2, q.Choices.Count);
        Assert.StartsWith("취소", q.Choices[0], StringComparison.Ordinal);
        Assert.StartsWith("그대로 만들기", q.Choices[1], StringComparison.Ordinal);
        Assert.DoesNotContain(q.Choices, ch => ch.Contains("ZipCrypto", StringComparison.Ordinal));
        Assert.Equal(0, q.DefaultIndex);
        if (!expectZip)
        {
            Assert.Empty(Directory.GetFileSystemEntries(c.Out));
            Assert.Equal("압축 취소됨", c.Vm.StatusText);
            return;
        }
        string made = Path.Combine(c.Out, "보고서.zip");
        Assert.Equal(new[] { "보고서.zip" }, NamesIn(c.Out));
        string dest = c.Temp.Dir("풀어봄");
        await Svc.For(made).ExtractAsync(made, dest, emoji);
        Assert.Equal("보고서 내용", System.IO.File.ReadAllText(Path.Combine(dest, "보고서.txt")));
    }

    /// <summary>
    /// 계약(W2-10 검증 지적 1차): 한글 암호 질문은 [ZIP으로 만들기]일 때만 — 암호 없음·영문 암호(AES)의 ZIP, 그리고 [그래도 만들기](exe — 스텁이 UTF-8·CP949 를
    /// 둘 다 맞춰 봄)는 AES 한글 암호여도 더 묻지 않는다.
    /// </summary>
    [Theory]
    [InlineData(1, null)]
    [InlineData(1, "abc12345")]
    [InlineData(0, FixtureCatalog.KoreanPassword)]
    public async Task 한글_암호_질문은_ZIP으로_바꿀_때만(int mailAnswer, string? password)
    {
        using var c = new Ctx("sfxzipnoq" + mailAnswer + (password is null ? "n" : password.Length));
        c.Ui.ChoiceAnswers.Enqueue(mailAnswer);
        c.Ui.CompressionRequestAnswer = Answer(c.Out,
            options: new CompressionOptions { Password = password, Encryption = ZipEncryption.Aes256 });

        await Compress(c, c.File("보고서.txt", "보고서 내용"));

        Assert.Single(c.Ui.ChoicePrompts);
        Assert.Empty(c.Ui.Errors);
        Assert.Equal(new[] { mailAnswer == 0 ? "보고서.exe" : "보고서.zip" }, NamesIn(c.Out));
    }

    /// <summary>계약: [취소]·창 닫기면 아무것도 만들지 않고 설정도 저장하지 않는다('압축 취소됨'). 확인란을 켰어도 기억하지 않는다.</summary>
    [Theory]
    [InlineData(2)]
    [InlineData(-1)]
    public async Task 취소하면_아무것도_만들지_않는다(int answer)
    {
        using var c = new Ctx("sfxcancel" + (answer + 1));
        string file = c.File("보고서.txt", "보고서 내용");
        c.Ui.ChoiceAnswers.Enqueue(answer);
        c.Ui.CheckAnswer = true;
        c.Ui.CompressionRequestAnswer = Answer(c.Out);

        await Compress(c, file);

        Assert.Single(c.Ui.CheckChoicePrompts);
        Assert.Empty(Directory.GetFileSystemEntries(c.Out));
        Assert.Equal("압축 취소됨", c.Vm.StatusText);
        Assert.Equal(0, c.Services.Settings.SaveCount);
        Assert.False(c.Vm.IsArchiveMode);
        Assert.True(c.Vm.IsStagingMode, "취소했는데 압축 대기 목록이 비었음");
        Assert.Empty(c.Ui.Errors);
    }

    /// <summary>계약: 설정 sfx.skipMailWarning 이 켜져 있으면 묻지 않고 바로 만든다(질문 0번).</summary>
    [Fact]
    public async Task 설정으로_끄면_묻지_않는다()
    {
        var s = new AppSettings();
        s.Sfx.SkipMailWarning = true;
        using var c = new Ctx("sfxskip", s);
        string file = c.File("보고서.txt", "보고서 내용");
        c.Ui.CompressionRequestAnswer = Answer(c.Out);

        await Compress(c, file);

        Assert.Empty(c.Ui.CheckChoicePrompts);
        Assert.Empty(c.Ui.ChoicePrompts);
        Assert.True(System.IO.File.Exists(Path.Combine(c.Out, "보고서.exe")));
    }

    /// <summary>
    /// 계약: '다시 묻지 않기'를 켜고 [그래도 만들기]를 고르면 settings.json 에 sfx.skipMailWarning = true 로 한 번 저장되고(압축 설정과 같은 저장),
    /// 다음 자동 풀림 만들기에서는 묻지 않는다. ([ZIP으로 만들기]와 함께 켠 것은 기억하지 않음 — <see cref="다시_묻지_않기를_켜도_ZIP으로_만들기면_기억하지_않고_다음에_또_묻는다"/>)
    /// </summary>
    [Fact]
    public async Task 다시_묻지_않기를_켜면_기억하고_다음엔_묻지_않는다()
    {
        using var c = new Ctx("sfxremember0");
        c.Ui.ChoiceAnswers.Enqueue(0);
        c.Ui.CheckAnswer = true;
        c.Ui.CompressionRequestAnswer = Answer(c.Out);

        await Compress(c, c.File("가.txt", "가"));

        Assert.Single(c.Ui.CheckChoicePrompts);
        Assert.Equal(new[] { "가.exe" }, NamesIn(c.Out));
        Assert.Equal(1, c.Services.Settings.SaveCount);
        Assert.Contains("\"skipMailWarning\": true", c.Services.Settings.Json!);
        Assert.True(c.Services.Settings.Load().Sfx.SkipMailWarning);
        Assert.True(c.Vm.Settings.Sfx.SkipMailWarning);

        await CompressAgain(c, c.File("나.txt", "나"));
        Assert.Single(c.Ui.CheckChoicePrompts);   // 두 번째는 묻지 않음
        Assert.True(System.IO.File.Exists(Path.Combine(c.Out, "나.exe")));
    }

    /// <summary>
    /// 계약(W2-10 검증 지적 2차): '다시 묻지 않기'를 켜고 [ZIP으로 만들기]를 고르면 ZIP 은 만들지만 그 확인란은 기억하지 않는다 — 다음에 자동 풀림을 고르면 다시 묻는다.
    /// 예전엔 기억해서 다음 자동 풀림이 묻지 않고 exe 로 만들어졌다(마지막으로 'exe 말고 ZIP' 을 고른 사람의 뜻과 반대). 압축 설정은 예전처럼 한 번 저장.
    /// </summary>
    [Fact]
    public async Task 다시_묻지_않기를_켜도_ZIP으로_만들기면_기억하지_않고_다음에_또_묻는다()
    {
        using var c = new Ctx("sfxremember1");
        c.Ui.ChoiceAnswers.Enqueue(1);
        c.Ui.CheckAnswer = true;
        c.Ui.CompressionRequestAnswer = Answer(c.Out);

        await Compress(c, c.File("가.txt", "가"));

        Assert.Single(c.Ui.CheckChoicePrompts);
        Assert.Equal(new[] { "가.zip" }, NamesIn(c.Out));
        Assert.Equal(1, c.Services.Settings.SaveCount);
        Assert.DoesNotContain("\"skipMailWarning\": true", c.Services.Settings.Json!);
        Assert.False(c.Services.Settings.Load().Sfx.SkipMailWarning);
        Assert.False(c.Vm.Settings.Sfx.SkipMailWarning);

        c.Ui.CheckAnswer = false;
        c.Ui.ChoiceAnswers.Enqueue(0);
        await CompressAgain(c, c.File("나.txt", "나"));
        Assert.Equal(2, c.Ui.CheckChoicePrompts.Count);   // 다시 묻는다
        Assert.True(System.IO.File.Exists(Path.Combine(c.Out, "나.exe")));
    }

    // ───────────── 4GB 넘는 자동 풀림(W2-10 검증 지적 2차) ─────────────

    /// <summary>무작위 바이트 파일(압축으로 줄지 않음 — 영상·사진처럼).</summary>
    private static string RandomFile(Ctx c, string name, int bytes, int seed)
    {
        var data = new byte[bytes];
        new Random(seed).NextBytes(data);
        string p = Path.Combine(c.Src, name);
        System.IO.File.WriteAllBytes(p, data);
        return p;
    }

    /// <summary>
    /// 계약: 원본(압축하기 전 크기 합)이 exe 한도(4GB — 시험은 작게)를 넘으면 만들기 전에(진행 창 '원본 크기를 재는 중' 동안 잰 뒤) 묻는다 —
    /// '원본이 모두 …예요. 자동 풀림 exe 는 4GB 를 넘으면 받는 사람 PC 에서 실행되지 않아요…' + [ZIP으로 만들기 (권장)](기본)·[그래도 자동 풀림 exe 로 해 보기 …]·[취소].
    /// [ZIP으로 만들기]면 같은 이름·같은 옵션(암호 포함)의 ZIP 이 생기고 exe 는 없다. 메일 안내 다음에 묻는다.
    /// </summary>
    [Fact]
    public async Task 원본이_4GB를_넘으면_만들기_전에_묻고_ZIP으로_만든다()
    {
        using var c = new Ctx("sfxbigzip");
        string file = RandomFile(c, "영상.bin", 10_000, 1);
        c.Ui.ChoiceAnswers.Enqueue(0);   // 메일 안내: 그래도 만들기
        c.Ui.ChoiceAnswers.Enqueue(0);   // 4GB: ZIP으로 만들기
        c.Ui.CompressionRequestAnswer = Answer(c.Out, options: new CompressionOptions { Password = "abc123" });

        using (SfxBuilder.LimitForTests(4_000))
            await Compress(c, file);

        Assert.Equal(2, c.Ui.ChoicePrompts.Count);
        var q = c.Ui.ChoicePrompts[1];
        Assert.StartsWith("원본이 모두 9.8KB 예요.", q.Message, StringComparison.Ordinal);
        Assert.Contains("4GB 를 넘으면 받는 사람 PC 에서 실행되지 않아요", q.Message);
        Assert.Contains("ZIP으로 만들기를 권해요", q.Message);
        Assert.Equal(MainViewModel.SfxTooLargeChoices, q.Choices);
        Assert.StartsWith("ZIP으로 만들기", q.Choices[0], StringComparison.Ordinal);
        Assert.StartsWith("그래도 자동 풀림", q.Choices[1], StringComparison.Ordinal);
        Assert.Equal("취소", q.Choices[2]);
        Assert.Equal(0, q.DefaultIndex);
        Assert.Contains(c.Ui.ProgressSessions[0].Reports, p => p.IsStage && p.CurrentFile == SfxBuilder.StageMeasuring);
        Assert.Empty(c.Ui.Errors);

        Assert.Equal(new[] { "영상.zip" }, NamesIn(c.Out));
        string zip = Path.Combine(c.Out, "영상.zip");
        Assert.IsType<PasswordRequiredException>(await Svc.Catch(() => Svc.For(zip).ExtractAsync(zip, c.Temp.Dir("암호없이"), null)));
        string dest = c.Temp.Dir("풀어봄");
        await Svc.For(zip).ExtractAsync(zip, dest, "abc123");
        Assert.Equal(System.IO.File.ReadAllBytes(file), System.IO.File.ReadAllBytes(Path.Combine(dest, "영상.bin")));
        Assert.Equal(CompressionFormat.Zip, c.Services.Settings.Load().Compress.LastFormat);
    }

    /// <summary>
    /// 계약: 그 질문에서 [취소]·창 닫기면 아무것도 만들지 않고('압축 취소됨') 설정도 저장하지 않는다 — 메일 안내에서 '다시 묻지 않기'를 켰어도 기억하지 않는다.
    /// 한도를 넘지 않으면 묻지 않는다(질문은 메일 안내 하나).
    /// </summary>
    [Theory]
    [InlineData(2)]
    [InlineData(-1)]
    public async Task 원본이_4GB를_넘는다는_질문에서_취소하면_아무것도_만들지_않는다(int answer)
    {
        using var c = new Ctx("sfxbigx" + (answer + 1));
        string file = RandomFile(c, "영상.bin", 10_000, 2);
        c.Ui.ChoiceAnswers.Enqueue(0);
        c.Ui.ChoiceAnswers.Enqueue(answer);
        c.Ui.CheckAnswer = true;
        c.Ui.CompressionRequestAnswer = Answer(c.Out);

        using (SfxBuilder.LimitForTests(4_000))
            await Compress(c, file);

        Assert.Equal(2, c.Ui.ChoicePrompts.Count);
        Assert.Empty(Directory.GetFileSystemEntries(c.Out));
        Assert.Equal("압축 취소됨", c.Vm.StatusText);
        Assert.Equal(0, c.Services.Settings.SaveCount);
        Assert.False(c.Vm.Settings.Sfx.SkipMailWarning);
        Assert.True(c.Vm.IsStagingMode, "취소했는데 압축 대기 목록이 비었음");
        Assert.Empty(c.Ui.Errors);

        // 한도를 넘지 않으면(시험 밖의 4GB) 메일 안내만 묻는다
        c.Ui.ChoiceAnswers.Enqueue(0);
        c.Ui.CheckAnswer = false;
        await CompressAgain(c, file);
        Assert.Equal(3, c.Ui.ChoicePrompts.Count);
        Assert.Equal(new[] { "영상.exe" }, NamesIn(c.Out));
    }

    /// <summary>
    /// 계약: [그래도 해 보기] 했는데 만든 exe 가 한도를 넘으면 만들지 않고(exe·임시 파일 없음) 그 까닭('…4GB 를 넘어서… 만들지 않았어요…') + 'ZIP으로 만들까요?' 를 묻는다 —
    /// [ZIP으로 만들기](기본)면 진행 창을 다시 띄워 같은 옵션의 ZIP 을 만들고 연다, [닫기]면 아무것도 없이 '오류 발생'(오류 창을 또 띄우지 않음).
    /// </summary>
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public async Task 그래도_해_보다가_4GB를_넘으면_만들지_않고_ZIP으로_만들지_묻는다(int retry)
    {
        using var c = new Ctx("sfxbigtry" + retry);
        string file = RandomFile(c, "영상.bin", 10_000, 3);
        c.Ui.ChoiceAnswers.Enqueue(0);       // 메일 안내: 그래도 만들기
        c.Ui.ChoiceAnswers.Enqueue(1);       // 4GB: 그래도 해 보기
        c.Ui.ChoiceAnswers.Enqueue(retry);   // 못 만들었어요 — ZIP으로 만들까요?
        c.Ui.CompressionRequestAnswer = Answer(c.Out);

        using (SfxBuilder.LimitForTests(4_000))
            await Compress(c, file);

        Assert.Equal(3, c.Ui.ChoicePrompts.Count);
        var q = c.Ui.ChoicePrompts[2];
        Assert.Contains("4GB 를 넘어서", q.Message);
        Assert.Contains("만들지 않았어요", q.Message);
        Assert.EndsWith(MainViewModel.SfxTooLargeRetryQuestion, q.Message, StringComparison.Ordinal);
        Assert.Equal(MainViewModel.SfxTooLargeRetryChoices, q.Choices);
        Assert.Equal(0, q.DefaultIndex);
        Assert.Empty(c.Ui.Errors);
        Assert.Empty(Directory.GetFiles(c.Out, "*.exe"));
        Assert.Empty(Directory.GetFiles(c.Out, "*.tmp"));
        if (retry == 0)
        {
            Assert.Equal(new[] { "영상.zip" }, NamesIn(c.Out));
            Assert.Equal(2, c.Ui.ProgressSessions.Count);
            Assert.True(c.Vm.IsArchiveMode, "만든 ZIP 을 열어 보이지 않음");
            Assert.Equal(Path.GetFullPath(Path.Combine(c.Out, "영상.zip")), Path.GetFullPath(c.Vm.CurrentArchivePath!));
        }
        else
        {
            Assert.Empty(Directory.GetFileSystemEntries(c.Out));
            Assert.Equal("오류 발생", c.Vm.StatusText);
            Assert.Single(c.Ui.ProgressSessions);
        }
    }

    /// <summary>
    /// 계약: 만들기 전 어림(원본 크기 합)은 한도 아래였어도 만든 exe 가 넘으면(스텁·머리 몫) 만들지 않고 ZIP 으로 만들지 묻는다(메일 안내 다음 질문 하나).
    /// 각각 따로면 묻지 않고 요약 창에 원본마다 까닭('4GB'·'ZIP')을 보인다(exe 없음).
    /// </summary>
    [Fact]
    public async Task 만든_뒤에_넘으면_하나는_ZIP으로_묻고_각각_따로는_요약에_까닭을_보인다()
    {
        using var c = new Ctx("sfxbigafter");
        string a = c.File("가.txt", "가 내용");
        string b = c.File("나.txt", "나 내용");
        c.Ui.ChoiceAnswers.Enqueue(0);   // 메일 안내
        c.Ui.ChoiceAnswers.Enqueue(0);   // 못 만들었어요 — ZIP으로
        c.Ui.CompressionRequestAnswer = Answer(c.Out);
        using (SfxBuilder.LimitForTests(20_000))   // 원본(몇 바이트)은 아래, 스텁(수십 KB)을 붙인 exe 는 위
            await Compress(c, a);
        Assert.Equal(2, c.Ui.ChoicePrompts.Count);
        Assert.EndsWith(MainViewModel.SfxTooLargeRetryQuestion, c.Ui.ChoicePrompts[1].Message, StringComparison.Ordinal);
        Assert.Equal(new[] { "가.zip" }, NamesIn(c.Out));

        c.Ui.ChoiceAnswers.Enqueue(0);   // 메일 안내
        c.Ui.CompressionRequestAnswer = Answer(c.Out, each: true);
        using (SfxBuilder.LimitForTests(20_000))
            await CompressAgain(c, a, b);
        Assert.Equal(3, c.Ui.ChoicePrompts.Count);   // 각각 따로는 더 묻지 않음
        var summary = Assert.Single(c.Ui.Summaries);
        Assert.Equal(2, summary.Total);
        Assert.Equal(0, summary.Succeeded);
        Assert.Equal(2, summary.Failures.Count);
        Assert.All(summary.Failures, f =>
        {
            Assert.Contains("4GB", f.Reason);
            Assert.Contains("ZIP", f.Reason);
        });
        Assert.Equal(new[] { "가.zip" }, NamesIn(c.Out));
    }

    /// <summary>계약: 각각 따로에서 한도를 넘는 원본이 있으면 만들기 전에 한 번 묻는다('원본 중 1개가 4GB 를 넘어요(가장 큰 것 …)') — [ZIP으로 만들기]면 모두 ZIP.</summary>
    [Fact]
    public async Task 각각_따로도_4GB를_넘는_원본이_있으면_한_번_묻는다()
    {
        using var c = new Ctx("sfxbigeach");
        string big = RandomFile(c, "영상.bin", 10_000, 4);
        string small = c.File("메모.txt", "짧음");
        c.Ui.ChoiceAnswers.Enqueue(0);
        c.Ui.ChoiceAnswers.Enqueue(0);
        c.Ui.CompressionRequestAnswer = Answer(c.Out, each: true);

        using (SfxBuilder.LimitForTests(4_000))
            await Compress(c, big, small);

        Assert.Equal(2, c.Ui.ChoicePrompts.Count);
        Assert.StartsWith("원본 중 1개가 4GB 를 넘어요(가장 큰 것 9.8KB).", c.Ui.ChoicePrompts[1].Message, StringComparison.Ordinal);
        Assert.Equal(new[] { "메모.zip", "영상.zip" }, NamesIn(c.Out));
    }

    // ───────────── 만드는 흐름 ─────────────

    /// <summary>계약: 자동 풀림 EXE 는 마지막 형식으로 기억하지 않는다(다음 압축 창이 exe 로 열려 메일로 보내다 막히지 않게) — 암호 방식·수준은 기억한다.</summary>
    [Fact]
    public async Task 자동풀림은_마지막_형식으로_기억하지_않는다()
    {
        using var c = new Ctx("sfxlast");
        c.Ui.ChoiceAnswers.Enqueue(0);
        c.Ui.CompressionRequestAnswer = Answer(c.Out,
            options: new CompressionOptions { Level = CompressionLevelOption.Maximum, Password = "abc123", Encryption = ZipEncryption.ZipCrypto });

        await Compress(c, c.File("가.txt", "가"));

        Assert.Equal(1, c.Services.Settings.SaveCount);
        var saved = c.Services.Settings.Load().Compress;
        Assert.Equal(CompressionFormat.Zip, saved.LastFormat);
        Assert.Equal(CompressionLevelOption.Maximum, saved.Level);
        Assert.Equal(ZipEncryption.ZipCrypto, saved.ZipEncryption);
        Assert.DoesNotContain("abc123", c.Services.Settings.Json!);

        c.Ui.CompressionRequestAnswer = _ => null;
        await CompressAgain(c, c.File("나.txt", "나"));
        Assert.Equal(CompressionFormat.Zip, c.Ui.CompressionPrompts[^1].Defaults.LastFormat);
    }

    /// <summary>
    /// 계약: 다 만들고 시험하기(진짜 시험기 — 일집 안에서 ZIP 부분을 끝까지 풀어 봄)를 통과하고 '옮기기'를 고르면 원본을 휴지통(가짜)으로.
    /// 암호 없음·AES-256·ZipCrypto 한글 암호 모두.
    /// </summary>
    [Theory]
    [InlineData(null, ZipEncryption.Aes256)]
    [InlineData("abc123", ZipEncryption.Aes256)]
    [InlineData(FixtureCatalog.KoreanPassword, ZipEncryption.ZipCrypto)]
    public async Task 다_만들고_시험하고_원본을_옮긴다(string? password, ZipEncryption encryption)
    {
        using var c = new Ctx("sfxtest" + (password is null ? "n" : encryption == ZipEncryption.Aes256 ? "a" : "z"));
        string root = SampleTree.Create(c.Src);
        c.Ui.ChoiceAnswers.Enqueue(0);   // 메일 안내: 그래도 만들기
        c.Ui.ChoiceAnswers.Enqueue(0);   // 원본: 옮기기
        c.Ui.CompressionRequestAnswer = Answer(c.Out, test: true, delete: true,
            options: new CompressionOptions { Password = password, Encryption = encryption });

        await Compress(c, root);

        Assert.Empty(c.Ui.Errors);
        Assert.Empty(c.Ui.Warnings);
        string exe = Assert.Single(Directory.GetFiles(c.Out, "*.exe"));
        Assert.Equal(new[] { exe }, c.Services.CompressTester.Tested);
        Assert.Equal(new[] { root }, c.Services.RecycleBin.AllSent);
        Assert.Equal(2, c.Ui.ChoicePrompts.Count);

        string dest = c.Temp.Dir("풀어봄");
        await Svc.For(exe).ExtractAsync(exe, dest, password);
        TreeAssert.ExtractedTreeEquals(dest, SampleTree.ExpectedOf(root), context: "자동 풀림");
    }

    /// <summary>계약: 진짜 시험기는 깨진 자동 풀림 exe 를 잡는다(가운데 바이트를 바꿈 — CRC 불일치) → 원본을 지우지 않는다.</summary>
    [Fact]
    public async Task 시험기는_깨진_자동풀림을_잡는다()
    {
        using var t = new TempDir("sfxtester");
        string src = t.Dir("원본");
        var rnd = new Random(10);
        var data = new byte[200_000];
        rnd.NextBytes(data);
        System.IO.File.WriteAllBytes(Path.Combine(src, "무작위.bin"), data);
        string exe = t.P("깨짐.exe");
        await SfxBuilder.CreateAsync(new[] { src }, exe, new CompressionOptions { Level = CompressionLevelOption.Store });
        CreatedArchiveTester.Instance.Test(exe, null, CancellationToken.None);   // 깨기 전에는 통과

        var bytes = System.IO.File.ReadAllBytes(exe);
        long stubLength = new FileInfo(SfxBuilder.StubPath).Length;
        bytes[stubLength + 100_000] ^= 0xFF;   // 저장만(Store)이라 파일 내용 한가운데
        System.IO.File.WriteAllBytes(exe, bytes);

        var ex = Record.Exception(() => CreatedArchiveTester.Instance.Test(exe, null, CancellationToken.None));
        Assert.NotNull(ex);
        Assert.IsAssignableFrom<ArchiveException>(ex);
        string message = ErrorMessages.ToUserMessage(ex);
        foreach (var marker in NotArchiveMarkers) Assert.DoesNotContain(marker, message, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>계약: 각각 따로도 원본마다 자동 풀림 exe 하나(안내는 한 번만 묻는다). 각 exe 는 일집 안에서 그 원본만 담고 있다.</summary>
    [Fact]
    public async Task 각각_따로도_원본마다_exe_하나()
    {
        using var c = new Ctx("sfxeach");
        string a = c.File("가.txt", "가 내용");
        string b = c.File("나.txt", "나 내용");
        c.Ui.ChoiceAnswers.Enqueue(0);
        c.Ui.CompressionRequestAnswer = Answer(c.Out, each: true);

        await Compress(c, a, b);

        Assert.Single(c.Ui.CheckChoicePrompts);
        Assert.Equal(new[] { "가.exe", "나.exe" }, NamesIn(c.Out));
        foreach (var (exe, name) in new[] { ("가.exe", "가.txt"), ("나.exe", "나.txt") })
        {
            string p = Path.Combine(c.Out, exe);
            var listed = await Svc.For(p).ListEntriesAsync(p);
            Assert.Equal(new[] { name }, listed.Select(e => e.Path).ToArray());
        }
    }

    /// <summary>계약: 스텁(sfx\Iljip.SfxStub.exe)이 없으면(백신이 지움 등) 압축 창의 형식 목록에 '자동 풀림 EXE' 가 없다.</summary>
    [Fact]
    public void 스텁이_없으면_형식_목록에_없다()
    {
        using var c = new Ctx("sfxmissing");
        string a = c.File("가.txt", "a");
        Assert.Contains(CompressionFormat.Sfx, c.Vm.BuildCompressionDialogRequest(new[] { a }).AvailableFormats);
        using (SfxBuilder.SimulateMissingForTests())
            Assert.DoesNotContain(CompressionFormat.Sfx, c.Vm.BuildCompressionDialogRequest(new[] { a }).AvailableFormats);
        Assert.Contains(CompressionFormat.Sfx, c.Vm.BuildCompressionDialogRequest(new[] { a }).AvailableFormats);
    }

    /// <summary>계약: 앱 흐름으로 만든 exe 를 무인 모드(/s)로 실제로 풀면 원본과 같다(창 없음).</summary>
    [SfxStubFact]
    public async Task 앱에서_만든_exe를_무인으로_풀면_원본과_같다()
    {
        using var c = new Ctx("sfxapprun");
        string root = SampleTree.Create(c.Src);
        c.Ui.ChoiceAnswers.Enqueue(0);
        c.Ui.CompressionRequestAnswer = Answer(c.Out, options: new CompressionOptions { Password = FixtureCatalog.KoreanPassword });

        await Compress(c, root);

        string exe = Assert.Single(Directory.GetFiles(c.Out, "*.exe"));
        string dest = c.Temp.P("푼곳");
        var run = SfxCreateTests.RunSilent(exe, "/s", "/d:" + dest, "/p:" + FixtureCatalog.KoreanPassword);
        Assert.True(run.ExitCode == 0, $"종료 코드 {run.ExitCode}: {run.Error}");
        run.AssertNoWindow();
        TreeAssert.ExtractedTreeEquals(dest, SampleTree.ExpectedOf(root), context: "자동 풀림");
    }

    // ───────────── 압축 창의 칸 규칙(순수 VM — 창을 띄우지 않음) ─────────────

    private static CompressionDialogViewModel DialogVm(string folder) =>
        new(new CompressionDialogRequest
        {
            Sources = new[] { Path.Combine(folder, "보고서.xlsx") },
            AllSourcesAreFiles = true,
            SuggestedFolder = folder,
            SuggestedBaseName = "보고서",
            AvailableFormats = new[] { CompressionFormat.Zip, CompressionFormat.SevenZip, CompressionFormat.Sfx },
            Defaults = new CompressSettings(),
        }, new FakeClock().SetLocal(2026, 10, 14));

    /// <summary>
    /// 계약: '자동 풀림 EXE' 를 고르면 형식 칸 아래 안내(설계 문구 그대로)가 보이고, ZIP 암호 방식(AES-256·ZipCrypto) 칸이 보이고, 나누기·7z 칸은 숨는다.
    /// 확장자 .exe·미리 보기 '보고서.exe'·암호 칸 켬·빼기·원본 지우기 가능. ZIP 으로 돌아가면 안내가 숨는다(바뀜 알림 포함).
    /// </summary>
    [Fact]
    public void 자동풀림을_고르면_안내가_보이고_나누기는_숨는다()
    {
        var vm = DialogVm(@"C:\자료");
        Assert.False(vm.ShowSfxNotice);
        Assert.Contains(vm.Formats, f => f.Format == CompressionFormat.Sfx && f.Label.StartsWith("자동 풀림 EXE", StringComparison.Ordinal));

        var changed = new List<string>();
        PropertyChangedEventHandler h = (_, e) => changed.Add(e.PropertyName!);
        vm.PropertyChanged += h;
        vm.Format = CompressionFormat.Sfx;
        vm.PropertyChanged -= h;

        Assert.Contains(nameof(vm.ShowSfxNotice), changed);
        Assert.True(vm.ShowSfxNotice);
        Assert.Equal(DesignNotice, vm.SfxNotice);
        Assert.Equal(".exe", vm.Extension);
        Assert.Equal("보고서.exe", vm.PreviewName);
        Assert.True(vm.PasswordEnabled);
        Assert.True(vm.ShowZipEncryption);
        Assert.False(vm.ShowSplit);
        Assert.False(vm.ShowSevenZipOptions);
        Assert.True(vm.ExcludeAvailable);
        Assert.True(vm.DeleteAvailable);
        Assert.Contains("PC 보호", vm.Hint);
        Assert.Contains("4GB 가 넘는 exe 는 Windows 에서 실행되지 않아", vm.Hint);   // W2-10 검증 지적 2차

        vm.Format = CompressionFormat.Zip;
        Assert.False(vm.ShowSfxNotice);
    }

    /// <summary>
    /// 계약: 자동 풀림의 엔진 옵션 — 암호 방식(ZipCrypto)은 그대로, 나누기는 버림, 압축 창은 한글 암호 호환 경고를 하지 않음(스텁이 UTF-8·CP949 를 둘 다 맞춰 봄 —
    /// [ZIP으로 만들기]로 바꿀 때는 따로 묻는다: <see cref="ZIP으로_만들기에서_AES_한글_암호면_호환을_한_번_더_묻는다"/>).
    /// </summary>
    [Fact]
    public void 자동풀림_옵션은_ZIP_암호_방식을_쓰고_나누기를_버린다()
    {
        var o = CompressionOptionsRules.Build(2, "가나다", ".exe", ZipEncryption.ZipCrypto, solid: false, encryptFileNames: true, splitBytes: 10 * CompressionOptionsRules.MegaByte);
        Assert.Equal(ZipEncryption.ZipCrypto, o.Encryption);
        Assert.Equal("가나다", o.Password);
        Assert.Null(o.SplitBytes);
        Assert.False(o.EncryptFileNames);
        Assert.True(CompressionOptionsRules.UsesZipEncryption(".exe"));
        Assert.False(CompressionOptionsRules.ChecksPasswordCompat(".exe"));
        Assert.False(CompressionFormats.SupportsSplit(CompressionFormat.Sfx));
        Assert.Equal(".exe", CompressionFormats.ExtensionOf(CompressionFormat.Sfx));
        Assert.Equal(CompressionFormat.Sfx, CompressionFormats.FromExtension(".EXE"));
    }

    /// <summary>계약: 설정 sfx.skipMailWarning 은 settings.json 에 쓰고 다시 읽으면 같다(기본 꺼짐 = 묻기). sfx 묶음의 모르는 필드는 남고, sfx 가 없는 옛 설정은 기본값.</summary>
    [Fact]
    public void 메일_안내_설정은_저장되고_모르는_필드는_남는다()
    {
        using var t = new TempDir("sfxsettings");
        string path = t.P("settings.json");
        var store = new JsonSettingsStore(path);
        Assert.False(new AppSettings().Sfx.SkipMailWarning);

        var s = new AppSettings();
        s.Sfx.SkipMailWarning = true;
        store.Save(s);
        string json = System.IO.File.ReadAllText(path);
        Assert.Contains("\"sfx\"", json);
        Assert.Contains("\"skipMailWarning\": true", json);
        Assert.True(store.Load().Sfx.SkipMailWarning);

        System.IO.File.WriteAllText(path, "{ \"schemaVersion\": 1, \"sfx\": { \"skipMailWarning\": true, \"futureKey\": 3 } }");
        var withUnknown = store.Load();
        Assert.True(withUnknown.Sfx.SkipMailWarning);
        store.Save(withUnknown);
        Assert.Contains("\"futureKey\": 3", System.IO.File.ReadAllText(path));

        System.IO.File.WriteAllText(path, "{ \"schemaVersion\": 1, \"sfx\": null, \"compress\": { \"level\": \"Fastest\" } }");
        var old = store.Load();
        Assert.NotNull(old.Sfx);
        Assert.False(old.Sfx.SkipMailWarning);
    }
}
