using System.IO;
using System.Runtime.CompilerServices;
using CommunityToolkit.Mvvm.Input;
using Iljip.Models;
using Iljip.Services;
using Iljip.Services.Editing;

namespace Iljip.ViewModels;

// 압축 고치기(I2-3 — W2-06)의 화면 쪽: 열린 ZIP·7z 에 파일 넣기(단추·☰·끌어 놓기)·지우기(Del — 묻고)·이름 바꾸기(F2)·새 폴더,
// 탐색기 우클릭 'zip에 추가'(--add-to — 메인 창 없이 진행 창만). 고치기는 편집기(ArchiveServiceLocator.EditorFor)가 임시 파일에 다 쓰고 검사한 뒤에만
// 원본과 바꾼다. 명령 차례(_commandGate) 안에서 하고, 끝나면 목록을 다시 읽는다. 묶음마다 partial 로 나눠 충돌을 줄인다(공통 설계 §0).
public partial class MainViewModel
{
    /// <summary>이 비동기 흐름이 이미 명령 차례(<see cref="_commandGate"/>)를 잡았는가 — 명령줄 처리 안에서 끌어 놓기 → 넣기로 이어질 때 다시 기다리다 멈추지 않게.</summary>
    private readonly AsyncLocal<bool> _inCommandGate = new();

    /// <summary>지금 보는 압축을 고칠 수 있는가(ZIP·7z — 나뉜 것·7z.dll 이 없을 때의 7z 는 아님).</summary>
    public bool CanEditArchive => IsArchiveMode && ArchiveServiceLocator.CanEdit(CurrentArchivePath);

    /// <summary>지금 보는 압축 안 폴더(맨 위면 빈 문자열) — 넣기·새 폴더의 자리.</summary>
    private string CurrentFolderKey => EditPlanner<object>.Normalize(SelectedFolder?.FullPath ?? string.Empty);

    /// <summary>명령 차례 안에서 한다(이미 잡은 흐름이면 그대로).</summary>
    private async Task<T> InCommandGateAsync<T>(Func<Task<T>> body)
    {
        if (_inCommandGate.Value) return await body();
        await _commandGate.WaitAsync();
        _inCommandGate.Value = true;
        try
        {
            return await body();
        }
        finally
        {
            _inCommandGate.Value = false;
            _commandGate.Release();
        }
    }

    /// <summary>
    /// 고칠 수 있는 압축을 보는 중인가 — 아니면(RAR·EGG 등) '이 형식은 고칠 수 없어요' 안내(7z 인데 이 PC 에서 7z.dll 을 못 불러오면 그 이유 —
    /// <see cref="ArchiveServiceLocator.NotEditableMessage"/>).
    /// </summary>
    private bool EnsureEditable()
    {
        if (!IsArchiveMode || string.IsNullOrEmpty(CurrentArchivePath)) return false;
        if (CanEditArchive) return true;
        _ui.ShowInfo(ArchiveServiceLocator.NotEditableMessage(CurrentArchivePath));
        return false;
    }

    /// <summary>화면용: 고칠 수 있으면 true, 아니면 '이 형식은 고칠 수 없어요' 안내 후 false(이름 바꾸기 칸을 열기 전에).</summary>
    public bool CheckEditable() => EnsureEditable();

    // ───────────────────────────── 단추·메뉴 ─────────────────────────────

    /// <summary>[파일 넣기]: 파일을 골라 지금 보는 폴더에 넣는다.</summary>
    [RelayCommand]
    private async Task AddFilesToArchiveAsync()
    {
        if (!EnsureEditable()) return;
        var picked = _ui.PickFiles("압축 파일에 넣을 파일 선택 (여러 개 가능)", null, multiselect: true);
        if (picked is not { Count: > 0 }) return;
        await AddPathsToArchiveAsync(picked);
    }

