using System.IO;
using Iljip.Models;
using Iljip.Services;

namespace Iljip.ViewModels;

// 명령줄(셸 확장 우클릭·두 번째 실행)·여러 개 풀기 — W2-05 에서 MainViewModel.cs 의 ProcessCommandLineAsync 를 옮겨 오고
// 우클릭 풀기를 메인 창 없이(I1-6)·여러 개 풀기(I2-6)를 더했다. 묶음마다 partial 로 나눠 충돌을 줄인다(공통 설계 §0).
public partial class MainViewModel
{
    /// <summary>
    /// 명령줄 인자를 받아 적절한 동작 수행(메인 창이 떠 있는 흐름). 해석은 <see cref="CommandLineParser"/>(순수 함수).
    /// 인자 형식:
    ///   --open &lt;archive&gt;               압축 파일을 열기만 함
    ///   --extract &lt;archive…&gt;           폴더 선택 다이얼로그로 압축 해제(여러 개면 창 한 번 + 고른 폴더 안 '이름' 폴더마다 — W2-05)
    ///   --extract-here &lt;archive…&gt;      압축 파일이 있는 폴더에 바로 해제(반디집 '여기에 풀기')
    ///   --extract-to-folder &lt;archive…&gt; '부모\이름' 폴더에 해제. 그 이름이 이미 있으면 '이름 (2)' …(합치지 않음)
    ///   --extract-to &lt;archive&gt; &lt;dst&gt;   지정 폴더로 해제
    ///   --compress &lt;p1&gt; &lt;p2&gt; ...       인자들을 Staging에 추가 (압축 다이얼로그 자동 진행)
    ///   --compress-to &lt;dir&gt; &lt;p…&gt;       '--compress' + 압축 창의 '만들 곳'을 그 폴더로(W2-11 — 관리자 권한으로 다시 시도)
    ///   --extract-to-folder-in &lt;dir&gt; &lt;archive…&gt; 그 폴더 안 '이름' 폴더마다 풀기(W2-11)
    ///   --extract-into &lt;dir&gt; &lt;archive…&gt; 그 폴더에 바로 모두 풀기(W2-11 검증 지적 — 관리자 권한으로 다시 시도)
    ///   &lt;path&gt;...                       경로만 주어지면 드롭과 동일한 동작
    /// 맨 앞 '--new-window'·'--elevated' 표시는 해석기가 떼어 낸다(W2-11 — 단일 인스턴스 건너뛰기는 App 몫).
    /// (W1-D 전에는 '--extract-here' 가 '이름' 폴더에 풀었다 — 그 동작은 '--extract-to-folder' 로 옮김)
    /// 탐색기 우클릭 풀기(메인 창 없이 진행 창만 — I1-6)는 App 이 <see cref="ProcessShellCommandAsync"/> 로 부른다.
    /// 풀기 동사에 압축이 여러 개면 여러 개 풀기(<see cref="BatchExtractor"/> — 진행 창 하나·암호 한 번·끝에 요약, I2-6).
    /// </summary>
    public async Task ProcessCommandLineAsync(string[] args)
    {
        var cmd = CommandLineParser.Parse(args);
        if (cmd.Verb == CommandVerb.None) return;

        // 한 번에 하나씩: 여러 압축을 골라 우클릭하면 파일마다 Iljip.exe 가 떠 단일 인스턴스 파이프로 거의 동시에 들어온다
        // (App.OnSecondInstanceLaunched 는 async void). 겹쳐 돌면 진행·암호 창이 섞이고 같은 '이름 (2)' 폴더를 둘이 고를 수 있어 차례로 처리한다.
        await _commandGate.WaitAsync();
        _inCommandGate.Value = true;   // W2-06: 이 흐름에서 이어지는 고치기(끌어 놓기 → 넣기)가 차례를 다시 기다리지 않게
        try
        {
            // W2-05(I2-6): 풀기 동사 뒤 압축이 여러 개면 여러 개 풀기
            if (cmd.Verb is CommandVerb.Extract or CommandVerb.ExtractHere or CommandVerb.ExtractToFolder
                && ExistingArchives(cmd) is { Count: > 1 } many)
            {
                if (cmd.Verb == CommandVerb.Extract)
                {
                    // W2-09(I2-13): 폴더 고르기 창 대신 풀기 창(폴더 만들기를 끄면 고른 폴더에 모두 바로)
                    var target = AskExtractTarget(many, selectedOnly: false, $"압축 파일 {many.Count}개를 풀 폴더를 선택하세요");
                    if (target is not null)
                        await RunExtractBatchWithTargetAsync(many, target);
                }
                else
                {
                    await RunExtractBatchAsync(many, RuleFor(cmd), fromShell: false);
                }
                return;
            }

            switch (cmd.Verb)
            {
                case CommandVerb.Open when File.Exists(cmd.Archive):
                    // W2-12: 파일 연결로 나눔 ZIP 의 '.z01' 을 두 번 누르면 같은 세트의 '.zip'(마지막 조각)으로 연다
                    await OpenArchiveAsync(ArchiveVolumes.OpenTargetFor(cmd.Archive!));
                    break;

                // 해제 명령은 열기가 실제로 된 경우에만 이어서 푼다 — 열지 못했으면(오류 창을 이미 보임) 해제 오류 창을 또 띄우지 않는다.
                // 파일 이름까지 암호가 걸린 7z 는 열기가 암호를 묻는다(I1-7) — 맞으면 그 암호로 풀고(다시 묻지 않음), 취소하면 풀지 않는다.
                case CommandVerb.Extract when File.Exists(cmd.Archive):
                    if (await OpenArchiveAsync(cmd.Archive!))
                        await ExtractAsync(); // 폴더 선택 다이얼로그 + 해제 완료까지 대기(fire-and-forget 제거)
                    break;

                case CommandVerb.ExtractHere when File.Exists(cmd.Archive):
                    {
                        var ap = Path.GetFullPath(cmd.Archive!);
                        var parent = Path.GetDirectoryName(ap) ?? Environment.CurrentDirectory;
                        if (await OpenArchiveAsync(ap))
                            await ExtractToFolderAsync(ap, parent);   // 같은 이름 파일은 설정대로(기본 '묻기' — W2-05, 예전엔 늘 '이름 (1).ext')
                        break;
                    }

                case CommandVerb.ExtractToFolder when File.Exists(cmd.Archive):
                    {
                        var ap = Path.GetFullPath(cmd.Archive!);
                        var parent = Path.GetDirectoryName(ap) ?? Environment.CurrentDirectory;
                        if (await OpenArchiveAsync(ap))
                        {
                            // 열기(오류 창) 뒤에 정한다 — 그사이 생긴 폴더와도 겹치지 않게
                            var dest = ArchiveNaming.UniqueFolder(parent, ArchiveNaming.FolderNameFor(ap));
                            // 취소·손상으로 멈췄는데 새 폴더에 파일이 하나도 안 남았으면(빈 하위 폴더뿐) 그 폴더를 지운다 — 다음에 풀 때 번호가 밀리지 않게.
                            // 앞서 다 풀린 파일이 있으면 남긴다(기존 계약). 암호 오류·암호 창 취소는 서비스가 이미 모두 되돌렸다.
                            if (!await ExtractToFolderAsync(ap, dest))
                                ArchiveNaming.RemoveFolderIfNoFiles(dest);
                        }
                        break;
                    }

                case CommandVerb.ExtractTo when File.Exists(cmd.Archive):
                    if (await OpenArchiveAsync(cmd.Archive!))
                        await ExtractToFolderAsync(cmd.Archive!, cmd.Dest!);
                    break;

                case CommandVerb.Compress:
                    // 셸 '일집으로 압축'은 사용자가 고른 것 — 압축을 보는 중이면 그것을 닫고 새 목록을 만든다
                    if (IsArchiveMode) StartNewStaging(cmd.Paths);
                    else AddToStaging(cmd.Paths);
                    // 사용자 의도가 명확하므로 압축 다이얼로그를 바로 띄움(끝날 때까지 기다림)
                    if (IsStagingMode && CompressCommand.CanExecute(null))
                        await CompressCommand.ExecuteAsync(null);
                    break;

                case CommandVerb.CompressTo:
                    // W2-11: '--compress' 와 같되 압축 창의 '만들 곳'을 그 폴더로(관리자 권한으로 다시 시도·오른쪽 끌기 W2-12)
                    if (IsArchiveMode) StartNewStaging(cmd.Paths);
                    else AddToStaging(cmd.Paths);
                    if (IsStagingMode && StagedSources() is { Count: > 0 } staged)
                        await CompressStagedAsync(staged, cmd.Dest);
                    break;

                case CommandVerb.ExtractToFolderIn:
                case CommandVerb.ExtractInto:
                    // W2-11: 보통은 메인 창 없이(ProcessShellCommandAsync) 오지만, 이 길로 와도 같게 — 그 폴더 안 '이름' 폴더마다 / 그 폴더에 바로 모두
                    if (ExistingArchives(cmd) is { Count: > 0 } inFolder)
                        await RunExtractBatchAsync(inFolder, RuleFor(cmd), fromShell: false);
                    break;

                case CommandVerb.AddTo:
                    // W2-06: 보통은 메인 창 없이(ProcessShellCommandAsync) 오지만, 이 길로 와도 같게 넣는다
                    await RunAddToAsync(cmd.Archive, cmd.Paths);
                    break;

                case CommandVerb.ExtractSmart:
                    // W2-12: 보통은 메인 창 없이(ProcessShellCommandAsync) 오지만, 이 길로 와도 같게 — 압축마다 알아서
                    if (ExistingArchives(cmd) is { Count: > 0 } smartArchives)
                        await RunExtractBatchAsync(smartArchives, ExtractTargetRule.Smart, fromShell: false);
                    break;

                case CommandVerb.CompressZip:
                case CommandVerb.Compress7z:
                    // W2-12: 창 없이 바로 압축(보통은 메인 창 없이 오지만 이 길로 와도 같게)
                    await RunQuickCompressAsync(cmd.Paths, cmd.Verb == CommandVerb.Compress7z ? CompressionFormat.SevenZip : CompressionFormat.Zip);
                    break;

                case CommandVerb.Paths:
                    {
                        // 키워드가 아니면 모두 경로로 간주 (드롭과 동일 처리)
                        var pathLike = cmd.Paths.Where(a => File.Exists(a) || Directory.Exists(a)).ToList();
                        if (pathLike.Count > 0)
                            await HandleDroppedPathsAsync(pathLike);
                        break;
                    }
            }
        }
        catch (Exception ex)
        {
            Logger.LogError("명령 처리 중 오류", ex);
            _ui.ShowError($"명령 처리 중 오류:\n\n{ErrorMessages.ToUserMessage(ex)}");
        }
        finally
        {
            _inCommandGate.Value = false;
            _commandGate.Release();
        }
    }

