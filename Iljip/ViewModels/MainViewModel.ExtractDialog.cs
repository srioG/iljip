using System.IO;
using Iljip.Models;
using Iljip.Services;

namespace Iljip.ViewModels;

// 풀기 창(W2-09 — I2-13): '다른 곳에 풀기'(툴바 [압축 풀기]·Ctrl+E·선택 항목 풀기)·명령줄 '--extract'·열기 창에서 여러 개 고르기가
// 폴더 고르기 창 대신 이 창을 쓴다. 창의 처음 값은 설정(extract.makeFolder·recentFolders·deleteArchiveAfter·dialogOpenFolderAfter),
// 고른 '폴더 만들기'·'폴더 열기'와 다 푼 곳(최근 푼 곳 맨 앞)은 풀기가 끝난 뒤 한 번 저장한다(DLP 연속 재기록 경합 교훈). 휴지통은 이번에만.
// 창 없이 풀기(--extract-to)·우클릭 '여기에/이름 폴더에 풀기'는 이 길을 타지 않는다.
public partial class MainViewModel
{
    /// <summary>
    /// 풀기 창을 띄운다(처음 값은 설정). 취소하면 null.
    /// 처음 풀 곳은 압축 파일이 있는 폴더 — 단 그곳이 '잠깐 두는' 폴더(일집 임시 폴더 = 압축 안의 압축, %TEMP%, Outlook 첨부의 INetCache)면
    /// 바탕 화면(없으면 최근에 푼 곳, 그것도 없으면 빈 칸 — [찾아보기]로 고르게). W2-09 검증 지적: 압축 안의 압축을 그대로 [풀기] 하면
    /// 일집 임시 폴더에 풀려 일집을 닫을 때 말없이 지워졌다. 최근 푼 곳 목록에도 그런 곳은 보이지 않는다.
    /// </summary>
    private ExtractTarget? AskExtractTarget(IReadOnlyList<string> archives, bool selectedOnly, string title)
    {
        var x = Settings.Extract;
        var temp = _services.TempLocations;
        var recent = x.VisibleRecentFolders().Where(f => !temp.IsTemporary(f)).ToList();
        string initial = Path.GetDirectoryName(archives[0]) ?? string.Empty;
        if (temp.IsTemporary(initial)) initial = temp.SuggestedFolder() ?? recent.FirstOrDefault() ?? string.Empty;
        var request = new ExtractTargetRequest
        {
            Archives = archives,
            SelectedOnly = selectedOnly,
            Title = title,
            InitialFolder = initial,
            RecentFolders = recent,
            MakeFolder = x.MakeFolder,
            DeleteArchiveAfter = !selectedOnly && x.DeleteArchiveAfter,
            OpenFolderAfter = x.DialogOpenFolderAfter,
        };
        return _ui.AskExtractTarget(request);
    }

    /// <summary>
    /// 압축 하나를 풀기 창의 답대로 푼다: 폴더 만들기면 고른 곳 안의 '이름' 폴더(이미 있으면 '이름 (2)' …)에, 아니면 고른 곳에 바로.
    /// 새로 만든 폴더에 파일이 하나도 안 남고 끝나면(취소·실패) 그 폴더를 지운다(다음에 풀 때 번호가 밀리지 않게 — 우클릭 '이름 폴더에 풀기'와 같은 규칙).
    /// 다 풀었으면 창에서 고른 대로 폴더를 연다. 설정은 끝에 한 번 저장.
    /// </summary>
    private async Task<bool> ExtractWithTargetAsync(string archivePath, ExtractTarget target, IReadOnlyCollection<string>? selectedPaths)
    {
        // 풀기 직전에 정한다 — 그사이 생긴 폴더와도 겹치지 않게
        string dest = target.DestinationFor(archivePath);
        bool ok = false;
        try
        {
            ok = await ExtractToFolderCoreAsync(archivePath, dest, selectedPaths, selectedPaths is null && target.DeleteArchiveAfter);
        }
        finally
        {
            if (!ok && target.MakeFolder) ArchiveNaming.RemoveFolderIfNoFiles(dest);
            RememberExtractTarget(target, ok);
        }
        if (ok && target.OpenFolderAfter) OpenFolderQuietly(dest);
        return ok;
    }

    /// <summary>여러 개를 풀기 창의 답대로 푼다(폴더 만들기면 고른 곳 안의 '이름' 폴더마다, 아니면 고른 곳에 모두 바로).</summary>
    private async Task<BatchResult> RunExtractBatchWithTargetAsync(IReadOnlyList<string> archives, ExtractTarget target)
    {
        var rule = new ExtractTargetRule(target.MakeFolder ? ExtractTargetKind.NamedFolderIn : ExtractTargetKind.Into, target.Folder);
        BatchResult? result = null;
        try
        {
            result = await RunExtractBatchAsync(archives, rule, fromShell: false, target);
            return result;
        }
        finally
        {
            RememberExtractTarget(target, result is { Cancelled: false } r && r.SucceededCount > 0);
        }
    }

    /// <summary>
    /// 풀기 창에서 고른 것을 기억한다: '폴더 만들기'·'폴더 열기'(다음 창의 처음 값), 다 풀었으면 고른 곳을 최근 푼 곳 맨 앞에.
    /// 바뀐 것이 있을 때만 한 번 저장한다(휴지통은 기억하지 않음 — 위험한 선택은 이번에만). '잠깐 두는' 폴더(%TEMP% 등)는 최근 목록에 넣지 않는다.
    /// </summary>
    private void RememberExtractTarget(ExtractTarget target, bool succeeded)
    {
        bool push = succeeded && !_services.TempLocations.IsTemporary(target.Folder);
        void Change(AppSettings s)
        {
            var e = s.Extract;
            e.MakeFolder = target.MakeFolder;
            e.DialogOpenFolderAfter = target.OpenFolderAfter;
            if (push) e.PushRecentFolder(target.Folder);
        }

        var x = Settings.Extract;
        var before = (x.MakeFolder, x.DialogOpenFolderAfter, Recent: string.Join("\n", x.RecentFolders ?? new List<string>()));
        Change(Settings);
        var after = (x.MakeFolder, x.DialogOpenFolderAfter, Recent: string.Join("\n", x.RecentFolders ?? new List<string>()));
        // 최신 설정에도 같은 바꾸기만(최근 푼 곳은 맨 앞에 넣기 — 다른 창이 넣은 곳은 그대로) — W2-11 검증 지적
        if (before != after) SaveSettings(Change);
    }

    /// <summary>탐색기로 폴더를 연다(못 열어도 풀기는 끝났다 — 창은 띄우지 않고 기록만).</summary>
    private void OpenFolderQuietly(string folder)
    {
        try
        {
            if (Directory.Exists(folder)) _services.Shell.OpenFolder(folder);
        }
        catch (Exception ex)
        {
            Logger.LogError("푼 폴더를 열지 못함: " + folder, ex);
        }
    }
}
