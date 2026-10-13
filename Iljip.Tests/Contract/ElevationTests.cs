using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Principal;
using Iljip.Models;
using Iljip.Services;
using Iljip.Tests.TestSupport;
using Iljip.ViewModels;

namespace Iljip.Tests.Contract;

/// <summary>
/// [W2-11 I3-16] '관리자 권한으로 다시 시도': 권한이 필요한 곳(Program Files 등)에 풀거나 압축하다 권한 오류가 나면 오류 창 대신 묻고, 고르면 같은 작업 인자로 runas.
/// 시험은 실제 권한 상승을 하지 않는다 — 인자 만들기(<see cref="ElevationService"/>·<see cref="CommandLineQuoting"/>)와 분기만 보고, 띄우기는 가짜(<see cref="FakeProcessLauncher"/>).
/// 권한 오류는 시험 임시 폴더에만 현재 사용자 '파일·폴더 만들기 거부' 권한을 걸어 만든다(시스템 설정은 건드리지 않음, 끝나면 되돌림).
/// 진짜 UAC 창·관리자 일집의 진행 창·네트워크 드라이브는 MANUAL_CHECKLIST 17절.
/// </summary>
public class ElevationTests
{
    private static Task Run(Func<Task> action, string what) => VmRunner.Run(action, what);

    private static string Copy(string work, string fixtureId, string? name = null)
    {
        var fx = FixtureCatalog.Get(fixtureId);
        string dst = Path.Combine(work, name ?? Path.GetFileName(fx.FullPath));
        File.Copy(fx.FullPath, dst);
        return dst;
    }

    // ───────────────────────────── 따옴표 싸기(CommandLineToArgvW 로 되읽기) ─────────────────────────────

