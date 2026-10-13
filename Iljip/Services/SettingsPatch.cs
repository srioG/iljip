using System.Text.Json;
using System.Text.Json.Nodes;

namespace Iljip.Services;

/// <summary>
/// 설정 두 벌의 차이 — 바뀐 칸만(W2-11 검증 지적: 일집 창이 둘이면 설정 창에서 고친 것을 그 창의 오래된 사본째 쓰지 않고,
/// 저장 직전에 다시 읽은 최신 설정에 '바뀐 칸'만 얹어 다른 창이 그사이 바꾼 칸을 되돌리지 않게).
/// 칸은 JSON 의 잎(값 하나, 목록은 통째로 한 칸 — 빼기 목록 등). 모르는 필드(뒤 판이 더한 것)도 같은 규칙.
/// </summary>
public sealed class SettingsPatch
{
    private readonly List<(string[] Path, JsonNode? Value, bool Remove)> _changes;

    private SettingsPatch(List<(string[] Path, JsonNode? Value, bool Remove)> changes) => _changes = changes;

    /// <summary>바뀐 칸이 없는가.</summary>
    public bool IsEmpty => _changes.Count == 0;

    /// <summary>바뀐 칸의 이름('extract.onCollision' 꼴 — 기록·시험용).</summary>
    public IReadOnlyList<string> ChangedKeys => _changes.Select(c => string.Join('.', c.Path)).ToList();

    /// <summary>
    /// <paramref name="before"/> → <paramref name="after"/> 에서 바뀐 칸. <paramref name="ignoreTopLevel"/> 의 묶음(예: 'recent' — 최근 파일은 따로 합침)은 보지 않는다.
    /// </summary>
    public static SettingsPatch Between(AppSettings before, AppSettings after, params string[] ignoreTopLevel)
    {
        var changes = new List<(string[] Path, JsonNode? Value, bool Remove)>();
        var ignore = new HashSet<string>(ignoreTopLevel, StringComparer.Ordinal);
        Diff(ToNode(before), ToNode(after), new List<string>(), changes, ignore);
        return new SettingsPatch(changes);
    }

    /// <summary>바뀐 칸만 <paramref name="target"/> 에 얹는다(그 밖의 칸은 그대로). 같은 객체를 고친다(<see cref="AppSettings.CopyFrom"/>).</summary>
    public void ApplyTo(AppSettings target)
    {
        if (IsEmpty) return;
        var root = ToNode(target);
        foreach (var (path, value, remove) in _changes)
        {
            var obj = root;
            for (int i = 0; i < path.Length - 1; i++)
            {
                if (obj[path[i]] is not JsonObject next)
                {
                    next = new JsonObject();
                    obj[path[i]] = next;
                }
                obj = next;
            }
            if (remove) obj.Remove(path[^1]);
            else obj[path[^1]] = value?.DeepClone();
        }
        var patched = root.Deserialize<AppSettings>(JsonSettingsStore.Options) ?? new AppSettings();
        target.CopyFrom(patched.Normalize());
    }

    private static JsonObject ToNode(AppSettings s) =>
        JsonSerializer.SerializeToNode(s, JsonSettingsStore.Options) as JsonObject ?? new JsonObject();

    private static void Diff(JsonObject a, JsonObject b, List<string> path, List<(string[] Path, JsonNode? Value, bool Remove)> changes, HashSet<string> ignore)
    {
        bool top = path.Count == 0;
        foreach (var (key, bv) in b)
        {
            if (top && ignore.Contains(key)) continue;
            a.TryGetPropertyValue(key, out var av);
            if (av is JsonObject ao && bv is JsonObject bo)
            {
                path.Add(key);
                Diff(ao, bo, path, changes, ignore);
                path.RemoveAt(path.Count - 1);
            }
            else if (!a.ContainsKey(key) || !JsonNode.DeepEquals(av, bv))
            {
                changes.Add(([.. path, key], bv?.DeepClone(), false));
            }
        }
        foreach (var (key, _) in a)
        {
            if (top && ignore.Contains(key)) continue;
            if (!b.ContainsKey(key)) changes.Add(([.. path, key], null, true));
        }
    }
}
