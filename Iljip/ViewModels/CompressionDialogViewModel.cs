using System.Globalization;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using Iljip.Models;
using Iljip.Services;
using Iljip.Views;

namespace Iljip.ViewModels;

/// <summary>형식 칸의 한 줄.</summary>
public sealed record FormatChoice(CompressionFormat Format, string Label);

/// <summary>날짜 모양 칸의 한 줄(오늘 날짜를 보기로 보여 줌).</summary>
public sealed record DateChoice(DateNameFormat Format, string Label);

/// <summary>
/// 한 화면 압축 창(<see cref="CompressionOptionsDialog"/>, W2-07 — I2-12)의 보이기 규칙과 값 — 순수 VM 이라 창을 띄우지 않고 시험한다.
/// 위에서 아래로: 이름·위치 → 형식 → 수준 → 암호(ZIP 방식·7z 이름 숨기기) → 나누기 → 각각 따로·날짜·쓸모없는 파일 빼기·시험·원본 지우기 → 만들 파일 이름 미리 보기.
/// 규칙: 7z 전용 칸은 7z 일 때만, 나누기는 ZIP·7Z 만, 암호는 TAR·GZ·BZ2 에서 끔(칸 모양은 <see cref="CompressionOptionsRules"/> 와 같음),
/// 날짜·각각 따로·이름을 바꾸면 미리 보기가 바뀜, 7Z 는 7z.dll 을 쓸 수 있을 때만 형식 목록에 있음(<see cref="CompressionDialogRequest.AvailableFormats"/>).
/// 처음 값은 설정(compress.*)이고, 암호는 이 VM 에 두지 않는다(창의 암호 칸이 직접 들고 있다가 [확인]에서 옵션으로만 넘김 — 설정에 저장될 길이 없게).
/// </summary>
public sealed partial class CompressionDialogViewModel : ObservableObject
{
    private readonly CompressionDialogRequest _request;
    private readonly DateTime _localNow;
    private bool _nameEdited;
    private bool _settingName;

    public CompressionDialogViewModel(CompressionDialogRequest request, IClock clock)
    {
        _request = request;
        _localNow = clock.UtcNow.ToLocalTime();
        var d = request.Defaults ?? new CompressSettings();
        FixedExtension = request.FixedExtension;

        var formats = request.AvailableFormats.Distinct().Select(f => new FormatChoice(f, CompressionFormats.Label(f))).ToList();
        if (formats.Count == 0) formats.Add(new FormatChoice(CompressionFormat.Zip, CompressionFormats.Label(CompressionFormat.Zip)));
        Formats = formats;
        // 고정 확장자(옵션 칸만 — 예전 옵션 창 어댑터)가 있으면 그 형식, 아니면 마지막 형식(이 PC 에서 못 쓰면 첫 형식 — 7z.dll 이 막힌 PC 의 7Z 등)
        _format = CompressionFormats.FromExtension(FixedExtension) is { } fixedFormat
            ? fixedFormat
            : formats.Any(f => f.Format == d.LastFormat) ? d.LastFormat : formats[0].Format;

        _levelIndex = LevelIndexOf(d.Level);
        _useZipCrypto = d.ZipEncryption == ZipEncryption.ZipCrypto;
        _solid = d.Solid;
        _encryptFileNames = d.EncryptFileNames;
        (_splitIndex, _splitCustomText) = SplitChoiceOf(d.SplitBytes);
        _eachSeparately = d.EachSeparately && request.SourceCount > 1;
        _dateEnabled = d.DateInName != DateNameFormat.None;
        _dateFormat = d.DateInName == DateNameFormat.None ? DateNameFormat.YearMonthDay : d.DateInName;
        _excludeEnabled = d.ExcludeEnabled;
        _excludePatternsText = PatternFileFilter.ToLines(d.ExcludePatterns ?? PatternFileFilter.DefaultPatterns.ToList());
        _testAfter = d.TestAfter;
        _deleteOriginalsAfter = d.DeleteOriginalsAfter;

        // 만들 곳의 처음 값(I2-12 — W2-07 검증 지적): 설정의 '원본이 있는 폴더(기본)·마지막으로 만든 곳·정해 둔 폴더' — 그 폴더가 없으면 원본이 있는 폴더.
        // 목록에는 처음 값, 원본이 있는 폴더, 마지막으로 만든 곳·정해 둔 폴더(지금 있는 것만)를 겹치지 않게.
        _outputFolder = ShowNameAndPlace ? d.InitialFolder(request.SuggestedFolder) : request.SuggestedFolder;
        var folders = new List<string>();
        void AddFolder(string? f, bool mustExist)
        {
            if (string.IsNullOrWhiteSpace(f)) return;
            if (mustExist && !CompressSettings.IsUsableFolder(f)) return;
            string t = f.Trim();
            if (!folders.Any(x => string.Equals(x.TrimEnd('\\'), t.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase))) folders.Add(t);
        }
        AddFolder(_outputFolder, mustExist: false);
        AddFolder(request.SuggestedFolder, mustExist: false);
        AddFolder(d.LastFolder, mustExist: true);
        AddFolder(d.FixedFolder, mustExist: true);
        FolderSuggestions = folders;

        _baseName = SuggestedNameFor(_format);
        DateFormats = new[] { DateNameFormat.YearMonthDay, DateNameFormat.YearMonthDayDashed, DateNameFormat.ShortYearMonthDay }
            .Select(f => new DateChoice(f, _localNow.ToString(CompressionFormats.DatePattern(f), CultureInfo.InvariantCulture)))
            .ToList();
    }

