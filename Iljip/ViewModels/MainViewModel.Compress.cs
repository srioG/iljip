using System.IO;
using Iljip.Models;
using Iljip.Services;

namespace Iljip.ViewModels;

// 한 화면 압축 창과 그 뒤(W2-07 — I2-4 각각 따로·I3-4 압축 후 원본 지우기·I3-5 이름에 날짜·I3-6 쓸모없는 파일 빼기·I2-12 설정 기억).
// 창의 처음 값은 설정(compress.*), [확인]이면 그 값을 기억해 한 번 저장(DLP 연속 재기록 경합 교훈 — 압축마다 한 번)하고 CompressFlow 로 만든다.
// 하나로 만들면 예전처럼 결과를 열어 보이고 안내(나눈 조각·건너뛴 파일·뺀 파일·시험·원본 지우기)를 한 창에, 각각 따로면 요약 창(W2-05 ShowSummary).
public partial class MainViewModel
{
    /// <summary>압축 대기 목록의 원본 경로들(목록 차례대로).</summary>
    private List<string> StagedSources() =>
        Entries.Select(e => e.SourcePath).Where(p => !string.IsNullOrEmpty(p)).Select(p => p!).ToList();

    /// <summary>압축 대기 목록(<paramref name="sourcePaths"/>)으로 한 화면 압축 창을 띄우고 그 답대로 만든다.</summary>
    /// <param name="forcedFolder">
    /// 압축 창 '만들 곳'의 처음 값을 이 폴더로(W2-11 — '--compress-to': 관리자 권한으로 다시 시도·오른쪽 끌기). null 이면 설정(compress.folderChoice)대로.
    /// </param>
    private async Task CompressStagedAsync(IReadOnlyList<string> sourcePaths, string? forcedFolder = null)
    {
        var request = _ui.AskCompression(BuildCompressionDialogRequest(sourcePaths, forcedFolder));
        if (request is null) return;
        bool skipMailWarningNextTime = false;
        if (request.Format == CompressionFormat.Sfx)
        {
            // W2-10: 회사 메일은 exe 첨부를 막는 경우가 많다 — 만들기 직전에 한 번 묻는다([ZIP으로 만들기]면 ZIP 으로, [취소]면 아무것도 안 함)
            request = ConfirmSfxMailWarning(request, out skipMailWarningNextTime);
            if (request is null)
            {
                StatusText = "압축 취소됨";
                return;
            }
        }

        var flow = new CompressFlow(_locator, _ui, _services.RecycleBin, _services.CompressTester, _services.Clock);
        var session = _ui.ShowProgress("압축 중...");
        CompressFlowResult result;
        try
        {
            if (request.Format == CompressionFormat.Sfx)
            {
                // W2-10 검증 지적 2차: 원본이 4GB 를 넘으면 exe 가 4GB 를 넘어 Windows 가 실행하지 않을 수 있다 — 만들기 전에 묻는다(ZIP 권함)
                request = await ConfirmSfxSizeAsync(request, session);
                if (request is null)
                {
                    StatusText = "압축 취소됨";
                    return;
                }
            }
            // '다시 묻지 않기'는 아무것도 취소하지 않고 만들기로 갈 때만 기억한다(저장은 압축 설정과 한 번)
            RememberCompressSettings(request, skipMailWarningNextTime);
            result = await flow.RunAsync(request, session);
        }
        finally
        {
            session.Close();
        }

        // 자동 풀림 exe 가 4GB 를 넘어 못 만들었으면(하나로 만들 때 — 만들기 전 어림보다 덜 줄었음) 그 까닭과 함께 ZIP 으로 다시 만들지 묻는다
        if (!request.EachSeparately && result.Outcomes.FirstOrDefault()?.Error is SfxTooLargeException tooLarge)
        {
            var zipRequest = AskZipAfterTooLarge(request, tooLarge);
            if (zipRequest is null)
            {
                StatusText = "오류 발생";
                return;
            }
            request = zipRequest;
            RememberCompressSettings(request);
            var again = _ui.ShowProgress("압축 중...");
            try
            {
                result = await flow.RunAsync(request, again);
            }
            finally
            {
                again.Close();
            }
        }

        if (request.EachSeparately) ShowEachSeparatelyResult(request, result);
        else await ShowSingleResultAsync(request, result);
    }