    /// <summary>명령줄 처리를 한 번에 하나씩(<see cref="ProcessCommandLineAsync"/>·<see cref="ProcessShellCommandAsync"/>).</summary>
    private readonly SemaphoreSlim _commandGate = new(1, 1);

    // ───────────── 탐색기 우클릭 풀기: 메인 창 없이 진행 창만(I1-6) ─────────────

    /// <summary>
    /// 탐색기 우클릭으로 온 명령(App 이 부름 — 첫 실행이든 이미 떠 있는 일집이 파이프로 받았든). 풀기 동사(<see cref="ParsedCommand.ShowMainWindow"/> 가 false —
    /// '여기에 풀기'·'"이름"\ 에 풀기'·대화형 '--extract-to')는 메인 창을 쓰지 않는다: 보던 압축·목록을 바꾸지 않고, 진행 창 하나만 띄워 풀고,
    /// 끝나면 설정(extract.openFolderAfter, 기본 켬)에 따라 새로 만든 폴더를 탐색기로 연다('여기에 풀기'는 이미 보던 폴더라 열지 않음).
    /// 오류는 쉬운 한국어 창 하나(여러 개면 끝에 요약). 그 밖의 동사는 <see cref="ProcessCommandLineAsync"/> 그대로.
    /// 이미 도는 우클릭 풀기와 같은 동사면 그 묶음에 붙는다(여러 개를 골라 우클릭하면 파일마다 일집이 떠 거의 동시에 들어옴 — 진행 창 하나·암호 한 번·요약 하나).
    /// </summary>
    public async Task ProcessShellCommandAsync(string[] args)
    {
        var cmd = CommandLineParser.Parse(args);
        if (cmd.ShowMainWindow)
        {
            await ProcessCommandLineAsync(args);
            return;
        }

        // W2-12: 파일 연결 넣기·빼기는 App 이 자체 프로세스로 맡는다(여기로 오지 않음) — 혹시 오면 아무것도 하지 않는다(파이프로 남의 레지스트리를 바꾸지 않게)
        if (cmd.Verb is CommandVerb.RegisterAssociations or CommandVerb.UnregisterAssociations) return;

        // W2-12: 탐색기 우클릭 '"이름.zip"으로 압축'·'"이름.7z"로 압축' — 압축 창 없이 진행 창만. 한 번에 하나씩(명령 차례)
        if (cmd.Verb is CommandVerb.CompressZip or CommandVerb.Compress7z)
        {
            await _commandGate.WaitAsync();
            _inCommandGate.Value = true;
            try
            {
                await RunQuickCompressAsync(cmd.Paths, cmd.Verb == CommandVerb.Compress7z ? CompressionFormat.SevenZip : CompressionFormat.Zip);
            }
            catch (Exception ex)
            {
                Logger.LogError("우클릭 바로 압축 중 오류", ex);
                _ui.ShowError($"압축하지 못했어요.\n\n{ErrorMessages.ToPlainMessage(ex)}");
            }
            finally
            {
                _inCommandGate.Value = false;
                _commandGate.Release();
            }
            return;
        }

        // W2-06: 탐색기 우클릭 'zip에 추가'(--add-to 압축 경로… / 기존 zip 고르기는 --add-to-pick 경로… — 고르는 창부터) — 진행 창만, 끝나면 짧은 안내
        // (RunEditAsync 가 명령 차례를 잡음)
        if (cmd.Verb == CommandVerb.AddTo)
        {
            try
            {
                await RunAddToAsync(cmd.Archive, cmd.Paths);
            }
            catch (Exception ex)
            {
                Logger.LogError("우클릭 넣기 중 오류", ex);
                _ui.ShowError($"압축 파일에 넣지 못했어요.\n\n{ErrorMessages.ToPlainMessage(ex)}");
            }
            return;
        }

        var archives = ExistingArchives(cmd);
        if (archives.Count == 0)
        {
            // 메인 창이 없어 조용히 끝나면 아무 일도 없었던 것처럼 보인다 — 그새 지웠거나 옮긴 파일이면 알려 준다
            if (cmd.Archives.Count > 0)
                _ui.ShowError($"풀 압축 파일을 찾을 수 없어요. 지웠거나 다른 곳으로 옮겼는지 확인해 주세요.\n\n{cmd.Archives[0]}");
            return;
        }
        var rule = RuleFor(cmd);

        lock (_shellBatchGate)
        {
            if (_shellBatch is { } running && running.Rule == rule && running.TryAdd(archives)) return;
        }

        await _commandGate.WaitAsync();
        _inCommandGate.Value = true;
        try
        {
            await RunExtractBatchAsync(archives, rule, fromShell: true);
        }
        catch (Exception ex)
        {
            Logger.LogError("우클릭 풀기 중 오류", ex);
            _ui.ShowError($"압축을 풀지 못했어요.\n\n{ErrorMessages.ToPlainMessage(ex)}");
        }
        finally
        {
            _inCommandGate.Value = false;
            _commandGate.Release();
        }
    }

