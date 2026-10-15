using System.ComponentModel;
using System.IO;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using Iljip.Models;
using Iljip.Services;

namespace Iljip.ViewModels;

// IJ3-01(W2-04 다시): 압축 속 파일 끌어 꺼내기·Ctrl+C(I1-4)·그림 미리 보기(I3-7)의 화면 쪽. 안의 파일 열기·다시 넣기는 MainViewModel.EntryOpen.cs.
// 클립보드·그림 읽기는 AppServices 로 받아 시험은 가짜로 돈다(창·진짜 클립보드 없음). 끌기의 실제 탐색기 주고받기는 손 점검(MANUAL_CHECKLIST 20절).
public partial class MainViewModel : IPreviewHost
{
    // ───────────────────────────── 압축 세대(미리 보기의 옛 그림 막기) ─────────────────────────────

    /// <summary>
    /// 지금 보는 압축의 세대 번호 — 압축을 열 때(같은 압축을 다시 열어도), 고친 뒤 목록을 다시 읽을 때, 닫을 때 하나씩 는다(<see cref="BumpArchiveGeneration"/>).
    /// 미리 보기는 읽기를 시작할 때의 번호와 끝났을 때의 번호가 같을 때만 기억·표시한다 — 같은 경로('사진/001.jpg')라도 압축이 바뀌면 내용이 다르다.
    /// (W2-04 보관본은 미리 보기 쪽에서만 세었고, 그 번호를 올린 뒤 화면의 '지금 압축' 이 바뀌기 전 틈에 시작한 읽기가 옛 압축에서 꺼낸 그림을 새 세대로 기억했다.)
    /// </summary>
    private int _archiveGeneration;

    /// <summary>시험용: 지금 세대 번호.</summary>
    internal int ArchiveGeneration => _archiveGeneration;

    /// <summary>
    /// 압축이 바뀌었다(열기·다시 읽기·닫기 — 지금 압축 경로·암호를 바꾼 '뒤'에, 기다림 없이 이어서 부른다): 세대를 올리고 미리 보기를 비운다.
    /// 그 전에 시작한 미리 보기 읽기는 끝나도 버려진다.
    /// </summary>
    private void BumpArchiveGeneration()
    {
        _archiveGeneration++;
        Preview.Reset();
        SelectedEntry = null;
    }

    // ───────────────────────────── I3-7 그림 미리 보기 ─────────────────────────────

    /// <summary>그림 미리 보기(오른쪽 칸).</summary>
    public ImagePreviewViewModel Preview { get; }

    /// <summary>목록에서 고른 항목(여럿이면 화면이 정한 하나) — 미리 보기가 따라간다.</summary>
    [ObservableProperty]
    private ArchiveEntry? _selectedEntry;

    /// <summary>미리 보기 칸을 보일지(켰고 압축을 보는 중).</summary>
    public bool IsPreviewPaneVisible => Preview.IsVisible && IsArchiveMode;

    private ImagePreviewViewModel CreatePreview()
    {
        var preview = new ImagePreviewViewModel(this, _services.ImageDecoder);
        preview.PropertyChanged += Preview_PropertyChanged;
        return preview;
    }

    /// <summary>창이 뜰 때(MainWindow) 한 번: 저장된 설정을 화면에 적용한다(미리 보기 칸 — 설정 ui.previewVisible). 닫을 때 저장은 <see cref="SaveOnExit"/>.</summary>
    public void ApplyStartupSettings()
    {
        Preview.IsVisible = Settings.Ui.PreviewVisible;
    }

