using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using Iljip.Models;

namespace Iljip.Services;

// 설정 저장(설계 §4) — W2-05 에서 처음 들임(모양은 W2-04 보관본과 같게 — 다시 합칠 때 겹치게). 뒤 묶음은 아래 클래스에 '덧붙이기만' 한다.
// 파일: %LOCALAPPDATA%\Iljip\settings.json(UTF-8 JSON, camelCase, "schemaVersion": 1). 모르는 필드는 [JsonExtensionData] 로 그대로 둔다
// (새 판이 쓴 설정을 옛 판이 열었다 저장해도 지워지지 않게). 암호는 절대 저장하지 않는다.
// 창 없이 풀기(--extract-to)는 설정을 읽지 않는다 — 동작이 늘 같아야 해서(재사용장치 계약).

/// <summary>일집 설정 전체. 키는 'extract.onCollision' 처럼 묶음.이름 꼴(JSON 에서는 중첩 객체).</summary>
public sealed class AppSettings
{
    public const int CurrentSchemaVersion = 1;

    public int SchemaVersion { get; set; } = CurrentSchemaVersion;

    /// <summary>풀기(extract.*) — W2-05.</summary>
    public ExtractSettings Extract { get; set; } = new();

    /// <summary>압축(compress.*) — W2-07(한 화면 압축 창이 [확인]할 때 기억). 암호는 없다(절대 저장하지 않음).</summary>
    public CompressSettings Compress { get; set; } = new();

    /// <summary>이 판이 모르는 필드(뒤 판이 더한 것) — 읽은 그대로 다시 쓴다.</summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Extra { get; set; }

    /// <summary>화면(ui.*) — W2-09(트리 너비).</summary>
    public UiSettings Ui { get; set; } = new();

    /// <summary>자동 풀림 exe(sfx.*) — W2-10(만들기 전 '회사 메일은 exe 를 막아요' 안내를 다시 묻지 않기).</summary>
    public SfxSettings Sfx { get; set; } = new();

    /// <summary>최근 연 압축 파일(recent.*) — W2-11(시작 화면 '최근 파일', I3-15). 다루기는 <see cref="RecentFilesService"/>.</summary>
    public RecentSettings Recent { get; set; } = new();

    /// <summary>탐색기 연결(shell.*) — W2-12(파일 연결을 넣었는지 기록).</summary>
    public ShellSettings Shell { get; set; } = new();

    /// <summary>읽은 JSON 에 묶음이 null 로 들어 있었을 때 기본값으로 채운다.</summary>
    internal AppSettings Normalize()
    {
        Extract ??= new ExtractSettings();
        Compress ??= new CompressSettings();
        Compress.ExcludePatterns ??= new List<string>(PatternFileFilter.DefaultPatterns);
        Extract.RecentFolders ??= new List<string>();
        Ui ??= new UiSettings();
        Sfx ??= new SfxSettings();
        Recent ??= new RecentSettings();
        Shell ??= new ShellSettings();
        Recent.Files ??= new List<RecentFile>();
        Recent.Files.RemoveAll(f => f is null || string.IsNullOrWhiteSpace(f.Path));
        return this;
    }

    /// <summary>깊은 복사(설정 창이 고치다 [취소]하면 버리게).</summary>
    public AppSettings Clone() =>
        JsonSerializer.Deserialize<AppSettings>(JsonSerializer.Serialize(this, JsonSettingsStore.Options), JsonSettingsStore.Options)!.Normalize();

    /// <summary>
    /// <paramref name="other"/> 의 묶음을 모두 이 객체로 가져온다(W2-11 검증 지적 — 화면이 쥔 설정 객체를 바꾸지 않고 내용만 바꾸게: 최근 파일 다루기가 이 객체를 쥐고 있음).
    /// 뒤 묶음이 칸을 더해도 빠지지 않게 공개 속성을 모두 옮긴다.
    /// </summary>
    internal void CopyFrom(AppSettings other)
    {
        foreach (var p in typeof(AppSettings).GetProperties(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance))
            if (p.CanRead && p.CanWrite && p.GetIndexParameters().Length == 0)
                p.SetValue(this, p.GetValue(other));
        Normalize();
    }
}

