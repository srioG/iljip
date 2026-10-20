using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using CommunityToolkit.Mvvm.Input;
using Iljip.Models;
using Iljip.Services;
using Iljip.Services.Editing;

namespace Iljip.ViewModels;

// 압축 속 파일 열기(I1-3 — W2-04 보관본의 모양을 줄여 들임)와 고친 파일 다시 넣기(I3-3 — W2-06).
// IJ3-01(W2-04 다시): 끌어내기·Ctrl+C·그림 미리 보기는 MainViewModel.EntryCopy.cs 에 — 고른 항목만 꺼내기(ExtractForUseAsync)는 여기서 같이 쓴다.
// 묶음마다 partial 로 나눠 충돌을 줄인다(공통 설계 §0).
public partial class MainViewModel
{
    /// <summary>VM 을 만든 스레드의 동기화 문맥(화면 스레드) — 파일 바뀜 알림(다른 스레드)에서 묻는 창을 화면 스레드로 넘긴다. 시험은 null(그 자리에서).</summary>
    private readonly SynchronizationContext? _uiContext = SynchronizationContext.Current;

    /// <summary>지금 압축의 '안의 파일 열기' 작업 폴더(처음 열 때 만든다).</summary>
    private ArchiveWorkspace? _workspace;

    /// <summary>
    /// 다른 압축으로 바꾸거나 닫은 압축의 작업 폴더 — 앱을 닫을 때(고친 파일이 있으면 물은 뒤) 지운다(W2-04 와 같음: 압축을 바꿀 때 지우면
    /// 아직 고치는 중인 파일이 사라짐). 같은 압축을 다시 열면 그 폴더를 다시 쓴다.
    /// </summary>
    private readonly List<ArchiveWorkspace> _retiredWorkspaces = new();

    /// <summary>작업 폴더별로 마지막에 맞은 암호(메모리에만 — 파일에 쓰지 않는다). 다시 넣기가 다른 압축을 보는 중에도 다시 묻지 않게.</summary>
    private readonly Dictionary<ArchiveWorkspace, string> _workspacePasswords = new(ReferenceEqualityComparer.Instance);

    /// <summary>연 파일 감시(I3-3 — 처음 파일을 열 때 만든다).</summary>
    private OpenedEntryWatcher? _entryWatcher;

    /// <summary>지금 '다시 넣을까요?'를 묻고 있는 파일(같은 파일을 겹쳐 묻지 않게).</summary>
    private readonly HashSet<OpenedEntry> _asking = new(ReferenceEqualityComparer.Instance);

    /// <summary>바뀜 알림 뒤 조용히 기다리는 시간(기본 1초 — 시험은 줄인다).</summary>
    internal TimeSpan EntryChangeQuietPeriod { get; set; } = TimeSpan.FromSeconds(1);

    /// <summary>이번 실행에서 압축 속 파일을 하나라도 열었는가(창을 닫을 때 고친 파일을 물어볼지 — 해시는 재지 않음).</summary>
    public bool HasOpenedEntries => AllWorkspaces().Any(w => w.OpenedEntries.Count > 0);

    /// <summary>지금 압축에서 연 파일들.</summary>
    public IReadOnlyList<OpenedEntry> OpenedEntries => _workspace?.OpenedEntries ?? Array.Empty<OpenedEntry>();

    /// <summary>시험용: 기다리는 '바뀜' 확인·질문이 모두 끝날 때까지.</summary>
    internal Task WhenEntryChecksIdleAsync() => _entryWatcher?.WhenIdleAsync() ?? Task.CompletedTask;

    // ───────────────────────────── I1-3 안의 파일 열기(최소) ─────────────────────────────

    /// <summary>목록에서 두 번 누르기·Enter: 폴더면 들어가고, 파일이면 꺼내서 기본 프로그램으로 연다.</summary>
    [RelayCommand]
    private Task OpenEntry(ArchiveEntry? entry) => entry is null ? Task.CompletedTask : OpenEntryAsync(entry);