    /// <summary>
    /// 압축을 관리자 권한으로 다시 할 명령(W2-11, I3-16): '--compress-to 만들폴더 원본…' — 관리자 일집에서 같은 원본·같은 만들 곳으로 압축 창이 다시 뜬다
    /// (암호·옵션은 창에서 다시 — 암호는 인자에 넣지 않음).
    /// </summary>
    private static ParsedCommand? CompressRetryFor(CompressionRequest request, IReadOnlyList<string> sources) =>
        sources.Count == 0 || string.IsNullOrWhiteSpace(request.OutputFolder)
            ? null
            : new ParsedCommand(CommandVerb.CompressTo, null, request.OutputFolder, sources.ToList());

    /// <summary>
    /// 한 화면 압축 창에 넘길 것: 설정(compress.* — 사본이라 창이 고쳐도 [확인] 전에는 그대로), 쓸 수 있는 형식(7Z 는 7z.dll 을 쓸 수 있을 때만),
    /// 제안 위치(첫 원본이 있는 폴더 — 창은 설정 compress.folderChoice 가 '마지막으로 만든 곳'·'정해 둔 폴더'면 그 폴더로 연다)·
    /// 이름(원본 하나면 그 이름, 여럿이면 그 폴더 이름 — 예전 저장 창 제안과 같음).
    /// </summary>
    internal CompressionDialogRequest BuildCompressionDialogRequest(IReadOnlyList<string> sourcePaths, string? forcedFolder = null)
    {
        var formats = new List<CompressionFormat> { CompressionFormat.Zip };
        if (SevenZipLibrary.IsAvailable) formats.Add(CompressionFormat.SevenZip);
        formats.AddRange(new[] { CompressionFormat.Tar, CompressionFormat.GZip, CompressionFormat.BZip2 });
        // W2-10(I3-1): 자동 풀림 EXE — 스텁(실행 파일 옆 sfx\Iljip.SfxStub.exe)이 있을 때만(설치가 덜 됐거나 백신이 지웠으면 형식에 없음)
        if (SfxBuilder.IsAvailable) formats.Add(CompressionFormat.Sfx);
        var defaults = Settings.Clone().Compress;
        // W2-11: 만들 곳을 정해서 왔으면(--compress-to) 창은 설정의 '처음 값 고르기' 대신 그 폴더로 연다(사본만 바꿈 — 설정은 그대로)
        if (!string.IsNullOrWhiteSpace(forcedFolder)) defaults.FolderChoice = CompressFolderChoice.SourceFolder;
        return new CompressionDialogRequest
        {
            Sources = sourcePaths,
            AllSourcesAreFiles = sourcePaths.All(File.Exists),
            SuggestedFolder = !string.IsNullOrWhiteSpace(forcedFolder) ? forcedFolder : Path.GetDirectoryName(sourcePaths[0]) ?? Environment.CurrentDirectory,
            SuggestedBaseName = CompressNaming.SuggestBaseName(sourcePaths),
            AvailableFormats = formats,
            Defaults = defaults,
        };
    }

    /// <summary>
    /// 창의 답을 설정(compress.*)에 기억하고 한 번 저장한다(I2-12). 암호는 저장하지 않는다(설정에 그 칸이 없음).
    /// 그 형식에서 쓰지 않는 칸(TAR 의 나누기, GZ 의 빼기, 원본 하나의 각각 따로, 암호 없는 7z 의 이름 숨기기)은 기억한 값을 그대로 둔다.
    /// '원본을 휴지통으로'는 기억하지 않는다(W2-07 검증 지적 — 한 번 켰다고 다음 창부터 켜진 채로 열리지 않게. 처음 값은 ☰ ▸ 설정에서만).
    /// 만든 곳은 compress.lastFolder 로 기억한다(설정이 '마지막으로 만든 곳'이면 다음 창의 처음 값).
    /// 저장은 <see cref="SaveSettings"/> — 최신 설정에 이 칸들만 얹는다(다른 창이 바꾼 설정 창의 값 등은 그대로 — W2-11 검증 지적).
    /// </summary>
    /// <param name="skipMailWarning">자동 풀림 exe 안내의 '다시 묻지 않기'를 골랐는가(sfx.skipMailWarning 을 켬 — 같은 저장에 함께).</param>
    private void RememberCompressSettings(CompressionRequest request, bool skipMailWarning = false)
    {
        void Change(AppSettings s)
        {
            RememberCompressChoices(s.Compress, request);
            if (skipMailWarning) s.Sfx.SkipMailWarning = true;
        }

        Change(Settings);
        SaveSettings(Change);
    }

