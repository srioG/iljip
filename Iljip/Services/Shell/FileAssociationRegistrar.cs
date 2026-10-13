using System.IO;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace Iljip.Services;

/// <summary>레지스트리 값 하나(키는 뿌리 아래 상대 경로, 이름 "" = (기본값)).</summary>
public sealed record RegistryEntry(string Key, string Name, object Value, RegistryValueKind Kind);

/// <summary>파일 연결 등록 옵션(W2-12).</summary>
public sealed class FileAssociationOptions
{
    /// <summary>일집 실행 파일 전체 경로('"…\Iljip.exe" --open "%1"' 에 들어감).</summary>
    public required string ExePath { get; init; }

    /// <summary>
    /// 압축 파일 위에 끌어 놓아 넣기(드롭 처리기 — I3-10)를 등록할 셸 확장 DLL 경로. null 이면 등록하지 않는다(기본).
    /// 패키지 매니페스트로는 선언할 수 없어 고전 COM(HKCU\Software\Classes\CLSID\{…}\InprocServer32)으로만 된다.
    /// </summary>
    public string? DropHandlerDll { get; init; }

    /// <summary>설치 폴더(실행 파일이 있는 폴더).</summary>
    public string InstallFolder => Path.GetDirectoryName(ExePath) ?? string.Empty;

    /// <summary>형식 아이콘 파일 — '&lt;설치 폴더&gt;\icons\zip.ico'(exe 리소스가 아니라 파일 — rc 컴파일러 없이 바꿀 수 있게).</summary>
    public string IconPathFor(string extension) => Path.Combine(InstallFolder, "icons", extension.TrimStart('.').ToLowerInvariant() + ".ico");
}

/// <summary>등록·해제 결과(화면 문구용).</summary>
public sealed record AssociationResult(bool Changed, int Extensions, string Message);

/// <summary>
/// 확장자 연결(W2-12, I1-1) — HKCU\Software\Classes 아래에 '연결 프로그램' 후보로만 넣는다(사용자별, 관리자 권한 불필요). 기본 앱을 빼앗지 않는다:
/// <list type="bullet">
/// <item>ProgId <c>Iljip.&lt;ext&gt;</c>(이름·아이콘 '&lt;설치 폴더&gt;\icons\&lt;ext&gt;.ico'·'일집으로 열기' = <c>"&lt;exe&gt;" --open "%1"</c>)</item>
/// <item><c>.&lt;ext&gt;\OpenWithProgids</c> 값 <c>Iljip.&lt;ext&gt;</c>(REG_NONE) — '연결 프로그램' 목록에만 들어감(기존 기본값·다른 프로그램 값은 손대지 않음)</item>
/// <item><c>Applications\Iljip.exe</c>(FriendlyAppName·SupportedTypes), <c>Software\Iljip\Capabilities</c> + <c>RegisteredApplications</c> 값 '일집' — Windows '기본 앱' 목록에 나옴</item>
/// <item>(선택) .zip·.7z 드롭 처리기 — <c>Iljip.zip\shellex\DropHandler</c> + <c>CLSID\{…}\InprocServer32</c>(ThreadingModel=Apartment)</item>
/// </list>
/// Windows 11 의 UserChoice(기본 앱)는 해시로 보호돼 프로그램이 못 바꾼다 — 절대 쓰지 않고 '기본 앱 설정' 화면을 연다(<see cref="IShellLauncher.OpenDefaultAppsSettings"/>).
/// iso 는 Windows 디스크 이미지(탑재) 기본 연결이 있어 '연결 프로그램' 후보로만 넣고 Capabilities(기본 앱 목록)에는 넣지 않는다.
/// 우리가 쓴 키·값은 <c>Software\Iljip\RegisteredKeys</c> 에 적고, 해제는 정확히 그것만(허용된 모양만) 지운다 — 기록이 없으면 아무것도 지우지 않는다.
/// 설치 프로그램(installer/Iljip.iss [Registry])은 같은 키를 Root: HKA 로 쓴다(계약 시험이 맞춰 봄). 적용 뒤 SHChangeNotify 는 부르는 쪽(진짜 셸) 몫.
/// </summary>
public sealed class FileAssociationRegistrar
{
    public const string AppName = "일집";
    public const string AppDescription = "광고 없는 압축 프로그램";
    public const string ProgIdPrefix = "Iljip.";
    public const string ClassesKey = @"Software\Classes";
    public const string AppKey = @"Software\Iljip";
    public const string CapabilitiesKey = @"Software\Iljip\Capabilities";
    public const string RecordKey = @"Software\Iljip\RegisteredKeys";
    public const string RegisteredApplicationsKey = @"Software\RegisteredApplications";
    public const string ApplicationsKey = @"Software\Classes\Applications\Iljip.exe";
    public const string OpenVerbTitle = "일집으로 열기";
    public const string DropHandlerName = "일집 끌어 놓기 처리기";

