using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace Iljip.SfxStub;

/// <summary>
/// 일집 자동 풀림 스텁(W2-10, I3-1). 두 가지로 돈다:
/// <list type="bullet">
/// <item>화면 모드(두 번 눌러 실행): '압축 풀 위치' 창(기본 바탕 화면\이름, [찾아보기]) → 암호(걸려 있으면) → 진행 막대 → 같은 이름이면 묻기 → 끝나면 폴더 열기. 문구는 모두 한국어.</item>
/// <item>무인 모드(<c>/s /d:&lt;폴더&gt; [/p:&lt;암호&gt;]</c>): 창·메시지 상자를 절대 띄우지 않는다. 종료 코드 0 성공·1 실패(암호·손상·쓰기)·2 인자 오류,
///   오류는 표준 오류에 UTF-8 한 줄. 같은 이름 파일은 덮어쓴다. 계약 테스트(SfxCreateTests)가 이 모드로 만든 exe 를 실제로 돌려 본다.
///   (지난번에 끊긴 풀기의 원래 파일을 보이는 폴더로 옮겼으면 그 안내를 표준 출력에 한 줄 — 종료 코드는 그대로 0.)</item>
/// </list>
/// </summary>
internal static class Program
{
    public const int ExitOk = 0;
    public const int ExitFailed = 1;
    public const int ExitArguments = 2;

    [STAThread]
    private static int Main(string[] args)
    {
        var parsed = SfxArgs.Parse(args);
        if (parsed.Silent) return RunSilent(parsed);
        return RunGui(parsed);
    }

    // ───────────── 무인 모드 ─────────────

    private static int RunSilent(SfxArgs a)
    {
        // 이 프로세스에서만: 오류 보고(WER) 창·'디스크를 넣으세요' 창을 띄우지 않는다
        try { SetErrorMode(SemFailCriticalErrors | SemNoGpFaultErrorBox | SemNoOpenFileErrorBox); } catch (Exception) { /* 없어도 계속 */ }

        if (a.Error is not null)
        {
            WriteError(a.Error);
            return ExitArguments;
        }
        if (a.Help)
        {
            WriteOut(SfxMessages.Usage);
            return ExitOk;
        }
        if (string.IsNullOrWhiteSpace(a.Folder))
        {
            WriteError(SfxMessages.SilentNeedsFolder);
            return ExitArguments;
        }

        string folder;
        try
        {
            folder = Path.GetFullPath(a.Folder);
        }
        catch (Exception ex) when (ex is ArgumentException || ex is NotSupportedException || ex is PathTooLongException || ex is System.Security.SecurityException)
        {
            WriteError("/d: 의 풀 위치가 올바르지 않아요: " + a.Folder);
            return ExitArguments;
        }

        try
        {
            using var payload = SfxPayload.Open(SfxPayload.SelfPath());
            byte[]? pw = SfxExtractor.ResolvePassword(payload, a.Password, silent: true);
            var outcome = SfxExtractor.Extract(payload, pw, folder, OverwriteAll.Instance, progress: null, CancellationToken.None);
            // 지난번에 끊긴 풀기에서 원래 파일을 보이는 폴더로 옮겼으면 표준 출력에 한 줄(성공은 그대로 0)
            if (outcome.Note is not null) WriteOut(outcome.Note);
            return ExitOk;
        }
        catch (Exception ex)
        {
            WriteError(SfxMessages.For(ex));
            return ExitFailed;
        }
    }

    /// <summary>표준 오류에 UTF-8 한 줄(줄바꿈은 빈칸으로). 표준 오류가 없으면(두 번 눌러 실행) 아무 일도 없다.</summary>
    internal static void WriteError(string message) => WriteLine(Console.OpenStandardError(), message);

    private static void WriteOut(string message) => WriteLine(Console.OpenStandardOutput(), message.Replace("\n", Environment.NewLine));

    private static void WriteLine(Stream s, string message)
    {
        try
        {
            using (s)
            {
                byte[] b = new UTF8Encoding(false).GetBytes(message + Environment.NewLine);
                s.Write(b, 0, b.Length);
                s.Flush();
            }
        }
        catch (Exception)
        {
            // 쓸 곳이 없으면 그만
        }
    }

    // ───────────── 화면 모드 ─────────────

    private static int RunGui(SfxArgs a)
    {
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        Application.ThreadException += (_, e) =>
        {
            MessageBox.Show(SfxMessages.For(e.Exception), SfxMessages.Title, MessageBoxButtons.OK, MessageBoxIcon.Error);
            Environment.Exit(ExitFailed);
        };

        if (a.Error is not null || a.Help)
        {
            string text = a.Error is null ? SfxMessages.Usage : a.Error + "\n\n" + SfxMessages.Usage;
            MessageBox.Show(text, SfxMessages.Title, MessageBoxButtons.OK, a.Error is null ? MessageBoxIcon.Information : MessageBoxIcon.Warning);
            return a.Error is null ? ExitOk : ExitArguments;
        }

        SfxPayload payload;
        try
        {
            payload = SfxPayload.Open(SfxPayload.SelfPath());
        }
        catch (Exception ex)
        {
            MessageBox.Show(SfxMessages.For(ex), SfxMessages.Title, MessageBoxButtons.OK, MessageBoxIcon.Error);
            return ExitFailed;
        }

        using (payload)
        {
            // 이름 검사는 창을 띄우기 전에(안전하지 않은 경로가 있으면 풀 곳을 묻지도 않음)
            try
            {
                SfxExtractor.Plan(payload);
            }
            catch (Exception ex)
            {
                MessageBox.Show(SfxMessages.For(ex), SfxMessages.Title, MessageBoxButtons.OK, MessageBoxIcon.Error);
                return ExitFailed;
            }

            using var form = new ExtractForm(payload, a.Folder, a.Password);
            Application.Run(form);
            return form.ExitCode;
        }
    }

    private const uint SemFailCriticalErrors = 0x0001;
    private const uint SemNoGpFaultErrorBox = 0x0002;
    private const uint SemNoOpenFileErrorBox = 0x8000;

    [DllImport("kernel32.dll")]
    private static extern uint SetErrorMode(uint mode);
}
