using System.IO;
using Iljip.Services;

namespace Iljip.ViewModels;

// 풀기 설정(같은 이름 처리 I1-5·풀고 나서 압축 파일 지우기 I2-5·우클릭 풀기 뒤 폴더 열기 I1-6)과 그 도우미 — W2-05.
// 설정은 처음 쓸 때 한 번 읽고(AppServices.Settings), 설정 창에서 [확인]할 때 한 번 저장한다(DLP 연속 재기록 경합 교훈).
// W2-11 검증 지적: 일집 창이 둘이면(새 창) 각 창이 켜질 때 읽은 사본 전체를 쓰면 서로의 최근 파일·설정을 지운다 —
// 저장은 모두 SaveSettings 로: 저장 직전에 최신 settings.json 을 다시 읽어 이 창이 바꾼 것만 얹는다(ISettingsStore.Update).
public partial class MainViewModel
{
    /// <summary>셸·휴지통·설정(시험은 가짜).</summary>
    private readonly AppServices _services;

    private AppSettings? _settings;

    /// <summary>지금 설정(처음 부를 때 settings.json 을 읽음 — 없거나 깨졌으면 기본값). 이 객체는 바꾸지 않고 내용만 고친다(최근 파일 다루기가 쥐고 있음).</summary>
    public AppSettings Settings => _settings ??= _services.Settings.Load();

    /// <summary>
    /// 설정 창에서 고친 설정: 이 창의 설정과 다른 칸만(설정 창이 바꾼 것) 이 창에 얹고, 최신 설정에도 그 칸만 얹어 한 번 저장한다
    /// (다른 창이 그사이 바꾼 칸은 그대로 — W2-11 검증 지적). 최근 파일은 설정 창이 다루지 않는다. 바뀐 칸이 없으면 저장하지 않는다.
    /// </summary>
    public void ApplySettings(AppSettings settings)
    {
        // W2-12: shell.*(파일 연결 기록)은 설정 창의 [연결 프로그램에 넣기]가 바로 저장한다 — 창이 열릴 때 사본의 옛 값으로 되돌리지 않게 뺀다
        var patch = SettingsPatch.Between(Settings, settings, "recent", "shell");
        if (patch.IsEmpty) return;
        patch.ApplyTo(Settings);
        SaveSettings(patch.ApplyTo);
    }

    /// <summary>아직 저장하지 못한 이 창의 설정 바꾸기(차례대로) — 다음 저장(창을 닫을 때 포함)에 다시 얹는다.</summary>
    private readonly List<Action<AppSettings>> _unsavedSettings = new();

    /// <summary>
    /// 설정 저장(W2-11 검증 지적 — 창 두 개): 이 창의 사본 전체를 쓰지 않고, 저장 직전 최신 settings.json 을 다시 읽어
    /// 이 창이 바꾼 것(<paramref name="change"/> — 이 창의 설정에는 부르는 쪽이 이미 했음, 앞서 못 저장한 것도 차례대로)만 얹고,
    /// 최근 파일은 이 창에서 넣고 뺀 것만 합친다(<see cref="RecentFilesService.Merge"/>). 그래서 다른 창이 그사이 저장한 최근 파일·설정을 지우지 않는다.
    /// 저장한 뒤 이 창의 최근 목록도 저장된 것으로 바꾸고 다시 보인다(다른 창이 연 것도 보이게). 못 읽거나 못 쓰면(DLP 잠김 등) 바꾼 것을 들고 있다가 다음 저장 때 다시.
    /// 화면 스레드에서 부른다(최근 파일 목록을 고침).
    /// </summary>
    private void SaveSettings(Action<AppSettings>? change = null)
    {
        if (change is not null) _unsavedSettings.Add(change);
        var changes = _unsavedSettings.ToList();
        var recent = Recent;
        var added = recent.AddedThisSession;
        var removed = recent.RemovedThisSession.ToList();
        AppSettings? saved;
        try
        {
            saved = _services.Settings.Update(latest =>
            {
                foreach (var c in changes) c(latest);
                latest.Recent.Files = RecentFilesService.Merge(added, latest.Recent.Files, removed);
            });
        }
        catch (Exception ex)
        {
            Logger.LogError("설정을 저장하지 못함", ex);
            saved = null;
        }
        if (saved is null) return;   // 다음 저장에 다시(기록은 저장소가 남김)
        _unsavedSettings.Clear();
        recent.MarkSaved();
        Settings.Recent.Files = saved.Recent.Files.Select(f => new RecentFile { Path = f.Path, LastOpenedUtc = f.LastOpenedUtc }).ToList();
        RefreshRecentFiles();
    }

