using System.IO;
using CommunityToolkit.Mvvm.Input;
using Iljip.Models;
using Iljip.Services;

namespace Iljip.ViewModels;

// 압축 검사·오류 원인 구분·깨진 ZIP 고치기(W2-08 — I2-7·I2-8·I3-12).
// ☰ ▸ 시험하기·목록 오른쪽 클릭 '시험하기'·'--test 압축'(메인 창 없이 진행 창·결과 창만) → 압축 검사(AppServices.ArchiveTester — 디스크에 쓰지 않음).
// ☰ ▸ ZIP 고치기…·손상된 ZIP 오류 창의 'ZIP 고치기 시도' → 살릴 수 있는 항목만 '이름_복구.zip' 으로(AppServices.ZipRepairer — 원본 그대로).
// 오류는 모두 문제 창(IUserInteraction.ShowProblem — ErrorCatalog 가 만든 쉬운 한국어, 영어 원문은 '자세히'에만).
public partial class MainViewModel
{
    // ───────────── 문제 창 ─────────────

    /// <summary>
    /// 예외를 문제 창으로 알린다(W2-08 — 영어 원문은 '자세히'에만). <paramref name="heading"/> = 무엇을 하다가('파일을 열 수 없어요.' 등).
    /// 취소면 창을 띄우지 않는다. 손상된 ZIP 이면 'ZIP 고치기 시도'를 고를 수 있고, 고르면 이어서 고친다. '관리자 권한으로 다시 시도'는 아직(W2-11) 버튼을 보이지 않는다.
    /// </summary>
    private async Task ShowProblemAsync(string heading, Exception ex, string? archivePath = null)
    {
        var problem = ErrorCatalog.Describe(ex, archivePath);
        if (problem.Kind == ProblemKind.Cancelled) return;
        await ShowProblemAsync(problem with { Heading = heading }, archivePath);
    }

    /// <summary>만든 문제를 창으로(고를 수 있는 일 중 지금 할 수 있는 것만 버튼으로) — 고친 뒤 결과까지 기다린다.</summary>
    private async Task ShowProblemAsync(UserProblem problem, string? archivePath)
    {
        string? chosen = _ui.ShowProblem(problem with { Actions = UsableActions(problem, archivePath) });
        if (chosen == ErrorCatalog.RepairZipActionId && archivePath is not null)
            await RepairZipFileAsync(archivePath);
    }

    /// <summary>동기 자리용 문제 창 — 이어서 하는 일(고치기 등)의 버튼은 빼고 알리기만 한다.</summary>
    private void ShowProblem(string heading, Exception ex)
    {
        var problem = ErrorCatalog.Describe(ex);
        if (problem.Kind == ProblemKind.Cancelled) return;
        _ui.ShowProblem(problem with { Heading = heading, Actions = Array.Empty<ProblemAction>() });
    }

    /// <summary>지금 할 수 있는 일만: 'ZIP 고치기 시도'(그 ZIP 이 아직 있을 때). '관리자 권한으로 다시 시도'는 W2-11 에서.</summary>
    private static IReadOnlyList<ProblemAction> UsableActions(UserProblem problem, string? archivePath) =>
        problem.Actions.Where(a => a.Id == ErrorCatalog.RepairZipActionId && archivePath is not null && File.Exists(archivePath)).ToList();

    // ───────────── 압축 검사(I2-7) ─────────────

    /// <summary>☰ ▸ 시험하기 / 목록 오른쪽 클릭 '시험하기': 연 압축(없으면 고르는 창)을 검사한다.</summary>
    [RelayCommand]
    private async Task TestArchiveAsync()
    {
        string? path = IsArchiveMode ? CurrentArchivePath : null;
        if (path is null)
        {
            var picked = _ui.PickFiles("시험할 압축 파일 고르기", BuildOpenFilter(), multiselect: false);
            if (picked is not { Count: > 0 }) return;
            path = picked[0];
        }
        await TestArchiveFileAsync(path);
    }

    /// <summary>
    /// 압축 파일 하나를 검사하고 결과를 알린다: 문제없으면 '문제없어요. 파일 128개를 확인했어요.', 문제가 있으면 문제 창('이름 — 이유' 목록, ZIP 이면 'ZIP 고치기 시도').
    /// 암호가 필요하면 묻고(틀리면 다시 — [취소]하면 그만), 이 압축을 열 때·풀 때 맞은 암호가 있으면 그것부터 쓴다. 취소하면 조용히.
    /// </summary>
    /// <returns>검사 결과(취소·실패면 null).</returns>
    internal async Task<TestReport?> TestArchiveFileAsync(string archivePath) => (await TestArchiveCoreAsync(archivePath)).Report;