    /// <summary>드롭 처리기 CLSID — C++ IljipCommands.h <c>CLSID_IljipDropHandler</c> 와 같은 값(ShellExtensionContractTests).</summary>
    public static readonly Guid DropHandlerClsid = new("61C3F459-E40B-441D-9F76-F56C70DFF8C1");

    /// <summary>연결하는 확장자(설계 §1 I1-1). '.001'·'.z01' 은 다른 프로그램(7-Zip·반디집)과 겹치지만 '연결 프로그램' 후보로만 넣으므로 빼앗지 않는다.</summary>
    public static IReadOnlyList<string> Extensions { get; } = new[]
    {
        ".zip", ".7z", ".rar", ".tar", ".gz", ".tgz", ".bz2", ".tbz2", ".tbz", ".xz", ".txz", ".zipx",
        ".lzh", ".lha", ".cab", ".wim", ".egg", ".alz", ".001", ".z01", ".iso",
    };

    /// <summary>'연결 프로그램' 후보로만(기본 앱 목록 Capabilities 에 넣지 않음) — Windows 기본 연결이 있는 형식.</summary>
    public static IReadOnlyList<string> OpenWithOnlyExtensions { get; } = new[] { ".iso" };

    /// <summary>드롭 처리기를 다는 형식 — 일집이 고칠 수 있는(W2-06 '--add-to') ZIP·7Z.</summary>
    public static IReadOnlyList<string> DropHandlerExtensions { get; } = new[] { ".zip", ".7z" };

    private readonly IRegistryRoot _root;

    public FileAssociationRegistrar(IRegistryRoot root) => _root = root;

    public static string ProgIdFor(string extension) => ProgIdPrefix + extension.TrimStart('.').ToLowerInvariant();

    /// <summary>탐색기 '유형' 칸에 보일 이름.</summary>
    public static string TypeNameFor(string extension) => extension.ToLowerInvariant() switch
    {
        ".tar" => "TAR 묶음 파일",
        ".iso" => "ISO 디스크 이미지",
        ".wim" => "WIM 이미지 파일",
        ".egg" => "EGG 압축 파일(알집)",
        ".alz" => "ALZ 압축 파일(알집)",
        ".001" => "나뉜 압축 파일 첫 조각",
        ".z01" => "나뉜 ZIP 첫 조각",
        var e => e.TrimStart('.').ToUpperInvariant() + " 압축 파일",
    };

    /// <summary>열기 명령 — 경로에 빈칸·한글이 있어도 되게 둘 다 큰따옴표.</summary>
    public static string OpenCommand(string exePath) => $"\"{exePath}\" --open \"%1\"";

    public static string DropHandlerClsidText => DropHandlerClsid.ToString("B").ToUpperInvariant();

    public static string DropHandlerClsidKey => $@"{ClassesKey}\CLSID\{DropHandlerClsidText}";

    // ───────────── 무엇을 쓰는가 ─────────────

