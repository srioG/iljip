using System.Text.Json;

namespace Iljip.Tests.TestSupport;

/// <summary>manifest.json 의 expected_entries 한 줄. 경로는 '/' 구분·NFC·폴더 끝 '/' 없음.</summary>
public sealed record ExpectedEntry(string Path, long Size, string? Sha256, bool IsDir);

/// <summary>manifest.json 의 픽스처 한 개.</summary>
public sealed class FixtureInfo
{
    public required string Id { get; init; }
    /// <summary>Fixtures 폴더 기준 상대 경로('/' 구분). 분할이면 여는 파일.</summary>
    public required string File { get; init; }
    public required string Format { get; init; }
    public string? Password { get; init; }
    public bool HeaderEncrypted { get; init; }
    public bool CommitOk { get; init; }
    public bool BandizipMade { get; init; }
    public IReadOnlyList<string> KnownGaps { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> SplitVolumes { get; init; } = Array.Empty<string>();
    public IReadOnlyList<ExpectedEntry> Expected { get; init; } = Array.Empty<ExpectedEntry>();
    /// <summary>tar.gz 등에서 압축 한 겹만 벗긴 .tar 자체(크기·sha256). 없으면 null.</summary>
    public (long Size, string Sha256, string Name)? InnerTar { get; init; }
    /// <summary>
    /// manifest bandizip_view.shows_inner_tar_only — 반디집 7.46 이 이 파일(.tar.xz·.tbz2)을 안쪽 .tar '하나'로
    /// 보여 주고 꺼낸다(tar 내용까지 펼치지 않음). 반디집과 같게 하려면 기대값은 InnerTar 다.
    /// </summary>
    public bool BandizipShowsInnerTarOnly { get; init; }

    /// <summary>
    /// 수정 시각을 '시간대 없는 벽시계'로 저장하는 형식인가(ZIP 의 DOS 시각). 아니면 절대 시각(UTC: tar·7z·gz·NTFS 추가필드).
    /// </summary>
    public bool StoresWallClockMtime => Format is "zip" or "zipx";

    public string FullPath => System.IO.Path.Combine(FixtureCatalog.Root, File.Replace('/', System.IO.Path.DirectorySeparatorChar));
    public bool Exists => System.IO.File.Exists(FullPath);

    public IEnumerable<ExpectedEntry> ExpectedFiles => Expected.Where(e => !e.IsDir);

    public override string ToString() => Id;
}

/// <summary>
/// 픽스처 폴더 찾기 + manifest.json 읽기.
/// 1순위: 테스트 출력 폴더의 Fixtures(csproj 가 복사), 2순위: 소스 트리의 Iljip.Tests/Fixtures.
/// </summary>
public static class FixtureCatalog
{
    private static readonly Lazy<string> _root = new(FindRoot);
    private static readonly Lazy<Dictionary<string, FixtureInfo>> _all = new(Load);

    public static string Root => _root.Value;
    public static IReadOnlyDictionary<string, FixtureInfo> All => _all.Value;

    public const string DefaultPassword = "Iljip-Test-1234";
    public const string KoreanPassword = "테스트암호";

    /// <summary>
    /// make_fixtures.py 가 모든 원본에 박은 수정 시각. 픽스처를 만든 PC 가 한국 시간(UTC+9)이라
    /// '2026-01-02 03:04:06 KST' = 절대 시각 2026-01-01T18:04:06Z 다.
    /// <list type="bullet">
    /// <item>tar·7z·gz·ZIP NTFS 추가필드는 이 '절대 시각'(UTC)을 저장한다 → 어느 시간대 PC 에서든 <see cref="FixedMtimeUtc"/> 와 비교.</item>
    /// <item>ZIP 의 DOS 시각은 시간대 없는 '벽시계'(03:04:06)를 저장한다 → <see cref="FixedMtimeWallClock"/> 과 비교.
    ///   (엔진이 NTFS 추가필드를 읽으면 절대 시각이 나오므로 ZIP 은 둘 중 하나만 맞아도 통과 — <see cref="MtimeExpect"/>)</item>
    /// </list>
    /// 한국 시간 PC 에서는 두 값이 같아 구분이 안 되지만, 다른 시간대(CI 등)에서는 9시간 이상 벌어진다.
    /// </summary>
    public static readonly DateTimeOffset FixedMtime = new(2026, 1, 2, 3, 4, 6, TimeSpan.FromHours(9));
    public static DateTime FixedMtimeUtc => FixedMtime.UtcDateTime;
    public static readonly DateTime FixedMtimeWallClock = new(2026, 1, 2, 3, 4, 6, DateTimeKind.Unspecified);

    public static FixtureInfo Get(string id) =>
        All.TryGetValue(id, out var f) ? f : throw new KeyNotFoundException($"manifest.json 에 '{id}' 픽스처가 없음");

    /// <summary>Fixtures 기준 상대경로(예: "bandizip/zip_aes256.zip")로 픽스처 찾기.</summary>
    public static FixtureInfo ByFile(string relative) =>
        All.Values.SingleOrDefault(f => f.File == relative)
        ?? throw new KeyNotFoundException($"manifest.json 에 file='{relative}' 픽스처가 없음");

    public static string PathOf(string relative) =>
        Path.Combine(Root, relative.Replace('/', Path.DirectorySeparatorChar));

