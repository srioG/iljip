using System.Buffers.Binary;
using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using Iljip.Models;
using Iljip.Services;
using Iljip.Tests.TestSupport;

namespace Iljip.Tests.Contract;

/// <summary>
/// [W2-10, I3-1] 자동 풀림 exe 만들기 — 일집 자체 스텁(Iljip.SfxStub, .NET Framework 4.6.2) + ZIP + 꼬리 'ILJIPSFX'.
/// 방금 빌드한 스텁으로 만든 exe 를 <b>무인 모드(/s)로만</b> 실제로 돌려 본다(CreateNoWindow·UseShellExecute=false — 창이 뜨는 화면 모드는 실행하지 않음,
/// 도는 동안 MainWindowHandle 이 늘 0 인지 본다). 화면 모드(풀 위치 창·진행·한글 표시)는 MANUAL_CHECKLIST 16절 '실기기 확인 필요'.
/// 계약: 한글 이름·빈 파일·빈 폴더·하위 폴더가 원본과 같게 풀린다 / 암호(AES-256·ZipCrypto, 한글 암호 포함)가 맞으면 풀리고 틀리거나 없으면 종료 코드 1·대상에 아무것도 없음 /
/// 끝이 잘리거나 내용이 깨지면 1·아무것도 남지 않음 / 풀 폴더 밖을 가리키는 이름이 하나라도 있으면 1·아무것도 풀지 않음 / 일집 안에서도 열고 푼다.
/// </summary>
public class SfxCreateTests
{
    // ───────────── 도우미 ─────────────

    /// <summary>스텁이 출력 폴더에 있다(빌드가 sfx\Iljip.SfxStub.exe 를 복사함 — 없으면 만들기 자체가 안 됨).</summary>
    private static string Stub
    {
        get
        {
            Assert.True(File.Exists(SfxBuilder.StubPath), "빌드 출력에 자동 풀림 스텁이 없음: " + SfxBuilder.StubPath);
            return SfxBuilder.StubPath;
        }
    }

    private static async Task<string> MakeSfx(TempDir t, IEnumerable<string> sources, string name = "자료 모음.exe", CompressionOptions? options = null)
    {
        string exe = t.P(name);
        await SfxBuilder.CreateAsync(sources, exe, options);
        Assert.True(File.Exists(exe), "자동 풀림 exe 가 안 생김");
        return exe;
    }

    /// <summary>
    /// 만든 exe 를 무인 모드로 실행한다(창 없이 — 60초 제한). 도는 동안 50ms 마다 창(MainWindowHandle)이 생겼는지 본다.
    /// 표준 오류는 UTF-8 한 줄(스텁 약속).
    /// </summary>
    internal static SfxRun RunSilent(string exe, params string[] args)
    {
        using var running = StartSilent(exe, args);
        return running.Finish();
    }

