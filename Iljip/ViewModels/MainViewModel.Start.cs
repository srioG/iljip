using System.Collections.ObjectModel;
using System.IO;
using CommunityToolkit.Mvvm.Input;
using Iljip.Models;
using Iljip.Services;

namespace Iljip.ViewModels;

// 시작 화면·최근 파일·연 압축 파일 관리(W2-11 — I3-15): 일집을 그냥 켰을 때(압축도 모으는 목록도 없을 때) 목록 자리에 시작 화면
// (큰 단추 [압축 파일 열기][압축하기][압축 풀기…]·끌어다 놓기 안내·'최근 파일'), 연 압축의 '다른 이름으로 저장'(같은 형식은 복사, ZIP↔7Z 는 풀고 다시 압축)·
// '이 압축 파일 지우기'(휴지통 — 묻고, 먼저 닫음), '새 창'(Ctrl+N — 단일 인스턴스를 건너뛰는 일집 하나 더).
// 최근 목록은 열 때마다 메모리에만 넣고 창을 닫을 때 한 번 저장한다(목록에서 빼기·모두 지우기는 바로 — DLP 연속 재기록 경합 교훈).
// 저장은 SaveSettings(이 창에서 넣고 뺀 것만 최신 목록에 얹음 — W2-11 검증 지적, 창 두 개).
// 묶음마다 partial 로 나눠 충돌을 줄인다(공통 설계 §0).
public partial class MainViewModel
{
    // ───────────────────────────── 시작 화면 ─────────────────────────────

    /// <summary>시작 화면을 보일 때 — 압축 파일도 모으는 목록도 없을 때(열면 숨고, 닫거나 지우면 다시 보임).</summary>
    public bool IsStartVisible => IsEmptyState;

    /// <summary>시작 화면 '최근 파일'(최근 순, 최대 10개). <see cref="RefreshRecentFiles"/> 로 채운다(창이 뜰 때·목록이 바뀔 때).</summary>
    public ObservableCollection<RecentFileItem> RecentFiles { get; } = new();

    /// <summary>최근 파일이 하나라도 있는가(없으면 '아직 연 압축 파일이 없어요').</summary>
    public bool HasRecentFiles => RecentFiles.Count > 0;

    /// <summary>찾을 수 없는(흐린 '없음') 최근 파일이 있는가 — 있으면 '없는 파일 정리' 단추가 보인다.</summary>
    public bool HasMissingRecentFiles => RecentFiles.Any(i => i.IsMissing);

    private RecentFilesService? _recent;

    private RecentFilesService Recent => _recent ??= new RecentFilesService(Settings, IsRecentExcluded, _services.Clock);

    /// <summary>최근 목록에 넣지 않는 곳: 일집 임시 폴더(압축 안의 압축)·Windows 임시 폴더·Outlook 첨부 임시 폴더(곧 사라짐).</summary>
    private bool IsRecentExcluded(string path) => _services.TempLocations.IsTemporary(path);

    /// <summary>최근 파일 목록을 설정에서 다시 채운다(없는 파일은 '없음' — 네트워크 경로는 보지 않고 있음으로).</summary>
    public void RefreshRecentFiles()
    {
        IReadOnlyList<RecentFileItem> items;
        try
        {
            items = Recent.Items();
        }
        catch (Exception ex)
        {
            Logger.LogError("최근 파일 목록을 읽지 못함", ex);
            items = Array.Empty<RecentFileItem>();
        }
        RecentFiles.Clear();
        foreach (var i in items) RecentFiles.Add(i);
        OnPropertyChanged(nameof(HasRecentFiles));
        OnPropertyChanged(nameof(HasMissingRecentFiles));
    }

    /// <summary>연 압축 파일을 최근 목록 맨 앞에(메모리에만 — 저장은 창을 닫을 때 <see cref="SaveOnExit"/>, 또는 그 전에 다른 설정을 저장할 때 함께).</summary>
    private void RememberRecentFile(string archivePath)
    {
        try
        {
            if (!Recent.Add(archivePath)) return;
            RefreshRecentFiles();
        }
        catch (Exception ex)
        {
            Logger.LogError("최근 파일에 넣지 못함: " + archivePath, ex);   // 열기는 됐다 — 창은 띄우지 않음
        }
    }

