namespace Iljip.Tests.TestSupport;

/// <summary>
/// MainViewModel 호출을 시간 제한(60초)을 걸고 스레드 풀에서 부른다. 화면 코드가 바뀌어 실제 창(MessageBox·폴더 선택 등)으로 빠지면
/// 테스트 호스트가 영영 멈추므로, 기다리다 시간이 넘으면 실패로 끝낸다(떠 버린 창은 테스트 프로세스가 끝날 때 같이 닫힌다).
/// 가짜 창(<see cref="FakeUserInteraction"/>)을 넣은 VM 은 창을 띄우지 않지만, 같은 안전장치를 쓴다.
/// </summary>
public static class VmRunner
{
    public static async Task Run(Func<Task> action, string what)
    {
        var run = Task.Run(action);
        try
        {
            await run.WaitAsync(TimeSpan.FromSeconds(60));
        }
        catch (TimeoutException)
        {
            Assert.Fail($"{what} 가 60초 안에 안 끝남 — 창이 떠서 멈춘 것으로 보임");
        }
    }
}
