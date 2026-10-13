using Iljip.Models;
using Iljip.Services;

namespace Iljip.Tests.TestSupport;

/// <summary>
/// 창 없이 화면(MainViewModel) 흐름을 시험하는 가짜 <see cref="IUserInteraction"/>.
/// 암호 창은 미리 넣어 둔 답을 차례로 돌려주고(다 쓰면 '취소' = null), 모든 호출을 기록한다.
/// 선택 창은 <see cref="ChooseByLabel"/>(버튼 문구 앞부분으로 고르기) 또는 넣어 둔 순번(<see cref="ChoiceAnswers"/>, 다 쓰면 -1 = 창 닫기).
/// 폴더·파일·저장 창은 넣어 둔 값(기본 null = 취소)을 돌려준다. 진행 창은 기록만 하는 가짜.
/// (W1-B 보관본의 같은 이름 가짜와 모양을 맞춤 — 선택 창은 W1-D 에서 더함)
/// W2-05: 같은 이름 창(<see cref="AskCollision"/> — 넣어 둔 답, 없으면 '이름 바꾸기')·요약 창(<see cref="Summaries"/>)을 더함.
/// W2-07: 한 화면 압축 창(<see cref="AskCompression"/> — 넣어 둔 답, 없으면 예전 저장 창·옵션 창 답으로)을 더함.
/// W2-09: 풀기 창(<see cref="AskExtractTarget"/> — 넣어 둔 답 <see cref="ExtractTargetAnswer"/>, 없으면 예전 폴더 고르기 창 답으로)을 더함.
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

    /// <summary>진행 창 제목(<see cref="ProgressSessions"/> 와 같은 차례 — W2-12: 창 없이 바로 압축의 '압축 중...').</summary>
    public List<string> ProgressTitles { get; } = new();

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

    /// <summary>선택 창이 뜰 때마다(답하기 전에) 문구로 부른다 — 묻는 동안 다른 일을 끼워 넣는 시험(W2-06: 묻는 동안 파일을 또 저장).</summary>
    public Action<string>? OnChoicePrompt { get; set; }

    public int Choose(string message, IReadOnlyList<string> choices, int defaultIndex = 0)
    {
        ChoicePrompts.Add((message, choices.ToList(), defaultIndex));
        OnChoicePrompt?.Invoke(message);
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

    /// <summary>진행 창이 보고를 받을 때마다 부른다(작업 스레드에서) — 진행 도중 다른 일을 끼워 넣는 시험(W2-06: 다시 넣는 도중 파일을 또 저장).</summary>
    public Action<ArchiveProgress>? OnProgressReport { get; set; }

    /// <summary>
    /// 진행 창이 이 조건에 맞는 보고를 받으면 [취소]를 누른 것처럼(다음에 여는 진행 창부터 — W2-07 검증 지적 2차: '만든 압축 파일을 시험하는 중' 에서 취소 등).
    /// </summary>
    public Func<ArchiveProgress, bool>? CancelWhen { get; set; }

    public IProgressSession ShowProgress(string title)
    {
        var cancelWhen = CancelWhen;
        var session = new Session(CancelProgress, CancelOnFirstBytes, p => OnProgressReport?.Invoke(p), cancelWhen);
        ProgressSessions.Add(session.Recorder);
        ProgressTitles.Add(title);
        return session;
    }

    // ───── W2-05: 같은 이름 묻기·여러 개 풀기 요약 ─────

    private readonly object _collisionGate = new();

    /// <summary>같은 이름 창이 불린 기록(해제 작업 스레드에서 불림).</summary>
    public List<CollisionQuestion> CollisionPrompts { get; } = new();

    /// <summary>같은 이름 창의 답(차례로). 비면 <see cref="DefaultCollisionAnswer"/>.</summary>
    public Queue<CollisionAnswer> CollisionAnswers { get; } = new();

    /// <summary>
    /// 답을 넣어 두지 않았을 때의 답 — 기본은 '이름 바꾸기'(예전 동작 '이름 (1)' 과 같아 기존 화면 시험이 그대로 통과).
    /// </summary>
    public CollisionAnswer DefaultCollisionAnswer { get; set; } = new(CollisionChoice.Rename, false);

    /// <summary>같은 이름 창이 처음 불릴 때 한 번 부른다(질문 중에 다른 일을 끼워 넣는 시험 — 예: 대상 파일 잠그기).</summary>
    public Action<CollisionQuestion>? OnCollisionPrompt { get; set; }

    public CollisionAnswer AskCollision(CollisionQuestion question)
    {
        lock (_collisionGate)
        {
            CollisionPrompts.Add(question);
            var hook = OnCollisionPrompt;
            OnCollisionPrompt = null;
            hook?.Invoke(question);
            return CollisionAnswers.Count > 0 ? CollisionAnswers.Dequeue() : DefaultCollisionAnswer;
        }
    }

    /// <summary>요약 창이 불린 기록.</summary>
    public List<OperationSummary> Summaries { get; } = new();

    public void ShowSummary(OperationSummary summary) => Summaries.Add(summary);

    // ───── W2-06: 글 한 줄 묻기(새 폴더 이름) ─────

    /// <summary>글 묻기 창이 불린 기록(제목, 문구, 처음 글자).</summary>
    public List<(string Title, string Message, string Initial)> TextPrompts { get; } = new();

    /// <summary>글 묻기 창의 답(차례로, null = 취소). 비면 취소(null).</summary>
    public Queue<string?> TextAnswers { get; } = new();

    public string? AskText(string title, string message, string initialText)
    {
        TextPrompts.Add((title, message, initialText));
        return TextAnswers.Count > 0 ? TextAnswers.Dequeue() : null;
    }

    // ───── W2-07: 한 화면 압축 창 ─────

    /// <summary>한 화면 압축 창이 불린 기록(넘긴 것 — 설정 기본값·쓸 수 있는 형식·제안 이름).</summary>
    public List<CompressionDialogRequest> CompressionPrompts { get; } = new();

    /// <summary>
    /// 한 화면 압축 창의 답(넘긴 것을 보고 만듦 — 원본은 넘긴 것을 쓰는 것이 보통). 비면 예전 관례: <see cref="SavePath"/>(저장 창) +
    /// <see cref="CompressionOptions"/>(옵션 창) 둘 다 있으면 그 경로·옵션으로(덮어쓰기는 저장 창이 확인한 것으로 봄), 하나라도 없으면 취소(null).
    /// </summary>
    public Func<CompressionDialogRequest, CompressionRequest?>? CompressionRequestAnswer { get; set; }

    public CompressionRequest? AskCompression(CompressionDialogRequest request)
    {
        CompressionPrompts.Add(request);
        if (CompressionRequestAnswer is { } answer) return answer(request);
        if (SavePath is null || CompressionOptions is null) return null;
        // 예전 3단계(저장 창 → 옵션 창)의 답을 한 화면 창의 답으로 — 형식은 저장 경로의 확장자, 빼기는 설정 기본값(창의 처음 값과 같음)
        var format = Iljip.Models.CompressionFormats.FromExtension(Path.GetExtension(SavePath)) ?? Iljip.Models.CompressionFormat.Zip;
        string name = Path.GetFileName(SavePath);
        string ext = Iljip.Models.CompressionFormats.ExtensionOf(format);
        string baseName = name.EndsWith(ext, StringComparison.OrdinalIgnoreCase) && name.Length > ext.Length ? name[..^ext.Length] : name;
        return new CompressionRequest
        {
            Sources = request.Sources,
            OutputFolder = Path.GetDirectoryName(SavePath) ?? string.Empty,
            BaseName = baseName,
            Format = format,
            Options = CompressionOptions,
            ExcludeEnabled = request.Defaults.ExcludeEnabled,
            ExcludePatterns = request.Defaults.ExcludePatterns.ToList(),
            OverwriteExisting = true,
        };
    }

    // ───── W2-09: 풀기 창 ─────

    /// <summary>풀기 창이 불린 기록(넘긴 것 — 압축 파일·처음 값·최근 폴더).</summary>
    public List<ExtractTargetRequest> ExtractTargetPrompts { get; } = new();

    /// <summary>
    /// 풀기 창의 답(넘긴 것을 보고 만듦, null 을 돌려주면 취소). 비면 예전 관례: 폴더 고르기 창(<see cref="FolderToPick"/>, null 이면 취소)의 폴더에 —
    /// 압축이 하나면 그 폴더에 바로, 여럿이면 그 안의 '이름' 폴더마다(예전 폴더 고르기 창 흐름과 같음), 휴지통은 넘긴 처음 값(설정) 그대로, 폴더는 열지 않음.
    /// </summary>
    public Func<ExtractTargetRequest, ExtractTarget?>? ExtractTargetAnswer { get; set; }

    public ExtractTarget? AskExtractTarget(ExtractTargetRequest request)
    {
        ExtractTargetPrompts.Add(request);
        if (ExtractTargetAnswer is { } answer) return answer(request);
        var folder = PickFolder(request.Title);
        return folder is null
            ? null
            : new ExtractTarget(folder, MakeFolder: request.Archives.Count > 1, DeleteArchiveAfter: request.DeleteArchiveAfter, OpenFolderAfter: false);
    }

    // ───── W2-10: 확인란이 있는 선택 창(자동 풀림 exe 를 만들기 전 '회사 메일은 exe 를 막아요' + '다시 묻지 않기') ─────

    /// <summary>확인란이 있는 선택 창이 불린 기록(문구, 버튼 문구들, 확인란 글자, 기본 버튼 순번). 버튼 고르기는 <see cref="Choose"/> 와 같은 답을 쓴다.</summary>
    public List<(string Message, IReadOnlyList<string> Choices, string CheckText, int DefaultIndex)> CheckChoicePrompts { get; } = new();

    /// <summary>확인란 답(기본 꺼짐).</summary>
    public bool CheckAnswer { get; set; }

    /// <summary>
    /// 버튼은 <see cref="Choose"/> 와 같은 규칙(넣어 둔 순번 → <see cref="ChooseByLabel"/> → -1)으로 고르고 <see cref="ChoicePrompts"/> 에도 남긴다(창 하나로 셈).
    /// 확인란은 <see cref="CheckAnswer"/>(창을 닫으면 -1 과 함께 그대로 돌려줌 — 부르는 쪽이 버튼에 따라 씀).
    /// </summary>
    public ChoiceWithCheck ChooseWithCheck(string message, IReadOnlyList<string> choices, string checkText, int defaultIndex = 0)
    {
        CheckChoicePrompts.Add((message, choices.ToList(), checkText, defaultIndex));
        int index = Choose(message, choices, defaultIndex);
        return new ChoiceWithCheck(index, CheckAnswer);
    }

    private sealed class Session : IProgressSession
    {
        private readonly CancellationTokenSource _cts = new();

        public Session(bool cancelled, bool cancelOnFirstBytes, Action<ArchiveProgress>? onReport = null, Func<ArchiveProgress, bool>? cancelWhen = null)
        {
            if (cancelled) _cts.Cancel();
            Recorder = new ProgressRecorder(p =>
            {
                onReport?.Invoke(p);
                if (cancelOnFirstBytes && p.BytesProcessed > 0) _cts.Cancel();
                if (cancelWhen is not null && cancelWhen(p)) _cts.Cancel();
            });
        }

        public ProgressRecorder Recorder { get; }
        public IProgress<ArchiveProgress> Progress => Recorder;
        public CancellationToken CancellationToken => _cts.Token;
        public void Close() { }
    }
}
