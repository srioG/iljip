using System.Windows;
using System.Windows.Media;
using Iljip.Models;
using Iljip.Services;
using Microsoft.Win32;

namespace Iljip.Views;

/// <summary>
/// 실제 앱의 창들(<see cref="IUserInteraction"/> 구현). 소유 창은 그때그때의 MainWindow(떠 있을 때), 아니면 떠 있는 진행 창, 그것도 없으면 소유자 없이.
/// W2-05(I1-6): 탐색기 우클릭 풀기는 메인 창을 띄우지 않는다 — 그때 뜨는 창(진행·암호·같은 이름·오류·결과)은 소유자가 없어 다른 창 뒤에 숨을 수 있어
/// 화면 가운데에 작업 표시줄 단추와 함께 띄우고 잠깐 맨 위로 올렸다 내린다. 메시지 창은 보이지 않는 맨 위 창을 소유자로 빌려 띄운다.
/// </summary>
public sealed class WpfUserInteraction : IUserInteraction
{
    private const string Caption = "일집";

    /// <summary>창의 소유자: 떠 있는 메인 창 → 떠 있는 진행 창 → 없음.</summary>
    private static Window? Owner
    {
        get
        {
            var app = Application.Current;
            if (app is null) return null;
            if (app.MainWindow is { IsLoaded: true, IsVisible: true } w) return w;
            return app.Windows.OfType<ProgressDialog>().LastOrDefault(p => p.IsVisible);
        }
    }

    /// <summary>
    /// 대화 창을 띄울 준비: 소유자가 있으면 그 가운데, 없으면(우클릭 풀기) 화면 가운데·작업 표시줄에 보이게·잠깐 맨 위로.
    /// </summary>
    private static void Prepare(Window dlg, bool showInTaskbarWhenAlone = true)
    {
        var owner = Owner;
        if (owner is not null && !ReferenceEquals(owner, dlg))
        {
            dlg.Owner = owner;
            return;
        }
        dlg.WindowStartupLocation = WindowStartupLocation.CenterScreen;
        dlg.ShowInTaskbar = showInTaskbarWhenAlone;
        // 소유자가 없으면 다른 프로그램 창 뒤에 숨을 수 있다 → 처음 보일 때만 맨 위로 올렸다 내린다
        dlg.Topmost = true;
        dlg.ContentRendered += (_, _) =>
        {
            dlg.Activate();
            dlg.Topmost = false;
        };
    }

    public string? AskPassword(string archiveName, PasswordPromptReason reason, string message)
    {
        var dlg = new PasswordPromptDialog(message);
        Prepare(dlg);
        return dlg.ShowDialog() == true ? dlg.Password ?? string.Empty : null;
    }

    public void ShowError(string message) => ShowMessage(message, MessageBoxImage.Error);

    public void ShowWarning(string message) => ShowMessage(message, MessageBoxImage.Warning);

    public void ShowInfo(string message) => ShowMessage(message, MessageBoxImage.None);

    /// <summary>메시지 창. 소유자가 없으면(우클릭 풀기) 보이지 않는 맨 위 창을 잠깐 만들어 소유자로 쓴다 — 오류 창이 뒤에 숨지 않게.</summary>
    private static void ShowMessage(string message, MessageBoxImage icon)
    {
        var owner = Owner;
        if (owner is not null)
        {
            MessageBox.Show(owner, message, Caption, MessageBoxButton.OK, icon);
            return;
        }
        var anchor = new Window
        {
            Title = Caption,
            Width = 1,
            Height = 1,
            WindowStyle = WindowStyle.None,
            AllowsTransparency = true,
            Background = Brushes.Transparent,
            Opacity = 0,
            ResizeMode = ResizeMode.NoResize,
            ShowInTaskbar = true,
            Topmost = true,
            WindowStartupLocation = WindowStartupLocation.CenterScreen,
        };
        try
        {
            anchor.Show();
            anchor.Activate();
            MessageBox.Show(anchor, message, Caption, MessageBoxButton.OK, icon);
        }
        finally
        {
            anchor.Close();
        }
    }

    public int Choose(string message, IReadOnlyList<string> choices, int defaultIndex = 0)
    {
        var dlg = new ChoiceDialog(message, choices, defaultIndex);
        Prepare(dlg);
        dlg.ShowDialog();
        return dlg.ChosenIndex;
    }

    public string? PickFolder(string title)
    {
        var dlg = new OpenFolderDialog { Title = title };
        return dlg.ShowDialog() == true ? dlg.FolderName : null;
    }

    public IReadOnlyList<string>? PickFiles(string title, string? filter, bool multiselect)
    {
        var dlg = new OpenFileDialog { Title = title, Multiselect = multiselect, CheckFileExists = true };
        if (!string.IsNullOrEmpty(filter)) dlg.Filter = filter;
        return dlg.ShowDialog() == true ? dlg.FileNames : null;
    }

