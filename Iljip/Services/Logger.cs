using System;
using System.IO;
using System.Text;

namespace Iljip.Services;

/// <summary>
/// 매우 단순한 파일 로거. 정식 배포 시 구조적 로깅(예: Serilog)으로 교체 가능.
/// 로그는 %LOCALAPPDATA%\Iljip\logs\error-yyyyMMdd.log 에 일자별로 누적된다.
/// 로깅 자체가 앱 동작을 방해하면 안 되므로 모든 예외를 내부에서 삼킨다.
/// </summary>
public static class Logger
{
    private static readonly object _gate = new();

    /// <summary>로그 디렉터리 경로(%LOCALAPPDATA%\Iljip\logs). 실패 시 임시 폴더로 폴백.</summary>
    private static string LogDirectory
    {
        get
        {
            try
            {
                string baseDir = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                return Path.Combine(baseDir, "Iljip", "logs");
            }
            catch
            {
                return Path.Combine(Path.GetTempPath(), "Iljip", "logs");
            }
        }
    }

    /// <summary>예외를 파일에 기록. 실패해도 조용히 무시한다.</summary>
    public static void LogError(string context, Exception ex)
    {
        try
        {
            var sb = new StringBuilder();
            sb.Append('[').Append(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")).Append("] ");
            sb.Append(context).AppendLine();
            sb.AppendLine(ex.ToString());
            sb.AppendLine(new string('-', 60));
            Write(sb.ToString());
        }
        catch
        {
            // 로깅 실패는 무시 (앱 동작에 영향 주지 않음)
        }
    }

    /// <summary>
    /// 다른 일집 프로세스(창 없이 풀기를 부르는 재사용장치·두 번째 창)가 같은 날 로그를 쓰는 중이면 잠깐 막힌다(공유 위반) —
    /// 그 기록을 말없이 잃지 않게 짧게 몇 번 다시 해 본다(3차 통합 점검: 재배포 뒤 창 없이 풀기 실패를 이 로그로 지켜보므로).
    /// </summary>
    private const int WriteAttempts = 5;
    private const int ErrorSharingViolation = unchecked((int)0x80070020);
    private const int ErrorLockViolation = unchecked((int)0x80070021);

    private static void Write(string text)
    {
        lock (_gate)
        {
            string dir = LogDirectory;
            Directory.CreateDirectory(dir);
            string file = Path.Combine(dir, $"error-{DateTime.Now:yyyyMMdd}.log");
            for (int attempt = 1; ; attempt++)
            {
                try
                {
                    File.AppendAllText(file, text, Encoding.UTF8);
                    return;
                }
                catch (IOException io) when (attempt < WriteAttempts && io.HResult is ErrorSharingViolation or ErrorLockViolation)
                {
                    System.Threading.Thread.Sleep(30 * attempt);
                }
            }
        }
    }
}
