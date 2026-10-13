using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Iljip.SfxStub;

/// <summary>
/// 화면 모드의 창(W2-10): '압축 풀 위치'(기본 바탕 화면\이름 — 있으면 '이름 (2)')·[찾아보기]·암호(걸려 있을 때만)·'다 풀면 폴더 열기'·진행 막대·[풀기][취소].
/// 암호는 [풀기] 때 확인값으로 먼저 가려(틀리면 그 자리에서 '암호가 맞지 않아요') 아무것도 만들지 않는다. 푸는 일은 작업 스레드에서(창이 굳지 않게),
/// 같은 이름 파일은 <see cref="CollisionForm"/> 으로 묻는다. 손상·안전하지 않은 경로면 알리고 닫고, 쓰기 실패(권한·공간)면 다른 곳을 고를 수 있게 남는다.
/// ★ 자동 시험은 이 창을 띄우지 않는다(무인 모드만) — 모양·동작은 MANUAL_CHECKLIST 16절 '실기기 확인 필요'.
/// </summary>
internal sealed class ExtractForm : Form
{
    private const int ContentWidth = 460;

    private readonly SfxPayload _payload;
    private readonly TextBox _folder;
    private readonly TextBox? _password;
    private readonly CheckBox _openFolder;
    private readonly ProgressBar _bar;
    private readonly Label _status;
    private readonly Label _error;
    private readonly Button _ok;
    private readonly Button _cancel;
    private readonly Button _browse;
    private readonly CheckBox? _showPassword;
    private CancellationTokenSource? _cts;
    private Task<ExtractOutcome>? _work;
    private bool _running;
    private bool _closeAfterRun;
    private long _lastReportTicks;

    /// <summary>창이 닫힌 뒤의 종료 코드(다 풀었으면 0, 아니면 1).</summary>
    public int ExitCode { get; private set; } = Program.ExitFailed;

    public ExtractForm(SfxPayload payload, string? initialFolder, string? initialPassword)
    {
        _payload = payload;
        string exeName = Path.GetFileName(payload.ExePath);
        Text = $"{SfxMessages.Title} — {exeName}";
        Font = PickFont();
        AutoScaleMode = AutoScaleMode.Font;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = true;
        StartPosition = FormStartPosition.CenterScreen;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        Padding = new Padding(16, 14, 16, 12);
        try { Icon = Icon.ExtractAssociatedIcon(payload.ExePath); } catch (Exception) { /* 기본 아이콘 */ }

        var layout = new TableLayoutPanel { ColumnCount = 1, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Dock = DockStyle.Fill };

        layout.Controls.Add(WrapLabel($"‘{exeName}’ 안의 파일 {payload.FileCount}개({FormatSize(payload.TotalBytes)})를 풀어요."));

        layout.Controls.Add(new Label { Text = "압축 풀 위치", AutoSize = true, Font = new Font(Font, FontStyle.Bold), Margin = new Padding(3, 12, 3, 4) });
        var folderRow = Row();
        _folder = new TextBox { Width = ContentWidth - 100, Text = initialFolder ?? DefaultFolder(payload.ExePath) };
        _browse = new Button { Text = "찾아보기...", AutoSize = true, Margin = new Padding(6, 0, 0, 0) };
        _browse.Click += (_, _) => Browse();
        folderRow.Controls.Add(_folder);
        folderRow.Controls.Add(_browse);
        layout.Controls.Add(folderRow);
        layout.Controls.Add(MutedLabel("폴더가 없으면 만들어요. 같은 이름 파일이 있으면 물어봐요."));

        if (payload.AnyEncrypted)
        {
            layout.Controls.Add(new Label { Text = "암호", AutoSize = true, Font = new Font(Font, FontStyle.Bold), Margin = new Padding(3, 12, 3, 4) });
            var pwRow = Row();
            _password = new TextBox { Width = 240, UseSystemPasswordChar = true, Text = initialPassword ?? string.Empty };
            _showPassword = new CheckBox { Text = "암호 보이기", AutoSize = true, Margin = new Padding(10, 3, 0, 0) };
            _showPassword.CheckedChanged += (_, _) => _password.UseSystemPasswordChar = !_showPassword.Checked;
            pwRow.Controls.Add(_password);
            pwRow.Controls.Add(_showPassword);
            layout.Controls.Add(pwRow);
        }

        _openFolder = new CheckBox { Text = "다 풀면 폴더 열기", AutoSize = true, Checked = true, Margin = new Padding(3, 12, 3, 4) };
        layout.Controls.Add(_openFolder);

        _bar = new ProgressBar { Width = ContentWidth, Height = 18, Minimum = 0, Maximum = 1000, Margin = new Padding(3, 10, 3, 2) };
        layout.Controls.Add(_bar);
        _status = MutedLabel(string.Empty);
        layout.Controls.Add(_status);
        _error = WrapLabel(string.Empty);
        _error.ForeColor = Color.FromArgb(0xC0, 0x39, 0x2B);
        _error.Visible = false;
        layout.Controls.Add(_error);

        var buttons = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.RightToLeft,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Width = ContentWidth,
            Anchor = AnchorStyles.Right,
            Margin = new Padding(3, 12, 3, 0),
            WrapContents = false,
        };
        _cancel = new Button { Text = "취소", Width = 84, Height = 30 };
        _ok = new Button { Text = "풀기", Width = 92, Height = 30, Margin = new Padding(3, 3, 8, 3) };
        _ok.Click += async (_, _) => await StartAsync();
        _cancel.Click += (_, _) => CancelOrClose();
        buttons.Controls.Add(_cancel);
        buttons.Controls.Add(_ok);
        layout.Controls.Add(buttons);

