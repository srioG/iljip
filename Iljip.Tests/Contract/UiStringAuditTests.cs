using System.Text.RegularExpressions;
using Iljip.Services;
using Iljip.Tests.TestSupport;

namespace Iljip.Tests.Contract;

/// <summary>
/// [W2-08 — I2-8] 화면 글 점검: 사용자에게 보이는 글은 한국어다(영어 예외 문구가 화면에 나오지 않게).
/// <list type="bullet">
/// <item>소스: XAML 의 Content·Text·Title·ToolTip·Header·Watermark 값, C# 의 창 호출(ShowError·ShowWarning·ShowInfo·AskPassword·Choose·ShowProblem·ShowProgress·MessageBox.Show)
///   안 글자에 한글이 있거나, 허용 목록(형식 이름 ZIP·7Z·TAR·AES-256·ZipCrypto, 단위 MB·%, 숫자·기호)뿐이어야 한다.</item>
/// <item>예외 문구를 창에 그대로 넘기지 않는다(<c>ShowError(… ex.Message …)</c> 금지 — <see cref="ErrorCatalog"/> 를 거친다).</item>
/// <item>실제로: 손상·잘림·암호·조각 빠짐 압축을 풀어 난 예외의 화면 문구(<see cref="ErrorCatalog.Describe"/>)에 한글이 있고 엔진 영어 원문이 없다.</item>
/// </list>
/// ★ 허용 목록을 넓혀서 통과시키지 말 것 — 새 영어 글이 생기면 한국어로 고친다.
/// </summary>
public class UiStringAuditTests
{
    /// <summary>한글 없이 써도 되는 낱말(형식 이름·단위). 이것을 빼고 나면 영어 글자가 남지 않아야 한다.</summary>
    private static readonly string[] Allowed = { "AES-256", "ZipCrypto", "ZIP", "7Z", "TAR", "MB", "%" };

    /// <summary>화면에 나오면 안 되는 엔진 영어 원문.</summary>
    private static readonly string[] EngineEnglish = { "CRC", "Data error", "Unexpected end", "Headers Error", "Can not open", "Exception" };

    private static readonly Regex XamlAttr = new(@"\b(Content|Text|Title|ToolTip|Header|Watermark)\s*=\s*""([^""]*)""", RegexOptions.Compiled);

    private static readonly Regex UiCall = new(@"(?<box>\bMessageBox)?\.(ShowError|ShowWarning|ShowInfo|AskPassword|Choose|ShowProblem|ShowProgress|Show)\s*\(", RegexOptions.Compiled);

    private static readonly Regex StringLiteral = new(@"\$?@?""(?:[^""\\]|\\.)*""", RegexOptions.Compiled);

    private static IEnumerable<string> SourceFiles(string pattern) =>
        Directory.EnumerateFiles(RepoFiles.PathOf("Iljip"), pattern, SearchOption.AllDirectories)
            .Where(p => !p.Split(Path.DirectorySeparatorChar).Any(part => part is "bin" or "obj"));

    /// <summary>글이 한국어인가(한글이 있거나, 허용 낱말·숫자·기호뿐).</summary>
    private static bool IsKoreanOrAllowed(string text)
    {
        if (ErrorCatalog.ContainsHangul(text)) return true;
        string rest = text;
        foreach (var w in Allowed) rest = rest.Replace(w, " ", StringComparison.Ordinal);
        return !Regex.IsMatch(rest, "[A-Za-z]");
    }

    /// <summary>계약: XAML 의 화면 글(바인딩 빼고)은 한국어 또는 허용 목록뿐.</summary>
    [Fact]
    public void XAML_화면_글은_한국어()
    {
        var bad = new List<string>();
        int seen = 0;
        foreach (var file in SourceFiles("*.xaml"))
        {
            string xaml = File.ReadAllText(file);
            foreach (Match m in XamlAttr.Matches(xaml))
            {
                string value = m.Groups[2].Value;
                if (value.StartsWith('{') || string.IsNullOrWhiteSpace(value)) continue;   // 바인딩·자원
                seen++;
                if (!IsKoreanOrAllowed(value)) bad.Add($"{Path.GetFileName(file)}: {m.Groups[1].Value}=\"{value}\"");
            }
        }
        Assert.True(seen > 50, $"XAML 화면 글을 거의 못 찾음({seen}) — 점검 정규식이 낡았는지 확인");
        Assert.True(bad.Count == 0, "한국어가 아닌 화면 글:\n  " + string.Join("\n  ", bad));
    }