    /// <summary>
    /// 압축 안 파일 하나를 앱 전용 임시 폴더(<c>temp\open\key16\순번\파일 이름</c>)에 그 파일만 꺼내 Windows 기본 프로그램으로 연다(I1-3).
    /// 실행 파일(.exe·.bat·.js …)은 먼저 '그래도 열까요?'(기본 [취소] — 설정 open.warnExecutables 를 끄면 묻지 않음, IJ3-01). 암호는 열 때 맞은 암호를 쓰고, 모르면 묻는다(틀리면 다시).
    /// 원본 압축에 인터넷 표식이 있으면 꺼낸 파일에도 붙인다(오피스 '제한된 보기' 유지). 연 파일은 고쳐 저장하는지 지켜본다(I3-3). 열었으면 true.
    /// </summary>
    public async Task<bool> OpenEntryAsync(ArchiveEntry entry)
    {
        if (entry.IsDirectory)
        {
            // 두 번 누르기와 같게(W2-09 검증 지적): 찾은 결과의 폴더면 찾기를 지우고 그 폴더로 — 예전엔 찾은 결과 목록이 그대로 남아 Enter·우클릭 '열기'가 아무 일도 안 한 것처럼 보였다
            GoToFolder(EditPlanner<object>.Normalize(entry.Path));
            return true;
        }
        if (!IsArchiveMode || CurrentArchivePath is not { } archive) return false;

        string name = entry.Name;
        if (Settings.Open.WarnExecutables && RiskyFileTypes.IsExecutable(name))
        {
            int pick = _ui.Choose(ExecutableWarning(name), new[] { "열기", "취소" }, defaultIndex: 1);
            if (pick != 0)
            {
                StatusText = "열기를 취소했어요";
                return false;
            }
        }

        string key = EditPlanner<object>.Normalize(entry.Path);
        ArchiveWorkspace ws;
        string slot;
        try
        {
            ws = EnsureWorkspace(archive);
            slot = ws.NewSlot();
        }
        catch (Exception ex)
        {
            Logger.LogError("임시 폴더를 만들지 못함", ex);
            ShowProblem("파일을 열 수 없어요.", ex);
            return false;
        }

        // 꺼내기는 옆 폴더(…\1.part\압축 안 폴더\파일)에 하고, 파일만 순번 폴더 바로 아래(…\1\파일)로 옮긴다(깊은 경로를 짧게 — W2-04 와 같음)
        string staging = slot + StagingSuffix;
        var outcome = await ExtractForUseAsync(archive, new[] { key }, staging, "여는 중...", "파일을 열 수 없어요");
        if (outcome != UseOutcome.Done)
        {
            TempFolders.DeleteBestEffort(staging);
            TempFolders.DeleteBestEffort(slot);
            StatusText = outcome == UseOutcome.Cancelled ? "열기를 취소했어요" : "오류 발생";
            return false;
        }
        if (!string.IsNullOrEmpty(_archivePassword)) _workspacePasswords[ws] = _archivePassword;

        string? found = LazyEntryExtractor.FindExtracted(staging, key);
        if (found is null)
        {
            TempFolders.DeleteBestEffort(staging);
            TempFolders.DeleteBestEffort(slot);
            _ui.ShowError("파일을 열 수 없어요:\n\n꺼낸 파일을 찾지 못했어요.");
            StatusText = "오류 발생";
            return false;
        }
        string path = await OpenFlatAsync(found, staging, slot, key);
        if (Settings.Extract.KeepInternetMark) ZoneIdentifier.Copy(archive, path);   // IJ3-03: 설정으로 끌 수 있음(기본 켬)
        // 처음 내용(SHA-256)은 열기 '전에' 잰다(연 프로그램이 고친 뒤 재면 고친 줄 모름). 큰 파일은 몇 초 걸려 뒤에서 잰다.
        var opened = await Task.Run(() => ws.Record(key, path));
        WatchOpenedEntry(opened);
        try
        {
            _services.Shell.OpenFile(path);
        }
        catch (Exception ex)
        {
            Logger.LogError("기본 프로그램으로 열기 실패: " + path, ex);
            string head = ex is Win32Exception { NativeErrorCode: ErrorNoAssociation } ? $"'{name}'을(를) 열 프로그램을 찾지 못했어요." : $"'{name}'을(를) 열지 못했어요.";
            ShowProblem(head, ex);
            StatusText = "오류 발생";
            return false;
        }
        StatusText = $"열었어요: {name}";
        return true;
    }

    /// <summary>ShellExecute 의 '연결된 프로그램 없음'(ERROR_NO_ASSOCIATION).</summary>
    private const int ErrorNoAssociation = 1155;

    /// <summary>꺼내는 옆 폴더 이름 = 순번 폴더 이름 + 이것(<c>1.part</c>) — W2-04 와 같음('~' 는 8.3 이름 풀이 때문에 쓰지 않음).</summary>
    internal const string StagingSuffix = ".part";

    /// <summary>실행 파일 경고 문구(W2-04 와 같음).</summary>
    internal static string ExecutableWarning(string name) =>
        $"'{name}'는 실행 파일이에요. 보낸 사람을 확실히 알 때만 여세요. 그래도 열까요?";

