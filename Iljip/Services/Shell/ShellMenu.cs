using System.IO;

namespace Iljip.Services;

/// <summary>탐색기에서 고른 것의 종류(셸 메뉴가 어떤 하위 메뉴를 보일지 — 확장자와 '폴더인가'만 본다, 파일 내용은 읽지 않음).</summary>
public enum ShellSelectionKind
{
    /// <summary>고른 것이 없음.</summary>
    None,
    /// <summary>압축 파일 하나.</summary>
    OneArchive,
    /// <summary>압축 파일 여러 개(모두 압축 파일).</summary>
    ManyArchives,
    /// <summary>파일·폴더(압축 파일이 아닌 것이 하나라도 있음 — 섞여도 됨).</summary>
    FilesOrFolders,
}

/// <summary>셸 하위 메뉴 하나: 제목과 Iljip.exe 에 넘길 동사(뒤에 고른 경로들 — '--open' 은 첫 경로 하나).</summary>
public sealed record ShellMenuItem(string Id, string Title, string Verb);

/// <summary>
/// 탐색기 우클릭 '일집 ▶' 하위 메뉴(W2-12, I1-2)의 규칙 — C++ 셸 확장(Iljip.ShellExtension/IljipMenu.cpp)이 같은 규칙으로 만든다.
/// C++ 는 이 파일을 쓰지 않으므로 둘을 같게 지키는 것은 계약 시험(소스 대조 + 빌드된 DLL 이 있으면 실제 GetTitle·EnumSubCommands 대조).
/// 확장자로만 고른다(셸 단계에서 파일 내용을 읽지 않음 — 설계 §1). 자동 풀림 .exe 는 압축 파일로 치지 않는다.
/// </summary>
public static class ShellMenu
{
    /// <summary>최상위 메뉴 제목('일집 ▶').</summary>
    public const string RootTitle = "일집";

    /// <summary>폴더 안 빈 곳(Directory\Background) 메뉴 — 일집 창을 연다.</summary>
    public const string BackgroundTitle = "일집 열기";

    /// <summary>오른쪽 단추로 끌어 놓을 때(I3-10) — 끌어 놓은 폴더에 압축(--compress-to).</summary>
    public const string DragCompressTitle = "일집: 여기에 압축";

    /// <summary>오른쪽 단추로 끌어 놓을 때(I3-10) — 끈 것이 모두 압축 파일이면, 끌어 놓은 폴더 안 '이름' 폴더에 풀기(--extract-to-folder-in).</summary>
    public const string DragExtractTitle = "일집: 여기에 풀기";

    /// <summary>메뉴 제목에 넣는 이름의 최대 글자 수(넘으면 39자 + '…').</summary>
    public const int MaxMenuNameChars = 40;

    /// <summary>
    /// 압축 파일로 보는 끝 확장자(C++ <c>ArchiveExtensions</c> 와 같은 차례). 일집이 풀 수 있는 형식의 끝 확장자 전부
    /// ('.tar.gz' → '.gz', '.7z.001'·'.zip.001' → '.001', '.part1.rar' → '.rar'). '.z01' 은 처리기가 없어(마지막 .zip 으로 엶) 넣지 않는다.
    /// </summary>
    public static IReadOnlyList<string> ArchiveExtensions { get; } = new[]
    {
        ".zip", ".7z", ".rar", ".tar", ".gz", ".bz2", ".xz", ".tgz", ".tbz2", ".tbz", ".txz",
        ".zipx", ".001", ".iso", ".cab", ".lzh", ".lha", ".wim", ".egg", ".alz",
    };

    /// <summary>탐색기에서 고른 것 하나(경로와 폴더인가 — C++ 는 GetFileAttributesW 로 본다).</summary>
    public readonly record struct Item(string Path, bool IsFolder);

    /// <summary>이름(확장자)만 보고 압축 파일인가.</summary>
    public static bool IsArchiveName(string path)
    {
        string name = System.IO.Path.GetFileName(path ?? string.Empty);
        int dot = name.LastIndexOf('.');
        return dot >= 0 && ArchiveExtensions.Contains(name[dot..], StringComparer.OrdinalIgnoreCase);
    }

    public static ShellSelectionKind Classify(IReadOnlyList<Item> items)
    {
        if (items.Count == 0) return ShellSelectionKind.None;
        if (items.All(i => !i.IsFolder && IsArchiveName(i.Path)))
            return items.Count == 1 ? ShellSelectionKind.OneArchive : ShellSelectionKind.ManyArchives;
        return ShellSelectionKind.FilesOrFolders;
    }

