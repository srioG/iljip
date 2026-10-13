namespace Iljip.Services;

/// <summary>명령줄(셸 확장 우클릭 메뉴·탐색기 더블클릭·두 번째 실행이 넘긴 인자)로 할 일.</summary>
public enum CommandVerb
{
    /// <summary>인자 없음·인자 모자람 — 아무것도 안 함.</summary>
    None,
    /// <summary>--open &lt;압축&gt; : 열기만.</summary>
    Open,
    /// <summary>--extract &lt;압축…&gt; : 풀 폴더를 고르는 창 → 풀기(여러 개면 창 한 번 + 여러 개 풀기 — W2-05).</summary>
    Extract,
    /// <summary>--extract-here &lt;압축…&gt; : 압축 파일이 있는 폴더에 바로 풀기(반디집 '여기에 풀기'). 우클릭이면 메인 창 없이 진행 창만(I1-6).</summary>
    ExtractHere,
    /// <summary>--extract-to-folder &lt;압축…&gt; : '부모\이름' 폴더에 풀기. 그 이름이 이미 있으면 '이름 (2)' …(합치지 않음). 우클릭이면 메인 창 없이.</summary>
    ExtractToFolder,
    /// <summary>--extract-to &lt;압축&gt; &lt;폴더&gt; : 지정 폴더에 풀기(창 없이 풀기는 App 이 따로 맡음).</summary>
    ExtractTo,
    /// <summary>--compress &lt;경로…&gt; : 압축 대기에 넣고 한 화면 압축 창부터(W2-07 — 예전 저장 창·옵션 창을 하나로).</summary>
    Compress,
    /// <summary>키워드 없이 경로만 — 끌어 놓기와 같게.</summary>
    Paths,
    /// <summary>
    /// --add-to &lt;압축&gt; &lt;경로…&gt; : 그 압축 파일(ZIP·7z)에 경로들을 넣는다(W2-06 — 탐색기 우클릭 'zip에 추가'). 메인 창 없이 진행 창만, 끝나면 짧은 안내.
    /// --add-to-pick &lt;경로…&gt; 도 이 동사(<see cref="ParsedCommand.Archive"/> = null) — 넣을 압축 파일을 고르는 창부터('zip에 추가(기존 zip 고르기)').
    /// </summary>
    AddTo,

    // ───── W2-11(관리자 권한으로 다시 시도 I3-16 — 오른쪽 끌기 W2-12 도 씀)에서 더한 것 ─────

    /// <summary>
    /// --compress-to &lt;만들 폴더&gt; &lt;경로…&gt; : '--compress' 와 같되 한 화면 압축 창의 '만들 곳'을 그 폴더로 정해 연다
    /// (<see cref="ParsedCommand.Dest"/> = 만들 폴더, <see cref="ParsedCommand.Paths"/> = 원본). 압축이 권한 오류로 실패했을 때 관리자 권한으로 다시 하는 길.
    /// </summary>
    CompressTo,

    /// <summary>
    /// --extract-to-folder-in &lt;폴더&gt; &lt;압축…&gt; : 그 폴더 안의 '이름' 폴더에 풀기(있으면 '이름 (2)' — <see cref="CommandVerb.ExtractToFolder"/> 와 같은 규칙, 부모만 다름).
    /// <see cref="ParsedCommand.Dest"/> = 그 폴더. 메인 창 없이 진행 창만.
    /// </summary>
    ExtractToFolderIn,

    /// <summary>
    /// --extract-into &lt;폴더&gt; &lt;압축…&gt; : 압축 여럿을 그 폴더에 바로 풀기(풀기 창에서 '폴더 만들기'를 끄고 여럿을 푼 것 — W2-11 검증 지적: 관리자 권한으로 다시 시도가
    /// 모두를 같은 곳에 다시 하게). <see cref="ParsedCommand.Dest"/> = 그 폴더. 메인 창 없이 진행 창만. 하나는 예전대로 '--extract-to 압축 폴더'.
    /// </summary>
    ExtractInto,
}

