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

    /// <summary>하나씩 넣는다(암호는 RunEditAsync 가 물음). 다 넣은 수를 상태 줄에.</summary>
    private async Task InsertLeftoversAsync(IReadOnlyList<LeftoverFile> pending)
    {
        int inserted = 0, kept = 0;
        foreach (var l in pending)
        {
            string archive = l.ArchivePath;
            bool editable = File.Exists(archive) && ArchiveServiceLocator.CanEdit(archive);
            if (!editable)
            {
                string why = File.Exists(archive) ? NotEditableShortFor(archive) : $"압축 파일 ‘{Path.GetFileName(archive)}’{Josa.EulReul(Path.GetFileName(archive))} 찾을 수 없어요.";
                if (SaveLeftoverElsewhere(l, $"‘{l.Name}’{Josa.EulReul(l.Name)} 압축 파일에 넣을 수 없어요. {why} 고친 내용을 잃지 않게 다른 곳에 저장할까요?")) RemoveLeftover(l);
                else kept++;
                continue;
            }
            var (folder, leaf) = SplitEntryKey(l.EntryKey);
            var ops = new EditOp[] { new EditOp.Add(new[] { l.FilePath }, folder, AsName: leaf, ReplaceExisting: true) };
            var failed = new StrongBox<bool>();
            var result = await RunEditAsync(archive, ops, "압축 파일에 넣는 중...", PasswordFor(archive), failed);
            if (result is not null)
            {
                inserted++;
                RemoveLeftover(l);
                continue;
            }
            // 넣지 못함(오류 창은 이미 보임) → 다른 곳에 저장을 권함. 취소면 그대로 두고 다음에 다시
            if (failed.Value && SaveLeftoverElsewhere(l, $"‘{l.Name}’{Josa.EulReul(l.Name)} 압축 파일에 넣지 못했어요. 고친 내용을 잃지 않게 다른 곳에 저장할까요?")) RemoveLeftover(l);
            else kept++;
        }
        StatusText = kept == 0
            ? $"고친 파일 {inserted}개를 압축 파일에 넣었어요"
            : $"고친 파일 {inserted}개를 압축 파일에 넣었어요. {kept}개는 그대로 두었어요 — 다음에 일집을 켤 때 다시 알려 드려요";
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