    /// <summary>아직 저장하지 않은 설정·최근 파일 바뀜이 있는가(창을 닫을 때 저장할지).</summary>
    private bool HasUnsavedSettings => _unsavedSettings.Count > 0 || _recent is { HasUnsavedChanges: true };

    /// <summary>같은 이름 처리기 — 설정(extract.onCollision)대로. 해제 한 번(암호를 다시 물어 다시 푸는 것 포함)·여러 개 풀기 한 묶음에 하나.</summary>
    private ICollisionResolver CreateCollisionResolver() => AskingResolver.For(Settings.Extract.OnCollision, _ui);

    /// <summary>
    /// 풀고 나서 압축 파일 지우기(I2-5, 설정 extract.deleteArchiveAfter — 기본 꺼짐). 부르는 쪽이 '전체를 오류 없이 다 풀었음'을 확인한 뒤 부른다.
    /// 여기서 더 보는 것: 건너뛴 파일 0개, 일집 임시 폴더 안의 압축이 아님, 휴지통이 있는 곳(로컬 드라이브 — 네트워크·이동식이면 영구 삭제가 되므로 그대로 두고 알림).
    /// 나뉜 압축은 조각을 모두(<see cref="ArchiveVolumes.AllParts"/>). 휴지통으로만 보내고, 못 보내면 그대로 둔다.
    /// 지웠고 그 압축을 보고 있었으면 화면을 비운다. 사용자에게 알릴 말이 있으면 돌려준다(없으면 null — 지웠거나 지울 일이 아님).
    /// 창 없이 풀기(--extract-to)는 이 길을 타지 않는다(설정을 읽지 않음 — 재사용장치 계약).
    /// </summary>
    /// <param name="enabled">지울지 — null 이면 설정(extract.deleteArchiveAfter). 풀기 창(W2-09)은 그 창에서 고른 값(이번에만)을 넘긴다.</param>
    internal string? RecycleArchiveAfterExtract(string archivePath, int skipped, bool? enabled = null)
    {
        if (!(enabled ?? Settings.Extract.DeleteArchiveAfter)) return null;
        if (skipped > 0) return null;   // 사용자가 건너뛴 파일이 있으면 압축 파일이 아직 필요할 수 있다
        if (AppPaths.IsUnderTemp(archivePath)) return null;
        if (!File.Exists(archivePath)) return null;

        var parts = ArchiveVolumes.AllParts(archivePath);
        string name = Path.GetFileName(archivePath);
        if (parts.Any(p => !_services.RecycleBin.IsRecyclable(p)))
            return $"‘{name}’은 휴지통이 없는 곳(네트워크 드라이브·USB 등)에 있어 지우지 않고 그대로 두었어요.";

        var result = _services.RecycleBin.Send(parts);
        if (!result.Succeeded)
        {
            Logger.LogError($"풀고 나서 압축 파일을 휴지통으로 옮기지 못함: {archivePath}", new IOException(result.Error ?? "휴지통 실패"));
            return $"‘{name}’ 압축 파일을 휴지통으로 옮기지 못해 그대로 두었어요.";
        }

        if (string.Equals(CurrentArchivePath, archivePath, StringComparison.OrdinalIgnoreCase))
            CloseView();   // 지운 압축을 계속 보여 주지 않는다
        return null;
    }
}
