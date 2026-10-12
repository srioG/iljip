using System.Diagnostics;

namespace Iljip.Tests.TestSupport;

/// <summary>
/// 이 PC 에 깔린 반디집 콘솔(bz.exe)로 '일집이 만든 압축 파일을 반디집이 푸는지' 시험한다(B3 양방향 호환).
/// ★ 시험(t) 명령만 쓴다 — 파일을 쓰거나 고치는 명령은 쓰지 않는다. 반디집이 없으면(지웠으면) 해당 테스트는 자동으로 건너뛴다.
/// </summary>
public static class BandizipCli
{
    public const string ExePath = @"C:\Program Files\Bandizip\bz.exe";

    public static bool Available => File.Exists(ExePath);

    /// <summary>'bz.exe t -y -p:암호 파일' 의 종료 코드(0 = 모두 맞게 풀림, 14 = 암호 틀림 — 2026-10-12 반디집 7.46 실측).</summary>
    public static async Task<int> TestAsync(string archivePath, string password)
    {
        var psi = new ProcessStartInfo(ExePath)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        psi.ArgumentList.Add("t");
        psi.ArgumentList.Add("-y");
        psi.ArgumentList.Add("-p:" + password);
        psi.ArgumentList.Add(archivePath);
        using var p = Process.Start(psi)!;
        var stdout = p.StandardOutput.ReadToEndAsync();
        var stderr = p.StandardError.ReadToEndAsync();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        try
        {
            await p.WaitForExitAsync(cts.Token);
        }
        catch (OperationCanceledException)
        {
            try { p.Kill(entireProcessTree: true); } catch { /* 이미 끝남 */ }
            Assert.Fail("bz.exe 가 60초 안에 끝나지 않음");
        }
        await Task.WhenAll(stdout, stderr);
        return p.ExitCode;
    }
}

/// <summary>반디집 콘솔(bz.exe)이 있을 때만 도는 Fact.</summary>
public sealed class BandizipCliFactAttribute : FactAttribute
{
    public BandizipCliFactAttribute()
    {
        if (!BandizipCli.Available) Skip = $"반디집 콘솔이 없음({BandizipCli.ExePath}) — 반디집을 지운 PC 에서는 건너뜀";
    }
}

/// <summary>반디집 콘솔(bz.exe)이 있을 때만 도는 Theory(W2-03 — 나눔 ZIP 여러 모양).</summary>
public sealed class BandizipCliTheoryAttribute : TheoryAttribute
{
    public BandizipCliTheoryAttribute()
    {
        if (!BandizipCli.Available) Skip = $"반디집 콘솔이 없음({BandizipCli.ExePath}) — 반디집을 지운 PC 에서는 건너뜀";
    }
}