/// <summary>extract.* — 풀기.</summary>
public sealed class ExtractSettings
{
    /// <summary>extract.onCollision — 같은 이름 파일이 있을 때(W2-05, I1-5). 기본 '묻기'. 일집 화면·우클릭 풀기에 적용(창 없이 풀기는 늘 '이름 (1)').</summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public CollisionPolicy OnCollision { get; set; } = CollisionPolicy.Ask;

    /// <summary>extract.openFolderAfter — 우클릭으로 '이름' 폴더에 푼 뒤 그 폴더를 탐색기로 열지(W2-05, I1-6). 기본 켬. '여기에 풀기'는 이미 보던 폴더라 열지 않음.</summary>
    public bool OpenFolderAfter { get; set; } = true;

    /// <summary>extract.deleteArchiveAfter — 다 풀고 나서 압축 파일(나뉜 조각 모두)을 휴지통으로(W2-05, I2-5). 기본 꺼짐. 오류·건너뛴 파일·취소가 없을 때만.</summary>
    public bool DeleteArchiveAfter { get; set; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Extra { get; set; }

    /// <summary>extract.makeFolder — 풀기 창의 '압축파일명으로 폴더 만들기'(W2-09, I2-13). 기본 켬, 창에서 고른 값을 기억.</summary>
    public bool MakeFolder { get; set; } = true;

    /// <summary>extract.recentFolders — 최근에 푼 곳(W2-09, I2-13). 가장 최근이 맨 앞, 최대 <see cref="MaxRecentFolders"/>개, 대소문자 무시로 겹치지 않음.</summary>
    public List<string> RecentFolders { get; set; } = new();

    /// <summary>
    /// extract.dialogOpenFolderAfter — 풀기 창의 '다 풀면 폴더 열기'(W2-09, I2-13). 기본 꺼짐(예전 일집 안 풀기는 폴더를 열지 않았음), 창에서 고른 값을 기억.
    /// 탐색기 우클릭 풀기의 <see cref="OpenFolderAfter"/> 와는 따로다(그쪽은 ☰ ▸ 설정에서만).
    /// </summary>
    public bool DialogOpenFolderAfter { get; set; }

    /// <summary>최근 푼 곳을 몇 개까지 기억하나.</summary>
    public const int MaxRecentFolders = 10;

    /// <summary>
    /// 푼 곳을 최근 목록 맨 앞에 넣는다(같은 폴더 — 대소문자·끝 '\' 무시 — 가 있으면 빼고 앞으로). 최대 <see cref="MaxRecentFolders"/>개.
    /// 저장은 하지 않는다(부르는 쪽이 작업이 끝날 때 한 번).
    /// </summary>
    public void PushRecentFolder(string folder)
    {
        string f = TrimFolder(folder);
        if (f.Length == 0) return;
        RecentFolders ??= new List<string>();
        RecentFolders.RemoveAll(x => string.Equals(TrimFolder(x), f, StringComparison.OrdinalIgnoreCase));
        RecentFolders.Insert(0, f);
        if (RecentFolders.Count > MaxRecentFolders) RecentFolders.RemoveRange(MaxRecentFolders, RecentFolders.Count - MaxRecentFolders);
    }

    /// <summary>
    /// 풀기 창에 보일 최근 폴더 — 지금 있는 폴더만(USB 를 뺐거나 지운 폴더는 숨김), 순서 그대로, 겹치는 것은 하나만.
    /// 네트워크 폴더(NAS·공유 폴더)는 있는지 보지 않고 보인다(W2-09 검증 지적 — 끊긴 NAS 를 보다가 창이 몇 초씩 굳음. [풀기] 때 확인 — <see cref="FolderProbe"/>).
    /// </summary>
    public IReadOnlyList<string> VisibleRecentFolders(Func<string, bool>? exists = null)
    {
        exists ??= FolderProbe.ExistsOrNetwork;
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var result = new List<string>();
        foreach (var x in RecentFolders ?? new List<string>())
        {
            string f = TrimFolder(x);
            if (f.Length == 0 || !seen.Add(f)) continue;
            bool ok;
            try { ok = exists(f); }
            catch { ok = false; }
            if (ok) result.Add(f);
            if (result.Count == MaxRecentFolders) break;
        }
        return result;
    }

    /// <summary>폴더 경로 다듬기: 앞뒤 공백, 끝 '\'(드라이브 뿌리 'C:\' 는 그대로).</summary>
    internal static string TrimFolder(string? folder)
    {
        string f = (folder ?? string.Empty).Trim();
        while (f.Length > 3 && (f.EndsWith('\\') || f.EndsWith('/'))) f = f[..^1];
        return f;
    }
}

/// <summary>sfx.* — 자동 풀림 exe(W2-10, I3-1).</summary>
public sealed class SfxSettings
{
    /// <summary>
    /// sfx.skipMailWarning — 자동 풀림 exe 를 만들기 직전의 '회사 메일은 실행 파일(.exe)을 막는 경우가 많아요' 질문을 건너뛸지(기본 꺼짐 = 늘 물음).
    /// 그 질문 창의 '다시 묻지 않기'로 켜고, ☰ ▸ 설정에서 되돌린다. 압축 창의 형식 칸 아래 안내는 이 값과 상관없이 늘 보인다.
    /// </summary>
    public bool SkipMailWarning { get; set; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Extra { get; set; }
}

/// <summary>shell.* — 탐색기 연결(W2-12, I1-1).</summary>
public sealed class ShellSettings
{
    /// <summary>
    /// shell.associationsRegistered — 이 사용자가 ☰ ▸ 설정 '파일 연결'로 연결을 넣었는가(기록용 — 실제 상태는 레지스트리의 RegisteredKeys 가 기준).
    /// </summary>
    public bool AssociationsRegistered { get; set; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Extra { get; set; }
}

/// <summary>recent.* — 최근 연 압축 파일(W2-11, I3-15). 최근 것이 맨 앞, 최대 <see cref="RecentFilesService.MaxFiles"/>개. 암호는 없다.</summary>
public sealed class RecentSettings
{
    /// <summary>recent.files — 경로와 마지막으로 연 시각(UTC).</summary>
    public List<RecentFile> Files { get; set; } = new();

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Extra { get; set; }
}

/// <summary>최근 연 압축 파일 하나.</summary>
public sealed class RecentFile
{
    /// <summary>전체 경로(나뉜 압축은 연 조각 — 첫 조각·.zip).</summary>
    public string Path { get; set; } = string.Empty;