    /// <summary>
    /// 고른 항목만 <paramref name="dest"/> 에 꺼낸다(진행 창·취소·암호 묻기 — 틀리면 다시) — 열기·Ctrl+C·끌기 전 암호 확인·미리 보기 [암호 넣기] 단추가 같이 쓴다.
    /// 지금 보는 압축이면 아는 암호로 먼저 해 보고, 맞은 암호는 기억한다. <paramref name="title"/> 이 null 이면 진행 창 없이(미리 보기).
    /// <paramref name="afterExtract"/> 가 있으면 꺼낸 뒤 같은 진행 창을 띄운 채 뒤 스레드에서 이어서 한다(IJ3-01 — Ctrl+C 의 꺼낸 자리 찾기·표식 붙이기:
    /// 수천 개를 골라도 화면이 굳지 않고 진행·[취소]가 그대로). 그 일이 취소되면 Cancelled, 예외면 오류 창 뒤 Failed.
    /// </summary>
    private async Task<UseOutcome> ExtractForUseAsync(string archive, IReadOnlyCollection<string> keys, string dest, string? title, string errorHead,
        Action<IProgressSession>? afterExtract = null)
    {
        var service = _locator.ResolveByContent(archive);
        if (service is null)
        {
            _ui.ShowError($"{errorHead}:\n\n지원하지 않는 형식이에요.");
            return UseOutcome.Failed;
        }
        bool sameArchive = string.Equals(archive, CurrentArchivePath, StringComparison.OrdinalIgnoreCase);
        string? password = sameArchive ? _archivePassword : null;
        while (true)
        {
            var session = title is null ? new QuietSession() : _ui.ShowProgress(title);
            try
            {
                try
                {
                    await service.ExtractEntriesAsync(archive, dest, keys, password, session.Progress, session.CancellationToken);
                }
                catch (OperationCanceledException)
                {
                    return UseOutcome.Cancelled;
                }
                catch (Exception ex) when (IsPasswordRelated(ex))
                {
                    session.Close();
                    var (reason, message) = PasswordPrompt(ex, password);
                    var pw = _ui.AskPassword(Path.GetFileName(archive), reason, message);
                    if (pw is null) return UseOutcome.Cancelled;
                    password = pw;
                    continue;
                }
                catch (Exception ex)
                {
                    session.Close();
                    Logger.LogError($"{errorHead}: {archive} → {KeysForLog(keys)}", ex);
                    // W2-08(IJ3-02): 문제 창 — 원문은 '자세히', 손상된 ZIP 이면 'ZIP 고치기 시도'
                    await ShowProblemAsync($"{errorHead}.", ex, archive);
                    return UseOutcome.Failed;
                }
                if (sameArchive && !string.IsNullOrEmpty(password)) _archivePassword = password;
                if (afterExtract is null) return UseOutcome.Done;

                // 꺼낸 뒤의 일(뒤 스레드) — 암호 판정과 섞이지 않게 따로 받는다(경로에 '암호'가 든 입출력 오류를 암호 문제로 보지 않게)
                try
                {
                    var ct = session.CancellationToken;
                    await Task.Run(() =>
                    {
                        ct.ThrowIfCancellationRequested();
                        afterExtract(session);
                    }, ct);
                    return UseOutcome.Done;
                }
                catch (OperationCanceledException)
                {
                    return UseOutcome.Cancelled;
                }
                catch (Exception ex)
                {
                    session.Close();
                    Logger.LogError($"{errorHead}(꺼낸 뒤): {archive} → {KeysForLog(keys)}", ex);
                    ShowProblem($"{errorHead}.", ex);
                    return UseOutcome.Failed;
                }
            }
            finally
            {
                session.Close();
            }
        }
    }

    /// <summary>로그에 남길 열쇠(많으면 앞의 몇 개와 개수만).</summary>
    private static string KeysForLog(IReadOnlyCollection<string> keys) =>
        keys.Count <= 5 ? string.Join(", ", keys) : $"{string.Join(", ", keys.Take(5))} 외 {keys.Count - 5}개";

    /// <summary>진행 창 없이(미리 보기의 [암호 넣기]) — 취소 없음.</summary>
    private sealed class QuietSession : IProgressSession, IProgress<ArchiveProgress>
    {
        public IProgress<ArchiveProgress> Progress => this;
        public CancellationToken CancellationToken => CancellationToken.None;
        public void Report(ArchiveProgress value) { }
        public void Close() { }
    }

    /// <summary>꺼낸 파일을 순번 폴더 바로 아래(<c>…\open\key16\1\파일</c>)로 옮긴다(W2-04 와 같음). 못 옮기면 꺼낸 자리 그대로.</summary>
    private static async Task<string> OpenFlatAsync(string found, string staging, string slot, string key)
    {
        string name = Path.GetFileName(EntryPaths.ToSafeRelative(key)).TrimEnd('.', ' ');
        if (name.Length == 0) name = Path.GetFileName(found);
        string target;
        try
        {
            target = await Task.Run(() => OutputFiles.MoveNew(found, Path.Combine(slot, name)));
        }
        catch (Exception ex)
        {
            Logger.LogError($"열 파일을 순번 폴더로 옮기지 못함(꺼낸 자리에서 엶): {found}", ex);
            return found;
        }
        TempFolders.DeleteBestEffort(staging);
        return target;
    }

    /// <summary>이 압축의 작업 폴더: 지금 것이 맞으면 그대로, 쉬게 한 것 중 맞는 것이 있으면 다시 쓰고, 없으면 새로(W2-04 와 같음).</summary>
    private ArchiveWorkspace EnsureWorkspace(string archive)
    {
        if (_workspace is { } ws && IsWorkspaceOf(ws, archive))
            return ws;
        RetireWorkspace();
        var revived = _retiredWorkspaces.FirstOrDefault(w => IsWorkspaceOf(w, archive));
        if (revived is not null)
        {
            _retiredWorkspaces.Remove(revived);
            _workspace = revived;
            return revived;
        }
        _workspace = ArchiveWorkspace.Open(archive);
        return _workspace;
    }

    private static bool IsWorkspaceOf(ArchiveWorkspace ws, string archive) =>
        string.Equals(ws.ArchivePath, Path.GetFullPath(archive), StringComparison.OrdinalIgnoreCase) && ws.Matches(archive);

