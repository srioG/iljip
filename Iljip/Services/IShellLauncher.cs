using System.Diagnostics;
using System.IO;

namespace Iljip.Services;

/// <summary>
/// 파일·폴더를 Windows 기본 프로그램·탐색기로 여는 일(설계 §5). 화면이 아닌 시스템 동작이라 <see cref="IUserInteraction"/> 과 따로 둔다.
/// 진짜는 <see cref="ShellLauncher"/>, 시험은 호출만 기록하는 가짜(Iljip.Tests/TestSupport/FakeShellLauncher) — 시험은 진짜 셸을 절대 부르지 않는다.
/// W2-05 는 <see cref="OpenFolder"/>(우클릭으로 풀고 결과 폴더 열기 — I1-6)를 쓴다. 모양은 W2-04 보관본과 같게(다시 합칠 때 겹치게). 뒤 묶음은 덧붙이기만.
/// </summary>
public interface IShellLauncher
{
    /// <summary>파일을 연결된 기본 프로그램으로 연다(더블클릭과 같음).</summary>
    void OpenFile(string path);

    /// <summary>폴더를 탐색기로 연다. <paramref name="select"/> 면 <paramref name="path"/>(파일·폴더)를 고른 채로 그 부모 폴더를 연다.</summary>
    void OpenFolder(string path, bool select = false);

    // ───── W2-12(파일 연결 I1-1)에서 더한 것 ─────

    /// <summary>
    /// Windows '기본 앱' 설정 화면을 연다(일집 칸으로 — <see cref="ShellLauncher.DefaultAppsUri"/>, 안 되면 기본 앱 첫 화면).
    /// Windows 11 의 기본 앱(UserChoice)은 해시로 보호돼 프로그램이 바꿀 수 없어 사용자가 직접 고르게 한다.
    /// </summary>
    void OpenDefaultAppsSettings();

    /// <summary>파일 연결을 바꿨다고 탐색기에 알린다(SHChangeNotify SHCNE_ASSOCCHANGED — 아이콘·'연결 프로그램' 목록을 다시 읽게).</summary>
    void NotifyAssociationsChanged();
}

/// <summary>진짜 셸(ShellExecute·explorer.exe).</summary>
public sealed class ShellLauncher : IShellLauncher
{
    public void OpenFile(string path)
    {
        using var _ = Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
    }

    public void OpenFolder(string path, bool select = false)
    {
        var psi = new ProcessStartInfo("explorer.exe") { UseShellExecute = false };
        if (select)
            psi.Arguments = $"/select,\"{path}\"";
        else
            psi.Arguments = $"\"{(Directory.Exists(path) ? path : Path.GetDirectoryName(path) ?? path)}\"";
        using var _ = Process.Start(psi);
    }

    /// <summary>'기본 앱' 설정의 일집 칸(RegisteredApplications 값 이름 '일집' — <see cref="FileAssociationRegistrar.AppName"/>).</summary>
    public const string DefaultAppsUri = "ms-settings:defaultapps?registeredAppUser=" + FileAssociationRegistrar.AppName;

    /// <summary>위 칸으로 못 열 때(옛 Windows 등) — 기본 앱 첫 화면.</summary>
    public const string DefaultAppsFallbackUri = "ms-settings:defaultapps";

    public void OpenDefaultAppsSettings()
    {
        try
        {
            using var _ = Process.Start(new ProcessStartInfo(DefaultAppsUri) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Logger.LogError("기본 앱 설정(일집 칸)을 열지 못함 — 첫 화면으로", ex);
            using var _ = Process.Start(new ProcessStartInfo(DefaultAppsFallbackUri) { UseShellExecute = true });
        }
    }

    public void NotifyAssociationsChanged() => SHChangeNotify(SHCNE_ASSOCCHANGED, SHCNF_IDLIST, IntPtr.Zero, IntPtr.Zero);

    private const int SHCNE_ASSOCCHANGED = 0x08000000;
    private const uint SHCNF_IDLIST = 0x0000;

    [System.Runtime.InteropServices.DllImport("shell32.dll")]
    private static extern void SHChangeNotify(int wEventId, uint uFlags, IntPtr dwItem1, IntPtr dwItem2);
}