    /// <summary>지금 도는 우클릭 풀기 묶음(뒤에 들어온 같은 동사가 붙음). 없으면 null.</summary>
    private BatchExtractor? _shellBatch;

    private readonly object _shellBatchGate = new();

    /// <summary>명령의 압축 파일 중 있는 것(전체 경로, 같은 것은 한 번).</summary>
    private static List<string> ExistingArchives(ParsedCommand cmd) =>
        cmd.Archives
            .Where(File.Exists)
            .Select(Path.GetFullPath)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

    /// <summary>풀기 동사 → 풀 곳 규칙.</summary>
    private static ExtractTargetRule RuleFor(ParsedCommand cmd) => cmd.Verb switch
    {
        CommandVerb.ExtractHere => ExtractTargetRule.Here,
        CommandVerb.ExtractTo => new ExtractTargetRule(ExtractTargetKind.Into, cmd.Dest),
        CommandVerb.ExtractToFolderIn => new ExtractTargetRule(ExtractTargetKind.NamedFolderIn, cmd.Dest),   // W2-11
        CommandVerb.ExtractInto => new ExtractTargetRule(ExtractTargetKind.Into, cmd.Dest),                // W2-11 검증 지적
        CommandVerb.ExtractSmart => ExtractTargetRule.Smart,                                                 // W2-12 알아서 풀기
        _ => ExtractTargetRule.NamedFolder,
    };