    public string? AskSavePath(string title, string filter, string defaultName, string initialDirectory, string defaultExtension)
    {
        var dlg = new SaveFileDialog
        {
            Title = title,
            Filter = filter,
            FileName = defaultName,
            InitialDirectory = initialDirectory,
            DefaultExt = defaultExtension,
        };
        return dlg.ShowDialog() == true ? dlg.FileName : null;
    }

    /// <summary>예전 옵션 창 — W2-07 부터 한 화면 압축 창(<see cref="AskCompression"/>)을 옵션 칸만 보이게 띄우는 어댑터(일집 화면은 더 쓰지 않음).</summary>
    public CompressionOptions? AskCompressionOptions(string targetExtension) =>
        AskCompressionCore(new CompressionDialogRequest { FixedExtension = string.IsNullOrEmpty(targetExtension) ? ".zip" : targetExtension }) is { } dlg
            ? dlg.Result ?? CompressionOptions.Default
            : null;

    /// <summary>한 화면 압축 창(W2-07, I2-12). 취소하면 null.</summary>
    public CompressionRequest? AskCompression(CompressionDialogRequest request) => AskCompressionCore(request)?.Request;

    /// <summary>창을 띄우고 [확인]으로 닫았으면 그 창(결과를 꺼낼 수 있게), 아니면 null.</summary>
    private static CompressionOptionsDialog? AskCompressionCore(CompressionDialogRequest request)
    {
        var dlg = new CompressionOptionsDialog(new Iljip.ViewModels.CompressionDialogViewModel(request, SystemClock.Instance));
        Prepare(dlg);
        return dlg.ShowDialog() == true ? dlg : null;
    }

    public IProgressSession ShowProgress(string title)
    {
        var dlg = new ProgressDialog { Title = title };
        var owner = Owner;
        if (owner is not null && owner is not ProgressDialog)
        {
            dlg.Owner = owner;
        }
        else
        {
            // 우클릭 풀기(메인 창 없음 — I1-6): 소유자 없이 화면 가운데, 작업 표시줄에 보이게
            dlg.WindowStartupLocation = WindowStartupLocation.CenterScreen;
            dlg.ShowInTaskbar = true;
            dlg.Topmost = true;
            dlg.ContentRendered += (_, _) =>
            {
                dlg.Activate();
                dlg.Topmost = false;
            };
        }
        var session = new Session(dlg);
        dlg.Show();
        return session;
    }

    /// <summary>
    /// 같은 이름 파일 창(I1-5). 해제 작업 스레드에서 불리므로 UI 스레드로 넘겨 띄우고 답이 올 때까지 기다린다(그동안 해제는 멈춰 있음).
    /// UI 스레드는 해제를 await 로 기다리는 중이라(막혀 있지 않음) 교착하지 않는다.
    /// </summary>
    public CollisionAnswer AskCollision(CollisionQuestion question)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null) return new CollisionAnswer(CollisionChoice.Cancel, false);
        if (!dispatcher.CheckAccess()) return dispatcher.Invoke(() => AskCollision(question));
        var dlg = new CollisionDialog(question);
        Prepare(dlg);
        dlg.ShowDialog();
        return dlg.Answer;
    }

    public void ShowSummary(OperationSummary summary)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is not null && !dispatcher.CheckAccess())
        {
            dispatcher.Invoke(() => ShowSummary(summary));
            return;
        }
        var dlg = new SummaryDialog(summary);
        Prepare(dlg);
        dlg.ShowDialog();
    }

    /// <summary>글 한 줄 묻기(W2-06 — 새 폴더 이름). 다른 스레드에서 불리면 UI 스레드로 넘긴다.</summary>
    public string? AskText(string title, string message, string initialText)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is not null && !dispatcher.CheckAccess())
            return dispatcher.Invoke(() => AskText(title, message, initialText));
        var dlg = new TextPromptDialog(title, message, initialText);
        Prepare(dlg);
        return dlg.ShowDialog() == true ? dlg.Text : null;
    }

    private sealed class Session : IProgressSession
    {
        private readonly ProgressDialog _dialog;

        public Session(ProgressDialog dialog)
        {
            _dialog = dialog;
            Progress = new ThrottledArchiveProgress(p => dialog.UpdateProgress(p));
        }

        public IProgress<ArchiveProgress> Progress { get; }

        public CancellationToken CancellationToken => _dialog.CancellationToken;

        // 이미 닫힌 창에 대한 Close() 는 WPF 에서 무동작이라 여러 번 불러도 안전
        public void Close() => _dialog.Close();
    }
}
