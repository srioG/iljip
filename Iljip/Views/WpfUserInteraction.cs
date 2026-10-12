using System.Windows;
using Iljip.Models;
using Iljip.Services;
using Microsoft.Win32;

namespace Iljip.Views;

/// <summary>실제 앱의 창들(<see cref="IUserInteraction"/> 구현). 소유 창은 그때그때의 MainWindow(창이 없으면 소유자 없이).</summary>
public sealed class WpfUserInteraction : IUserInteraction
{
    private const string Caption = "일집";

    private static Window? Owner => Application.Current?.MainWindow is { IsLoaded: true } w ? w : null;

    public string? AskPassword(string archiveName, PasswordPromptReason reason, string message)
    {
        var dlg = new PasswordPromptDialog(message) { Owner = Owner };
        return dlg.ShowDialog() == true ? dlg.Password ?? string.Empty : null;
    }

    public void ShowError(string message) =>
        MessageBox.Show(message, Caption, MessageBoxButton.OK, MessageBoxImage.Error);

    public void ShowWarning(string message) =>
        MessageBox.Show(message, Caption, MessageBoxButton.OK, MessageBoxImage.Warning);

    public void ShowInfo(string message) =>
        MessageBox.Show(message, Caption);

    public int Choose(string message, IReadOnlyList<string> choices, int defaultIndex = 0) =>
        ChoiceDialog.Ask(Owner, message, defaultIndex, choices);

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

    public CompressionOptions? AskCompressionOptions(string targetExtension)
    {
        var dlg = new CompressionOptionsDialog(targetExtension) { Owner = Owner };
        return dlg.ShowDialog() == true ? dlg.Result ?? CompressionOptions.Default : null;
    }

    public IProgressSession ShowProgress(string title)
    {
        var dlg = new ProgressDialog { Owner = Owner, Title = title };
        var session = new Session(dlg);
        dlg.Show();
        return session;
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
