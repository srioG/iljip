using System.IO;
using System.Runtime.CompilerServices;
using Iljip.Services;
using Iljip.Services.Editing;

namespace Iljip.ViewModels;

// I5-A(X3-F1·I3-3-close): 일집을 닫은 뒤(또는 일집이 갑자기 꺼진 뒤) 다른 프로그램에서 고쳐 저장해 압축 파일에 아직 넣지 않은 파일 — 다음 시작 때 알린다.
// 찾는 것은 청소(TempJanitor.CleanupStale(clock, leftovers)) — 그런 파일은 지우지 않고 남긴다. 여기서는 묻고 고른 대로 한다(App 이 메인 창을 띄운 뒤 부름).
public partial class MainViewModel
{
    /// <summary>알림의 단추(차례대로).</summary>
    internal static readonly string[] LeftoverChoices = { "압축에 넣기", "폴더 열기", "버리기", "나중에" };

    /// <summary>
    /// ‘압축 파일에 아직 넣지 않은 고친 파일이 있어요’ — [압축에 넣기][폴더 열기][버리기][나중에](기본 [압축에 넣기]).
    /// 넣기 = 꺼낸 압축 파일의 원래 자리·원래 이름으로 바꿔 넣기(이름을 바꾼 파일도 — 압축의 다른 항목은 그대로). 넣지 못하면(압축 파일이 없거나 고칠 수 없는 형식·실패) 다른 곳에 저장을 권한다.
    /// 폴더 열기 = 그 파일을 고른 채 탐색기로(파일은 그대로 — 다음 시작 때 다시 알림). 버리기 = 휴지통으로. 나중에·창 닫기 = 그대로 두고 다음 시작 때 다시 알림.
    /// 넣거나 저장하거나 버린 파일의 순번 폴더는 지우고, 작업 폴더에 남은 것이 없으면 작업 폴더째 지운다.
    /// </summary>
    public async Task OfferLeftoverChangesAsync(IReadOnlyList<LeftoverFile> leftovers)
    {
        var pending = leftovers.Where(l => File.Exists(l.FilePath)).ToList();
        if (pending.Count == 0) return;
        int pick = _ui.Choose(LeftoverMessage(pending), LeftoverChoices, defaultIndex: 0);
        switch (pick)
        {
            case 0:
                await InsertLeftoversAsync(pending);
                break;
            case 1:
                foreach (var l in pending.GroupBy(l => l.SlotDir, StringComparer.OrdinalIgnoreCase).Select(g => g.First()).Take(3))
                {
                    try
                    {
                        _services.Shell.OpenFolder(l.FilePath, select: true);
                    }
                    catch (Exception ex)
                    {
                        Logger.LogError("넣지 않은 고친 파일의 폴더를 열지 못함: " + l.FilePath, ex);
                    }
                }
                StatusText = "폴더를 열었어요. 필요한 파일은 다른 곳에 옮겨 두세요 — 그대로 두면 다음에 일집을 켤 때 다시 알려 드려요";
                break;
            case 2:
                DiscardLeftovers(pending);
                break;
            default:
                StatusText = "고친 파일은 그대로 두었어요 — 다음에 일집을 켤 때 다시 알려 드려요";
                break;
        }
    }

    /// <summary>알림 문구 — 이름은 다섯 개까지(이름을 바꾼 것은 압축 안 이름도, 압축 파일이 없어졌으면 그 말도).</summary>
    internal static string LeftoverMessage(IReadOnlyList<LeftoverFile> pending)
    {
        var lines = pending.Take(5).Select(l =>
        {
            string arc = Path.GetFileName(l.ArchivePath);
            string missing = File.Exists(l.ArchivePath) ? string.Empty : "(압축 파일을 찾을 수 없어요)";
            return $"- {DisplayName(l.Name, l.IsRenamed ? l.ExtractedName : null)} — {arc}{missing}";
        }).ToList();
        if (pending.Count > 5) lines.Add($"외 {pending.Count - 5}개");
        return "압축 속에서 열어 고친 파일 중 압축 파일에 아직 넣지 않은 것이 있어요(일집을 닫은 뒤에 저장한 것).\n" +
               string.Join("\n", lines) +
               "\n\n[압축에 넣기]를 누르면 원래 압축 파일의 원래 자리에 넣어요. 어떻게 할까요?";
    }