    /// <summary>
    /// 풀 곳 규칙 → 관리자 권한으로 다시 할 같은 명령(W2-11, I3-16): 여기에 '--extract-here', '이름' 폴더 '--extract-to-folder', 고른 폴더 안 '이름' 폴더
    /// '--extract-to-folder-in', 고른 폴더에 바로는 하나면 '--extract-to', 여럿이면 '--extract-into 폴더 압축…'(W2-11 검증 지적 — 예전엔 명령이 없어 첫 압축만 다시 열었음).
    /// </summary>
    internal static ParsedCommand? ElevatedRetryFor(ExtractTargetRule rule, IReadOnlyList<string> archives)
    {
        if (archives.Count == 0) return null;
        var none = Array.Empty<string>();
        return rule.Kind switch
        {
            ExtractTargetKind.Here => new ParsedCommand(CommandVerb.ExtractHere, archives[0], null, none) { Archives = archives },
            ExtractTargetKind.NamedFolder => new ParsedCommand(CommandVerb.ExtractToFolder, archives[0], null, none) { Archives = archives },
            ExtractTargetKind.Smart => new ParsedCommand(CommandVerb.ExtractSmart, archives[0], null, none) { Archives = archives },   // W2-12
            ExtractTargetKind.NamedFolderIn when rule.Folder is not null
                => new ParsedCommand(CommandVerb.ExtractToFolderIn, archives[0], rule.Folder, none) { Archives = archives },
            ExtractTargetKind.Into when rule.Folder is not null && archives.Count == 1
                => new ParsedCommand(CommandVerb.ExtractTo, archives[0], rule.Folder, none),
            ExtractTargetKind.Into when rule.Folder is not null
                => new ParsedCommand(CommandVerb.ExtractInto, archives[0], rule.Folder, none) { Archives = archives },
            _ => null,
        };
    }