    /// <summary>만든 exe 를 무인 모드로 시작만 한다(창 없이). 끝나기는 <see cref="SfxRunning.Finish"/> — 도는 동안 창이 생기는지 계속 본다.</summary>
    internal static SfxRunning StartSilent(string exe, params string[] args)
    {
        var psi = new ProcessStartInfo(exe)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            StandardErrorEncoding = new UTF8Encoding(false),
            StandardOutputEncoding = new UTF8Encoding(false),
            WorkingDirectory = Path.GetDirectoryName(exe)!,
        };
        foreach (var a in args) psi.ArgumentList.Add(a);
        return new SfxRunning(exe, Process.Start(psi)!);
    }

    /// <summary>무인 모드로 도는 자동 풀림 exe 하나(시작부터 60초 제한 — 넘으면 끄고 실패).</summary>
    internal sealed class SfxRunning : IDisposable
    {
        private readonly string _exe;
        private readonly Task<string> _err, _out;
        private readonly Stopwatch _sw = Stopwatch.StartNew();
        private bool _window;

        public SfxRunning(string exe, Process p)
        {
            _exe = exe;
            Process = p;
            _err = p.StandardError.ReadToEndAsync();
            _out = p.StandardOutput.ReadToEndAsync();
        }

        public Process Process { get; }

        /// <summary>창이 생겼는지 한 번 본다(끝났으면 그냥 넘어감).</summary>
        public void CheckWindow()
        {
            try
            {
                Process.Refresh();
                if (!Process.HasExited && Process.MainWindowHandle != IntPtr.Zero) _window = true;
            }
            catch (InvalidOperationException)
            {
                // 그 사이에 끝남
            }
        }

        /// <summary>끝나기를 기다린다(50ms 마다 창 확인, 시작부터 60초 제한).</summary>
        public SfxRun Finish()
        {
            while (!Process.WaitForExit(50))
            {
                CheckWindow();
                if (_sw.Elapsed > TimeSpan.FromSeconds(60))
                {
                    Kill();
                    Assert.Fail("자동 풀림 exe 가 60초 안에 끝나지 않음(창이 떴을 수 있음): " + _exe);
                }
            }
            Process.WaitForExit();
            return new SfxRun(Process.ExitCode, _err.Result.Trim(), _out.Result.Trim(), _window, _sw.Elapsed);
        }

        /// <summary>강제로 끝낸다(작업 관리자 '프로세스 끝내기'와 같음 — 정리할 틈 없음).</summary>
        public void Kill()
        {
            try { Process.Kill(entireProcessTree: true); } catch { /* 이미 끝남 */ }
            Process.WaitForExit();
        }

        public void Dispose()
        {
            if (!Process.HasExited) Kill();
            Process.Dispose();
        }
    }

    internal sealed record SfxRun(int ExitCode, string Error, string Output, bool WindowSeen, TimeSpan Elapsed)
    {
        public void AssertNoWindow() => Assert.False(WindowSeen, "무인 모드인데 창이 떴음");
    }

    /// <summary>임의의 ZIP 바이트로 자동 풀림 exe 를 직접 만든다(스텁 + ZIP + 꼬리 — 경로 공격 등 일집이 만들지 않는 ZIP 을 넣을 때).</summary>
    private static string ComposeRaw(TempDir t, string zipPath, string name = "raw.exe")
    {
        string exe = t.P(name);
        SfxBuilder.Compose(SfxBuilder.ReadStub(Stub), zipPath, exe, CancellationToken.None);
        return exe;
    }

    private static void AssertNothingIn(string dest, string context)
    {
        Assert.True(!Directory.Exists(dest) || !Directory.EnumerateFileSystemEntries(dest, "*", SearchOption.AllDirectories).Any(),
            $"{context}: 실패했는데 대상에 무엇이 남음: " + (Directory.Exists(dest) ? string.Join(", ", TreeAssert.Snapshot(dest).Keys) : string.Empty));
    }

    // ───────────── 만들고 무인으로 풀기 ─────────────

    /// <summary>
    /// 계약: 한글 이름·공백·기호·빈 파일·빈 폴더·깊은 폴더·바이너리가 든 폴더로 만든 자동 풀림 exe 를 '/s /d:폴더' 로 실행하면 종료 코드 0, 창 없음,
    /// 원본과 똑같은 트리(임시 폴더 '.일집-임시-…' 도 남지 않음). 파일 모양: 'MZ' 로 시작, 꼬리 'ILJIPSFX' 의 데이터 시작 = 스텁 길이.
    /// </summary>
    [SfxStubFact]
    public async Task 만든_자동풀림_exe를_무인으로_풀면_원본과_같다()
    {
        using var t = new TempDir("sfxmk");
        string root = SampleTree.Create(t.Dir("src"));
        string exe = await MakeSfx(t, new[] { root });

        byte[] head = new byte[2];
        using (var fs = File.OpenRead(exe))
        {
            fs.ReadExactly(head);
            Assert.True(SfxBuilder.TryReadTrailer(fs, out long start, out long length));
            Assert.Equal(new FileInfo(Stub).Length, start);
            Assert.Equal(fs.Length - SfxBuilder.TrailerLength - start, length);
        }
        Assert.Equal("MZ", Encoding.ASCII.GetString(head));

        string dest = t.P("풀린 곳");
        var run = RunSilent(exe, "/s", "/d:" + dest);
        Assert.True(run.ExitCode == 0, $"종료 코드 {run.ExitCode}: {run.Error}");
        run.AssertNoWindow();
        Assert.Equal(string.Empty, run.Error);
        TreeAssert.ExtractedTreeEquals(dest, SampleTree.ExpectedOf(root), context: "자동 풀림");
        // 시각도 원본대로(ZIP 의 DOS 시각 — 2초 단위)
        Assert.Equal(SampleTree.Mtime, File.GetLastWriteTime(Path.Combine(dest, SampleTree.RootName, "한글 파일.txt")));
    }

    /// <summary>계약: 암호가 맞으면 풀린다 — AES-256·ZipCrypto, 영문·한글 암호(일집은 ZipCrypto 한글 암호를 CP949 로, AES 는 UTF-8 로 쓴다 — 스텁은 둘 다 알아봄).</summary>
    [SfxStubTheory]
    [InlineData(ZipEncryption.Aes256, "abcd1234")]
    [InlineData(ZipEncryption.Aes256, "한글암호2026")]
    [InlineData(ZipEncryption.ZipCrypto, "abcd1234")]
    [InlineData(ZipEncryption.ZipCrypto, "한글암호2026")]
    public async Task 암호가_맞으면_풀리고_원본과_같다(ZipEncryption encryption, string password)
    {
        using var t = new TempDir("sfxpw");
        string root = SampleTree.Create(t.Dir("src"));
        string exe = await MakeSfx(t, new[] { root }, options: new CompressionOptions { Password = password, Encryption = encryption });

        string dest = t.P("out");
        var run = RunSilent(exe, "/s", "/d:" + dest, "/p:" + password);
        Assert.True(run.ExitCode == 0, $"종료 코드 {run.ExitCode}: {run.Error}");
        run.AssertNoWindow();
        TreeAssert.ExtractedTreeEquals(dest, SampleTree.ExpectedOf(root), context: $"{encryption} '{password}'");
    }

    /// <summary>
    /// 계약: 틀린 암호면 종료 코드 1, 쉬운 한국어 '암호가 맞지 않아요', 대상에는 아무것도 없다(폴더도 만들지 않음).
    /// 암호를 주지 않으면(무인 모드는 묻지 않음) 1 과 '/p:암호' 안내.
    /// </summary>
    [SfxStubTheory]
    [InlineData(ZipEncryption.Aes256)]
    [InlineData(ZipEncryption.ZipCrypto)]
    public async Task 암호가_틀리거나_없으면_1이고_대상에_아무것도_없다(ZipEncryption encryption)
    {
        using var t = new TempDir("sfxpwx");
        string root = SampleTree.Create(t.Dir("src"));
        string exe = await MakeSfx(t, new[] { root }, options: new CompressionOptions { Password = "맞는암호123", Encryption = encryption });

        string wrongDest = t.P("틀림");
        var wrong = RunSilent(exe, "/s", "/d:" + wrongDest, "/p:틀린암호123");
        Assert.Equal(1, wrong.ExitCode);
        wrong.AssertNoWindow();
        Assert.Contains("암호가 맞지 않아요", wrong.Error);
        Assert.False(Directory.Exists(wrongDest), "틀린 암호인데 대상 폴더가 생김");

        string noneDest = t.P("없음");
        var none = RunSilent(exe, "/s", "/d:" + noneDest);
        Assert.Equal(1, none.ExitCode);
        none.AssertNoWindow();
        Assert.Contains("/p:", none.Error);
        Assert.Contains("암호", none.Error);
        Assert.False(Directory.Exists(noneDest), "암호 없이 실행했는데 대상 폴더가 생김");

        // 이미 있던 대상 폴더면 그 안에 아무것도 남지 않는다
        string existing = t.Dir("있던 폴더");
        var wrong2 = RunSilent(exe, "/s", "/d:" + existing, "/p:x");
        Assert.Equal(1, wrong2.ExitCode);
        AssertNothingIn(existing, "틀린 암호(있던 폴더)");
    }

    /// <summary>계약: 끝이 잘린 자동 풀림 exe(끝 1KB 를 뗌 — 덜 받아진 첨부)는 종료 코드 1·'잘렸어요'·대상에 아무것도 없다.</summary>
    [SfxStubFact]
    public async Task 끝이_잘린_자동풀림은_1이고_아무것도_남지_않는다()
    {
        using var t = new TempDir("sfxcut");
        string root = SampleTree.Create(t.Dir("src"));
        string exe = await MakeSfx(t, new[] { root });
        byte[] all = File.ReadAllBytes(exe);
        string cut = t.P("잘림.exe");
        File.WriteAllBytes(cut, all[..^1024]);

        string dest = t.P("out");
        var run = RunSilent(cut, "/s", "/d:" + dest);
        Assert.Equal(1, run.ExitCode);
        run.AssertNoWindow();
        Assert.Contains("잘렸어요", run.Error);
        AssertNothingIn(dest, "끝이 잘림");
    }

    /// <summary>
    /// 계약: 파일 내용이 깨진 자동 풀림(압축 데이터 한가운데 1바이트를 바꿈)은 종료 코드 1, 대상에 반쪽 파일·임시 폴더가 남지 않는다.
    /// 암호 없음(CRC-32)·AES(인증값 HMAC — AE-2 는 CRC 칸이 0) 둘 다.
    /// </summary>
    [SfxStubTheory]
    [InlineData(null)]
    [InlineData("암호abc123")]
    public async Task 내용이_깨지면_1이고_반쪽_파일이_남지_않는다(string? password)
    {
        using var t = new TempDir("sfxbad");
        string src = t.Dir("src");
        var rnd = new Random(7);
        var big = new byte[300_000];
        rnd.NextBytes(big);
        File.WriteAllBytes(Path.Combine(src, "앞 파일.bin"), big);
        File.WriteAllText(Path.Combine(src, "뒤 파일.txt"), string.Concat(Enumerable.Repeat("한글 내용 ", 5000)));
        string exe = await MakeSfx(t, new[] { Path.Combine(src, "앞 파일.bin"), Path.Combine(src, "뒤 파일.txt") },
            options: new CompressionOptions { Password = password, Level = CompressionLevelOption.Store });

        byte[] b = File.ReadAllBytes(exe);
        long start = new FileInfo(Stub).Length;
        int at = (int)(start + 150_000);   // 첫 파일(저장만) 데이터 한가운데
        b[at] ^= 0x5A;
        string bad = t.P("깨짐.exe");
        File.WriteAllBytes(bad, b);

        string dest = t.P("out");
        var run = password is null ? RunSilent(bad, "/s", "/d:" + dest) : RunSilent(bad, "/s", "/d:" + dest, "/p:" + password);
        Assert.Equal(1, run.ExitCode);
        run.AssertNoWindow();
        Assert.Contains("앞 파일.bin", run.Error);
        AssertNothingIn(dest, "내용 깨짐");
    }

    public static TheoryData<string> SlipNames
    {
        get
        {
            var d = new TheoryData<string>();
            foreach (var n in new[]
                     {
                         "../evil.txt", "..\\evil.txt", "safe/../../evil.txt", "ABS", "../dest_evil.txt", "../dest2/evil.txt", "a/../../dest_evil.txt",
                         // 뿌리 상대('/evil.txt')는 넣지 않는다 — 방어가 깨지면 드라이브 뿌리에 쓰게 됨(ZipSlipTests 와 같은 원칙, 스텁은 '/' 로 시작하는 이름도 거절)
                         "a.txt:evil", "evil.txt.", "con.txt", "C:evil.txt", "sub/./x.txt",
                     })
                d.Add(n);
            return d;
        }
    }

    /// <summary>
    /// 계약: 풀 폴더 밖을 가리키는 이름(zip-slip — '../', 절대 경로, 드라이브, 형제 접두사)·Windows 특수 이름(대체 데이터 스트림 ':', 끝 점, 장치 이름 CON)이
    /// 하나라도 든 데이터는 종료 코드 1·'안전하지 않은 경로', 대상 폴더 밖에 아무것도 없고 안에도 아무것도 풀지 않는다(앞의 멀쩡한 항목도).
    /// </summary>
    [SfxStubTheory]
    [MemberData(nameof(SlipNames))]
    public void 경로공격_이름이_있으면_1이고_아무것도_풀지_않는다(string attack)
    {
        using var t = new TempDir("sfxslip");
        string dest = Path.Combine(t.Dir("a", "b"), "dest");
        string name = attack == "ABS" ? t.P("abs_evil.txt").Replace('\\', '/') : attack;
        string zip = t.P("slip.zip");
        ZipSlipTests.Make("zip", zip, ("ok.txt", Encoding.UTF8.GetBytes("ok")), (name, ZipSlipTests.Evil));
        string exe = ComposeRaw(t, zip);
        var before = TreeAssert.Snapshot(t.Root);

        var run = RunSilent(exe, "/s", "/d:" + dest);

        Assert.Equal(1, run.ExitCode);
        run.AssertNoWindow();
        Assert.Contains("안전하지 않은 경로", run.Error);
        ZipSlipTests.AssertNothingOutside(t, dest, before, $"자동 풀림 '{attack}'");
        AssertNothingIn(dest, $"자동 풀림 '{attack}'");
    }

    /// <summary>계약: 인자가 틀리면 종료 코드 2·쉬운 한국어(창 없음) — /d 없는 /s, 모르는 인자, 빈 /d:.</summary>
    [SfxStubTheory]
    [InlineData("/s")]
    [InlineData("/s|/d:{dest}|/x")]
    [InlineData("/s|/d:")]
    [InlineData("/s|그냥글자")]
    public async Task 인자가_틀리면_2(string argSpec)
    {
        using var t = new TempDir("sfxarg");
        string f = t.P("a.txt");
        File.WriteAllText(f, "a");
        string exe = await MakeSfx(t, new[] { f });
        string dest = t.P("out");
        var run = RunSilent(exe, argSpec.Replace("{dest}", dest).Split('|'));
        Assert.Equal(2, run.ExitCode);
        run.AssertNoWindow();
        Assert.Matches("[가-힣]", run.Error);
        Assert.False(Directory.Exists(dest));
    }

    /// <summary>
    /// 계약(무인 모드의 같은 이름): 이미 있는 같은 이름 파일은 덮어쓰고(도움말에 적은 약속 — 7-Zip 자동 풀림의 -y 와 같음), 다른 파일은 그대로 둔다.
    /// 대소문자·'/D:'·'-s' 도 받는다. exe 이름·위치에 한글·공백이 있어도, 다른 폴더에서 실행해도 된다.
    /// </summary>
    [SfxStubFact]
    public async Task 무인_모드는_같은_이름을_덮어쓰고_다른_파일은_그대로다()
    {
        using var t = new TempDir("sfxow");
        string src = t.Dir("src");
        File.WriteAllText(Path.Combine(src, "보고서.txt"), "새 내용");
        File.WriteAllText(Path.Combine(src, "새 파일.txt"), "새");
        string exe = await MakeSfx(t, new[] { Path.Combine(src, "보고서.txt"), Path.Combine(src, "새 파일.txt") }, name: "한글 이름 (최종).exe");

        string dest = t.Dir("기존 폴더");
        File.WriteAllText(Path.Combine(dest, "보고서.txt"), "옛 내용");
        File.WriteAllText(Path.Combine(dest, "내 파일.txt"), "건드리면 안 됨");
        File.SetAttributes(Path.Combine(dest, "보고서.txt"), FileAttributes.ReadOnly);

        var run = RunSilent(exe, "-S", "/D:" + dest);
        Assert.True(run.ExitCode == 0, $"종료 코드 {run.ExitCode}: {run.Error}");
        Assert.Equal("새 내용", File.ReadAllText(Path.Combine(dest, "보고서.txt")));
        Assert.Equal("새", File.ReadAllText(Path.Combine(dest, "새 파일.txt")));
        Assert.Equal("건드리면 안 됨", File.ReadAllText(Path.Combine(dest, "내 파일.txt")));
        Assert.Equal(new[] { "내 파일.txt", "보고서.txt", "새 파일.txt" }, TreeAssert.Snapshot(dest).Keys.ToArray());
    }

    /// <summary>계약: 빈 압축(빈 폴더 하나)도 만들고 푼다.</summary>
    [SfxStubFact]
    public async Task 빈_폴더만_있어도_만들고_푼다()
    {
        using var t = new TempDir("sfxempty");
        string empty = t.Dir("src", "빈 폴더");
        string exe = await MakeSfx(t, new[] { empty });
        string dest = t.P("out");
        var run = RunSilent(exe, "/s", "/d:" + dest);
        Assert.True(run.ExitCode == 0, $"종료 코드 {run.ExitCode}: {run.Error}");
        Assert.True(Directory.Exists(Path.Combine(dest, "빈 폴더")));
        Assert.Empty(TreeAssert.Files(dest));
    }

    // ───────────── 일집 안에서 열기·시험 ─────────────

    /// <summary>
    /// 계약: 일집이 만든 자동 풀림 exe 를 일집 안에서 열면(내용 판별 — .exe 는 확장자로 고르지 않음) <see cref="SfxArchiveService"/> 가 꼬리로 알아보고
    /// 목록·전체 풀기·선택 풀기가 된다. 암호(한글 ZipCrypto 암호 포함)도 ZIP 과 같은 규칙. 만든 exe 시험(<see cref="CreatedArchiveTester"/>)도 통과.
    /// </summary>
    [Theory]
    [InlineData(null, ZipEncryption.Aes256)]
    [InlineData("한글암호2026", ZipEncryption.ZipCrypto)]
    [InlineData("한글암호2026", ZipEncryption.Aes256)]
    public async Task 일집_안에서_우리_자동풀림을_열고_푼다(string? password, ZipEncryption encryption)
    {
        using var t = new TempDir("sfxin");
        string root = SampleTree.Create(t.Dir("src"));
        string exe = await MakeSfx(t, new[] { root }, options: new CompressionOptions { Password = password, Encryption = encryption });

        var locator = new ArchiveServiceLocator();
        Assert.Null(locator.Resolve(exe));   // 확장자로는 압축으로 치지 않음(우클릭·끌어 놓기 규칙 그대로)
        var svc = locator.ResolveByContent(exe);
        Assert.IsType<SfxArchiveService>(svc);
        Assert.Equal(ArchiveKind.ZipSfx, ArchiveSniffer.Detect(exe, out long offset));
        Assert.Equal(new FileInfo(Stub).Length, offset);

        var list = await svc!.ListEntriesAsync(exe, password);
        TreeAssert.ListFilesEqual(list, SampleTree.ExpectedOf(root), "목록");

        string dest = t.P("out");
        await svc.ExtractAsync(exe, dest, password);
        TreeAssert.ExtractedTreeEquals(dest, SampleTree.ExpectedOf(root), context: "일집 안 풀기");

        string some = t.P("some");
        string one = $"{SampleTree.RootName}/하위/반복.txt";
        await svc.ExtractEntriesAsync(exe, some, new[] { one }, password);
        Assert.Equal(new[] { one }, TreeAssert.Files(some));

        CreatedArchiveTester.Instance.Test(exe, password, CancellationToken.None);
        if (password is not null)
        {
            var ex = await Svc.Catch(() => svc.ExtractAsync(exe, t.P("wrong"), "틀린암호"));
            Assert.IsAssignableFrom<ArchivePasswordException>(ex);
            TreeAssert.NoFiles(t.P("wrong"));
        }
    }

    /// <summary>계약: 내용이 깨진 일집 자동 풀림은 일집 안의 시험·풀기에서도 손상으로 알린다(반쪽 파일 없음).</summary>
    [Fact]
    public async Task 일집_안에서도_깨진_자동풀림은_손상으로_알린다()
    {
        using var t = new TempDir("sfxinbad");
        string src = t.Dir("src");
        var rnd = new Random(11);
        var big = new byte[200_000];
        rnd.NextBytes(big);
        File.WriteAllBytes(Path.Combine(src, "자료.bin"), big);
        string exe = await MakeSfx(t, new[] { Path.Combine(src, "자료.bin") }, options: new CompressionOptions { Level = CompressionLevelOption.Store });
        byte[] b = File.ReadAllBytes(exe);
        b[(int)(new FileInfo(Stub).Length + 100_000)] ^= 0x33;
        File.WriteAllBytes(exe, b);

        Assert.ThrowsAny<ArchiveException>(() => CreatedArchiveTester.Instance.Test(exe, null, CancellationToken.None));
        string dest = t.P("out");
        var ex = await Svc.Catch(() => Svc.For(exe).ExtractAsync(exe, dest));
        Assert.IsAssignableFrom<CorruptArchiveException>(ex);
        TreeAssert.NoFiles(dest);
    }

    // ───────────── 만들기의 안전 ─────────────

    /// <summary>
    /// 계약: 스텁이 없으면 쉬운 한국어(<see cref="SfxStubMissingException"/>)로 멈추고 아무것도 만들지 않는다(반쪽 exe·임시 파일 없음).
    /// 취소하면 exe·임시 파일이 남지 않고, 덮어쓰려던 기존 exe 는 바이트 그대로다.
    /// </summary>
    [Fact]
    public async Task 스텁이_없거나_취소하면_아무것도_남지_않는다()
    {
        using var t = new TempDir("sfxsafe");
        string src = t.Dir("src");
        string f = Path.Combine(src, "a.txt");
        File.WriteAllText(f, "내용");
        string outDir = t.Dir("out");
        string exe = Path.Combine(outDir, "결과.exe");

        var missing = await Svc.Catch(() => SfxBuilder.CreateAsync(new[] { f }, exe, null, null, CancellationToken.None, t.P("없는 스텁.exe")));
        Assert.IsType<SfxStubMissingException>(missing);
        Assert.Contains("부품", missing!.Message);
        Assert.Empty(Directory.EnumerateFileSystemEntries(outDir));

        File.WriteAllText(exe, "예전 exe");
        string sha = TreeAssert.Sha256(exe);
        using var cts = new CancellationTokenSource();
        var progress = new ProgressRecorder(p => { if (p.BytesProcessed > 0 || p.IsStage) cts.Cancel(); });
        var big = new byte[3_000_000];
        new Random(3).NextBytes(big);
        File.WriteAllBytes(Path.Combine(src, "큰 파일.bin"), big);
        var cancelled = await Svc.Catch(() => SfxBuilder.CreateAsync(new[] { src }, exe, null, progress, cts.Token));
        Assert.IsAssignableFrom<OperationCanceledException>(cancelled);
        Assert.Equal(sha, TreeAssert.Sha256(exe));
        Assert.Equal(new[] { "결과.exe" }, Directory.EnumerateFileSystemEntries(outDir).Select(Path.GetFileName).ToArray());
    }

    /// <summary>
    /// 계약: 원본 폴더 안에 있던 같은 이름 exe 를 덮어쓰면 예전 exe 는 새 exe 에 담기지 않는다(만들 exe 자신은 원본에서 뺌). 빼기 목록·건너뛴 원본은 ZIP 과 같다.
    /// </summary>
    [Fact]
    public async Task 원본_폴더_안의_예전_exe는_담지_않는다()
    {
        using var t = new TempDir("sfxself");
        string src = t.Dir("자료");
        File.WriteAllText(Path.Combine(src, "문서.txt"), "문서");
        File.WriteAllText(Path.Combine(src, "Thumbs.db"), "썸네일");
        string exe = Path.Combine(src, "자료.exe");
        File.WriteAllText(exe, "예전에 만든 exe");

        var result = await SfxBuilder.CreateAsync(new[] { src }, exe,
            new CompressionOptions().WithExcludeFilter(new PatternFileFilter(PatternFileFilter.DefaultPatterns)));

        var names = (await new SfxArchiveService().ListEntriesAsync(exe)).Where(e => !e.IsDirectory).Select(e => e.Path).ToList();
        Assert.Equal(new[] { "자료/문서.txt" }, names);
        Assert.Single(result.ExcludedPaths);
        using var fs = File.OpenRead(exe);
        Assert.True(SfxBuilder.TryReadTrailer(fs, out _, out _));
    }

    /// <summary>
    /// 계약(소스·빌드 확인): 스텁은 .NET Framework 4.6.2 이고 암호 계산(WinZip AES 키·AES-CTR·HMAC·ZipCrypto·CRC)은 본체와 같은 파일(Iljip\Services\Shared)을 링크한다 —
    /// 일집이 만든 암호를 스텁이 다르게 계산하는 일이 없게(본체 WinZipAes·ZipPasswordProbe 도 그 공유 코드를 씀).
    /// 스텁은 실행 파일 옆 sfx 폴더에만 복사된다(출력 폴더 맨 위에 Iljip.SfxStub.exe 를 두지 않음). 저장소의 Iljip\bin 아래에는 어떤 exe 도 만들지 않는다
    /// (재사용장치가 그 경로를 일집-dist 보다 먼저 찾음 — 빌드 산출물은 Directory.Build.props 로 저장소 밖).
    /// </summary>
    [Fact]
    public void 스텁은_공유_암호_코드를_쓰고_sfx_폴더에만_복사된다()
    {
        string csproj = RepoFiles.Read("Iljip.SfxStub", "Iljip.SfxStub.csproj");
        Assert.Contains("<TargetFramework>net462</TargetFramework>", csproj);
        Assert.Contains(@"<Compile Include=""..\Iljip\Services\Shared\*.cs""", csproj);
        Assert.Contains("<DebugType>none</DebugType>", csproj);
        Assert.True(File.Exists(RepoFiles.PathOf("Iljip", "Services", "Shared", "WinZipAesCore.cs")));
        Assert.True(File.Exists(RepoFiles.PathOf("Iljip", "Services", "Shared", "ZipCryptoCore.cs")));
        string aes = RepoFiles.Read("Iljip", "Services", "WinZipAes.cs");
        Assert.Contains("WinZipAesKeys.", aes);
        Assert.Contains("WinZipAesCipher", aes);
        Assert.Contains("ZipCryptoKeys.CheckHeader(", RepoFiles.Read("Iljip", "Services", "ZipPasswordProbe.cs"));
        string manifest = RepoFiles.Read("Iljip.SfxStub", "app.manifest");
        Assert.Contains("level=\"asInvoker\"", manifest);   // 관리자 권한을 묻지 않음(설치 프로그램으로 보지 않게)

        Assert.True(File.Exists(Path.Combine(AppContext.BaseDirectory, "sfx", "Iljip.SfxStub.exe")), "시험 출력에 sfx\\Iljip.SfxStub.exe 가 없음");
        Assert.False(File.Exists(Path.Combine(AppContext.BaseDirectory, "Iljip.SfxStub.exe")), "스텁이 출력 폴더 맨 위에도 복사됨");
        // 산출물을 저장소 밖으로 보내는 설정(이 PC 의 Directory.Build.props — git 에 없음)이 있을 때만 본다(새로 받은 저장소는 원래 Iljip\bin 에 빌드됨)
        string props = RepoFiles.PathOf("Directory.Build.props");
        string repoBin = RepoFiles.PathOf("Iljip", "bin");
        if (File.Exists(props) && File.ReadAllText(props).Contains("<BaseOutputPath>", StringComparison.Ordinal) && Directory.Exists(repoBin))
            Assert.Empty(Directory.GetFiles(repoBin, "*.exe", SearchOption.AllDirectories));
    }

    // ───────────── 같은 곳에 동시에·강제로 끊긴 뒤(W2-10 검증 지적 1차) ─────────────

    /// <summary>
    /// 스텁이 풀 곳 안 임시 폴더에 다 풀어 놓을 때까지 기다린다(주인 표시 빼고 파일 <paramref name="files"/> 개 — 30초 제한). 그 임시 폴더 경로.
    /// 시험이 그 풀 곳의 마무리 잠금을 쥐고 있으면 스텁은 여기서 멈춰 기다린다.
    /// </summary>
    private static string WaitUntilStaged(string dest, int files, SfxRunning run)
    {
        var sw = Stopwatch.StartNew();
        while (sw.Elapsed < TimeSpan.FromSeconds(30))
        {
            run.CheckWindow();
            if (run.Process.HasExited)
            {
                var early = run.Finish();
                Assert.Fail($"임시 폴더에 다 풀기 전에 끝남(종료 코드 {early.ExitCode}): {early.Error}");
            }
            var stagings = Directory.Exists(dest) ? Directory.GetDirectories(dest, SfxStaging.Prefix + "*") : Array.Empty<string>();
            if (stagings.Length == 1 && TreeAssert.Files(stagings[0]).Count(f => f != SfxStaging.MarkerFileName) >= files)
                return stagings[0];
            Thread.Sleep(50);
        }
        Assert.Fail("30초 안에 임시 폴더에 다 풀지 못함: " + dest);
        return string.Empty;
    }

    /// <summary>
    /// 계약: 같은 풀 곳의 '제자리로 옮기기'는 한 번에 하나씩 — 다른 자동 풀림이 그 풀 곳을 마무리하는 중이면(시험이 그 잠금을 쥠) 임시 폴더에 다 풀어 놓고
    /// 기다리며, 그동안 풀 곳에는 숨은 임시 폴더 말고 아무것도 옮기지 않는다. 잠금이 풀리면 이어서 끝내고 종료 코드 0·원본과 같음·임시 폴더 없음.
    /// (예전: 두세 개가 같은 곳에 동시에 풀면 실패한 쪽의 되돌리기가 성공한 쪽의 파일을 지워 '0 인데 파일이 없음'이 생겼음)
    /// </summary>
    [SfxStubFact]
    public async Task 같은_풀_곳의_마무리는_한_번에_하나씩_한다()
    {
        using var t = new TempDir("sfxlock");
        string root = SampleTree.Create(t.Dir("src"));
        string exe = await MakeSfx(t, new[] { root });
        string dest = t.P("같은 곳");
        var expected = SampleTree.ExpectedOf(root);

        // 아래는 await 없이 한 스레드에서(뮤텍스는 잡은 스레드가 놓아야 함)
        using var held = new Mutex(true, SfxStaging.DestinationMutexName(Path.GetFullPath(dest)), out bool createdNew);
        Assert.True(createdNew, "같은 이름의 잠금이 이미 있음");
        using var run = StartSilent(exe, "/s", "/d:" + dest);
        string staging = WaitUntilStaged(dest, expected.Count(e => !e.IsDir), run);
        Thread.Sleep(800);
        Assert.False(run.Process.HasExited, "잠금을 쥐고 있는데 끝남(기다리지 않음)");
        Assert.Equal(new[] { Path.GetFileName(staging) }, Directory.GetFileSystemEntries(dest).Select(Path.GetFileName).ToArray());
        held.ReleaseMutex();

        var result = run.Finish();
        Assert.True(result.ExitCode == 0, $"종료 코드 {result.ExitCode}: {result.Error}");
        Assert.True(result.Elapsed > TimeSpan.FromMilliseconds(800));
        TreeAssert.ExtractedTreeEquals(dest, expected, context: "잠금 뒤");
    }

    /// <summary>
    /// 계약: 같은 곳에 셋을 동시에 무인으로 풀어도 모두 종료 코드 0 이고 결과는 원본과 같다(빠진 파일·임시 폴더 없음 — 나중 것은 같은 이름을 덮어씀).
    /// </summary>
    [SfxStubFact]
    public async Task 같은_곳에_셋을_동시에_풀어도_모두_되고_빠진_파일이_없다()
    {
        using var t = new TempDir("sfxconc");
        string root = SampleTree.Create(t.Dir("src"));
        var rnd = new Random(14);
        for (int i = 0; i < 6; i++)
        {
            var b = new byte[1_500_000];
            rnd.NextBytes(b);
            File.WriteAllBytes(Path.Combine(root, "하위", $"큰 파일 {i}.bin"), b);
        }
        string exe = await MakeSfx(t, new[] { root }, options: new CompressionOptions { Level = CompressionLevelOption.Store });
        string dest = t.P("같은 곳");

        var runs = Enumerable.Range(0, 3).Select(_ => StartSilent(exe, "/s", "/d:" + dest)).ToList();
        try
        {
            foreach (var r in runs.Select(r => r.Finish()))
            {
                Assert.True(r.ExitCode == 0, $"종료 코드 {r.ExitCode}: {r.Error}");
                r.AssertNoWindow();
            }
        }
        finally
        {
            foreach (var r in runs) r.Dispose();
        }
        TreeAssert.ExtractedTreeEquals(dest, SampleTree.ExpectedOf(root), context: "동시에 셋");
    }

    /// <summary>
    /// 계약: 풀다가 강제로 끝나면(작업 관리자 종료·전원 꺼짐 — 정리할 틈 없음) 풀 곳에 숨은 임시 폴더와 그 안의 파일이 남지만, 바로 다시 풀면
    /// (하루를 기다리지 않고) 그 임시 폴더를 지우고 풀린다 — 종료 코드 0·원본과 같음·임시 폴더 없음·안내 없음(원래 파일을 옮긴 것이 없으므로).
    /// 예전: 하루 지난 것만 지워 수백 MB 가 숨은 채로 남았음.
    /// </summary>
    [SfxStubFact]
    public async Task 강제로_끝난_풀기의_임시_폴더는_바로_다음_풀기가_지운다()
    {
        using var t = new TempDir("sfxkill");
        string root = SampleTree.Create(t.Dir("src"));
        string exe = await MakeSfx(t, new[] { root });
        string dest = t.P("풀 곳");
        var expected = SampleTree.ExpectedOf(root);

        string staging;
        using (var held = new Mutex(true, SfxStaging.DestinationMutexName(Path.GetFullPath(dest)), out _))
        {
            using var run = StartSilent(exe, "/s", "/d:" + dest);
            staging = WaitUntilStaged(dest, expected.Count(e => !e.IsDir), run);
            run.Kill();
            held.ReleaseMutex();
        }
        Assert.True(Directory.Exists(staging), "강제 종료인데 임시 폴더가 없음(시험 전제)");
        Assert.True(File.GetAttributes(staging).HasFlag(FileAttributes.Hidden));
        Assert.NotEmpty(TreeAssert.Files(staging));

        var again = RunSilent(exe, "/s", "/d:" + dest);
        Assert.True(again.ExitCode == 0, $"종료 코드 {again.ExitCode}: {again.Error}");
        again.AssertNoWindow();
        Assert.Equal(string.Empty, again.Output);
        Assert.False(Directory.Exists(staging), "강제로 끝난 풀기의 임시 폴더가 그대로 남음");
        TreeAssert.ExtractedTreeEquals(dest, expected, context: "강제 종료 뒤 다시");
    }

    /// <summary>
    /// 계약: 제자리로 옮기다 실패하면(풀 곳에 같은 이름의 '폴더'가 있어 파일을 둘 수 없음) 이번에 옮긴 것을 모두 되돌린다 — 풀 곳에 없던 폴더를 통째로 옮긴 것
    /// (작은 파일이 많을 때 빠르게 하려고 폴더째 옮김 — W2-10 검증 지적 1차)과 덮어쓴 파일(원래 파일로 되살림)까지. 풀 곳은 풀기 전과 바이트까지 같고 임시 폴더도 없다. 종료 코드 1.
    /// </summary>
    [SfxStubFact]
    public void 옮기다_실패하면_통째로_옮긴_폴더와_덮어쓴_파일까지_되돌린다()
    {
        using var t = new TempDir("sfxrollback");
        string zip = t.P("r.zip");
        // 차례: 덮어쓸 파일 → 풀 곳에 없는 폴더(통째로 옮겨짐) → 폴더에 막히는 파일
        ZipSlipTests.Make("zip", zip,
            ("보고서.txt", Encoding.UTF8.GetBytes("새 보고서")),
            ("새 폴더/가.txt", Encoding.UTF8.GetBytes("가")),
            ("새 폴더/하위/나.txt", Encoding.UTF8.GetBytes("나")),
            ("있던 폴더/다.txt", Encoding.UTF8.GetBytes("다")),
            ("막힘.txt", Encoding.UTF8.GetBytes("막힘")));
        string exe = ComposeRaw(t, zip);
        string dest = t.Dir("풀 곳");
        File.WriteAllText(Path.Combine(dest, "보고서.txt"), "옛 보고서");
        Directory.CreateDirectory(Path.Combine(dest, "있던 폴더"));
        File.WriteAllText(Path.Combine(dest, "있던 폴더", "내 파일.txt"), "그대로");
        Directory.CreateDirectory(Path.Combine(dest, "막힘.txt"));
        File.WriteAllText(Path.Combine(dest, "막힘.txt", "안.txt"), "그대로");
        var before = TreeAssert.Snapshot(dest);

        var run = RunSilent(exe, "/s", "/d:" + dest);

        Assert.Equal(1, run.ExitCode);
        run.AssertNoWindow();
        Assert.Contains("폴더가 있어서", run.Error);
        Assert.Equal(before, TreeAssert.Snapshot(dest));
        Assert.Equal("옛 보고서", File.ReadAllText(Path.Combine(dest, "보고서.txt")));
    }

    /// <summary>
    /// 계약: 압축 속 맨 위 이름이 임시 폴더의 표시 이름('.사용 중'·'.덮어쓴 파일')과 같아도 풀린다(풀린 것은 임시 폴더 안 한 칸 아래에 두므로 겹치지 않음).
    /// </summary>
    [SfxStubFact]
    public void 표시_이름과_같은_항목도_풀린다()
    {
        using var t = new TempDir("sfxnames");
        string zip = t.P("n.zip");
        ZipSlipTests.Make("zip", zip,
            (SfxStaging.MarkerFileName, Encoding.UTF8.GetBytes("표시와 같은 이름")),
            (SfxStaging.BackupFolderName + "/0", Encoding.UTF8.GetBytes("백업과 같은 이름")));
        string exe = ComposeRaw(t, zip);
        string dest = t.P("out");

        var run = RunSilent(exe, "/s", "/d:" + dest);

        Assert.True(run.ExitCode == 0, $"종료 코드 {run.ExitCode}: {run.Error}");
        Assert.Equal("표시와 같은 이름", File.ReadAllText(Path.Combine(dest, SfxStaging.MarkerFileName)));
        Assert.Equal("백업과 같은 이름", File.ReadAllText(Path.Combine(dest, SfxStaging.BackupFolderName, "0")));
        Assert.Empty(Directory.GetDirectories(dest, SfxStaging.Prefix + "*"));
    }

    /// <summary>
    /// 강제로 끊긴 자동 풀림이 남긴 모양을 만든다: 주인 표시(아무도 안 엶)·반쪽 파일·덮어쓰려고 비켜 둔 원래 파일 둘(목록에 원래 자리가 있는 것 '하위\보고서.txt'·없는 것 '1'),
    /// 그리고 살아 있는 다른 풀기의 임시 폴더(주인 표시를 시험이 열어 둠 — 돌려준 스트림을 닫기 전까지)와 표시 없는 새 임시 폴더(본체가 푸는 중일 수 있음).
    /// </summary>
    private static (string Stale, string Live, string Unmarked, FileStream LiveMarker) MakeLeftovers(string dest)
    {
        string stale = Path.Combine(dest, SfxStaging.Prefix + "0badf00d");
        Directory.CreateDirectory(Path.Combine(stale, "하위"));
        File.WriteAllBytes(Path.Combine(stale, SfxStaging.MarkerFileName), Array.Empty<byte>());
        File.WriteAllBytes(Path.Combine(stale, "하위", "반쪽.bin"), new byte[1000]);
        string backups = Directory.CreateDirectory(Path.Combine(stale, SfxStaging.BackupFolderName)).FullName;
        File.WriteAllText(Path.Combine(backups, "0"), "원래 보고서");
        File.WriteAllText(Path.Combine(backups, "1"), "목록에 없는 원래 파일");
        File.WriteAllText(Path.Combine(backups, SfxStaging.BackupListName), "0\t하위\\보고서.txt\r\n", new UTF8Encoding(false));
        new DirectoryInfo(stale).Attributes |= FileAttributes.Hidden;

        string live = Path.Combine(dest, SfxStaging.Prefix + "11111111");
        Directory.CreateDirectory(live);
        File.WriteAllText(Path.Combine(live, "푸는 중.bin"), "x");
        var liveMarker = new FileStream(Path.Combine(live, SfxStaging.MarkerFileName), FileMode.Create, FileAccess.ReadWrite, FileShare.None);

        string unmarked = Path.Combine(dest, SfxStaging.Prefix + "22222222");
        Directory.CreateDirectory(unmarked);
        File.WriteAllText(Path.Combine(unmarked, "본체.bin"), "y");
        return (stale, live, unmarked, liveMarker);
    }

    private static void AssertRescued(string dest, string context)
    {
        string rescue = Assert.Single(Directory.GetDirectories(dest, SfxStaging.RescueFolderPrefix + " (*"));
        Assert.Matches(@"^덮어쓰기 전 원래 파일 \(\d{4}-\d{2}-\d{2} \d{2}-\d{2}-\d{2}\)$", Path.GetFileName(rescue));
        Assert.False(new DirectoryInfo(rescue).Attributes.HasFlag(FileAttributes.Hidden), $"{context}: 옮겨 둔 폴더가 숨겨져 있음");
        Assert.Equal(new[] { "1", "하위/보고서.txt" }, TreeAssert.Files(rescue).ToArray());
        Assert.Equal("원래 보고서", File.ReadAllText(Path.Combine(rescue, "하위", "보고서.txt")));
        Assert.Equal("목록에 없는 원래 파일", File.ReadAllText(Path.Combine(rescue, "1")));
    }

    /// <summary>
    /// 계약: 강제로 끊긴 자동 풀림의 임시 폴더에 덮어쓰려고 비켜 둔 사용자 원래 파일이 있으면, 다음 풀기는 그 파일을 지우지 않고 풀 곳의 보이는 폴더
    /// '덮어쓰기 전 원래 파일 (날짜 시각)' 에 원래 자리 모양 그대로(목록에 없으면 번호 이름) 옮기고, 표준 출력에 그 안내 한 줄(종료 코드는 0).
    /// 반쪽 파일·임시 폴더는 지운다. 살아 있는 다른 풀기의 임시 폴더(주인 표시를 누가 열어 둠)와 표시 없는 새 임시 폴더는 건드리지 않는다.
    /// (예전: 숨은 '.덮어쓴 파일' 에 말없이 남았다가 하루 뒤 같은 곳에 풀면 안내 없이 지워졌음)
    /// </summary>
    [SfxStubFact]
    public async Task 끊긴_풀기가_비켜_둔_원래_파일은_지우지_않고_보이는_폴더로_옮긴다()
    {
        using var t = new TempDir("sfxrescue");
        string src = t.Dir("src");
        File.WriteAllText(Path.Combine(src, "새 파일.txt"), "새 내용");
        string exe = await MakeSfx(t, new[] { Path.Combine(src, "새 파일.txt") });
        string dest = t.Dir("풀 곳");
        var (stale, live, unmarked, liveMarker) = MakeLeftovers(dest);
        using (liveMarker)
        {
            var run = RunSilent(exe, "/s", "/d:" + dest);
            Assert.True(run.ExitCode == 0, $"종료 코드 {run.ExitCode}: {run.Error}");
            run.AssertNoWindow();
            Assert.Contains("덮어쓰기 전 원래 파일 2개", run.Output);
            Assert.Contains(SfxStaging.RescueFolderPrefix + " (", run.Output);
        }

        Assert.False(Directory.Exists(stale), "주인 없는 임시 폴더가 남음");
        AssertRescued(dest, "자동 풀림");
        Assert.True(File.Exists(Path.Combine(live, "푸는 중.bin")), "살아 있는 풀기의 임시 폴더를 건드림");
        Assert.True(File.Exists(Path.Combine(unmarked, "본체.bin")), "표시 없는 새 임시 폴더를 건드림");
        Assert.Equal("새 내용", File.ReadAllText(Path.Combine(dest, "새 파일.txt")));
        Assert.Equal(new[] { live, unmarked }.OrderBy(x => x, StringComparer.Ordinal),
            Directory.GetDirectories(dest, SfxStaging.Prefix + "*").OrderBy(x => x, StringComparer.Ordinal));
    }

    /// <summary>
    /// 계약: 일집 본체의 풀기(7z.dll·압축된 tar 의 임시 폴더 정리 — <c>StagingFolder.CleanupStale</c>)도 같은 규칙이다 — 주인 없는 자동 풀림 임시 폴더는 바로 정리하며
    /// 원래 파일은 보이는 폴더로 옮기고, 살아 있는 것·표시 없는 새 것은 그대로, 표시 없는 것도 하루가 지났으면 지운다(본체의 예전 규칙).
    /// </summary>
    [Fact]
    public void 본체의_임시_폴더_정리도_같은_규칙이다()
    {
        using var t = new TempDir("sfxrescuemain");
        string dest = t.Dir("풀 곳");
        var (stale, live, unmarked, liveMarker) = MakeLeftovers(dest);
        string old = Path.Combine(dest, SfxStaging.Prefix + "33333333");
        Directory.CreateDirectory(old);
        File.WriteAllText(Path.Combine(old, "옛 조각.bin"), "z");
        Directory.SetCreationTimeUtc(old, DateTime.UtcNow.AddDays(-2));
        using (liveMarker)
            StagingFolder.CleanupStale(dest);

        Assert.False(Directory.Exists(stale));
        Assert.False(Directory.Exists(old), "하루 지난 표시 없는 임시 폴더가 남음");
        AssertRescued(dest, "본체");
        Assert.True(Directory.Exists(live));
        Assert.True(Directory.Exists(unmarked));
    }

    /// <summary>
    /// 계약: 원래 파일을 하나라도 못 옮기면(옮길 자리가 막힘) 그 임시 폴더는 지우지 않는다 — 원래 파일이 사라지지 않게. 옮길 수 있게 되면 다음 정리 때 옮긴다.
    /// </summary>
    [Fact]
    public void 원래_파일을_못_옮기면_임시_폴더를_지우지_않는다()
    {
        using var t = new TempDir("sfxrescueblock");
        string dest = t.Dir("풀 곳");
        var (stale, _, _, liveMarker) = MakeLeftovers(dest);
        liveMarker.Dispose();
        // 원래 파일 '0' 을 누가 열어 둠(옮길 수 없음)
        using (new FileStream(Path.Combine(stale, SfxStaging.BackupFolderName, "0"), FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            var r = SfxStaging.CleanupStale(dest, new DateTime(2026, 10, 14, 9, 30, 0));
            Assert.Equal(1, r.Moved);
            Assert.Equal(1, r.Left);
            Assert.Equal("덮어쓰기 전 원래 파일 (2026-10-14 09-30-00)", r.FolderName);
        }
        Assert.True(File.Exists(Path.Combine(stale, SfxStaging.BackupFolderName, "0")), "못 옮긴 원래 파일이 사라짐");

        var again = SfxStaging.CleanupStale(dest, new DateTime(2026, 10, 14, 9, 31, 0));
        Assert.Equal(1, again.Moved);
        Assert.Equal(0, again.Left);
        Assert.False(Directory.Exists(stale));
        Assert.Equal("원래 보고서", File.ReadAllText(Path.Combine(dest, "덮어쓰기 전 원래 파일 (2026-10-14 09-31-00)", "하위", "보고서.txt")));
    }

    // ───────────── W2-10 검증 지적 2차 ─────────────

    /// <summary>재사용장치가 '압축이 아님'으로 읽는 표지 — 손상 문구에 들어가면 안 됨(ReuseDeviceContractTests 와 같은 목록).</summary>
    private static readonly string[] NotArchiveMarkers = { "압축 파일이 아닌", "압축파일이 아닌", "not archive", "not an archive", "unsupported archive" };

    /// <summary>
    /// 계약: 만들 exe 가 한도(<see cref="SfxBuilder.MaxExeBytes"/> = 4GB − 1바이트 — Windows 가 시작하는 가장 큰 실행 파일, 2026-10-14 실측)를 넘으면
    /// 만들지 않는다 — <see cref="SfxTooLargeException"/>(쉬운 말 '4GB'·'ZIP'·만들었으면 됐을 크기), exe·임시 파일이 남지 않고 덮어쓰려던 기존 exe 는 바이트 그대로.
    /// 한도와 정확히 같은 크기는 만든다. (한도는 시험에서만 작게 — 4GB 를 실제로 쓰지 않음)
    /// </summary>
    [Fact]
    public async Task 한도를_넘는_exe는_만들지_않고_아무것도_남지_않는다()
    {
        Assert.Equal(4_294_967_295L, SfxBuilder.MaxExeBytes);
        using var t = new TempDir("sfxbig");
        string src = t.Dir("src");
        var data = new byte[200_000];
        new Random(21).NextBytes(data);
        File.WriteAllBytes(Path.Combine(src, "영상.bin"), data);
        string first = t.P("기준", "자료.exe");
        await SfxBuilder.CreateAsync(new[] { src }, first);
        long exact = new FileInfo(first).Length;

        string outDir = t.Dir("만든곳");
        string exe = Path.Combine(outDir, "자료.exe");
        using (SfxBuilder.LimitForTests(exact))
            await SfxBuilder.CreateAsync(new[] { src }, exe);
        Assert.Equal(exact, new FileInfo(exe).Length);   // 한도와 같은 크기는 만든다

        File.WriteAllText(exe, "예전 exe");
        string sha = TreeAssert.Sha256(exe);
        Exception? ex;
        using (SfxBuilder.LimitForTests(exact - 1))
            ex = await Svc.Catch(() => SfxBuilder.CreateAsync(new[] { src }, exe));
        var tooLarge = Assert.IsType<SfxTooLargeException>(ex);
        Assert.Equal(exact, tooLarge.ExeBytes);
        Assert.Contains("4GB", tooLarge.Message);
        Assert.Contains("ZIP", tooLarge.Message);
        Assert.Contains("실행되지 않아요", ErrorMessages.ToPlainMessage(tooLarge));
        Assert.Equal(sha, TreeAssert.Sha256(exe));
        Assert.Equal(new[] { "자료.exe" }, Directory.EnumerateFileSystemEntries(outDir).Select(Path.GetFileName).ToArray());

        // 만들기 전 어림: 원본 크기 합(빼기 목록 적용)이 한도를 넘는가
        Assert.Equal(200_000, SfxBuilder.SourceBytes(new[] { src }, null, CancellationToken.None));
        Assert.False(SfxBuilder.MayExceedLimit(SfxBuilder.MaxExeBytes));
        Assert.True(SfxBuilder.MayExceedLimit(SfxBuilder.MaxExeBytes + 1));
        using (SfxBuilder.LimitForTests(199_999))
            Assert.True(SfxBuilder.MayExceedLimit(SfxBuilder.SourceBytes(new[] { src }, null, CancellationToken.None)));
        File.WriteAllText(Path.Combine(src, "Thumbs.db"), "썸네일");
        Assert.Equal(200_000, SfxBuilder.SourceBytes(new[] { src }, new PatternFileFilter(PatternFileFilter.DefaultPatterns), CancellationToken.None));
    }

    /// <summary>
    /// 코드 서명 도구(signtool)가 하는 것처럼: 파일 끝을 8바이트 경계까지 0 으로 채우고 서명 표(WIN_CERTIFICATE — 길이 4 + 판 0x0200 + 종류 2(PKCS#7) + 내용, 8바이트 배수)를
    /// 덧붙인 뒤 PE 머리의 보안 디렉터리(데이터 디렉터리 4번)가 그 표를 가리키게 한다. 내용은 가짜(서명 검사는 하지 않음 — 모양만). <paramref name="after"/> 는 표 뒤에 더 붙일 것.
    /// </summary>
    internal static void SimulateCodeSignature(string exe, byte[]? after = null)
    {
        byte[] file = File.ReadAllBytes(exe);
        int pad = (8 - file.Length % 8) % 8;
        const int certBytes = 1500;
        int tableLength = (8 + certBytes + 7) & ~7;
        var table = new byte[tableLength];
        BinaryPrimitives.WriteUInt32LittleEndian(table, (uint)tableLength);
        BinaryPrimitives.WriteUInt16LittleEndian(table.AsSpan(4), 0x0200);
        BinaryPrimitives.WriteUInt16LittleEndian(table.AsSpan(6), 0x0002);
        new Random(5).NextBytes(table.AsSpan(8, certBytes));
        var signed = file.Concat(new byte[pad]).Concat(table).Concat(after ?? Array.Empty<byte>()).ToArray();
        int pe = BinaryPrimitives.ReadInt32LittleEndian(signed.AsSpan(0x3C));
        int magic = BinaryPrimitives.ReadUInt16LittleEndian(signed.AsSpan(pe + 24));
        int security = pe + 24 + (magic == 0x20B ? 144 : 128);
        Assert.Equal(0u, BinaryPrimitives.ReadUInt32LittleEndian(signed.AsSpan(security)));   // 스텁은 서명이 없음(시험 전제)
        BinaryPrimitives.WriteUInt32LittleEndian(signed.AsSpan(security), (uint)(file.Length + pad));
        BinaryPrimitives.WriteUInt32LittleEndian(signed.AsSpan(security + 4), (uint)tableLength);
        File.WriteAllBytes(exe, signed);
    }

    /// <summary>
    /// 계약: 만든 exe 에 나중에 코드 서명을 해도(끝에 서명 표가 붙음 — 회사가 SmartScreen·백신 오탐을 줄이려고 서명하는 길) 무인 모드로 풀리고 일집 안에서도 열린다
    /// (꼬리를 서명 표 바로 앞에서 찾음 — 스텁·본체가 같은 공유 소스 <see cref="SfxTrailer"/>). 예전엔 꼬리가 파일 맨 끝이 아니라고 '손상'이었다.
    /// </summary>
    [SfxStubFact]
    public async Task 나중에_코드_서명을_한_exe도_풀리고_일집에서도_열린다()
    {
        using var t = new TempDir("sfxsign");
        string root = SampleTree.Create(t.Dir("src"));
        string exe = await MakeSfx(t, new[] { root });
        long before = new FileInfo(exe).Length;
        SimulateCodeSignature(exe);
        Assert.True(new FileInfo(exe).Length > before);
        using (var fs = File.OpenRead(exe))
        {
            Assert.True(SfxBuilder.TryReadTrailer(fs, out long start, out long length));
            Assert.Equal(new FileInfo(Stub).Length, start);
            Assert.Equal(before - SfxBuilder.TrailerLength - start, length);
        }

        string dest = t.P("out");
        var run = RunSilent(exe, "/s", "/d:" + dest);
        Assert.True(run.ExitCode == 0, $"종료 코드 {run.ExitCode}: {run.Error}");
        run.AssertNoWindow();
        TreeAssert.ExtractedTreeEquals(dest, SampleTree.ExpectedOf(root), context: "서명한 exe");

        var svc = new ArchiveServiceLocator().ResolveByContent(exe);
        Assert.IsType<SfxArchiveService>(svc);
        string inApp = t.P("일집 안");
        await svc!.ExtractAsync(exe, inApp);
        TreeAssert.ExtractedTreeEquals(inApp, SampleTree.ExpectedOf(root), context: "서명한 exe — 일집 안");
        CreatedArchiveTester.Instance.Test(exe, null, CancellationToken.None);
    }

    /// <summary>
    /// 계약: 꼬리 뒤에 서명 표가 아닌 것이 붙었으면(0 바이트를 덧붙임 — 보안 디렉터리가 비어 있음) 또는 서명 표 뒤에 또 무엇이 붙었으면 예전처럼 손상이다 —
    /// 종료 코드 1·'잘렸어요'·대상에 아무것도 없음(서명 표는 보안 디렉터리가 가리키고 정확히 파일 끝까지일 때만 건너뜀).
    /// </summary>
    [SfxStubTheory]
    [InlineData("zeros")]
    [InlineData("after-signature")]
    public async Task 꼬리_뒤에_서명_표가_아닌_것이_붙으면_손상이다(string kind)
    {
        using var t = new TempDir("sfxtail" + kind[0]);
        string src = t.Dir("src");
        File.WriteAllText(Path.Combine(src, "a.txt"), "내용");
        string exe = await MakeSfx(t, new[] { src });
        if (kind == "zeros") File.AppendAllText(exe, "\0\0\0\0\0\0\0\0");
        else SimulateCodeSignature(exe, after: Encoding.ASCII.GetBytes("JUNKJUNKJUNKJUNK"));
        using (var fs = File.OpenRead(exe))
            Assert.False(SfxBuilder.TryReadTrailer(fs, out _, out _));

        string dest = t.P("out");
        var run = RunSilent(exe, "/s", "/d:" + dest);
        Assert.Equal(1, run.ExitCode);
        run.AssertNoWindow();
        Assert.Contains("잘렸어요", run.Error);
        AssertNothingIn(dest, kind);
    }

    /// <summary>
    /// 계약: 같은 폴더에 같은 한글 이름이 붙은 글자(NFC)·자모 분리(NFD) 두 모양으로 다 있던 원본(NTFS 는 둘을 다른 이름으로 둠 — 맥에서 복사해 온 파일 등)으로
    /// 만든 exe 도 풀린다 — 원본과 같은 두 파일(이름은 바꾸지 않음). 예전엔 둘 다 NFC 로 바꿔 '같은 이름이 두 번… 손상'으로 통째로 거절했다.
    /// 맥에서 만든 것처럼 자모 분리 이름만 있으면 예전처럼 붙은 글자로 풀고, 바꾸지 않아도 겹치는 이름(대소문자만 다름)은 예전처럼 손상이다.
    /// </summary>
    [SfxStubFact]
    public async Task NFC와_NFD_두_모양이_같이_있어도_원본대로_푼다()
    {
        using var t = new TempDir("sfxnfd");
        string src = t.Dir("src", "자료");
        string nfc = "맥에서온파일.txt";
        string nfd = nfc.Normalize(NormalizationForm.FormD);
        Assert.NotEqual(nfc, nfd);
        File.WriteAllText(Path.Combine(src, nfc), "붙은 글자");
        File.WriteAllText(Path.Combine(src, nfd), "자모 분리");
        Assert.Equal(2, Directory.GetFiles(src).Length);   // NTFS 는 둘 다 둠(시험 전제)
        string exe = await MakeSfx(t, new[] { src });

        string dest = t.P("out");
        var run = RunSilent(exe, "/s", "/d:" + dest);
        Assert.True(run.ExitCode == 0, $"종료 코드 {run.ExitCode}: {run.Error}");
        run.AssertNoWindow();
        string folder = Path.Combine(dest, "자료");
        Assert.Equal(new[] { nfc, nfd }.OrderBy(n => n, StringComparer.Ordinal),
            Directory.GetFiles(folder).Select(Path.GetFileName).OrderBy(n => n, StringComparer.Ordinal));
        Assert.Equal("붙은 글자", File.ReadAllText(Path.Combine(folder, nfc)));
        Assert.Equal("자모 분리", File.ReadAllText(Path.Combine(folder, nfd)));

        // 자모 분리 이름만 있으면(맥에서 만든 ZIP) 붙은 글자로
        string macZip = t.P("mac.zip");
        ZipSlipTests.Make("zip", macZip, ("맥 폴더".Normalize(NormalizationForm.FormD) + "/" + nfd, Encoding.UTF8.GetBytes("맥")));
        string macExe = ComposeRaw(t, macZip, "mac.exe");
        string macDest = t.P("out_mac");
        var mac = RunSilent(macExe, "/s", "/d:" + macDest);
        Assert.True(mac.ExitCode == 0, $"종료 코드 {mac.ExitCode}: {mac.Error}");
        Assert.Equal(new[] { "맥 폴더/" + nfc }, TreeAssert.Files(macDest).ToArray());
        Assert.True(File.Exists(Path.Combine(macDest, "맥 폴더", nfc)));

        // 바꾸지 않아도 겹치면(대소문자만 다른 두 이름) 예전처럼 손상 — 아무것도 풀지 않음
        string dupZip = t.P("dup.zip");
        ZipSlipTests.Make("zip", dupZip, ("Report.txt", Encoding.UTF8.GetBytes("1")), ("report.txt", Encoding.UTF8.GetBytes("2")));
        string dupExe = ComposeRaw(t, dupZip, "dup.exe");
        string dupDest = t.P("out_dup");
        var dup = RunSilent(dupExe, "/s", "/d:" + dupDest);
        Assert.Equal(1, dup.ExitCode);
        Assert.Contains("같은 이름", dup.Error);
        AssertNothingIn(dupDest, "대소문자만 다른 두 이름");
    }

    /// <summary>
    /// 계약: 끝이 잘린 일집 자동 풀림(꼬리·ZIP 끝 머리가 없음 — 덜 받아진 첨부)은 일집 안에서도 '지원하지 않는 형식'이 아니라 손상으로 알린다
    /// (ZIP 자동 풀림으로 알아보고 목록·풀기에서 '압축 파일의 끝부분이 잘려 있어요' — 예전: 내용 판별이 압축을 못 찾아 'Unsupported archive type: .exe').
    /// 대상에 아무것도 남지 않는다.
    /// 스텁만 있는 exe(붙은 것이 없음)는 그대로 압축이 아니다. 창 없이 풀기는 CommandLineTests 가 실제 Iljip.exe 로 본다.
    /// </summary>
    [Theory]
    [InlineData(30)]
    [InlineData(5000)]
    public async Task 끝이_잘린_자동풀림은_일집_안에서도_손상으로_알린다(int cutBytes)
    {
        using var t = new TempDir("sfxcutapp");
        string root = SampleTree.Create(t.Dir("src"));
        string exe = await MakeSfx(t, new[] { root });
        byte[] all = File.ReadAllBytes(exe);
        string cut = t.P("잘림.exe");
        File.WriteAllBytes(cut, all[..^cutBytes]);

        Assert.Equal(ArchiveKind.ZipSfx, ArchiveSniffer.Detect(cut, out long offset));
        Assert.Equal(new FileInfo(Stub).Length, offset);
        var svc = new ArchiveServiceLocator().ResolveForAutomation(cut);
        Assert.IsType<SfxArchiveService>(svc);
        string dest = t.P("out");
        var ex = await Svc.Catch(() => svc!.ExtractAsync(cut, dest));
        Assert.True(Assert.IsAssignableFrom<CorruptArchiveException>(ex).IsTruncated, "잘림으로 알리지 않음: " + ex!.Message);
        string message = ErrorMessages.ToUserMessage(ex!);
        Assert.Contains("끝부분이 잘려 있어요", message);
        foreach (var m in NotArchiveMarkers) Assert.DoesNotContain(m, message, StringComparison.OrdinalIgnoreCase);
        TreeAssert.NoFiles(dest);
        Assert.IsAssignableFrom<CorruptArchiveException>(await Svc.Catch(() => svc!.ListEntriesAsync(cut)));

        string bare = t.P("스텁만.exe");
        File.Copy(Stub, bare);
        Assert.Equal(ArchiveKind.Unknown, ArchiveSniffer.Detect(bare));
        Assert.Null(new ArchiveServiceLocator().ResolveForAutomation(bare));
    }

    /// <summary>
    /// 계약: 자동 풀림이 끝나며 임시 폴더를 지울 때는 주인 표시를 쥔 채 나머지(덮어쓴 원래 파일 등)를 먼저 지우고 표시는 마지막에 닫는다
    /// (<see cref="SfxStaging.DeleteOwnedTree"/> — 스텁의 마무리·주인 없는 흔적 정리가 같이 씀). 지우는 동안(여기서는 남은 파일 하나가 잠겨 멈춤)
    /// 같은 곳에 막 시작한 다른 풀기의 정리는 그 폴더를 '살아 있음'으로 보고 건드리지 않는다. 예전엔 표시를 먼저 닫아, 지우는 사이에 시작한 풀기가
    /// 방금 일부러 덮어쓴 원래 파일을 '덮어쓰기 전 원래 파일 (…)' 폴더로 옮기며 '지난번에 끝까지 못 푼 흔적을 정리했어요'라고 잘못 알렸다.
    /// </summary>
    [Fact]
    public async Task 임시_폴더를_지우는_동안에는_주인_표시를_놓지_않는다()
    {
        using var t = new TempDir("sfxdelorder");
        string dest = t.Dir("풀 곳");
        string staging = Path.Combine(dest, SfxStaging.Prefix + "abcdef01");
        string backups = Directory.CreateDirectory(Path.Combine(staging, SfxStaging.BackupFolderName)).FullName;
        File.WriteAllText(Path.Combine(backups, "0"), "일부러 덮어쓴 원래 파일");
        File.WriteAllText(Path.Combine(backups, SfxStaging.BackupListName), "0\t보고서.txt\r\n");
        string content = Directory.CreateDirectory(Path.Combine(staging, "내용", "하위")).FullName;
        string locked = Path.Combine(content, "잠긴 것.bin");
        File.WriteAllText(locked, "x");
        var marker = new FileStream(Path.Combine(staging, SfxStaging.MarkerFileName), FileMode.Create, FileAccess.ReadWrite, FileShare.None);

        Task<bool> deleting;
        using (new FileStream(locked, FileMode.Open, FileAccess.Read, FileShare.Read))   // 보안 프로그램이 잡은 것처럼 — 지우기는 200ms 간격으로 다시
        {
            deleting = Task.Run(() => SfxStaging.DeleteOwnedTree(staging, marker, attempts: 100));
            // 덮어쓴 원래 파일은 지워졌고 잠긴 것에서 멈춰 있는 동안
            var sw = Stopwatch.StartNew();
            while (Directory.Exists(backups) && sw.Elapsed < TimeSpan.FromSeconds(15)) await Task.Delay(20);
            Assert.False(Directory.Exists(backups), "덮어쓴 원래 파일을 먼저 지우지 않음");
            Assert.False(deleting.IsCompleted, "잠긴 파일이 있는데 지우기가 끝남(시험 전제)");

            Assert.Equal(SfxStaging.Owner.Alive, SfxStaging.Probe(staging, out var claim));
            claim?.Dispose();
            var other = SfxStaging.CleanupStale(dest, new DateTime(2026, 10, 14, 9, 0, 0));
            Assert.Equal(0, other.Moved);
            Assert.True(Directory.Exists(staging), "다른 풀기의 정리가 지우는 중인 임시 폴더를 가져감");
        }
        Assert.True(await deleting.WaitAsync(TimeSpan.FromSeconds(30)), "잠금을 푼 뒤에도 다 지우지 못함");
        Assert.Empty(Directory.GetFileSystemEntries(dest));
    }

    /// <summary>
    /// 계약: 덮어쓴 원래 파일의 원래 자리 목록(목록.txt — <see cref="SfxStaging.BackupList"/>)은 한 번 열어 둔 채 쓰지만(파일마다 열고 닫지 않음 — 덮어쓰기가 느렸던 까닭)
    /// 줄마다 OS 로 넘긴다 — 쓰는 도중에(프로세스가 강제로 죽은 것처럼) 다른 쪽이 읽어도 그때까지의 줄이 다 있다. 닫은 뒤에는 흔적 정리가 그 이름으로 옮긴다.
    /// </summary>
    [Fact]
    public void 원래_자리_목록은_열어_둔_채_줄마다_남는다()
    {
        using var t = new TempDir("sfxlist");
        string dest = t.Dir("풀 곳");
        string staging = Path.Combine(dest, SfxStaging.Prefix + "12345678");
        string backups = Directory.CreateDirectory(Path.Combine(staging, SfxStaging.BackupFolderName)).FullName;
        File.WriteAllBytes(Path.Combine(staging, SfxStaging.MarkerFileName), Array.Empty<byte>());
        string listPath = Path.Combine(backups, SfxStaging.BackupListName);
        using (var list = new SfxStaging.BackupList(backups))
        {
            list.Add(0, @"하위\보고서.txt");
            File.WriteAllText(Path.Combine(backups, "0"), "원래 보고서");
            list.Add(1, "한글 파일.txt");
            File.WriteAllText(Path.Combine(backups, "1"), "원래 한글 파일");
            using var fs = new FileStream(listPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var reader = new StreamReader(fs, new UTF8Encoding(false));
            Assert.Equal("0\t하위\\보고서.txt\r\n1\t한글 파일.txt\r\n", reader.ReadToEnd());
        }

        var r = SfxStaging.CleanupStale(dest, new DateTime(2026, 10, 14, 10, 0, 0));
        Assert.Equal(2, r.Moved);
        string rescued = Path.Combine(dest, "덮어쓰기 전 원래 파일 (2026-10-14 10-00-00)");
        Assert.Equal("원래 보고서", File.ReadAllText(Path.Combine(rescued, "하위", "보고서.txt")));
        Assert.Equal("원래 한글 파일", File.ReadAllText(Path.Combine(rescued, "한글 파일.txt")));
        Assert.False(Directory.Exists(staging));
    }

    /// <summary>
    /// 계약: 같은 곳에 다시 풀어 이미 있는 파일을 많이 덮어써도(원래 있던 폴더는 처음 볼 때 한 번 훑어 같은 이름을 가림 — 파일마다 옮겨 보지 않음, W2-10 검증 지적 2차)
    /// 결과는 같다 — 종료 코드 0·모두 새 내용·덮어쓴 원래 파일·임시 폴더가 남지 않음·풀 곳의 다른 파일은 그대로. 같은 이름의 '폴더'가 있으면 예전처럼 실패하고 되돌린다
    /// (<see cref="옮기다_실패하면_통째로_옮긴_폴더와_덮어쓴_파일까지_되돌린다"/>). 대소문자만 다른 이름은 그대로 덮어쓴다(NTFS 는 같은 파일).
    /// </summary>
    [SfxStubFact]
    public void 이미_있는_파일을_많이_덮어써도_결과가_같다()
    {
        using var t = new TempDir("sfxmanyow");
        const int count = 300;
        string zip = t.P("many.zip");
        var items = Enumerable.Range(0, count).Select(i => ($"폴더/{i:D4}.txt", Encoding.UTF8.GetBytes($"새 {i}"))).ToList();
        items.Add(("폴더/Case.txt", Encoding.UTF8.GetBytes("새 대소문자")));
        ZipSlipTests.Make("zip", zip, items.ToArray());
        string exe = ComposeRaw(t, zip);
        string dest = t.Dir("풀 곳");
        string folder = Directory.CreateDirectory(Path.Combine(dest, "폴더")).FullName;
        for (int i = 0; i < count; i += 2) File.WriteAllText(Path.Combine(folder, $"{i:D4}.txt"), $"옛 {i}");
        File.WriteAllText(Path.Combine(folder, "CASE.TXT"), "옛 대소문자");
        File.WriteAllText(Path.Combine(folder, "내 파일.txt"), "그대로");

        var run = RunSilent(exe, "/s", "/d:" + dest);

        Assert.True(run.ExitCode == 0, $"종료 코드 {run.ExitCode}: {run.Error}");
        run.AssertNoWindow();
        Assert.Equal(string.Empty, run.Output);
        for (int i = 0; i < count; i++) Assert.Equal($"새 {i}", File.ReadAllText(Path.Combine(folder, $"{i:D4}.txt")));
        Assert.Equal("새 대소문자", File.ReadAllText(Path.Combine(folder, "case.txt")));
        Assert.Equal("그대로", File.ReadAllText(Path.Combine(folder, "내 파일.txt")));
        Assert.Equal(count + 2, Directory.GetFiles(folder).Length);
        Assert.Equal(new[] { "폴더" }, Directory.GetFileSystemEntries(dest).Select(Path.GetFileName).ToArray());
    }

    // ───────────── 실행할 수 있는지(환경) ─────────────

    /// <summary>
    /// 스텁을 실행해 볼 수 있는가 — 회사 보안 정책(AppLocker·WDAC 등)이 임시 폴더의 exe 실행을 막으면 실행 시험은 실패가 아니라
    /// '환경 조건'으로 건너뛰고 그 까닭을 보인다(설계 W2-10 위험). 건너뛰는 것은 <b>Windows 가 실행 자체를 거절했을 때만</b>(<see cref="SkipReasonFor"/>).
    /// 스텁이 없거나, 실행은 됐는데 바로 죽거나(예: 0xE0434352) 종료 코드 약속이 깨졌으면 건너뛰지 않는다 — 그건 스텁의 고장이라 시험이 실패해야 한다
    /// (W2-10 검증 지적 1차: 예전엔 종료 코드가 1 이 아니기만 해도 37개가 모두 '건너뜀'이 됐음. 종료 코드는 <see cref="스텁만_실행하면_데이터가_없어_1이고_창이_없다"/> 가 본다).
    /// </summary>
    internal static string? CannotRunReason => LazyReason.Value;

    private static readonly Lazy<string?> LazyReason = new(() =>
    {
        if (!File.Exists(SfxBuilder.StubPath)) return null;
        string dir = Path.Combine(TempDir.BaseRoot, "sfxprobe_" + Guid.NewGuid().ToString("N")[..8]);
        try
        {
            Directory.CreateDirectory(dir);
            string copy = Path.Combine(dir, "probe.exe");
            File.Copy(SfxBuilder.StubPath, copy);
            RunSilent(copy, "/s", "/d:" + Path.Combine(dir, "out"));
            return null;   // 실행됐다 — 종료 코드가 무엇이든 건너뛰지 않음
        }
        catch (Exception ex)
        {
            return SkipReasonFor(ex);
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch { /* 무시 */ }
        }
    });

    /// <summary>
    /// 스텁을 시작하지 못한 까닭이 '이 PC 의 정책이 실행을 막음'이면 건너뛸 까닭(문구), 아니면 null(건너뛰지 않음 — 시험이 돌아 실패로 드러남).
    /// 파일이 없음(2·3)·exe 가 아님(193 ERROR_BAD_EXE_FORMAT)·CPU 가 안 맞음(216)은 스텁이 망가진 것이라 건너뛰지 않는다.
    /// 그 밖의 Win32 거절(5 접근 거부·1260 정책·4551 WDAC·225 백신 등)은 정책으로 본다.
    /// </summary>
    internal static string? SkipReasonFor(Exception? startError) =>
        startError is Win32Exception w && !BrokenStubErrors.Contains(w.NativeErrorCode)
            ? $"이 PC 의 보안 정책이 임시 폴더의 exe 실행을 막음(AppLocker 등, 오류 {w.NativeErrorCode}) — 실행 시험은 건너뜀: {w.Message}"
            : null;

    private static readonly HashSet<int> BrokenStubErrors = new() { 2, 3, 193, 216 };

    /// <summary>
    /// 계약(W2-10 검증 지적 1차): 건너뛰기 판정은 Windows 의 실행 거절(Win32Exception — 정책)만 받는다. 스텁 파일이 망가진 거절(193 등)·
    /// 다른 예외·예외 없음(= 실행됨, 종료 코드가 무엇이든)은 건너뛰지 않는다.
    /// </summary>
    [Fact]
    public void 건너뛰기는_실행이_정책으로_막혔을_때만()
    {
        Assert.NotNull(SkipReasonFor(new Win32Exception(1260)));   // ERROR_ACCESS_DISABLED_BY_POLICY(AppLocker·소프트웨어 제한 정책)
        Assert.NotNull(SkipReasonFor(new Win32Exception(4551)));   // WDAC(앱 제어 정책)
        Assert.NotNull(SkipReasonFor(new Win32Exception(5)));
        Assert.Null(SkipReasonFor(new Win32Exception(193)));       // exe 가 아님(망가진 스텁)
        Assert.Null(SkipReasonFor(new Win32Exception(2)));
        Assert.Null(SkipReasonFor(new InvalidOperationException("x")));
        Assert.Null(SkipReasonFor(null));
    }

    /// <summary>
    /// 계약(W2-10 검증 지적 1차): 스텁만(붙은 데이터 없이) 무인 모드로 돌리면 바로 죽지 않고 종료 코드 1 과 쉬운 한국어 '손상·잘렸어요' 한 줄, 창 없음, 풀 곳도 안 만듦.
    /// 스텁이 시작하자마자 죽거나 종료 코드 약속이 깨지면 여기서 실패한다(예전엔 실행 시험 전체가 '건너뜀'으로 숨었음).
    /// </summary>
    [SfxStubFact]
    public void 스텁만_실행하면_데이터가_없어_1이고_창이_없다()
    {
        using var t = new TempDir("sfxbare");
        string copy = t.P("그냥 스텁.exe");
        File.Copy(Stub, copy);
        string dest = t.P("out");
        var run = RunSilent(copy, "/s", "/d:" + dest);
        Assert.True(run.ExitCode == 1, $"종료 코드 {run.ExitCode}(0x{run.ExitCode:X8}): {run.Error}");
        run.AssertNoWindow();
        Assert.Contains("잘렸어요", run.Error);
        Assert.False(Directory.Exists(dest));
    }
}

/// <summary>만든 자동 풀림 exe 를 실제로 실행하는 시험 — 이 PC 가 exe 실행을 막으면(AppLocker 등) 건너뜀(환경 조건).</summary>
public sealed class SfxStubFactAttribute : FactAttribute
{
    public SfxStubFactAttribute()
    {
        if (SfxCreateTests.CannotRunReason is { } why) Skip = why;
    }
}

/// <summary><see cref="SfxStubFactAttribute"/> 의 Theory 판.</summary>
public sealed class SfxStubTheoryAttribute : TheoryAttribute
{
    public SfxStubTheoryAttribute()
    {
        if (SfxCreateTests.CannotRunReason is { } why) Skip = why;
    }
}
