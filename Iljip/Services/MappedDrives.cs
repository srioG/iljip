using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace Iljip.Services;

/// <summary>
/// 네트워크 드라이브 문자(Z: 등) → UNC(\\서버\공유)(W2-11 — '관리자 권한으로 다시 시도'). 사용자가 연결한 네트워크 드라이브는 UAC 로 올린
/// 관리자 일집에는 보이지 않으므로(로그인 세션이 다름) 경로를 UNC 로 바꿔 넘긴다. 진짜는 <see cref="MappedDriveResolver"/>(WNetGetConnection),
/// 시험은 가짜 매핑(Iljip.Tests/TestSupport/FakeMappedDriveResolver).
/// </summary>
public interface IMappedDriveResolver
{
    /// <summary>
    /// <paramref name="fullPath"/> 가 연결된 네트워크 드라이브 위면 같은 곳의 UNC 경로, 아니면 그대로. 네트워크 드라이브인데 UNC 를 알 수 없으면 null.
    /// </summary>
    string? ToUncIfMapped(string fullPath);
}

/// <summary>진짜: 드라이브 종류(GetDriveType — 네트워크를 거치지 않음)를 보고, 네트워크 드라이브면 WNetGetConnection 으로 연결된 공유 이름을 묻는다.</summary>
public sealed class MappedDriveResolver : IMappedDriveResolver
{
    private const int NoError = 0;
    private const int ErrorMoreData = 234;
    private const int ErrorConnectionUnavail = 1201;   // 기억된 연결(지금은 끊김) — 이름은 알려 준다

    public string? ToUncIfMapped(string fullPath)
    {
        if (string.IsNullOrEmpty(fullPath) || fullPath.Length < 2 || fullPath[1] != ':' || !char.IsAsciiLetter(fullPath[0])) return fullPath;
        string drive = fullPath[..2];
        try
        {
            if (new DriveInfo(drive).DriveType != DriveType.Network) return fullPath;
        }
        catch
        {
            return fullPath;
        }
        string? remote = Remote(drive);
        if (string.IsNullOrEmpty(remote)) return null;
        string rest = fullPath.Length > 2 ? fullPath[2..] : string.Empty;
        return remote.TrimEnd('\\') + (rest.Length == 0 ? "\\" : rest.StartsWith('\\') ? rest : "\\" + rest);
    }

    private static string? Remote(string drive)
    {
        int length = 512;
        for (int attempt = 0; attempt < 2; attempt++)
        {
            var sb = new StringBuilder(length);
            int rc = WNetGetConnection(drive, sb, ref length);
            if (rc is NoError or ErrorConnectionUnavail) return sb.ToString();
            if (rc != ErrorMoreData) return null;
        }
        return null;
    }

    [DllImport("mpr.dll", CharSet = CharSet.Unicode, EntryPoint = "WNetGetConnectionW")]
    private static extern int WNetGetConnection(string lpLocalName, StringBuilder lpRemoteName, ref int lpnLength);
}