    /// <summary>쓸 값 전부(정해진 차례). 우리 키(<see cref="OwnedKeys"/>)와 남과 같이 쓰는 키의 우리 값(<see cref="SharedValues"/>)으로 나뉜다.</summary>
    public static IReadOnlyList<RegistryEntry> ExpectedEntries(FileAssociationOptions o)
    {
        var list = new List<RegistryEntry>();
        string cmd = OpenCommand(o.ExePath);
        bool drop = o.DropHandlerDll is not null;
        foreach (var ext in Extensions)
        {
            string progId = $@"{ClassesKey}\{ProgIdFor(ext)}";
            string name = TypeNameFor(ext);
            list.Add(Sz(progId, "", name));
            list.Add(Sz(progId, "FriendlyTypeName", name));
            list.Add(Sz(progId + @"\DefaultIcon", "", o.IconPathFor(ext)));
            list.Add(Sz(progId + @"\shell", "", "open"));
            list.Add(Sz(progId + @"\shell\open", "", OpenVerbTitle));
            list.Add(Sz(progId + @"\shell\open\command", "", cmd));
            if (drop && DropHandlerExtensions.Contains(ext))
                list.Add(Sz(progId + @"\shellex\DropHandler", "", DropHandlerClsidText));
            list.Add(new RegistryEntry($@"{ClassesKey}\{ext}\OpenWithProgids", ProgIdFor(ext), Array.Empty<byte>(), RegistryValueKind.None));
        }

        list.Add(Sz(ApplicationsKey, "FriendlyAppName", AppName));
        list.Add(Sz(ApplicationsKey + @"\shell\open\command", "", cmd));
        foreach (var ext in Extensions)
            list.Add(Sz(ApplicationsKey + @"\SupportedTypes", ext, ""));

        list.Add(Sz(CapabilitiesKey, "ApplicationName", AppName));
        list.Add(Sz(CapabilitiesKey, "ApplicationDescription", AppDescription));
        foreach (var ext in Extensions.Except(OpenWithOnlyExtensions))
            list.Add(Sz(CapabilitiesKey + @"\FileAssociations", ext, ProgIdFor(ext)));
        list.Add(Sz(RegisteredApplicationsKey, AppName, CapabilitiesKey));

        if (drop)
        {
            list.Add(Sz(DropHandlerClsidKey, "", DropHandlerName));
            list.Add(Sz(DropHandlerClsidKey + @"\InprocServer32", "", o.DropHandlerDll!));
            list.Add(Sz(DropHandlerClsidKey + @"\InprocServer32", "ThreadingModel", "Apartment"));
        }
        return list;

        static RegistryEntry Sz(string key, string name, string value) => new(key, name, value, RegistryValueKind.String);
    }

    /// <summary>통째로 우리 것인 키(해제 때 아래까지 지움). 기록 키(<see cref="RecordKey"/>)는 따로.</summary>
    public static IReadOnlyList<string> OwnedKeys(FileAssociationOptions o)
    {
        var keys = Extensions.Select(e => $@"{ClassesKey}\{ProgIdFor(e)}").ToList();
        keys.Add(ApplicationsKey);
        keys.Add(CapabilitiesKey);
        if (o.DropHandlerDll is not null) keys.Add(DropHandlerClsidKey);
        return keys;
    }

    /// <summary>남과 같이 쓰는 키 안의 우리 값(해제 때 그 값만 지움).</summary>
    public static IReadOnlyList<(string Key, string Name)> SharedValues() =>
        Extensions.Select(e => ($@"{ClassesKey}\{e}\OpenWithProgids", ProgIdFor(e)))
                  .Append((RegisteredApplicationsKey, AppName))
                  .ToList();

    // ───────────── 등록·해제 ─────────────

    /// <summary>
    /// 등록(멱등 — 두 번 해도 결과가 같다). 순서: ① 원래 없던 키(우리가 만들게 될 키)를 미리 적어 기록(<see cref="RecordKey"/>)을 먼저 쓴다 —
    /// 도중에 실패해도 해제가 지울 수 있게 ② 우리 키는 통째로 지우고 다시 써서 예전 판이 남긴 값이 섞이지 않게 ③ 같이 쓰는 키에는 우리 값만.
    /// 예전 기록에 있는데 이번에 없는 것(드롭 처리기를 끈 경우 등)은 지운다.
    /// </summary>
    public AssociationResult Register(FileAssociationOptions o)
    {
        var entries = ExpectedEntries(o);
        var owned = OwnedKeys(o);
        var shared = SharedValues();
        var prev = ReadRecord();

        // 원래 없던 키(우리 키 자신·시스템 뿌리는 빼고) — 해제 때 비어 있으면 지운다
        var created = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var key in entries.Select(e => e.Key).Append(RecordKey).Distinct(StringComparer.OrdinalIgnoreCase))
            foreach (var path in SelfAndParents(key))
                if (!IsSystemRoot(path) && !owned.Any(k => IsSameOrUnder(path, k)) && !IsSameOrUnder(path, RecordKey) && !Exists(path))
                    created.Add(path);
        if (prev is not null)
            foreach (var c in prev.CreatedKeys.Where(IsAllowedCreated)) created.Add(c);

