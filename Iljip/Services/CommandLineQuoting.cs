using System.Text;

namespace Iljip.Services;

/// <summary>
/// 인자 목록 → 명령줄 한 줄(W2-11 — 새 창·관리자 권한으로 다시 시도가 일집을 다시 띄울 때). Windows 의 해석(CommandLineToArgvW·.NET 시작 코드)이
/// 같은 목록으로 되돌리는 꼴로 싼다: 빈칸·탭·따옴표가 없으면 그대로, 있으면 큰따옴표로 싸고 안의 '"' 는 '\"', 따옴표 바로 앞·끝의 역슬래시는 두 배.
/// 한글·빈칸이 든 경로, 끝이 '\' 인 폴더('C:\풀 곳\'), 따옴표가 든 이름을 시험이 CommandLineToArgvW 로 되읽어 확인한다(ElevationTests).
/// ShellExecuteEx('runas')는 인자를 문자열 하나로만 받아 ProcessStartInfo.ArgumentList 를 쓸 수 없어 직접 싼다.
/// </summary>
public static class CommandLineQuoting
{
    /// <summary>인자들을 빈칸 하나로 이어 붙인 한 줄.</summary>
    public static string Join(IEnumerable<string> args)
    {
        var sb = new StringBuilder();
        foreach (var a in args) Append(sb, a ?? string.Empty);
        return sb.ToString();
    }

    /// <summary>인자 하나를 덧붙인다(앞에 무엇이 있으면 빈칸 하나 뒤에).</summary>
    public static void Append(StringBuilder sb, string arg)
    {
        if (sb.Length != 0) sb.Append(' ');
        if (arg.Length != 0 && !NeedsQuotes(arg))
        {
            sb.Append(arg);
            return;
        }

        sb.Append('"');
        int i = 0;
        while (i < arg.Length)
        {
            char c = arg[i++];
            if (c == '\\')
            {
                int slashes = 1;
                while (i < arg.Length && arg[i] == '\\')
                {
                    i++;
                    slashes++;
                }
                if (i == arg.Length)
                {
                    sb.Append('\\', slashes * 2);   // 닫는 따옴표 앞 — 두 배로(아니면 따옴표가 글자가 됨)
                }
                else if (arg[i] == '"')
                {
                    sb.Append('\\', slashes * 2 + 1);   // 글자 '"' 앞 — 두 배 + 따옴표 이스케이프
                    sb.Append('"');
                    i++;
                }
                else
                {
                    sb.Append('\\', slashes);   // 그 밖은 그대로
                }
                continue;
            }
            if (c == '"')
            {
                sb.Append('\\').Append('"');
                continue;
            }
            sb.Append(c);
        }
        sb.Append('"');
    }

    private static bool NeedsQuotes(string arg)
    {
        foreach (char c in arg)
            if (char.IsWhiteSpace(c) || c == '"') return true;
        return false;
    }
}