    /// <summary>창의 답을 압축 설정 칸에 옮긴다(<see cref="RememberCompressSettings"/> — 이 창의 설정과 저장 직전 최신 설정에 똑같이).</summary>
    private static void RememberCompressChoices(CompressSettings c, CompressionRequest request)
    {
        var o = request.Options;
        // 자동 풀림 EXE 는 마지막 형식으로 기억하지 않는다(W2-10) — 다음 압축 창이 exe 로 열려 메일로 보내다 막히는 일이 없게(그 전 형식 그대로)
        if (request.Format != CompressionFormat.Sfx) c.LastFormat = request.Format;
        c.Level = o.Level;
        if (request.Format is CompressionFormat.Zip or CompressionFormat.Sfx) c.ZipEncryption = o.Encryption;
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
    }

    // ───── W2-10(I3-1): 자동 풀림 exe 를 만들기 전 안내 ─────

    /// <summary>
    /// 만들기 직전 묻는 문구(압축 창의 형식 칸 아래에도 같은 첫 문장이 늘 보임).
    /// 받는 PC 의 반응은 단정하지 않는다(검증 지적 — Smart App Control·회사 정책이 걸린 PC 는 [실행] 선택지 자체가 없음).
    /// </summary>
    internal const string SfxMailQuestion =
        "회사 메일은 실행 파일(.exe)을 막는 경우가 많아요. 받는 사람이 못 받을 수 있어요. 메일로 보낼 거면 ZIP을 권해요.\n\n" +
        "자동 풀림 exe 는 USB·공유 폴더로 건넬 때 좋아요. 받는 PC 에 따라 'Windows의 PC 보호' 창이나 백신 경고가 뜰 수 있어요. " +
        "[추가 정보] → [실행]이 보이면 눌러서 풀면 되고, 실행이 아예 막히는 PC(회사 보안 설정 등)도 있으니 그때는 ZIP으로 보내 주세요.";

    /// <summary>버튼: 0 그래도 만들기, 1 ZIP으로 만들기, 2 취소.</summary>
    internal static readonly string[] SfxMailChoices = { "그래도 만들기", "ZIP으로 만들기", "취소" };

    internal const string SfxMailCheckText = "다시 묻지 않기";

    /// <summary>
    /// [ZIP으로 만들기] + AES-256 + 한글 암호(CP949 로 바꿀 수 있음)일 때 묻는 문구 — 압축 창이 ZIP 에 하는 한글 암호 경고(B3)와 같은 내용.
    /// 버튼: <see cref="SfxZipKoreanAesChoices"/>.
    /// </summary>
    internal const string SfxZipKoreanAesQuestion =
        "ZIP으로 만들면 한글 암호가 다른 프로그램에서 안 풀릴 수 있어요 — 영문·숫자를 권장해요.\n\n" +
        "AES-256 방식은 한글 암호를 반디집·알집과 다르게 저장해서, 받는 사람이 반디집·알집으로는 못 풀 수 있어요. " +
        "ZipCrypto 방식으로 만들면 반디집·알집에서도 풀려요(보안은 약해요).";

    /// <summary>버튼: 0 ZipCrypto 로(권장·기본), 1 AES-256 그대로, 2 취소.</summary>
    internal static readonly string[] SfxZipKoreanAesChoices =
    {
        "ZipCrypto 방식으로 만들기 (반디집·알집과 호환, 보안은 약해요)",
        "AES-256 그대로 만들기 (일집에서는 풀려요)",
        "취소 (암호를 영문·숫자로 다시 정하기)",
    };

    /// <summary>[ZIP으로 만들기] + CP949 로 못 바꾸는 글자(이모지 등)가 든 암호일 때 묻는 문구(어느 방식으로도 반디집·알집과 맞출 수 없음).</summary>
    internal const string SfxZipUnreadablePasswordQuestion =
        "암호에 다른 프로그램이 못 읽는 글자(이모지 등)가 있어요. ZIP으로 만들면 받는 사람이 반디집·알집으로 못 풀 수 있어요 — 영문·숫자를 권장해요.";