    /// <summary>지금 작업 폴더를 '쉬는' 목록으로 — 지우지 않는다(앱을 닫을 때 <see cref="Shutdown"/>). 감시는 그대로(그 파일을 고쳐도 묻는다).</summary>
    private void RetireWorkspace()
    {
        var ws = _workspace;
        _workspace = null;
        if (ws is null || ws.IsDisposed || _retiredWorkspaces.Contains(ws)) return;
        _retiredWorkspaces.Add(ws);
    }

    /// <summary>이 실행에서 만든 작업 폴더 전부(지금 것 + 쉬는 것).</summary>
    private IEnumerable<ArchiveWorkspace> AllWorkspaces() =>
        (_workspace is null ? Enumerable.Empty<ArchiveWorkspace>() : new[] { _workspace }).Concat(_retiredWorkspaces).Where(w => !w.IsDisposed);

    // ───────────────────────────── I3-3 고친 파일 다시 넣기 ─────────────────────────────

    private void WatchOpenedEntry(OpenedEntry entry)
    {
        _entryWatcher ??= new OpenedEntryWatcher(_services.FileChanges, OnOpenedEntryChangedFromWatcher);
        _entryWatcher.QuietPeriod = EntryChangeQuietPeriod;
        _entryWatcher.Watch(entry);
    }

    /// <summary>감시가 알린 '내용이 바뀜'(다른 스레드) → 화면 스레드에서 묻는다(시험은 그 자리에서).</summary>
    private Task OnOpenedEntryChangedFromWatcher(OpenedEntry entry)
    {
        if (_uiContext is null) return OnOpenedEntryChangedAsync(entry);
        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _uiContext.Post(async _ =>
        {
            try
            {
                await OnOpenedEntryChangedAsync(entry);
                done.TrySetResult();
            }
            catch (Exception ex)
            {
                done.TrySetException(ex);
            }
        }, null);
        return done.Task;
    }

    /// <summary>
    /// 연 파일을 고쳐 저장했다(I3-3): ‘‘보고서.xlsx’가 바뀌었어요. 압축 파일에 다시 넣을까요?’ [넣기][나중에][넣지 않기].
    /// 넣기 = 같은 폴더·같은 이름으로 바꿔 넣기(한 번의 고치기 — 원본은 검사 뒤에만 바뀜, 연 뒤에 그 항목을 지웠으면 제자리에 새로 — <see cref="ReinsertAsync"/>).
    /// 나중에 = 압축을 닫거나 일집을 닫을 때 다시 묻는다.
    /// 넣지 않기 = 이번 내용은 넣지 않는다(또 고치면 다시 묻는다). 고칠 수 없는 형식(RAR 등)이면 ‘다른 곳에 저장할까요?’ → 저장 창 → 복사.
    /// </summary>
    internal async Task OnOpenedEntryChangedAsync(OpenedEntry entry)
    {
        if (entry.Postponed || !BeginAsking(entry)) return;
        try
        {
            if (!await Task.Run(entry.IsModified)) return;
            await AskAndReinsertAsync(entry, ReinsertMoment.Changed);
        }
        finally
        {
            EndAsking(entry);
        }
    }

    private bool BeginAsking(OpenedEntry entry)
    {
        lock (_asking) return _asking.Add(entry);
    }

    private void EndAsking(OpenedEntry entry)
    {
        lock (_asking) _asking.Remove(entry);
    }

    /// <summary>언제 묻는가(문구·단추가 조금 다름).</summary>
    private enum ReinsertMoment
    {
        /// <summary>방금 저장함.</summary>
        Changed,
        /// <summary>압축을 닫거나 다른 압축을 엶(일집은 그대로 — 나중에 또 물을 수 있음).</summary>
        Leaving,
        /// <summary>일집을 닫음(임시 폴더가 지워짐 — [취소]로 닫기를 멈출 수 있음).</summary>
        Exiting,
    }

