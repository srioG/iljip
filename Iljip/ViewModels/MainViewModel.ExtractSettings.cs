using System.IO;
using Iljip.Services;

namespace Iljip.ViewModels;

// 풀기 설정(같은 이름 처리 I1-5·풀고 나서 압축 파일 지우기 I2-5·우클릭 풀기 뒤 폴더 열기 I1-6)과 그 도우미 — W2-05.
// 설정은 처음 쓸 때 한 번 읽고(AppServices.Settings), 설정 창에서 [확인]할 때 한 번 저장한다(DLP 연속 재기록 경합 교훈).
public partial class MainViewModel
{
    /// <summary>셸·휴지통·설정(시험은 가짜).</summary>
    private readonly AppServices _services;

    private AppSettings? _settings;

    /// <summary>지금 설정(처음 부를 때 settings.json 을 읽음 — 없거나 깨졌으면 기본값).</summary>
    public AppSettings Settings => _settings ??= _services.Settings.Load();

    /// <summary>설정 창에서 고친 설정을 쓰고 한 번 저장한다.</summary>
    public void ApplySettings(AppSettings settings)
    {
        _settings = settings;
        _services.Settings.Save(settings);
    }

    /// <summary>같은 이름 처리기 — 설정(extract.onCollision)대로. 해제 한 번(암호를 다시 물어 다시 푸는 것 포함)·여러 개 풀기 한 묶음에 하나.</summary>
    private ICollisionResolver CreateCollisionResolver() => AskingResolver.For(Settings.Extract.OnCollision, _ui);

    /// <summary>
    /// 풀고 나서 압축 파일 지우기(I2-5, 설정 extract.deleteArchiveAfter — 기본 꺼짐). 부르는 쪽이 '전체를 오류 없이 다 풀었음'을 확인한 뒤 부른다.
    /// 여기서 더 보는 것: 건너뛴 파일 0개, 일집 임시 폴더 안의 압축이 아님, 휴지통이 있는 곳(로컬 드라이브 — 네트워크·이동식이면 영구 삭제가 되므로 그대로 두고 알림).
    /// 나뉜 압축은 조각을 모두(<see cref="ArchiveVolumes.AllParts"/>). 휴지통으로만 보내고, 못 보내면 그대로 둔다.
    /// 지웠고 그 압축을 보고 있었으면 화면을 비운다. 사용자에게 알릴 말이 있으면 돌려준다(없으면 null — 지웠거나 지울 일이 아님).
    /// 창 없이 풀기(--extract-to)는 이 길을 타지 않는다(설정을 읽지 않음 — 재사용장치 계약).
    /// </summary>
    internal string? RecycleArchiveAfterExtract(string archivePath, int skipped)
    {
        if (!Settings.Extract.DeleteArchiveAfter) return null;
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