    /// <summary>시작 화면 [압축하기]: 파일을 골라 모으는 목록에 넣고 한 화면 압축 창을 띄운다(폴더는 창에 끌어다 놓기).</summary>
    [RelayCommand]
    private async Task StartCompressAsync()
    {
        var picked = _ui.PickFiles("압축할 파일 고르기 (여러 개 가능 — 폴더는 창에 끌어다 놓으세요)", null, multiselect: true);
        if (picked is not { Count: > 0 }) return;
        AddToStaging(picked);
        if (IsStagingMode && CompressCommand.CanExecute(null))
            await CompressCommand.ExecuteAsync(null);
    }

    /// <summary>
    /// 시작 화면 [압축 풀기…]: 압축 파일을 고르고 풀기 창으로 푼다. 하나면 열어 보이며 풀고('--extract' 와 같음), 여럿이면 풀기 창 한 번 + 여러 개 풀기.
    /// </summary>
    [RelayCommand]
    private async Task StartExtractAsync()
    {
        var picked = _ui.PickFiles("풀 압축 파일 고르기 (여러 개 가능)", BuildOpenFilter(), multiselect: true);
        if (picked is not { Count: > 0 }) return;
        var archives = picked.Where(File.Exists).Select(Path.GetFullPath).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (archives.Count == 0) return;
        if (archives.Count == 1)
        {
            if (await OpenArchiveAsync(archives[0]))
                await ExtractAsync();
            return;
        }
        var target = AskExtractTarget(archives, selectedOnly: false, $"압축 파일 {archives.Count}개를 풀 폴더를 선택하세요");
        if (target is null) return;
        await RunExtractBatchWithTargetAsync(archives, target);
    }

    // ───────────────────────────── 최근 파일 ─────────────────────────────

    /// <summary>없는 최근 파일을 눌렀을 때 묻는 문구.</summary>
    internal static string RecentMissingQuestion(string name) =>
        $"‘{name}’{Josa.EulReul(name)} 찾을 수 없어요. 지웠거나 다른 곳으로 옮긴 것 같아요.\n최근 파일 목록에서 뺄까요?";

    /// <summary>
    /// 최근 파일 하나를 누름: 있으면 연다. 없으면 '목록에서 뺄까요?'([목록에서 빼기][그대로 두기]).
    /// 있는지는 작업 스레드에서 5초까지만 본다(끊긴 NAS 를 보다 창이 굳지 않게 — 모르면 열어 보고, 못 열면 열기의 오류 창).
    /// </summary>
    [RelayCommand]
    private async Task OpenRecentAsync(RecentFileItem? item)
    {
        if (item is null) return;
        var probe = Task.Run(() => SafeFileExists(item.Path));
        bool exists = await Task.WhenAny(probe, Task.Delay(TimeSpan.FromSeconds(5))) != probe || probe.Result;
        if (!exists)
        {
            int pick = _ui.Choose(RecentMissingQuestion(item.Name), new[] { "목록에서 빼기", "그대로 두기" }, defaultIndex: 0);
            if (pick == 0) RemoveRecentCore(item.Path);
            else RefreshRecentFiles();
            return;
        }
        await OpenArchiveAsync(item.Path);
    }

    /// <summary>최근 파일 오른쪽 클릭 '목록에서 지우기'(파일은 그대로) — 바로 저장.</summary>
    [RelayCommand]
    private void RemoveRecent(RecentFileItem? item)
    {
        if (item is not null) RemoveRecentCore(item.Path);
    }

