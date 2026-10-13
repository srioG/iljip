using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Security.Principal;

namespace Iljip.Services;

/// <summary>
/// 일집을 하나 더 띄우는 일(설계 §5 — W2-11): '새 창'(I3-15)과 '관리자 권한으로 다시 시도'(I3-16). 화면이 아닌 시스템 동작이라 따로 둔다.
/// 진짜는 <see cref="ProcessLauncher"/>, 시험은 호출만 기록하는 가짜(Iljip.Tests/TestSupport/FakeProcessLauncher) — 시험은 진짜 프로세스·UAC(runas)를 절대 부르지 않는다.
/// 인자는 목록으로 받고 따옴표 싸기는 <see cref="CommandLineQuoting"/>. 암호는 어떤 인자에도 넣지 않는다(부르는 쪽 약속 — ElevationService).
/// </summary>
public interface IProcessLauncher
{
    /// <summary>지금 일집 실행 파일 경로(새로 띄울 때 씀). 모르면 null.</summary>
    string? ExecutablePath { get; }

    /// <summary>이 일집이 이미 관리자 권한으로 돌고 있는가(그러면 '관리자 권한으로 다시 시도'를 권하지 않는다).</summary>
    bool IsCurrentProcessElevated { get; }

    /// <summary>같은 권한으로 일집을 하나 더 띄운다(새 창 — '--new-window' 를 넣는 것은 부르는 쪽).</summary>
    void StartNewInstance(string exe, IReadOnlyList<string> args);

    /// <summary>
    /// 관리자 권한으로 띄운다(UAC 창이 뜸). 사용자가 UAC 창에서 [아니요]를 누르면 <see cref="Win32Exception"/>(NativeErrorCode 1223)을 던진다 —
    /// 부르는 쪽이 조용히 넘긴다(오류 창 없음).
    /// </summary>
    void StartElevated(string exe, IReadOnlyList<string> args);
}

/// <summary>진짜: Process.Start(새 창), ShellExecuteEx 'runas'(관리자 권한).</summary>
public sealed class ProcessLauncher : IProcessLauncher
{
    /// <summary>UAC 창에서 [아니요] — ERROR_CANCELLED.</summary>
    public const int ErrorCancelled = 1223;

    public string? ExecutablePath => Environment.ProcessPath;

    /// <summary>프로세스 권한은 도는 동안 바뀌지 않는다 — 한 번만 본다(창 제목이 바뀔 때마다 묻지 않게).</summary>
    private readonly Lazy<bool> _elevated = new(() =>
    {
        try
        {
            using var id = WindowsIdentity.GetCurrent();
            return new WindowsPrincipal(id).IsInRole(WindowsBuiltInRole.Administrator);
        }
        catch
        {
            return false;   // 모르면 권한 상승을 권할 수 있게(권해도 UAC 창이 판단)
        }
    });

    public bool IsCurrentProcessElevated => _elevated.Value;

    public void StartNewInstance(string exe, IReadOnlyList<string> args)
    {
        var psi = new ProcessStartInfo(exe)
        {
            UseShellExecute = false,
            Arguments = CommandLineQuoting.Join(args),
            WorkingDirectory = Path.GetDirectoryName(exe) ?? string.Empty,
        };
        using var _ = Process.Start(psi);
    }

    public void StartElevated(string exe, IReadOnlyList<string> args)
    {
        // runas 는 ShellExecuteEx 라 인자를 문자열 하나로만 넘긴다(ArgumentList 불가). 관리자 쪽 작업 폴더는 대개 System32 라 경로는 모두 전체 경로로 넘긴다(ElevationService).
        var psi = new ProcessStartInfo(exe)
        {
            UseShellExecute = true,
            Verb = "runas",
            Arguments = CommandLineQuoting.Join(args),
            WorkingDirectory = Path.GetDirectoryName(exe) ?? string.Empty,
        };
        using var _ = Process.Start(psi);
    }
}