    /// <summary>하위 메뉴(차례대로). 고른 것이 없으면 빈 목록.</summary>
    public static IReadOnlyList<ShellMenuItem> ItemsFor(IReadOnlyList<Item> items)
    {
        switch (Classify(items))
        {
            case ShellSelectionKind.OneArchive:
                return new[]
                {
                    new ShellMenuItem("open", "일집으로 열기", CommandLineParser.OpenKeyword),
                    new ShellMenuItem("extract-here", "여기에 풀기", CommandLineParser.ExtractHereKeyword),
                    new ShellMenuItem("extract-to-folder", $"\"{ShortenForMenu(ArchiveNaming.FolderNameFor(items[0].Path))}\"\\ 에 풀기",
                        CommandLineParser.ExtractToFolderKeyword),
                    new ShellMenuItem("extract-smart", "알아서 풀기", CommandLineParser.ExtractSmartKeyword),
                    new ShellMenuItem("extract", "다른 곳에 풀기...", CommandLineParser.ExtractKeyword),
                };
            case ShellSelectionKind.ManyArchives:
                return new[]
                {
                    new ShellMenuItem("extract", "압축 풀기...", CommandLineParser.ExtractKeyword),
                    new ShellMenuItem("extract-smart", "각각 알아서 풀기", CommandLineParser.ExtractSmartKeyword),
                    new ShellMenuItem("extract-to-folder", "각각 이름 폴더에 풀기", CommandLineParser.ExtractToFolderKeyword),
                };
            case ShellSelectionKind.FilesOrFolders:
                {
                    string name = ShortenForMenu(CompressBaseName(items));
                    return new[]
                    {
                        new ShellMenuItem("compress", "압축하기...", CommandLineParser.CompressKeyword),
                        new ShellMenuItem("compress-zip", $"\"{name}.zip\"으로 압축", CommandLineParser.CompressZipKeyword),
                        new ShellMenuItem("compress-7z", $"\"{name}.7z\"로 압축", CommandLineParser.Compress7zKeyword),
                    };
                }
            default:
                return Array.Empty<ShellMenuItem>();
        }
    }

    /// <summary>
    /// '"이름.zip"으로 압축'의 이름 — <see cref="CompressNaming.SuggestBaseName"/> 와 같은 규칙(창 없이 바로 압축이 실제로 만드는 이름)이되
    /// '폴더인가'를 파일 시스템에 묻지 않고 받은 값으로(C++ 와 같게). 하나: 폴더면 그 이름, 파일이면 확장자를 뗀 이름. 여럿: 첫 것이 있는 폴더 이름.
    /// </summary>
    public static string CompressBaseName(IReadOnlyList<Item> items)
    {
        if (items.Count == 0) return CompressNaming.FallbackName;
        if (items.Count == 1)
        {
            string path = items[0].Path ?? string.Empty;
            string trimmed = path.TrimEnd(System.IO.Path.DirectorySeparatorChar, System.IO.Path.AltDirectorySeparatorChar);
            string name;
            if (items[0].IsFolder)
                name = System.IO.Path.GetFileName(trimmed);
            else
            {
                name = System.IO.Path.GetFileNameWithoutExtension(trimmed);
                if (string.IsNullOrWhiteSpace(name)) name = System.IO.Path.GetFileName(trimmed);
            }
            if (string.IsNullOrWhiteSpace(name))
            {
                try { name = new DirectoryInfo(path).Name; } catch { /* 잘못된 경로 */ }
            }
            return CompressNaming.Sanitize(name);
        }
        string dir = System.IO.Path.GetDirectoryName(items[0].Path) ?? string.Empty;
        try { return CompressNaming.Sanitize(new DirectoryInfo(string.IsNullOrEmpty(dir) ? Environment.CurrentDirectory : dir).Name); }
        catch { return CompressNaming.FallbackName; }
    }

    /// <summary>메뉴 제목에 넣을 이름 줄이기(40자를 넘으면 39자 + '…', 서로게이트 쌍은 반으로 자르지 않음) — C++ ShortenForMenu 와 같음.</summary>
    public static string ShortenForMenu(string name)
    {
        if (name.Length <= MaxMenuNameChars) return name;
        int cut = MaxMenuNameChars - 1;
        if (cut > 0 && char.IsHighSurrogate(name[cut - 1])) cut--;
        return name[..cut] + "…";
    }
}
