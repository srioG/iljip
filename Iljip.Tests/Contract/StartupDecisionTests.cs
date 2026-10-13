using Iljip.Services;
using Iljip.Tests.TestSupport;

namespace Iljip.Tests.Contract;

/// <summary>
/// [W2-11 I3-15·I3-16] 켜질 때의 결정(<see cref="StartupDecision"/> — App.OnStartup 에서 떼어 낸 순수 함수):
/// 창 없이 풀기(재사용장치 계약 — 판정은 예전 그대로) → '--new-window'·'--elevated' 면 뮤텍스를 얻었든 못 얻었든 따로 뜨는 창 → 그 밖은 예전대로(첫 일집/넘기기).
/// 실제 UAC·두 창 띄우기는 사람이 확인(MANUAL_CHECKLIST 17절).
/// </summary>
public class StartupDecisionTests
{
    private static bool Exists(string p) => p == "있는.zip";

    /// <summary>일반 동사는 예전 결정 그대로: 뮤텍스를 얻었으면 첫 일집, 못 얻었으면 넘기기.</summary>
    [Theory]
    [InlineData(true, StartupKind.FirstInstance)]
    [InlineData(false, StartupKind.Forward)]
    public void 일반_동사는_예전_결정(bool mutex, StartupKind expected)
    {
        foreach (var args in new[]
                 {
                     Array.Empty<string>(),
                     new[] { "--open", "있는.zip" },
                     new[] { "--extract-here", "있는.zip" },
                     new[] { "--compress", "x.txt" },
                     new[] { "--compress-to", @"C:\곳", "x.txt" },
                     new[] { "있는.zip" },
                     new[] { "--extract-to", "없는.zip", @"C:\곳" },   // 파일이 없으면 창 없이 풀기가 아님(예전과 같음)
                     new[] { "--compress", "x.txt", "--new-window" },   // 표시는 맨 앞일 때만
                 })
            Assert.Equal(expected, StartupDecision.Decide(args, mutex, Exists));
    }

    /// <summary>'--new-window'·'--elevated' 는 뮤텍스를 얻었든 못 얻었든 따로 뜨는 창(단일 인스턴스·파이프를 건너뜀).</summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void 새_창과_관리자_표시는_늘_따로_뜬다(bool mutex)
    {
        foreach (var args in new[]
                 {
                     new[] { "--new-window" },
                     new[] { "--new-window", "있는.zip" },
                     new[] { "--new-window", "--open", "있는.zip" },
                     new[] { "--elevated", "--extract-to", "있는.zip", @"C:\Program Files\곳" },
                     new[] { "--elevated", "--extract-here", "있는.zip" },
                     new[] { "--elevated", "--compress-to", @"C:\Program Files\곳", "x.txt" },
                     new[] { "--elevated", "--open", "있는.zip" },
                     new[] { "--elevated" },
                 })
        {
            Assert.Equal(StartupKind.Independent, StartupDecision.Decide(args, mutex, Exists));
            Assert.True(StartupDecision.SkipsSingleInstance(args));
        }
        Assert.False(StartupDecision.SkipsSingleInstance(new[] { "--open", "있는.zip" }));
        Assert.False(StartupDecision.SkipsSingleInstance(Array.Empty<string>()));
    }

    /// <summary>
    /// ★ 재사용장치 계약: '--extract-to 압축 폴더'(첫 인자, 대소문자 무시, 파일이 있고 폴더가 빈칸이 아님)는 예전처럼 창 없이 풀기(뮤텍스와 상관없음).
    /// '--elevated' 가 앞에 붙으면 첫 인자가 달라 창 없이 풀기가 아니다(대화형 — 진행 창).
    /// </summary>
    [Fact]
    public void 창_없이_풀기_판정은_예전_그대로이고_elevated가_앞에_붙으면_아니다()
    {
        foreach (bool mutex in new[] { true, false })
        {
            Assert.Equal(StartupKind.Headless, StartupDecision.Decide(new[] { "--extract-to", "있는.zip", @"C:\곳" }, mutex, Exists));
            Assert.Equal(StartupKind.Headless, StartupDecision.Decide(new[] { "--EXTRACT-TO", "있는.zip", @"C:\곳" }, mutex, Exists));
            Assert.Equal(StartupKind.Headless, StartupDecision.Decide(new[] { "--extract-to", "있는.zip", @"C:\곳", "남는 인자" }, mutex, Exists));   // 예전 판정(3개 이상)
            Assert.Equal(StartupKind.Independent, StartupDecision.Decide(new[] { "--elevated", "--extract-to", "있는.zip", @"C:\곳" }, mutex, Exists));
        }
        Assert.False(StartupDecision.IsHeadlessExtract(new[] { "--extract-to", "있는.zip", " " }, Exists));
        Assert.False(StartupDecision.IsHeadlessExtract(new[] { "--extract-to", "있는.zip" }, Exists));
        Assert.False(StartupDecision.IsHeadlessExtract(new[] { "--elevated", "--extract-to", "있는.zip", @"C:\곳" }, Exists));
    }