    /// <summary>[폴더 넣기]: 폴더를 골라 지금 보는 폴더에 그 이름째 넣는다.</summary>
    [RelayCommand]
    private async Task AddFolderToArchiveAsync()
    {
        if (!EnsureEditable()) return;
        var folder = _ui.PickFolder("압축 파일에 넣을 폴더 선택");
        if (folder is null) return;
        await AddPathsToArchiveAsync(new[] { folder });
    }

    /// <summary>[새 폴더]: 이름을 물어 지금 보는 폴더 안에 빈 폴더를 만든다.</summary>
    [RelayCommand]
    private async Task NewFolderInArchiveAsync()
    {
        if (!EnsureEditable()) return;
        string? name = _ui.AskText("새 폴더", "새 폴더 이름을 넣어 주세요.", SuggestNewFolderName());
        if (name is null) return;
        await CreateFolderAsync(name);
    }

    /// <summary>지금 폴더에 없는 '새 폴더' 이름('새 폴더', '새 폴더 (2)' …).</summary>
    private string SuggestNewFolderName()
    {
        // 지금 폴더의 이름들(W2-09: 찾는 중이면 목록이 찾은 결과라 트리의 지금 폴더로 본다)
        var taken = (IsSearching && SelectedFolder is { } folder
                ? folder.Children.Select(c => c.Name).Concat(folder.Files.Select(f => f.Name))
                : VisibleEntries.Select(e => e.Name))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (!taken.Contains("새 폴더")) return "새 폴더";
        for (int n = 2; ; n++)
            if (!taken.Contains($"새 폴더 ({n})")) return $"새 폴더 ({n})";
    }

    // ───────────────────────────── 고치기 ─────────────────────────────

    /// <summary>파일·폴더를 지금 보는 압축(<paramref name="targetFolder"/> — 없으면 지금 보는 폴더)에 넣는다. 넣었으면 true.</summary>
    public async Task<bool> AddPathsToArchiveAsync(IReadOnlyList<string> paths, string? targetFolder = null)
    {
        if (!EnsureEditable()) return false;
        var sources = paths.Where(p => !string.IsNullOrEmpty(p)).ToList();
        if (sources.Count == 0) return false;
        var result = await RunEditAsync(CurrentArchivePath!, new EditOp[] { new EditOp.Add(sources, targetFolder ?? CurrentFolderKey) }, "압축 파일에 넣는 중...");
        if (result is null) return false;
        StatusText = AddedStatus(result);
        return true;
    }

    /// <summary>
    /// 고른 항목을 압축 파일에서 지운다 — 먼저 ‘N개를 압축 파일에서 지울까요?’(기본 [취소]). 폴더면 그 아래 전부. 지웠으면 true.
    /// </summary>
    public async Task<bool> DeleteEntriesAsync(IReadOnlyList<ArchiveEntry> entries)
    {
        if (entries.Count == 0 || !EnsureEditable()) return false;
        // 고른 줄마다 하나(같은 줄을 두 번 넣지는 않음). 겉보기 이름이 같은 줄(맥 NFD·NFC 파일)을 여럿 골랐으면 같은 키가 그만큼 — 고치기 계획이 고른 수를 센다(W2-09 검증 지적)
        var keys = entries.Distinct<ArchiveEntry>(ReferenceEqualityComparer.Instance)
            .Select(e => EditPlanner<object>.Normalize(e.Path)).Where(k => k.Length > 0).ToList();
        if (keys.Count == 0) return false;
        int pick = _ui.Choose(DeleteQuestion(entries), new[] { "지우기", "취소" }, defaultIndex: 1);
        if (pick != 0)
        {
            StatusText = "지우기를 취소했어요";
            return false;
        }
        var result = await RunEditAsync(CurrentArchivePath!, new EditOp[] { new EditOp.Delete(keys) }, "압축 파일에서 지우는 중...");
        if (result is null) return false;
        StatusText = $"{keys.Count}개를 압축 파일에서 지웠어요";
        return true;
    }

