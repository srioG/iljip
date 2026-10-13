using Iljip.Services;

namespace Iljip.Models;

// 한 화면 압축 창의 요청 모델(W2-07 — I2-4·I3-4·I3-5·I3-6·I2-12). CompressionOptions 는 엔진 옵션(수준·암호·방식·솔리드·이름 암호화·나누기)으로 두고,
// 화면과 흐름 옵션(이름·위치·형식·각각 따로·날짜·빼기·시험·원본 지우기)은 이 모델로 나눈다. 다른 묶음(W2-08·W2-10 등)은 필드만 더한다.

/// <summary>만들 압축 형식. 7Z 는 7z.dll 을 쓸 수 있을 때만 고를 수 있다(<see cref="CompressionDialogRequest.AvailableFormats"/>).</summary>
public enum CompressionFormat
{
    Zip,
    SevenZip,
    Tar,
    /// <summary>GZ — 파일 하나만(폴더·여러 개는 '각각 따로'로 파일마다).</summary>
    GZip,
    /// <summary>BZ2 — 파일 하나만.</summary>
    BZip2,

    /// <summary>
    /// 자동 풀림 EXE(W2-10, I3-1) — 받는 사람에게 프로그램이 없어도 두 번 눌러 풀리는 exe(일집 자체 스텁 + ZIP). ZIP 과 같은 수준·암호 방식, 나누기는 없음.
    /// 스텁(sfx\Iljip.SfxStub.exe)이 있을 때만 고를 수 있다. 회사 메일은 exe 첨부를 막는 경우가 많아 창이 늘 안내한다.
    /// </summary>
    Sfx,
}

/// <summary>압축 파일 이름 뒤에 붙일 날짜 모양(I3-5). 예: 보고서_20261013.zip.</summary>
public enum DateNameFormat
{
    /// <summary>붙이지 않음(기본).</summary>
    None,
    /// <summary>yyyyMMdd(20261013).</summary>
    YearMonthDay,
    /// <summary>yyyy-MM-dd(2026-10-13).</summary>
    YearMonthDayDashed,
    /// <summary>yyMMdd(261013).</summary>
    ShortYearMonthDay,
}

/// <summary>
/// 압축 창 '만들 곳'의 처음 값(I2-12 — 반디집 환경 설정의 기본 압축 폴더와 같은 뜻, W2-07 검증 지적). ☰ ▸ 설정에서 고른다.
/// 고른 폴더가 없으면(지워짐·USB 를 뺌·아직 압축한 적 없음) 원본이 있는 폴더로 연다.
/// </summary>
public enum CompressFolderChoice
{
    /// <summary>원본이 있는 폴더(기본 — 예전과 같음).</summary>
    SourceFolder,
    /// <summary>마지막으로 만든 곳(compress.lastFolder).</summary>
    LastFolder,
    /// <summary>정해 둔 폴더(compress.fixedFolder).</summary>
    FixedFolder,
}

/// <summary>형식별 규칙(확장자·화면 글자·할 수 있는 것).</summary>
public static class CompressionFormats
{
    public static string ExtensionOf(CompressionFormat format) => format switch
    {
        CompressionFormat.SevenZip => ".7z",
        CompressionFormat.Tar => ".tar",
        CompressionFormat.GZip => ".gz",
        CompressionFormat.BZip2 => ".bz2",
        CompressionFormat.Sfx => ".exe",
        _ => ".zip",
    };

    /// <summary>확장자(점 포함, 대소문자 무시) → 형식. 모르면 null.</summary>
    public static CompressionFormat? FromExtension(string? extension) => (extension ?? string.Empty).ToLowerInvariant() switch
    {
        ".zip" => CompressionFormat.Zip,
        ".7z" => CompressionFormat.SevenZip,
        ".tar" => CompressionFormat.Tar,
        ".gz" => CompressionFormat.GZip,
        ".bz2" => CompressionFormat.BZip2,
        ".exe" => CompressionFormat.Sfx,
        _ => null,
    };

    /// <summary>형식 칸의 글자.</summary>
    public static string Label(CompressionFormat format) => format switch
    {
        CompressionFormat.SevenZip => "7Z (더 작게 · 7-Zip·반디집)",
        CompressionFormat.Tar => "TAR (묶기만 · 압축 안 함)",
        CompressionFormat.GZip => "GZ (파일 하나씩)",
        CompressionFormat.BZip2 => "BZ2 (파일 하나씩)",
        CompressionFormat.Sfx => "자동 풀림 EXE (프로그램 없이 풀림 · 메일로는 막힐 수 있음)",
        _ => "ZIP (어디서나 열림 · 권장)",
    };

    /// <summary>파일 하나만 담는 형식(GZ·BZ2).</summary>
    public static bool IsSingleFile(CompressionFormat format) => format is CompressionFormat.GZip or CompressionFormat.BZip2;

    /// <summary>암호를 걸 수 있는가(ZIP·7Z·자동 풀림 EXE — 자동 풀림은 ZIP 과 같은 AES-256·ZipCrypto).</summary>
    public static bool SupportsPassword(CompressionFormat format) => format is CompressionFormat.Zip or CompressionFormat.SevenZip or CompressionFormat.Sfx;

    /// <summary>나눠 만들 수 있는가(ZIP·7Z — W2-03. 자동 풀림 EXE 는 한 파일이어야 풀리므로 안 됨).</summary>
    public static bool SupportsSplit(CompressionFormat format) => format is CompressionFormat.Zip or CompressionFormat.SevenZip;