/// <summary>
/// 해석 결과. <see cref="Paths"/> 는 <see cref="CommandVerb.Compress"/>·<see cref="CommandVerb.Paths"/>·<see cref="CommandVerb.AddTo"/>(넣을 경로 — W2-06) 일 때만 찬다.
/// <see cref="Archive"/> 는 압축 파일 하나(여러 개면 첫 번째 — 예전 호출과 호환), <see cref="Archives"/> 는 전부(W2-05).
/// </summary>
public sealed record ParsedCommand(CommandVerb Verb, string? Archive, string? Dest, IReadOnlyList<string> Paths)
{
    public static readonly ParsedCommand None = new(CommandVerb.None, null, null, Array.Empty<string>());

    /// <summary>압축 파일 전부(풀기 동사는 동사 뒤 여러 개 — 설계 §6, W2-05). 첫 번째가 <see cref="Archive"/>.</summary>
    public IReadOnlyList<string> Archives { get; init; } = Archive is null ? Array.Empty<string>() : new[] { Archive };

    /// <summary>
    /// 메인 창을 띄울 명령인가(I1-6, W2-05). 탐색기 우클릭 풀기 동사('여기에 풀기'·'"이름"\ 에 풀기'·대화형 '--extract-to')는
    /// 메인 창 없이 진행 창만 — 일집이 이미 떠 있어도 메인 창을 앞으로 가져오지 않는다. 그 밖(열기·풀기 창·압축·경로)은 메인 창.
    /// W2-06: '--add-to'(압축 파일에 넣기)도 메인 창 없이. W2-11: '--extract-to-folder-in'(폴더 안 '이름' 폴더에 풀기)도 메인 창 없이.
    /// </summary>
    public bool ShowMainWindow => Verb is not (CommandVerb.ExtractHere or CommandVerb.ExtractToFolder or CommandVerb.ExtractTo or CommandVerb.AddTo
        or CommandVerb.ExtractToFolderIn or CommandVerb.ExtractInto);

    /// <summary>
    /// '--new-window' 표시(W2-11, I3-15 '창 두 개로 나란히'): 단일 인스턴스(이미 떠 있는 일집에 넘기기)를 건너뛰고 따로 창을 띄운다.
    /// 뒤에 동사·경로가 없어도 된다(그때 <see cref="Verb"/> 는 None — 빈 새 창).
    /// </summary>
    public bool NewWindow { get; init; }

    /// <summary>
    /// '--elevated' 표시(W2-11, I3-16): '관리자 권한으로 다시 시도'로 뜬 일집. 다시 권한 상승을 권하지 않고(무한 반복 방지), 단일 인스턴스 파이프에 붙지 않는다
    /// (권한 수준이 다른 파이프는 막힘). 창 없이 풀기(재사용장치 계약 — 정확히 '--extract-to 압축 폴더')로 해석되지 않게 늘 맨 앞에 둔다.
    /// </summary>
    public bool Elevated { get; init; }
}