    /// <summary>마지막으로 연 시각(UTC).</summary>
    public DateTime LastOpenedUtc { get; set; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Extra { get; set; }
}

/// <summary>ui.* — 화면(W2-09).</summary>
public sealed class UiSettings
{
    /// <summary>기본 트리 너비(화소).</summary>
    public const double DefaultTreeWidth = 260;

    /// <summary>ui.treeWidth — 왼쪽 폴더 트리 너비(I2-14 — 가르는 막대로 바꾸면 창을 닫을 때 한 번 저장). 기본 260.</summary>
    public double TreeWidth { get; set; } = DefaultTreeWidth;

    /// <summary>쓸 수 있는 트리 너비로(너무 좁거나 넓거나 숫자가 아니면 기본값).</summary>
    public static double ClampTreeWidth(double width) =>
        double.IsFinite(width) && width >= 80 && width <= 2000 ? width : DefaultTreeWidth;

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Extra { get; set; }
}

/// <summary>
/// compress.* — 압축(W2-07, I2-12 '기억'). 한 화면 압축 창의 처음 값이고, 창을 [확인]으로 닫을 때 그 값으로 바꿔 한 번 저장한다.
/// 단 '원본을 휴지통으로'(deleteOriginalsAfter)·'만들 곳' 처음 값 고르기(folderChoice·fixedFolder)는 압축 창이 바꾸지 않는다(☰ ▸ 설정에서만 — W2-07 검증 지적).
/// ★ 암호 칸은 두지 않는다(암호는 절대 저장하지 않음 — 시험이 JSON 에 'password' 가 없는지 본다).
/// 7z 칸(solid·encryptFileNames)은 W2-02, splitBytes 는 W2-03, testAfter 는 W2-08 몫이지만 같은 창에서 기억하므로 여기서 같이 들인다.
/// </summary>
public sealed class CompressSettings
{
    /// <summary>compress.lastFormat — 마지막으로 고른 형식(기본 ZIP). 7z.dll 을 못 쓰는 PC 에서 7Z 였으면 창이 ZIP 으로 바꿔 보인다.</summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public CompressionFormat LastFormat { get; set; } = CompressionFormat.Zip;