    /// <summary>
    /// 압축한 뒤 원본을 휴지통으로 옮길 수 있는가(I3-4) — 목록으로 이름·크기를 맞춰 볼 수 있는 ZIP·7Z·TAR(·자동 풀림 EXE — 안은 ZIP, W2-10) 만.
    /// GZ·BZ2 는 목록에 크기가 없어 원본과 맞춰 볼 수 없으므로 하지 않는다.
    /// </summary>
    public static bool SupportsDeleteOriginals(CompressionFormat format) =>
        format is CompressionFormat.Zip or CompressionFormat.SevenZip or CompressionFormat.Tar or CompressionFormat.Sfx;

    /// <summary>쓸모없는 파일 빼기를 쓰는가(폴더를 담는 형식 — ZIP·7Z·TAR).</summary>
    public static bool SupportsExclude(CompressionFormat format) => !IsSingleFile(format);

    /// <summary>날짜 모양의 .NET 형식 문자열(None 이면 빈 문자열).</summary>
    public static string DatePattern(DateNameFormat format) => format switch
    {
        DateNameFormat.YearMonthDay => "yyyyMMdd",
        DateNameFormat.YearMonthDayDashed => "yyyy-MM-dd",
        DateNameFormat.ShortYearMonthDay => "yyMMdd",
        _ => string.Empty,
    };
}

/// <summary>
/// 한 화면 압축 창의 답(I2-12). <see cref="CompressFlow"/> 가 이대로 만든다.
/// 이름은 <see cref="OutputFolder"/>\<see cref="BaseName"/>(+ '_날짜')(+ 형식 확장자). '각각 따로'면 원본마다 '원본 이름(+ _날짜).확장자'.
/// </summary>
public sealed class CompressionRequest
{
    /// <summary>압축할 원본(파일·폴더 경로).</summary>
    public IReadOnlyList<string> Sources { get; init; } = Array.Empty<string>();

    /// <summary>만들 곳(폴더).</summary>
    public string OutputFolder { get; init; } = string.Empty;

    /// <summary>압축 파일 이름(확장자·날짜 없이). '각각 따로'면 쓰지 않는다.</summary>
    public string BaseName { get; init; } = string.Empty;

    public CompressionFormat Format { get; init; } = CompressionFormat.Zip;

    /// <summary>엔진 옵션(수준·암호·ZIP 방식·7z 솔리드·이름 암호화·나누기). 빼기 목록은 흐름이 <see cref="ExcludePatterns"/> 로 채운다.</summary>
    public CompressionOptions Options { get; init; } = CompressionOptions.Default;

    /// <summary>I2-4: 고른 파일·폴더마다 따로 압축(거래처별).</summary>
    public bool EachSeparately { get; init; }

    /// <summary>I3-5: 이름 뒤에 날짜.</summary>
    public DateNameFormat DateInName { get; init; } = DateNameFormat.None;

    /// <summary>I3-6: 쓸모없는 파일 빼기를 쓰는가(끄면 아무것도 빼지 않음). 이 요청을 바로 만든 쪽이 정하지 않으면 끔(다 담음).</summary>
    public bool ExcludeEnabled { get; init; }

    /// <summary>I3-6: 빼기 목록(<see cref="PatternFileFilter"/> 모양).</summary>
    public IReadOnlyList<string> ExcludePatterns { get; init; } = Array.Empty<string>();

    /// <summary>다 만든 뒤 압축 파일을 시험(모든 항목을 풀어 확인값 확인 — W2-08 이 키움).</summary>
    public bool TestAfter { get; init; }

    /// <summary>I3-4: 시험을 통과하면 원본을 휴지통으로(묻고 나서). 기본 꺼짐.</summary>
    public bool DeleteOriginalsAfter { get; init; }

    /// <summary>
    /// 같은 이름의 압축 파일이 이미 있을 때 바꾸기로 했는가(창이 '바꿀까요?'를 물어 [바꾸기]를 고름 — 예전 저장 창의 덮어쓰기 확인과 같음).
    /// false 면 '이름 (2)' 로 비켜 만든다. '각각 따로'는 쓰지 않는다(늘 비켜 만듦 — 같은 이름 처리기).
    /// </summary>
    public bool OverwriteExisting { get; init; }
}

/// <summary>한 화면 압축 창에 넘기는 것(설정 기본값·쓸 수 있는 형식·제안 이름·원본).</summary>
public sealed class CompressionDialogRequest
{
    /// <summary>압축할 원본.</summary>
    public IReadOnlyList<string> Sources { get; init; } = Array.Empty<string>();

    /// <summary>원본 수.</summary>
    public int SourceCount => Sources.Count;

    /// <summary>원본이 모두 파일인가(GZ·BZ2 를 고를 수 있는지 판단).</summary>
    public bool AllSourcesAreFiles { get; init; }

    /// <summary>제안 위치(보통 첫 원본이 있는 폴더).</summary>
    public string SuggestedFolder { get; init; } = string.Empty;

    /// <summary>제안 이름(확장자 없이 — 원본 하나면 그 이름, 여럿이면 원본이 있는 폴더 이름).</summary>
    public string SuggestedBaseName { get; init; } = string.Empty;

    /// <summary>쓸 수 있는 형식(7Z 는 7z.dll 을 쓸 수 있을 때만).</summary>
    public IReadOnlyList<CompressionFormat> AvailableFormats { get; init; } = new[] { CompressionFormat.Zip };

    /// <summary>설정(compress.*) — 창의 처음 값. 창은 이것을 고치지 않는다(저장은 부르는 쪽이 [확인] 뒤 한 번).</summary>
    public CompressSettings Defaults { get; init; } = new();

    /// <summary>
    /// 옵션 칸만 보이는 창(예전 <see cref="IUserInteraction.AskCompressionOptions"/> 어댑터) — 이 확장자의 형식으로 고정하고 이름·위치·각각 따로·날짜·원본 지우기는 숨긴다. 보통 null.
    /// </summary>
    public string? FixedExtension { get; init; }
}
