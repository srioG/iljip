using System.Diagnostics;
using System.Windows;
using System.Windows.Markup;
using System.Windows.Threading;

namespace Iljip.Tests.TestSupport;

/// <summary>
/// 창을 띄우지 않고 만들기만 하는 XAML 시험(W2-07 검증 지적 2차)의 틀 — <c>MainWindowEditUiTests</c> 와 같은 방법:
/// STA 스레드에서 앱 객체(Application — StaticResource 를 찾으려면 필요)를 잠깐 만들어 테마·스타일 자원을 넣고, 끝나면 Application.Current 를 원래(없음)로 되돌린다.
/// 묶기(Binding) 오류는 <see cref="BindingErrors"/> 에 모인다(WPF 의 데이터 묶기 추적을 켜서 — 오류 수준만).
/// 이 틀을 쓰는 시험은 [Collection(WpfApplicationCollection.Name)] 로 다른 시험과 따로(순차) 돌린다.
/// </summary>
public static class WpfAppScope
{
    public static void Run(Action<BindingErrorLog> body)
    {
        Sta.Run(() =>
        {
            var existing = Application.Current;
            // 앞서 다른 시험(MainWindowEditUiTests 등)이 앱 객체를 만들었다가 되돌렸으면 WPF 는 '한 번 만들었음' 표시 때문에 또 만들지 못한다 — 그 표시도 되돌린다
            if (existing is null) SetAppStatic("_appCreatedInThisAppDomain", false);
            var app = existing ?? new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            bool addedResources = app.Resources.MergedDictionaries.Count == 0;
            if (addedResources)
            {
                app.Resources.MergedDictionaries.Add((ResourceDictionary)XamlReader.Parse(RepoFiles.Read("Iljip", "Resources", "Themes", "Light.xaml")));
                app.Resources.MergedDictionaries.Add((ResourceDictionary)XamlReader.Parse(RepoFiles.Read("Iljip", "Resources", "Styles.xaml")));
            }
            PresentationTraceSources.Refresh();   // 디버거가 없어도 추적을 켠다(설정을 다시 읽으므로 듣는 쪽·수준은 그 뒤에 넣는다)
            var source = PresentationTraceSources.DataBindingSource;
            var oldLevel = source.Switch.Level;
            var log = new BindingErrorLog();
            source.Listeners.Add(log);
            source.Switch.Level = SourceLevels.Error;
            try
            {
                body(log);
            }
            finally
            {
                source.Listeners.Remove(log);
                source.Switch.Level = oldLevel;
                if (existing is null)
                {
                    // 이 스레드가 끝나면 앱 객체의 Dispatcher 도 죽는다 — 남겨 두면 다른 시험의 창 코드가 죽은 Dispatcher 로 넘기다 멈춘다
                    SetAppStatic("_appInstance", null);
                    SetAppStatic("_appCreatedInThisAppDomain", false);
                }
                else if (addedResources)
                {
                    app.Resources.MergedDictionaries.Clear();
                }
            }
        });
    }

    private static void SetAppStatic(string name, object? value)
    {
        var field = typeof(Application).GetField(name, System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        if (field is null) throw new InvalidOperationException($"WPF 내부 이름이 바뀜(Application.{name}) — 앱 객체를 되돌리지 못함");
        field.SetValue(null, value);
    }

    /// <summary>이 스레드의 Dispatcher 에 쌓인 일(늦게 붙는 묶기 등)을 다 처리한다 — 창을 띄우지 않으므로 직접 돌린다.</summary>
    public static void Pump() =>
        Dispatcher.CurrentDispatcher.Invoke(DispatcherPriority.ApplicationIdle, new Action(() => { }));
}

/// <summary>데이터 묶기 오류(예: 'BindingExpression path error') 기록.</summary>
public sealed class BindingErrorLog : TraceListener
{
    private readonly List<string> _errors = new();

    public IReadOnlyList<string> Errors
    {
        get { lock (_errors) return _errors.ToList(); }
    }

    public void Clear()
    {
        lock (_errors) _errors.Clear();
    }

    public override void Write(string? message)
    {
        // 머리(추적 이름)만 온다 — 본문은 WriteLine
    }

    public override void WriteLine(string? message)
    {
        lock (_errors) _errors.Add(message ?? string.Empty);
    }
}
