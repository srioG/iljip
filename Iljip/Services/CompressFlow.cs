using System.IO;
using Iljip.Models;

namespace Iljip.Services;

/// <summary>방금 만든(또는 못 만든) 압축 하나 — 각각 따로(I2-4)면 원본마다 하나, 아니면 하나.</summary>
public sealed class CompressOutcome
{
    internal CompressOutcome(IReadOnlyList<string> sources, string target)
    {
        Sources = sources;
        Target = target;
    }

    /// <summary>이 압축에 넣은 원본.</summary>
    public IReadOnlyList<string> Sources { get; }

    /// <summary>만든(만들려던) 압축 파일 경로 — 같은 이름이 있어 '이름 (2)' 로 비켰으면 그 이름.</summary>
    public string Target { get; internal set; }

    /// <summary>원본 이름(요약 창 — 원본 하나면 그 이름, 여럿이면 압축 파일 이름).</summary>
    public string Name => Sources.Count == 1 ? DisplayName(Sources[0]) : Path.GetFileName(Target);

    /// <summary>만든 결과(못 만들었으면 null).</summary>
    public CompressResult? Result { get; internal set; }

    public bool Succeeded => Result is not null;

    /// <summary>못 만든 까닭(날 오류 — 로그·'자세히'). 같은 이름이라 건너뛰었으면 null.</summary>
    public Exception? Error { get; internal set; }

    /// <summary>못 만든 까닭(쉬운 한국어).</summary>
    public string? Reason { get; internal set; }

    /// <summary>시험했는가(통과든 아니든).</summary>
    public bool Tested { get; internal set; }

    /// <summary>시험에서 나온 문제(쉬운 한국어). 통과했거나 시험하지 않았으면 null.</summary>
    public string? TestProblem { get; internal set; }

    /// <summary>열 경로(나눠 만들었으면 여는 조각).</summary>
    public string ResultPath => Result?.OpenPath ?? Target;

    /// <summary>만든 파일 전부(나눴으면 조각 모두).</summary>
    public IReadOnlyList<string> OutputFiles => Result is { Volumes.Count: > 0 } r ? r.Volumes : new[] { Target };

    /// <summary>
    /// 압축하기 직전 원본 파일마다의 크기·수정 시각(원본 지우기를 켰을 때만 — W2-07 검증 지적). 지우기 전에 지금 원본과 맞춰 보아,
    /// 크기는 같고 내용만 바뀐 파일도 휴지통으로 보내지 않는다. 못 쟀으면 null(그 원본은 지우지 않음).
    /// </summary>
    internal IReadOnlyDictionary<string, SourceStamp>? SourceStamps { get; set; }

    internal static string DisplayName(string path)
    {
        string name = Path.GetFileName(path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        return string.IsNullOrEmpty(name) ? path : name;
    }
}

/// <summary>원본 파일 하나의 크기·수정 시각(UTC) — 바뀌었는지만 본다.</summary>
internal readonly record struct SourceStamp(long Length, DateTime LastWriteUtc);

/// <summary><see cref="CompressFlow.RunAsync"/> 의 결과.</summary>
public sealed class CompressFlowResult
{
    public IReadOnlyList<CompressOutcome> Outcomes { get; init; } = Array.Empty<CompressOutcome>();

    /// <summary>취소해서 손대지 않은 원본(각각 따로).</summary>
    public IReadOnlyList<string> NotStarted { get; init; } = Array.Empty<string>();

    /// <summary>압축(또는 같은 이름 창)을 취소했다.</summary>
    public bool Cancelled { get; init; }

    /// <summary>시험을 취소했다(만든 압축은 남고 원본은 지우지 않음).</summary>
    public bool TestCancelled { get; init; }

    /// <summary>알림(시험 통과·원본을 휴지통으로 옮김 등).</summary>
    public IReadOnlyList<string> Notes { get; init; } = Array.Empty<string>();

    /// <summary>경고(시험에서 문제·원본을 남긴 까닭·휴지통 실패).</summary>
    public IReadOnlyList<string> Warnings { get; init; } = Array.Empty<string>();

    /// <summary>휴지통으로 보낸 원본.</summary>
    public IReadOnlyList<string> Recycled { get; init; } = Array.Empty<string>();

    public int SucceededCount => Outcomes.Count(o => o.Succeeded);