    private static string FindRoot()
    {
        string outDir = Path.Combine(AppContext.BaseDirectory, "Fixtures");
        if (File.Exists(Path.Combine(outDir, "manifest.json")))
            return outDir;

        // 소스 트리에서 찾기(출력 폴더 복사가 빠졌을 때 대비)
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            string cand = Path.Combine(dir.FullName, "Iljip.Tests", "Fixtures");
            if (File.Exists(Path.Combine(cand, "manifest.json"))) return cand;
            dir = dir.Parent;
        }
        const string known = @"D:\Python\일집\Iljip.Tests\Fixtures";
        if (File.Exists(Path.Combine(known, "manifest.json"))) return known;
        throw new DirectoryNotFoundException("Fixtures/manifest.json 을 찾지 못함");
    }

    private static Dictionary<string, FixtureInfo> Load()
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(Root, "manifest.json")));
        var result = new Dictionary<string, FixtureInfo>(StringComparer.Ordinal);
        foreach (var f in doc.RootElement.GetProperty("fixtures").EnumerateArray())
        {
            var info = new FixtureInfo
            {
                Id = f.GetProperty("id").GetString()!,
                File = f.GetProperty("file").GetString()!,
                Format = f.GetProperty("format").GetString()!,
                Password = Str(f, "password"),
                HeaderEncrypted = Bool(f, "header_encrypted"),
                CommitOk = Bool(f, "commit_ok"),
                BandizipMade = Bool(f, "bandizip_made"),
                KnownGaps = StrArray(f, "iljip_known_gaps"),
                SplitVolumes = StrArray(f, "split_volumes"),
                Expected = f.GetProperty("expected_entries").EnumerateArray().Select(e => new ExpectedEntry(
                    e.GetProperty("path").GetString()!,
                    e.GetProperty("size").GetInt64(),
                    Str(e, "sha256"),
                    e.GetProperty("is_dir").GetBoolean())).ToList(),
                InnerTar = f.TryGetProperty("inner_tar", out var it) && it.ValueKind == JsonValueKind.Object
                    ? (it.GetProperty("size").GetInt64(), it.GetProperty("sha256").GetString()!, it.GetProperty("name").GetString()!)
                    : null,
                BandizipShowsInnerTarOnly = f.TryGetProperty("bandizip_view", out var bv) && bv.ValueKind == JsonValueKind.Object
                    && Bool(bv, "shows_inner_tar_only")
            };
            result.Add(info.Id, info);
        }
        return result;
    }

    private static string? Str(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static bool Bool(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.True;

    private static IReadOnlyList<string> StrArray(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Array
            ? v.EnumerateArray().Select(x => x.GetString() ?? string.Empty).ToList()
            : Array.Empty<string>();
}

/// <summary>
/// 픽스처 수정 시각 비교(시간대와 무관하게). 형식이 저장하는 값의 종류에 따라 기준이 다르다 — <see cref="FixtureCatalog.FixedMtime"/> 설명 참고.
/// </summary>
public static class MtimeExpect
{
    private static readonly TimeSpan Tolerance = TimeSpan.FromSeconds(2);   // DOS 시각 2초 단위

    /// <summary>목록의 LastModified(현지·UTC·미지정 어느 Kind 든)가 기대와 맞으면 null, 아니면 실패 사유.</summary>
    public static string? CheckListed(DateTime actual, FixtureInfo fx)
    {
        // 절대 시각으로 맞는가: Kind 가 Utc 면 그대로, Local/Unspecified 면 현지 시각으로 보고 UTC 로 바꿔 비교
        DateTime asUtc = actual.Kind == DateTimeKind.Utc ? actual : DateTime.SpecifyKind(actual, DateTimeKind.Local).ToUniversalTime();
        if ((asUtc - FixtureCatalog.FixedMtimeUtc).Duration() <= Tolerance) return null;
        // 벽시계로 맞는가(ZIP DOS 시각만 허용)
        if (fx.StoresWallClockMtime && actual.Kind != DateTimeKind.Utc
            && (DateTime.SpecifyKind(actual, DateTimeKind.Unspecified) - FixtureCatalog.FixedMtimeWallClock).Duration() <= Tolerance)
            return null;
        return Describe(actual, fx);
    }

    /// <summary>풀린 파일의 수정 시각이 기대와 맞으면 null, 아니면 실패 사유.</summary>
    public static string? CheckFile(string path, FixtureInfo fx)
    {
        if ((File.GetLastWriteTimeUtc(path) - FixtureCatalog.FixedMtimeUtc).Duration() <= Tolerance) return null;
        if (fx.StoresWallClockMtime
            && (DateTime.SpecifyKind(File.GetLastWriteTime(path), DateTimeKind.Unspecified) - FixtureCatalog.FixedMtimeWallClock).Duration() <= Tolerance)
            return null;
        return Describe(File.GetLastWriteTime(path), fx);
    }

    private static string Describe(DateTime actual, FixtureInfo fx) =>
        $"{actual:yyyy-MM-dd HH:mm:ss}({actual.Kind}) — 기대: 절대 시각 {FixtureCatalog.FixedMtimeUtc:yyyy-MM-dd HH:mm:ss}Z" +
        $" = 이 PC 현지 {FixtureCatalog.FixedMtimeUtc.ToLocalTime():yyyy-MM-dd HH:mm:ss}" +
        (fx.StoresWallClockMtime ? $", 또는 벽시계 {FixtureCatalog.FixedMtimeWallClock:yyyy-MM-dd HH:mm:ss}(ZIP DOS 시각)" : "") +
        $" [현재 시간대 {TimeZoneInfo.Local.Id}]";
}
