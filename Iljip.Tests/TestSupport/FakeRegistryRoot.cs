using System.Text;
using Iljip.Services;
using Microsoft.Win32;

namespace Iljip.Tests.TestSupport;

/// <summary>
/// 가짜 레지스트리 뿌리(W2-12 — 설계 §5): 메모리 트리. 진짜 HKCU 는 시험용 하위 키조차 쓰지 않는다.
/// 키 이름은 Windows 처럼 대소문자를 가리지 않고, <see cref="ExportReg"/> 로 정렬한 .reg 모양 글을 만든다(스냅숏 비교용).
/// </summary>
public sealed class FakeRegistryRoot : IRegistryRoot
{
    private sealed class Node
    {
        public string Name = string.Empty;
        public readonly Dictionary<string, Node> Children = new(StringComparer.OrdinalIgnoreCase);
        public readonly Dictionary<string, (object Value, RegistryValueKind Kind)> Values = new(StringComparer.OrdinalIgnoreCase);
    }

    private readonly Node _root = new();
    private readonly object _gate = new();

    /// <summary>쓰기를 막을 키(앞부분이 같으면) — 회사 정책으로 막힌 곳 흉내.</summary>
    public List<string> DenyWrites { get; } = new();

    /// <summary>진짜 HKCU 처럼 늘 있는 키(Software\Classes)를 미리 만든 뿌리.</summary>
    public static FakeRegistryRoot WithDefaults()
    {
        var r = new FakeRegistryRoot();
        r.CreateSubKey(@"Software\Classes").Dispose();
        return r;
    }

    private static string[] Split(string path) => path.Split('\\', StringSplitOptions.RemoveEmptyEntries);

    private Node? Find(string path)
    {
        var n = _root;
        foreach (var part in Split(path))
            if (!n.Children.TryGetValue(part, out n!)) return null;
        return n;
    }

    public IRegistryKey CreateSubKey(string path)
    {
        lock (_gate)
        {
            CheckWrite(path);
            var n = _root;
            foreach (var part in Split(path))
            {
                if (!n.Children.TryGetValue(part, out var child))
                    n.Children[part] = child = new Node { Name = part };
                n = child;
            }
            return new Key(this, n, path, writable: true);
        }
    }

    public IRegistryKey? OpenSubKey(string path, bool writable)
    {
        lock (_gate)
            return Find(path) is { } n ? new Key(this, n, path, writable) : null;
    }

    public void DeleteSubKeyTree(string path, bool throwOnMissing)
    {
        lock (_gate)
        {
            var parts = Split(path);
            var parent = parts.Length == 1 ? _root : Find(string.Join('\\', parts[..^1]));
            if (parent is null || !parent.Children.ContainsKey(parts[^1]))
            {
                if (throwOnMissing) throw new ArgumentException("키 없음: " + path);
                return;
            }
            CheckWrite(path);
            parent.Children.Remove(parts[^1]);
        }
    }

    private void CheckWrite(string path)
    {
        if (DenyWrites.Any(d => path.StartsWith(d, StringComparison.OrdinalIgnoreCase)))
            throw new UnauthorizedAccessException("레지스트리 쓰기 막힘(가짜): " + path);
    }

    /// <summary>키가 있는가.</summary>
    public bool KeyExists(string path)
    {
        lock (_gate) return Find(path) is not null;
    }

    /// <summary>값(없으면 null).</summary>
    public object? Value(string key, string name)
    {
        lock (_gate)
            return Find(key) is { } n && n.Values.TryGetValue(name, out var v) ? v.Value : null;
    }

    /// <summary>값의 종류(없으면 null).</summary>
    public RegistryValueKind? Kind(string key, string name)
    {
        lock (_gate)
            return Find(key) is { } n && n.Values.TryGetValue(name, out var v) ? v.Kind : null;
    }