    /// <summary>버튼: 0 취소(기본), 1 그대로 만들기.</summary>
    internal static readonly string[] SfxZipUnreadablePasswordChoices =
    {
        "취소 (암호 다시 정하기)",
        "그대로 만들기 (일집에서는 풀려요)",
    };

    /// <summary>
    /// 자동 풀림 exe 를 만들기 직전 '회사 메일은 exe 를 막아요'를 묻는다(설정 sfx.skipMailWarning 이 켜져 있으면 묻지 않음).
    /// [그래도 만들기] → 그대로, [ZIP으로 만들기] → 같은 이름·같은 옵션의 ZIP(<see cref="ToZipRequest"/>), [취소]·창 닫기 → null.
    /// '다시 묻지 않기'는 [그래도 만들기]를 골랐을 때만 기억한다(<paramref name="skipNextTime"/> — 부르는 쪽이 끝까지 취소 없이 만들기로 갈 때 설정에 넣고
    /// <see cref="RememberCompressSettings"/> 가 한 번 저장). [ZIP으로 만들기]와 함께 켠 것은 기억하지 않는다 — 예전엔 기억해서 다음에 자동 풀림을 고르면
    /// 묻지 않고 exe 가 만들어졌다(마지막으로 'exe 말고 ZIP' 을 고른 사람의 뜻과 반대 — W2-10 검증 지적 2차).
    /// [ZIP으로 만들기]면 보통 ZIP 이 되므로 압축 창이 ZIP 에 하는 한글 암호 호환 확인을 여기서 한다(<see cref="ConfirmZipPasswordCompat"/> —
    /// 검증 지적: exe 는 스텁이 UTF-8·CP949 를 둘 다 맞춰 봐 압축 창이 묻지 않았는데, 그대로 ZIP 으로 바꾸면 반디집·알집이 못 여는 AES 한글 암호 ZIP 이 조용히 생겼음).
    /// 거기서 [취소]하면 아무것도 만들지 않는다.
    /// </summary>
    private CompressionRequest? ConfirmSfxMailWarning(CompressionRequest request, out bool skipNextTime)
    {
        skipNextTime = false;
        if (Settings.Sfx.SkipMailWarning) return request;
        var answer = _ui.ChooseWithCheck(SfxMailQuestion, SfxMailChoices, SfxMailCheckText, defaultIndex: 0);
        if (answer.Index is not (0 or 1)) return null;
        if (answer.Index == 0)
        {
            skipNextTime = answer.Checked;
            return request;
        }
        var zipOptions = ConfirmZipPasswordCompat(request.Options);
        return zipOptions is null ? null : ToZipRequest(request, zipOptions);
    }

    /// <summary>
    /// 자동 풀림 대신 만들 보통 ZIP 요청: 같은 원본·곳·이름·옵션(<paramref name="zipOptions"/> — 한글 암호 확인을 거친 것). 같은 이름 ZIP 이 있으면 '이름 (2).zip'
    /// (압축 창의 [바꾸기]는 exe 이름에 대한 답이었으므로 바꾸지 않음).
    /// </summary>
    private static CompressionRequest ToZipRequest(CompressionRequest request, CompressionOptions zipOptions) => new()
    {
        Sources = request.Sources,
        OutputFolder = request.OutputFolder,
        BaseName = request.BaseName,
        Format = CompressionFormat.Zip,
        Options = zipOptions,
        EachSeparately = request.EachSeparately,
        DateInName = request.DateInName,
        ExcludeEnabled = request.ExcludeEnabled,
        ExcludePatterns = request.ExcludePatterns,
        TestAfter = request.TestAfter,
        DeleteOriginalsAfter = request.DeleteOriginalsAfter,
        OverwriteExisting = false,
    };

    // ───── W2-10 검증 지적 2차: 자동 풀림 exe 는 4GB 를 넘으면 Windows 가 실행하지 않는다 ─────