    /// <summary>compress.level — 압축 수준(기본 보통).</summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public CompressionLevelOption Level { get; set; } = CompressionLevelOption.Normal;

    /// <summary>compress.solid — 7Z 솔리드(기본 켬).</summary>
    public bool Solid { get; set; } = true;

    /// <summary>compress.encryptFileNames — 7Z 파일 이름도 암호로 숨기기(기본 끔. 암호를 넣었을 때만 씀).</summary>
    public bool EncryptFileNames { get; set; }

    /// <summary>compress.zipEncryption — ZIP 암호 방식(기본 AES-256).</summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public ZipEncryption ZipEncryption { get; set; } = ZipEncryption.Aes256;

    /// <summary>compress.splitBytes — 나누기 크기(null = 나누지 않음, 기본).</summary>
    public long? SplitBytes { get; set; }

    /// <summary>compress.eachSeparately — 각각 따로 압축(I2-4, 기본 끔).</summary>
    public bool EachSeparately { get; set; }

    /// <summary>compress.dateInName — 이름에 날짜(I3-5, 기본 붙이지 않음).</summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public DateNameFormat DateInName { get; set; } = DateNameFormat.None;

    /// <summary>compress.excludeEnabled — 쓸모없는 파일 빼기(I3-6, 기본 켬).</summary>
    public bool ExcludeEnabled { get; set; } = true;

    /// <summary>compress.excludePatterns — 빼기 목록(기본 <see cref="PatternFileFilter.DefaultPatterns"/>, 설정 창·압축 창에서 고침).</summary>
    public List<string> ExcludePatterns { get; set; } = new(PatternFileFilter.DefaultPatterns);

    /// <summary>compress.testAfter — 다 만들고 시험하기(기본 끔 — W2-08 이 키움).</summary>
    public bool TestAfter { get; set; }

    /// <summary>
    /// compress.deleteOriginalsAfter — 압축 창에서 '원본을 휴지통으로'를 켠 채로 시작할지(I3-4, 기본 끔. 켜도 매번 묻는다).
    /// 압축 창에서 켠 것은 기억하지 않는다(W2-07 검증 지적 — 한 번 켰다고 다음 창부터 켜진 채로 열리지 않게). ☰ ▸ 설정에서만 바꾼다.
    /// </summary>
    public bool DeleteOriginalsAfter { get; set; }

    /// <summary>compress.lastFolder — 마지막으로 만든 곳(I2-12 — 창의 위치 칸 목록에 보이고, <see cref="FolderChoice"/> 가 '마지막 폴더'면 처음 값).</summary>
    public string? LastFolder { get; set; }

    /// <summary>compress.folderChoice — 압축 창 '만들 곳'의 처음 값(원본이 있는 폴더(기본)·마지막으로 만든 곳·정해 둔 폴더 — W2-07 검증 지적, I2-12).</summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public CompressFolderChoice FolderChoice { get; set; } = CompressFolderChoice.SourceFolder;

    /// <summary>compress.fixedFolder — '정해 둔 폴더'(<see cref="FolderChoice"/> 가 FixedFolder 일 때 처음 값).</summary>
    public string? FixedFolder { get; set; }

    /// <summary>
    /// 압축 창 '만들 곳'의 처음 값: <see cref="FolderChoice"/> 의 폴더가 지금 있으면 그 폴더, 아니면(없어짐·USB 를 뺌·아직 없음) <paramref name="sourceFolder"/>(원본이 있는 폴더).
    /// </summary>
    public string InitialFolder(string sourceFolder)
    {
        string? chosen = FolderChoice switch
        {
            CompressFolderChoice.LastFolder => LastFolder,
            CompressFolderChoice.FixedFolder => FixedFolder,
            _ => null,
        };
        return IsUsableFolder(chosen) ? chosen!.Trim() : sourceFolder;
    }

