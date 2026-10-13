using System.IO;
using Iljip.Models;
using Iljip.Services;

namespace Iljip.ViewModels;

// 한 화면 압축 창과 그 뒤(W2-07 — I2-4 각각 따로·I3-4 압축 후 원본 지우기·I3-5 이름에 날짜·I3-6 쓸모없는 파일 빼기·I2-12 설정 기억).
// 창의 처음 값은 설정(compress.*), [확인]이면 그 값을 기억해 한 번 저장(DLP 연속 재기록 경합 교훈 — 압축마다 한 번)하고 CompressFlow 로 만든다.
// 하나로 만들면 예전처럼 결과를 열어 보이고 안내(나눈 조각·건너뛴 파일·뺀 파일·시험·원본 지우기)를 한 창에, 각각 따로면 요약 창(W2-05 ShowSummary).
public partial class MainViewModel
{
    /// <summary>압축 대기 목록(<paramref name="sourcePaths"/>)으로 한 화면 압축 창을 띄우고 그 답대로 만든다.</summary>
    private async Task CompressStagedAsync(IReadOnlyList<string> sourcePaths)
    {
        var request = _ui.AskCompression(BuildCompressionDialogRequest(sourcePaths));
        if (request is null) return;
        RememberCompressSettings(request);

        var flow = new CompressFlow(_locator, _ui, _services.RecycleBin, _services.CompressTester, _services.Clock);
        var session = _ui.ShowProgress("압축 중...");
        CompressFlowResult result;
        try
        {
            result = await flow.RunAsync(request, session);
        }
        finally
        {
            session.Close();
        }

        if (request.EachSeparately) ShowEachSeparatelyResult(result);
        else await ShowSingleResultAsync(request, result);
    }

    /// <summary>
    /// 한 화면 압축 창에 넘길 것: 설정(compress.* — 사본이라 창이 고쳐도 [확인] 전에는 그대로), 쓸 수 있는 형식(7Z 는 7z.dll 을 쓸 수 있을 때만),
    /// 제안 위치(첫 원본이 있는 폴더 — 창은 설정 compress.folderChoice 가 '마지막으로 만든 곳'·'정해 둔 폴더'면 그 폴더로 연다)·
    /// 이름(원본 하나면 그 이름, 여럿이면 그 폴더 이름 — 예전 저장 창 제안과 같음).
    /// </summary>
    internal CompressionDialogRequest BuildCompressionDialogRequest(IReadOnlyList<string> sourcePaths)
    {
        var formats = new List<CompressionFormat> { CompressionFormat.Zip };
        if (SevenZipLibrary.IsAvailable) formats.Add(CompressionFormat.SevenZip);
        formats.AddRange(new[] { CompressionFormat.Tar, CompressionFormat.GZip, CompressionFormat.BZip2 });
        return new CompressionDialogRequest
        {
            Sources = sourcePaths,
            AllSourcesAreFiles = sourcePaths.All(File.Exists),
            SuggestedFolder = Path.GetDirectoryName(sourcePaths[0]) ?? Environment.CurrentDirectory,
            SuggestedBaseName = CompressNaming.SuggestBaseName(sourcePaths),
            AvailableFormats = formats,
            Defaults = Settings.Clone().Compress,
        };
    }

    /// <summary>
    /// 창의 답을 설정(compress.*)에 기억하고 한 번 저장한다(I2-12). 암호는 저장하지 않는다(설정에 그 칸이 없음).
    /// 그 형식에서 쓰지 않는 칸(TAR 의 나누기, GZ 의 빼기, 원본 하나의 각각 따로, 암호 없는 7z 의 이름 숨기기)은 기억한 값을 그대로 둔다.
    /// '원본을 휴지통으로'는 기억하지 않는다(W2-07 검증 지적 — 한 번 켰다고 다음 창부터 켜진 채로 열리지 않게. 처음 값은 ☰ ▸ 설정에서만).
    /// 만든 곳은 compress.lastFolder 로 기억한다(설정이 '마지막으로 만든 곳'이면 다음 창의 처음 값).
    /// </summary>
    private void RememberCompressSettings(CompressionRequest request)
    {
        var c = Settings.Compress;
        var o = request.Options;
        c.LastFormat = request.Format;
        c.Level = o.Level;
        if (request.Format == CompressionFormat.Zip) c.ZipEncryption = o.Encryption;
        if (request.Format == CompressionFormat.SevenZip)
        {
            c.Solid = o.Solid;
            if (o.HasPassword) c.EncryptFileNames = o.EncryptFileNames;
        }
        if (CompressionFormats.SupportsSplit(request.Format)) c.SplitBytes = o.SplitBytes;
        if (request.Sources.Count > 1) c.EachSeparately = request.EachSeparately;
        c.DateInName = request.DateInName;
        if (CompressionFormats.SupportsExclude(request.Format))
        {
            c.ExcludeEnabled = request.ExcludeEnabled;
            c.ExcludePatterns = request.ExcludePatterns.ToList();
        }
        c.TestAfter = request.TestAfter;
        if (!string.IsNullOrWhiteSpace(request.OutputFolder)) c.LastFolder = request.OutputFolder;
        _services.Settings.Save(Settings);
    }