/// <summary>
/// 명령줄 해석(순수 함수 — 파일을 보지 않는다. 파일이 있는지는 화면(MainViewModel)이 본다).
/// 키워드는 대소문자를 가린다('--OPEN' 은 키워드가 아니라 경로 목록 — 기존 동작, 기록 테스트).
/// 창 없이 풀기('--extract-to', 대소문자 무시)는 App.IsHeadlessExtractCommand 가 먼저 따로 맡는다.
/// '--extract-to-folder' 와 '--extract-to' 는 통째로 같은지로만 비교하므로 섞이지 않는다.
/// 풀기 동사(--extract·--extract-here·--extract-to-folder)는 뒤의 경로를 모두 압축 파일로 받는다(W2-05 — 여러 개 풀기 I2-6). '--open' 은 하나.
/// 셸 확장(C++ IljipCommands.cpp)이 넘기는 키워드는 모두 여기서 알아봐야 한다(ShellExtensionContractTests 가 확인).
/// W2-11: 맨 앞 표시 '--new-window'(새 창)·'--elevated'(관리자 권한으로 다시 뜬 일집)를 떼고 해석하며, 반대로 명령을 인자로 되돌리는 <see cref="ToArgs"/> 를 둔다.
/// </summary>
public static class CommandLineParser
{
    public const string OpenKeyword = "--open";
    public const string ExtractKeyword = "--extract";
    public const string ExtractHereKeyword = "--extract-here";
    public const string ExtractToFolderKeyword = "--extract-to-folder";
    public const string ExtractToKeyword = "--extract-to";
    public const string CompressKeyword = "--compress";

    /// <summary>압축 파일에 넣기(W2-06): '--add-to 압축 경로…'.</summary>
    public const string AddToKeyword = "--add-to";

    /// <summary>
    /// 기존 압축 파일을 골라 넣기(W2-06 — 탐색기 우클릭 'zip에 추가(기존 zip 고르기)'): '--add-to-pick 경로…'. 일집이 ZIP·7Z 고르는 창을 띄운다
    /// (셸 메뉴가 따로 파일 고르는 창을 만들지 않아도 되게).
    /// </summary>
    public const string AddToPickKeyword = "--add-to-pick";

    /// <summary>만들 곳을 정한 압축(W2-11 — 관리자 권한으로 다시 시도·W2-12 오른쪽 끌기): '--compress-to 만들폴더 경로…'.</summary>
    public const string CompressToKeyword = "--compress-to";

    /// <summary>그 폴더 안 '이름' 폴더에 풀기(W2-11 — 관리자 권한으로 다시 시도·W2-12 오른쪽 끌기): '--extract-to-folder-in 폴더 압축…'.</summary>
    public const string ExtractToFolderInKeyword = "--extract-to-folder-in";

    /// <summary>
    /// 압축 여럿을 그 폴더에 바로 풀기(W2-11 검증 지적 — 관리자 권한으로 다시 시도): '--extract-into 폴더 압축…'.
    /// ★ 창 없이 풀기('--extract-to' 와 통째로 같을 때만 — 재사용장치 계약)와 섞이지 않는다(통째로 비교).
    /// </summary>
    public const string ExtractIntoKeyword = "--extract-into";

    /// <summary>새 창(W2-11): 단일 인스턴스를 건너뛴다. 동사 앞에 붙는 표시('--new-window [--open 압축]'·'--new-window 경로').</summary>
    public const string NewWindowFlag = "--new-window";

    /// <summary>관리자 권한으로 다시 뜬 일집(W2-11): 동사 앞에 붙는 표시(늘 맨 앞 — 창 없이 풀기 판정에 걸리지 않게).</summary>
    public const string ElevatedFlag = "--elevated";

    /// <summary>알아보는 키워드 전부(셸 확장 계약 확인용). 동사 앞 표시(<see cref="Flags"/>)는 따로.</summary>
    public static IReadOnlyList<string> Keywords { get; } = new[]
    {
        OpenKeyword, ExtractKeyword, ExtractHereKeyword, ExtractToFolderKeyword, ExtractToKeyword, CompressKeyword, AddToKeyword, AddToPickKeyword,
        CompressToKeyword, ExtractToFolderInKeyword, ExtractIntoKeyword,
    };

    /// <summary>동사 앞에 붙는 표시(W2-11) — 맨 앞에 연달아 올 때만 표시로 본다(순서 무관, 대소문자는 키워드처럼 가림).</summary>
    public static IReadOnlyList<string> Flags { get; } = new[] { NewWindowFlag, ElevatedFlag };