    /// <summary>
    /// 최근 파일 오른쪽 클릭 '모두 지우기' — 묻고(기본 [취소]) 비운 뒤 바로 저장. 저장된 목록도 모두 비운다
    /// (다른 창이 그사이 저장한 줄까지 — 보이는 목록이 비어야 하므로).
    /// </summary>
    [RelayCommand]
    private void ClearRecent()
    {
        if (Recent.Files.Count == 0) return;
        int pick = _ui.Choose("최근 파일 목록을 모두 지울까요?\n(목록만 지워요. 파일은 그대로 있어요)", new[] { "모두 지우기", "취소" }, defaultIndex: 1);
        if (pick != 0) return;
        Recent.Clear();
        SaveSettings(s => s.Recent.Files.Clear());
        RefreshRecentFiles();
        StatusText = "최근 파일 목록을 지웠어요";
    }

    /// <summary>'없는 파일 정리'의 질문(몇 개·이름 셋까지).</summary>
    internal static string RemoveMissingQuestion(IReadOnlyList<string> names)
    {
        string shown = string.Join(", ", names.Take(3).Select(n => $"‘{n}’")) + (names.Count > 3 ? $" 외 {names.Count - 3}개" : string.Empty);
        return $"찾을 수 없는 파일 {names.Count}개({shown})를 최근 파일 목록에서 뺄까요?\n" +
               "(목록만 정리해요. USB 를 뺐거나 네트워크가 끊겨 안 보이는 것이면 다시 연결한 뒤에 하세요)";
    }

    /// <summary>
    /// 최근 파일 '없는 파일 정리'(제목 옆 단추·오른쪽 클릭 메뉴 — W2-11 검증 지적: 하나씩만 뺄 수 있었음): 찾을 수 없는 줄(흐린 '없음')만 한꺼번에 묻고(기본 [목록에서 빼기]) 뺀 뒤 바로 저장.
    /// 있는 파일은 그대로, 네트워크 경로(NAS·공유 폴더)는 보지 않으므로 빼지 않는다(끊긴 NAS 로 굳지 않게 — 그 줄은 누를 때 확인). 없으면 그렇다고만 알림.
    /// </summary>
    [RelayCommand]
    private void RemoveMissingRecent()
    {
        RefreshRecentFiles();   // 지금 있는지 다시 본다
        var missing = RecentFiles.Where(i => i.IsMissing).ToList();
        if (missing.Count == 0)
        {
            _ui.ShowInfo("찾을 수 없는 파일이 없어요. 최근 파일이 모두 그 자리에 있어요.");
            return;
        }
        int pick = _ui.Choose(RemoveMissingQuestion(missing.Select(i => i.Name).ToList()), new[] { "목록에서 빼기", "취소" }, defaultIndex: 0);
        if (pick != 0) return;
        foreach (var m in missing) Recent.Remove(m.Path);
        SaveSettings();
        RefreshRecentFiles();
        StatusText = $"찾을 수 없는 파일 {missing.Count}개를 최근 파일 목록에서 뺐어요";
    }

    /// <summary>최근 파일 오른쪽 클릭 '새 창으로 열기'(창 두 개로 나란히).</summary>
    [RelayCommand]
    private void OpenRecentInNewWindow(RecentFileItem? item)
    {
        if (item is null) return;
        StartNewWindow(new ParsedCommand(CommandVerb.Open, item.Path, null, Array.Empty<string>()));
    }

    /// <summary>최근 파일 오른쪽 클릭 '들어 있는 폴더 열기'(탐색기에서 그 파일을 골라 둔 채).</summary>
    [RelayCommand]
    private void ShowRecentInFolder(RecentFileItem? item)
    {
        if (item is null) return;
        try
        {
            _services.Shell.OpenFolder(item.Path, select: true);
        }
        catch (Exception ex)
        {
            Logger.LogError("최근 파일 폴더를 열지 못함: " + item.Path, ex);
        }
    }

    private void RemoveRecentCore(string path)
    {
        Recent.Remove(path);
        SaveSettings();
        RefreshRecentFiles();
        StatusText = $"‘{Path.GetFileName(path)}’{Josa.EulReul(Path.GetFileName(path))} 최근 파일 목록에서 뺐어요";
    }

