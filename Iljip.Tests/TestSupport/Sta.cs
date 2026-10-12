namespace Iljip.Tests.TestSupport;

/// <summary>WPF 요소·셸(COM) 항목처럼 STA 스레드가 필요한 일을 따로 만든 STA 스레드에서 돌린다(테스트 스레드는 MTA). 창은 띄우지 않는다.</summary>
public static class Sta
{
    public static void Run(Action action)
    {
        Exception? error = null;
        var t = new Thread(() =>
        {
            try { action(); }
            catch (Exception ex) { error = ex; }
        });
        t.SetApartmentState(ApartmentState.STA);
        t.Start();
        t.Join();
        if (error is not null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(error).Throw();
    }
}