    /// <summary>
    /// 만들기 전 어림 질문: 원본(압축하기 전 크기 합)이 4GB 를 넘을 때. 예: '원본이 모두 6.2GB 예요. …'.
    /// <paramref name="over"/> 는 4GB 를 넘는 묶음 수(각각 따로면 원본마다 — 하나로 만들면 1), <paramref name="largest"/> 는 그중 가장 큰 크기.
    /// </summary>
    internal static string SfxTooLargeQuestion(bool eachSeparately, int over, long largest) =>
        (eachSeparately
            ? $"원본 중 {over}개가 4GB 를 넘어요(가장 큰 것 {CompressFlow.FormatSize(largest)}). "
            : $"원본이 모두 {CompressFlow.FormatSize(largest)} 예요. ") +
        "자동 풀림 exe 는 4GB 를 넘으면 받는 사람 PC 에서 실행되지 않아요(Windows 제한).\n\n" +
        "압축해서 4GB 아래로 줄면 만들어지지만, 사진·영상처럼 이미 압축된 파일은 거의 줄지 않아요(넘으면 만들지 않고 알려 드려요). " +
        "ZIP으로 만들기를 권해요 — ZIP 은 크기 제한이 없고, 나눠 만들 수도 있어요.";

    /// <summary>버튼: 0 ZIP으로 만들기(권장·기본), 1 그래도 자동 풀림으로 해 보기, 2 취소.</summary>
    internal static readonly string[] SfxTooLargeChoices =
    {
        "ZIP으로 만들기 (권장)",
        "그래도 자동 풀림 exe 로 해 보기 (4GB 가 넘으면 만들지 않아요)",
        "취소",
    };

    /// <summary>만들어 보니 4GB 를 넘어 못 만들었을 때 덧붙이는 질문(앞에는 <see cref="SfxTooLargeException"/> 의 문구).</summary>
    internal const string SfxTooLargeRetryQuestion = "같은 내용을 ZIP으로 만들까요?";

    /// <summary>버튼: 0 ZIP으로 만들기(기본), 1 닫기.</summary>
    internal static readonly string[] SfxTooLargeRetryChoices = { "ZIP으로 만들기", "닫기" };

    /// <summary>
    /// 자동 풀림을 만들기 전에(진행 창을 띄운 채 — 작업 스레드에서) 원본 크기 합을 잰다(빼기 목록 적용, 각각 따로면 원본마다). 4GB 를 넘는 것이 있으면 묻는다:
    /// [ZIP으로 만들기](기본 — 한글 암호 확인을 거친 같은 옵션의 ZIP) · [그래도 해 보기](그대로 — 만들기 뒤에 넘으면 <see cref="SfxTooLargeException"/>) · [취소]·창 닫기 → null.
    /// 재는 동안 진행 창 [취소]면 null. 재지 못하면(드묾) 묻지 않고 그대로(만들기 뒤 확인이 지킴).
    /// </summary>
    private async Task<CompressionRequest?> ConfirmSfxSizeAsync(CompressionRequest request, IProgressSession session)
    {
        var ct = session.CancellationToken;
        session.Progress.Report(new ArchiveProgress { CurrentFile = SfxBuilder.StageMeasuring, IsStage = true });
        IFileFilter? filter = request.ExcludeEnabled && request.ExcludePatterns.Count > 0 ? new PatternFileFilter(request.ExcludePatterns) : null;
        var groups = request.EachSeparately
            ? request.Sources.Select(s => (IReadOnlyList<string>)new[] { s }).ToList()
            : new List<IReadOnlyList<string>> { request.Sources };
        List<long> sizes;
        try
        {
            sizes = await Task.Run(() => groups.Select(g => SfxBuilder.SourceBytes(g, filter, ct)).ToList(), ct);
        }
        catch (OperationCanceledException)
        {
            return null;
        }
        catch (Exception ex)
        {
            Logger.LogError("자동 풀림을 만들기 전 원본 크기를 재지 못함(만들기 뒤에 확인)", ex);
            return request;
        }

        var over = sizes.Where(SfxBuilder.MayExceedLimit).ToList();
        if (over.Count == 0) return request;
        switch (_ui.Choose(SfxTooLargeQuestion(request.EachSeparately, over.Count, over.Max()), SfxTooLargeChoices, defaultIndex: 0))
        {
            case 0:
                var zipOptions = ConfirmZipPasswordCompat(request.Options);
                return zipOptions is null ? null : ToZipRequest(request, zipOptions);
            case 1:
                return request;
            default:
                return null;
        }
    }