    // ───────────── 여러 개 풀기(I2-6) ─────────────

    /// <summary>
    /// 일집 안에서 열기 창으로 압축을 여러 개 골랐을 때: '모두 풀까요?' → 풀 폴더 → 그 안의 '이름' 폴더마다 푼다(서로 섞이지 않게).
    /// </summary>
    internal async Task ExtractPickedArchivesAsync(IReadOnlyList<string> picked)
    {
        var archives = picked.Where(File.Exists).Select(Path.GetFullPath).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (archives.Count == 0) return;
        if (archives.Count == 1)
        {
            await OpenArchiveAsync(archives[0]);
            return;
        }
        int choice = _ui.Choose($"압축 파일 {archives.Count}개를 골랐어요. 모두 풀까요?\n(다음 창에서 풀 곳을 골라요 — 압축마다 '이름' 폴더를 만들어 풀 수 있어요)",
            new[] { "모두 풀기", "취소" }, defaultIndex: 0);
        if (choice != 0) return;
        // W2-09(I2-13): 폴더 고르기 창 대신 풀기 창
        var target = AskExtractTarget(archives, selectedOnly: false, $"압축 파일 {archives.Count}개를 풀 폴더를 선택하세요");
        if (target is null) return;
        await RunExtractBatchWithTargetAsync(archives, target);
    }