        // 예전 기록에만 있는 것 — 지금 판이 더는 쓰지 않음
        if (prev is not null)
        {
            foreach (var k in prev.OwnedKeys.Where(k => IsAllowedOwned(k) && !owned.Contains(k, StringComparer.OrdinalIgnoreCase)))
                _root.DeleteSubKeyTree(k, throwOnMissing: false);
            foreach (var (k, n) in prev.SharedValues.Where(s => IsAllowedShared(s.Key, s.Name) && !shared.Contains(s)))
                DeleteValue(k, n);
        }

        WriteRecord(new Record(o.ExePath, owned, shared, created.OrderBy(c => c, StringComparer.OrdinalIgnoreCase).ToList()));

        foreach (var k in owned) _root.DeleteSubKeyTree(k, throwOnMissing: false);
        foreach (var group in entries.GroupBy(e => e.Key, StringComparer.OrdinalIgnoreCase))
        {
            using var key = _root.CreateSubKey(group.Key);
            foreach (var e in group) key.SetValue(e.Name, e.Value, e.Kind);
        }

        // 예전 판이 만든 키 중 이제 비었고 쓰지 않는 것
        var needed = new HashSet<string>(entries.SelectMany(e => SelfAndParents(e.Key)), StringComparer.OrdinalIgnoreCase);
        DeleteEmpty(created.Where(c => !needed.Contains(c) && !IsSameOrUnder(RecordKey, c)));

