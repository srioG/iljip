using Iljip.Models;
using Iljip.Services;

namespace Iljip.Tests.TestSupport;

/// <summary>
/// 창 없이 화면(MainViewModel) 흐름을 시험하는 가짜 <see cref="IUserInteraction"/>.
/// 암호 창은 미리 넣어 둔 답을 차례로 돌려주고(다 쓰면 '취소' = null), 모든 호출을 기록한다.
/// 선택 창은 <see cref="ChooseByLabel"/>(버튼 문구 앞부분으로 고르기) 또는 넣어 둔 순번(<see cref="ChoiceAnswers"/>, 다 쓰면 -1 = 창 닫기).
/// 폴더·파일·저장 창은 넣어 둔 값(기본 null = 취소)을 돌려준다. 진행 창은 기록만 하는 가짜.
/// (W1-B 보관본의 같은 이름 가짜와 모양을 맞춤 — 선택 창은 W1-D 에서 더함)
/// </summary>
public sealed class FakeUserInteraction : IUserInteraction
{
    private readonly Queue<string?> _passwords;

    public FakeUserInteraction(params string?[] passwordAnswers) => _passwords = new Queue<string?>(passwordAnswers);

    /// <summary>암호 창이 불린 기록(압축 이름, 이유, 문구).</summary>
    public List<(string ArchiveName, PasswordPromptReason Reason, string Message)> PasswordPrompts { get; } = new();
    public List<string> Errors { get; } = new();
    public List<string> Warnings { get; } = new();
    public List<string> Infos { get; } = new();
    public List<ProgressRecorder> ProgressSessions { get; } = new();

    /// <summary>선택 창이 불린 기록(문구, 버튼 문구들, 기본 버튼 순번).</summary>
    public List<(string Message, IReadOnlyList<string> Choices, int DefaultIndex)> ChoicePrompts { get; } = new();

    /// <summary>선택 창의 답(순번). 비면 <see cref="ChooseByLabel"/>, 그것도 없으면 -1(창 닫기).</summary>
    public Queue<int> ChoiceAnswers { get; } = new();

    /// <summary>이 문구로 시작하는 버튼을 고른다(없으면 -1).</summary>
    public string? ChooseByLabel { get; set; }

    public string? FolderToPick { get; set; }
    public IReadOnlyList<string>? FilesToPick { get; set; }
    public string? SavePath { get; set; }
    public CompressionOptions? CompressionOptions { get; set; }

    /// <summary>진행 창 [취소]를 미리 누른 것처럼(다음에 여는 진행 창부터).</summary>
    public bool CancelProgress { get; set; }

    /// <summary>진행 창이 '처리한 바이트 &gt; 0' 을 처음 보고받을 때 [취소]를 누른 것처럼(풀거나 압축하는 도중 취소 — 다음에 여는 진행 창부터).</summary>
    public bool CancelOnFirstBytes { get; set; }

    /// <summary>창 하나라도 떴는지(오류·경고 창 — 테스트가 '조용히 끝남'을 확인할 때).</summary>
    public bool AnyErrorOrWarning => Errors.Count > 0 || Warnings.Count > 0;

    public string? AskPassword(string archiveName, PasswordPromptReason reason, string message)
    {
        PasswordPrompts.Add((archiveName, reason, message));
        return _passwords.Count > 0 ? _passwords.Dequeue() : null;
    }

    public void ShowError(string message) => Errors.Add(message);
    public void ShowWarning(string message) => Warnings.Add(message);
    public void ShowInfo(string message) => Infos.Add(message);

    public int Choose(string message, IReadOnlyList<string> choices, int defaultIndex = 0)
    {
        ChoicePrompts.Add((message, choices.ToList(), defaultIndex));
        if (ChoiceAnswers.Count > 0) return ChoiceAnswers.Dequeue();
        if (ChooseByLabel is { } label)
        {
            for (int i = 0; i < choices.Count; i++)
                if (choices[i].StartsWith(label, StringComparison.Ordinal)) return i;
        }
        return -1;
    }

    public string? PickFolder(string title) => FolderToPick;

    /// <summary>파일 고르기 창에 넘긴 형식 칸(filter) 기록.</summary>
    public List<string?> PickFilesFilters { get; } = new();

    public IReadOnlyList<string>? PickFiles(string title, string? filter, bool multiselect)
    {
        PickFilesFilters.Add(filter);
        return FilesToPick;
    }

    /// <summary>저장 창에 넘긴 제안 이름 기록.</summary>
    public List<string> SaveDefaultNames { get; } = new();

    public string? AskSavePath(string title, string filter, string defaultName, string initialDirectory, string defaultExtension)
    {
        SaveDefaultNames.Add(defaultName);
        return SavePath;
    }

    public CompressionOptions? AskCompressionOptions(string targetExtension) => CompressionOptions;

    public IProgressSession ShowProgress(string title)
    {
        var session = new Session(CancelProgress, CancelOnFirstBytes);
        ProgressSessions.Add(session.Recorder);
        return session;
    }

    private sealed class Session : IProgressSession
    {
        private readonly CancellationTokenSource _cts = new();

        public Session(bool cancelled, bool cancelOnFirstBytes)
        {
            if (cancelled) _cts.Cancel();
            Recorder = new ProgressRecorder(p =>
            {
                if (cancelOnFirstBytes && p.BytesProcessed > 0) _cts.Cancel();
            });
        }

        public ProgressRecorder Recorder { get; }
        public IProgress<ArchiveProgress> Progress => Recorder;
        public CancellationToken CancellationToken => _cts.Token;
        public void Close() { }
    }
}