    /// <summary>지우기 확인 문구: ‘N개를 압축 파일에서 지울까요?’ + 이름 몇 개 + 되돌릴 수 없다는 한 줄.</summary>
    internal static string DeleteQuestion(IReadOnlyList<ArchiveEntry> entries)
    {
        var names = entries.Take(3).Select(e => "- " + e.Name + (e.IsDirectory ? " 폴더" : string.Empty)).ToList();
        if (entries.Count > 3) names.Add($"외 {entries.Count - 3}개");
        return $"{entries.Count}개를 압축 파일에서 지울까요?\n{string.Join("\n", names)}\n\n지운 것은 되돌릴 수 없어요.";
    }

    /// <summary>항목 이름을 바꾼다(F2). 폴더면 그 아래 전부의 경로가 따라 바뀐다. 못 쓰는 이름이면 알리고 false.</summary>
    public async Task<bool> RenameEntryAsync(ArchiveEntry entry, string newName)
    {
        if (!EnsureEditable()) return false;
        newName = (newName ?? string.Empty).Trim();
        if (string.Equals(newName, entry.Name, StringComparison.Ordinal)) return false;
        if (EditPlanner<object>.ValidateName(newName) is { } problem)
        {
            _ui.ShowWarning(problem);
            return false;
        }
        string key = EditPlanner<object>.Normalize(entry.Path);
        var result = await RunEditAsync(CurrentArchivePath!, new EditOp[] { new EditOp.Rename(key, newName) }, "이름 바꾸는 중...");
        if (result is null) return false;
        StatusText = $"이름을 ‘{newName}’(으)로 바꿨어요";
        return true;
    }

    /// <summary>지금 보는 폴더 안에 빈 폴더를 만든다. 못 쓰는 이름이면 알리고 false.</summary>
    public async Task<bool> CreateFolderAsync(string name)
    {
        if (!EnsureEditable()) return false;
        name = (name ?? string.Empty).Trim();
        if (EditPlanner<object>.ValidateName(name) is { } problem)
        {
            _ui.ShowWarning(problem);
            return false;
        }
        var result = await RunEditAsync(CurrentArchivePath!, new EditOp[] { new EditOp.NewFolder(EditPlanner<object>.Join(CurrentFolderKey, name)) }, "새 폴더 만드는 중...");
        if (result is null) return false;
        StatusText = $"‘{name}’ 폴더를 만들었어요";
        return true;
    }