    /// <summary>
    /// 묻고 고른 대로 한다. 일집 닫기를 멈춰야 하면(닫을 때 [취소]) false.
    /// 기준은 '실제로 넣은(저장한) 바이트'·'물어본 내용'의 표로 다시 잡는다 — 넣는 동안이나 묻는 동안 또 저장한 내용은 바뀐 것으로 남아,
    /// 넣고 나서 바로 다시 묻는다(W2-06 검증 지적: 넣는 동안 저장한 내용이 기준이 되어 말없이 사라지던 문제).
    /// 넣지 못했으면(오류 창을 보인 뒤) 고친 내용을 잃지 않게 '다른 곳에 저장'을 권한다.
    /// </summary>
    private async Task<bool> AskAndReinsertAsync(OpenedEntry entry, ReinsertMoment moment)
    {
        string? archive = entry.ArchivePath;
        bool editable = archive is not null && File.Exists(archive) && ArchiveServiceLocator.CanEdit(archive);
        string notEditable = NotEditableShortFor(archive);
        while (true)
        {
            // 물어보는 내용의 표 — [넣지 않기]면 이 내용만 넘긴다(묻는 동안 또 저장한 내용은 다시 묻게). 이름을 바꿨으면 새 이름을 따라간 뒤(I5-A)
            var asked = await Task.Run(entry.Stamp);
            string name = entry.Name;
            string subject = SubjectOf(entry);
            // I5-A: 편집기가 이름을 바꿨어도 압축에는 원래 항목 이름으로 넣는다(그 자리를 바꿔 넣기 — 압축의 다른 항목·이름은 그대로)
            string renamed = entry.IsRenamed && editable ? $" 압축 파일에는 원래 이름(‘{entry.ExtractedName}’) 그대로 넣어요." : string.Empty;
            // I5-A: 닫을 때 그 파일을 아직 다른 프로그램이 잡고 있으면 — 닫은 뒤 거기서 또 저장한 내용은 바로 들어가지 않음을 함께 알린다
            string stillOpen = moment == ReinsertMoment.Exiting && await Task.Run(entry.IsHeldByOtherProgram) ? "\n\n" + StillOpenNote : string.Empty;
            string message = moment switch
            {
                ReinsertMoment.Changed => editable
                    ? $"{subject} 바뀌었어요. 압축 파일에 다시 넣을까요?{renamed}"
                    : $"{subject} 바뀌었어요. {notEditable} 바뀐 파일을 다른 곳에 저장할까요?",
                ReinsertMoment.Exiting => (editable
                    ? $"{subject} 바뀌었는데 압축 파일에 아직 넣지 않았어요. 일집을 닫으면 바뀐 내용이 없어져요. 압축 파일에 넣을까요?{renamed}"
                    : $"{subject} 바뀌었는데 아직 저장하지 않았어요. 일집을 닫으면 바뀐 내용이 없어져요. 다른 곳에 저장할까요?") + stillOpen,
                _ => editable
                    ? $"{subject} 바뀌었는데 압축 파일에 아직 넣지 않았어요. 지금 넣을까요?{renamed}"
                    : $"{subject} 바뀌었는데 아직 저장하지 않았어요. 다른 곳에 저장할까요?",
            };
            string doIt = editable ? "넣기" : "다른 곳에 저장";
            string skip = editable ? "넣지 않기" : "저장하지 않기";
            var choices = moment == ReinsertMoment.Exiting
                ? new[] { doIt, skip + "(닫기)", "취소" }
                : new[] { doIt, "나중에", skip };
            int pick = _ui.Choose(message, choices, defaultIndex: 0);

            if (pick == 0)
            {
                var (done, failed) = editable ? await ReinsertAsync(entry) : (SaveChangedCopy(entry), false);
                // 넣지 못했으면(오류 창은 이미 보임) 고친 내용을 잃지 않게 다른 곳에 저장할 길을 준다(취소한 것이면 묻지 않음)
                if (done is null && failed) done = OfferSaveAfterFailure(entry, moment);
                if (done is not null)
                {
                    entry.Rebase(done);
                    // 넣는 동안(저장 창을 보는 동안) 또 저장했으면 — 넣은 내용과 다름 — 그 내용으로 다시 묻는다
                    if (await Task.Run(entry.IsModified)) continue;
                    return true;
                }
                if (moment == ReinsertMoment.Exiting) return false;   // 넣지 못함·취소 — 닫지 않고 다시 할 수 있게
                entry.Postponed = true;
                return true;
            }
            if (moment == ReinsertMoment.Exiting)
            {
                // [넣지 않기(닫기)]면 물어본 내용은 넘긴 것으로(I5-A — 닫은 뒤 남긴 파일을 다음 시작 때 '넣지 않은 고친 파일'로 다시 알리지 않게) 닫고,
                // [취소]·창 닫기면 닫지 않음
                if (pick != 1) return false;
                entry.Rebase(asked);
                return true;
            }
            if (pick == 2)
            {
                entry.Rebase(asked);   // 물어본 내용은 넣지 않음 — 또 고치면 다시 묻는다
                StatusText = $"‘{name}’은(는) 압축 파일에 넣지 않았어요";
            }
            else
            {
                entry.Postponed = true;   // 나중에(창 닫기 포함) — 압축을 닫거나 일집을 닫을 때 다시 묻는다
            }
            return true;
        }
    }

    /// <summary>
    /// 질문의 주어: ‘보고서.txt’가 — 편집기가 이름을 바꿨으면 ‘새 이름.pdf’(압축 안 이름 ‘한글 이름.pdf’)가(I5-A).
    /// </summary>
    internal static string SubjectOf(OpenedEntry entry) => DisplayNameOf(entry) + Josa.IGa(entry.Name);

    /// <summary>‘보고서.txt’ — 이름을 바꿨으면 ‘새 이름.pdf’(압축 안 이름 ‘한글 이름.pdf’)(I5-A).</summary>
    internal static string DisplayNameOf(OpenedEntry entry) => DisplayName(entry.Name, entry.IsRenamed ? entry.ExtractedName : null);

    internal static string DisplayName(string name, string? originalName) =>
        originalName is null ? $"‘{name}’" : $"‘{name}’(압축 안 이름 ‘{originalName}’)";

    /// <summary>닫을 때 그 파일이 아직 다른 프로그램에 열려 있을 때 덧붙이는 말(I5-A).</summary>
    internal const string StillOpenNote =
        "이 파일은 아직 다른 프로그램에서 열려 있어요. 일집을 닫은 뒤 그 프로그램에서 또 저장한 내용은 압축 파일에 바로 들어가지 않아요(지우지 않고 남겨 두었다가 다음에 일집을 켤 때 넣을지 물어볼게요).";