    /// <summary>
    /// 계약: C# 의 창 호출에 넘기는 글자는 한국어 또는 허용 목록뿐(줄바꿈 같은 이스케이프·{보간}은 빼고 봄),
    /// 예외 문구(ex.Message·e.Message·inner.Message …)를 창에 그대로 넘기지 않는다.
    /// </summary>
    [Fact]
    public void 창에_넘기는_글은_한국어이고_예외_문구를_그대로_넘기지_않는다()
    {
        var bad = new List<string>();
        int calls = 0;
        foreach (var file in SourceFiles("*.cs"))
        {
            string code = File.ReadAllText(file);
            foreach (Match m in UiCall.Matches(code))
            {
                if (m.Groups[1].Value == "Show" && !m.Groups["box"].Success) continue;   // 창.Show() 는 글이 아님 — MessageBox.Show 만
                string args = Arguments(code, m.Index + m.Length);
                calls++;
                foreach (Match lit in StringLiteral.Matches(args))
                {
                    string body = Regex.Replace(lit.Value, @"^\$?@?""|""$", string.Empty);
                    body = Regex.Replace(body, @"\\(u[0-9A-Fa-f]{4}|.)", " ");                    // 이스케이프(\n·\t·…)
                    if (lit.Value.StartsWith('$')) body = Regex.Replace(body, @"\{[^{}]*\}", " ");  // 보간 자리
                    if (!IsKoreanOrAllowed(body)) bad.Add($"{Path.GetFileName(file)}: {m.Groups[1].Value}({lit.Value})");
                }
                if (Regex.IsMatch(args, @"\b(ex|e|err|error|exception|inner|caught)\w*\.(Message|ToString\(\))"))
                    bad.Add($"{Path.GetFileName(file)}: {m.Groups[1].Value} 에 예외 문구를 그대로 넘김 — {args.Trim()}");
            }
        }
        Assert.True(calls > 30, $"창 호출을 거의 못 찾음({calls}) — 점검 정규식이 낡았는지 확인");
        Assert.True(bad.Count == 0, "한국어가 아니거나 예외 문구를 그대로 쓰는 창 호출:\n  " + string.Join("\n  ", bad.Distinct()));
    }

    /// <summary>여는 괄호 뒤부터 짝이 맞는 닫는 괄호까지(문자열 안 괄호는 건너뜀).</summary>
    private static string Arguments(string code, int start)
    {
        int depth = 1, i = start;
        while (i < code.Length && depth > 0)
        {
            char c = code[i];
            if (c == '"')
            {
                bool verbatim = i > 0 && code[i - 1] == '@' || i > 1 && code[i - 1] == '$' && code[i - 2] == '@';
                i++;
                while (i < code.Length)
                {
                    if (!verbatim && code[i] == '\\') { i += 2; continue; }
                    if (code[i] == '"')
                    {
                        if (verbatim && i + 1 < code.Length && code[i + 1] == '"') { i += 2; continue; }
                        break;
                    }
                    i++;
                }
            }
            else if (c == '(') depth++;
            else if (c == ')') depth--;
            i++;
        }
        return code[start..Math.Max(start, i - 1)];
    }

