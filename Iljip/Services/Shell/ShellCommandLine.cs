using System.IO;
using System.Text;

namespace Iljip.Services;

/// <summary>
/// 셸 확장(C++ IljipLaunch.cpp <c>LaunchIljip</c>)이 Iljip.exe 를 부르는 명령줄 규칙의 C# 거울(W2-12). C++ 는 이 코드를 쓰지 않는다 —
/// 둘을 같게 지키는 것은 계약 시험(ShellExtensionContractTests 의 소스 대조, ShellCommandLineTests 의 CommandLineToArgvW 되읽기).
/// <list type="bullet">
/// <item>모양: <c>"&lt;exe&gt;" &lt;동사&gt; ["앞 인자"…] "경로"…</c> — 앞 인자·경로는 늘 큰따옴표, 닫는 따옴표 바로 앞의 역슬래시는 두 배
///   ('C:\' 같은 드라이브 뿌리·끝이 '\' 인 폴더가 다음 인자를 먹지 않게). 한글·빈칸 그대로.</item>
/// <item>전체 길이가 <see cref="MaxChars"/>(30,000자 — Windows 명령줄 한도 32,767자보다 여유)를 넘으면 경로들을
///   <c>%LOCALAPPDATA%\Iljip\temp\args\&lt;guid&gt;.lst</c>(UTF-8, 한 줄에 한 경로)에 쓰고 <c>--list-file "&lt;그 파일&gt;"</c> 를 넘긴다.
///   일집은 읽고 지운다(<see cref="CommandLineParser.ExpandListFile"/>). 앞 인자(--compress-to 의 만들 폴더 등)는 늘 명령줄에.</item>
/// </list>
/// </summary>
public static class ShellCommandLine
{
    /// <summary>명령줄 전체(실행 파일 포함) 최대 글자 수 — C++ <c>MaxCommandLineChars</c> 와 같은 값.</summary>
    public const int MaxChars = 30_000;

    /// <summary>긴 목록 파일을 두는 곳(<c>%LOCALAPPDATA%\Iljip\temp\args</c> — 설계 §3). 시험은 AppPaths 를 바꿔 임시 폴더.</summary>
    public static string ListFileFolder => Path.Combine(AppPaths.TempRoot, "args");

    /// <summary>목록 파일 확장자.</summary>
    public const string ListFileExtension = ".lst";

    /// <summary>C++ QuoteArg 와 같은 규칙: 늘 큰따옴표로 싸고, 안의 '"' 는 '\"', 따옴표 바로 앞(끝 포함)의 역슬래시는 두 배.</summary>
    public static string Quote(string arg)
    {
        var sb = new StringBuilder(arg.Length + 2);
        sb.Append('"');
        int slashes = 0;
        foreach (char c in arg)
        {
            if (c == '\\')
            {
                slashes++;
                continue;
            }
            if (c == '"')
            {
                sb.Append('\\', slashes * 2 + 1).Append('"');
                slashes = 0;
                continue;
            }
            sb.Append('\\', slashes).Append(c);
            slashes = 0;
        }
        sb.Append('\\', slashes * 2).Append('"');
        return sb.ToString();
    }

    /// <summary>
    /// 명령줄 한 줄. 길면 <paramref name="writeListFile"/>(기본 <see cref="WriteListFile"/>)로 경로를 파일에 쓰고 '--list-file' 로 넘긴다.
    /// </summary>
    public static string Build(string exePath, string verb, IReadOnlyList<string> leading, IReadOnlyList<string> paths,
        Func<IReadOnlyList<string>, string>? writeListFile = null)
    {
        var head = new StringBuilder();
        head.Append(Quote(exePath)).Append(' ').Append(verb);
        foreach (var a in leading) head.Append(' ').Append(Quote(a));

        var tail = new StringBuilder();
        foreach (var p in paths) tail.Append(' ').Append(Quote(p));

        if (head.Length + tail.Length <= MaxChars) return head.Append(tail).ToString();

        string list = (writeListFile ?? WriteListFile)(paths);
        return head.Append(' ').Append(CommandLineParser.ListFileKeyword).Append(' ').Append(Quote(list)).ToString();
    }

    /// <summary>경로 목록 파일을 만든다(UTF-8 BOM 없음, 줄 끝 CRLF). 만든 파일 경로.</summary>
    public static string WriteListFile(IReadOnlyList<string> paths)
    {
        Directory.CreateDirectory(ListFileFolder);
        string file = Path.Combine(ListFileFolder, Guid.NewGuid().ToString("D") + ListFileExtension);
        var sb = new StringBuilder();
        foreach (var p in paths) sb.Append(p).Append("\r\n");
        using (var fs = new FileStream(file, FileMode.CreateNew, FileAccess.Write, FileShare.None))
        {
            byte[] bytes = new UTF8Encoding(false).GetBytes(sb.ToString());
            fs.Write(bytes, 0, bytes.Length);
        }
        return file;
    }

    /// <summary><paramref name="path"/> 가 일집이 만든 목록 파일인가(일집 임시 폴더 temp\args 안의 .lst) — 이것만 읽은 뒤 지운다.</summary>
    public static bool IsOwnListFile(string path)
    {
        try
        {
            string root = Path.GetFullPath(ListFileFolder).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            string full = Path.GetFullPath(path);
            return full.StartsWith(root, StringComparison.OrdinalIgnoreCase)
                   && string.Equals(Path.GetExtension(full), ListFileExtension, StringComparison.OrdinalIgnoreCase)
                   && Path.GetDirectoryName(full)!.Length == root.Length - 1;
        }
        catch
        {
            return false;
        }
    }
}