        bool changed = prev is null || !string.Equals(prev.ExePath, o.ExePath, StringComparison.OrdinalIgnoreCase)
                       || !prev.OwnedKeys.SequenceEqual(owned, StringComparer.OrdinalIgnoreCase);
        return new AssociationResult(changed, Extensions.Count, RegisteredMessage);
    }

    /// <summary>
    /// 해제: 기록에 적힌 것만 지운다 — 우리 값(같이 쓰는 키) → 우리 키 → 기록 → 원래 없던 키 중 비어 있는 것. 기록이 없으면 아무것도 지우지 않는다(안전).
    /// 기록이 이상하게 바뀌어 있어도 허용된 모양(Iljip.* ProgId·Applications\Iljip.exe·우리 CLSID·Software\Iljip 아래, OpenWithProgids 의 Iljip.* 값,
    /// RegisteredApplications 의 '일집')이 아니면 건드리지 않는다.
    /// </summary>
    public AssociationResult Unregister()
    {
        var prev = ReadRecord();
        if (prev is null) return new AssociationResult(false, 0, NotRegisteredMessage);

        foreach (var (k, n) in prev.SharedValues.Where(s => IsAllowedShared(s.Key, s.Name)))
            DeleteValue(k, n);
        foreach (var k in prev.OwnedKeys.Where(IsAllowedOwned))
            _root.DeleteSubKeyTree(k, throwOnMissing: false);
        _root.DeleteSubKeyTree(RecordKey, throwOnMissing: false);
        DeleteEmpty(prev.CreatedKeys.Where(IsAllowedCreated));
        return new AssociationResult(true, Extensions.Count, UnregisteredMessage);
    }

    /// <summary>등록 기록이 있는가(설정 창 '지금 상태').</summary>
    public bool IsRegistered() => ReadRecord() is not null;

    public const string RegisteredMessage =
        "일집을 압축 파일의 ‘연결 프로그램’에 넣었어요. 압축 파일을 오른쪽 클릭 ▸ ‘연결 프로그램’에서 일집을 고를 수 있어요.\n" +
        "두 번 눌러 바로 일집으로 열리게 하려면 [기본 앱 설정 열기]에서 일집을 골라 주세요(Windows 가 직접 고르게 해요). iso 는 Windows 의 디스크 이미지 열기를 바꾸지 않아요.";

    public const string UnregisteredMessage = "일집이 넣었던 파일 연결을 뺐어요. 다른 프로그램의 연결은 그대로예요.";

    public const string NotRegisteredMessage = "일집이 넣은 파일 연결이 없어요(뺄 것이 없어요).";

    // ───────────── 기록(Software\Iljip\RegisteredKeys) ─────────────

    private sealed record Record(string ExePath, IReadOnlyList<string> OwnedKeys, IReadOnlyList<(string Key, string Name)> SharedValues, IReadOnlyList<string> CreatedKeys);

    private Record? ReadRecord()
    {
        using var key = _root.OpenSubKey(RecordKey, writable: false);
        if (key is null) return null;
        var owned = key.GetValue("OwnedKeys") as string[] ?? Array.Empty<string>();
        var shared = (key.GetValue("SharedValues") as string[] ?? Array.Empty<string>())
            .Select(s => s.Split('|', 2)).Where(p => p.Length == 2).Select(p => (p[0], p[1])).ToList();
        var created = key.GetValue("CreatedKeys") as string[] ?? Array.Empty<string>();
        return new Record(key.GetValue("ExePath") as string ?? string.Empty, owned, shared, created);
    }

    private void WriteRecord(Record r)
    {
        _root.DeleteSubKeyTree(RecordKey, throwOnMissing: false);
        using var key = _root.CreateSubKey(RecordKey);
        key.SetValue("Version", 1, RegistryValueKind.DWord);
        key.SetValue("ExePath", r.ExePath, RegistryValueKind.String);
        key.SetValue("OwnedKeys", r.OwnedKeys.ToArray(), RegistryValueKind.MultiString);
        key.SetValue("SharedValues", r.SharedValues.Select(s => $"{s.Key}|{s.Name}").ToArray(), RegistryValueKind.MultiString);
        key.SetValue("CreatedKeys", r.CreatedKeys.ToArray(), RegistryValueKind.MultiString);
    }

    // ───────────── 허용 모양(기록이 바뀌어 있어도 남의 것을 지우지 않게) ─────────────

    private static readonly Regex OwnedPattern = new(
        @"^Software\\Classes\\Iljip\.[a-z0-9]+$|^Software\\Classes\\Applications\\Iljip\.exe$|^Software\\Iljip\\Capabilities$|^Software\\Classes\\CLSID\\" +
        Regex.Escape(DropHandlerClsidText) + "$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static readonly Regex SharedKeyPattern = new(@"^Software\\Classes\\\.[a-z0-9]+\\OpenWithProgids$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static readonly Regex SharedNamePattern = new(@"^Iljip\.[a-z0-9]+$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static readonly Regex CreatedPattern = new(
        @"^Software\\Classes\\\.[a-z0-9]+(\\OpenWithProgids)?$|^Software\\Classes\\Applications$|^Software\\Classes\\CLSID$|^Software\\Iljip$|^Software\\RegisteredApplications$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static bool IsAllowedOwned(string key) => OwnedPattern.IsMatch(key);

    private static bool IsAllowedShared(string key, string name) =>
        (SharedKeyPattern.IsMatch(key) && SharedNamePattern.IsMatch(name))
        || (key.Equals(RegisteredApplicationsKey, StringComparison.OrdinalIgnoreCase) && name == AppName);

    private static bool IsAllowedCreated(string key) => CreatedPattern.IsMatch(key);

    // ───────────── 도우미 ─────────────

    /// <summary>늘 있는 뿌리(지우지 않음).</summary>
    private static bool IsSystemRoot(string path) =>
        path.Equals("Software", StringComparison.OrdinalIgnoreCase) || path.Equals(ClassesKey, StringComparison.OrdinalIgnoreCase);

    private static IEnumerable<string> SelfAndParents(string key)
    {
        for (string? k = key; !string.IsNullOrEmpty(k); k = k.Contains('\\') ? k[..k.LastIndexOf('\\')] : null)
            yield return k;
    }

    private static bool IsSameOrUnder(string path, string root) =>
        path.Equals(root, StringComparison.OrdinalIgnoreCase) || path.StartsWith(root + @"\", StringComparison.OrdinalIgnoreCase);

    private bool Exists(string path)
    {
        using var k = _root.OpenSubKey(path, writable: false);
        return k is not null;
    }

    private void DeleteValue(string keyPath, string name)
    {
        using var key = _root.OpenSubKey(keyPath, writable: true);
        key?.DeleteValue(name, throwOnMissing: false);
    }

    /// <summary>깊은 것부터, 값도 하위 키도 없는 것만 지운다.</summary>
    private void DeleteEmpty(IEnumerable<string> keys)
    {
        foreach (var k in keys.OrderByDescending(k => k.Count(c => c == '\\')).ThenBy(k => k, StringComparer.OrdinalIgnoreCase))
        {
            bool empty;
            using (var key = _root.OpenSubKey(k, writable: false))
            {
                if (key is null) continue;
                empty = key.GetValueNames().Length == 0 && key.GetSubKeyNames().Length == 0;
            }
            if (empty) _root.DeleteSubKeyTree(k, throwOnMissing: false);
        }
    }
}