    /// <summary>
    /// 창을 닫을 때 한 번: 가르는 막대로 바꾼 트리 너비(W2-09)와 이번에 연 최근 파일(W2-11)을 함께 — 바뀐 것이 있을 때만 한 번 저장한다
    /// (따로 두 번 쓰면 회사 PC DLP 가 연속 저장을 말없이 놓칠 수 있음). 저장은 <see cref="SaveSettings"/> — 최신 설정에 이 창이 바꾼 것만 얹는다(창 두 개),
    /// 앞서 못 저장한 바꾸기도 함께.
    /// </summary>
    public void SaveOnExit(double treeWidth)
    {
        Action<AppSettings>? change = null;
        if (double.IsFinite(treeWidth) && treeWidth > 0)
        {
            double w = UiSettings.ClampTreeWidth(Math.Round(treeWidth));
            if (Math.Abs(Settings.Ui.TreeWidth - w) >= 0.5)
            {
                Settings.Ui.TreeWidth = w;
                change = s => s.Ui.TreeWidth = w;
            }
        }
        if (change is null && !HasUnsavedSettings) return;
        SaveSettings(change);
    }

    private static bool SafeFileExists(string path)
    {
        try
        {
            return File.Exists(path);
        }
        catch
        {
            return false;
        }
    }

    // ───────────────────────────── 새 창 ─────────────────────────────

    /// <summary>☰ ▸ '새 창'(Ctrl+N): 빈 일집 창을 하나 더(단일 인스턴스를 건너뜀 — 창 두 개로 나란히).</summary>
    [RelayCommand]
    private void NewWindow() => StartNewWindow(ParsedCommand.None);

    /// <summary>
    /// 일집을 하나 더 띄운다: 맨 앞 '--new-window'(관리자 권한으로 뜬 창이면 '--elevated' 도 — 새 창도 관리자 권한이라 제목·권하지 않기를 맞춤) + 명령.
    /// 띄웠으면 true(못 띄우면 쉬운 한국어 오류 창).
    /// </summary>
    internal bool StartNewWindow(ParsedCommand command)
    {
        string? exe = _services.Processes.ExecutablePath;
        if (string.IsNullOrWhiteSpace(exe))
        {
            _ui.ShowError("일집 실행 파일을 찾지 못해 새 창을 열 수 없어요.");
            return false;
        }
        var args = CommandLineParser.ToArgs(command with { NewWindow = true, Elevated = IsElevatedInstance });
        try
        {
            _services.Processes.StartNewInstance(exe, args);
            StatusText = "새 창을 열었어요";
            return true;
        }
        catch (Exception ex)
        {
            Logger.LogError("새 창을 열지 못함", ex);
            _ui.ShowError($"새 창을 열지 못했어요.\n\n{ErrorMessages.ToUserMessage(ex)}");
            return false;
        }
    }

    // ───────────────────────────── 다른 이름으로 저장 ─────────────────────────────

    /// <summary>다른 형식으로 바꿀 때 원래 압축에 암호가 있었으면 묻는 문구.</summary>
    internal const string SamePasswordQuestion =
        "원래 압축 파일에 암호가 걸려 있었어요. 새 파일에도 같은 암호를 걸까요?\n([암호 없이 저장]하면 누구나 열 수 있어요)";

    /// <summary>버튼: 0 같은 암호 걸기(기본), 1 암호 없이 저장, 2 취소.</summary>
    internal static readonly string[] SamePasswordChoices = { "같은 암호 걸기", "암호 없이 저장", "취소" };