    /// <summary>
    /// 고치기 한 번: 진행 창(취소 가능) + 같은 이름이면 묻기(넣기) + 암호(없거나 틀리면 묻고 다시 — 취소하면 그만). 끝나면 지금 보는 압축이면 목록을 다시 읽는다.
    /// 실패하면 쉬운 한국어 오류 창(원본은 그대로). 됐으면 결과, 취소·실패면 null — 실패(오류 창을 보임)였으면 <paramref name="failed"/> 에 true.
    /// </summary>
    /// <param name="elevateAs">
    /// 권한 오류일 때 관리자 권한으로 다시 할 같은 명령(W2-11, I3-16 — '--add-to 압축 경로…'). null 이면 명령줄로 나타낼 수 없는 고치기라
    /// '관리자 권한으로 일집 다시 열기'(그 압축을 관리자 일집에서 엶)만 권한다.
    /// </param>
    /// <param name="usedPassword">됐으면 쓴 암호(없으면 null)를 담는다 — 메모리에만(5차 통합 점검: 다음 시작 넣기가 같은 압축의 둘째 파일에 암호를 다시 묻지 않게).</param>
    private Task<EditResult?> RunEditAsync(string archive, IReadOnlyList<EditOp> ops, string title, string? knownPassword = null, StrongBox<bool>? failed = null,
        ParsedCommand? elevateAs = null, StrongBox<string?>? usedPassword = null) =>
        InCommandGateAsync(async () =>
        {
            var editor = ArchiveServiceLocator.EditorFor(archive);
            if (editor is null)
            {
                _ui.ShowWarning(ArchiveServiceLocator.NotEditableMessage(archive));
                if (failed is not null) failed.Value = true;
                return (EditResult?)null;
            }
            bool current = string.Equals(archive, CurrentArchivePath, StringComparison.OrdinalIgnoreCase);
            string? password = knownPassword ?? (current ? _archivePassword : null);
            var resolver = AskingResolver.For(CollisionPolicy.Ask, _ui);   // 압축 안에서는 늘 묻는다(말없이 덮어쓰지 않게)
            while (true)
            {
                var session = _ui.ShowProgress(title);
                using var scope = ExtractionScope.Begin(resolver);
                try
                {
                    var result = await editor.EditAsync(archive, ops, password, session.Progress, session.CancellationToken);
                    session.Close();
                    if (usedPassword is not null) usedPassword.Value = password;   // 5차 통합 점검: 다음 시작 넣기가 같은 압축의 다음 파일에 다시 묻지 않게
                    if (current && !string.IsNullOrEmpty(password)) _archivePassword = password;
                    if (!string.IsNullOrEmpty(password))
                        foreach (var ws in AllWorkspaces().Where(w => string.Equals(w.ArchivePath, Path.GetFullPath(archive), StringComparison.OrdinalIgnoreCase)))
                            _workspacePasswords[ws] = password;
                    if (current) await ReloadArchiveListAsync();
                    if (result.Skipped.Count > 0)
                        _ui.ShowWarning(SkippedLines($"넣기를 마쳤어요. 넣지 못한 파일 {result.Skipped.Count}개:", result.Skipped));
                    return result;
                }
                catch (OperationCanceledException)
                {
                    StatusText = "고치기를 취소했어요";
                    return null;
                }
                catch (Exception ex) when (IsPasswordRelated(ex))
                {
                    session.Close();
                    var (reason, message) = PasswordPrompt(ex, password);
                    var pw = _ui.AskPassword(Path.GetFileName(archive), reason, message);
                    if (pw is null)
                    {
                        StatusText = "고치기를 취소했어요";
                        return null;
                    }
                    password = pw;
                }
                catch (Exception ex)
                {
                    session.Close();
                    Logger.LogError($"압축 고치기 실패: {archive}", ex);
                    string failure = $"압축 파일을 고치지 못했어요. 원래 압축 파일은 그대로예요.\n\n{ErrorMessages.ToUserMessage(ex)}";
                    // W2-11(I3-16): 압축 파일이 있는 곳에 쓸 권한이 없으면 관리자 권한으로(넣기는 같은 명령, 그 밖의 고치기는 일집 다시 열기)
                    // 아니면 문제 창(W2-08 — 원문은 '자세히')
                    if (!OfferElevatedRetry(ex, failure, elevateAs, archive, archive))
                        ShowProblem("압축 파일을 고치지 못했어요. 원래 압축 파일은 그대로예요.", ex);
                    StatusText = "오류 발생";
                    if (failed is not null) failed.Value = true;
                    return null;
                }
                finally
                {
                    session.Close();
                }
            }
        });

    /// <summary>고친 뒤 지금 보는 압축의 목록을 다시 읽는다(보던 폴더가 남아 있으면 그 폴더로).</summary>
    private async Task ReloadArchiveListAsync()
    {
        if (CurrentArchivePath is not { } archive) return;
        var service = _locator.ResolveByContent(archive);
        if (service is null) return;
        string folder = SelectedFolder?.FullPath ?? string.Empty;
        try
        {
            var entries = await service.ListEntriesAsync(archive, _archivePassword);
            Entries.Clear();
            foreach (var e in entries) Entries.Add(e);
            BumpArchiveGeneration();   // IJ3-01: 같은 경로라도 내용이 바뀌었을 수 있다 — 고치기 전에 읽어 둔 그림은 버린다
            BuildFolderTree();
            if (folder.Length > 0) NavigateToFolder(folder);
            StatusText = $"{entries.Count}개 항목";
        }
        catch (Exception ex)
        {
            Logger.LogError("고친 뒤 목록 다시 읽기 실패: " + archive, ex);
            ShowProblem("고친 압축 파일의 목록을 다시 읽지 못했어요.", ex);
            StatusText = "오류 발생";
        }
    }

