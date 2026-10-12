namespace Iljip.Services;

/// <summary>명령줄(셸 확장 우클릭 메뉴·탐색기 더블클릭·두 번째 실행이 넘긴 인자)로 할 일.</summary>
public enum CommandVerb
{
    /// <summary>인자 없음·인자 모자람 — 아무것도 안 함.</summary>
    None,
    /// <summary>--open &lt;압축&gt; : 열기만.</summary>
    Open,
    /// <summary>--extract &lt;압축&gt; : 풀 폴더를 고르는 창 → 풀기.</summary>
    Extract,
    /// <summary>--extract-here &lt;압축&gt; : 압축 파일이 있는 폴더에 바로 풀기(반디집 '여기에 풀기').</summary>
    ExtractHere,
    /// <summary>--extract-to-folder &lt;압축&gt; : '부모\이름' 폴더에 풀기. 그 이름이 이미 있으면 '이름 (2)' …(합치지 않음).</summary>
    ExtractToFolder,
    /// <summary>--extract-to &lt;압축&gt; &lt;폴더&gt; : 지정 폴더에 풀기(창 없이 풀기는 App 이 따로 맡음).</summary>
    ExtractTo,
    /// <summary>--compress &lt;경로…&gt; : 압축 대기에 넣고 저장 창부터.</summary>
    Compress,
    /// <summary>키워드 없이 경로만 — 끌어 놓기와 같게.</summary>
    Paths,
}

/// <summary>해석 결과. <see cref="Paths"/> 는 <see cref="CommandVerb.Compress"/>·<see cref="CommandVerb.Paths"/> 일 때만 찬다.</summary>
public sealed record ParsedCommand(CommandVerb Verb, string? Archive, string? Dest, IReadOnlyList<string> Paths)
{
    public static readonly ParsedCommand None = new(CommandVerb.None, null, null, Array.Empty<string>());
}

/// <summary>
/// 명령줄 해석(순수 함수 — 파일을 보지 않는다. 파일이 있는지는 화면(MainViewModel)이 본다).
/// 키워드는 대소문자를 가린다('--OPEN' 은 키워드가 아니라 경로 목록 — 기존 동작, 기록 테스트).
/// 창 없이 풀기('--extract-to', 대소문자 무시)는 App.IsHeadlessExtractCommand 가 먼저 따로 맡는다.
/// '--extract-to-folder' 와 '--extract-to' 는 통째로 같은지로만 비교하므로 섞이지 않는다.
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

    /// <summary>알아보는 키워드 전부(셸 확장 계약 확인용).</summary>
    public static IReadOnlyList<string> Keywords { get; } = new[]
    {
        OpenKeyword, ExtractKeyword, ExtractHereKeyword, ExtractToFolderKeyword, ExtractToKeyword, CompressKeyword,
    };

    public static ParsedCommand Parse(string[]? args)
    {
        if (args is null || args.Length == 0) return ParsedCommand.None;

        // 압축 하나를 받는 키워드: 인자가 모자라면 None(뒤에 더 붙은 인자는 무시 — 예전 switch 와 같음)
        CommandVerb? single = args[0] switch
        {
            OpenKeyword => CommandVerb.Open,
            ExtractKeyword => CommandVerb.Extract,
            ExtractHereKeyword => CommandVerb.ExtractHere,
            ExtractToFolderKeyword => CommandVerb.ExtractToFolder,
            _ => null,
        };
        if (single is { } verb)
            return args.Length >= 2 && !string.IsNullOrEmpty(args[1])
                ? new ParsedCommand(verb, args[1], null, Array.Empty<string>())
                : ParsedCommand.None;

        switch (args[0])
        {
            case ExtractToKeyword:
                return args.Length >= 3 && !string.IsNullOrEmpty(args[1]) && !string.IsNullOrWhiteSpace(args[2])
                    ? new ParsedCommand(CommandVerb.ExtractTo, args[1], args[2], Array.Empty<string>())
                    : ParsedCommand.None;

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