    /// <summary>
    /// ☰ ▸ '다른 이름으로 저장…': 저장 창(처음 이름 '이름 - 복사본' + 같은 형식) → 같은 형식이면 복사(나뉜 압축은 조각 전부 새 이름 규칙으로),
    /// ZIP·7Z 로 바꾸면 일집 임시 폴더(temp\convert\guid)에 다 풀고 새 형식으로 다시 압축(원래 암호가 있었으면 '같은 암호를 걸까요?'), 끝나면 임시 폴더를 지운다.
    /// 다 되면 새 파일을 연다. 실패·취소면 반쪽 파일을 남기지 않고 원래 압축은 그대로.
    /// </summary>
    [RelayCommand]
    private async Task SaveArchiveAsAsync()
    {
        if (!IsArchiveMode || CurrentArchivePath is not { } archive)
        {
            _ui.ShowInfo("먼저 압축 파일을 열어주세요.");
            return;
        }
        if (!File.Exists(archive))
        {
            _ui.ShowError($"‘{Path.GetFileName(archive)}’{Josa.EulReul(Path.GetFileName(archive))} 찾을 수 없어요. 지웠거나 다른 곳으로 옮겼는지 확인해 주세요.");
            return;
        }
        bool sevenZip = SevenZipLibrary.IsAvailable;
        string initialDir = Path.GetDirectoryName(Path.GetFullPath(archive)) ?? string.Empty;
        // 압축 안의 압축(일집 임시 폴더)·임시 폴더의 압축은 그 자리에 저장하면 곧 사라진다 — 바탕 화면을 권함
        if (_services.TempLocations.IsTemporary(initialDir)) initialDir = _services.TempLocations.SuggestedFolder() ?? initialDir;
        string suffix = ArchiveSaveAs.SuffixOf(archive);
        string? chosen = _ui.AskSavePath("다른 이름으로 저장", ArchiveSaveAs.Filter(archive, sevenZip), ArchiveSaveAs.SuggestName(archive), initialDir, suffix.TrimStart('.'));
        if (string.IsNullOrWhiteSpace(chosen)) return;

        SaveAsPlan plan;
        try
        {
            plan = ArchiveSaveAs.Plan(archive, chosen, sevenZip);
        }
        catch (Exception ex)
        {
            Logger.LogError("다른 이름으로 저장 계획 실패: " + chosen, ex);
            _ui.ShowError($"다른 이름으로 저장하지 못했어요.\n\n{ErrorMessages.ToUserMessage(ex)}");
            return;
        }
        switch (plan.Kind)
        {
            case SaveAsKind.Copy:
                await SaveCopyAsync(archive, plan);
                break;
            case SaveAsKind.Convert:
                await SaveConvertedAsync(archive, plan);
                break;
            default:
                _ui.ShowWarning(plan.Problem ?? "그 이름으로는 저장할 수 없어요.");
                break;
        }
    }

    /// <summary>다른 이름으로 저장 실패의 머리.</summary>
    private const string SaveAsFailedHead = "다른 이름으로 저장하지 못했어요. 원래 압축 파일은 그대로예요.";

    /// <summary>같은 형식: 파일(조각 전부)을 복사하고 새 파일을 연다(같은 내용이라 맞았던 암호를 그대로 씀).</summary>
    private async Task SaveCopyAsync(string archive, SaveAsPlan plan)
    {
        var session = _ui.ShowProgress("다른 이름으로 저장하는 중...");
        try
        {
            await ArchiveSaveAs.CopyAsync(plan.Pairs, session.Progress, session.CancellationToken);
        }
        catch (OperationCanceledException)
        {
            StatusText = "다른 이름으로 저장을 취소했어요";
            return;
        }
        catch (Exception ex)
        {
            session.Close();
            Logger.LogError($"다른 이름으로 저장(복사) 실패: {archive} → {plan.Target}", ex);
            string failure = $"{SaveAsFailedHead}\n\n{ErrorMessages.ToUserMessage(ex)}";
            if (!OfferElevatedRetry(ex, failure, null, archive, plan.Target)) _ui.ShowError(failure);
            StatusText = "오류 발생";
            return;
        }
        finally
        {
            session.Close();
        }

        await OpenArchiveAsync(plan.Target, _archivePassword);
        StatusText = plan.Pairs.Count > 1
            ? $"다른 이름으로 저장했어요: 조각 {plan.Pairs.Count}개 — {plan.Target}"
            : $"다른 이름으로 저장했어요: {plan.Target}";
    }