    /// <summary>
    /// 각각 따로(I2-4)의 요약 창: '3개를 모두 압축했어요.'·못 만든 것과 까닭·만든 파일 이름·뺀 파일·건너뛴 파일·시험·원본 지우기 알림. 날 오류는 '자세히' 아래에만.
    /// </summary>
    public OperationSummary ToSummary()
    {
        var notes = new List<string>();
        var made = Outcomes.Where(o => o.Succeeded).Select(o => Path.GetFileName(o.ResultPath)).ToList();
        if (made.Count > 0)
            notes.Add("만든 파일: " + string.Join(", ", made.Take(10)) + (made.Count > 10 ? $" 외 {made.Count - 10}개" : string.Empty));
        var excluded = Outcomes.Where(o => o.Succeeded).SelectMany(o => o.Result!.ExcludedPaths).ToList();
        if (excluded.Count > 0) notes.Add(CompressFlow.ExcludedMessage(excluded));
        foreach (var o in Outcomes.Where(o => o.Succeeded && o.Result!.Skipped.Count > 0))
            notes.Add($"‘{Path.GetFileName(o.ResultPath)}’: 다른 프로그램이 쓰고 있는 파일 등 {o.Result!.Skipped.Count}개는 건너뛰었어요.");
        notes.AddRange(Notes);
        notes.AddRange(Warnings);
        return new OperationSummary
        {
            Title = "압축 결과",
            Kind = SummaryKind.Compress,
            Total = Outcomes.Count + NotStarted.Count,
            Succeeded = SucceededCount,
            Failures = Outcomes.Where(o => !o.Succeeded && o.Reason is not null)
                .Select(o => new OperationFailure(o.Name, o.Reason!, o.Error?.ToString()))
                .ToList(),
            NotStarted = NotStarted.Select(CompressOutcome.DisplayName).ToList(),
            Notes = notes,
            Cancelled = Cancelled,
        };
    }
}

/// <summary>
/// 한 화면 압축 창의 요청대로 압축한다(W2-07). 진행 창 하나에서:
/// <list type="number">
/// <item>만들기 — 각각 따로(I2-4)면 원본마다 '원본 이름(+ _날짜).확장자'를 차례로(진행 창 '2/5 · 이름'), 아니면 하나. 쓸모없는 파일 빼기(I3-6)는 엔진 옵션의 빼기 목록으로.
///   같은 이름이 있으면: 하나 만들기는 창이 [바꾸기]를 골랐을 때만 바꾸고 아니면 '이름 (2)', 각각 따로는 같은 이름 처리기(W2-05 — 기본 '이름 (2)').
///   단 만들 압축이 원본 그 자체('a.zip' 을 같은 곳에 a.zip 으로)면 무엇을 골랐든 '이름 (2)'(바꾸면 원본이 빈 압축으로 덮임 — W2-07 검증 지적).
///   실패·취소한 압축의 부분 파일은 지운다(시작 전 모습과 달라졌을 때만 — 덮어쓰려던 기존 파일을 열기도 전에 지우지 않게).</item>
/// <item>시험 — '다 만들고 시험하기' 또는 '원본을 휴지통으로'면 만든 압축마다 <see cref="ICreatedArchiveTester"/>(모든 항목을 풀어 보며 확인값까지).
///   진행 창에는 단계 알림(<see cref="ArchiveProgress.IsStage"/> — 솎아내기에 버려지지 않음)으로 '만든 압축 파일을 시험하는 중'.</item>
/// <item>원본 지우기(I3-4, 기본 꺼짐) — 시험을 통과한 압축의 원본만, 원본이 아직 압축 내용과 같은지(이름·크기가 압축 목록과, 크기·수정 시각이 압축하기 직전과)
///   다시 맞춰 보고, 묻고(기본 버튼 '그대로 두기'), 휴지통으로만(<see cref="IRecycleBin"/> — 영구 삭제는 절대 하지 않음). 네트워크 드라이브·USB(휴지통 없음),
///   압축 파일이 그 폴더 안에 있음, 폴더 안에 뺀 파일·못 넣은 파일이 있음, 압축과 맞지 않음(그새 바뀜)이면 그 원본은 남기고 까닭을 알린다. 모르면 지우지 않는다.
///   폴더와 그 안의 파일을 같이 골랐으면 폴더만 보낸다. 맞춰 보기·휴지통으로 옮기기는 작업 스레드에서 진행 창을 띄운 채로 한다(창이 굳지 않게 — W2-07 검증 지적).</item>
/// </list>
/// 창 없이 풀기(--extract-to)와 무관하고, 창 없이 바로 압축(W2-12 --compress-zip)에서는 원본 지우기를 쓰지 않는다.
/// </summary>
public sealed class CompressFlow
{
    private readonly ArchiveServiceLocator _locator;
    private readonly IUserInteraction _ui;
    private readonly IRecycleBin _recycleBin;
    private readonly ICreatedArchiveTester _tester;
    private readonly IClock _clock;
    private readonly ICollisionResolver _collisions;

    /// <param name="collisions">각각 따로에서 같은 이름의 압축 파일이 있을 때(null 이면 늘 '이름 (2)').</param>
    public CompressFlow(ArchiveServiceLocator locator, IUserInteraction ui, IRecycleBin recycleBin, ICreatedArchiveTester tester, IClock clock,
        ICollisionResolver? collisions = null)
    {
        _locator = locator;
        _ui = ui;
        _recycleBin = recycleBin;
        _tester = tester;
        _clock = clock;
        _collisions = collisions ?? new FixedResolver(CollisionChoice.Rename);
    }

    /// <summary>시험하는 동안의 진행 창 문구.</summary>
    internal const string StageTesting = "만든 압축 파일을 시험하는 중";

    /// <summary>원본 지우기를 켰을 때 압축하기 전에 원본 크기·수정 시각을 재는 동안의 진행 창 문구.</summary>
    internal const string StageMeasuring = "원본을 살펴보는 중";

    /// <summary>원본 지우기 전에 원본과 압축 내용을 맞춰 보는 동안의 진행 창 문구.</summary>
    internal const string StageChecking = "원본과 압축 내용을 맞춰 보는 중";

    /// <summary>원본을 휴지통으로 옮기는 동안의 진행 창 문구(진행 창 제목도 같은 뜻).</summary>
    internal const string StageRecycling = "원본을 휴지통으로 옮기는 중";

    /// <summary>맞춰 보기·옮기기를 취소해서 지우지 않았을 때.</summary>
    internal const string RecycleCancelledMessage = "취소해서 원본은 지우지 않았어요.";