    private void Preview_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ImagePreviewViewModel.IsVisible))
        {
            OnPropertyChanged(nameof(IsPreviewPaneVisible));
            if (Preview.IsVisible && IsArchiveMode) _ = Preview.ShowAsync(SelectedEntry);
        }
        else if (e.PropertyName == nameof(ImagePreviewViewModel.CurrentEntry))
        {
            // 이전/다음으로 넘기면 목록 선택도 따라간다
            var cur = Preview.CurrentEntry;
            if (cur is not null && !ReferenceEquals(cur, SelectedEntry) && VisibleEntries.Contains(cur))
                SelectedEntry = cur;
        }
    }

    partial void OnSelectedEntryChanged(ArchiveEntry? value)
    {
        if (!Preview.IsVisible || !IsArchiveMode) return;
        if (value is not null && ReferenceEquals(value, Preview.CurrentEntry)) return;
        _ = Preview.ShowAsync(value);
    }

    int IPreviewHost.ArchiveGeneration => _archiveGeneration;

    IReadOnlyList<ArchiveEntry> IPreviewHost.FolderEntries => VisibleEntries.ToList();

    bool IPreviewHost.NeedsPassword(ArchiveEntry entry) =>
        entry.IsEncrypted && entry.Size > 0 && string.IsNullOrEmpty(_archivePassword);

    async Task IPreviewHost.ExtractQuietAsync(int generation, ArchiveEntry entry, string dest, CancellationToken ct)
    {
        // 세대가 지났으면 꺼내지 않는다 — 지금 압축(경로·암호)은 이미 다른 압축의 것일 수 있다
        if (generation != _archiveGeneration || CurrentArchivePath is not { } archive)
            throw new OperationCanceledException("미리 보기: 그사이 압축이 바뀜");
        var service = _locator.ResolveByContent(archive) ?? throw new UnsupportedFormatException(Path.GetExtension(archive));
        await service.ExtractEntriesAsync(archive, dest, new[] { DragPayloadBuilder.NormalizeKey(entry.Path) }, _archivePassword, null, ct);
    }

    Task<UseOutcome> IPreviewHost.ExtractWithPromptAsync(int generation, ArchiveEntry entry, string dest)
    {
        if (generation != _archiveGeneration || CurrentArchivePath is not { } archive) return Task.FromResult(UseOutcome.Cancelled);
        return ExtractForUseAsync(archive, new[] { DragPayloadBuilder.NormalizeKey(entry.Path) }, dest, null, "미리 볼 수 없어요");
    }

    // ───────────────────────────── I1-4 Ctrl+C ─────────────────────────────

    /// <summary>이번 실행에서 Ctrl+C 로 꺼내 둔 사본 폴더(오래된 것부터) — 쌓이지 않게 <see cref="TakeOldClipCopies"/>.</summary>
    private readonly List<string> _clipCopies = new();

    /// <summary>Ctrl+C 가 도는 중(진행 창은 모달이 아니라 그 사이 또 누를 수 있음 — 겹치지 않게).</summary>
    private bool _copyInFlight;

    /// <summary>
    /// 앱을 켜 둔 동안 남기는 Ctrl+C 사본 수: 지금 클립보드에 있는 것 + 바로 앞 것(그 붙여넣기가 아직 도는 중일 수 있어서).
    /// 그보다 오래된 것은 클립보드에서 이미 밀려났으므로 지운다(큰 동영상을 여러 번 복사해도 C: 에 쌓이지 않게 — W2-04 1차 검증).
    /// </summary>
    internal const int ClipCopiesKeptWhileRunning = 2;

    /// <summary>
    /// 고른 항목을 <c>temp\clip\guid\</c> 에 꺼내(진행 창·취소 가능) 클립보드에 파일 목록으로 넣는다 — 탐색기에서 붙여넣기(Ctrl+V)(I1-4).
    /// <list type="bullet">
    /// <item>꺼낸 뒤의 정리(꺼낸 자리 찾기·인터넷 표식 붙이기 — <see cref="ClipboardCopyPlan"/>)도 같은 진행 창을 띄운 채 뒤 스레드에서 한다 —
    ///   수천 개를 골라도 화면이 굳지 않고 [취소]가 먹는다(IJ3-01 — W2-04 보관본은 화면 스레드에서 고른 것마다 폴더를 다시 읽어 오래 굳었다).</item>
    /// <item>디스크 이름은 풀기 엔진 규칙대로다('회의록 10:00.txt' → '회의록 10_00.txt', 'CON.txt' → '_CON.txt', '...' → '.._', 대소문자만 다른 두 항목의 뒤엣것 →
    ///   'report (1).txt'). 바뀐 이름은 넣은 뒤 알린다('이름을 바꿔서 복사했어요' — 최대 5개 + 외 N개).</item>
    /// <item>고른 것 중 하나라도 꺼낸 자리를 못 찾으면 말없이 빼지 않고 알린 뒤 아무것도 넣지 않는다.</item>
    /// <item>사본은 앱을 닫아도 마지막 것은 남긴다(붙여넣기가 늦을 수 있음 — 다음 시작 때 24시간 지난 것만 지움). 취소·실패면 사본을 지우고 클립보드는 그대로.</item>
    /// </list>
    /// </summary>
    public async Task<bool> CopyEntriesToClipboardAsync(IReadOnlyList<ArchiveEntry> selected)
    {
        if (!IsArchiveMode || CurrentArchivePath is not { } archive) return false;
        if (_copyInFlight)
        {
            StatusText = "앞의 복사가 아직 끝나지 않았어요. 끝난 뒤에 다시 해 주세요";
            return false;
        }
        _copyInFlight = true;
        try
        {
            return await CopyEntriesCoreAsync(archive, selected);
        }
        finally
        {
            _copyInFlight = false;
        }
    }

    private async Task<bool> CopyEntriesCoreAsync(string archive, IReadOnlyList<ArchiveEntry> selected)
    {
        var picks = ClipboardCopyPlan.Picks(selected);
        if (picks.Count == 0) return false;
        var keys = picks.Select(p => p.Key).ToList();
        var all = Entries.ToList();   // 뒤 스레드는 화면 목록을 만지지 않게 사본으로

        string dest = Path.Combine(AppPaths.ClipRoot, Guid.NewGuid().ToString("N"));
        ClipboardCopyPlan.Result? plan = null;
        bool keepMark = Settings.Extract.KeepInternetMark;   // IJ3-03: 설정으로 끌 수 있음(기본 켬)
        var outcome = await ExtractForUseAsync(archive, keys, dest, "복사할 파일을 꺼내는 중...", "복사하지 못했어요",
            session => plan = ClipboardCopyPlan.Collect(dest, picks, all, ZoneIdentifier.ReadIfEnabled(archive, keepMark), session.Progress, session.CancellationToken));
        if (outcome != UseOutcome.Done || plan is null)
        {
            await DeleteInBackgroundAsync(dest);
            StatusText = outcome == UseOutcome.Cancelled ? "복사를 취소했어요" : "오류 발생";
            return false;
        }
        if (plan.Missing.Count > 0 || plan.Paths.Count == 0)
        {
            Logger.LogError($"복사: 꺼낸 자리를 못 찾음 {archive} → {KeysForLog(plan.Missing)}", new FileNotFoundException(dest));
            await DeleteInBackgroundAsync(dest);
            _ui.ShowError("복사하지 못했어요:\n\n꺼낸 파일을 찾지 못했어요" + MissingList(plan.Missing));
            StatusText = "오류 발생";
            return false;
        }

        try
        {
            _services.Clipboard.SetFileDrop(plan.Paths, DragDropEffects.Copy);
        }
        catch (Exception ex)
        {
            Logger.LogError("클립보드에 넣지 못함", ex);
            await DeleteInBackgroundAsync(dest);
            _ui.ShowError("복사하지 못했어요:\n\n다른 프로그램이 클립보드를 쓰고 있어요. 잠시 뒤 다시 해 주세요.");
            StatusText = "오류 발생";
            return false;
        }
        _clipCopies.Add(dest);
        var old = TakeOldClipCopies(ClipCopiesKeptWhileRunning);
        if (old.Count > 0) await Task.Run(() => { foreach (var d in old) TempFolders.DeleteBestEffort(d); });

        int count = plan.Paths.Count;
        StatusText = plan.Renamed.Count == 0
            ? $"{count}개 항목을 복사했어요 — 탐색기에서 붙여넣기(Ctrl+V) 하세요"
            : $"{count}개 항목을 복사했어요(이름 {plan.Renamed.Count}개는 바꿈) — 탐색기에서 붙여넣기(Ctrl+V) 하세요";
        if (ClipboardCopyPlan.RenamedNotice(plan.Renamed) is { } notice) _ui.ShowInfo(notice);
        return true;
    }

    /// <summary>사본 폴더를 뒤에서 지운다(파일 수천 개면 몇 초 — 화면이 굳지 않게).</summary>
    private static Task DeleteInBackgroundAsync(string dir) => Task.Run(() => TempFolders.DeleteBestEffort(dir));

    /// <summary>이번 실행의 Ctrl+C 사본 중 최근 <paramref name="keep"/> 개만 남기고 나머지를 목록에서 빼 돌려준다(부른 쪽이 지움).</summary>
    private List<string> TakeOldClipCopies(int keep)
    {
        var old = new List<string>();
        while (_clipCopies.Count > keep)
        {
            old.Add(_clipCopies[0]);
            _clipCopies.RemoveAt(0);
        }
        return old;
    }

    /// <summary>못 찾은 항목 이름(최대 5개) — 오류 창 뒤에 붙임.</summary>
    private static string MissingList(IReadOnlyList<string> keys)
    {
        if (keys.Count == 0) return ".";
        var lines = keys.Take(5).Select(k => "- " + k[(k.LastIndexOf('/') + 1)..]).ToList();
        if (keys.Count > 5) lines.Add($"외 {keys.Count - 5}개");
        return ":\n" + string.Join("\n", lines);
    }

    // ───────────────────────────── I1-4 끌어 꺼내기 ─────────────────────────────

    /// <summary>진행 중인 끌기(앱을 닫을 때 정리).</summary>
    private readonly List<VirtualFileDataObject> _activeDrags = new();

    /// <summary>끌어내기 진행 창을 띄울 크기 — 이번에 꺼낼 파일들의 합(한 번에 꺼내기면 끌어낸 파일 전부). 이보다 작으면 금방 끝나 창 없이.</summary>
    internal const long DragProgressThreshold = 8L * 1024 * 1024;

    /// <summary>끌기 전에 암호를 물어야 하는가(고른 것 안에 암호 항목이 있는데 아는 암호가 없음). 끌기 도중에는 묻지 않는다.</summary>
    public bool DragNeedsPassword(IReadOnlyList<ArchiveEntry> selected) =>
        string.IsNullOrEmpty(_archivePassword) && ExpandFiles(selected).Any(e => e.IsEncrypted && e.Size > 0);

    /// <summary>
    /// 끌기 전 암호 확인: 고른 것 중 가장 작은 암호 파일을 잠깐 꺼내 보며 암호를 묻는다(틀리면 다시). 맞으면 기억하고
    /// '다시 끌어 주세요'(암호 창 때문에 마우스를 놓았으므로 이번 끌기는 하지 않음). 맞췄으면 true.
    /// </summary>
    public async Task<bool> EnsurePasswordForDragAsync(IReadOnlyList<ArchiveEntry> selected)
    {
        if (!IsArchiveMode || CurrentArchivePath is not { } archive) return false;
        var probe = ExpandFiles(selected).Where(e => e.IsEncrypted && e.Size > 0).MinBy(e => e.Size);
        if (probe is null) return true;
        string dest = Path.Combine(AppPaths.DragRoot, Guid.NewGuid().ToString("N"));
        try
        {
            var outcome = await ExtractForUseAsync(archive, new[] { DragPayloadBuilder.NormalizeKey(probe.Path) }, dest, "암호를 확인하는 중...", "꺼내지 못했어요");
            if (outcome != UseOutcome.Done)
            {
                if (outcome == UseOutcome.Cancelled) StatusText = "끌기를 취소했어요";
                return false;
            }
            StatusText = "암호를 확인했어요. 다시 끌어 주세요.";
            return true;
        }
        finally
        {
            TempFolders.DeleteBestEffort(dest);
        }
    }

    /// <summary>
    /// 끌기 데이터(<see cref="VirtualFileDataObject"/>)를 만든다 — 아직 아무것도 꺼내지 않는다(놓은 순간에 꺼냄 — 파일이 여럿이면 한 번에, <see cref="LazyEntryExtractor"/>).
    /// 경로가 너무 긴 항목이 있으면 알리고 끌지 않는다. 끌 것이 없으면 null. 암호는 <see cref="DragNeedsPassword"/> 로 먼저 확인할 것.
    /// </summary>
    public VirtualFileDataObject? CreateDragData(IReadOnlyList<ArchiveEntry> selected)
    {
        if (!IsArchiveMode || CurrentArchivePath is not { } archive) return null;
        var payload = DragPayloadBuilder.Build(selected, Entries.ToList());
        if (payload.TooLong.Count > 0)
        {
            _ui.ShowWarning($"경로가 너무 긴 항목 {payload.TooLong.Count}개는 끌어낼 수 없어요. [압축 풀기]로 풀어 주세요.");
            return null;
        }
        if (payload.Items.Count == 0) return null;

        // 끄는 동안 화면이 다른 압축을 열어도 영향이 없게 서비스를 따로 쓴다(설계 위험 항목)
        var service = new ArchiveServiceLocator().ResolveByContent(archive);
        if (service is null) return null;
        string root = Path.Combine(AppPaths.DragRoot, Guid.NewGuid().ToString("N"));
        var ui = SynchronizationContext.Current;
        var extractor = new LazyEntryExtractor(service, archive, _archivePassword, payload.Items, root,
            items => items.Sum(i => Math.Max(0, i.Size)) >= DragProgressThreshold ? OpenProgressOn(ui, DragProgressTitle(items)) : null)
        {
            KeepInternetMark = Settings.Extract.KeepInternetMark,   // IJ3-03: 끄면 임시 파일에도 끌기 데이터('ZoneIdentifier' 형식)에도 표식 없음
        };
        // 오류 창은 끌기 한 번에 한 번만 — 손상·경로 공격 항목이 여럿 든 폴더를 끌면 실패한 파일마다 창이 줄줄이 떴다(탐색기도 제 오류 창을 띄움 — W2-04 2차 검증).
        // 뒤의 실패는 알림줄에 개수만 센다.
        int failures = 0;
        extractor.ExtractionFailed += (_, ex) =>
        {
            int n = Interlocked.Increment(ref failures);
            RunOn(ui, () =>
            {
                if (n == 1) _ui.ShowError($"끌어 꺼내지 못했어요:\n\n{ErrorMessages.ToUserMessage(ex)}");
                StatusText = n == 1 ? "오류 발생" : $"오류 발생 — 끌어 꺼내지 못한 파일 {n}개";
            });
        };
        var data = new VirtualFileDataObject(extractor);
        lock (_activeDrags) _activeDrags.Add(data);
        data.Finished += d =>
        {
            lock (_activeDrags) _activeDrags.Remove(d);
        };
        return data;
    }

    /// <summary>끌어 꺼내기 진행 창 제목 — 파일 하나면 그 이름, 여럿(한 번에 꺼내기)이면 개수.</summary>
    internal static string DragProgressTitle(IReadOnlyList<DragItem> items) =>
        items.Count == 1 ? $"'{Path.GetFileName(items[0].RelativePath)}' 꺼내는 중..." : $"끌어낸 파일 {items.Count}개를 꺼내는 중...";

    /// <summary>진행 창을 UI 스레드에서 열고, 닫기도 UI 스레드에서(끌기 데이터 요청은 다른 스레드로 올 수 있음).</summary>
    private IProgressSession OpenProgressOn(SynchronizationContext? ui, string title)
    {
        IProgressSession? session = null;
        RunOnSync(ui, () => session = _ui.ShowProgress(title));
        return new MarshalledSession(session!, ui);
    }

    private static void RunOnSync(SynchronizationContext? ui, Action action)
    {
        if (ui is null || SynchronizationContext.Current == ui) action();
        else ui.Send(_ => action(), null);
    }

    private static void RunOn(SynchronizationContext? ui, Action action)
    {
        if (ui is null || SynchronizationContext.Current == ui) action();
        else ui.Post(_ => action(), null);
    }

    /// <summary>진행 창 닫기는 UI 로 '넘기기만'(Post) — 꺼내는 스레드가 UI 를 기다리지 않게(UI 가 꺼내기를 기다리는 중일 수 있음).</summary>
    private sealed class MarshalledSession(IProgressSession inner, SynchronizationContext? ui) : IProgressSession
    {
        public IProgress<ArchiveProgress> Progress => inner.Progress;
        public CancellationToken CancellationToken => inner.CancellationToken;
        public void Close() => RunOn(ui, inner.Close);
    }

    /// <summary>
    /// 고른 것(폴더면 그 안 전부)의 파일 항목. 압축 전체를 한 번만 훑는다(항목마다 조상 경로를 고른 폴더와 맞춰 봄) — IJ3-01: W2-04 보관본은
    /// 고른 것마다 전체를 다시 훑어, 수천 개를 골라 끌기 시작하면(마우스를 움직이는 순간 화면 스레드에서) 오래 굳었다.
    /// </summary>
    private List<ArchiveEntry> ExpandFiles(IReadOnlyList<ArchiveEntry> selected)
    {
        var result = new List<ArchiveEntry>();
        var folders = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var s in selected)
        {
            if (!s.IsDirectory)
            {
                result.Add(s);
                continue;
            }
            string key = DragPayloadBuilder.NormalizeKey(s.Path);
            if (key.Length > 0) folders.Add(key);
        }
        if (folders.Count == 0) return result;
        foreach (var e in Entries)
        {
            if (e.IsDirectory) continue;
            string k = DragPayloadBuilder.NormalizeKey(e.Path);
            for (int slash = k.LastIndexOf('/'); slash > 0; slash = k.LastIndexOf('/', slash - 1))
            {
                if (!folders.Contains(k[..slash])) continue;
                result.Add(e);
                break;
            }
        }
        return result;
    }

    // ───────────────────────────── 닫기 ─────────────────────────────

    /// <summary>앱을 닫을 때(<see cref="Shutdown"/>): Ctrl+C 사본은 마지막 것만 남기고, 남은 끌기를 정리하고, 미리 보기를 비운다.</summary>
    private void ShutdownEntryCopy()
    {
        foreach (var d in TakeOldClipCopies(keep: 1)) TempFolders.DeleteBestEffort(d);
        List<VirtualFileDataObject> drags;
        lock (_activeDrags) drags = _activeDrags.ToList();
        foreach (var d in drags) d.Dispose();
        Preview.Reset();
    }
}
