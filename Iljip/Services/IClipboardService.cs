using System.Collections.Specialized;
using System.IO;
using System.Windows;

namespace Iljip.Services;

/// <summary>
/// 클립보드(설계 §5 — W2-04 Ctrl+C). 시험은 기록만 하는 가짜(TestSupport/FakeClipboard) — 진짜 클립보드를 절대 쓰지 않는다.
/// </summary>
public interface IClipboardService
{
    /// <summary>
    /// 파일 목록을 클립보드에 넣는다(CF_HDROP — 탐색기에서 붙여넣기). <paramref name="effect"/> 는 'Preferred DropEffect'
    /// (복사 = <see cref="DragDropEffects.Copy"/> — 붙여넣으면 복사, 원본(임시 폴더)은 그대로).
    /// </summary>
    void SetFileDrop(IReadOnlyList<string> paths, DragDropEffects effect);
}

/// <summary>진짜 클립보드. WPF 클립보드는 STA 스레드에서만 되므로 UI 스레드(없으면 따로 만든 STA 스레드)에서 넣는다.</summary>
public sealed class ClipboardService : IClipboardService
{
    public void SetFileDrop(IReadOnlyList<string> paths, DragDropEffects effect)
    {
        var data = BuildFileDropData(paths, effect);
        // copy: true — 일집을 꺼도 붙여넣기가 되게(OleFlushClipboard). WPF 가 '클립보드를 못 엶'을 몇 번 다시 해 본다.
        RunOnSta(() => Clipboard.SetDataObject(data, copy: true));
    }

    /// <summary>
    /// 클립보드에 넣을 데이터: FileDrop(CF_HDROP) + 'Preferred DropEffect'(4바이트 DWORD — 복사면 01 00 00 00).
    /// 시험이 클립보드 없이 모양을 확인할 수 있게 따로 뺐다.
    /// </summary>
    internal static DataObject BuildFileDropData(IReadOnlyList<string> paths, DragDropEffects effect)
    {
        var list = new StringCollection();
        foreach (var p in paths) list.Add(p);
        var data = new DataObject();
        data.SetFileDropList(list);
        data.SetData(PreferredDropEffectFormat, new MemoryStream(BitConverter.GetBytes((int)effect)));
        return data;
    }

    /// <summary>CFSTR_PREFERREDDROPEFFECT.</summary>
    internal const string PreferredDropEffectFormat = "Preferred DropEffect";

    private static void RunOnSta(Action action)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is not null)
        {
            if (dispatcher.CheckAccess()) action();
            else dispatcher.Invoke(action);
            return;
        }
        if (Thread.CurrentThread.GetApartmentState() == ApartmentState.STA)
        {
            action();
            return;
        }
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