    // ───── 값(창과 묶임) ─────

    /// <summary>형식.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Extension), nameof(PasswordEnabled), nameof(ShowZipEncryption), nameof(ShowSevenZipOptions), nameof(ShowSplit),
        nameof(Hint), nameof(CanEncryptFileNames), nameof(ExcludeAvailable), nameof(DeleteAvailable), nameof(PreviewName))]
    private CompressionFormat _format;

    /// <summary>압축 파일 이름(확장자·날짜 없이).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PreviewName))]
    private string _baseName;

    /// <summary>만들 곳(폴더).</summary>
    [ObservableProperty]
    private string _outputFolder;

    /// <summary>수준 칸 순번(<see cref="CompressionOptionsRules.LevelOf"/>).</summary>
    [ObservableProperty]
    private int _levelIndex;

    /// <summary>ZIP 암호 방식이 ZipCrypto 인가(아니면 AES-256).</summary>
    [ObservableProperty]
    private bool _useZipCrypto;

    /// <summary>7Z 솔리드.</summary>
    [ObservableProperty]
    private bool _solid;

    /// <summary>7Z 파일 이름도 암호로 숨기기.</summary>
    [ObservableProperty]
    private bool _encryptFileNames;

    /// <summary>암호 칸에 무엇이 들어 있는가(암호 자체는 두지 않음 — 창이 알려 줌).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanEncryptFileNames))]
    private bool _hasPassword;

    /// <summary>나누기 칸 순번(<see cref="CompressionOptionsRules.SplitLabels"/>).</summary>
    [ObservableProperty]
    private int _splitIndex;

    /// <summary>나누기 '직접 입력'의 MB.</summary>
    [ObservableProperty]
    private string _splitCustomText;

    /// <summary>I2-4 각각 따로 압축.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PreviewName), nameof(NameEditable))]
    private bool _eachSeparately;

    /// <summary>I3-5 이름에 날짜 붙이기.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PreviewName))]
    private bool _dateEnabled;

    /// <summary>날짜 모양.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PreviewName))]
    private DateNameFormat _dateFormat;

    /// <summary>I3-6 쓸모없는 파일 빼기.</summary>
    [ObservableProperty]
    private bool _excludeEnabled;

    /// <summary>빼기 목록(한 줄에 하나).</summary>
    [ObservableProperty]
    private string _excludePatternsText;

    /// <summary>빼기 목록 고치는 칸을 펼쳤는가.</summary>
    [ObservableProperty]
    private bool _showExcludeEditor;

    /// <summary>다 만들고 시험하기.</summary>
    [ObservableProperty]
    private bool _testAfter;

    /// <summary>I3-4 원본을 휴지통으로(시험 통과 뒤, 묻고 나서).</summary>
    [ObservableProperty]
    private bool _deleteOriginalsAfter;

    // ───── 보이기 규칙 ─────

    /// <summary>옵션 칸만 보이는 창의 고정 확장자(예전 옵션 창 어댑터). 보통 null.</summary>
    public string? FixedExtension { get; }

    /// <summary>이름·위치·형식·각각 따로·날짜·빼기·시험·원본 지우기 칸을 보이는가(옵션 칸만 보이는 창이 아니면).</summary>
    public bool ShowNameAndPlace => FixedExtension is null;

    /// <summary>만들 형식의 확장자(.zip 등).</summary>
    public string Extension => FixedExtension ?? CompressionFormats.ExtensionOf(Format);

    /// <summary>칸 모양(<see cref="CompressionOptionsRules.LayoutFor"/> — 옛 옵션 창과 같은 규칙).</summary>
    internal CompressionOptionsRules.Layout Layout => CompressionOptionsRules.LayoutFor(Extension);

    /// <summary>암호 칸을 쓸 수 있는가(ZIP·7Z).</summary>
    public bool PasswordEnabled => Layout.PasswordEnabled;

    /// <summary>ZIP 암호 방식 칸(ZIP 만).</summary>
    public bool ShowZipEncryption => Layout.ShowZipEncryption;

    /// <summary>7Z 칸(솔리드·파일 이름 숨기기 — 7Z 만).</summary>
    public bool ShowSevenZipOptions => Layout.ShowSevenZipOptions;

    /// <summary>나누기 칸(ZIP·7Z 만).</summary>
    public bool ShowSplit => Layout.ShowSplit;

    /// <summary>아래 안내 문구.</summary>
    public string Hint => Layout.Hint;

    /// <summary>'파일 이름도 암호로 숨기기'를 고를 수 있는가 — 7Z 이고 암호를 넣었을 때만.</summary>
    public bool CanEncryptFileNames => CompressionOptionsRules.CanEncryptFileNames(Extension, HasPassword ? "*" : null);

    /// <summary>원본 수.</summary>
    public int SourceCount => _request.SourceCount;

    /// <summary>'각각 따로'를 고를 수 있는가(원본이 둘 이상).</summary>
    public bool CanEachSeparately => ShowNameAndPlace && SourceCount > 1;

    /// <summary>이름 칸을 고칠 수 있는가(각각 따로면 이름은 원본마다 정해짐).</summary>
    public bool NameEditable => !(EachSeparately && CanEachSeparately);

    /// <summary>쓸모없는 파일 빼기를 쓰는 형식인가(ZIP·7Z·TAR — GZ·BZ2 는 고른 파일 하나뿐).</summary>
    public bool ExcludeAvailable => CompressionFormats.SupportsExclude(Format);

    /// <summary>원본 지우기를 쓰는 형식인가(ZIP·7Z·TAR).</summary>
    public bool DeleteAvailable => CompressionFormats.SupportsDeleteOriginals(Format);

    /// <summary>형식 칸 목록(7Z 는 7z.dll 을 쓸 수 있을 때만).</summary>
    public IReadOnlyList<FormatChoice> Formats { get; }

    /// <summary>날짜 모양 목록(오늘 날짜를 보기로).</summary>
    public IReadOnlyList<DateChoice> DateFormats { get; }

    /// <summary>위치 칸 목록: 원본이 있는 폴더, 마지막으로 만든 곳(있으면).</summary>
    public IReadOnlyList<string> FolderSuggestions { get; }

    /// <summary>
    /// 만들 파일 이름 미리 보기: '보고서_20261013.zip', 각각 따로면 '보고서_20261013.zip 외 2개 (원본마다 하나씩)'.
    /// 같은 이름이 있으면 실제로는 '이름 (2)' 로 비켜 만든다(창이 [확인]에서 묻거나 흐름이 비킴).
    /// </summary>
    public string PreviewName
    {
        get
        {
            if (!ShowNameAndPlace) return string.Empty;
            var date = DateEnabled ? DateFormat : DateNameFormat.None;
            if (!NameEditable && _request.Sources.Count > 0)
            {
                string first = CompressNaming.FileNameFor(CompressNaming.BaseNameFor(_request.Sources[0], Format), Format, date, _localNow);
                return $"{first} 외 {SourceCount - 1}개 (원본마다 하나씩)";
            }
            return CompressNaming.FileNameFor(CleanBaseName(), Format, date, _localNow);
        }
    }

    // ───── 동작 ─────

    partial void OnFormatChanged(CompressionFormat value)
    {
        // 이름을 고치지 않았으면 형식에 맞는 제안으로(GZ·BZ2 는 '보고서.xlsx' 처럼 확장자까지 — 풀면 원래 이름이 나오게)
        if (_nameEdited) return;
        _settingName = true;
        try { BaseName = SuggestedNameFor(value); }
        finally { _settingName = false; }
    }

    partial void OnBaseNameChanged(string value)
    {
        if (!_settingName) _nameEdited = true;
    }

    /// <summary>빼기 목록을 기본으로 되돌린다.</summary>
    public void ResetExcludePatterns() => ExcludePatternsText = PatternFileFilter.ToLines(PatternFileFilter.DefaultPatterns);

    /// <summary>[확인] 전에 볼 것 — 문제가 있으면 쉬운 한국어 안내, 없으면 null(암호 두 칸·나누기 크기는 창이 따로 봄).</summary>
    public string? Validate()
    {
        if (!ShowNameAndPlace) return null;
        if (NameEditable && CompressNaming.CheckBaseName(CleanBaseName()) is { } badName) return badName;

        string folder = (OutputFolder ?? string.Empty).Trim();
        if (folder.Length == 0) return "만들 곳(폴더)을 골라 주세요.";
        bool valid;
        try
        {
            valid = folder.IndexOfAny(Path.GetInvalidPathChars()) < 0 && Path.IsPathFullyQualified(folder);
        }
        catch
        {
            valid = false;
        }
        if (!valid) return "만들 곳(폴더) 경로가 올바르지 않아요. [찾아보기]로 골라 주세요.";
        if (!Directory.Exists(folder)) return $"‘{folder}’ 폴더가 없어요. [찾아보기]로 골라 주세요.";

        if (CompressionFormats.IsSingleFile(Format))
        {
            if (!_request.AllSourcesAreFiles)
                return "GZ·BZ2 는 파일만 압축할 수 있어요. 폴더가 있으면 ZIP·7Z·TAR 를 골라 주세요.";
            if (SourceCount > 1 && NameEditable)
                return "GZ·BZ2 는 파일 하나만 담을 수 있어요. ‘각각 따로 압축’을 켜거나 ZIP·7Z 를 골라 주세요.";
        }
        return null;
    }

    /// <summary>하나로 만들 때 그 이름(또는 첫 조각)이 이미 있는가 — 창이 '바꿀까요?'를 묻는다(각각 따로는 흐름이 '이름 (2)' 로 비킴).</summary>
    public bool TargetExists(bool split) =>
        ShowNameAndPlace && NameEditable && CompressNaming.IsTaken(OutputFolder.Trim(), StemWithDate(), Format, split && ShowSplit);

    /// <summary>같은 이름이 있을 때 비켜 만들 이름(예: 보고서 (2).zip).</summary>
    public string AlternativeName(bool split) =>
        Path.GetFileName(CompressNaming.UniqueTarget(OutputFolder.Trim(), StemWithDate(), Format, split && ShowSplit));

    /// <summary>
    /// 만들 압축 파일이 압축하려는 원본 그 자체인가(예: 'a.zip' 하나를 같은 폴더에 ZIP 으로 — 제안 이름도 a.zip). 그러면 [바꾸기]를 주지 않는다 —
    /// 바꾸면 원본이 빈 압축으로 덮여 내용이 사라지므로(W2-07 검증 지적). 창은 [새 이름으로]·[취소]만 묻고, 흐름도 이 경우는 늘 비켜 만든다.
    /// </summary>
    public bool TargetIsSource
    {
        get
        {
            if (!ShowNameAndPlace || !NameEditable) return false;
            string folder = (OutputFolder ?? string.Empty).Trim();
            if (folder.Length == 0) return false;
            string target;
            try { target = Path.Combine(folder, StemWithDate() + Extension); }
            catch { return false; }
            return _request.Sources.Any(s => CompressFlow.SamePath(s, target));
        }
    }

    /// <summary>같은 이름이 있을 때 묻는 문구(원본 그 자체면 바꿀 수 없다는 안내).</summary>
    public string ExistingTargetMessage => TargetIsSource
        ? $"‘{PreviewName}’은(는) 지금 압축하려는 원본이에요. 같은 이름으로는 만들 수 없어요."
        : $"‘{PreviewName}’ 파일이 이미 있어요. 바꿀까요?";

    /// <summary>
    /// 창의 답을 만든다. <paramref name="options"/> 는 창이 <see cref="CompressionOptionsRules.Build"/> 로 만든 엔진 옵션(암호 포함 — 이 VM 에는 두지 않음).
    /// 형식에 맞지 않는 값은 버린다(GZ·BZ2 의 빼기·원본 지우기, 원본 하나의 각각 따로).
    /// </summary>
    public CompressionRequest BuildRequest(CompressionOptions options, bool overwriteExisting) => new()
    {
        Sources = _request.Sources,
        OutputFolder = (OutputFolder ?? string.Empty).Trim(),
        BaseName = CleanBaseName(),
        Format = Format,
        Options = options,
        EachSeparately = EachSeparately && CanEachSeparately,
        DateInName = DateEnabled ? DateFormat : DateNameFormat.None,
        ExcludeEnabled = ExcludeAvailable && ExcludeEnabled,
        ExcludePatterns = PatternFileFilter.ParseLines(ExcludePatternsText),
        TestAfter = TestAfter,
        DeleteOriginalsAfter = DeleteAvailable && DeleteOriginalsAfter,
        OverwriteExisting = overwriteExisting && !TargetIsSource,   // 원본 그 자체는 바꾸지 않는다
    };

    // ───── 도우미 ─────

    /// <summary>이름 칸 값 — 앞뒤 공백을 빼고, 형식 확장자를 직접 쳐 넣었으면 뗀다('보고서.zip' → '보고서').</summary>
    private string CleanBaseName()
    {
        string name = (BaseName ?? string.Empty).Trim();
        string ext = Extension;
        if (name.Length > ext.Length && name.EndsWith(ext, StringComparison.OrdinalIgnoreCase)) name = name[..^ext.Length].TrimEnd();
        return name;
    }

    private string StemWithDate() => CompressNaming.WithDate(CleanBaseName(), DateEnabled ? DateFormat : DateNameFormat.None, _localNow, Format);

    private string SuggestedNameFor(CompressionFormat format) =>
        _request.Sources.Count == 1 ? CompressNaming.BaseNameFor(_request.Sources[0], format)
        : !string.IsNullOrWhiteSpace(_request.SuggestedBaseName) ? _request.SuggestedBaseName
        : CompressNaming.SuggestBaseName(_request.Sources, format);

    private static int LevelIndexOf(CompressionLevelOption level) => level switch
    {
        CompressionLevelOption.Store => 0,
        CompressionLevelOption.Fastest => 1,
        CompressionLevelOption.Maximum => 3,
        _ => 2,
    };

    /// <summary>기억한 나누기 크기 → 칸 순번·직접 입력 MB.</summary>
    private static (int Index, string Custom) SplitChoiceOf(long? bytes)
    {
        if (bytes is not > 0) return (0, "10");
        for (int i = 0; i < CompressionOptionsRules.SplitPresetBytes.Length; i++)
            if (CompressionOptionsRules.SplitPresetBytes[i] == bytes) return (i, "10");
        decimal mb = (decimal)bytes.Value / CompressionOptionsRules.MegaByte;
        return (CompressionOptionsRules.SplitCustomIndex, mb.ToString("0.##", CultureInfo.InvariantCulture));
    }
}