    /// <summary><see cref="TestArchiveFileAsync"/> 본체 — 취소했는지도 돌려준다(여러 개를 차례로 시험하다 취소하면 멈추게).</summary>
    private async Task<(TestReport? Report, bool Cancelled)> TestArchiveCoreAsync(string archivePath)
    {
        string name = Path.GetFileName(archivePath);
        bool sameArchive = string.Equals(archivePath, CurrentArchivePath, StringComparison.OrdinalIgnoreCase);
        string? password = sameArchive ? _archivePassword : null;
        StatusText = "압축 파일을 시험하는 중...";
        while (true)
        {
            var session = _ui.ShowProgress("압축 파일 시험하는 중...");
            try
            {
                var report = await _services.ArchiveTester.TestAsync(archivePath, password, session.Progress, session.CancellationToken);
                session.Close();
                if (sameArchive && !string.IsNullOrEmpty(password)) _archivePassword = password;
                StatusText = !report.Ok ? $"시험 완료: 문제 있음 ({name})"
                    : report.Notes.Count > 0 ? $"시험 완료: 손상 없음 — 알림 있음 ({name})"   // I5-B
                    : $"시험 완료: 문제없음 ({name})";
                await ShowTestReportAsync(archivePath, report);
                return (report, false);
            }
            catch (OperationCanceledException)
            {
                StatusText = "시험을 취소했어요";
                return (null, true);
            }
            catch (Exception ex) when (IsPasswordRelated(ex))
            {
                session.Close();
                var (reason, message) = PasswordPrompt(ex, password);
                var pw = _ui.AskPassword(name, reason, message);
                if (pw is null)
                {
                    StatusText = "시험을 취소했어요";
                    return (null, true);
                }
                password = pw;
            }
            catch (Exception ex)
            {
                session.Close();
                Logger.LogError($"압축 시험 실패: {archivePath}", ex);
                StatusText = "오류 발생";
                await ShowProblemAsync($"‘{name}’을 시험하지 못했어요.", ex, archivePath);
                return (null, false);
            }
            finally
            {
                session.Close();
            }
        }
    }

    /// <summary>검사 결과 창: 문제없으면 안내, 있으면 문제 창(원문은 '자세히', ZIP 이면 'ZIP 고치기 시도').</summary>
    private async Task ShowTestReportAsync(string archivePath, TestReport report)
    {
        string name = Path.GetFileName(archivePath);
        if (report.Ok)
        {
            // I5-B: 손상은 없지만 알릴 것(tar 링크는 풀면 내용 없이 만들어짐)이 있으면 '문제없어요' 대신 알림 창에 그 줄들까지
            if (report.Notes.Count > 0) _ui.ShowWarning($"‘{name}’ 시험 결과\n\n{report.ToDisplayText()}");
            else _ui.ShowInfo($"‘{name}’ 시험 결과\n\n{report.Headline}");
            return;
        }
        var problem = new UserProblem
        {
            Kind = ProblemKind.Corrupt,
            Title = "시험 결과 — 문제가 있어요",
            Heading = $"‘{name}’을 시험했더니 문제가 있어요.",
            Message = report.ToDisplayText(),
            Details = report.DetailText(),
            // 고치기는 손상·잘림·검사값 불일치일 때만(풀 수 없는 압축 방식·조각 빠짐은 고쳐도 소용없음 — W2-08 검증 지적). 나눠진 ZIP 의 조각(.zip.001·.z01)도(IJ3-02)
            Actions = ZipRepairer.IsRepairableName(archivePath) && report.HasDamage ? new[] { ErrorCatalog.RepairZipAction } : Array.Empty<ProblemAction>(),
        };
        await ShowProblemAsync(problem, archivePath);
    }

    /// <summary>'--test 압축…'(메인 창 없이 — 진행 창·결과 창만): 있는 압축마다 차례로 검사한다. 없는 파일이면 알린다.</summary>
    private async Task RunTestCommandAsync(ParsedCommand cmd)
    {
        var archives = ExistingArchives(cmd);
        if (archives.Count == 0)
        {
            if (cmd.Archives.Count > 0)
                _ui.ShowError($"시험할 압축 파일을 찾을 수 없어요. 지웠거나 다른 곳으로 옮겼는지 확인해 주세요.\n\n{cmd.Archives[0]}");
            return;
        }
        foreach (var archive in archives)
            if ((await TestArchiveCoreAsync(archive)).Cancelled) break;   // 취소하면 남은 것은 시험하지 않는다
    }

    // ───────────── 깨진 ZIP 고치기(I3-12) ─────────────

    /// <summary>☰ ▸ ZIP 고치기…: 연 ZIP(없으면 고르는 창)을 고친다.</summary>
    [RelayCommand]
    private async Task RepairZipAsync()
    {
        string? path = IsArchiveMode && CurrentArchivePath is { } cur && ZipRepairer.IsRepairableName(cur) ? cur : null;
        if (path is null)
        {
            var picked = _ui.PickFiles("고칠 ZIP 파일 고르기", "ZIP 압축 파일 (*.zip, 나눠진 조각 *.zip.001·*.z01)|*.zip;*.zip.001;*.z01|모든 파일 (*.*)|*.*", multiselect: false);
            if (picked is not { Count: > 0 }) return;
            path = picked[0];
        }
        await RepairZipFileAsync(path);
    }