    /// <summary>
    /// 다른 형식(ZIP↔7Z 등): 일집 임시 폴더(<c>temp\convert\guid</c>)에 다 풀고(암호는 맞았던 것 — 모르면 묻기), 원래 암호가 있었으면 같은 암호를 걸지 묻고,
    /// 새 파일 옆 임시 파일(<c>.~이름.8글자.tmp</c>)에 새 형식으로 다 만든 뒤에만 바꿔 놓는다. 끝나면(실패·취소도) 임시 폴더·임시 파일을 지운다.
    /// </summary>
    private async Task SaveConvertedAsync(string archive, SaveAsPlan plan)
    {
        var format = plan.Format!.Value;
        var source = _locator.ResolveByContent(archive);
        var writer = _locator.ForExtension(CompressionFormats.ExtensionOf(format));
        if (source is null || writer is null)
        {
            _ui.ShowError($"{SaveAsFailedHead}\n\n이 형식으로는 바꿔 저장할 수 없어요.");
            return;
        }

        string work = Path.Combine(AppPaths.TempRoot, ConvertFolderName, Guid.NewGuid().ToString("N"));
        string? tmp = null;
        try
        {
            var (outcome, password) = await ExtractAllForConvertAsync(archive, source, work);
            if (outcome != UseOutcome.Done)
            {
                StatusText = outcome == UseOutcome.Cancelled ? "다른 이름으로 저장을 취소했어요" : "오류 발생";
                return;
            }

            // 원래 압축에 암호가 있었으면(풀 때 쓴 암호) 같은 암호를 걸지 묻는다 — 암호는 어디에도 저장하지 않는다
            string? newPassword = null;
            if (!string.IsNullOrEmpty(password))
            {
                int pick = _ui.Choose(SamePasswordQuestion, SamePasswordChoices, defaultIndex: 0);
                if (pick == 0) newPassword = password;
                else if (pick != 1)
                {
                    StatusText = "다른 이름으로 저장을 취소했어요";
                    return;
                }
            }
            var plain = new CompressionOptions { Level = CompressionLevelOption.Normal, Password = newPassword };
            // ZIP + 한글 암호(AES)면 압축 창과 같은 호환 확인(반디집·알집이 못 여는 ZIP 이 조용히 생기지 않게)
            var options = format == CompressionFormat.Zip ? ConfirmZipPasswordCompat(plain) : plain;
            if (options is null)
            {
                StatusText = "다른 이름으로 저장을 취소했어요";
                return;
            }

            var session = _ui.ShowProgress("다른 형식으로 저장하는 중...");
            try
            {
                var sources = Directory.Exists(work) ? Directory.EnumerateFileSystemEntries(work).ToList() : new List<string>();
                tmp = OutputFiles.TempSibling(plan.Target);
                var result = await writer.CompressAsync(sources, tmp, options, session.Progress, session.CancellationToken);
                // 풀어 둔 것을 하나라도 못 담았으면(다른 프로그램이 잡음 등) 빠진 압축을 내놓지 않는다 — 임시 파일은 아래 finally 가 지움
                if (result.Skipped.Count > 0)
                    throw new IOException($"풀어 둔 파일 {result.Skipped.Count}개를 새 압축 파일에 담지 못했어요. 잠시 뒤 다시 해 주세요.");
                string made = tmp;
                await Task.Run(() => OutputFiles.ReplaceExisting(made, plan.Target));
                tmp = null;
            }
            catch (OperationCanceledException)
            {
                StatusText = "다른 이름으로 저장을 취소했어요";
                return;
            }
            catch (Exception ex)
            {
                session.Close();
                Logger.LogError($"다른 형식으로 저장 실패: {archive} → {plan.Target}", ex);
                string failure = $"{SaveAsFailedHead}\n\n{ErrorMessages.ToUserMessage(ex)}";
                if (!OfferElevatedRetry(ex, failure, null, archive, plan.Target)) _ui.ShowError(failure);
                StatusText = "오류 발생";
                return;
            }
            finally
            {
                session.Close();
            }

            await OpenArchiveAsync(plan.Target, newPassword);
            StatusText = $"다른 형식으로 저장했어요: {plan.Target}";
        }
        finally
        {
            if (tmp is not null) TryDeleteFile(tmp);
            TempFolders.DeleteBestEffort(work);
        }
    }