    /// <summary>설정 창의 '정해 둔 폴더' 칸 검사 — 괜찮으면 null, 아니면 쉬운 한국어 안내.</summary>
    public static string? CheckFixedFolder(string? folder)
    {
        string f = (folder ?? string.Empty).Trim();
        if (f.Length == 0) return "정해 둔 폴더를 [찾아보기]로 골라 주세요.";
        if (!IsFullPath(f)) return "정해 둔 폴더 경로가 올바르지 않아요. [찾아보기]로 골라 주세요.";
        if (!Directory.Exists(f)) return $"‘{f}’ 폴더가 없어요. [찾아보기]로 골라 주세요.";
        return null;
    }

    /// <summary>처음 값으로 쓸 수 있는 폴더인가(전체 경로이고 지금 있음). 모르면 false.</summary>
    internal static bool IsUsableFolder(string? folder)
    {
        string f = (folder ?? string.Empty).Trim();
        return f.Length > 0 && IsFullPath(f) && Directory.Exists(f);
    }

    private static bool IsFullPath(string f)
    {
        try
        {
            return f.IndexOfAny(Path.GetInvalidPathChars()) < 0 && Path.IsPathFullyQualified(f);
        }
        catch
        {
            return false;
        }
    }

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Extra { get; set; }
}

/// <summary>설정 읽기·쓰기(설계 §4). 앱은 <see cref="JsonSettingsStore"/>, 시험은 <see cref="InMemorySettingsStore"/>.</summary>
public interface ISettingsStore
{
    /// <summary>설정을 읽는다. 없거나 깨져 있으면 기본값(깨진 파일은 옆으로 옮겨 둠).</summary>
    AppSettings Load();

    /// <summary>설정을 쓴다. 창을 닫을 때나 작업이 끝날 때 한 번만 부른다(DLP 연속 재기록 경합 교훈).</summary>
    void Save(AppSettings settings);

    /// <summary>
    /// 저장 직전에 최신 설정을 다시 읽어 <paramref name="change"/> 를 얹고 쓴다(W2-11 검증 지적 — 일집 창이 둘이면 각 창의 오래된 사본 전체를 쓰지 않고
    /// 그 창이 바꾼 것만 얹어, 다른 창이 그사이 저장한 최근 파일·설정을 지우지 않게). 읽기~쓰기 사이에 다른 일집이 끼어들지 않게 한다.
    /// 저장한 설정을 돌려준다 — 있는 설정 파일을 읽지 못했거나(잠김 등) 쓰지 못했으면 null(그때는 아무것도 덮어쓰지 않음 — 부르는 쪽이 다음에 다시).
    /// </summary>
    AppSettings? Update(Action<AppSettings> change);
}

/// <summary>
/// settings.json 저장소. 쓰기는 <c>settings.json.tmp</c> 에 다 쓴 뒤 File.Replace(없으면 Move)로 한 번에 바꾼다 — 쓰다 멈춰도 반쪽 설정이 남지 않는다.
/// 회사 PC 의 DLP·OneDrive 가 파일을 잠깐 잡으면 200ms 간격으로 3번 다시 해 본다. 읽다 깨져 있으면 <c>settings.bad-yyyyMMddHHmmss.json</c> 으로 옮기고 기본값.
/// 경로를 주지 않으면 쓸 때마다 <see cref="AppPaths.SettingsFile"/> 를 본다(시험의 <see cref="AppPaths.OverrideRootForTests"/> 를 따르게).
/// W2-11 검증 지적: 일집 창이 둘이면(새 창) 두 프로세스가 같은 파일을 쓴다 — 쓰기(<see cref="Save"/>·<see cref="Update"/>)는 설정 파일마다 이름 붙은 뮤텍스로
/// 한 번에 하나씩(같은 <c>.tmp</c> 를 둘이 쓰지 않게), <see cref="Update"/> 는 그 안에서 최신 파일을 다시 읽어 바꾼 것만 얹는다.
/// </summary>
public sealed class JsonSettingsStore : ISettingsStore
{
    private readonly string? _path;

    public JsonSettingsStore(string? path = null) => _path = path;

    public string FilePath => _path ?? AppPaths.SettingsFile;