    /// <summary>모든 키 경로(정렬).</summary>
    public List<string> AllKeys()
    {
        var list = new List<string>();
        lock (_gate) Walk(_root, string.Empty, (p, _) => list.Add(p));
        return list.Where(p => p.Length > 0).OrderBy(p => p, StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>
    /// 정렬한 .reg 모양 글(HKEY_CURRENT_USER 뿌리). 키는 대소문자 무시 정렬, 값은 (기본값) 먼저 그다음 이름 순.
    /// REG_SZ "…", REG_DWORD dword:…, REG_NONE hex(0):…, REG_MULTI_SZ 는 읽기 쉽게 multi:"a","b".
    /// </summary>
    public string ExportReg()
    {
        var keys = new List<(string Path, Node Node)>();
        lock (_gate) Walk(_root, string.Empty, (p, n) => { if (p.Length > 0) keys.Add((p, n)); });
        var sb = new StringBuilder();
        foreach (var (path, node) in keys.OrderBy(k => k.Path, StringComparer.OrdinalIgnoreCase))
        {
            sb.Append("[HKEY_CURRENT_USER\\").Append(path).Append("]\n");
            foreach (var (name, (value, kind)) in node.Values.OrderBy(v => v.Key.Length == 0 ? 0 : 1).ThenBy(v => v.Key, StringComparer.OrdinalIgnoreCase))
                sb.Append(name.Length == 0 ? "@" : Quote(name)).Append('=').Append(Render(value, kind)).Append('\n');
            sb.Append('\n');
        }
        return sb.ToString();
    }

    private static string Quote(string s) => "\"" + s.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";

    private static string Render(object value, RegistryValueKind kind) => kind switch
    {
        RegistryValueKind.String => Quote((string)value),
        RegistryValueKind.ExpandString => "hex(2):" + Quote((string)value),
        RegistryValueKind.DWord => "dword:" + Convert.ToUInt32(value).ToString("x8"),
        RegistryValueKind.MultiString => "multi:" + string.Join(",", ((string[])value).Select(Quote)),
        RegistryValueKind.None => "hex(0):" + string.Join(",", ((byte[])value).Select(b => b.ToString("x2"))),
        _ => kind + ":" + value,
    };

    private static void Walk(Node n, string path, Action<string, Node> visit)
    {
        visit(path, n);
        foreach (var c in n.Children.Values)
            Walk(c, path.Length == 0 ? c.Name : path + "\\" + c.Name, visit);
    }

    private sealed class Key(FakeRegistryRoot owner, Node node, string path, bool writable) : IRegistryKey
    {
        public void SetValue(string? name, object value, RegistryValueKind kind)
        {
            if (!writable) throw new UnauthorizedAccessException("읽기 전용으로 연 키");
            lock (owner._gate)
            {
                owner.CheckWrite(path);
                // 진짜 레지스트리처럼 값을 복사해 둔다(배열을 나중에 바꿔도 영향 없게)
                object stored = value switch
                {
                    string[] a => a.ToArray(),
                    byte[] b => b.ToArray(),
                    _ => value,
                };
                node.Values[name ?? string.Empty] = (stored, kind);
            }
        }

        public object? GetValue(string? name)
        {
            lock (owner._gate)
                return node.Values.TryGetValue(name ?? string.Empty, out var v) ? v.Value : null;
        }

        public RegistryValueKind? GetValueKind(string? name)
        {
            lock (owner._gate)
                return node.Values.TryGetValue(name ?? string.Empty, out var v) ? v.Kind : null;
        }

        public void DeleteValue(string name, bool throwOnMissing)
        {
            if (!writable) throw new UnauthorizedAccessException("읽기 전용으로 연 키");
            lock (owner._gate)
            {
                if (!node.Values.Remove(name) && throwOnMissing) throw new ArgumentException("값 없음: " + name);
            }
        }

        public string[] GetValueNames()
        {
            lock (owner._gate) return node.Values.Keys.ToArray();
        }

        public string[] GetSubKeyNames()
        {
            lock (owner._gate) return node.Children.Keys.ToArray();
        }

        public void Dispose() { }
    }
}