    /// <summary>원본 지우기를 묻는 문구의 머리(뒤에 원본 이름 목록).</summary>
    internal static string RecycleQuestion(int count, long bytes) => $"원본 {count}개({FormatSize(bytes)})를 휴지통으로 옮길까요?";

    internal static readonly string[] RecycleChoices = { "옮기기", "그대로 두기" };

    /// <summary>시험에서 문제가 있어 원본을 남겼을 때.</summary>
    internal const string TestFailedKeptMessage = "시험에서 문제가 있어 원본을 지우지 않았어요";

    /// <summary>
    /// '다 만들고 시험하기'만 켠 채 시험을 취소했을 때(W2-07 검증 지적 2차 — 예전엔 아무 알림이 없었다). 만든 압축 파일은 그대로 있다.
    /// 예: '시험을 취소했어요(3개 중 1개만 시험했어요). 만든 압축 파일은 그대로 있어요.'
    /// </summary>
    internal static string TestCancelledMessage(int made, int tested) =>
        made > 1 ? $"시험을 취소했어요({made}개 중 {tested}개만 시험했어요). 만든 압축 파일은 그대로 있어요."
                 : "시험을 취소했어요. 만든 압축 파일은 그대로 있어요.";

    /// <summary>
    /// 요청대로 만들고(시험하고, 원본을 지우고) 결과를 돌려준다. <paramref name="session"/> 은 만들기·시험·원본 맞춰 보기 동안 쓰고, 원본 지우기를 묻기 전에 닫는다
    /// (부르는 쪽이 다시 닫아도 됨). [옮기기]를 고르면 휴지통으로 옮기는 동안 진행 창을 하나 더 띄운다.
    /// 창(묻기)은 부른 흐름(UI 스레드)에서만 띄운다 — 만들기·시험·원본 맞춰 보기·휴지통으로 옮기기는 작업 스레드.
    /// </summary>
    public async Task<CompressFlowResult> RunAsync(CompressionRequest request, IProgressSession session)
    {
        var ct = session.CancellationToken;
        var format = request.Format;
        IFileFilter? filter = request.ExcludeEnabled && request.ExcludePatterns.Count > 0 && CompressionFormats.SupportsExclude(format)
            ? new PatternFileFilter(request.ExcludePatterns)
            : null;
        var options = request.Options.WithExcludeFilter(filter);
        bool split = options.SplitBytes is > 0 && CompressionFormats.SupportsSplit(format);
        var localNow = _clock.UtcNow.ToLocalTime();
        bool deleteWanted = request.DeleteOriginalsAfter && CompressionFormats.SupportsDeleteOriginals(format);

        // 할 일: 각각 따로면 원본마다(이름은 원본에서), 아니면 하나(창의 이름)
        var jobs = request.EachSeparately
            ? request.Sources.Select(s => (Sources: (IReadOnlyList<string>)new[] { s }, Base: CompressNaming.BaseNameFor(s, format))).ToList()
            : new List<(IReadOnlyList<string> Sources, string Base)>
            {
                (request.Sources, string.IsNullOrWhiteSpace(request.BaseName) ? CompressNaming.SuggestBaseName(request.Sources, format) : request.BaseName.Trim()),
            };

        var outcomes = new List<CompressOutcome>();
        var notStarted = new List<string>();
        bool cancelled = false;
        for (int i = 0; i < jobs.Count; i++)
        {
            var (sources, baseName) = jobs[i];
            if (ct.IsCancellationRequested)
            {
                cancelled = true;
                notStarted.AddRange(jobs.Skip(i).SelectMany(j => j.Sources));
                break;
            }

            string stem = CompressNaming.WithDate(baseName, request.DateInName, localNow, format);
            string target = Path.Combine(request.OutputFolder, stem + CompressionFormats.ExtensionOf(format));
            var outcome = new CompressOutcome(sources, target);

            // 같은 이름(또는 같은 이름의 첫 조각)이 있으면
            if (CompressNaming.IsTaken(request.OutputFolder, stem, format, split))
            {
                // 만들 압축이 원본 그 자체면(‘a.zip’ 을 같은 곳에 a.zip 으로) 묻지도 바꾸지도 않고 비켜 만든다 —
                // 바꾸면 쓰는 쪽이 자기 자신을 원본에서 빼고 덮어써 원본이 빈 압축이 된다(W2-07 검증 지적, 예전 저장 창 흐름부터 있던 일)
                bool targetIsSource = sources.Any(s => SamePath(s, target));
                CollisionChoice choice = targetIsSource ? CollisionChoice.Rename
                    : request.EachSeparately ? _collisions.Resolve(Question(target, jobs.Count - i - 1))
                    : request.OverwriteExisting ? CollisionChoice.Overwrite : CollisionChoice.Rename;
                if (choice == CollisionChoice.Cancel)
                {
                    cancelled = true;
                    notStarted.AddRange(jobs.Skip(i).SelectMany(j => j.Sources));
                    break;
                }
                if (choice == CollisionChoice.Skip)
                {
                    outcome.Reason = $"같은 이름의 압축 파일(‘{Path.GetFileName(target)}’)이 있어 건너뛰었어요.";
                    outcomes.Add(outcome);
                    continue;
                }
                if (choice == CollisionChoice.Rename)
                    outcome.Target = target = CompressNaming.UniqueTarget(request.OutputFolder, stem, format, split);
            }
            outcomes.Add(outcome);

            var service = ServiceFor(format);
            IProgress<ArchiveProgress> progress = jobs.Count > 1
                ? new BatchProgress(session.Progress, i + 1, jobs.Count, Path.GetFileName(target))
                : session.Progress;
            // 실패 정리 때 '이번에 만들거나 고친 파일'만 지우려고 시작 전 모습을 적어 둔다 — 원본을 못 읽어(SourceUnreadable 등)
            // 압축 파일을 열기도 전에 끝났는데 덮어쓰려던 기존 파일을 지우는 일이 없게.
            var before = OutputStamp.Of(target);
            try
            {
                // 원본 지우기를 켰으면 압축하기 직전 원본의 크기·수정 시각을 적어 둔다(작업 스레드 — 파일이 많아도 창이 굳지 않게)
                if (deleteWanted)
                {
                    progress.Report(new ArchiveProgress { CurrentFile = StageMeasuring, IsStage = true });
                    var stampSources = sources;
                    outcome.SourceStamps = await Task.Run(() => TakeStamps(stampSources, filter, ct), ct);
                }
                outcome.Result = await service.CompressAsync(sources, target, options, progress, ct);
            }
            catch (OperationCanceledException)
            {
                DeletePartialOutput(target, before);
                outcome.Reason = "취소했어요.";
                cancelled = true;
                notStarted.AddRange(jobs.Skip(i + 1).SelectMany(j => j.Sources));
                break;
            }
            catch (Exception ex)
            {
                // 비취소 실패(I/O·디스크 가득·원본 변경 등)로도 부분·손상 압축이 남아 정상 파일로 오인될 수 있다 — 취소와 같이 정리
                DeletePartialOutput(target, before);
                Logger.LogError($"압축 실패: {target}", ex);
                outcome.Error = ex;
                outcome.Reason = ErrorMessages.ToPlainMessage(ex);
            }
        }

        // 시험: '다 만들고 시험하기' 또는 원본 지우기(시험을 통과해야 지움)
        bool testCancelled = false;
        if (!cancelled && (request.TestAfter || deleteWanted))
        {
            var made = outcomes.Where(o => o.Succeeded).ToList();
            for (int k = 0; k < made.Count; k++)
            {
                var o = made[k];
                // 단계 알림 — 압축 마지막 보고와 40ms 안에 붙어 와도 진행 창 앞의 솎아내기가 버리지 않는다(W2-07 검증 지적).
                // 크기를 모르므로 0(진행 창은 움직이는 막대). 여러 개면 '만든 압축 파일을 시험하는 중 (2/3) — 이름'.
                // 순번은 글자로만 넣는다 — BatchIndex/BatchCount 로 보내면 진행 창이 막대를 '앞서 끝난 수 / 전체'로 그려
                // 압축이 끝나 100% 이던 막대가 0%·33%·66% 로 되돌아가 처음부터 다시 하는 것처럼 보였다(W2-07 검증 지적 2차).
                string name = Path.GetFileName(o.ResultPath);
                session.Progress.Report(new ArchiveProgress
                {
                    CurrentFile = made.Count > 1 ? $"{StageTesting} ({k + 1}/{made.Count}) — {name}" : $"{StageTesting} — {name}",
                    IsStage = true,
                });
                try
                {
                    string path = o.ResultPath;
                    string? pw = options.Password;
                    await Task.Run(() => _tester.Test(path, pw, ct), ct);
                    o.Tested = true;
                }
                catch (OperationCanceledException)
                {
                    testCancelled = true;
                    break;
                }
                catch (Exception ex)
                {
                    Logger.LogError($"만든 압축 파일 시험에서 문제: {o.ResultPath}", ex);
                    o.Tested = true;
                    o.TestProblem = ErrorMessages.ToPlainMessage(ex);
                }
            }
        }

        // 원본 지우기 1단계: 지워도 되는 원본 고르기 — 진행 창을 띄운 채 작업 스레드에서(원본 폴더를 다시 훑고 크기를 재므로 파일이 많으면 오래 걸림)
        IReadOnlyList<RecycleCandidate>? candidates = null;
        var checkWarnings = new List<string>();
        bool checkCancelled = false;
        if (deleteWanted && !cancelled && !testCancelled)
        {
            session.Progress.Report(new ArchiveProgress { CurrentFile = StageChecking, IsStage = true });
            try
            {
                string? pw = options.Password;
                candidates = await Task.Run(() => FindDeletableAsync(outcomes, format, filter, pw, checkWarnings, ct), ct);
            }
            catch (OperationCanceledException)
            {
                checkCancelled = true;
            }
            catch (Exception ex)
            {
                // 무엇이든 실패하면 지우지 않은 것으로 끝낸다(모르면 지우지 않음)
                Logger.LogError("압축 후 원본 확인 중 오류", ex);
                checkWarnings.Add("원본을 휴지통으로 옮기지 못해 그대로 두었어요.");
            }
        }
        session.Close();   // 원본 지우기를 묻기 전에 진행 창을 닫는다

        var notes = new List<string>();
        var warnings = new List<string>();
        var tested = outcomes.Where(o => o.Tested).ToList();
        if (request.TestAfter && tested.Count > 0)
        {
            // 시험을 도중에 취소했으면 '모두 문제없어요'는 하지 않는다(시험하지 않은 것이 있음 — 아래 취소 알림)
            if (!testCancelled && tested.All(o => o.TestProblem is null))
                notes.Add(tested.Count == 1 ? "만든 압축 파일을 시험해 봤어요 — 문제없어요." : $"만든 압축 파일 {tested.Count}개를 시험해 봤어요 — 모두 문제없어요.");
            foreach (var o in tested.Where(o => o.TestProblem is not null && !deleteWanted))
                warnings.Add($"‘{Path.GetFileName(o.ResultPath)}’을(를) 시험했더니 문제가 있어요: {o.TestProblem} 다시 만들어 주세요.");
        }
        if (testCancelled && deleteWanted)
            warnings.Add("시험을 취소해서 원본은 지우지 않았어요.");
        else if (testCancelled)
            notes.Add(TestCancelledMessage(outcomes.Count(o => o.Succeeded), tested.Count));
        if (request.DeleteOriginalsAfter && !CompressionFormats.SupportsDeleteOriginals(format))
            warnings.Add("GZ·BZ2 는 원본을 지우지 않아요(압축 내용과 원본을 맞춰 볼 수 없어서). ZIP·7Z·TAR 로 만들면 지울 수 있어요.");
        warnings.AddRange(checkWarnings);
        if (checkCancelled) warnings.Add(RecycleCancelledMessage);

        // 원본 지우기 2단계: 묻고(UI 스레드) → 다시 맞춰 보고 휴지통으로(작업 스레드, 진행 창)
        var recycled = new List<string>();
        if (candidates is { Count: > 0 })
        {
            try
            {
                recycled.AddRange(await AskAndRecycleAsync(candidates, filter, notes, warnings));
            }
            catch (Exception ex)
            {
                // 무엇이든 실패하면 지우지 않은 것으로 끝낸다(모르면 지우지 않음)
                Logger.LogError("압축 후 원본 지우기 중 오류", ex);
                warnings.Add("원본을 휴지통으로 옮기지 못해 그대로 두었어요.");
            }
        }

        return new CompressFlowResult
        {
            Outcomes = outcomes,
            NotStarted = notStarted,
            Cancelled = cancelled,
            TestCancelled = testCancelled,
            Notes = notes,
            Warnings = warnings,
            Recycled = recycled,
        };
    }