    /// <summary>
    /// 만들어 보니 exe 가 4GB 를 넘어 못 만들었을 때(<see cref="SfxTooLargeException"/> — 아무것도 남지 않음): 그 까닭과 함께 ZIP 으로 만들지 묻는다.
    /// [ZIP으로 만들기](기본)면 한글 암호 확인을 거친 ZIP 요청, [닫기]·창 닫기·한글 암호 질문에서 취소면 null.
    /// </summary>
    private CompressionRequest? AskZipAfterTooLarge(CompressionRequest request, SfxTooLargeException tooLarge)
    {
        if (_ui.Choose(tooLarge.Message + "\n\n" + SfxTooLargeRetryQuestion, SfxTooLargeRetryChoices, defaultIndex: 0) != 0) return null;
        var zipOptions = ConfirmZipPasswordCompat(request.Options);
        return zipOptions is null ? null : ToZipRequest(request, zipOptions);
    }

    /// <summary>
    /// 자동 풀림 대신 보통 ZIP 으로 만들 때의 한글 암호 호환 확인(<see cref="PasswordPolicy.CheckCompat"/> — 압축 창 B3 와 같은 판정).
    /// AES-256 + 한글 암호 → [ZipCrypto 로 만들기](옵션 사본, 방식만 바꿈)·[AES-256 그대로]·[취소], CP949 로 못 바꾸는 글자 → [취소]·[그대로],
    /// 그 밖(암호 없음·영문 암호·ZipCrypto 한글 암호 — CP949 로 써서 반디집과 맞음) → 묻지 않고 그대로. 취소·창 닫기 → null.
    /// </summary>
    private CompressionOptions? ConfirmZipPasswordCompat(CompressionOptions o)
    {
        if (!o.HasPassword) return o;
        switch (PasswordPolicy.CheckCompat(o.Password, ".zip", o.Encryption))
        {
            case PasswordCompat.NonAsciiAes:
                return _ui.Choose(SfxZipKoreanAesQuestion, SfxZipKoreanAesChoices, defaultIndex: 0) switch
                {
                    0 => new CompressionOptions
                    {
                        Level = o.Level,
                        Password = o.Password,
                        Encryption = ZipEncryption.ZipCrypto,
                        Solid = o.Solid,
                        EncryptFileNames = o.EncryptFileNames,
                        SplitBytes = o.SplitBytes,
                        ExcludeFilter = o.ExcludeFilter,
                    },
                    1 => o,
                    _ => null,
                };
            case PasswordCompat.NotCp949Encodable:
                return _ui.Choose(SfxZipUnreadablePasswordQuestion, SfxZipUnreadablePasswordChoices, defaultIndex: 0) == 1 ? o : null;
            default:
                return o;
        }
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
            // 하나도 못 담음은 무엇을 못 읽었는지까지(쉬운 한국어로 만든 글), 그 밖은 문제 창(W2-08 — 원문은 '자세히')
            if (o.Error is NothingToCompressException none) _ui.ShowError(NothingCompressedMessage(none.Skipped));
            else if (o.Error is { } ex)
            {
                // W2-11(I3-16): 만들 곳에 쓸 권한이 없으면(읽기 전용·Program Files 등) '관리자 권한으로 다시 시도' — 같은 원본·같은 만들 곳으로 압축 창부터('--compress-to')
                // 엔진 오류에 풀기 문구('…다시 받아서 풀어 보세요')가 나오지 않게(W2-08 검증 지적 2차)
                var problem = ErrorCatalog.DescribeCompressFailure(ex);
                if (problem.Kind != ProblemKind.Cancelled
                    && !OfferElevatedRetry(ex, $"압축하지 못했어요.\n\n{problem.Message}", CompressRetryFor(request, request.Sources), null, request.OutputFolder))
                    _ui.ShowProblem(problem with { Heading = "압축하지 못했어요." });
            }
            else _ui.ShowError($"압축하지 못했어요.\n\n{o.Reason}");
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
    private void ShowEachSeparatelyResult(CompressionRequest request, CompressFlowResult result)
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

        // W2-11(I3-16): 만들 곳에 쓸 권한이 없어 못 만든 것이 있으면 요약 뒤에 한 번 묻는다(그 원본들만 '--compress-to')
        var denied = result.Outcomes.Where(o => !o.Succeeded && ElevationService.IsAccessDenied(o.Error)).ToList();
        if (denied.Count > 0)
            OfferElevatedRetry(denied[0].Error!, $"압축 파일 {denied.Count}개는 만들 곳에 쓸 권한이 없어 만들지 못했어요.",
                CompressRetryFor(request, denied.SelectMany(o => o.Sources).ToList()), null, request.OutputFolder);
    }
}