    /// <summary>다른 형식으로 바꿀 때 풀어 두는 일집 임시 폴더 이름(<c>temp\convert</c> — 설계 §3).</summary>
    internal const string ConvertFolderName = "convert";

    /// <summary>
    /// 바꿔 저장하려고 전체를 <paramref name="dest"/> 에 푼다(진행 창·취소·암호 묻기 — 틀리면 다시). 맞은 암호를 돌려준다(없으면 null).
    /// 실패면 쉬운 한국어 오류 창(원래 압축은 그대로).
    /// </summary>
    private async Task<(UseOutcome Outcome, string? Password)> ExtractAllForConvertAsync(string archive, IArchiveService service, string dest)
    {
        bool sameArchive = string.Equals(archive, CurrentArchivePath, StringComparison.OrdinalIgnoreCase);
        string? password = sameArchive ? _archivePassword : null;
        while (true)
        {
            var session = _ui.ShowProgress("다른 형식으로 바꾸려고 푸는 중...");
            try
            {
                Directory.CreateDirectory(dest);
                await service.ExtractAsync(archive, dest, password, session.Progress, session.CancellationToken);
                if (sameArchive && !string.IsNullOrEmpty(password)) _archivePassword = password;
                return (UseOutcome.Done, string.IsNullOrEmpty(password) ? null : password);
            }
            catch (OperationCanceledException)
            {
                return (UseOutcome.Cancelled, null);
            }
            catch (Exception ex) when (IsPasswordRelated(ex))
            {
                session.Close();
                var (reason, message) = PasswordPrompt(ex, password);
                var pw = _ui.AskPassword(Path.GetFileName(archive), reason, message);
                if (pw is null) return (UseOutcome.Cancelled, null);
                password = pw;
                TempFolders.DeleteBestEffort(dest);   // 처음부터 다시(서비스가 되돌렸어도 혹시 남은 것)
            }
            catch (Exception ex)
            {
                session.Close();
                Logger.LogError($"다른 형식으로 바꾸려고 풀기 실패: {archive}", ex);
                _ui.ShowError($"{SaveAsFailedHead}\n\n{ErrorMessages.ToUserMessage(ex)}");
                return (UseOutcome.Failed, null);
            }
            finally
            {
                session.Close();
            }
        }
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.SetAttributes(path, FileAttributes.Normal);
                File.Delete(path);
            }
        }
        catch
        {
            // 남으면 다음에 같은 이름을 쓰지 않을 뿐(임시 이름은 늘 새로)
        }
    }

    // ───────────────────────────── 이 압축 파일 지우기 ─────────────────────────────

    /// <summary>지우기 확인 문구(나뉜 압축은 조각 수, 고쳐 놓고 넣지 않은 파일이 있으면 그 경고).</summary>
    internal static string DeleteArchiveQuestion(string name, int parts, IReadOnlyList<string> pendingNames)
    {
        var lines = new List<string> { $"‘{name}’{Josa.EulReul(name)} 휴지통으로 옮길까요?" };
        if (parts > 1) lines.Add($"나뉜 조각 {parts}개를 모두 옮겨요.");
        if (pendingNames.Count > 0)
        {
            string shown = string.Join(", ", pendingNames.Take(3).Select(n => $"‘{n}’")) + (pendingNames.Count > 3 ? $" 외 {pendingNames.Count - 3}개" : string.Empty);
            lines.Add($"\n이 압축에서 열어 고친 파일({shown})은 아직 압축 파일에 넣지 않았어요. 지우면 고친 내용도 없어져요.");
        }
        lines.Add("\n휴지통에서 되살릴 수 있어요.");
        return string.Join("\n", lines);
    }

    /// <summary>
    /// ☰ ▸ '이 압축 파일 지우기': 묻고(기본 [취소]) — 먼저 보기를 닫고(보기·찾기 — 일집이 압축 파일을 잡고 있지 않게) — 조각 전부를 휴지통으로(영구 삭제는 하지 않음 —
    /// 휴지통이 없는 곳(네트워크·USB)이면 지우지 않고 알림). 옮겼으면 그 압축의 작업 폴더(압축 속에서 연 파일)와 연 파일 감시를 놓고, 시작 화면으로 돌아가고 최근 목록에서 뺀다.
    /// 못 옮기면 알리고 다시 연다 — 작업 폴더는 그대로라 열어 고쳐 놓은 파일도 그대로다(W2-11 검증 지적: 예전엔 옮기기 전에 지워 실패해도 고친 내용이 사라짐).
    /// </summary>
    [RelayCommand]
    private async Task DeleteArchiveFileAsync()
    {
        if (!IsArchiveMode || CurrentArchivePath is not { } archive)
        {
            _ui.ShowInfo("먼저 압축 파일을 열어주세요.");
            return;
        }
        string full = Path.GetFullPath(archive);
        string name = Path.GetFileName(full);
        if (!File.Exists(full))
        {
            _ui.ShowError($"‘{name}’{Josa.EulReul(name)} 찾을 수 없어요. 이미 지웠거나 다른 곳으로 옮겼는지 확인해 주세요.");
            return;
        }
        var parts = ArchiveVolumes.AllParts(full);
        if (parts.Any(p => !_services.RecycleBin.IsRecyclable(p)))
        {
            _ui.ShowWarning($"‘{name}’{Josa.IGa(name)} 휴지통이 없는 곳(네트워크 드라이브·USB 등)에 있어서 일집에서는 지우지 않아요. 지우려면 탐색기에서 지워 주세요.");
            return;
        }

        var mine = AllWorkspaces().Where(w => string.Equals(w.ArchivePath, full, StringComparison.OrdinalIgnoreCase)).ToList();
        var pending = (await PendingChangesAsync(mine)).Select(e => e.Name).ToList();
        int pick = _ui.Choose(DeleteArchiveQuestion(name, parts.Count, pending), new[] { "휴지통으로 옮기기", "취소" }, defaultIndex: 1);
        if (pick != 0)
        {
            StatusText = "지우기를 취소했어요";
            return;
        }

        // 먼저 보기를 닫는다(보기·찾기 — 일집이 잡고 있는 것이 없게). 작업 폴더(일집 임시 폴더 — 압축 파일을 잡지 않음)는 옮긴 뒤에만 놓는다:
        // 못 옮기면 압축 파일이 그대로 남으므로 그 안에서 열어 고쳐 놓은 파일도 그대로 둔다(다시 열면 같은 작업 폴더를 다시 씀)
        string? password = _archivePassword;
        CloseView();
        var result = _services.RecycleBin.Send(parts);
        if (!result.Succeeded)
        {
            Logger.LogError($"압축 파일을 휴지통으로 옮기지 못함: {full}", new IOException(result.Error ?? "휴지통 실패"));
            _ui.ShowError($"‘{name}’{Josa.EulReul(name)} 휴지통으로 옮기지 못했어요. 압축 파일은 그대로 있어요.\n\n{result.Error}");
            await OpenArchiveAsync(full, password);   // 보던 압축을 다시
            return;
        }

        ReleaseWorkspacesOf(full);
        Recent.Remove(full);
        SaveSettings();
        RefreshRecentFiles();
        StatusText = parts.Count > 1 ? $"‘{name}’ 조각 {parts.Count}개를 휴지통으로 옮겼어요" : $"‘{name}’{Josa.EulReul(name)} 휴지통으로 옮겼어요";
    }

    /// <summary>이 압축의 작업 폴더(압축 속 파일을 열어 둔 곳)를 감시에서 빼고 지운다(쉬는 것 포함).</summary>
    private void ReleaseWorkspacesOf(string archiveFullPath)
    {
        foreach (var ws in AllWorkspaces().Where(w => string.Equals(w.ArchivePath, archiveFullPath, StringComparison.OrdinalIgnoreCase)).ToList())
        {
            foreach (var e in ws.OpenedEntries) _entryWatcher?.Unwatch(e);
            _workspacePasswords.Remove(ws);
            _retiredWorkspaces.Remove(ws);
            if (ReferenceEquals(_workspace, ws)) _workspace = null;
            ws.Dispose();
        }
    }
}