    internal static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        // 한글(최근 폴더 이름 등)을 \uXXXX 로 바꾸지 않고 그대로 — 사람이 열어 봐도 읽히게
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.Create(System.Text.Unicode.UnicodeRanges.All),
    };

    public AppSettings Load()
    {
        // 못 읽음(잠김 등 — 잠깐 기다려 다시 봐도) — 기본값으로 시작하되 파일은 건드리지 않는다
        return Read(FilePath) ?? new AppSettings();
    }

    public void Save(AppSettings settings)
    {
        using var gate = WriteGate.Enter(FilePath);
        TryWrite(FilePath, settings);
    }

    public AppSettings? Update(Action<AppSettings> change)
    {
        string path = FilePath;
        using var gate = WriteGate.Enter(path);
        // 있는데 못 읽으면 덮어쓰지 않는다 — 기본값에 바꾼 것만 얹어 쓰면 다른 설정이 모두 사라짐
        var latest = Read(path);
        if (latest is null) return null;
        change(latest);
        return TryWrite(path, latest) ? latest : null;
    }

    /// <summary>
    /// 설정 파일을 읽는다. 없으면 기본값, 깨져 있으면 옆으로 옮기고 기본값. 있는데 읽지 못하면(잠김 등 — DLP·OneDrive 면 200ms 간격으로 3번 다시 봄) null.
    /// </summary>
    private static AppSettings? Read(string path)
    {
        string text;
        try
        {
            if (!File.Exists(path)) return new AppSettings();
            text = ReadWithRetry(path);
        }
        catch (Exception ex)
        {
            Logger.LogError("설정을 읽지 못함: " + path, ex);
            return null;
        }

        try
        {
            var s = JsonSerializer.Deserialize<AppSettings>(text, Options);
            if (s is null) throw new JsonException("설정이 비어 있음(null)");
            return s.Normalize();
        }
        catch (JsonException ex)
        {
            Logger.LogError("설정 파일이 깨져 있어 옆으로 옮기고 기본값을 씀: " + path, ex);
            MoveAside(path);
            return new AppSettings();
        }
    }

    private static string ReadWithRetry(string path)
    {
        for (int attempt = 0; ; attempt++)
        {
            try
            {
                return File.ReadAllText(path);
            }
            catch (Exception ex) when (OutputFiles.IsTransientLock(ex) && attempt < OutputFiles.MoveLockRetries)
            {
                Thread.Sleep(OutputFiles.MoveLockDelayMs);
            }
        }
    }

    /// <summary>임시 파일에 다 쓴 뒤 한 번에 바꾼다. 됐으면 true(못 하면 기록만 — 임시 파일은 지움).</summary>
    private static bool TryWrite(string path, AppSettings settings)
    {
        string tmp = path + ".tmp";
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(tmp, JsonSerializer.Serialize(settings.Normalize(), Options), new System.Text.UTF8Encoding(false));
            Retry(() =>
            {
                if (File.Exists(path)) File.Replace(tmp, path, null, ignoreMetadataErrors: true);
                else File.Move(tmp, path);
            });
            return true;
        }
        catch (Exception ex)
        {
            Logger.LogError("설정을 저장하지 못함: " + path, ex);
            try { File.Delete(tmp); } catch { /* 무시 */ }
            return false;
        }
    }

    /// <summary>
    /// 설정 파일 하나에 한 번에 한 일집만 쓰게 하는 이름 붙은 뮤텍스(같은 사용자 세션 — 이름은 파일 경로의 해시라 시험의 다른 경로와 섞이지 않음).
    /// 3초 안에 못 잡으면 기록만 하고 그냥 쓴다(앞 일집이 멈췄을 때 저장을 영영 못 하지 않게).
    /// </summary>
    private sealed class WriteGate : IDisposable
    {
        private readonly Mutex? _mutex;
        private readonly bool _owned;

        private WriteGate(Mutex? mutex, bool owned)
        {
            _mutex = mutex;
            _owned = owned;
        }