        Controls.Add(layout);
        AcceptButton = _ok;
        CancelButton = _cancel;
        FormClosing += OnClosing;
        Shown += (_, _) =>
        {
            Activate();
            if (_password is not null && _password.Text.Length == 0) _password.Focus();
            else _ok.Focus();
        };
    }

    private static Font PickFont()
    {
        try
        {
            var f = new Font("Malgun Gothic", 9f);
            if (string.Equals(f.Name, "Malgun Gothic", StringComparison.OrdinalIgnoreCase) || f.Name == "맑은 고딕") return f;
            f.Dispose();
        }
        catch (Exception) { /* 아래로 */ }
        return SystemFonts.MessageBoxFont ?? SystemFonts.DefaultFont;
    }

    private static FlowLayoutPanel Row() => new FlowLayoutPanel
    {
        AutoSize = true,
        AutoSizeMode = AutoSizeMode.GrowAndShrink,
        WrapContents = false,
        Margin = new Padding(0),
    };

    private static Label WrapLabel(string text) => new Label { Text = text, AutoSize = true, MaximumSize = new Size(ContentWidth, 0), Margin = new Padding(3, 3, 3, 3) };

    private static Label MutedLabel(string text) => new Label
    {
        Text = text,
        AutoSize = true,
        MaximumSize = new Size(ContentWidth, 0),
        ForeColor = SystemColors.GrayText,
        Margin = new Padding(3, 2, 3, 2),
    };

    /// <summary>처음 풀 위치: 바탕 화면\exe 이름(있으면 '이름 (2)' …). 바탕 화면을 모르면 exe 가 있는 폴더.</summary>
    internal static string DefaultFolder(string exePath)
    {
        string name = Path.GetFileNameWithoutExtension(exePath);
        if (string.IsNullOrWhiteSpace(name)) name = "압축 풀기";
        string parent;
        try { parent = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory); }
        catch (Exception) { parent = string.Empty; }
        if (string.IsNullOrEmpty(parent) || !Directory.Exists(parent)) parent = Path.GetDirectoryName(exePath) ?? string.Empty;
        string first = Path.Combine(parent, name);
        if (!Directory.Exists(first) && !File.Exists(first)) return first;
        for (int n = 2; n < 1000; n++)
        {
            string p = Path.Combine(parent, $"{name} ({n})");
            if (!Directory.Exists(p) && !File.Exists(p)) return p;
        }
        return first;
    }

    private void Browse()
    {
        using var dlg = new FolderBrowserDialog { Description = "압축을 풀 폴더를 고르세요.", ShowNewFolderButton = true };
        string current = _folder.Text.Trim();
        try
        {
            string? existing = current;
            while (!string.IsNullOrEmpty(existing) && !Directory.Exists(existing)) existing = Path.GetDirectoryName(existing);
            if (!string.IsNullOrEmpty(existing)) dlg.SelectedPath = existing;
        }
        catch (Exception) { /* 처음 위치 없이 */ }
        if (dlg.ShowDialog(this) == DialogResult.OK) _folder.Text = dlg.SelectedPath;
    }

    private void ShowError(string message)
    {
        _error.Text = message;
        _error.Visible = message.Length > 0;
    }

    private void SetRunning(bool running)
    {
        _running = running;
        _folder.Enabled = _browse.Enabled = _openFolder.Enabled = _ok.Enabled = !running;
        if (_password is not null) _password.Enabled = !running;
        if (_showPassword is not null) _showPassword.Enabled = !running;
        _cancel.Text = "취소";
        UseWaitCursor = running;
    }

    private async Task StartAsync()
    {
        if (_running) return;
        ShowError(string.Empty);
        string text = _folder.Text.Trim().Trim('"').Trim();
        if (text.Length == 0)
        {
            ShowError(SfxMessages.FolderMissing);
            _folder.Focus();
            return;
        }
        string folder;
        try
        {
            if (!Path.IsPathRooted(text) || text.StartsWith("\\", StringComparison.Ordinal) && !text.StartsWith("\\\\", StringComparison.Ordinal))
                throw new ArgumentException(text);
            folder = Path.GetFullPath(text);
        }
        catch (Exception)
        {
            ShowError(SfxMessages.FolderInvalid);
            _folder.Focus();
            return;
        }

        byte[]? pw = null;
        try
        {
            pw = SfxExtractor.ResolvePassword(_payload, _password?.Text, silent: false);
        }
        catch (SfxException ex) when (ex.Kind == SfxErrorKind.PasswordRequired || ex.Kind == SfxErrorKind.WrongPassword)
        {
            ShowError(ex.Message);
            _password?.Focus();
            _password?.SelectAll();
            return;
        }
        catch (Exception ex)
        {
            FailAndClose(ex);
            return;
        }

        SetRunning(true);
        _bar.Value = 0;
        _status.Text = "푸는 중...";
        _cts = new CancellationTokenSource();
        var token = _cts.Token;
        var collisions = new GuiCollisionHandler(this);
        try
        {
            _work = Task.Run(() => SfxExtractor.Extract(_payload, pw, folder, collisions, OnProgress, token), token);
            var outcome = await _work;
            ExitCode = Program.ExitOk;
            _running = false;
            _bar.Value = _bar.Maximum;
            // 지난번에 끊긴 풀기에서 원래 파일을 보이는 폴더로 옮겼으면 그 안내도(W2-10 검증 지적 1차)
            string note = outcome.Note is null ? string.Empty : "\n\n" + outcome.Note;
            if (_openFolder.Checked)
            {
                OpenFolder(folder);
                if (outcome.Skipped > 0 || outcome.Note is not null)
                {
                    string skipped = outcome.Skipped > 0 ? $"다 풀었어요. 같은 이름이라 건너뛴 파일이 {outcome.Skipped}개 있어요." : "다 풀었어요.";
                    MessageBox.Show(this, skipped + note, SfxMessages.Title, MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
            }
            else
            {
                string more = outcome.Skipped > 0 ? $"\n같은 이름이라 건너뛴 파일: {outcome.Skipped}개" : string.Empty;
                MessageBox.Show(this, $"다 풀었어요.\n{folder}{more}{note}", SfxMessages.Title, MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            Close();
        }
        catch (Exception ex)
        {
            SetRunning(false);
            switch (SfxMessages.KindOf(ex))
            {
                case SfxErrorKind.Cancelled:
                    _bar.Value = 0;
                    _status.Text = SfxMessages.Cancelled;
                    // 되돌리다 원래 파일을 제자리로 못 돌려놨으면 어디에 있는지 알린다(문구는 SfxException 에 붙어 옴)
                    if (ex is SfxException) ShowError(ex.Message);
                    if (_closeAfterRun) Close();
                    return;
                case SfxErrorKind.Damaged:
                case SfxErrorKind.UnsafePath:
                case SfxErrorKind.Unsupported:
                    FailAndClose(ex);
                    return;
                default:
                    // 쓰기 실패(권한·공간·잠김)·암호(ZipCrypto 의 드문 경우) — 다른 곳·다른 암호로 다시 할 수 있게 남는다
                    _bar.Value = 0;
                    _status.Text = string.Empty;
                    ShowError(SfxMessages.For(ex));
                    if (_closeAfterRun) Close();
                    return;
            }
        }
        finally
        {
            _cts?.Dispose();
            _cts = null;
            _work = null;
        }
    }

    private void FailAndClose(Exception ex)
    {
        MessageBox.Show(this, SfxMessages.For(ex), SfxMessages.Title, MessageBoxButtons.OK, MessageBoxIcon.Error);
        ExitCode = Program.ExitFailed;
        Close();
    }

    /// <summary>작업 스레드에서 불림 — 50ms 에 한 번만 창으로 넘긴다(마무리 단계는 늘).</summary>
    private void OnProgress(ExtractProgress p)
    {
        long now = Stopwatch.GetTimestamp();
        if (!p.Finishing && now - Interlocked.Read(ref _lastReportTicks) < Stopwatch.Frequency / 20) return;
        Interlocked.Exchange(ref _lastReportTicks, now);
        int value = p.BytesTotal > 0 ? (int)Math.Min(1000, p.BytesDone * 1000 / p.BytesTotal) : (p.FilesTotal > 0 ? p.FilesDone * 1000 / p.FilesTotal : 0);
        string text = p.Waiting ? SfxMessages.WaitingForOther : p.Finishing ? "마무리하는 중..." : $"푸는 중 ({p.FilesDone + 1}/{p.FilesTotal}) {p.Current}";
        try
        {
            BeginInvoke(new Action(() =>
            {
                _bar.Value = Math.Max(_bar.Minimum, Math.Min(_bar.Maximum, value));
                _status.Text = text;
            }));
        }
        catch (InvalidOperationException) { /* 창이 닫히는 중 */ }
    }

    private void CancelOrClose()
    {
        if (_running)
        {
            _cts?.Cancel();
            _status.Text = "취소하는 중...";
            return;
        }
        Close();
    }

    /// <summary>
    /// 푸는 중에 닫으면 먼저 멈추고(이번에 만든 것은 지움) 끝나면 닫는다.
    /// 단 로그오프·종료(<see cref="CloseReason.WindowsShutDown"/>)·작업 관리자의 [작업 끝내기]는 막지 않는다(W2-10 검증 지적 1차 — 예전엔 e.Cancel 로 로그오프를 붙잡았음):
    /// 멈추라고 하고 되돌리기를 몇 초만 기다린 뒤 그대로 닫힌다. 그 안에 못 끝내면 숨은 임시 폴더가 남지만 다음에 같은 곳에 풀 때 정리된다(주인 표시가 닫히므로).
    /// </summary>
    private void OnClosing(object? sender, FormClosingEventArgs e)
    {
        if (!_running) return;
        if (e.CloseReason == CloseReason.WindowsShutDown || e.CloseReason == CloseReason.TaskManagerClosing)
        {
            _cts?.Cancel();
            try { _work?.Wait(TimeSpan.FromSeconds(4)); } catch (Exception) { /* 취소·실패 — 되돌리기는 끝남 */ }
            return;
        }
        e.Cancel = true;
        _closeAfterRun = true;
        _cts?.Cancel();
        _status.Text = "취소하는 중...";
    }

    private static void OpenFolder(string folder)
    {
        try
        {
            Process.Start(new ProcessStartInfo("explorer.exe", "\"" + folder + "\"") { UseShellExecute = true });
        }
        catch (Exception)
        {
            // 탐색기를 못 열어도 풀기는 끝났다
        }
    }

    internal static string FormatSize(long bytes)
    {
        const double KB = 1024, MB = KB * 1024, GB = MB * 1024;
        if (bytes >= GB) return $"{bytes / GB:0.##}GB";
        if (bytes >= MB) return $"{bytes / MB:0.#}MB";
        if (bytes >= KB) return $"{bytes / KB:0.#}KB";
        return $"{bytes}바이트";
    }

    /// <summary>같은 이름 파일이 있을 때 창으로 묻는다(작업 스레드 → 창 스레드). '남은 파일도 똑같이'면 다시 묻지 않는다.</summary>
    private sealed class GuiCollisionHandler : ICollisionHandler
    {
        private readonly ExtractForm _owner;
        private CollisionChoice? _forAll;

        public GuiCollisionHandler(ExtractForm owner) => _owner = owner;

        public CollisionChoice Resolve(string targetPath, ZipItem incoming, int remaining)
        {
            if (_forAll is CollisionChoice all) return all;
            CollisionChoice choice = CollisionChoice.Cancel;
            bool applyAll = false;
            _owner.Invoke(new Action(() =>
            {
                using var dlg = new CollisionForm(targetPath, incoming, remaining, _owner.Font);
                dlg.ShowDialog(_owner);
                choice = dlg.Choice;
                applyAll = dlg.ApplyToAll;
            }));
            if (applyAll && choice != CollisionChoice.Cancel) _forAll = choice;
            return choice;
        }
    }
}

/// <summary>‘보고서.xlsx’ 파일이 이미 있어요 — [덮어쓰기][건너뛰기][취소] + '남은 파일도 똑같이'. 기본 버튼은 [건너뛰기](되돌릴 수 없는 일을 하지 않는 것).</summary>
internal sealed class CollisionForm : Form
{
    private readonly CheckBox? _all;

    public CollisionChoice Choice { get; private set; } = CollisionChoice.Cancel;

    public bool ApplyToAll => _all?.Checked == true;

    public CollisionForm(string targetPath, ZipItem incoming, int remaining, Font font)
    {
        Text = SfxMessages.Title;
        Font = font;
        AutoScaleMode = AutoScaleMode.Font;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = MinimizeBox = false;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterParent;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        Padding = new Padding(16, 14, 16, 12);

        string existing;
        try
        {
            var fi = new FileInfo(targetPath);
            existing = $"{ExtractForm.FormatSize(fi.Length)}, {fi.LastWriteTime:yyyy-MM-dd HH:mm}";
        }
        catch (Exception)
        {
            existing = "알 수 없음";
        }
        string incomingText = ExtractForm.FormatSize(incoming.UncompressedSize)
                              + (incoming.LastWriteTime is DateTime t ? $", {t:yyyy-MM-dd HH:mm}" : string.Empty);

        var layout = new TableLayoutPanel { ColumnCount = 1, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Dock = DockStyle.Fill };
        layout.Controls.Add(new Label
        {
            Text = $"‘{Path.GetFileName(targetPath)}’ 파일이 이미 있어요.\n\n있는 파일: {existing}\n새 파일: {incomingText}",
            AutoSize = true,
            MaximumSize = new Size(420, 0),
        });
        if (remaining > 0)
        {
            _all = new CheckBox { Text = "남은 파일도 똑같이", AutoSize = true, Margin = new Padding(3, 12, 3, 3) };
            layout.Controls.Add(_all);
        }
        var buttons = new FlowLayoutPanel { FlowDirection = FlowDirection.RightToLeft, AutoSize = true, WrapContents = false, Anchor = AnchorStyles.Right, Margin = new Padding(3, 12, 3, 0) };
        var cancel = new Button { Text = "취소", Width = 80, Height = 30 };
        var skip = new Button { Text = "건너뛰기", Width = 90, Height = 30 };
        var overwrite = new Button { Text = "덮어쓰기", Width = 90, Height = 30 };
        cancel.Click += (_, _) => Done(CollisionChoice.Cancel);
        skip.Click += (_, _) => Done(CollisionChoice.Skip);
        overwrite.Click += (_, _) => Done(CollisionChoice.Overwrite);
        buttons.Controls.Add(cancel);
        buttons.Controls.Add(skip);
        buttons.Controls.Add(overwrite);
        layout.Controls.Add(buttons);
        Controls.Add(layout);
        AcceptButton = skip;
        CancelButton = cancel;
        Shown += (_, _) => skip.Focus();
    }

    private void Done(CollisionChoice choice)
    {
        Choice = choice;
        DialogResult = DialogResult.OK;
        Close();
    }
}