    // ───────────── 원본 지우기(I3-4) ─────────────

    /// <summary>지워도 되는 원본 하나(크기, 맞춰 볼 압축 목록, 그 압축).</summary>
    internal sealed record RecycleCandidate(string Source, long Bytes, IReadOnlyDictionary<string, long> Listing, CompressOutcome Outcome);

    /// <summary>
    /// 1단계(작업 스레드 — 진행 창이 떠 있는 동안): 시험을 통과한 압축의 원본 중 지워도 되는 것만 고른다. 지우지 않는 원본은 까닭을 <paramref name="warnings"/> 에.
    /// 폴더와 그 안의 파일을 같이 골랐으면(대기 목록이 막지 않음) 폴더만 남긴다 — 크기를 두 번 세지 않고, 폴더를 옮긴 뒤 안의 파일을 못 찾아
    /// '못 옮겼어요'로 잘못 알리는 일이 없게(W2-07 검증 지적). 취소하면 <see cref="OperationCanceledException"/>.
    /// </summary>
    private async Task<IReadOnlyList<RecycleCandidate>> FindDeletableAsync(
        IReadOnlyList<CompressOutcome> outcomes, CompressionFormat format, IFileFilter? filter, string? password, List<string> warnings, CancellationToken ct)
    {
        var candidates = new List<RecycleCandidate>();
        foreach (var o in outcomes.Where(o => o.Succeeded))
        {
            ct.ThrowIfCancellationRequested();
            if (!o.Tested) continue;
            if (o.TestProblem is not null)
            {
                warnings.Add($"‘{Path.GetFileName(o.ResultPath)}’: {TestFailedKeptMessage}. ({o.TestProblem})");
                continue;
            }

            Dictionary<string, long> listing;
            try
            {
                var entries = await ServiceFor(format).ListEntriesAsync(o.ResultPath, password, ct);
                listing = new Dictionary<string, long>(StringComparer.Ordinal);
                foreach (var e in entries.Where(e => !e.IsDirectory))
                    listing.TryAdd(NormalizeEntry(e.Path), e.Size);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                Logger.LogError($"원본과 맞춰 보려고 목록을 읽지 못함: {o.ResultPath}", ex);
                warnings.Add($"‘{Path.GetFileName(o.ResultPath)}’의 목록을 읽지 못해 원본을 지우지 않았어요.");
                continue;
            }

            foreach (var src in o.Sources)
            {
                ct.ThrowIfCancellationRequested();
                if (CheckDeletable(src, o, filter, listing, ct, out long bytes) is { } why)
                {
                    if (why.Length > 0) warnings.Add(why);
                    continue;
                }
                candidates.Add(new RecycleCandidate(src, bytes, listing, o));
            }
        }

        // 같은 원본 두 번·다른 후보(폴더) 안의 후보는 뺀다 — 폴더를 옮기면 안의 것도 같이 간다
        var distinct = candidates
            .GroupBy(c => FullPathOrSelf(c.Source), StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .ToList();
        return distinct
            .Where(c => !distinct.Any(p => !ReferenceEquals(p, c) && IsUnder(c.Source, p.Source)))
            .ToList();
    }

    /// <summary>
    /// 2단계: 묻고(UI 스레드 — 기본 버튼 '그대로 두기') [옮기기]면 진행 창을 띄운 채 작업 스레드에서 한 번 더 맞춰 보고(묻는 동안 바뀌었을 수 있음) 휴지통으로 보낸다.
    /// 보낸 원본을 돌려준다. 지우지 않는 원본은 까닭을 <paramref name="warnings"/> 에, 보냈으면 '옮겼어요'를 <paramref name="notes"/> 에.
    /// 보내기 전에 진행 창 [취소]를 누르면 보내지 않는다(보내는 중에는 멈출 수 없음 — 휴지통에서 되살릴 수 있음).
    /// </summary>
    private async Task<IReadOnlyList<string>> AskAndRecycleAsync(
        IReadOnlyList<RecycleCandidate> candidates, IFileFilter? filter, List<string> notes, List<string> warnings)
    {
        const int MaxNames = 10;
        var lines = new List<string> { RecycleQuestion(candidates.Count, candidates.Sum(c => c.Bytes)) };
        foreach (var c in candidates.Take(MaxNames))
            lines.Add(Directory.Exists(c.Source) ? $"- {CompressOutcome.DisplayName(c.Source)} 폴더" : $"- {CompressOutcome.DisplayName(c.Source)}");
        if (candidates.Count > MaxNames) lines.Add($"외 {candidates.Count - MaxNames}개");
        lines.Add(string.Empty);
        lines.Add("시험을 통과한 압축 파일에 모두 들어 있어요. 휴지통에서 되살릴 수 있어요.");
        int answer = _ui.Choose(string.Join("\n", lines), RecycleChoices, defaultIndex: 1);
        if (answer != 0) return Array.Empty<string>();

        var session = _ui.ShowProgress(StageRecycling + "...");
        List<string> send;
        List<string> why;
        RecycleResult? result;
        try
        {
            var ct = session.CancellationToken;
            session.Progress.Report(new ArchiveProgress { CurrentFile = StageRecycling, IsStage = true });
            (send, why, result) = await Task.Run(() =>
            {
                var toSend = new List<string>();
                var reasons = new List<string>();
                foreach (var c in candidates)
                {
                    ct.ThrowIfCancellationRequested();
                    // 묻는 동안 바뀌었을 수 있다 — 보내기 직전에 한 번 더 맞춰 본다
                    if (CheckDeletable(c.Source, c.Outcome, filter, c.Listing, ct, out _) is { } reason)
                    {
                        if (reason.Length > 0) reasons.Add(reason);
                        continue;
                    }
                    toSend.Add(c.Source);
                }
                ct.ThrowIfCancellationRequested();
                RecycleResult? r = toSend.Count > 0 ? _recycleBin.Send(toSend) : null;
                return (toSend, reasons, r);
            });
        }
        catch (OperationCanceledException)
        {
            warnings.Add(RecycleCancelledMessage);
            return Array.Empty<string>();
        }
        finally
        {
            session.Close();
        }

        warnings.AddRange(why);
        if (result is null) return Array.Empty<string>();
        if (!result.Succeeded)
        {
            Logger.LogError("압축 후 원본을 휴지통으로 옮기지 못함", new IOException(result.Error ?? "휴지통 실패"));
            string detail = string.IsNullOrEmpty(result.Error) ? string.Empty : $" ({result.Error})";
            // 한 번에 보낸 것 중 일부만 옮겨졌을 수 있다(Windows 는 하나가 실패해도 앞의 것은 이미 옮김) — 없어진 것은 옮긴 것으로 알리고,
            // 남은 것만 '그대로 두었어요'(W2-07 검증 지적 2차: 예전엔 일부를 옮겼어도 '옮기지 못해 그대로 두었어요'만 알렸다)
            var moved = send.Where(p => !File.Exists(p) && !Directory.Exists(p)).ToList();
            if (moved.Count == 0)
            {
                warnings.Add("원본을 휴지통으로 옮기지 못해 그대로 두었어요." + detail);
                return Array.Empty<string>();
            }
            var left = send.Except(moved, StringComparer.OrdinalIgnoreCase).ToList();
            notes.Add($"원본 {moved.Count}개를 휴지통으로 옮겼어요.");
            if (left.Count > 0)
                warnings.Add($"원본 {left.Count}개는 휴지통으로 옮기지 못해 그대로 두었어요: "
                             + string.Join(", ", left.Take(MaxNames).Select(CompressOutcome.DisplayName))
                             + (left.Count > MaxNames ? $" 외 {left.Count - MaxNames}개" : string.Empty) + detail);
            return moved;
        }
        notes.Add($"원본 {send.Count}개를 휴지통으로 옮겼어요.");
        return send;
    }

    /// <summary>
    /// 원본 파일마다 크기·수정 시각(압축하기 직전 — 같은 빼기 목록으로 모은 것). 무엇이든 못 읽으면 null(그 원본은 지우지 않음 — 압축은 그대로 함).
    /// 훑는 동안 [취소]를 보며(폴더마다·파일마다 — W2-07 검증 지적 2차) 취소면 <see cref="OperationCanceledException"/>(삼키지 않음).
    /// </summary>
    internal static Dictionary<string, SourceStamp>? TakeStamps(IReadOnlyList<string> sources, IFileFilter? filter, CancellationToken ct)
    {
        try
        {
            var set = SourceCollector.Collect(sources, filter, ct);
            var stamps = new Dictionary<string, SourceStamp>(StringComparer.OrdinalIgnoreCase);
            foreach (var (file, _) in set.Files)
            {
                ct.ThrowIfCancellationRequested();
                var fi = new FileInfo(file);
                if (fi.Exists) stamps[file] = new SourceStamp(fi.Length, fi.LastWriteTimeUtc);
            }
            return stamps;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Logger.LogError("압축 전 원본 크기·시각을 재지 못함(원본 지우기는 하지 않음)", ex);
            return null;
        }
    }

    /// <summary>
    /// 이 원본을 지워도 되는가. 되면 null(<paramref name="bytes"/> = 원본 크기), 안 되면 까닭(빈 문자열이면 알리지 않음 — 이미 없어짐).
    /// 파일마다 이름·크기가 압축 목록과, 크기·수정 시각이 압축하기 직전(<see cref="CompressOutcome.SourceStamps"/>)과 같아야 한다. 모르면 지우지 않는다.
    /// 원본 폴더를 다 훑으므로 작업 스레드에서 부르고, 훑는 동안 [취소]를 본다(폴더마다·파일마다 — W2-07 검증 지적 2차, 취소면 <see cref="OperationCanceledException"/>).
    /// </summary>
    private string? CheckDeletable(string src, CompressOutcome o, IFileFilter? filter, IReadOnlyDictionary<string, long> listing, CancellationToken ct, out long bytes)
    {
        bytes = 0;
        string name = CompressOutcome.DisplayName(src);
        bool isDir = Directory.Exists(src);
        if (!isDir && !File.Exists(src)) return string.Empty;   // 이미 없음

        // 네트워크 드라이브·USB 등 휴지통이 없는 곳 — 지우면 영구 삭제가 된다
        if (IsOnDriveWithoutRecycleBin(src) || !_recycleBin.IsRecyclable(src))
            return $"‘{name}’은(는) 네트워크 드라이브나 USB 처럼 휴지통이 없는 곳에 있어 지우지 않았어요.";

        // 만든 압축 파일이 그 폴더 안에 있으면 폴더를 지우면 압축 파일까지 사라진다
        if (isDir && o.OutputFiles.Any(f => IsUnder(f, src)))
            return $"‘{name}’ 폴더 안에 압축 파일을 만들어서 폴더는 지우지 않았어요.";

        // 지금 원본을 다시 훑어 압축 내용과 맞춰 본다(같은 빼기 목록 — 압축할 때와 같은 이름 규칙)
        var set = SourceCollector.Collect(new[] { src }, filter, ct);
        if (set.Excluded.Count > 0)
            return $"‘{name}’ 폴더 안에 뺀 파일(쓸모없는 파일 {set.Excluded.Count}개)이 있어서 폴더는 지우지 않았어요.";
        if (set.Skipped.Count > 0 || o.Result!.Skipped.Any(s => IsSameOrUnder(s.Path, src)))
            return $"‘{name}’ 안에 압축에 넣지 못한 파일이 있어서 지우지 않았어요.";
        // 압축하기 직전의 크기·수정 시각도 맞춰 본다 — 크기는 같고 내용만 바뀐 파일(압축하는 동안·묻는 동안 고쳐 저장)을 걸러 낸다(W2-07 검증 지적)
        if (o.SourceStamps is not { } stamps) return $"‘{name}’을(를) 확인하지 못해 지우지 않았어요.";
        foreach (var (file, entryName) in set.Files)
        {
            ct.ThrowIfCancellationRequested();
            long length;
            DateTime written;
            try
            {
                var fi = new FileInfo(file);
                length = fi.Length;
                written = fi.LastWriteTimeUtc;
            }
            catch { return $"‘{name}’을(를) 확인하지 못해 지우지 않았어요."; }
            if (!listing.TryGetValue(NormalizeEntry(entryName), out long inArchive) || inArchive != length
                || !stamps.TryGetValue(file, out var before) || before.Length != length || before.LastWriteUtc != written)
                return $"‘{name}’이(가) 압축 파일 내용과 맞지 않아(압축하는 동안이나 그 뒤에 바뀌었을 수 있어요) 지우지 않았어요.";
            bytes += length;
        }
        if (set.Files.Count == 0 && !isDir)
            return $"‘{name}’을(를) 확인하지 못해 지우지 않았어요.";
        return null;
    }

    /// <summary>
    /// 휴지통이 없는 곳인가 — \\서버\… 공유 폴더, 연결한 네트워크 드라이브, USB 같은 이동식·CD, 알 수 없는 드라이브. 모르면 true
    /// (지우지 않음 — 휴지통이 없는 곳에서 지우면 영구 삭제가 되므로). <see cref="IRecycleBin.IsRecyclable"/> 과 별도로 한 번 더 본다(설계 W2-07 위험).
    /// </summary>
    internal static bool IsOnDriveWithoutRecycleBin(string path)
    {
        try
        {
            string full = Path.GetFullPath(path);
            if (full.StartsWith(@"\\", StringComparison.Ordinal)) return true;
            string? root = Path.GetPathRoot(full);
            if (string.IsNullOrEmpty(root)) return true;
            return new DriveInfo(root).DriveType is not DriveType.Fixed;
        }
        catch
        {
            return true;
        }
    }

    /// <summary>같은 파일·폴더 경로인가(전체 경로로, 대소문자·끝 '\' 무시). 경로가 이상하면 false.</summary>
    internal static bool SamePath(string a, string b)
    {
        try
        {
            return string.Equals(Path.GetFullPath(a).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                Path.GetFullPath(b).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar), StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    private static string FullPathOrSelf(string path)
    {
        try { return Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar); }
        catch { return path; }
    }

    private static bool IsUnder(string path, string folder)
    {
        try
        {
            string f = Path.GetFullPath(folder).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            return Path.GetFullPath(path).StartsWith(f, StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return true;   // 모르면 안에 있다고 본다(지우지 않음)
        }
    }

    private static bool IsSameOrUnder(string path, string src)
    {
        try
        {
            return string.Equals(Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar), Path.GetFullPath(src).TrimEnd(Path.DirectorySeparatorChar),
                       StringComparison.OrdinalIgnoreCase)
                   || IsUnder(path, src);
        }
        catch
        {
            return true;
        }
    }

    /// <summary>목록 이름 맞추기: '\'→'/', 앞뒤 '/' 뺌, NFC.</summary>
    private static string NormalizeEntry(string? name) =>
        (name ?? string.Empty).Replace('\\', '/').Trim('/').Normalize(System.Text.NormalizationForm.FormC);

    // ───────────── 도우미 ─────────────

    /// <summary>
    /// 만들 형식의 처리기 — 만들 파일 이름이 아니라 고른 형식으로 고른다(W2-07 검증 지적 2차). GZ·BZ2 는 원래 이름을 그대로 두므로
    /// 'backup.tar' 를 GZ 로 만들면 'backup.tar.gz' 인데, 이름으로 고르면 가장 긴 확장자 '.tar.gz'(압축된 TAR — 풀기만 됨) 처리기가 골라져
    /// '만들기는 아직 지원하지 않아요'로 실패했다(예전 저장 창은 'backup.gz' 를 제안해 없던 일).
    /// </summary>
    private IArchiveService ServiceFor(CompressionFormat format) =>
        _locator.ForExtension(CompressionFormats.ExtensionOf(format)) ?? _locator.GetDefaultForCompression();

    /// <summary>뺀 파일 안내(I3-6): '쓸모없는 파일 3개는 뺐어요: Thumbs.db, ~$보고서.xlsx, __MACOSX 폴더' + 끄는 방법.</summary>
    public static string ExcludedMessage(IReadOnlyList<string> excludedPaths)
    {
        const int MaxNames = 5;
        var names = excludedPaths.Take(MaxNames)
            .Select(p => Directory.Exists(p) ? $"{CompressOutcome.DisplayName(p)} 폴더" : CompressOutcome.DisplayName(p))
            .ToList();
        string list = string.Join(", ", names) + (excludedPaths.Count > MaxNames ? $" 외 {excludedPaths.Count - MaxNames}개" : string.Empty);
        return $"쓸모없는 파일 {excludedPaths.Count}개는 뺐어요: {list}\n(압축 창에서 ‘쓸모없는 파일 빼기’를 끄면 다 넣어요.)";
    }

    /// <summary>크기 글자(12.3MB).</summary>
    internal static string FormatSize(long bytes)
    {
        const double KB = 1024, MB = KB * 1024, GB = MB * 1024;
        if (bytes >= GB) return $"{bytes / GB:0.##}GB";
        if (bytes >= MB) return $"{bytes / MB:0.#}MB";
        if (bytes >= KB) return $"{bytes / KB:0.#}KB";
        return $"{bytes}바이트";
    }

    private static CollisionQuestion Question(string target, int remaining)
    {
        long size = -1;
        DateTime time = default;
        try
        {
            var fi = new FileInfo(target);
            if (fi.Exists)
            {
                size = fi.Length;
                time = fi.LastWriteTime;
            }
        }
        catch
        {
            // 모르면 '알 수 없음'
        }
        return new CollisionQuestion(target, size, time, -1, null, remaining);
    }

    /// <summary>압축 실패·취소 때 부분 산출물 지우기 — 시작 전과 달라졌을 때만(새로 생겼거나 고쳐 쓰였을 때).</summary>
    private static void DeletePartialOutput(string path, OutputStamp before)
    {
        if (OutputStamp.Of(path) == before) return;   // 손대지 않은 기존 파일(또는 처음부터 없음)
        try { File.Delete(path); } catch { /* 정리 실패는 무시 */ }
    }

    /// <summary>파일이 있는지·크기·수정 시각(바뀌었는지만 본다).</summary>
    private readonly record struct OutputStamp(bool Exists, long Length, DateTime LastWriteUtc)
    {
        public static OutputStamp Of(string path)
        {
            try
            {
                var fi = new FileInfo(path);
                return fi.Exists ? new OutputStamp(true, fi.Length, fi.LastWriteTimeUtc) : default;
            }
            catch
            {
                return default;
            }
        }
    }

    /// <summary>압축 하나의 진행률에 '몇 번째 · 이름'을 붙여 넘긴다(각각 따로 — 진행 창 '2/5 · 보고서.zip').</summary>
    private sealed class BatchProgress : IProgress<ArchiveProgress>
    {
        private readonly IProgress<ArchiveProgress> _inner;
        private readonly int _index;
        private readonly int _count;
        private readonly string _name;

        public BatchProgress(IProgress<ArchiveProgress> inner, int index, int count, string name)
        {
            _inner = inner;
            _index = index;
            _count = count;
            _name = name;
        }

        public void Report(ArchiveProgress value) => _inner.Report(new ArchiveProgress
        {
            CurrentFile = value.CurrentFile,
            IsStage = value.IsStage,
            BytesProcessed = value.BytesProcessed,
            TotalBytes = value.TotalBytes,
            FilesProcessed = value.FilesProcessed,
            TotalFiles = value.TotalFiles,
            BatchIndex = _index,
            BatchCount = _count,
            BatchName = _name,
        });
    }
}