    /// <summary>(이름, 원본 픽스처, 망가뜨리는 방법, 줄 암호 — null 이면 없음)</summary>
    public static TheoryData<string, string, string, string?> Broken => new()
    {
        { "trunc_half.zip", "zip_default", "half", null },
        { "deflate_broken.zip", "zip_default", "xor200", null },
        { "garbage.zip", "zip_default", "garbage", null },
        { "trunc_half.7z", "7z_default", "half", null },
        { "data_broken.7z", "7z_default", "xor100", null },
        { "trunc_half.tar", "tar_default", "half", null },
        { "trunc_half.tar.gz", "targz_default", "half", null },
        { "trunc_half.gz", "gz_single", "half", null },
        { "trunc_half.bz2", "bz2_single_python", "half", null },
        { "trunc_half.lzh", "lzh_default", "half", null },
        { "trunc_half.zipx", "zipx_default", "half", null },
        { "garbage.rar", "zip_default", "garbage", null },
        { "garbage.egg", "zip_default", "garbage", null },
        { "aes_no_pw.zip", "zip_aes256", "none", null },
        { "aes_wrong_pw.zip", "zip_aes256", "none", "틀린암호" },
        { "zipcrypto_wrong_pw.zip", "zip_zipcrypto", "none", "틀린암호" },
        { "header_no_pw.7z", "7z_header_encrypted", "none", null },
        { "header_wrong_pw.7z", "7z_header_encrypted", "none", "틀린암호" },
    };

    /// <summary>
    /// 계약(실제): 손상·잘림·암호 압축을 풀다 난 예외의 화면 문구는 한글이 있고 엔진 영어 원문('CRC'·'Data error'·'Unexpected end'·'Headers Error'·'Can not open'·'Exception')이 없다.
    /// 창 없이 풀기 한 줄(재사용장치)도 같다.
    /// </summary>
    [Theory]
    [MemberData(nameof(Broken))]
    public async Task 손상_잘림_암호_오류의_화면_문구는_한국어(string name, string sourceId, string how, string? password)
    {
        using var tmp = new TempDir("uiaudit");
        string arc = tmp.P(name);
        var src = File.ReadAllBytes(FixtureCatalog.Get(sourceId).FullPath);
        File.WriteAllBytes(arc, how == "none" ? src : ArchiveTesterTests.Damage(src, how));
        var ex = await Svc.Catch(() => Svc.For(arc).ExtractAsync(arc, tmp.P("out"), password));
        Assert.True(ex is not null, $"{name}: 예외 없이 끝남");
        AssertShownIsKorean(ex!, name, arc);
    }

    /// <summary>계약(실제): 조각이 빠진 분할 압축(7z·숫자 분할 zip·.z01 분할 zip)도 화면 문구는 한국어(빠진 조각 이름).</summary>
    [Theory]
    [InlineData("7z_split")]
    [InlineData("zip_numbered_split")]
    [InlineData("zip_split")]
    public async Task 조각_빠짐_오류의_화면_문구는_한국어(string id)
    {
        var fx = FixtureCatalog.Get(id);
        using var tmp = new TempDir("uisplit");
        // 첫 조각(또는 .zip 끝 조각)만 둔다
        string keep = fx.SplitVolumes.FirstOrDefault(v => v.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)) ?? fx.SplitVolumes[0];
        string arc = tmp.P(Path.GetFileName(keep));
        File.Copy(FixtureCatalog.PathOf(keep), arc);
        var ex = await Svc.Catch(() => Svc.For(arc).ExtractAsync(arc, tmp.P("out")));
        Assert.True(ex is not null, $"{id}: 조각이 빠졌는데 예외 없이 끝남");
        AssertShownIsKorean(ex!, id, arc);
    }

    private static void AssertShownIsKorean(Exception ex, string context, string archivePath)
    {
        var p = ErrorCatalog.Describe(ex, archivePath);
        string shown = (p with { Heading = "압축을 풀지 못했어요." }).ToDisplayText();
        Assert.True(ErrorCatalog.ContainsHangul(p.Message), $"{context}: 문구에 한글 없음 — {p.Message} ({ex.GetType().FullName}: {ex.Message})");
        foreach (var word in EngineEnglish)
        {
            Assert.True(!shown.Contains(word, StringComparison.Ordinal), $"{context}: 화면에 '{word}' — {shown}");
            Assert.True(!ErrorCatalog.HeadlessMessage(ex).Contains(word, StringComparison.Ordinal), $"{context}: 창 없이 풀기 문구에 '{word}'");
        }
        Assert.Equal(p.Message, ErrorMessages.ToUserMessage(ex));
    }
}
