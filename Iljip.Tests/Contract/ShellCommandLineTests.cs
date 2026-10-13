using System.Runtime.InteropServices;
using System.Text;
using Iljip.Services;
using Iljip.Tests.TestSupport;

namespace Iljip.Tests.Contract;

/// <summary>
/// [W2-12 I1-2] 셸 확장이 일집을 부르는 명령줄(<see cref="ShellCommandLine"/> — C++ IljipLaunch.cpp 의 C# 거울):
/// 늘 큰따옴표·닫는 따옴표 앞 역슬래시 두 배(드라이브 뿌리 'C:\'·끝이 '\' 인 폴더), 30,000자가 넘으면 '--list-file 목록파일'(일집 임시 폴더 temp\args\*.lst).
/// Windows 가 실제로 하는 해석(CommandLineToArgvW — 셸·창을 부르지 않는 순수 함수)으로 되읽어 확인한다.
/// 목록 파일은 처리하는 일집이 해석 전에 읽고 지운다(<see cref="CommandLineParser.ExpandListFile"/> — App.OnStartup·OnSecondInstanceLaunched).
/// </summary>
public class ShellCommandLineTests
{
    private const string Exe = @"C:\Program Files\일집\Iljip.exe";

    [DllImport("shell32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr CommandLineToArgvW(string lpCmdLine, out int pNumArgs);

    [DllImport("kernel32.dll")]
    private static extern IntPtr LocalFree(IntPtr hMem);

    /// <summary>Windows 가 실제로 하는 해석(실행 파일 이름 포함 전부).</summary>
    private static string[] ParseLikeWindows(string line)
    {
        IntPtr argv = CommandLineToArgvW(line, out int n);
        Assert.NotEqual(IntPtr.Zero, argv);
        try
        {
            var result = new string[n];
            for (int i = 0; i < n; i++) result[i] = Marshal.PtrToStringUni(Marshal.ReadIntPtr(argv, i * IntPtr.Size))!;
            return result;
        }
        finally
        {
            LocalFree(argv);
        }
    }

    /// <summary>한글·빈칸·드라이브 뿌리·끝 역슬래시·UNC·따옴표 — 되읽으면 [exe, 동사, 앞 인자…, 경로…] 그대로.</summary>
    [Fact]
    public void 되읽으면_경로가_그대로다()
    {
        var leading = new[] { @"D:\" };
        var paths = new[]
        {
            @"C:\Users\홍길동\받은 파일\보고서 2026.zip",
            @"D:\끝이 역슬래시인 폴더\",
            @"E:\",
            @"\\서버\공유 폴더\자료.7z",
            @"C:\a\\",
            @"C:\평범한경로\a.zip",
            "빈칸 끝 ",
        };
        string line = ShellCommandLine.Build(Exe, CommandLineParser.ExtractToFolderInKeyword, leading, paths, _ => throw new InvalidOperationException("짧은데 목록 파일을 만듦"));
        Assert.Equal(new[] { Exe, CommandLineParser.ExtractToFolderInKeyword }.Concat(leading).Concat(paths).ToArray(), ParseLikeWindows(line));

        // 그대로 해석하면 같은 명령(셸 확장이 넘긴 키워드를 일집이 알아봄)
        var cmd = CommandLineParser.Parse(ParseLikeWindows(line)[1..]);
        Assert.Equal(CommandVerb.ExtractToFolderIn, cmd.Verb);
        Assert.Equal(@"D:\", cmd.Dest);
        Assert.Equal(paths, cmd.Archives);
    }

    /// <summary>글자 그대로: 늘 큰따옴표, 닫는 따옴표 앞 역슬래시는 두 배(C++ QuoteCommandArg 와 같은 규칙 — 소스 대조는 ShellExtensionContractTests).</summary>
    [Fact]
    public void 명령줄_모양이_정해져_있다()
    {
        Assert.Equal("\"C:\\Program Files\\일집\\Iljip.exe\" --compress-to \"D:\\받은 곳\\\\\" \"C:\\a b\\c.txt\" \"E:\\\\\"",
            ShellCommandLine.Build(Exe, "--compress-to", new[] { @"D:\받은 곳\" }, new[] { @"C:\a b\c.txt", @"E:\" }));
        Assert.Equal("\"\"", ShellCommandLine.Quote(""));
        Assert.Equal("\"a\\\\\\\"b\"", ShellCommandLine.Quote("a\\\"b"));   // a\"b → "a\\\"b"
        Assert.Equal("\"C:\\\\\"", ShellCommandLine.Quote(@"C:\"));
    }

    /// <summary>경계: 실행 파일 포함 정확히 30,000자면 명령줄에 그대로, 한 글자 넘으면 목록 파일 하나(앞 인자는 늘 명령줄에).</summary>
    [Fact]
    public void 삼만_자를_넘을_때만_목록_파일()
    {
        Assert.Equal(30_000, ShellCommandLine.MaxChars);
        string head = ShellCommandLine.Quote(Exe) + " " + CommandLineParser.ExtractHereKeyword;
        int fit = ShellCommandLine.MaxChars - head.Length - 3;   // ' "' + 경로 + '"'
        string exact = @"C:\" + new string('가', fit - 3);
        int calls = 0;
        string inline = ShellCommandLine.Build(Exe, CommandLineParser.ExtractHereKeyword, Array.Empty<string>(), new[] { exact }, _ => { calls++; return "x"; });
        Assert.Equal(ShellCommandLine.MaxChars, inline.Length);
        Assert.Equal(0, calls);
        Assert.Equal(exact, ParseLikeWindows(inline)[2]);

        string over = exact + "나";
        IReadOnlyList<string>? written = null;
        string listed = ShellCommandLine.Build(Exe, CommandLineParser.CompressToKeyword, new[] { @"D:\만들 곳\" }, new[] { over },
            p => { calls++; written = p; return @"C:\임시\목록 파일.lst"; });
        Assert.Equal(1, calls);
        Assert.Equal(new[] { over }, written);
        Assert.Equal(new[] { Exe, CommandLineParser.CompressToKeyword, @"D:\만들 곳\", CommandLineParser.ListFileKeyword, @"C:\임시\목록 파일.lst" }, ParseLikeWindows(listed));
    }

    /// <summary>
    /// 아주 많이 골랐을 때(800개 — 명령줄 4만 자 넘음): 목록 파일은 일집 임시 폴더 temp\args\&lt;guid&gt;.lst, UTF-8(BOM 없음)·CRLF·한 줄에 하나.
    /// 처리하는 일집이 ExpandListFile 로 원래 경로들로 바꾸고 파일을 지운다 — 해석하면 800개 모두.
    /// </summary>
    [Fact]
    public void 많이_고르면_목록_파일로_넘기고_읽은_뒤_지운다()
    {
        var paths = Enumerable.Range(1, 800).Select(i => $@"C:\Users\홍길동\받은 파일\아주 긴 폴더 이름이 있는 곳\압축 파일 {i:D4} — 보고서 사본.zip").ToList();
        string line = ShellCommandLine.Build(Exe, CommandLineParser.ExtractSmartKeyword, Array.Empty<string>(), paths);
        Assert.True(line.Length < 1000, "목록 파일로 넘기지 않음");
        var argv = ParseLikeWindows(line);
        Assert.Equal(4, argv.Length);
        Assert.Equal(CommandLineParser.ListFileKeyword, argv[2]);
        string file = argv[3];
        Assert.Equal(Path.GetFullPath(ShellCommandLine.ListFileFolder), Path.GetDirectoryName(file));
        Assert.Matches(@"^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}\.lst$", Path.GetFileName(file));
        Assert.True(ShellCommandLine.IsOwnListFile(file));
        byte[] bytes = File.ReadAllBytes(file);
        Assert.False(bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF, "BOM 이 있음");
        Assert.Equal(string.Concat(paths.Select(p => p + "\r\n")), new UTF8Encoding(false).GetString(bytes));

        string[] expanded = CommandLineParser.ExpandListFile(argv[1..]);
        Assert.Equal(new[] { CommandLineParser.ExtractSmartKeyword }.Concat(paths).ToArray(), expanded);
        Assert.False(File.Exists(file), "읽은 목록 파일을 지우지 않음");
        var cmd = CommandLineParser.Parse(expanded);
        Assert.Equal(CommandVerb.ExtractSmart, cmd.Verb);
        Assert.Equal(paths, cmd.Archives);
    }

    /// <summary>일집 임시 폴더 밖의 파일(남의 파일)·하위 폴더의 .lst·다른 확장자는 읽기만 하고 지우지 않는다. BOM·LF·빈 줄도 된다. 뒤 인자는 그대로.</summary>
    [Fact]
    public void 남의_목록_파일은_지우지_않는다()
    {
        using var tmp = new TempDir("lst");
        string foreign = tmp.P("목록.lst");
        File.WriteAllText(foreign, "C:\\가.zip\n\r\nC:\\나 다.zip\r\n\r\n", new UTF8Encoding(true));
        Assert.False(ShellCommandLine.IsOwnListFile(foreign));
        Assert.Equal(new[] { "--compress", @"C:\가.zip", @"C:\나 다.zip", "뒤" }, CommandLineParser.ExpandListFile(new[] { "--compress", "--list-file", foreign, "뒤" }));
        Assert.True(File.Exists(foreign));

        string nested = Path.Combine(ShellCommandLine.ListFileFolder, "하위", "x.lst");
        Directory.CreateDirectory(Path.GetDirectoryName(nested)!);
        File.WriteAllText(nested, "C:\\a.zip\r\n");
        string txt = Path.Combine(ShellCommandLine.ListFileFolder, Guid.NewGuid().ToString("D") + ".txt");
        File.WriteAllText(txt, "C:\\b.zip\r\n");
        try
        {
            Assert.False(ShellCommandLine.IsOwnListFile(nested));
            Assert.False(ShellCommandLine.IsOwnListFile(txt));
            CommandLineParser.ExpandListFile(new[] { "--open", "--list-file", nested });
            CommandLineParser.ExpandListFile(new[] { "--open", "--list-file", txt });
            Assert.True(File.Exists(nested));
            Assert.True(File.Exists(txt));
        }
        finally
        {
            File.Delete(nested);
            File.Delete(txt);
        }
    }

    /// <summary>목록 파일이 그새 없어졌으면 그 두 인자만 빠져 해석은 None(아무것도 안 함). '--list-file' 이 없으면 그대로(같은 배열), 마지막이면 표시만 빠짐.</summary>
    [Fact]
    public void 목록_파일이_없으면_아무것도_안_한다()
    {
        string missing = Path.Combine(ShellCommandLine.ListFileFolder, Guid.NewGuid().ToString("D") + ".lst");
        var expanded = CommandLineParser.ExpandListFile(new[] { "--extract-here", "--list-file", missing });
        Assert.Equal(new[] { "--extract-here" }, expanded);
        Assert.Equal(CommandVerb.None, CommandLineParser.Parse(expanded).Verb);

        var plain = new[] { "--open", @"C:\a.zip" };
        Assert.Same(plain, CommandLineParser.ExpandListFile(plain));
        Assert.Equal(new[] { "--compress" }, CommandLineParser.ExpandListFile(new[] { "--compress", "--list-file" }));
        Assert.Empty(CommandLineParser.ExpandListFile(null));
        Assert.DoesNotContain(CommandLineParser.ListFileKeyword, CommandLineParser.Keywords);   // 동사가 아니라 바꿔 넣기 표시
    }

    /// <summary>App: 처리하는 일집이 해석 전에 목록 파일을 펼친다 — 첫 일집(OnStartup)과 넘겨받은 일집(OnSecondInstanceLaunched) 둘 다.</summary>
    [Fact]
    public void 앱이_해석_전에_목록_파일을_펼친다()
    {
        string app = RepoFiles.Read("Iljip", "App.xaml.cs");
        Assert.Contains("string[] startArgs = CommandLineParser.ExpandListFile(e.Args);", app);
        int second = app.IndexOf("private async Task OnSecondInstanceLaunched(string[] args)", StringComparison.Ordinal);
        Assert.True(second > 0);
        int expand = app.IndexOf("args = CommandLineParser.ExpandListFile(args);", second, StringComparison.Ordinal);
        int parse = app.IndexOf("CommandLineParser.Parse(args)", second, StringComparison.Ordinal);
        Assert.True(expand > second && expand < parse, "넘겨받은 인자를 해석하기 전에 펼치지 않음");
        // 펼친 인자로 창을 띄울지·창 없이 처리할지 정한다
        int startExpand = app.IndexOf("string[] startArgs", StringComparison.Ordinal);
        Assert.Contains("CommandLineParser.Parse(startArgs).ShowMainWindow", app[startExpand..]);
    }
}
