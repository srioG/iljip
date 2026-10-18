using System.IO;
using Iljip.Models;
using Iljip.Services;

namespace Iljip.ViewModels;

// 탐색기 연결(W2-12) — 우클릭 '"이름.zip"으로 압축'·'"이름.7z"로 압축'(창 없이 바로 압축, I1-2)과 ☰ ▸ 설정 '파일 연결'(I1-1).
// 알아서 풀기('--extract-smart')는 여러 개 풀기(BatchExtractor — ExtractTargetKind.Smart)를 그대로 쓴다(MainViewModel.CommandLine.cs).
// 레지스트리·셸·기본 앱 설정은 AppServices(시험은 가짜 — 진짜 HKCU·ms-settings 를 절대 부르지 않음).
public partial class MainViewModel
{
    // ───────────── 창 없이 바로 압축(I1-2) ─────────────

    /// <summary>창 없이 바로 압축의 진행 창 제목.</summary>
    internal const string QuickCompressTitle = "압축 중...";

    /// <summary>
    /// 우클릭 '"이름.zip"으로 압축'·'"이름.7z"로 압축'(--compress-zip·--compress-7z): 압축 창 없이 첫 원본이 있는 폴더에 '이름.zip/.7z'
    /// (이름은 압축 창 제안과 같은 규칙 — 하나면 그 이름, 여럿이면 그 폴더 이름. 있으면 '이름 (2)'). 수준·7z 솔리드·쓸모없는 파일 빼기·'다 만들고 시험하기'는
    /// 마지막 설정(compress.*), 암호·나누기·날짜·원본 지우기는 쓰지 않는다(창이 없어 묻지 않음). 설정은 바꾸지 않는다.
    /// 메인 창 없이 진행 창만 — 되면 조용히(상태 줄만), 건너뛴 파일·뺀 파일이 있으면 안내 한 번, 못 만들면 쉬운 한국어 오류(권한이면 관리자 권한으로 다시 시도 — 압축 창으로).
    /// </summary>
    internal async Task<CompressFlowResult?> RunQuickCompressAsync(IReadOnlyList<string> paths, CompressionFormat format)
    {
        var sources = paths.Where(p => File.Exists(p) || Directory.Exists(p))
            .Select(Path.GetFullPath)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (sources.Count == 0)
        {
            if (paths.Count > 0)
                _ui.ShowError($"압축할 파일을 찾을 수 없어요. 지웠거나 다른 곳으로 옮겼는지 확인해 주세요.\n\n{paths[0]}");
            return null;
        }
        if (format == CompressionFormat.SevenZip && !SevenZipLibrary.IsAvailable)
        {
            _ui.ShowError($"‘{CompressNaming.SuggestBaseName(sources, format)}.7z’를 만들지 못했어요.\n\n{EngineUnavailableException.DefaultMessage}");
            return null;
        }

        var request = BuildQuickCompressRequest(sources, format);
        var flow = new CompressFlow(_locator, _ui, _services.RecycleBin, _services.CompressTester, _services.Clock);
        var session = _ui.ShowProgress(QuickCompressTitle);
        CompressFlowResult result;
        try
        {
            result = await flow.RunAsync(request, session);
        }
        finally
        {
            session.Close();
        }

        var o = result.Outcomes.FirstOrDefault();
        if (o is null || !o.Succeeded)
        {
            if (result.Cancelled || o is null)
            {
                StatusText = "압축 취소됨";
                return result;
            }
            string failure = o.Error is NothingToCompressException none
                ? NothingCompressedMessage(none.Skipped)
                : $"‘{Path.GetFileName(o.Target)}’을 만들지 못했어요.\n\n{(o.Error is { } ex ? ErrorMessages.ToCompressMessage(ex) : o.Reason)}";   // 압축 실패 문구(W2-08 — 풀기 문구 아님)
            // 만들 곳에 쓸 권한이 없으면 관리자 권한으로 다시 — 같은 원본·같은 만들 곳으로 압축 창부터('--compress-to', 암호 없이 같은 일을 다시 고를 수 있게)
            if (o.Error is null || !OfferElevatedRetry(o.Error, failure, CompressRetryFor(request, request.Sources), null, request.OutputFolder))
                _ui.ShowError(failure);
            StatusText = "오류 발생";
            return result;
        }

        var r = o.Result!;
        StatusText = $"압축 완료: {o.ResultPath}";
        // 되면 조용히(창 없이 바로 압축) — 빠진 것이 있을 때만 알린다(시험 통과 같은 알림은 그때 같이). 시험에서 문제가 있으면 늘 경고.
        var notes = new List<string>();
        if (r.Skipped.Count > 0) notes.Add(SkippedMessage(r.Skipped));
        if (r.Excluded > 0) notes.Add(CompressFlow.ExcludedMessage(r.ExcludedPaths));
        if (notes.Count > 0)
        {
            notes.AddRange(result.Notes);
            string noteText = string.Join("\n\n", notes);
            _ui.ShowInfo($"‘{Path.GetFileName(o.ResultPath)}’을 만들었어요.\n\n{noteText}");
        }
        if (result.Warnings.Count > 0)
            _ui.ShowWarning(string.Join("\n\n", result.Warnings));
        return result;
    }