    /// <summary>넣기 결과 한 줄(상태 줄·우클릭 넣기 안내).</summary>
    internal static string AddedStatus(EditResult result) =>
        result.AddedFiles == 0 && result.CollisionSkipped > 0
            ? "같은 이름이라 넣지 않았어요"
            : $"{result.AddedFiles}개를 넣었어요";

    // ───────────────────────────── 탐색기 우클릭 'zip에 추가'(--add-to) ─────────────────────────────

    /// <summary>'기존 압축에 넣기'에서 압축 파일을 고르는 창의 형식 칸(ZIP·7Z 만 고칠 수 있음).</summary>
    internal const string AddToPickFilter = "ZIP·7Z 압축 파일 (*.zip;*.7z)|*.zip;*.7z";

    /// <summary>
    /// '--add-to 압축 경로…'(메인 창 없이 — <see cref="ProcessShellCommandAsync"/>): 진행 창만 띄우고, 암호가 필요하면 묻고, 끝나면 ‘3개를 넣었어요’ 한 줄.
    /// <paramref name="archive"/> 가 null 이면('--add-to-pick 경로…' — 탐색기 우클릭 'zip에 추가(기존 zip 고르기)') 먼저 넣을 압축 파일을 고르는 창(ZIP·7Z)을
    /// 띄운다 — 고르지 않으면 조용히 그만. 보던 압축·목록은 바꾸지 않는다(같은 압축을 보고 있으면 목록만 다시 읽음). 고칠 수 없는 형식·없는 파일은 쉬운 한국어 오류 창.
    /// </summary>
    internal async Task RunAddToAsync(string? archive, IReadOnlyList<string> paths)
    {
        var sources = paths.Where(p => File.Exists(p) || Directory.Exists(p)).Select(Path.GetFullPath)
            .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (sources.Count == 0)
        {
            _ui.ShowError("넣을 파일을 찾을 수 없어요. 지웠거나 다른 곳으로 옮겼는지 확인해 주세요.");
            return;
        }
        if (archive is null)
        {
            var picked = _ui.PickFiles("넣을 압축 파일 고르기 (ZIP·7Z)", AddToPickFilter, multiselect: false);
            if (picked is not { Count: > 0 }) return;   // 고르지 않음 — 조용히 그만
            archive = picked[0];
        }
        string full;
        try
        {
            full = Path.GetFullPath(archive);
        }
        catch
        {
            full = archive;
        }
        if (!File.Exists(full))
        {
            _ui.ShowError($"넣을 압축 파일을 찾을 수 없어요. 지웠거나 다른 곳으로 옮겼는지 확인해 주세요.\n\n{archive}");
            return;
        }
        if (ArchiveServiceLocator.EditorFor(full) is null)
        {
            _ui.ShowError($"‘{Path.GetFileName(full)}’에 넣지 못했어요.\n\n{ArchiveServiceLocator.NotEditableMessage(full)}");
            return;
        }
        var result = await RunEditAsync(full, new EditOp[] { new EditOp.Add(sources, string.Empty) }, "압축 파일에 넣는 중...",
            elevateAs: new ParsedCommand(CommandVerb.AddTo, full, null, sources));
        if (result is null) return;
        _ui.ShowInfo($"{AddedStatus(result)}.\n({Path.GetFileName(full)})");
    }
}
