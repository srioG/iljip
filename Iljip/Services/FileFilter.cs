using System.Text;
using System.Text.RegularExpressions;

namespace Iljip.Services;

// 쓸모없는 파일 빼기(I3-6, W2-07). 압축할 폴더 안에서 찾은 파일·폴더 중 빼기 목록에 걸리는 것은 담지 않는다(SourceCollector).
// 사용자가 직접 고른 파일·폴더(맨 위)는 목록에 걸려도 늘 담는다 — 일부러 고른 desktop.ini 같은 것까지 빼지 않게.

/// <summary>압축할 때 뺄 것을 정한다(이름 단위 — 경로는 보지 않음).</summary>
public interface IFileFilter
{
    /// <summary><paramref name="name"/>(파일·폴더 이름 하나)이 빼기 목록에 걸리는가.</summary>
    bool IsExcluded(string name, bool isDirectory);
}

/// <summary>
/// 빼기 목록(설정 compress.excludePatterns). 줄마다 이름 하나 — '*'(아무 글자 여럿)·'?'(아무 글자 하나)를 쓸 수 있고 대소문자는 가리지 않는다.
/// 끝이 '/'(또는 '\')면 폴더만(예: '__MACOSX/'), 아니면 파일만. 경로('폴더/파일')는 쓰지 않는다 — 이름에 '/'가 들어갈 수 없어 맞는 것이 없다.
/// </summary>
public sealed class PatternFileFilter : IFileFilter
{
    /// <summary>
    /// 기본 빼기 목록(설계 §4): 탐색기 미리 보기 캐시(Thumbs.db)·폴더 꾸밈 정보(desktop.ini)·맥 꾸러미(.DS_Store, ._*, __MACOSX/)·
    /// 오피스가 문서를 여는 동안 만드는 잠금 파일(~$*)·임시 파일(*.~tmp).
    /// </summary>
    public static readonly IReadOnlyList<string> DefaultPatterns = new[]
    {
        "Thumbs.db", "desktop.ini", ".DS_Store", "._*", "__MACOSX/", "~$*", "*.~tmp",
    };

    private readonly List<(Regex Rule, bool DirectoryOnly)> _rules = new();

    public PatternFileFilter(IEnumerable<string> patterns)
    {
        var kept = new List<string>();
        foreach (var raw in patterns ?? Array.Empty<string>())
        {
            string p = (raw ?? string.Empty).Trim();
            if (p.Length == 0) continue;
            bool dirOnly = p.EndsWith('/') || p.EndsWith('\\');
            string name = p.TrimEnd('/', '\\');
            if (name.Length == 0) continue;
            kept.Add(p);
            _rules.Add((ToRegex(name), dirOnly));
        }
        Patterns = kept;
    }

    /// <summary>쓰는 목록(빈 줄을 뺀 것).</summary>
    public IReadOnlyList<string> Patterns { get; }

    public bool IsExcluded(string name, bool isDirectory)
    {
        if (string.IsNullOrEmpty(name)) return false;
        string n = name.Normalize(NormalizationForm.FormC);
        foreach (var (rule, dirOnly) in _rules)
            if (dirOnly == isDirectory && rule.IsMatch(n)) return true;
        return false;
    }

    /// <summary>글 상자(한 줄에 하나)의 내용 → 목록. 앞뒤 공백·빈 줄·같은 줄(대소문자 무시)은 뺀다.</summary>
    public static List<string> ParseLines(string? text)
    {
        var list = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in (text ?? string.Empty).Split('\n'))
        {
            string p = line.Trim();
            if (p.Length == 0 || !seen.Add(p)) continue;
            list.Add(p);
        }
        return list;
    }

    /// <summary>목록 → 글 상자 내용(한 줄에 하나).</summary>
    public static string ToLines(IEnumerable<string>? patterns) => string.Join(Environment.NewLine, patterns ?? Array.Empty<string>());

    private static Regex ToRegex(string pattern)
    {
        var sb = new StringBuilder("^");
        foreach (char c in pattern.Normalize(NormalizationForm.FormC))
        {
            sb.Append(c switch
            {
                '*' => ".*",
                '?' => ".",
                _ => Regex.Escape(c.ToString()),
            });
        }
        sb.Append('$');
        return new Regex(sb.ToString(), RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Singleline);
    }
}
