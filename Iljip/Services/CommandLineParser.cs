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
    /// W2-06: '--add-to'(압축 파일에 넣기)도 메인 창 없이.
    /// </summary>
    public bool ShowMainWindow => Verb is not (CommandVerb.ExtractHere or CommandVerb.ExtractToFolder or CommandVerb.ExtractTo or CommandVerb.AddTo);
}

/// <summary>
/// 명령줄 해석(순수 함수 — 파일을 보지 않는다. 파일이 있는지는 화면(MainViewModel)이 본다).
/// 키워드는 대소문자를 가린다('--OPEN' 은 키워드가 아니라 경로 목록 — 기존 동작, 기록 테스트).
/// 창 없이 풀기('--extract-to', 대소문자 무시)는 App.IsHeadlessExtractCommand 가 먼저 따로 맡는다.
/// '--extract-to-folder' 와 '--extract-to' 는 통째로 같은지로만 비교하므로 섞이지 않는다.
/// 풀기 동사(--extract·--extract-here·--extract-to-folder)는 뒤의 경로를 모두 압축 파일로 받는다(W2-05 — 여러 개 풀기 I2-6). '--open' 은 하나.
/// 셸 확장(C++ IljipCommands.cpp)이 넘기는 키워드는 모두 여기서 알아봐야 한다(ShellExtensionContractTests 가 확인).
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

    /// <summary>알아보는 키워드 전부(셸 확장 계약 확인용).</summary>
    public static IReadOnlyList<string> Keywords { get; } = new[]
    {
        OpenKeyword, ExtractKeyword, ExtractHereKeyword, ExtractToFolderKeyword, ExtractToKeyword, CompressKeyword, AddToKeyword, AddToPickKeyword,
    };

    public static ParsedCommand Parse(string[]? args)
    {
        if (args is null || args.Length == 0) return ParsedCommand.None;

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

            default:
                // 키워드가 아니면 모두 경로로 본다(드롭과 같은 처리). 있는 경로만 고르는 것은 화면 몫.
                return new ParsedCommand(CommandVerb.Paths, null, null, args.Where(a => !string.IsNullOrEmpty(a)).ToList());
        }
    }
}