    /// <summary>
    /// 깨진 ZIP 고치기: 저장할 곳(기본 '이름_복구.zip')을 묻고, 암호 항목이 있으면 암호를 묻는다([취소]면 내용 확인 없이 그대로 옮김),
    /// 진행 창을 띄워 살릴 수 있는 항목만 새 ZIP 으로 옮긴 뒤 '전체 128개 중 120개를 살렸어요. 못 살린 파일: …'(목록이 없으면 '원래 몇 개였는지 확실하지 않아요 — …'). 원본은 그대로.
    /// </summary>
    /// <returns>결과(취소·실패면 null).</returns>
    internal async Task<RepairReport?> RepairZipFileAsync(string archivePath)
    {
        string name = Path.GetFileName(archivePath);
        string? dest = _ui.AskSavePath("고친 ZIP 저장", "ZIP 압축 파일 (*.zip)|*.zip", ZipRepairer.SuggestedName(archivePath),
            Path.GetDirectoryName(Path.GetFullPath(archivePath)) ?? string.Empty, ".zip");
        if (dest is null) return null;

        bool sameArchive = string.Equals(archivePath, CurrentArchivePath, StringComparison.OrdinalIgnoreCase);
        string? password = sameArchive ? _archivePassword : null;
        if (string.IsNullOrEmpty(password))
        {
            // 암호 항목이 있는지 먼저 훑는다 — 항목이 아주 많거나 큰 ZIP 은 오래 걸릴 수 있어 진행 창(취소 가능)을 띄우고 작업 스레드에서(W2-08 검증 지적: 화면이 멈춤)
            StatusText = "ZIP 을 살펴보는 중...";
            var probe = _ui.ShowProgress("ZIP 살펴보는 중...");
            bool encrypted;
            try
            {
                encrypted = await _services.ZipRepairer.HasEncryptedEntriesAsync(archivePath, probe.Progress, probe.CancellationToken);
            }
            catch (OperationCanceledException)
            {
                StatusText = "ZIP 고치기를 취소했어요";
                return null;
            }
            finally
            {
                probe.Close();
            }
            if (encrypted) password = _ui.AskPassword(name, PasswordPromptReason.Required, RepairPasswordMessage);
        }

        StatusText = "ZIP 을 고치는 중...";
        while (true)
        {
            var session = _ui.ShowProgress("ZIP 고치는 중...");
            try
            {
                var report = await _services.ZipRepairer.RepairAsync(archivePath, dest, password, session.Progress, session.CancellationToken);
                session.Close();
                StatusText = report.OutputPath is not null ? $"ZIP 고치기 완료: {report.OutputPath}" : "ZIP 고치기 — 살린 파일 없음";
                // I4-1(F2): 목록의 모든 파일을 확인해 살렸을 때만 안내 창, 그 밖(못 살림·확인 못 함·원래 개수를 모름·새 ZIP 없음)은 주의 창
                if (report.IsComplete) _ui.ShowInfo($"‘{name}’ 고치기 결과\n\n{report.ToDisplayText()}");
                else _ui.ShowWarning($"‘{name}’ 고치기 결과\n\n{report.ToDisplayText()}");
                return report;
            }
            catch (OperationCanceledException)
            {
                StatusText = "ZIP 고치기를 취소했어요";
                return null;
            }
            catch (WrongPasswordException)
            {
                session.Close();
                // 다시 묻는다 — [취소]면 암호 없이(내용 확인 없이 그대로 옮김)
                password = _ui.AskPassword(name, PasswordPromptReason.Wrong, RepairWrongPasswordMessage);
            }
            catch (Exception ex)
            {
                session.Close();
                Logger.LogError($"ZIP 고치기 실패: {archivePath}", ex);
                StatusText = "오류 발생";
                ShowProblem($"‘{name}’을 고치지 못했어요. 원래 파일은 그대로예요.", ex);
                return null;
            }
            finally
            {
                session.Close();
            }
        }
    }

    /// <summary>고치기 전에 암호를 물을 때의 문구.</summary>
    internal const string RepairPasswordMessage =
        "암호가 걸린 파일이 있어요. 암호를 넣으면 내용까지 확인하고 살려요.\n[취소]를 누르면 내용을 확인하지 않고 그대로 옮겨요.";

    /// <summary>고치다가 암호가 맞지 않을 때의 문구.</summary>
    internal const string RepairWrongPasswordMessage =
        "암호가 맞지 않아요(파일이 손상된 경우에도 이렇게 보일 수 있어요). 다시 넣어 주세요.\n[취소]를 누르면 내용을 확인하지 않고 그대로 옮겨요.";
}
