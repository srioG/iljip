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
}
