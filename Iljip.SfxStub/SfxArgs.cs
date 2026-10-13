using System;

namespace Iljip.SfxStub;

/// <summary>
/// 명령줄: <c>/s</c>(무인 — 창 없음) <c>/d:&lt;폴더&gt;</c>(풀 위치) <c>/p:&lt;암호&gt;</c>(암호) <c>/?</c>(도움말). 앞 글자는 '/'·'-' 둘 다, 대소문자 무시.
/// 같은 인자를 두 번 주면 뒤의 것. 모르는 인자는 오류(종료 코드 2).
/// </summary>
internal sealed class SfxArgs
{
    public bool Silent { get; private set; }

    public string? Folder { get; private set; }

    public string? Password { get; private set; }

    public bool Help { get; private set; }

    /// <summary>인자가 틀렸으면 그 까닭(쉬운 한국어). 맞으면 null.</summary>
    public string? Error { get; private set; }

    public static SfxArgs Parse(string[] args)
    {
        var r = new SfxArgs();
        foreach (string raw in args)
        {
            string a = raw ?? string.Empty;
            if (a.Length < 2 || (a[0] != '/' && a[0] != '-'))
            {
                r.Error ??= SfxMessages.UnknownArgument(a);
                continue;
            }
            string body = a.Substring(1);
            if (Is(body, "s"))
            {
                r.Silent = true;
            }
            else if (Is(body, "?") || Is(body, "h") || Is(body, "help"))
            {
                r.Help = true;
            }
            else if (HasValue(body, "d", out string folder))
            {
                folder = folder.Trim().Trim('"').Trim();
                if (folder.Length == 0) r.Error ??= "/d: 뒤에 풀 폴더를 넣어 주세요.";
                else r.Folder = folder;
            }
            else if (HasValue(body, "p", out string password))
            {
                r.Password = password.Length == 0 ? null : password;
            }
            else
            {
                r.Error ??= SfxMessages.UnknownArgument(a);
            }
        }
        return r;
    }

    private static bool Is(string body, string name) => string.Equals(body, name, StringComparison.OrdinalIgnoreCase);

    private static bool HasValue(string body, string name, out string value)
    {
        value = string.Empty;
        if (body.Length < name.Length + 1 || !body.StartsWith(name, StringComparison.OrdinalIgnoreCase) || body[name.Length] != ':') return false;
        value = body.Substring(name.Length + 1);
        return true;
    }
}