        public static WriteGate Enter(string path)
        {
            Mutex? mutex = null;
            try
            {
                byte[] hash = System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(Path.GetFullPath(path).ToUpperInvariant()));
                mutex = new Mutex(false, @"Local\Iljip.Settings." + Convert.ToHexString(hash, 0, 12));
                bool owned;
                try
                {
                    owned = mutex.WaitOne(TimeSpan.FromSeconds(3));
                }
                catch (AbandonedMutexException)
                {
                    owned = true;   // 앞 일집이 놓지 않고 끝남 — 이제 우리 것
                }
                if (!owned) Logger.LogError("설정 쓰기 차례를 3초 안에 못 받음(그냥 씀): " + path, new TimeoutException());
                return new WriteGate(mutex, owned);
            }
            catch (Exception ex)
            {
                Logger.LogError("설정 쓰기 차례를 못 만듦(그냥 씀): " + path, ex);
                mutex?.Dispose();
                return new WriteGate(null, false);
            }
        }

        public void Dispose()
        {
            if (_owned)
            {
                try { _mutex?.ReleaseMutex(); } catch { /* 무시 */ }
            }
            _mutex?.Dispose();
        }
    }

    private static void MoveAside(string path)
    {
        try
        {
            string dir = Path.GetDirectoryName(path)!;
            string bad = Path.Combine(dir, $"settings.bad-{DateTime.Now:yyyyMMddHHmmss}.json");
            for (int i = 2; File.Exists(bad); i++)
                bad = Path.Combine(dir, $"settings.bad-{DateTime.Now:yyyyMMddHHmmss}-{i}.json");
            Retry(() => File.Move(path, bad));
        }
        catch (Exception ex)
        {
            Logger.LogError("깨진 설정 파일을 옮기지 못함: " + path, ex);
        }
    }

    /// <summary>IOException(잠김)이면 200ms 간격으로 3번 다시(설계 §3 DLP 대비).</summary>
    private static void Retry(Action action)
    {
        for (int attempt = 0; ; attempt++)
        {
            try
            {
                action();
                return;
            }
            catch (Exception ex) when (OutputFiles.IsTransientLock(ex) && attempt < OutputFiles.MoveLockRetries)
            {
                Thread.Sleep(OutputFiles.MoveLockDelayMs);
            }
        }
    }
}

/// <summary>시험용 — 파일 없이 메모리에만(쓸 때마다 JSON 으로 한 번 돌려 실제 저장과 같은 모양을 확인할 수 있게).</summary>
public sealed class InMemorySettingsStore : ISettingsStore
{
    /// <summary>마지막으로 저장된 JSON(없으면 null).</summary>
    public string? Json { get; private set; }

    /// <summary>저장 횟수(<see cref="Save"/> 와 저장까지 된 <see cref="Update"/>).</summary>
    public int SaveCount { get; private set; }

    /// <summary>읽은 횟수(<see cref="Load"/> — 화면이 처음 설정을 읽는 것). 저장 직전에 다시 읽는 것은 <see cref="UpdateCount"/>.</summary>
    public int LoadCount { get; private set; }

    /// <summary><see cref="Update"/>(저장 직전 다시 읽고 바꾼 것만 얹기 — W2-11 검증 지적)를 부른 횟수.</summary>
    public int UpdateCount { get; private set; }

    /// <summary>true 면 <see cref="Update"/> 가 '있는 설정 파일을 못 읽음'처럼 null 을 돌려주고 아무것도 쓰지 않는다(DLP 잠김 흉내).</summary>
    public bool FailUpdates { get; set; }

    public InMemorySettingsStore(AppSettings? initial = null)
    {
        if (initial is not null) Json = JsonSerializer.Serialize(initial, JsonSettingsStore.Options);
    }

    public AppSettings Load()
    {
        LoadCount++;
        return Read();
    }

    public void Save(AppSettings settings)
    {
        SaveCount++;
        Json = JsonSerializer.Serialize(settings, JsonSettingsStore.Options);
    }

    public AppSettings? Update(Action<AppSettings> change)
    {
        UpdateCount++;
        if (FailUpdates) return null;
        var latest = Read();
        change(latest);
        SaveCount++;
        Json = JsonSerializer.Serialize(latest, JsonSettingsStore.Options);
        return latest;
    }

    private AppSettings Read() =>
        Json is null ? new AppSettings() : (JsonSerializer.Deserialize<AppSettings>(Json, JsonSettingsStore.Options) ?? new AppSettings()).Normalize();
}