    /// <summary>
    /// '이 형식은 고칠 수 없어요.'(ArchiveNotEditableException 문구의 앞 문장) — 7z 인데 이 PC 에서 7z.dll 을 못 불러오면 '이 PC에서는 7Z 파일을 고칠 수 없어요.'.
    /// </summary>
    private static string NotEditableShortFor(string? archive) =>
        ArchiveServiceLocator.NotEditableMessage(archive) == ArchiveNotEditableException.SevenZipEngineMessage
            ? "이 PC에서는 7Z 파일을 고칠 수 없어요."
            : "이 형식은 고칠 수 없어요.";

    /// <summary>다시 넣을 내용을 잠깐 복사해 두는 옆 폴더 이름 = 연 파일 폴더 이름 + 이것(<c>1.reinsert</c> — 작업 폴더 안이라 닫을 때 같이 지워짐).</summary>
    internal const string ReinsertSuffix = ".reinsert";

    /// <summary>
    /// 고친 파일을 압축에 다시 넣는다: 같은 폴더에 같은 이름으로, 있던 항목은 바꿔 넣기(한 번의 고치기 — 연 뒤에 압축에서 그 항목을 지웠거나 이름을 바꿨으면
    /// 원래 자리에 새로 넣는다). 넣을 내용은 먼저 옆 폴더에 복사해 고정하고 그 바이트의 표를 돌려준다(넣는 동안 또 저장해도 섞이지 않음).
    /// 지금 보는 압축이면 목록을 다시 읽는다. 못 넣었으면 표는 null — 실패(오류 창을 보임)면 Failed, 취소면 아님.
    /// </summary>
    private async Task<(ContentStamp? Inserted, bool Failed)> ReinsertAsync(OpenedEntry entry)
    {
        string archive = entry.ArchivePath!;
        var (folder, leaf) = SplitEntryKey(entry.EntryKey);   // I5-A: 이름을 바꿨어도 압축 안 원래 이름으로
        string snapshotDir = Path.GetDirectoryName(entry.TempPath)! + ReinsertSuffix;
        string snapshot = Path.Combine(snapshotDir, Path.GetFileName(entry.TempPath));
        try
        {
            ContentStamp inserted;
            try
            {
                inserted = await Task.Run(() => entry.CopyTo(snapshot));
            }
            catch (Exception ex)
            {
                Logger.LogError("다시 넣을 파일 복사 실패: " + entry.TempPath, ex);
                ShowProblem("고친 파일을 읽지 못해 압축 파일에 넣지 못했어요.", ex);
                return (null, true);
            }
            var ops = new EditOp[] { new EditOp.Add(new[] { snapshot }, folder, AsName: leaf, ReplaceExisting: true) };
            var failed = new StrongBox<bool>();
            var result = await RunEditAsync(archive, ops, "압축 파일에 다시 넣는 중...", PasswordFor(archive), failed);
            if (result is null) return (null, failed.Value);
            StatusText = $"‘{leaf}’을(를) 압축 파일에 다시 넣었어요";
            return (inserted, false);
        }
        finally
        {
            TempFolders.DeleteBestEffort(snapshotDir);
        }
    }

    /// <summary>압축 안 경로 → (부모 폴더 열쇠, 마지막 이름) — 다시 넣기는 늘 이 자리·이 이름(바꿔 넣기).</summary>
    internal static (string Folder, string Leaf) SplitEntryKey(string entryKey)
    {
        string key = EditPlanner<object>.Normalize(entryKey);
        string folder = EditPlanner<object>.ParentOf(key);
        return (folder, folder.Length == 0 ? key : key[(folder.Length + 1)..]);
    }

    /// <summary>다시 넣기에 실패했을 때: ‘…’을(를) 압축 파일에 넣지 못했어요. 고친 내용을 잃지 않게 다른 곳에 저장할까요? — 저장했으면 그 표.</summary>
    private ContentStamp? OfferSaveAfterFailure(OpenedEntry entry, ReinsertMoment moment)
    {
        string name = entry.Name;
        int pick = _ui.Choose(
            $"‘{name}’{Josa.EulReul(name)} 압축 파일에 넣지 못했어요. 고친 내용을 잃지 않게 다른 곳에 저장할까요?",
            new[] { "다른 곳에 저장", moment == ReinsertMoment.Exiting ? "닫지 않기" : "나중에" }, defaultIndex: 0);
        return pick == 0 ? SaveChangedCopy(entry) : null;
    }

    /// <summary>그 압축에 맞았던 암호(지금 압축이면 그 암호, 아니면 그 작업 폴더에 기억한 것). 없으면 null.</summary>
    private string? PasswordFor(string archive)
    {
        if (string.Equals(archive, CurrentArchivePath, StringComparison.OrdinalIgnoreCase) && !string.IsNullOrEmpty(_archivePassword))
            return _archivePassword;
        foreach (var (ws, pw) in _workspacePasswords)
            if (string.Equals(ws.ArchivePath, Path.GetFullPath(archive), StringComparison.OrdinalIgnoreCase)) return pw;
        return null;
    }

