using System.IO;

namespace Iljip.Services;

/// <summary>
/// 압축 안 항목 이름 → 대상 폴더에 만들 상대 경로(W1-B 2차 검증 지적). 모든 해제 경로(7z.dll·SharpCompress·압축된 tar·EGG/ALZ)가 같이 쓴다.
/// Windows 가 그 이름 그대로는 만들 수 없거나 엉뚱하게 만드는 조각만 바꾸고(7-Zip·반디집과 같은 방식), 나머지는 그대로 둔다:
/// - 파일 이름에 못 쓰는 글자(<c>: * ? " &lt; &gt; |</c>·제어 문자) → '_'. ':' 를 그대로 두면 'time 10:00.log' 가 0바이트 'time 10' 과
///   숨은 데이터 스트림(ADS) '00.log' 로 갈려 내용이 안 보이게 숨었고, '?'·'*' 는 해제를 중간에 멈췄다.
/// - 점·공백만으로 된 조각('...') → 마지막 글자를 '_' 로('.._'). Windows 는 끝의 점·공백을 떼어 버려 그 폴더를 만들 수 없다.
///   ('.'·'..' 는 그대로 — 대상 폴더 밖으로 나가는지는 호출자가 원래 이름으로 따로 막는다.)
/// - 장치 이름(CON·PRN·AUX·NUL·COM1~9·LPT1~9 등, 확장자가 붙어도) → 앞에 '_'('CON.txt' → '_CON.txt'). 그대로 만들면
///   탐색기·PowerShell 로 열거나 지우기 어려운 파일이 생긴다.
/// 이름 끝의 점·공백('dot.')은 Windows 가 떼어 낸 이름('dot')으로 만들어지므로 손대지 않는다(예전과 같음).
/// 화면 목록·선택 비교는 원래 이름을 쓴다 — 바뀌는 것은 디스크에 쓰는 이름뿐이다.
/// W2-09: 맥에서 만든 ZIP 의 한글 이름(NFD — 'ㅂㅗㄱㅗㅅㅓ' 처럼 자모가 풀린 꼴)은 NFC(완성형)로 모아 쓴다(<see cref="EntryNames.Nfc"/>).
/// 화면 목록도 NFC 로 보이므로 화면 이름과 디스크 이름이 같다.
/// </summary>
public static class EntryPaths
{
    private static readonly char[] Invalid = Path.GetInvalidFileNameChars();

    private static readonly HashSet<string> ReservedNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL", "CONIN$", "CONOUT$",
        "COM0", "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9", "COM¹", "COM²", "COM³",
        "LPT0", "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9", "LPT¹", "LPT²", "LPT³",
    };

    /// <summary>항목 이름('/'·'\' 구분) → 대상 폴더 아래에 만들 상대 경로('\' 구분).</summary>
    public static string ToSafeRelative(string entryPath)
    {
        var parts = EntryNames.Nfc(entryPath).Replace('/', '\\').Split('\\');
        for (int i = 0; i < parts.Length; i++)
            parts[i] = SafeComponent(parts[i]);
        return string.Join('\\', parts);
    }

    /// <summary>경로 조각 하나를 Windows 가 그대로 만들 수 있는 이름으로.</summary>
    public static string SafeComponent(string component)
    {
        if (component.Length == 0 || component == "." || component == "..") return component;
        string s = component.IndexOfAny(Invalid) < 0 ? component : string.Concat(component.Select(c => Array.IndexOf(Invalid, c) >= 0 ? '_' : c));
        if (s.TrimEnd('.', ' ').Length == 0) s = s[..^1] + "_";
        if (IsReservedDeviceName(s)) s = "_" + s;
        return s;
    }

    /// <summary>장치 이름인지(확장자 앞부분 기준, 대소문자 무관, 끝 공백 무시 — 'con .txt' 도 장치 이름).</summary>
    public static bool IsReservedDeviceName(string component)
    {
        int dot = component.IndexOf('.');
        string stem = (dot >= 0 ? component[..dot] : component).TrimEnd(' ');
        return ReservedNames.Contains(stem);
    }

    /// <summary>
    /// Windows 가 그 이름 그대로 만들지 못하는 조각(점·공백만으로 된 조각, 장치 이름)이 있는지 — '\'·'/' 구분 상대 경로.
    /// 7z.dll(SharpSevenZip)은 이런 이름을 바꾸지 않고 그대로 쓰려다 실패하거나 다루기 어려운 파일을 만들어서, 이런 항목은 따로 꺼낸다
    /// (<see cref="SevenZipEngine"/>).
    /// </summary>
    public static bool HasUnrepresentableComponent(string relativePath)
    {
        foreach (var c in relativePath.Replace('/', '\\').Split('\\'))
        {
            if (c.Length == 0 || c == "." || c == "..") continue;
            if (c.TrimEnd('.', ' ').Length == 0 || IsReservedDeviceName(c)) return true;
        }
        return false;
    }

    /// <summary>
    /// 해제할 항목의 출력 경로(전체 경로). 원래 이름과 바꾼 이름이 둘 다 대상 폴더 안이어야 한다 — 아니면 경로 공격으로 보고
    /// <see cref="InvalidDataException"/>(<see cref="ErrorMessages.UnsafePathPrefix"/>). 원래 이름으로도 보는 이유: 절대 경로('C:/…')는
    /// ':' 를 '_' 로 바꾸면 안쪽 이름처럼 보여 공격을 놓친다.
    /// </summary>
    public static string ResolveOutput(string destinationFolder, string entryPath)
    {
        string fullDestRoot = Path.GetFullPath(destinationFolder + Path.DirectorySeparatorChar);
        // GetFullPath 가 거절하는 글자는 NUL 하나뿐이다 — 원래 이름 검사에서는 그것만 바꿔 본다(나머지는 원래 모양 그대로 판정).
        string original = Path.GetFullPath(Path.Combine(destinationFolder, entryPath.Replace('\0', '_')));
        string safe = Path.GetFullPath(Path.Combine(destinationFolder, ToSafeRelative(entryPath)));
        if (!original.StartsWith(fullDestRoot, StringComparison.OrdinalIgnoreCase) || !safe.StartsWith(fullDestRoot, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException(ErrorMessages.UnsafePathPrefix + entryPath);
        return safe;
    }
}