    /// <summary>
    /// 하나씩 넣는다(암호는 RunEditAsync 가 물음). 다 넣은 수를 상태 줄에.
    /// 5차 통합 점검: ① 넣을 내용은 먼저 옆 폴더(<c>순번.reinsert</c>)에 복사해 고정한다(다시 넣기 <see cref="ReinsertAsync"/> 와 같게) — 그 순간 다른 프로그램이 혼자 잡아
    /// 읽지 못하면 압축은 건드리지 않고 그대로 둔다(예전엔 넣기가 원래 항목만 지운 채 '넣었어요' 하고 고친 파일까지 지웠다). 넣는 동안 또 저장했으면 지우지 않고 다음에 다시.
    /// ② 같은 압축에서 한 번 맞은 암호는 이 알림 동안 기억해 둘째 파일에 다시 묻지 않는다(메모리에만 — 끝나면 지움).
    /// </summary>
    private async Task InsertLeftoversAsync(IReadOnlyList<LeftoverFile> pending)
    {
        int inserted = 0, kept = 0, savedAgain = 0, elsewhere = 0;
        var passwords = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            foreach (var l in pending)
            {
                string archive = l.ArchivePath;
                bool editable = File.Exists(archive) && ArchiveServiceLocator.CanEdit(archive);
                if (!editable)
                {
                    string why = File.Exists(archive) ? NotEditableShortFor(archive) : $"압축 파일 ‘{Path.GetFileName(archive)}’{Josa.EulReul(Path.GetFileName(archive))} 찾을 수 없어요.";
                    if (SaveLeftoverElsewhere(l, $"‘{l.Name}’{Josa.EulReul(l.Name)} 압축 파일에 넣을 수 없어요. {why} 고친 내용을 잃지 않게 다른 곳에 저장할까요?")) { RemoveLeftover(l); elsewhere++; }
                    else kept++;
                    continue;
                }
                var outcome = await InsertLeftoverAsync(l, passwords);
                if (outcome == LeftoverInsert.Inserted)
                {
                    inserted++;
                    RemoveLeftover(l);
                    continue;
                }
                if (outcome == LeftoverInsert.InsertedButChangedAgain)
                {
                    inserted++;
                    savedAgain++;   // 넣는 동안 또 저장함 — 지우지 않고 다음 시작에 다시 알림
                    continue;
                }
                // 넣지 못함(오류 창은 이미 보임) → 다른 곳에 저장을 권함. 취소·못 읽음이면 그대로 두고 다음에 다시
                if (outcome == LeftoverInsert.Failed
                    && SaveLeftoverElsewhere(l, $"‘{l.Name}’{Josa.EulReul(l.Name)} 압축 파일에 넣지 못했어요. 고친 내용을 잃지 않게 다른 곳에 저장할까요?")) { RemoveLeftover(l); elsewhere++; }
                else kept++;
            }
        }
        finally
        {
            passwords.Clear();   // 암호를 메모리에 오래 두지 않는다
        }
        StatusText = LeftoverStatus(inserted, savedAgain, elsewhere, kept);
    }

    /// <summary>
    /// 다음 시작 넣기의 상태 줄: '고친 파일 N개를 압축 파일에 넣었어요' / 넣었지만 넣는 동안 또 저장된 것 '그중 M개는 …' / 다른 곳에 저장한 것 / 못 넣은 것 'K개는 그대로 두었어요'
    /// (맨 앞 것에만 '고친 파일') — 남긴 것이 있으면 '다음에 일집을 켤 때 다시 알려 드려요'.
    /// </summary>
    internal static string LeftoverStatus(int inserted, int savedAgain, int elsewhere, int kept)
    {
        var parts = new List<string>();
        if (inserted > 0) parts.Add($"고친 파일 {inserted}개를 압축 파일에 넣었어요");
        if (savedAgain > 0) parts.Add($"그중 {savedAgain}개는 넣는 동안 또 저장돼서 지우지 않았어요");
        if (elsewhere > 0) parts.Add(parts.Count == 0 ? $"고친 파일 {elsewhere}개를 다른 곳에 저장했어요" : $"{elsewhere}개는 다른 곳에 저장했어요");
        if (kept > 0) parts.Add(parts.Count == 0 ? $"고친 파일 {kept}개는 그대로 두었어요" : $"{kept}개는 그대로 두었어요");
        string text = string.Join(". ", parts);
        return savedAgain + kept > 0 ? text + " — 다음에 일집을 켤 때 다시 알려 드려요" : text;
    }

    /// <summary>남은 파일 하나 넣기의 결과.</summary>
    private enum LeftoverInsert
    {
        Inserted,
        /// <summary>넣었는데 그 사이 그 파일을 또 저장함(넣은 것은 복사해 둔 내용) — 파일은 남김.</summary>
        InsertedButChangedAgain,
        /// <summary>넣지 못함(오류 창을 보임) — 다른 곳에 저장을 권함.</summary>
        Failed,
        /// <summary>암호 창 [취소]·고친 파일을 읽지 못함 — 그대로 둠.</summary>
        NotDone,
    }

    /// <summary>
    /// 남은 파일 하나를 넣는다: 옆 폴더에 복사(크기·시각 표) → 원래 자리·원래 이름으로 바꿔 넣기 → 넣은 뒤 원래 파일이 복사한 때와 같은지 본다. 복사 폴더는 늘 지운다.
    /// </summary>
    private async Task<LeftoverInsert> InsertLeftoverAsync(LeftoverFile l, Dictionary<string, string> passwords)
    {
        string archive = l.ArchivePath;
        string full = Path.GetFullPath(archive);
        string snapshotDir = l.SlotDir.TrimEnd(Path.DirectorySeparatorChar) + ReinsertSuffix;
        string snapshot = Path.Combine(snapshotDir, l.ExtractedName);
        try
        {
            ContentStamp copied;
            try
            {
                copied = await Task.Run(() => CopyLeftover(l.FilePath, snapshot));
            }
            catch (Exception ex)
            {
                Logger.LogError("넣지 않은 고친 파일 복사 실패(그대로 둠): " + l.FilePath, ex);
                ShowProblem($"‘{l.Name}’{Josa.EulReul(l.Name)} 읽지 못해 압축 파일에 넣지 못했어요. 고친 파일은 그대로 두었어요 — 그 파일을 연 프로그램을 닫고 다음에 일집을 켤 때 다시 넣어 주세요.", ex);
                return LeftoverInsert.NotDone;
            }
            var (folder, leaf) = SplitEntryKey(l.EntryKey);
            var ops = new EditOp[] { new EditOp.Add(new[] { snapshot }, folder, AsName: leaf, ReplaceExisting: true) };
            var failed = new StrongBox<bool>();
            var used = new StrongBox<string?>();
            string? known = passwords.TryGetValue(full, out var pw) ? pw : PasswordFor(archive);
            var result = await RunEditAsync(archive, ops, "압축 파일에 넣는 중...", known, failed, usedPassword: used);
            if (result is null) return failed.Value ? LeftoverInsert.Failed : LeftoverInsert.NotDone;
            if (!string.IsNullOrEmpty(used.Value)) passwords[full] = used.Value;
            var now = new FileInfo(l.FilePath);
            bool same = now.Exists && now.Length == copied.Size && now.LastWriteTimeUtc == copied.WriteUtc;
            return same || !now.Exists ? LeftoverInsert.Inserted : LeftoverInsert.InsertedButChangedAgain;
        }
        finally
        {
            TempFolders.DeleteBestEffort(snapshotDir);
        }
    }

    /// <summary>남은 파일을 <paramref name="destination"/> 에 복사하고 복사한 때의 원래 파일 크기·시각을 돌려준다(사본 시각은 원래 파일 것으로).</summary>
    private static ContentStamp CopyLeftover(string source, string destination)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        var before = new FileInfo(source);
        if (!before.Exists) throw new FileNotFoundException("고친 파일을 찾지 못했어요.", source);
        long size = before.Length;
        DateTime written = before.LastWriteTimeUtc;
        using (var src = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 1 << 16))
        using (var dst = new FileStream(destination, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 16))
            src.CopyTo(dst);
        try { File.SetLastWriteTimeUtc(destination, written); } catch { /* 시각 맞추기 실패는 무시 */ }
        return new ContentStamp(size, written, null);
    }

    /// <summary>[다른 곳에 저장][나중에] → 저장 창 → 복사. 저장했으면 true.</summary>
    private bool SaveLeftoverElsewhere(LeftoverFile l, string question)
    {
        if (_ui.Choose(question, new[] { "다른 곳에 저장", "나중에" }, defaultIndex: 0) != 0) return false;
        string ext = Path.GetExtension(l.Name);
        string filter = ext.Length > 0 ? $"{ext.TrimStart('.').ToUpperInvariant()} 파일 (*{ext})|*{ext}|모든 파일 (*.*)|*.*" : "모든 파일 (*.*)|*.*";
        string initialDir = Path.GetDirectoryName(l.ArchivePath) is { } d && Directory.Exists(d) ? d : Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        var dest = _ui.AskSavePath("고친 파일 저장", filter, l.Name, initialDir, ext.TrimStart('.'));
        if (string.IsNullOrEmpty(dest)) return false;
        try
        {
            File.Copy(l.FilePath, dest, overwrite: true);   // 덮어쓰기는 저장 창이 이미 물음
            StatusText = $"고친 파일을 저장했어요: {dest}";
            return true;
        }
        catch (Exception ex)
        {
            Logger.LogError("넣지 않은 고친 파일 저장 실패: " + dest, ex);
            ShowProblem("고친 파일을 저장하지 못했어요.", ex);
            return false;
        }
    }

    /// <summary>휴지통으로 — 옮겼으면 순번 폴더를 지운다. 못 옮기면 그대로 두고 알린다.</summary>
    private void DiscardLeftovers(IReadOnlyList<LeftoverFile> pending)
    {
        var files = pending.Select(l => l.FilePath).ToList();
        var result = _services.RecycleBin.Send(files);
        if (!result.Succeeded)
        {
            Logger.LogError("넣지 않은 고친 파일을 휴지통으로 옮기지 못함", new IOException(result.Error ?? "휴지통 실패"));
            _ui.ShowWarning($"고친 파일을 휴지통으로 옮기지 못했어요. 파일은 그대로 있어요 — 다음에 일집을 켤 때 다시 알려 드려요.\n\n{result.Error}");
            return;
        }
        foreach (var l in pending) RemoveLeftover(l);
        StatusText = $"고친 파일 {pending.Count}개를 휴지통으로 옮겼어요";
    }

    /// <summary>처리한 파일의 순번 폴더를 지우고, 작업 폴더에 연 파일 목록(owner.lock) 말고 남은 것이 없으면 작업 폴더째 지운다.</summary>
    private static void RemoveLeftover(LeftoverFile l)
    {
        string ws = Path.GetFullPath(l.WorkspaceDir).TrimEnd(Path.DirectorySeparatorChar);
        string slot = Path.GetFullPath(l.SlotDir).TrimEnd(Path.DirectorySeparatorChar);
        if (slot.StartsWith(ws + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) TempFolders.DeleteBestEffort(slot);
        else
            try { File.Delete(l.FilePath); } catch { /* 남김 — 다음에 다시 */ }
        if (TempFolders.HasNoFilesBut(ws, ArchiveWorkspace.LockFileName)) TempFolders.DeleteBestEffort(ws);
    }
}
