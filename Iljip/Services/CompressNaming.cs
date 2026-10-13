using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using Iljip.Models;

namespace Iljip.Services;

/// <summary>
/// 만들 압축 파일 이름 규칙(W2-07): 제안 이름, 각각 따로의 원본별 이름(I2-4), 이름 뒤 날짜(I3-5), 같은 이름이 있을 때 '이름 (2)'.
/// 순수 함수(파일은 있는지만 본다) — 한 화면 압축 창의 미리 보기와 <see cref="CompressFlow"/> 가 같이 쓴다.
/// </summary>
public static class CompressNaming
{
    /// <summary>이름을 정할 수 없을 때(드라이브 뿌리 등) — 예전 저장 창 제안과 같은 값.</summary>
    public const string FallbackName = "archive";

    /// <summary>이름 칸에 못 쓰는 글자 안내.</summary>
    public const string InvalidCharsMessage = "이름에 쓸 수 없는 글자가 있어요: \\ / : * ? \" < > |";

    public const string EmptyNameMessage = "압축 파일 이름을 넣어 주세요.";

    /// <summary>파일 이름에서 못 쓰는 글자를 빼고 끝의 점·공백을 지운다. 비면 <see cref="FallbackName"/>.</summary>
    public static string Sanitize(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return FallbackName;
        foreach (char c in Path.GetInvalidFileNameChars())
            name = name.Replace(c.ToString(), string.Empty);
        // Windows 는 끝에 마침표·공백을 허용하지 않음
        name = name.Trim().TrimEnd('.').Trim();
        return string.IsNullOrWhiteSpace(name) ? FallbackName : name;
    }

    /// <summary>
    /// 원본 하나 → 압축 파일 이름(확장자·날짜 없이). 파일 '보고서.xlsx' → '보고서'(확장자를 뗌), 폴더 '자료.v2' → '자료.v2'(폴더 이름 그대로).
    /// GZ·BZ2 는 파일 이름 그대로('보고서.xlsx' → '보고서.xlsx.gz' — 풀면 원래 이름이 나오게).
    /// 확장자만 있는 파일(.gitignore)·드라이브 뿌리(C:\) 에서도 비정상 이름이 나오지 않게 단계적으로 대신한다.
    /// </summary>
    public static string BaseNameFor(string sourcePath, CompressionFormat format = CompressionFormat.Zip)
    {
        string trimmed = (sourcePath ?? string.Empty).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        string name;
        if (Directory.Exists(sourcePath) || CompressionFormats.IsSingleFile(format))
            name = Path.GetFileName(trimmed);
        else
        {
            // 1) 확장자를 뗀 파일명  2) 비면(점으로 시작하는 파일 등) 확장자 포함 파일명
            name = Path.GetFileNameWithoutExtension(trimmed);
            if (string.IsNullOrWhiteSpace(name)) name = Path.GetFileName(trimmed);
        }
        // 3) 그래도 비면(드라이브 뿌리·구분자로 끝나는 경로) 디렉터리명
        if (string.IsNullOrWhiteSpace(name))
        {
            try { name = new DirectoryInfo(sourcePath ?? string.Empty).Name; } catch { /* 잘못된 경로 */ }
        }
        return Sanitize(name);
    }

    /// <summary>
    /// 한 화면 압축 창의 제안 이름: 원본 하나면 <see cref="BaseNameFor"/>, 여럿이면 첫 원본이 있는 폴더 이름(예전 저장 창 제안과 같음).
    /// </summary>
    public static string SuggestBaseName(IReadOnlyList<string> sources, CompressionFormat format = CompressionFormat.Zip)
    {
        if (sources.Count == 0) return FallbackName;
        if (sources.Count == 1) return BaseNameFor(sources[0], format);
        string dir = Path.GetDirectoryName(sources[0]) ?? string.Empty;
        try { return Sanitize(new DirectoryInfo(string.IsNullOrEmpty(dir) ? Environment.CurrentDirectory : dir).Name); }
        catch { return FallbackName; }
    }

    /// <summary>
    /// 이름 뒤에 '_날짜'(I3-5, 예: 보고서 → 보고서_20261013). 이미 이름에 같은 날(세 모양 중 어느 것이든)이 있으면 두 번 붙이지 않는다.
    /// GZ·BZ2(<paramref name="archiveFormat"/>)는 이름에 원래 확장자가 들어 있으므로('보고서.xlsx') 날짜를 그 확장자 <b>앞</b>에 넣는다
    /// ('보고서_20261013.xlsx' → 보고서_20261013.xlsx.gz) — 풀었을 때 '보고서.xlsx_20261013' 처럼 확장자가 깨져 엑셀로 안 열리는 일이 없게(W2-07 검증 지적).
    /// </summary>
    public static string WithDate(string baseName, DateNameFormat format, DateTime localNow, CompressionFormat archiveFormat = CompressionFormat.Zip)
    {
        if (format == DateNameFormat.None) return baseName;
        if (ContainsDate(baseName, localNow)) return baseName;
        string stamp = localNow.ToString(CompressionFormats.DatePattern(format), CultureInfo.InvariantCulture);
        if (CompressionFormats.IsSingleFile(archiveFormat) && SplitInnerExtension(baseName) is { } parts)
            return Append(parts.Stem, stamp) + parts.Extension;
        return Append(baseName, stamp);

        static string Append(string name, string stamp)
        {
            string stem = name.TrimEnd('_', '-', ' ');
            return stem.Length == 0 ? stamp : $"{stem}_{stamp}";
        }
    }