    /// <summary>
    /// 하나로 만들었을 때(예전 흐름 그대로): 실패면 오류 창(하나도 못 담음은 무엇을 못 읽었는지까지), 취소면 조용히,
    /// 되면 결과를 열어 보이고(방금 넣은 암호로 — 파일 이름까지 숨긴 7z 도 다시 묻지 않음) 안내를 한 창에.
    /// </summary>
    private async Task ShowSingleResultAsync(CompressionRequest request, CompressFlowResult result)
    {
        var o = result.Outcomes.FirstOrDefault();
        if (o is null || !o.Succeeded)
        {
            if (result.Cancelled || o is null)
            {
                StatusText = "압축 취소됨";
                return;
            }
            _ui.ShowError(o.Error is NothingToCompressException none
                ? NothingCompressedMessage(none.Skipped)
                : $"압축 실패:\n\n{(o.Error is { } ex ? ErrorMessages.ToUserMessage(ex) : o.Reason)}");
            StatusText = "오류 발생";
            return;
        }

        // 건너뛴 파일이 있어도 성공이다 — 결과물은 지우지 않고, 연 뒤에 무엇이 빠졌는지 알린다(I0-2)
        // 나눠 만들었으면(W2-03) 실제로 생긴 조각 이름('이름 (2)' 로 바뀌었을 수도)으로 알리고 그 세트를 연다(ZIP 은 .zip, 7Z 는 .001).
        var r = o.Result!;
        string resultPath = o.ResultPath;
        StatusText = r.Volumes.Count > 0 ? $"압축 완료: {r.Volumes.Count}개 조각 — {resultPath}" : $"압축 완료: {o.Target}";

        // 결과 압축 파일을 자동으로 열어줌 (Staging 비워지고 Archive 모드로 전환).
        // 방금 넣은 암호를 넘긴다 — 파일 이름까지 암호화한 7z 도 다시 묻지 않고 열리고, 이어서 풀 때도 묻지 않는다(W2-02).
        await OpenArchiveAsync(resultPath, request.Options.HasPassword ? request.Options.Password : null);

        var notes = new List<string>();
        if (r.Volumes.Count > 0) notes.Add(SplitDoneMessage(r.Volumes, r.OpenPath));
        if (r.Skipped.Count > 0) notes.Add(SkippedMessage(r.Skipped));
        if (r.Excluded > 0) notes.Add(CompressFlow.ExcludedMessage(r.ExcludedPaths));
        notes.AddRange(result.Notes);
        if (notes.Count > 0)
            _ui.ShowInfo(string.Join("\n\n", notes));
        if (result.Warnings.Count > 0)
            _ui.ShowWarning(string.Join("\n\n", result.Warnings));
    }

    /// <summary>
    /// 각각 따로(I2-4)의 끝: 요약 창(만든 수·못 만든 것과 까닭·뺀 파일·시험·원본 지우기). 압축한 원본은 대기 목록에서 빼고(못 만든 것은 남겨 다시 할 수 있게),
    /// 다 비면 빈 화면으로.
    /// </summary>
    private void ShowEachSeparatelyResult(CompressFlowResult result)
    {
        int total = result.Outcomes.Count + result.NotStarted.Count;
        // 시험만 취소했으면 압축은 다 됐지만 그렇다고 알린다(W2-07 검증 지적 2차 — 예전엔 그냥 '압축 완료')
        StatusText = result.Cancelled
            ? "압축 취소됨"
            : result.SucceededCount == total ? (result.TestCancelled ? $"압축 완료: {total}개 (시험은 취소)" : $"압축 완료: {total}개") : "오류 발생";

        var done = result.Outcomes.Where(o => o.Succeeded).SelectMany(o => o.Sources).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (done.Count > 0 && IsStagingMode)
        {
            foreach (var e in Entries.Where(e => e.SourcePath is { } p && done.Contains(p)).ToList())
                Entries.Remove(e);
            RefreshVisibleEntries();
            OnPropertyChanged(nameof(WindowTitle));
        }

        _ui.ShowSummary(result.ToSummary());
    }
}