    /// <summary>
    /// 여러 개(하나도 됨) 풀기를 진행 창 하나로 한다. 끝나면:
    /// - 풀고 나서 압축 파일 지우기(I2-5 — 설정, 묶음을 취소하지 않았고 그 압축이 오류·건너뛴 파일 없이 다 풀렸을 때만, 휴지통으로).
    /// - 우클릭(<paramref name="fromShell"/>)이고 '이름' 폴더·지정 폴더에 풀었으면 설정에 따라 결과 폴더 열기(I1-6).
    /// - 하나면 실패만 쉬운 한국어 창으로(암호 창 취소는 조용히), 여럿이면 요약 창(모두 풀었고 폴더를 열었으면 띄우지 않음).
    /// </summary>
    /// <param name="target">
    /// 풀기 창의 답(W2-09) — 있으면 휴지통·폴더 열기를 설정 대신 그 답대로 하고(폴더 열기는 하나면 그 폴더, 여럿이면 고른 풀 곳 한 번), 없으면 설정대로(예전과 같음).
    /// </param>
    internal async Task<BatchResult> RunExtractBatchAsync(IReadOnlyList<string> archives, ExtractTargetRule rule, bool fromShell, ExtractTarget? target = null)
    {
        var batch = new BatchExtractor(_locator, _ui, CreateCollisionResolver(), rule, archives);
        if (fromShell)
            lock (_shellBatchGate) _shellBatch = batch;

        var session = _ui.ShowProgress("압축 해제 중...");
        BatchResult result;
        try
        {
            result = await batch.RunAsync(session);
        }
        finally
        {
            session.Close();
            if (fromShell)
                lock (_shellBatchGate)
                    if (ReferenceEquals(_shellBatch, batch)) _shellBatch = null;
        }

        // I2-5: 풀고 나서 압축 파일(조각 모두)을 휴지통으로 — 묶음을 취소했으면 하나도 지우지 않는다
        var notes = new List<string>();
        if (!result.Cancelled)
        {
            foreach (var o in result.Outcomes.Where(o => o.Succeeded))
                if (RecycleArchiveAfterExtract(o.ArchivePath, o.Skipped, target?.DeleteArchiveAfter) is { } note)
                    notes.Add(note);
        }

        // I1-6: 우클릭으로 새 폴더(또는 지정 폴더)에 풀었으면 결과 폴더를 연다(설정). '여기에 풀기'는 이미 보던 폴더라 열지 않는다.
        // W2-09: 풀기 창으로 풀었으면 그 창의 '다 풀면 폴더 열기'대로.
        bool opened = false;
        if (target is not null)
        {
            if (target.OpenFolderAfter && !result.Cancelled)
                opened = OpenResultFolder(result, target.Folder);
        }
        else if (fromShell && rule.Kind != ExtractTargetKind.Here && Settings.Extract.OpenFolderAfter && !result.Cancelled)
            opened = OpenResultFolder(result, smart: rule.Kind == ExtractTargetKind.Smart);

        int total = result.Total;
        StatusText = result.Cancelled
            ? "압축 해제 취소됨"
            : result.SucceededCount == total
                ? total == 1 ? $"압축 해제 완료: {result.Outcomes[0].Destination}" : $"압축 해제 완료: {total}개"
                : "오류 발생";

        if (total == 1 && result.Outcomes.Count == 1)
        {
            var only = result.Outcomes[0];
            // 하나만: 실패면 쉬운 한국어 창 하나(취소·암호 창 취소는 조용히 — 기존 흐름과 같음).
            // W2-11(I3-16): 풀 곳에 쓸 권한이 없으면 그 창에서 '관리자 권한으로 다시 시도'를 묻는다(같은 동사·같은 압축)
            if (!only.Succeeded && only.Error is not null)
            {
                string failure = $"‘{only.Name}’을 풀지 못했어요.\n\n{only.Reason}";
                if (!OfferElevatedRetry(only.Error, failure, ElevatedRetryFor(rule, new[] { only.ArchivePath }), only.ArchivePath, WriteFolderFor(rule, only.ArchivePath)))
                    _ui.ShowError(failure);
            }
            foreach (var n in notes) _ui.ShowWarning(n);
        }
        else if (!(opened && result.SucceededCount == total && notes.Count == 0))
        {
            _ui.ShowSummary(result.ToSummary(notes));
            // W2-11(I3-16): 권한이 없어 못 푼 것이 있으면 요약 뒤에 한 번 묻는다(그것들만 같은 동사로).
            // 풀 곳이 네트워크(공유 폴더 등)인 것은 관리자 권한으로도 같아서 빼고 묻는다(검증 지적)
            var denied = result.Outcomes
                .Where(o => !o.Succeeded && ElevationService.IsAccessDenied(o.Error) && ElevationService.CanHelpWriteTo(WriteFolderFor(rule, o.ArchivePath)))
                .ToList();
            if (denied.Count > 0)
            {
                var paths = denied.Select(o => o.ArchivePath).ToList();
                OfferElevatedRetry(denied[0].Error!, $"압축 파일 {denied.Count}개는 풀 곳에 쓸 권한이 없어 풀지 못했어요.", ElevatedRetryFor(rule, paths), paths[0],
                    WriteFolderFor(rule, paths[0]));
            }
        }
        return result;
    }

