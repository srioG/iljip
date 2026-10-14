using System.Runtime.ExceptionServices;
using System.Windows.Threading;

namespace Iljip.Tests.TestSupport;

/// <summary>
/// 화면 스레드 흉내(W2-04 1차 검증): WPF 디스패처가 도는 STA 스레드를 따로 만들어 그 위에서 비동기 일을 돌린다 — await 뒤에 같은 스레드로 돌아오는 것까지
/// 진짜 화면과 같다(창은 띄우지 않음). 화면 스레드를 오래 붙잡는 일(큰 파일 해시 등)이 뒤 스레드로 갔는지 볼 때 쓴다.
/// </summary>
public static class DispatcherThread
{
    public static void Run(Func<Task> action)
    {
        Sta.Run(() =>
        {
            var dispatcher = Dispatcher.CurrentDispatcher;
            SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(dispatcher));
            var frame = new DispatcherFrame();
            Exception? error = null;
            dispatcher.InvokeAsync(async () =>
            {
                try
                {
                    await action();
                }
                catch (Exception ex)
                {
                    error = ex;
                }
                finally
                {
                    frame.Continue = false;
                }
            });
            Dispatcher.PushFrame(frame);
            dispatcher.InvokeShutdown();
            if (error is not null) ExceptionDispatchInfo.Capture(error).Throw();
        });
    }
}