    /// <summary>
    /// 고칠 수 없는 압축(RAR 등)에서 연 파일을 고쳤을 때(또는 다시 넣기에 실패했을 때): 저장 창 → 복사. 저장했으면 복사한 바이트의 표, 아니면 null.
    /// </summary>
    private ContentStamp? SaveChangedCopy(OpenedEntry entry)
    {
        string name = entry.Name;
        string initialDir = entry.ArchivePath is { } a && Path.GetDirectoryName(a) is { } d ? d : Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        string ext = Path.GetExtension(name);
        string filter = ext.Length > 0 ? $"{ext.TrimStart('.').ToUpperInvariant()} 파일 (*{ext})|*{ext}|모든 파일 (*.*)|*.*" : "모든 파일 (*.*)|*.*";
        var dest = _ui.AskSavePath("바뀐 파일 저장", filter, name, initialDir, ext.TrimStart('.'));
        if (string.IsNullOrEmpty(dest)) return null;
        try
        {
            var saved = entry.CopyTo(dest);
            StatusText = $"바뀐 파일을 저장했어요: {dest}";
            return saved;
        }
        catch (Exception ex)
        {
            Logger.LogError("바뀐 파일 저장 실패: " + dest, ex);
            ShowProblem("바뀐 파일을 저장하지 못했어요.", ex);
            return null;
        }
    }

    /// <summary>아직 넣지 않은 고친 파일(작업 폴더들 — 지운 것·내용이 같은 것은 빼고).</summary>
    private async Task<List<OpenedEntry>> PendingChangesAsync(IEnumerable<ArchiveWorkspace> workspaces)
    {
        var all = workspaces.SelectMany(w => w.OpenedEntries).ToList();
        return await Task.Run(() => all.Where(e => e.IsModified()).ToList());
    }

    /// <summary>
    /// 지금 압축을 떠나기 전(비우기·다른 압축 열기): 이 압축에서 연 파일 중 고쳐 놓고 아직 넣지 않은 것을 하나씩 묻는다(I3-3 '나중에'의 다시 묻기).
    /// 방금 저장해 아직 조용히 기다리는 확인은 끝나기를 기다리지만, 이미 묻거나 넣는 중인 파일은 기다리지도 묻지도 않는다 — 그 흐름이 끝까지 맡는다
    /// (작업 폴더는 앱을 닫을 때까지 남고 넣기는 그 압축 경로로 한다). W2-06 검증 지적 2차: 명령줄 열기(--open 등)는 명령 차례를 쥔 채 여기로 오는데,
    /// 묻는 중 [넣기]는 그 차례를 기다리므로 묻기·넣기까지 기다리면(예전 WhenIdleAsync) 서로를 기다려 영영 멈췄다.
    /// </summary>
    private async Task AskPendingBeforeLeavingAsync()
    {
        if (_workspace is not { } ws) return;
        if (_entryWatcher is { } w) await w.WhenSettledAsync();
        foreach (var e in await PendingChangesAsync(new[] { ws }))
        {
            if (!BeginAsking(e)) continue;   // 다른 흐름이 묻거나 넣는 중 — 그쪽이 맡는다
            try
            {
                await AskAndReinsertAsync(e, ReinsertMoment.Leaving);
            }
            finally
            {
                EndAsking(e);
            }
        }
    }

    /// <summary>
    /// 일집을 닫기 전(창 닫기): 고쳐 놓고 아직 넣지 않은 파일이 있으면 하나씩 묻는다 — [넣기][넣지 않기(닫기)][취소].
    /// [취소]하거나 넣다가 실패하면 false(닫지 않음). 다 끝났으면 true.
    /// 다른 흐름이 '다시 넣을까요?'를 묻거나 다시 넣는 중인 파일이 있으면 닫지 않는다(false — ‘…’를 압축 파일에 다시 넣는 일이 아직 끝나지 않았어요…).
    /// 진행 창은 모달이 아니라 넣는 중에도 메인 창을 닫을 수 있는데, 그대로 닫으면 작업 폴더(고친 파일·넣을 사본)가 지워지고 앱이 끝나 넣기도 끊긴다(W2-06 검증 지적 2차).
    /// I5-A(X3-F1·I3-3-close): 고친 것은 없어도 연 파일을 다른 프로그램이 아직 열어 두고 있으면(Light PDF·워드·엑셀·한글은 연 동안 파일을 잡음) 한 번 알린다 —
    /// ‘압축 속에서 연 파일 N개가 아직 다른 프로그램에서 열려 있어요 … 일집을 닫을까요?’ [닫기][취소]. 일집을 닫은 뒤 거기서 저장한 내용은 압축에 바로 들어가지 않지만
    /// 지워지지도 않는다(닫을 때 그 순번 폴더를 남기고 다음 시작 때 청소가 알림 — <see cref="ArchiveWorkspace.Dispose"/>·<see cref="TempJanitor"/>).
    /// 위 '고친 파일' 질문을 이미 받은 파일은 그 질문에 같은 말을 덧붙였으므로 다시 세지 않는다. 파일을 잡지 않는 프로그램(메모장 등)은 알 수 없어 묻지 않는다.
    /// </summary>
    public async Task<bool> ConfirmExitAsync()
    {
        if (AskingEntry() is { } busy) return RefuseExitWhileAsking(busy);
        var asked = new HashSet<OpenedEntry>(ReferenceEqualityComparer.Instance);
        foreach (var e in await PendingChangesAsync(AllWorkspaces()))
        {
            if (!BeginAsking(e)) return RefuseExitWhileAsking(e);
            try
            {
                if (!await AskAndReinsertAsync(e, ReinsertMoment.Exiting)) return false;
                asked.Add(e);
            }
            finally
            {
                EndAsking(e);
            }
        }
        var all = AllWorkspaces().SelectMany(w => w.OpenedEntries).Where(e => !asked.Contains(e)).ToList();
        var stillOpen = await Task.Run(() => all.Where(e => e.IsHeldByOtherProgram()).ToList());
        if (stillOpen.Count == 0) return true;
        int pick = _ui.Choose(StillOpenOnExitMessage(stillOpen), new[] { "닫기", "취소" }, defaultIndex: 0);
        return pick == 0;   // [취소]·창 닫기면 닫지 않음
    }