    /// <summary>풀 곳 규칙에서 이 압축을 풀 폴더(여기에·'이름' 폴더는 압축이 있는 폴더, 고른 폴더면 그 폴더) — 관리자 권한 질문이 네트워크 폴더인지 볼 때.</summary>
    private static string? WriteFolderFor(ExtractTargetRule rule, string archive) =>
        rule.Kind is ExtractTargetKind.Here or ExtractTargetKind.NamedFolder or ExtractTargetKind.Smart ? Path.GetDirectoryName(archive) : rule.Folder;

    /// <summary>
    /// 우클릭 풀기 뒤 결과 폴더 열기(I1-6). 하나를 풀었으면 그 폴더, 여럿을 풀었으면 모두 같은 곳에 생겼을 때 그 위 폴더를 한 번(창이 여러 개 뜨지 않게).
    /// 진짜 탐색기는 <see cref="AppServices.Shell"/>(시험은 가짜 — 호출만 기록).
    /// </summary>
    /// <param name="chosenFolder">풀기 창에서 고른 풀 곳(W2-09) — 여럿을 풀었으면 이 폴더를 한 번 연다.</param>
    /// <param name="smart">
    /// 알아서 풀기(W2-12): 압축마다 보일 폴더(<see cref="ArchiveOutcome.ShowFolder"/> — '이름' 폴더 또는 바로 푼 맨 위 폴더)를 연다.
    /// 바로 푼 파일 하나뿐인 압축은 이미 보던 폴더라 열지 않는다('여기에 풀기'와 같음).
    /// </param>
    private bool OpenResultFolder(BatchResult result, string? chosenFolder = null, bool smart = false)
    {
        var done = result.Outcomes
            .Where(o => o.Succeeded && o.Destination is not null && (!smart || o.ShowFolder is not null))
            .Select(o => smart ? o.ShowFolder! : o.Destination!)
            .ToList();
        if (done.Count == 0) return false;
        try
        {
            if (done.Count == 1)
            {
                _services.Shell.OpenFolder(done[0]);
                return true;
            }
            if (chosenFolder is not null)
            {
                _services.Shell.OpenFolder(chosenFolder);
                return true;
            }
            var parents = done.Select(d => Path.GetDirectoryName(d.TrimEnd(Path.DirectorySeparatorChar)))
                .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            if (parents.Count == 1 && parents[0] is { } parent)
            {
                _services.Shell.OpenFolder(parent);
                return true;
            }
        }
        catch (Exception ex)
        {
            Logger.LogError("결과 폴더를 열지 못함", ex);   // 탐색기를 못 띄워도 풀기는 끝났다 — 창은 띄우지 않음
        }
        return false;
    }
}