    [DllImport("shell32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr CommandLineToArgvW(string lpCmdLine, out int pNumArgs);

    [DllImport("kernel32.dll")]
    private static extern IntPtr LocalFree(IntPtr hMem);

    /// <summary>Windows 가 실제로 하는 해석(첫 토큰은 실행 파일 이름이라 빼고).</summary>
    private static string[] ParseLikeWindows(string args)
    {
        IntPtr argv = CommandLineToArgvW("Iljip.exe " + args, out int n);
        Assert.NotEqual(IntPtr.Zero, argv);
        try
        {
            var result = new string[n - 1];
            for (int i = 1; i < n; i++) result[i - 1] = Marshal.PtrToStringUni(Marshal.ReadIntPtr(argv, i * IntPtr.Size))!;
            return result;
        }
        finally
        {
            LocalFree(argv);
        }
    }

    /// <summary>한글·빈칸·끝 역슬래시(빈칸이 있을 때와 없을 때)·따옴표·탭·빈 인자를 싸서 CommandLineToArgvW 로 되읽으면 원래와 같다.</summary>
    [Fact]
    public void 따옴표_싸기는_CommandLineToArgvW로_되읽으면_같다()
    {
        var args = new[]
        {
            "--elevated", "--extract-to",
            @"C:\Users\홍길동\받은 파일\보고서 2026.zip",
            @"C:\Program Files\풀 곳\",
            @"D:\끝\",
            @"C:\a\\",
            @"\\서버\공유 폴더\자료.7z",
            "따옴표\"있음",
            "끝에\\\"",
            "탭\t있음",
            "",
            @"C:\평범한경로\a.zip",
            "빈칸 끝 ",
        };
        string line = CommandLineQuoting.Join(args);
        Assert.Equal(args, ParseLikeWindows(line));
        // 빈칸·따옴표가 없으면 그대로(사람이 로그로 읽기 쉽게)
        Assert.StartsWith(@"--elevated --extract-to ""C:\Users\홍길동\받은 파일\보고서 2026.zip"" ""C:\Program Files\풀 곳\\""", line);
        Assert.Contains(@" D:\끝\ ", line);
    }

    // ───────────────────────────── 인자 만들기(BuildRetry) ─────────────────────────────

    private static readonly FakeMappedDriveResolver NoMap = new();

    /// <summary>동사별 인자: 맨 앞 '--elevated', 경로는 전체 경로, 새 창 표시는 뗌. 다시 해석하면 같은 동사·경로 + Elevated.</summary>
    [Fact]
    public void 동사별_인자()
    {
        const string exe = @"C:\Program Files\일집\Iljip.exe";
        var none = Array.Empty<string>();
        var cases = new (ParsedCommand Cmd, string[] Args)[]
        {
            (new ParsedCommand(CommandVerb.ExtractTo, @"C:\받은\a.zip", @"C:\Program Files\곳", none),
                new[] { "--elevated", "--extract-to", @"C:\받은\a.zip", @"C:\Program Files\곳" }),
            (new ParsedCommand(CommandVerb.ExtractHere, @"C:\Program Files\x\a.zip", null, none) { Archives = new[] { @"C:\Program Files\x\a.zip", @"C:\Program Files\x\b.7z" } },
                new[] { "--elevated", "--extract-here", @"C:\Program Files\x\a.zip", @"C:\Program Files\x\b.7z" }),
            (new ParsedCommand(CommandVerb.ExtractToFolder, @"C:\Program Files\x\a.zip", null, none),
                new[] { "--elevated", "--extract-to-folder", @"C:\Program Files\x\a.zip" }),
            (new ParsedCommand(CommandVerb.ExtractToFolderIn, @"C:\a.zip", @"C:\Program Files\곳", none) { Archives = new[] { @"C:\a.zip", @"C:\b.zip" } },
                new[] { "--elevated", "--extract-to-folder-in", @"C:\Program Files\곳", @"C:\a.zip", @"C:\b.zip" }),
            (new ParsedCommand(CommandVerb.ExtractInto, @"C:\a.zip", @"C:\Program Files\곳", none) { Archives = new[] { @"C:\a.zip", @"C:\b.7z" } },
                new[] { "--elevated", "--extract-into", @"C:\Program Files\곳", @"C:\a.zip", @"C:\b.7z" }),
            (new ParsedCommand(CommandVerb.CompressTo, null, @"C:\Program Files\곳", new[] { @"C:\자료\가.txt", @"C:\자료\폴더" }),
                new[] { "--elevated", "--compress-to", @"C:\Program Files\곳", @"C:\자료\가.txt", @"C:\자료\폴더" }),
            (new ParsedCommand(CommandVerb.AddTo, @"C:\Program Files\x\a.zip", null, new[] { @"C:\자료\가.txt" }),
                new[] { "--elevated", "--add-to", @"C:\Program Files\x\a.zip", @"C:\자료\가.txt" }),
            (ElevationService.ReopenCommand(@"C:\Program Files\x\a.zip"),
                new[] { "--elevated", "--open", @"C:\Program Files\x\a.zip" }),
            (new ParsedCommand(CommandVerb.Open, @"C:\a.zip", null, none) { NewWindow = true },
                new[] { "--elevated", "--open", @"C:\a.zip" }),
        };
        foreach (var (cmd, expected) in cases)
        {
            var plan = ElevationService.BuildRetry(cmd, exe, NoMap);
            Assert.Null(plan.Problem);
            var retry = plan.Retry!;
            Assert.Equal(exe, retry.Exe);
            Assert.Equal(expected, retry.Args);
            // 실제로 넘기는 한 줄을 Windows 처럼 되읽어도 같고, 해석하면 같은 명령 + 관리자 표시
            Assert.Equal(expected, ParseLikeWindows(retry.CommandLine));
            var back = CommandLineParser.Parse(retry.Args.ToArray());
            Assert.True(back.Elevated);
            Assert.False(back.NewWindow);
            Assert.Equal(cmd.Verb, back.Verb);
            // ★ 창 없이 풀기(재사용장치 계약)로 해석되지 않는다
            Assert.False(StartupDecision.IsHeadlessExtract(retry.Args, _ => true));
        }
    }

    /// <summary>상대 경로는 전체 경로로(관리자 일집의 작업 폴더는 대개 System32 라).</summary>
    [Fact]
    public void 상대_경로는_전체_경로로()
    {
        var plan = ElevationService.BuildRetry(new ParsedCommand(CommandVerb.ExtractTo, "a.zip", @"풀 곳\", Array.Empty<string>()), @"C:\Iljip.exe", NoMap);
        Assert.Equal(Path.GetFullPath("a.zip"), plan.Retry!.Args[2]);
        Assert.Equal(Path.GetFullPath(@"풀 곳\"), plan.Retry.Args[3]);
    }

    /// <summary>연결된 네트워크 드라이브(Z:)는 관리자 쪽에 보이지 않아 UNC 로 바꾼다. 못 바꾸면 쉬운 안내(띄우지 않음).</summary>
    [Fact]
    public void 매핑_드라이브는_UNC로_바뀌고_못_바꾸면_안내()
    {
        var map = new FakeMappedDriveResolver();
        map.Map["Z:"] = @"\\나스\공유";
        var plan = ElevationService.BuildRetry(new ParsedCommand(CommandVerb.ExtractTo, @"Z:\받은 파일\a.zip", @"C:\Program Files\곳", Array.Empty<string>()), @"C:\Iljip.exe", map);
        Assert.Equal(new[] { "--elevated", "--extract-to", @"\\나스\공유\받은 파일\a.zip", @"C:\Program Files\곳" }, plan.Retry!.Args);

        var dest = ElevationService.BuildRetry(new ParsedCommand(CommandVerb.CompressTo, null, @"Z:\", new[] { @"Z:\자료\가.txt" }), @"C:\Iljip.exe", map);
        Assert.Equal(new[] { "--elevated", "--compress-to", @"\\나스\공유\", @"\\나스\공유\자료\가.txt" }, dest.Retry!.Args);

        map.Unresolvable.Add("Y:");
        var bad = ElevationService.BuildRetry(new ParsedCommand(CommandVerb.ExtractTo, @"Y:\a.zip", @"C:\곳", Array.Empty<string>()), @"C:\Iljip.exe", map);
        Assert.Null(bad.Retry);
        Assert.Equal(ElevationService.NetworkDriveProblem, bad.Problem);
        Assert.Contains("네트워크 드라이브는 관리자 권한으로 다시 시도할 수 없어요", bad.Problem);

        var noExe = ElevationService.BuildRetry(ElevationService.ReopenCommand(@"C:\a.zip"), null, NoMap);
        Assert.Equal(ElevationService.NoExecutableProblem, noExe.Problem);
    }

    // ───────────────────────────── 권한 오류 판정 ─────────────────────────────

    /// <summary>권한 오류(UnauthorizedAccess·HResult 0x80070005·Win32 5, 안쪽 예외 포함)만 '관리자 권한으로 다시 시도'. 일집 예외(원본 못 읽음 등)·다른 IO 오류는 아님.</summary>
    [Fact]
    public void 권한_오류만_다시_시도를_권한다()
    {
        var denied = new Exception[]
        {
            new UnauthorizedAccessException(@"Access to the path 'C:\Program Files\x' is denied."),
            new IOException("덮어쓰지 못했어요", new UnauthorizedAccessException()) { HResult = unchecked((int)0x80070005) },
            new Win32Exception(5),
            new InvalidOperationException("감쌈", new UnauthorizedAccessException()),
        };
        foreach (var ex in denied)
        {
            Assert.True(ElevationService.IsAccessDenied(ex), ex.GetType().Name);
            Assert.Equal(new[] { ElevationService.RetryActionId }, ElevationService.ActionsFor(ex, canElevate: true));
            Assert.Empty(ElevationService.ActionsFor(ex, canElevate: false));   // '--elevated' 일집·이미 관리자면 없음
        }
        var others = new Exception[]
        {
            new IOException("공간 부족") { HResult = unchecked((int)0x80070070) },
            new IOException("다른 프로그램이 씀") { HResult = unchecked((int)0x80070020) },
            new SourceUnreadableException(@"C:\a.txt", "권한 없음", new UnauthorizedAccessException()),
            new CorruptArchiveException(),
            new WrongPasswordException(),
            new OperationCanceledException(),
            new FileNotFoundException(),
        };
        foreach (var ex in others)
        {
            Assert.False(ElevationService.IsAccessDenied(ex), ex.GetType().Name);
            Assert.Empty(ElevationService.ActionsFor(ex, canElevate: true));
        }
        Assert.False(ElevationService.IsAccessDenied(null));
    }

    // ───────────────────────────── 화면 분기 ─────────────────────────────

    /// <summary>시험 임시 폴더에만: 현재 사용자가 그 폴더 바로 안에 파일·폴더를 만들지 못하게(되돌리기는 Dispose).</summary>
    internal static IDisposable DenyCreate(string dir)
    {
        var info = new DirectoryInfo(dir);
        var user = WindowsIdentity.GetCurrent().User!;
        var rule = new FileSystemAccessRule(user, FileSystemRights.CreateFiles | FileSystemRights.CreateDirectories,
            InheritanceFlags.None, PropagationFlags.None, AccessControlType.Deny);
        var acl = info.GetAccessControl();
        acl.AddAccessRule(rule);
        info.SetAccessControl(acl);
        return new Undo(() =>
        {
            var a = info.GetAccessControl();
            a.RemoveAccessRule(rule);
            info.SetAccessControl(a);
        });
    }

    private sealed class Undo(Action undo) : IDisposable
    {
        public void Dispose() => undo();
    }

    private sealed class Ctx : IDisposable
    {
        public TempDir Tmp { get; }
        public string Work { get; }
        public string Denied { get; }
        public TestServices Services { get; } = new();
        public FakeUserInteraction Ui { get; }
        public MainViewModel Vm { get; }
        private readonly IDisposable _deny;

        public Ctx(string tag, params string?[] passwords)
        {
            Tmp = new TempDir(tag);
            Work = Tmp.Dir("받은 파일");
            Denied = Tmp.Dir("막힌 곳");
            _deny = DenyCreate(Denied);
            Ui = new FakeUserInteraction(passwords);
            Vm = new MainViewModel(Ui, null, Services.Services);
        }

        public void Dispose()
        {
            _deny.Dispose();
            Tmp.Dispose();
        }
    }

    private static ExtractTarget Into(string folder) => new(folder, MakeFolder: false, DeleteArchiveAfter: false, OpenFolderAfter: false);

    /// <summary>
    /// 계약: 권한이 없는 곳에 '다른 곳에 풀기' → 오류 창 대신 ‘…쓸 권한이 없어요… 관리자 권한으로 다시 해 볼까요?’ [관리자 권한으로 다시 시도][닫기],
    /// 고르면 같은 작업 인자('--elevated --extract-to 압축 폴더')로 관리자 일집을 띄운다. ZIP(SharpCompress 길)·7Z(7z.dll 길) 모두.
    /// </summary>
    [Theory]
    [InlineData("zip_default")]
    [InlineData("7z_default")]
    public async Task 권한_없는_곳에_풀면_관리자_권한으로_다시_시도를_묻고_같은_인자로_띄운다(string id)
    {
        using var c = new Ctx("elx");
        string archive = Copy(c.Work, id);
        c.Ui.ExtractTargetAnswer = _ => Into(c.Denied);
        c.Ui.ChoiceAnswers.Enqueue(0);

        await Run(() => c.Vm.OpenArchiveAsync(archive), "열기");
        await Run(() => c.Vm.ExtractCommand.ExecuteAsync(null), "풀기");

        Assert.Empty(c.Ui.Errors);
        var prompt = Assert.Single(c.Ui.ChoicePrompts);
        Assert.Contains("쓸 권한이 없어요", prompt.Message);
        Assert.Contains(ElevationService.RetryQuestion, prompt.Message);
        Assert.Equal(new[] { ElevationService.RetryLabel, "닫기" }, prompt.Choices);
        var (exe, args) = Assert.Single(c.Services.Processes.Elevated);
        Assert.Equal(c.Services.Processes.ExecutablePath, exe);
        Assert.Equal(new[] { "--elevated", "--extract-to", archive, c.Denied }, args);
        Assert.Empty(c.Services.Processes.NewInstances);
        Assert.Empty(Directory.EnumerateFileSystemEntries(c.Denied));   // 아무것도 남지 않음
    }

    /// <summary>[닫기]·창 닫기면 띄우지 않는다(오류 창도 다시 띄우지 않음 — 질문 창이 그 안내).</summary>
    [Fact]
    public async Task 닫기면_띄우지_않는다()
    {
        using var c = new Ctx("elclose");
        string archive = Copy(c.Work, "zip_default");
        c.Ui.ExtractTargetAnswer = _ => Into(c.Denied);
        c.Ui.ChoiceAnswers.Enqueue(1);

        await Run(() => c.Vm.OpenArchiveAsync(archive), "열기");
        await Run(() => c.Vm.ExtractCommand.ExecuteAsync(null), "풀기");

        Assert.Single(c.Ui.ChoicePrompts);
        Assert.Empty(c.Services.Processes.Elevated);
        Assert.Empty(c.Ui.Errors);
        Assert.Equal("오류 발생", c.Vm.StatusText);
    }

    /// <summary>'--elevated' 로 뜬 일집·이미 관리자 권한인 일집은 다시 권하지 않는다(무한 반복 방지) — 예전처럼 오류 창 하나.</summary>
    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task 관리자_일집은_다시_권하지_않는다(bool elevatedFlag, bool processElevated)
    {
        using var c = new Ctx("elno");
        string archive = Copy(c.Work, "zip_default");
        c.Vm.IsElevatedInstance = elevatedFlag;
        c.Services.Processes.IsCurrentProcessElevated = processElevated;
        c.Ui.ExtractTargetAnswer = _ => Into(c.Denied);

        await Run(() => c.Vm.OpenArchiveAsync(archive), "열기");
        await Run(() => c.Vm.ExtractCommand.ExecuteAsync(null), "풀기");

        Assert.Empty(c.Ui.ChoicePrompts);
        Assert.Contains("쓸 권한이 없어요", Assert.Single(c.Ui.Errors));
        Assert.Empty(c.Services.Processes.Elevated);
        Assert.False(c.Vm.CanOfferElevation);
        // 제목의 '(관리자)'는 '--elevated' 표시가 아니라 실제 권한을 따른다(W2-11 검증 지적 — 표시만 붙이고 보통 권한으로 켜면 붙지 않음)
        if (processElevated) Assert.EndsWith(" (관리자)", c.Vm.WindowTitle);
        else Assert.DoesNotContain("(관리자)", c.Vm.WindowTitle);
    }

    /// <summary>UAC 창에서 [아니요](Win32 오류 1223)면 조용히 — 오류 창 없음. 다른 실패는 쉬운 한국어 오류 창.</summary>
    [Fact]
    public async Task UAC_취소는_조용히_다른_실패는_오류_창()
    {
        using var c = new Ctx("eluac");
        string archive = Copy(c.Work, "zip_default");
        c.Ui.ExtractTargetAnswer = _ => Into(c.Denied);
        c.Services.Processes.ThrowOnElevate = new Win32Exception(1223);
        c.Ui.ChoiceAnswers.Enqueue(0);

        await Run(() => c.Vm.OpenArchiveAsync(archive), "열기");
        await Run(() => c.Vm.ExtractCommand.ExecuteAsync(null), "풀기");

        Assert.Single(c.Services.Processes.Elevated);
        Assert.Empty(c.Ui.Errors);
        Assert.Empty(c.Ui.Warnings);

        // 다른 실패(실행 파일이 없어짐 등)는 알린다
        c.Services.Processes.ThrowOnElevate = new Win32Exception(2);
        c.Ui.ChoiceAnswers.Enqueue(0);
        await Run(() => c.Vm.ExtractCommand.ExecuteAsync(null), "풀기 2");
        Assert.StartsWith("관리자 권한으로 다시 시작하지 못했어요.", Assert.Single(c.Ui.Errors));
    }

    /// <summary>★ 암호는 절대 인자에 넣지 않는다 — 암호 압축을 맞는 암호로 연 뒤 권한 오류로 다시 시도해도 인자·한 줄에 암호가 없다(관리자 일집이 다시 묻는다).</summary>
    [Fact]
    public async Task 암호는_인자에_없다()
    {
        using var c = new Ctx("elpw");
        var fx = FixtureCatalog.Get("zip_aes256");
        string archive = Copy(c.Work, "zip_aes256");
        c.Ui.ExtractTargetAnswer = _ => Into(c.Denied);
        c.Ui.ChoiceAnswers.Enqueue(0);

        await Run(() => c.Vm.OpenArchiveAsync(archive, fx.Password), "열기(암호)");
        await Run(() => c.Vm.ExtractCommand.ExecuteAsync(null), "풀기");

        var (_, args) = Assert.Single(c.Services.Processes.Elevated);
        Assert.False(string.IsNullOrEmpty(fx.Password));
        Assert.DoesNotContain(args, a => a.Contains(fx.Password!, StringComparison.Ordinal));
        Assert.DoesNotContain(fx.Password!, CommandLineQuoting.Join(args), StringComparison.Ordinal);
        Assert.Empty(c.Ui.PasswordPrompts);
    }

    /// <summary>고른 항목만 풀기는 명령줄로 나타낼 수 없어 '관리자 권한으로 일집 다시 열기'(인자는 압축 파일 경로).</summary>
    [Fact]
    public async Task 고른_항목만_풀기는_일집_다시_열기()
    {
        using var c = new Ctx("elsel");
        string archive = Copy(c.Work, "zip_default");
        c.Ui.ExtractTargetAnswer = _ => Into(c.Denied);
        c.Ui.ChoiceAnswers.Enqueue(0);

        await Run(() => c.Vm.OpenArchiveAsync(archive), "열기");
        await Run(() => c.Vm.ExtractSelectedAsync(c.Vm.VisibleEntries.Take(1).ToList()), "고른 것 풀기");

        var prompt = Assert.Single(c.Ui.ChoicePrompts);
        Assert.Contains(ElevationService.ReopenQuestion, prompt.Message);
        Assert.Equal(new[] { ElevationService.ReopenLabel, "닫기" }, prompt.Choices);
        Assert.Equal(new[] { "--elevated", "--open", archive }, Assert.Single(c.Services.Processes.Elevated).Args);
    }

    /// <summary>탐색기 우클릭 '"이름"\ 에 풀기'(메인 창 없이) — 압축 파일이 있는 폴더에 쓸 권한이 없으면 같은 동사로 다시 시도.</summary>
    [Theory]
    [InlineData("--extract-to-folder")]
    [InlineData("--extract-here")]
    public async Task 우클릭_풀기도_같은_동사로_다시_시도(string verb)
    {
        using var c = new Ctx("elshell");
        // 압축 파일을 막힌 폴더 안에 두려면 막기 전에 넣어야 한다 — 다른 폴더를 새로 막는다
        string folder = c.Tmp.Dir("읽기만 되는 곳");
        string archive = Copy(folder, "zip_default");
        using var deny = DenyCreate(folder);
        c.Ui.ChoiceAnswers.Enqueue(0);

        await Run(() => c.Vm.ProcessShellCommandAsync(new[] { verb, archive }), "우클릭 풀기");

        Assert.Empty(c.Ui.Errors);
        Assert.Contains(ElevationService.RetryQuestion, Assert.Single(c.Ui.ChoicePrompts).Message);
        Assert.Equal(new[] { "--elevated", verb, archive }, Assert.Single(c.Services.Processes.Elevated).Args);
        Assert.Equal(new[] { Path.GetFileName(archive) }, Directory.EnumerateFileSystemEntries(folder).Select(Path.GetFileName));
    }

    /// <summary>여러 개 풀기(우클릭으로 둘 이상)에서 권한이 없어 못 푼 것만 요약 뒤에 한 번 묻는다.</summary>
    [Fact]
    public async Task 여러_개_풀기는_요약_뒤에_못_푼_것만_묻는다()
    {
        using var c = new Ctx("elbatch");
        string folder = c.Tmp.Dir("막힌 받은 곳");
        string blocked = Copy(folder, "zip_default", "막힘.zip");
        string ok = Copy(c.Work, "zip_default", "됨.zip");
        using var deny = DenyCreate(folder);
        c.Ui.ChoiceAnswers.Enqueue(0);

        await Run(() => c.Vm.ProcessShellCommandAsync(new[] { "--extract-to-folder", blocked, ok }), "우클릭 여러 개");

        var summary = Assert.Single(c.Ui.Summaries);
        Assert.Equal(1, summary.Succeeded);
        var prompt = Assert.Single(c.Ui.ChoicePrompts);
        Assert.Contains("압축 파일 1개는 풀 곳에 쓸 권한이 없어", prompt.Message);
        Assert.Equal(new[] { "--elevated", "--extract-to-folder", blocked }, Assert.Single(c.Services.Processes.Elevated).Args);
        Assert.True(Directory.Exists(Path.Combine(c.Work, "됨")));
    }

    /// <summary>
    /// 만들 곳에 쓸 권한이 없는 압축(읽기 전용·Program Files 등) → 같은 원본·같은 만들 곳으로 '--compress-to'(관리자 일집에서 압축 창부터 — 암호·옵션은 다시).
    /// 아무 파일도 남지 않는다.
    /// </summary>
    [Fact]
    public async Task 권한_없는_곳에_압축하면_compress_to로_다시_시도()
    {
        using var c = new Ctx("elcomp");
        string src = Path.Combine(c.Work, "보고서.txt");
        File.WriteAllText(src, "내용");
        c.Ui.CompressionRequestAnswer = r => new CompressionRequest
        {
            Sources = r.Sources,
            OutputFolder = c.Denied,
            BaseName = "보고서",
            Format = CompressionFormat.Zip,
            Options = new CompressionOptions { Password = "비밀-1234" },
        };
        c.Ui.ChoiceAnswers.Enqueue(0);

        c.Vm.AddToStaging(new[] { src });
        await Run(() => c.Vm.CompressCommand.ExecuteAsync(null), "압축");

        Assert.Empty(c.Ui.Errors);
        Assert.Contains(ElevationService.RetryQuestion, Assert.Single(c.Ui.ChoicePrompts).Message);
        var args = Assert.Single(c.Services.Processes.Elevated).Args;
        Assert.Equal(new[] { "--elevated", "--compress-to", c.Denied, src }, args);
        Assert.DoesNotContain(args, a => a.Contains("비밀-1234", StringComparison.Ordinal));
        Assert.Empty(Directory.EnumerateFileSystemEntries(c.Denied));
    }

    /// <summary>
    /// '--compress-to 만들폴더 원본…'(관리자 일집이 받는 명령): 압축 창의 '만들 곳' 처음 값이 그 폴더(설정이 '정해 둔 폴더'여도 — 사본만 바꿈, 설정은 그대로).
    /// </summary>
    [Fact]
    public async Task compress_to는_만들_곳을_그_폴더로_압축_창을_연다()
    {
        using var t = new TempDir("ctoform");
        string src = Path.Combine(t.Dir("자료"), "가.txt");
        File.WriteAllText(src, "가");
        string fixedFolder = t.Dir("정해 둔 곳");
        string dest = t.Dir("만들 곳");
        var svc = TestServices.WithCompress(s =>
        {
            s.FolderChoice = CompressFolderChoice.FixedFolder;
            s.FixedFolder = fixedFolder;
        });
        var ui = new FakeUserInteraction();
        ui.CompressionRequestAnswer = _ => null;   // 창을 보기만 하고 취소
        var vm = new MainViewModel(ui, null, svc.Services);

        await Run(() => vm.ProcessCommandLineAsync(new[] { "--elevated", "--compress-to", dest, src }), "--compress-to");

        var req = Assert.Single(ui.CompressionPrompts);
        Assert.Equal(dest, req.SuggestedFolder);
        Assert.Equal(dest, req.Defaults.InitialFolder(req.SuggestedFolder));
        Assert.Equal(new[] { src }, req.Sources);
        Assert.Equal(CompressFolderChoice.FixedFolder, vm.Settings.Compress.FolderChoice);   // 설정은 그대로
        Assert.Equal(0, svc.Settings.SaveCount);
    }

    /// <summary>네트워크 드라이브 위 압축을 다시 시도하면 UNC 로 바꿔 넘기고, 못 바꾸면 쉬운 안내(띄우지 않음).</summary>
    [Fact]
    public void 화면도_매핑_드라이브는_UNC로_못_바꾸면_안내()
    {
        var svc = new TestServices();
        svc.MappedDrives.Map["Z:"] = @"\\나스\공유";
        svc.MappedDrives.Unresolvable.Add("Y:");
        var ui = new FakeUserInteraction();
        var vm = new MainViewModel(ui, null, svc.Services);

        Assert.True(vm.StartElevated(new ParsedCommand(CommandVerb.ExtractToFolder, @"Z:\받은 파일\a.zip", null, Array.Empty<string>())));
        Assert.Equal(new[] { "--elevated", "--extract-to-folder", @"\\나스\공유\받은 파일\a.zip" }, Assert.Single(svc.Processes.Elevated).Args);

        Assert.False(vm.StartElevated(new ParsedCommand(CommandVerb.ExtractToFolder, @"Y:\a.zip", null, Array.Empty<string>())));
        Assert.Single(svc.Processes.Elevated);
        Assert.Equal(ElevationService.NetworkDriveProblem, Assert.Single(ui.Errors));
    }

    // ───────────────────────────── W2-11 검증 지적 ─────────────────────────────

    /// <summary>
    /// 검증 지적: 풀기 창에서 '폴더 만들기'를 끄고 압축 여럿을 막힌 곳에 바로 풀면, 다시 시도가 첫 압축만 다시 여는 게 아니라
    /// 모두를 같은 곳에 바로 다시 푸는 '--elevated --extract-into 폴더 압축…'(질문도 '관리자 권한으로 다시 시도'). 막힌 곳에는 아무것도 남지 않는다.
    /// </summary>
    [Fact]
    public async Task 여러_압축을_한_폴더에_바로_풀다_막히면_모두를_extract_into로_다시()
    {
        using var c = new Ctx("elinto");
        string a = Copy(c.Work, "zip_default", "가.zip");
        string b = Copy(c.Work, "7z_default", "나.7z");
        c.Ui.ExtractTargetAnswer = _ => Into(c.Denied);
        c.Ui.ChoiceAnswers.Enqueue(0);

        await Run(() => c.Vm.ProcessCommandLineAsync(new[] { "--extract", a, b }), "여럿을 막힌 곳에 바로");

        Assert.Single(c.Ui.Summaries);
        var prompt = Assert.Single(c.Ui.ChoicePrompts);
        Assert.Contains("압축 파일 2개는 풀 곳에 쓸 권한이 없어", prompt.Message);
        Assert.Contains(ElevationService.RetryQuestion, prompt.Message);
        Assert.Equal(new[] { ElevationService.RetryLabel, "닫기" }, prompt.Choices);
        var args = Assert.Single(c.Services.Processes.Elevated).Args;
        Assert.Equal(new[] { "--elevated", "--extract-into", c.Denied, a, b }, args);
        Assert.False(StartupDecision.IsHeadlessExtract(args, _ => true));
        Assert.Empty(Directory.EnumerateFileSystemEntries(c.Denied));
    }

    /// <summary>관리자 일집이 받는 '--extract-into 폴더 압축…': 메인 창 없이(보던 것을 바꾸지 않고) 모두를 그 폴더에 바로 푼다.</summary>
    [Fact]
    public async Task extract_into는_모두를_그_폴더에_바로_푼다()
    {
        using var t = new TempDir("elintook");
        string src = t.Dir("받은 파일");
        string a = Path.Combine(src, "가.zip");
        RawZip.Write(a, RawZip.Utf8("가.txt", "가 내용"));
        string b = Path.Combine(src, "나.zip");
        RawZip.Write(b, RawZip.Utf8("폴더/나.txt", "나 내용"));
        string dest = t.Dir("풀 곳");
        var svc = new TestServices();
        var ui = new FakeUserInteraction();
        var vm = new MainViewModel(ui, null, svc.Services);

        await Run(() => vm.ProcessShellCommandAsync(new[] { "--elevated", "--extract-into", dest, a, b }), "--extract-into");

        Assert.Empty(ui.Errors);
        Assert.Equal("가 내용", File.ReadAllText(Path.Combine(dest, "가.txt")));
        Assert.Equal("나 내용", File.ReadAllText(Path.Combine(dest, "폴더", "나.txt")));
        Assert.Equal(new[] { "가.txt", "폴더" }, Directory.EnumerateFileSystemEntries(dest).Select(Path.GetFileName).OrderBy(n => n, StringComparer.Ordinal));
        Assert.Null(vm.CurrentArchivePath);
    }

    /// <summary>
    /// 검증 지적: 덮어쓸 파일이 '읽기 전용'이라 못 바꾼 것은 관리자 권한으로 다시 해도 같다 — '관리자 권한으로 다시 시도'를 묻지 않고
    /// '…읽기 전용이라 덮어쓰지 못했어요… 읽기 전용을 끄고' 오류 창 하나. 원래 파일은 그대로, 숨김 임시 파일이 남지 않는다. ZIP(직접 쓰는 길)·7Z(7z.dll 길).
    /// </summary>
    [Theory]
    [InlineData("zip_default")]
    [InlineData("7z_default")]
    public async Task 읽기_전용_파일을_덮어쓰지_못하면_관리자_권한을_권하지_않는다(string id)
    {
        using var c = new Ctx("elro");
        string archive = Copy(c.Work, id);
        string dest = c.Tmp.Dir("풀 곳");
        var first = FixtureCatalog.Get(id).Expected.First(e => !e.IsDir);
        string existing = Path.Combine(dest, first.Path.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(existing)!);
        File.WriteAllText(existing, "원래 내용");
        File.SetAttributes(existing, FileAttributes.ReadOnly);
        try
        {
            c.Vm.Settings.Extract.OnCollision = CollisionPolicy.Overwrite;
            c.Ui.ExtractTargetAnswer = _ => Into(dest);

            await Run(() => c.Vm.OpenArchiveAsync(archive), "열기");
            await Run(() => c.Vm.ExtractCommand.ExecuteAsync(null), "풀기(덮어쓰기)");

            Assert.Empty(c.Ui.ChoicePrompts);
            Assert.Empty(c.Services.Processes.Elevated);
            string error = Assert.Single(c.Ui.Errors);
            Assert.Contains("읽기 전용이라 덮어쓰지 못했어요", error);
            Assert.Contains("'읽기 전용'을 끄고", error);
            Assert.DoesNotContain("관리자 권한", error);
            Assert.Equal("원래 내용", File.ReadAllText(existing));
            Assert.DoesNotContain(Directory.EnumerateFiles(dest, "*", SearchOption.AllDirectories),
                f => Path.GetFileName(f).StartsWith(OutputFiles.TempPrefix, StringComparison.Ordinal));
        }
        finally
        {
            File.SetAttributes(existing, FileAttributes.Normal);
        }
    }

    /// <summary>
    /// 읽기 전용 파일을 못 바꾼 예외(안쪽에 있어도)는 권한 오류가 아니다. 덮어쓰기(<see cref="OutputFiles.ReplaceExisting"/>)는 읽기 전용이면 기다리지 않고
    /// 그 예외 — 원래 파일·새 파일 모두 그대로.
    /// </summary>
    [Fact]
    public void 읽기_전용은_권한_오류가_아니다()
    {
        var ro = new ReadOnlyTargetException(@"C:\풀 곳\견적.xlsx", new UnauthorizedAccessException());
        Assert.False(ElevationService.IsAccessDenied(ro));
        Assert.False(ElevationService.IsAccessDenied(new IOException("감쌈", ro)));
        Assert.Empty(ElevationService.ActionsFor(ro, canElevate: true));
        string message = ErrorMessages.ToUserMessage(ro);
        Assert.Contains("견적.xlsx", message);
        Assert.Contains("읽기 전용이라 덮어쓰지 못했어요", message);
        Assert.DoesNotContain("다른 프로그램", message);

        using var t = new TempDir("elrepl");
        string target = Path.Combine(t.Root, "있던.txt");
        File.WriteAllText(target, "원래");
        string source = Path.Combine(t.Root, "새.txt");
        File.WriteAllText(source, "새");
        File.SetAttributes(target, FileAttributes.ReadOnly);
        try
        {
            var ex = Assert.Throws<ReadOnlyTargetException>(() => OutputFiles.ReplaceExisting(source, target));
            Assert.Equal(target, ex.TargetPath);
            Assert.Equal("원래", File.ReadAllText(target));
            Assert.True(File.Exists(source));
        }
        finally
        {
            File.SetAttributes(target, FileAttributes.Normal);
        }
    }

    /// <summary>
    /// 검증 지적: 쓰려던 곳이 네트워크(공유 폴더·NAS — UNC)면 관리자 권한으로 다시 해도 서버 쪽 권한은 같아 권하지 않는다(질문 없이 부르는 쪽의 오류 창).
    /// 내 PC 의 폴더면 예전처럼 묻는다. 경로 모양만 보고 디스크·네트워크에 가지 않는다.
    /// </summary>
    [Fact]
    public void 쓰려던_곳이_네트워크면_관리자_권한을_권하지_않는다()
    {
        Assert.False(ElevationService.CanHelpWriteTo(@"\\나스\공유\받은 곳"));
        Assert.False(ElevationService.CanHelpWriteTo(@"\\?\UNC\나스\공유\a.zip"));
        Assert.True(ElevationService.CanHelpWriteTo(@"C:\Program Files\곳"));
        Assert.True(ElevationService.CanHelpWriteTo(null));

        var svc = new TestServices();
        var ui = new FakeUserInteraction();
        var vm = new MainViewModel(ui, null, svc.Services);
        var toShare = new ParsedCommand(CommandVerb.ExtractTo, @"C:\받은\a.zip", @"\\나스\공유\받은 곳", Array.Empty<string>());
        Assert.False(vm.OfferElevatedRetry(new UnauthorizedAccessException(), "풀지 못했어요", toShare, @"C:\받은\a.zip", toShare.Dest));
        Assert.Empty(ui.ChoicePrompts);

        ui.ChoiceAnswers.Enqueue(1);
        var toLocal = toShare with { Dest = @"C:\Program Files\곳" };
        Assert.True(vm.OfferElevatedRetry(new UnauthorizedAccessException(), "풀지 못했어요", toLocal, @"C:\받은\a.zip", toLocal.Dest));
        Assert.Contains(ElevationService.RetryQuestion, Assert.Single(ui.ChoicePrompts).Message);
        Assert.Empty(svc.Processes.Elevated);   // [닫기]
    }

    /// <summary>
    /// 검증 지적(시험 없던 분기): 탐색기 'zip에 추가'(--add-to)로 넣다가 압축 파일이 든 폴더에 임시 파일을 못 만들면(쓸 권한 없음)
    /// '관리자 권한으로 다시 시도' — 같은 명령 '--elevated --add-to 압축 넣을것…'. 원래 압축은 바이트 그대로, 임시 파일이 남지 않는다.
    /// </summary>
    [Fact]
    public async Task add_to는_권한이_없으면_같은_명령으로_다시_시도()
    {
        using var c = new Ctx("eladd");
        string folder = c.Tmp.Dir("고칠 곳");
        string archive = Copy(folder, "zip_default", "a.zip");
        byte[] before = File.ReadAllBytes(archive);
        string add = Path.Combine(c.Work, "넣을.txt");
        File.WriteAllText(add, "넣을 내용");
        using var deny = DenyCreate(folder);
        c.Ui.ChoiceAnswers.Enqueue(0);

        await Run(() => c.Vm.ProcessShellCommandAsync(new[] { "--add-to", archive, add }), "--add-to");

        Assert.Empty(c.Ui.Errors);
        Assert.Empty(c.Ui.Infos);   // '넣었어요' 안내 없음
        var prompt = Assert.Single(c.Ui.ChoicePrompts);
        Assert.StartsWith("압축 파일을 고치지 못했어요", prompt.Message);
        Assert.Contains("쓸 권한이 없어요", prompt.Message);
        Assert.Contains(ElevationService.RetryQuestion, prompt.Message);
        Assert.Equal(new[] { ElevationService.RetryLabel, "닫기" }, prompt.Choices);
        Assert.Equal(new[] { "--elevated", "--add-to", archive, add }, Assert.Single(c.Services.Processes.Elevated).Args);
        Assert.Equal(before, File.ReadAllBytes(archive));
        Assert.Equal(new[] { "a.zip" }, Directory.EnumerateFileSystemEntries(folder).Select(Path.GetFileName));
    }

    /// <summary>넣기 밖의 고치기(새 폴더·지우기·이름 바꾸기)는 명령줄로 나타낼 수 없어 '관리자 권한으로 일집 다시 열기'(--open 압축).</summary>
    [Fact]
    public async Task 넣기_밖의_고치기는_일집_다시_열기()
    {
        using var c = new Ctx("eledit");
        string folder = c.Tmp.Dir("고칠 곳");
        string archive = Copy(folder, "zip_default", "a.zip");
        byte[] before = File.ReadAllBytes(archive);
        using var deny = DenyCreate(folder);
        c.Ui.ChoiceAnswers.Enqueue(0);

        await Run(() => c.Vm.OpenArchiveAsync(archive), "열기");
        bool made = true;
        await Run(async () => made = await c.Vm.CreateFolderAsync("새 폴더"), "새 폴더");

        Assert.False(made);
        Assert.Empty(c.Ui.Errors);
        var prompt = Assert.Single(c.Ui.ChoicePrompts);
        Assert.Contains(ElevationService.ReopenQuestion, prompt.Message);
        Assert.Equal(new[] { ElevationService.ReopenLabel, "닫기" }, prompt.Choices);
        Assert.Equal(new[] { "--elevated", "--open", archive }, Assert.Single(c.Services.Processes.Elevated).Args);
        Assert.Equal(before, File.ReadAllBytes(archive));
    }
}