    /// <summary>
    /// App.OnStartup(소스 확인 — 창을 띄우지 않음): 창 없이 풀기 판정은 StartupDecision 으로 옮겼고(같은 판정), 따로 뜨는 창이면 뮤텍스를 만들지도 파이프 서버를 열지도 않는다
    /// (단일 인스턴스 블록 전체가 SkipsSingleInstance 확인 안에). '--elevated' 창이면 VM 에 표시를 넘긴다.
    /// </summary>
    [Fact]
    public void App은_따로_뜨는_창이면_뮤텍스와_파이프를_건너뛴다()
    {
        string src = RepoFiles.Read("Iljip", "App.xaml.cs");
        int a = src.IndexOf("protected override async void OnStartup", StringComparison.Ordinal);
        int b = src.IndexOf("private static bool IsHeadlessExtractCommand", StringComparison.Ordinal);
        string body = src[a..b];
        int headless = body.IndexOf("IsHeadlessExtractCommand(e.Args)", StringComparison.Ordinal);
        int skip = body.IndexOf("if (!StartupDecision.SkipsSingleInstance(e.Args))", StringComparison.Ordinal);
        int mutex = body.IndexOf("new Mutex(", StringComparison.Ordinal);
        int pipe = body.IndexOf("StartPipeServer()", StringComparison.Ordinal);
        int window = body.IndexOf("new MainWindow()", StringComparison.Ordinal);
        Assert.True(headless >= 0 && skip > headless, "창 없이 풀기 판정 뒤에 단일 인스턴스 건너뛰기 확인이 있어야 함");
        Assert.True(mutex > skip && pipe > mutex && window > pipe, "뮤텍스·파이프는 건너뛰기 확인 안에 있어야 함");
        // 건너뛰기 블록이 창 만들기 전에 닫힌다(뮤텍스·파이프만 감쌈)
        string block = body[skip..window];
        Assert.Equal(block.Count(c => c == '{'), block.Count(c => c == '}'));
        Assert.Contains("MarkElevated(win)", body);

        string headlessMethod = src[b..src.IndexOf("private static async Task<int> RunHeadlessExtractAsync", StringComparison.Ordinal)];
        Assert.Contains("StartupDecision.IsHeadlessExtract(args)", headlessMethod);
    }

    /// <summary>
    /// W2-12: 파일 연결 넣기·빼기('--register-associations [--drop-handler]'·'--unregister-associations')는 뮤텍스와 상관없이 따로(자체 프로세스 — 떠 있는 일집에 넘기지 않음).
    /// 창 없이 풀기 판정은 그대로 먼저. App 은 창 없이 풀기 다음, 단일 인스턴스(뮤텍스) 전에 이것만 처리하고 끝낸다(진짜 HKCU 는 AppServices 기본값으로만).
    /// </summary>
    [Fact]
    public void 파일_연결_명령은_따로_돌고_끝난다()
    {
        foreach (bool mutex in new[] { true, false })
        {
            Assert.Equal(StartupKind.Associations, StartupDecision.Decide(new[] { "--register-associations" }, mutex, Exists));
            Assert.Equal(StartupKind.Associations, StartupDecision.Decide(new[] { "--register-associations", "--drop-handler" }, mutex, Exists));
            Assert.Equal(StartupKind.Associations, StartupDecision.Decide(new[] { "--unregister-associations" }, mutex, Exists));
            Assert.Equal(StartupKind.Associations, StartupDecision.Decide(new[] { "--elevated", "--register-associations" }, mutex, Exists));
            Assert.Equal(mutex ? StartupKind.FirstInstance : StartupKind.Forward, StartupDecision.Decide(new[] { "--extract-smart", "a.zip" }, mutex, Exists));
            Assert.Equal(mutex ? StartupKind.FirstInstance : StartupKind.Forward, StartupDecision.Decide(new[] { "--compress-zip", "a" }, mutex, Exists));
        }
        Assert.True(StartupDecision.SkipsSingleInstance(new[] { "--unregister-associations" }));
        Assert.False(StartupDecision.SkipsSingleInstance(new[] { "--extract-smart", "a.zip" }));

        string src = RepoFiles.Read("Iljip", "App.xaml.cs");
        int a = src.IndexOf("protected override async void OnStartup", StringComparison.Ordinal);
        string body = src[a..];
        int headless = body.IndexOf("IsHeadlessExtractCommand(e.Args)", StringComparison.Ordinal);
        int assoc = body.IndexOf("if (StartupDecision.IsAssociationCommand(e.Args))", StringComparison.Ordinal);
        int mutexAt = body.IndexOf("new Mutex(", StringComparison.Ordinal);
        Assert.True(headless >= 0 && assoc > headless && mutexAt > assoc, "파일 연결 명령은 창 없이 풀기 다음, 뮤텍스 전에");
        string block = body[assoc..mutexAt];
        Assert.Contains("FileAssociationCommand.Run(CommandLineParser.Parse(e.Args), AppServices.CreateDefault(), Environment.ProcessPath)", block);
        Assert.Contains("Shutdown(run?.ExitCode ?? 1);", block);
        Assert.Contains("return;", block);
    }
}