    /// <summary>창 없이 바로 압축의 요청(설정의 마지막 값 — 사본만 읽음, 암호·나누기·날짜·원본 지우기 없음, 같은 이름은 '이름 (2)').</summary>
    internal CompressionRequest BuildQuickCompressRequest(IReadOnlyList<string> sources, CompressionFormat format)
    {
        var c = Settings.Compress;
        return new CompressionRequest
        {
            Sources = sources,
            OutputFolder = Path.GetDirectoryName(sources[0]) ?? Environment.CurrentDirectory,
            BaseName = CompressNaming.SuggestBaseName(sources, format),
            Format = format,
            Options = new CompressionOptions { Level = c.Level, Solid = c.Solid },
            EachSeparately = false,
            DateInName = DateNameFormat.None,
            ExcludeEnabled = c.ExcludeEnabled,
            ExcludePatterns = c.ExcludePatterns.ToList(),
            TestAfter = c.TestAfter,
            DeleteOriginalsAfter = false,
            OverwriteExisting = false,
        };
    }

    // ───────────── 파일 연결(I1-1) — ☰ ▸ 설정 '파일 연결' ─────────────

    /// <summary>파일 연결에 쓸 실행 파일(지금 일집). 모르면 null.</summary>
    private string? AssociationExePath => _services.Processes.ExecutablePath;

    /// <summary>드롭 처리기(zip·7z 위에 끌어 놓아 넣기 — I3-10)를 등록할 수 있는가: 셸 확장 DLL 이 일집 옆에 있을 때(MSIX 셸 확장 설치본).</summary>
    public bool CanRegisterDropHandler => FileAssociationCommand.DropHandlerDllFor(AssociationExePath) is not null;

    /// <summary>지금 일집이 넣은 파일 연결이 있는가(레지스트리 기록 — 설정 창 '지금 상태').</summary>
    public bool IsFileAssociationRegistered
    {
        get
        {
            try { return new FileAssociationRegistrar(_services.Registry).IsRegistered(); }
            catch (Exception ex)
            {
                Logger.LogError("파일 연결 상태를 읽지 못함", ex);
                return false;
            }
        }
    }

    /// <summary>
    /// 파일 연결 넣기(☰ ▸ 설정 [연결 프로그램에 넣기] = '--register-associations'): HKCU 에 '연결 프로그램' 후보·기본 앱 목록 칸을 넣고(기본 앱은 빼앗지 않음)
    /// 탐색기에 알린다. 설정 shell.associationsRegistered 를 기록. 화면에 보일 문구를 돌려준다(설정 창이 그 자리에 보임 — 창을 따로 띄우지 않음).
    /// </summary>
    public string RegisterFileAssociations(bool dropHandler)
    {
        var run = FileAssociationCommand.Register(_services, AssociationExePath, dropHandler);
        if (run.Ok) RememberAssociations(true);
        return run.Message;
    }

    /// <summary>파일 연결 빼기(☰ ▸ 설정 [연결 빼기] = '--unregister-associations'): 일집이 넣은 것(기록에 적힌 것)만 뺀다.</summary>
    public string UnregisterFileAssociations()
    {
        var run = FileAssociationCommand.Unregister(_services);
        if (run.Ok && run.Changed) RememberAssociations(false);
        return run.Message;
    }

    /// <summary>
    /// Windows '기본 앱' 설정 화면(일집 칸)을 연다 — Windows 11 은 기본 앱을 프로그램이 바꿀 수 없어 사용자가 직접 고른다. 못 열면 안내 문구를 돌려준다(되면 null).
    /// </summary>
    public string? OpenDefaultAppsSettings()
    {
        try
        {
            _services.Shell.OpenDefaultAppsSettings();
            return null;
        }
        catch (Exception ex)
        {
            Logger.LogError("기본 앱 설정을 열지 못함", ex);
            return "Windows 설정을 열지 못했어요. 시작 ▸ 설정 ▸ 앱 ▸ 기본 앱에서 ‘일집’을 찾아 주세요.";
        }
    }

    /// <summary>shell.associationsRegistered 기록(한 번 저장 — 최신 설정에 이 칸만 얹음).</summary>
    private void RememberAssociations(bool registered)
    {
        if (Settings.Shell.AssociationsRegistered == registered) return;
        void Change(AppSettings s) => s.Shell.AssociationsRegistered = registered;
        Change(Settings);
        SaveSettings(Change);
    }
}
