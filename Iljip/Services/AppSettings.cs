using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

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

    /// <summary>이 판이 모르는 필드(뒤 판이 더한 것) — 읽은 그대로 다시 쓴다.</summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Extra { get; set; }

    /// <summary>읽은 JSON 에 묶음이 null 로 들어 있었을 때 기본값으로 채운다.</summary>
    internal AppSettings Normalize()
    {
        Extract ??= new ExtractSettings();
        return this;
    }

    /// <summary>깊은 복사(설정 창이 고치다 [취소]하면 버리게).</summary>
    public AppSettings Clone() =>
        JsonSerializer.Deserialize<AppSettings>(JsonSerializer.Serialize(this, JsonSettingsStore.Options), JsonSettingsStore.Options)!.Normalize();
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
}

/// <summary>설정 읽기·쓰기(설계 §4). 앱은 <see cref="JsonSettingsStore"/>, 시험은 <see cref="InMemorySettingsStore"/>.</summary>
public interface ISettingsStore
{
    /// <summary>설정을 읽는다. 없거나 깨져 있으면 기본값(깨진 파일은 옆으로 옮겨 둠).</summary>
    AppSettings Load();

    /// <summary>설정을 쓴다. 창을 닫을 때나 작업이 끝날 때 한 번만 부른다(DLP 연속 재기록 경합 교훈).</summary>
    void Save(AppSettings settings);
}

/// <summary>
/// settings.json 저장소. 쓰기는 <c>settings.json.tmp</c> 에 다 쓴 뒤 File.Replace(없으면 Move)로 한 번에 바꾼다 — 쓰다 멈춰도 반쪽 설정이 남지 않는다.
/// 회사 PC 의 DLP·OneDrive 가 파일을 잠깐 잡으면 200ms 간격으로 3번 다시 해 본다. 읽다 깨져 있으면 <c>settings.bad-yyyyMMddHHmmss.json</c> 으로 옮기고 기본값.
/// 경로를 주지 않으면 쓸 때마다 <see cref="AppPaths.SettingsFile"/> 를 본다(시험의 <see cref="AppPaths.OverrideRootForTests"/> 를 따르게).
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
        string path = FilePath;
        string text;
        try
        {
            if (!File.Exists(path)) return new AppSettings();
            text = File.ReadAllText(path);
        }
        catch (Exception ex)
        {
            // 못 읽음(잠김 등) — 기본값으로 시작하되 파일은 건드리지 않는다
            Logger.LogError("설정을 읽지 못함(기본값 사용): " + path, ex);
            return new AppSettings();
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

    public void Save(AppSettings settings)
    {
        string path = FilePath;
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
        }
        catch (Exception ex)
        {
            Logger.LogError("설정을 저장하지 못함: " + path, ex);
            try { File.Delete(tmp); } catch { /* 무시 */ }
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

    /// <summary>저장 횟수.</summary>
    public int SaveCount { get; private set; }

    /// <summary>읽은 횟수.</summary>
    public int LoadCount { get; private set; }

    public InMemorySettingsStore(AppSettings? initial = null)
    {
        if (initial is not null) Json = JsonSerializer.Serialize(initial, JsonSettingsStore.Options);
    }

    public AppSettings Load()
    {
        LoadCount++;
        return Json is null ? new AppSettings() : (JsonSerializer.Deserialize<AppSettings>(Json, JsonSettingsStore.Options) ?? new AppSettings()).Normalize();
    }

    public void Save(AppSettings settings)
    {
        SaveCount++;
        Json = JsonSerializer.Serialize(settings, JsonSettingsStore.Options);
    }
}