    public static ParsedCommand Parse(string[]? args)
    {
        if (args is null || args.Length == 0) return ParsedCommand.None;

        // W2-11: 맨 앞의 표시('--new-window'·'--elevated')를 떼고 나머지를 예전대로 해석한다
        int start = 0;
        bool newWindow = false, elevated = false;
        for (; start < args.Length; start++)
        {
            if (args[start] == NewWindowFlag) newWindow = true;
            else if (args[start] == ElevatedFlag) elevated = true;
            else break;
        }
        if (start == 0) return ParseVerb(args);
        var cmd = ParseVerb(args[start..]);
        return cmd with { NewWindow = newWindow, Elevated = elevated };
    }

    /// <summary>
    /// <see cref="Parse"/> 의 반대(W2-11 — 관리자 권한으로 다시 시도·새 창이 같은 명령을 다시 만들 때): 해석하면 같은 명령이 되는 인자 목록.
    /// 표시는 맨 앞('--elevated' 가 가장 앞 — 창 없이 풀기 판정 'args[0] == --extract-to' 에 걸리지 않게). 따옴표 싸기는 <see cref="CommandLineQuoting"/>.
    /// </summary>
    public static IReadOnlyList<string> ToArgs(ParsedCommand cmd)
    {
        var a = new List<string>();
        if (cmd.Elevated) a.Add(ElevatedFlag);
        if (cmd.NewWindow) a.Add(NewWindowFlag);
        switch (cmd.Verb)
        {
            case CommandVerb.Open:
                a.Add(OpenKeyword);
                a.Add(cmd.Archive!);
                break;
            case CommandVerb.Extract:
            case CommandVerb.ExtractHere:
            case CommandVerb.ExtractToFolder:
                a.Add(cmd.Verb switch
                {
                    CommandVerb.Extract => ExtractKeyword,
                    CommandVerb.ExtractHere => ExtractHereKeyword,
                    _ => ExtractToFolderKeyword,
                });
                a.AddRange(cmd.Archives);
                break;
            case CommandVerb.ExtractTo:
                a.Add(ExtractToKeyword);
                a.Add(cmd.Archive!);
                a.Add(cmd.Dest!);
                break;
            case CommandVerb.ExtractToFolderIn:
            case CommandVerb.ExtractInto:
                a.Add(cmd.Verb == CommandVerb.ExtractInto ? ExtractIntoKeyword : ExtractToFolderInKeyword);
                a.Add(cmd.Dest!);
                a.AddRange(cmd.Archives);
                break;
            case CommandVerb.Compress:
                a.Add(CompressKeyword);
                a.AddRange(cmd.Paths);
                break;
            case CommandVerb.CompressTo:
                a.Add(CompressToKeyword);
                a.Add(cmd.Dest!);
                a.AddRange(cmd.Paths);
                break;
            case CommandVerb.AddTo:
                if (cmd.Archive is null) a.Add(AddToPickKeyword);
                else
                {
                    a.Add(AddToKeyword);
                    a.Add(cmd.Archive);
                }
                a.AddRange(cmd.Paths);
                break;
            case CommandVerb.Paths:
                a.AddRange(cmd.Paths);
                break;
        }
        return a;
    }