    /// <summary>닫을 때 '아직 다른 프로그램에서 열려 있는 파일' 알림(I5-A) — 이름은 다섯 개까지.</summary>
    internal static string StillOpenOnExitMessage(IReadOnlyList<OpenedEntry> entries)
    {
        var lines = entries.Take(5).Select(e => $"- {DisplayNameOf(e)}" + (e.ArchivePath is { } a ? $" — {Path.GetFileName(a)}" : string.Empty)).ToList();
        if (entries.Count > 5) lines.Add($"외 {entries.Count - 5}개");
        return $"압축 속에서 연 파일 {entries.Count}개가 아직 다른 프로그램에서 열려 있어요.\n{string.Join("\n", lines)}\n\n" +
               "일집을 닫은 뒤 그 프로그램에서 저장한 내용은 압축 파일에 바로 들어가지 않아요. " +
               "고친 파일은 지우지 않고 남겨 두었다가 다음에 일집을 켤 때 압축 파일에 넣을지 물어볼게요.\n\n일집을 닫을까요?";
    }

    /// <summary>지금 묻거나 다시 넣는 중인 파일 하나(없으면 null).</summary>
    private OpenedEntry? AskingEntry()
    {
        lock (_asking) return _asking.FirstOrDefault();
    }

    /// <summary>묻거나 넣는 중이라 닫지 않는다는 안내 — 늘 false(닫지 않음).</summary>
    private bool RefuseExitWhileAsking(OpenedEntry entry)
    {
        _ui.ShowInfo(ExitWhileReinsertingMessage(entry.Name));
        return false;
    }

    /// <summary>다시 넣는 일(묻기·넣기)이 끝나지 않았을 때 닫기를 멈추는 안내.</summary>
    internal static string ExitWhileReinsertingMessage(string name) =>
        $"‘{name}’{Josa.EulReul(name)} 압축 파일에 다시 넣는 일이 아직 끝나지 않았어요. 끝난 뒤에 닫아 주세요.";

    /// <summary>
    /// 일집을 닫을 때(창이 닫힘): 감시를 멈추고 작업 폴더를 모두 지운다(다른 프로그램이 잡은 파일은 남김 — 예외 없음).
    /// IJ3-01: 남은 끌기의 임시 폴더도 지우고, Ctrl+C 사본은 마지막 것만 남긴다(아직 클립보드에 있을 수 있음 — 일집을 꺼도 붙여넣기 되게, 24시간 뒤 청소).
    /// I5-A: 다른 프로그램이 아직 잡고 있는 연 파일·넣지도 넘기지도 않은 고친 파일은 그 순번 폴더째 남기고 연 파일 목록도 남긴다(<see cref="ArchiveWorkspace.Dispose"/>) —
    /// 다음 시작 때 청소가 보고 그 뒤 고쳐 저장했으면 알린다(<see cref="OfferLeftoverChangesAsync"/>).
    /// </summary>
    public void Shutdown()
    {
        _entryWatcher?.Dispose();
        _entryWatcher = null;
        foreach (var ws in AllWorkspaces().ToList()) ws.Dispose();
        _workspace = null;
        _retiredWorkspaces.Clear();
        _workspacePasswords.Clear();
        ShutdownEntryCopy();
    }
}

/// <summary>
/// 받침에 따라 '이/가'·'을/를' 고르기(W2-06 — ‘보고서.xlsx’가·‘자료’가·‘사진’이). 마지막 글자가 한글이면 받침으로,
/// 영문자는 읽는 소리(L·M·N·R 은 받침 있음), 숫자도 읽는 소리(0·1·3·6·7·8 은 받침 있음)로. 그 밖은 '이(가)'.
/// </summary>
internal static class Josa
{
    public static string IGa(string word) => HasFinal(word) switch { true => "이", false => "가", null => "이(가)" };

    public static string EulReul(string word) => HasFinal(word) switch { true => "을", false => "를", null => "을(를)" };

    private static bool? HasFinal(string word)
    {
        string w = (word ?? string.Empty).TrimEnd(' ', ')', ']', '’', '\'', '"');
        if (w.Length == 0) return null;
        char c = w[^1];
        if (c is >= '가' and <= '힣') return (c - '가') % 28 != 0;
        if (char.IsAsciiLetter(c)) return "lmnrLMNR".Contains(c);
        if (char.IsAsciiDigit(c)) return "013678".Contains(c);
        return null;
    }
}