    /// <summary>
    /// GZ·BZ2 이름의 원래 확장자 나누기: '보고서.xlsx' → ('보고서', '.xlsx'), 'backup.tar' → ('backup', '.tar').
    /// 확장자로 보는 것은 점 뒤 1~10글자의 영문·숫자뿐(공백·한글이 든 '보고서 v1.2 최종' 의 '.2 최종' 은 확장자가 아님). 아니면 null(이름 끝에 붙임).
    /// </summary>
    private static (string Stem, string Extension)? SplitInnerExtension(string name)
    {
        int dot = name.LastIndexOf('.');
        if (dot <= 0 || dot == name.Length - 1) return null;
        string ext = name[dot..];
        if (ext.Length > 11 || !ext.Skip(1).All(char.IsAsciiLetterOrDigit)) return null;
        string stem = name[..dot];
        return string.IsNullOrWhiteSpace(stem.TrimEnd('_', '-', ' ')) ? null : (stem, ext);
    }

    /// <summary>이름에 그날 날짜(yyyyMMdd·yyyy-MM-dd·yyMMdd 중 하나)가 숫자에 붙지 않은 채로 들어 있는가.</summary>
    public static bool ContainsDate(string baseName, DateTime localNow)
    {
        foreach (var f in new[] { DateNameFormat.YearMonthDay, DateNameFormat.YearMonthDayDashed, DateNameFormat.ShortYearMonthDay })
        {
            string stamp = localNow.ToString(CompressionFormats.DatePattern(f), CultureInfo.InvariantCulture);
            if (Regex.IsMatch(baseName ?? string.Empty, $"(?<![0-9]){Regex.Escape(stamp)}(?![0-9])", RegexOptions.CultureInvariant)) return true;
        }
        return false;
    }

    /// <summary>날짜까지 붙인 압축 파일 이름(확장자 포함, 예: 보고서_20261013.zip, GZ 는 보고서_20261013.xlsx.gz).</summary>
    public static string FileNameFor(string baseName, CompressionFormat format, DateNameFormat date, DateTime localNow) =>
        WithDate(baseName, date, localNow, format) + CompressionFormats.ExtensionOf(format);

    /// <summary>이름 칸 검사 — 비었거나 못 쓰는 글자가 있으면 안내, 괜찮으면 null.</summary>
    public static string? CheckBaseName(string? baseName)
    {
        if (string.IsNullOrWhiteSpace(baseName) || string.IsNullOrWhiteSpace(baseName.Trim().TrimEnd('.'))) return EmptyNameMessage;
        if (baseName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0) return InvalidCharsMessage;
        return null;
    }

    /// <summary>
    /// <paramref name="folder"/> 안에 새로 만들 압축 파일 경로: '이름.확장자'가 (나눠 만들면 첫 조각 '이름.z01'·'이름.7z.001' 도) 없으면 그대로,
    /// 있으면 '이름 (2).확장자', '이름 (3).확장자' …(번호가 (2)부터인 것은 탐색기·반디집 관례 — 세트 이름(W2-03)과 같음).
    /// </summary>
    public static string UniqueTarget(string folder, string baseNameWithDate, CompressionFormat format, bool split)
    {
        // GZ·BZ2 는 번호도 원래 확장자 앞에('보고서 (2).xlsx.gz' — 풀면 '보고서 (2).xlsx', 날짜와 같은 까닭)
        var inner = CompressionFormats.IsSingleFile(format) ? SplitInnerExtension(baseNameWithDate) : null;
        for (int n = 1; n < 10_000; n++)
        {
            string stem = n == 1 ? baseNameWithDate
                : inner is { } p ? $"{p.Stem} ({n}){p.Extension}"
                : $"{baseNameWithDate} ({n})";
            if (!IsTaken(folder, stem, format, split)) return Path.Combine(folder, stem + CompressionFormats.ExtensionOf(format));
        }
        return Path.Combine(folder, $"{baseNameWithDate} ({Guid.NewGuid():N}){CompressionFormats.ExtensionOf(format)}");
    }

    /// <summary>그 이름(또는 그 이름의 첫 조각)이 이미 있는가.</summary>
    internal static bool IsTaken(string folder, string stem, CompressionFormat format, bool split)
    {
        string ext = CompressionFormats.ExtensionOf(format);
        if (Exists(Path.Combine(folder, stem + ext))) return true;
        if (!split) return false;
        return format switch
        {
            CompressionFormat.Zip => Exists(Path.Combine(folder, stem + ".z01")),
            CompressionFormat.SevenZip => Exists(Path.Combine(folder, stem + ext + ".001")),
            _ => false,
        };

        static bool Exists(string p) => File.Exists(p) || Directory.Exists(p);
    }
}