    private static ParsedCommand ParseVerb(string[] args)
    {
        if (args.Length == 0) return ParsedCommand.None;

        // 압축을 받는 키워드: 인자가 모자라면 None. '--open' 은 뒤에 더 붙은 인자를 무시(예전 switch 와 같음),
        // 풀기 동사는 뒤의 빈 문자열이 아닌 인자를 모두 압축 파일로(W2-05 — 첫 번째가 Archive)
        CommandVerb? single = args[0] switch
        {
            OpenKeyword => CommandVerb.Open,
            ExtractKeyword => CommandVerb.Extract,
            ExtractHereKeyword => CommandVerb.ExtractHere,
            ExtractToFolderKeyword => CommandVerb.ExtractToFolder,
            _ => null,
        };
        if (single is { } verb)
        {
            if (args.Length < 2 || string.IsNullOrEmpty(args[1])) return ParsedCommand.None;
            if (verb == CommandVerb.Open) return new ParsedCommand(verb, args[1], null, Array.Empty<string>());
            var archives = args.Skip(1).Where(a => !string.IsNullOrEmpty(a)).ToList();
            return new ParsedCommand(verb, archives[0], null, Array.Empty<string>()) { Archives = archives };
        }

        switch (args[0])
        {
            case ExtractToKeyword:
                return args.Length >= 3 && !string.IsNullOrEmpty(args[1]) && !string.IsNullOrWhiteSpace(args[2])
                    ? new ParsedCommand(CommandVerb.ExtractTo, args[1], args[2], Array.Empty<string>())
                    : ParsedCommand.None;

            case AddToKeyword:
                {
                    // W2-06: 첫 인자가 넣을 압축 파일, 나머지(빈 문자열 뺌)가 넣을 경로. 넣을 경로가 없으면 None.
                    if (args.Length < 3 || string.IsNullOrEmpty(args[1])) return ParsedCommand.None;
                    var items = args.Skip(2).Where(a => !string.IsNullOrEmpty(a)).ToList();
                    return items.Count > 0 ? new ParsedCommand(CommandVerb.AddTo, args[1], null, items) : ParsedCommand.None;
                }

            case AddToPickKeyword:
                {
                    // W2-06: 넣을 경로만(빈 문자열 뺌) — 압축 파일은 일집이 고르는 창으로 묻는다(Archive = null). 넣을 경로가 없으면 None.
                    var items = args.Skip(1).Where(a => !string.IsNullOrEmpty(a)).ToList();
                    return items.Count > 0 ? new ParsedCommand(CommandVerb.AddTo, null, null, items) : ParsedCommand.None;
                }

            case CompressKeyword:
                {
                    var paths = args.Skip(1).Where(a => !string.IsNullOrEmpty(a)).ToList();
                    return paths.Count > 0 ? new ParsedCommand(CommandVerb.Compress, null, null, paths) : ParsedCommand.None;
                }

            case CompressToKeyword:
                {
                    // W2-11: 첫 인자가 만들 폴더, 나머지(빈 문자열 뺌)가 원본. 원본이 없으면 None.
                    if (args.Length < 3 || string.IsNullOrWhiteSpace(args[1])) return ParsedCommand.None;
                    var paths = args.Skip(2).Where(a => !string.IsNullOrEmpty(a)).ToList();
                    return paths.Count > 0 ? new ParsedCommand(CommandVerb.CompressTo, null, args[1], paths) : ParsedCommand.None;
                }

            case ExtractToFolderInKeyword:
            case ExtractIntoKeyword:
                {
                    // W2-11: 첫 인자가 '이름' 폴더들을 만들 폴더(--extract-into 는 바로 풀 폴더), 나머지(빈 문자열 뺌)가 압축 파일. 압축이 없으면 None.
                    if (args.Length < 3 || string.IsNullOrWhiteSpace(args[1])) return ParsedCommand.None;
                    var archives = args.Skip(2).Where(a => !string.IsNullOrEmpty(a)).ToList();
                    var folderVerb = args[0] == ExtractIntoKeyword ? CommandVerb.ExtractInto : CommandVerb.ExtractToFolderIn;
                    return archives.Count > 0
                        ? new ParsedCommand(folderVerb, archives[0], args[1], Array.Empty<string>()) { Archives = archives }
                        : ParsedCommand.None;
                }

            default:
                // 키워드가 아니면 모두 경로로 본다(드롭과 같은 처리). 있는 경로만 고르는 것은 화면 몫.
                return new ParsedCommand(CommandVerb.Paths, null, null, args.Where(a => !string.IsNullOrEmpty(a)).ToList());
        }
    }
}
