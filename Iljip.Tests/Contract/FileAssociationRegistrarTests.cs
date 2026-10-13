using System.Text.RegularExpressions;
using Iljip.Services;
using Iljip.Tests.TestSupport;
using Iljip.ViewModels;
using Microsoft.Win32;

namespace Iljip.Tests.Contract;

/// <summary>
/// [W2-12 I1-1] 파일 연결 등록(<see cref="FileAssociationRegistrar"/>) — 가짜 레지스트리(<see cref="FakeRegistryRoot"/>)로만 확인한다.
/// 진짜 HKCU·SHChangeNotify·ms-settings 는 절대 부르지 않는다(설계 §5). 기본 앱(UserChoice)은 쓰지 않는다 — '기본 앱 설정' 화면을 열기만.
/// 설치 프로그램(installer/Iljip.iss [Registry])이 같은 키를 쓰는지도 소스로 맞춰 본다.
/// </summary>
public class FileAssociationRegistrarTests
{
    private const string Exe = @"C:\Program Files\일집\Iljip.exe";

    private static FileAssociationOptions Options(string? dll = null) => new() { ExePath = Exe, DropHandlerDll = dll };

    /// <summary>가짜 레지스트리의 값 전체(우리 기록 키 빼고) — (키, 이름, 값 글, 종류).</summary>
    private static HashSet<(string, string, string, RegistryValueKind)> Values(FakeRegistryRoot reg, bool includeRecord = false)
    {
        var set = new HashSet<(string, string, string, RegistryValueKind)>();
        foreach (var key in reg.AllKeys())
        {
            if (!includeRecord && key.StartsWith(FileAssociationRegistrar.RecordKey, StringComparison.OrdinalIgnoreCase)) continue;
            using var k = reg.OpenSubKey(key, writable: false)!;
            foreach (var name in k.GetValueNames())
                set.Add((key.ToLowerInvariant(), name, Render(k.GetValue(name)), k.GetValueKind(name)!.Value));
        }
        return set;
    }

    private static string Render(object? v) => v switch
    {
        byte[] b => "bytes:" + Convert.ToHexString(b),
        string[] a => "multi:" + string.Join("|", a),
        _ => Convert.ToString(v, System.Globalization.CultureInfo.InvariantCulture) ?? "",
    };

    private static HashSet<(string, string, string, RegistryValueKind)> Expected(FileAssociationOptions o) =>
        FileAssociationRegistrar.ExpectedEntries(o).Select(e => (e.Key.ToLowerInvariant(), e.Name, Render(e.Value), e.Kind)).ToHashSet();

    /// <summary>등록하면 기대 키·값 전체 집합과 정확히 같고(남는 것도 빠진 것도 없음), 경로 따옴표·%1·아이콘·REG_NONE 이 맞다.</summary>
    [Fact]
    public void 등록하면_기대_키와_값_전체와_같다()
    {
        var reg = FakeRegistryRoot.WithDefaults();
        var result = new FileAssociationRegistrar(reg).Register(Options());

        Assert.True(result.Changed);
        Assert.Equal(Expected(Options()), Values(reg));
        Assert.Equal(21, FileAssociationRegistrar.Extensions.Count);

        // 정렬한 .reg 글로 대표 묶음(.7z)을 글자 그대로 확인 — 따옴표·%1·아이콘 경로·REG_NONE
        // (.zip 은 정렬하면 Iljip.zipx 가 사이에 끼어 한 덩어리로 못 봄 — 값은 아래 Value 로 따로)
        string text = reg.ExportReg();
        Assert.Contains(
            "[HKEY_CURRENT_USER\\Software\\Classes\\Iljip.7z]\n" +
            "@=\"7Z 압축 파일\"\n" +
            "\"FriendlyTypeName\"=\"7Z 압축 파일\"\n\n" +
            "[HKEY_CURRENT_USER\\Software\\Classes\\Iljip.7z\\DefaultIcon]\n" +
            "@=\"C:\\\\Program Files\\\\일집\\\\icons\\\\7z.ico\"\n\n" +
            "[HKEY_CURRENT_USER\\Software\\Classes\\Iljip.7z\\shell]\n" +
            "@=\"open\"\n\n" +
            "[HKEY_CURRENT_USER\\Software\\Classes\\Iljip.7z\\shell\\open]\n" +
            "@=\"일집으로 열기\"\n\n" +
            "[HKEY_CURRENT_USER\\Software\\Classes\\Iljip.7z\\shell\\open\\command]\n" +
            "@=\"\\\"C:\\\\Program Files\\\\일집\\\\Iljip.exe\\\" --open \\\"%1\\\"\"\n\n",
            text);
        Assert.Contains("[HKEY_CURRENT_USER\\Software\\Classes\\.zip\\OpenWithProgids]\n\"Iljip.zip\"=hex(0):\n\n", text);
        Assert.Equal("ZIP 압축 파일", reg.Value(@"Software\Classes\Iljip.zip", ""));
        Assert.Equal(@"C:\Program Files\일집\icons\zip.ico", reg.Value(@"Software\Classes\Iljip.zip\DefaultIcon", ""));
        Assert.Equal("\"C:\\Program Files\\일집\\Iljip.exe\" --open \"%1\"", reg.Value(@"Software\Classes\Iljip.zip\shell\open\command", ""));
        Assert.Equal("\"C:\\Program Files\\일집\\Iljip.exe\" --open \"%1\"", reg.Value(@"Software\Classes\Iljip.egg\shell\open\command", ""));
        Assert.Equal(@"C:\Program Files\일집\icons\alz.ico", reg.Value(@"Software\Classes\Iljip.alz\DefaultIcon", ""));
        Assert.Equal("EGG 압축 파일(알집)", reg.Value(@"Software\Classes\Iljip.egg", "FriendlyTypeName"));

        // 기본 앱 목록(Capabilities)·RegisteredApplications — iso 는 '연결 프로그램' 후보로만
        Assert.Equal(@"Software\Iljip\Capabilities", reg.Value(@"Software\RegisteredApplications", "일집"));
        Assert.Equal("Iljip.zip", reg.Value(@"Software\Iljip\Capabilities\FileAssociations", ".zip"));
        Assert.Null(reg.Value(@"Software\Iljip\Capabilities\FileAssociations", ".iso"));
        Assert.Equal(RegistryValueKind.None, reg.Kind(@"Software\Classes\.iso\OpenWithProgids", "Iljip.iso"));
        Assert.Equal("", reg.Value(@"Software\Classes\Applications\Iljip.exe\SupportedTypes", ".iso"));
        Assert.Equal("일집", reg.Value(FileAssociationRegistrar.ApplicationsKey, "FriendlyAppName"));

        // UserChoice(기본 앱 — 해시로 보호)는 절대 쓰지 않는다. .zip 의 기본값(기본 앱)도 쓰지 않는다
        Assert.DoesNotContain(reg.AllKeys(), k => k.Contains("UserChoice", StringComparison.OrdinalIgnoreCase));
        Assert.Null(reg.Value(@"Software\Classes\.zip", ""));
        // 드롭 처리기는 고르지 않으면 없다
        Assert.False(reg.KeyExists(FileAssociationRegistrar.DropHandlerClsidKey));
        Assert.False(reg.KeyExists(@"Software\Classes\Iljip.zip\shellex"));
        Assert.True(new FileAssociationRegistrar(reg).IsRegistered());
    }

    [Fact]
    public void 두_번_등록해도_결과가_같다()
    {
        var reg = FakeRegistryRoot.WithDefaults();
        new FileAssociationRegistrar(reg).Register(Options());
        string first = reg.ExportReg();
        new FileAssociationRegistrar(reg).Register(Options());
        Assert.Equal(first, reg.ExportReg());
    }

    /// <summary>다른 프로그램의 OpenWithProgids 값·.zip 기본값·그 프로그램 ProgId·RegisteredApplications 값은 등록·해제 모두 그대로.</summary>
    [Fact]
    public void 다른_프로그램_값은_건드리지_않고_해제하면_처음과_같다()
    {
        var reg = FakeRegistryRoot.WithDefaults();
        using (var k = reg.CreateSubKey(@"Software\Classes\.zip")) k.SetValue("", "CompressedFolder", RegistryValueKind.String);
        using (var k = reg.CreateSubKey(@"Software\Classes\.zip\OpenWithProgids")) k.SetValue("7-Zip.zip", Array.Empty<byte>(), RegistryValueKind.None);
        using (var k = reg.CreateSubKey(@"Software\Classes\7-Zip.zip\shell\open\command")) k.SetValue("", "\"C:\\7z\\7zFM.exe\" \"%1\"", RegistryValueKind.String);
        using (var k = reg.CreateSubKey(@"Software\RegisteredApplications")) k.SetValue("OneDrive", @"SOFTWARE\Microsoft\OneDrive\Capabilities", RegistryValueKind.String);
        using (var k = reg.CreateSubKey(@"Software\Classes\.rar")) k.SetValue("", "WinRAR", RegistryValueKind.String);
        string before = reg.ExportReg();

        new FileAssociationRegistrar(reg).Register(Options(@"C:\Program Files\일집\Iljip.ShellExtension.dll"));
        Assert.Equal("CompressedFolder", reg.Value(@"Software\Classes\.zip", ""));
        Assert.Equal(RegistryValueKind.None, reg.Kind(@"Software\Classes\.zip\OpenWithProgids", "7-Zip.zip"));
        Assert.Equal(RegistryValueKind.None, reg.Kind(@"Software\Classes\.zip\OpenWithProgids", "Iljip.zip"));
        Assert.Equal("WinRAR", reg.Value(@"Software\Classes\.rar", ""));
        Assert.Equal(@"SOFTWARE\Microsoft\OneDrive\Capabilities", reg.Value(@"Software\RegisteredApplications", "OneDrive"));

        var result = new FileAssociationRegistrar(reg).Unregister();
        Assert.True(result.Changed);
        Assert.Equal(FileAssociationRegistrar.UnregisteredMessage, result.Message);
        Assert.Equal(before, reg.ExportReg());   // 남의 것은 그대로, 우리가 만든 것만 지워 처음과 같다
        Assert.False(new FileAssociationRegistrar(reg).IsRegistered());
    }

    /// <summary>빈 HKCU(Software\Classes 만)에서 넣고 빼면 처음과 같다 — 우리가 만든 빈 키(.egg·Applications·Software\Iljip 등)까지 지움, 남는 것 0.</summary>
    [Fact]
    public void 해제하면_우리가_만든_것만_지워_남는_것이_없다()
    {
        var reg = FakeRegistryRoot.WithDefaults();
        var keysBefore = reg.AllKeys();
        string before = reg.ExportReg();
        new FileAssociationRegistrar(reg).Register(Options(@"C:\Program Files\일집\Iljip.ShellExtension.dll"));
        Assert.True(reg.AllKeys().Count > 100);
        new FileAssociationRegistrar(reg).Unregister();
        Assert.Equal(keysBefore, reg.AllKeys());
        Assert.Equal(before, reg.ExportReg());
        Assert.Empty(Values(reg, includeRecord: true));
    }

    /// <summary>기록(RegisteredKeys)이 없으면 아무것도 지우지 않는다 — 다른 경로(설치 프로그램 등)가 만든 Iljip.* 도 그대로.</summary>
    [Fact]
    public void 기록이_없으면_아무것도_지우지_않는다()
    {
        var reg = FakeRegistryRoot.WithDefaults();
        using (var k = reg.CreateSubKey(@"Software\Classes\Iljip.zip")) k.SetValue("", "ZIP 압축 파일", RegistryValueKind.String);
        using (var k = reg.CreateSubKey(@"Software\Classes\.zip\OpenWithProgids")) k.SetValue("Iljip.zip", Array.Empty<byte>(), RegistryValueKind.None);
        string before = reg.ExportReg();

        var result = new FileAssociationRegistrar(reg).Unregister();
        Assert.False(result.Changed);
        Assert.Equal(FileAssociationRegistrar.NotRegisteredMessage, result.Message);
        Assert.Equal(before, reg.ExportReg());
    }

    /// <summary>기록이 바뀌어 남의 키·값을 가리켜도(허용된 모양이 아니면) 지우지 않는다.</summary>
    [Fact]
    public void 기록이_남의_키를_가리켜도_지우지_않는다()
    {
        var reg = FakeRegistryRoot.WithDefaults();
        using (var k = reg.CreateSubKey(@"Software\Classes\7-Zip.zip")) k.SetValue("", "7-Zip", RegistryValueKind.String);
        using (var k = reg.CreateSubKey(@"Software\Classes\.zip")) k.SetValue("", "CompressedFolder", RegistryValueKind.String);
        using (var k = reg.CreateSubKey(@"Software\Microsoft\Windows")) k.SetValue("x", "y", RegistryValueKind.String);
        new FileAssociationRegistrar(reg).Register(Options());
        using (var rec = reg.OpenSubKey(FileAssociationRegistrar.RecordKey, writable: true)!)
        {
            rec.SetValue("OwnedKeys", new[] { @"Software\Classes\7-Zip.zip", @"Software\Microsoft", @"Software\Classes\Iljip.zip" }, RegistryValueKind.MultiString);
            rec.SetValue("SharedValues", new[] { @"Software\Classes\.zip|", @"Software\Classes\.zip\OpenWithProgids|Iljip.zip" }, RegistryValueKind.MultiString);
            rec.SetValue("CreatedKeys", new[] { @"Software\Classes\.zip", @"Software\Microsoft\Windows" }, RegistryValueKind.MultiString);
        }

        new FileAssociationRegistrar(reg).Unregister();
        Assert.Equal("7-Zip", reg.Value(@"Software\Classes\7-Zip.zip", ""));
        Assert.Equal("CompressedFolder", reg.Value(@"Software\Classes\.zip", ""));
        Assert.Equal("y", reg.Value(@"Software\Microsoft\Windows", "x"));
        Assert.False(reg.KeyExists(@"Software\Classes\Iljip.zip"));               // 우리 것(허용 모양)은 지움
        Assert.Null(reg.Value(@"Software\Classes\.zip\OpenWithProgids", "Iljip.zip"));
    }

    /// <summary>드롭 처리기(선택): zip·7z ProgId 의 shellex\DropHandler 와 CLSID\{…}\InprocServer32(ThreadingModel=Apartment). 끄고 다시 등록하면 둘 다 지운다.</summary>
    [Fact]
    public void 드롭_처리기는_고를_때만_zip과_7z에()
    {
        const string dll = @"C:\Program Files\일집\Iljip.ShellExtension.dll";
        var reg = FakeRegistryRoot.WithDefaults();
        new FileAssociationRegistrar(reg).Register(Options(dll));

        string clsid = "{61C3F459-E40B-441D-9F76-F56C70DFF8C1}";
        Assert.Equal(clsid, FileAssociationRegistrar.DropHandlerClsidText);
        Assert.Equal(clsid, reg.Value(@"Software\Classes\Iljip.zip\shellex\DropHandler", ""));
        Assert.Equal(clsid, reg.Value(@"Software\Classes\Iljip.7z\shellex\DropHandler", ""));
        Assert.False(reg.KeyExists(@"Software\Classes\Iljip.rar\shellex"));
        Assert.False(reg.KeyExists(@"Software\Classes\Iljip.egg\shellex"));
        Assert.Equal(dll, reg.Value($@"Software\Classes\CLSID\{clsid}\InprocServer32", ""));
        Assert.Equal("Apartment", reg.Value($@"Software\Classes\CLSID\{clsid}\InprocServer32", "ThreadingModel"));
        Assert.Equal(FileAssociationRegistrar.DropHandlerName, reg.Value($@"Software\Classes\CLSID\{clsid}", ""));
        Assert.Equal(Expected(Options(dll)), Values(reg));

        // 끄고 다시: 드롭 처리기만 사라지고 나머지는 같다(예전 기록에만 있던 것은 지움)
        new FileAssociationRegistrar(reg).Register(Options());
        Assert.False(reg.KeyExists(@"Software\Classes\Iljip.zip\shellex"));
        Assert.False(reg.KeyExists($@"Software\Classes\CLSID\{clsid}"));
        Assert.Equal(Expected(Options()), Values(reg));
    }

    /// <summary>쓰기가 막히면(회사 정책 등) 쉬운 한국어로 알리고 예외로 멈추지 않는다.</summary>
    [Fact]
    public void 쓰기가_막히면_쉬운_말로_알린다()
    {
        var svc = new TestServices();
        svc.Registry.DenyWrites.Add(@"Software\Classes\Iljip");
        var run = FileAssociationCommand.Register(svc.Services, Exe, dropHandler: false);
        Assert.False(run.Ok);
        Assert.Equal(1, run.ExitCode);
        Assert.StartsWith("파일 연결을 넣지 못했어요.", run.Message);
        Assert.DoesNotContain("Exception", run.Message);
        Assert.Equal(0, svc.Shell.AssociationsChangedNotified);
    }

    // ───────────── 화면(☰ ▸ 설정 '파일 연결')·명령줄 ─────────────

    /// <summary>[기본 앱 설정 열기]는 가짜 셸에 한 번(진짜 ms-settings 는 안 엶). 기본 앱(UserChoice)은 일집이 바꾸지 않는다.</summary>
    [Fact]
    public void 기본_앱_설정_열기는_셸을_한번_부른다()
    {
        var svc = new TestServices();
        var vm = new MainViewModel(new FakeUserInteraction(), null, svc.Services);
        Assert.Null(vm.OpenDefaultAppsSettings());
        Assert.Equal(1, svc.Shell.DefaultAppsOpened);
        Assert.Equal("ms-settings:defaultapps?registeredAppUser=일집", ShellLauncher.DefaultAppsUri);
        Assert.Equal("ms-settings:defaultapps", ShellLauncher.DefaultAppsFallbackUri);
        Assert.DoesNotContain(svc.Registry.AllKeys(), k => k != "Software" && k != @"Software\Classes");   // 레지스트리는 건드리지 않음
    }

    /// <summary>설정 창 [연결 프로그램에 넣기]·[연결 빼기]: 지금 일집 경로로 넣고, 탐색기에 알리고, shell.associationsRegistered 를 한 번 저장.</summary>
    [Fact]
    public void 설정_창_단추로_넣고_뺀다()
    {
        var svc = new TestServices();
        var vm = new MainViewModel(new FakeUserInteraction(), null, svc.Services);
        Assert.False(vm.IsFileAssociationRegistered);
        Assert.False(vm.CanRegisterDropHandler);   // 가짜 일집 경로 옆에 셸 확장 DLL 없음

        string msg = vm.RegisterFileAssociations(dropHandler: false);
        Assert.Equal(FileAssociationRegistrar.RegisteredMessage, msg);
        Assert.True(vm.IsFileAssociationRegistered);
        Assert.Equal(FileAssociationRegistrar.OpenCommand(svc.Processes.ExecutablePath!), svc.Registry.Value(@"Software\Classes\Iljip.7z\shell\open\command", ""));
        Assert.Equal(1, svc.Shell.AssociationsChangedNotified);
        Assert.True(vm.Settings.Shell.AssociationsRegistered);
        Assert.Contains("\"associationsRegistered\": true", svc.Settings.Json);

        // 드롭 처리기를 골랐는데 DLL 이 없으면 넣지 않고 그렇다고 알림
        string withDrop = vm.RegisterFileAssociations(dropHandler: true);
        Assert.EndsWith(FileAssociationCommand.NoDropHandlerNote, withDrop);
        Assert.False(svc.Registry.KeyExists(FileAssociationRegistrar.DropHandlerClsidKey));

        Assert.Equal(FileAssociationRegistrar.UnregisteredMessage, vm.UnregisterFileAssociations());
        Assert.False(vm.IsFileAssociationRegistered);
        Assert.False(vm.Settings.Shell.AssociationsRegistered);
        Assert.Contains("\"associationsRegistered\": false", svc.Settings.Json);
        Assert.Equal(FileAssociationRegistrar.NotRegisteredMessage, vm.UnregisterFileAssociations());
    }

    /// <summary>설정 창을 연 뒤 [연결 프로그램에 넣기] → [확인]: 창이 열릴 때의 사본(shell 칸 옛 값)이 기록을 되돌리지 않는다.</summary>
    [Fact]
    public void 설정_창_확인이_파일_연결_기록을_되돌리지_않는다()
    {
        var svc = new TestServices();
        var vm = new MainViewModel(new FakeUserInteraction(), null, svc.Services);
        var dialogCopy = vm.Settings.Clone();          // 설정 창이 열릴 때 받은 사본
        vm.RegisterFileAssociations(dropHandler: false);
        dialogCopy.Extract.OpenFolderAfter = false;    // 창에서 다른 칸을 바꾸고 [확인]
        vm.ApplySettings(dialogCopy);
        Assert.True(vm.Settings.Shell.AssociationsRegistered);
        Assert.False(vm.Settings.Extract.OpenFolderAfter);
        Assert.Contains("\"associationsRegistered\": true", svc.Settings.Json);
    }

    /// <summary>
    /// 명령줄 '--register-associations [--drop-handler]'·'--unregister-associations'(자체 프로세스 — App 이 이것만 부르고 끝남):
    /// 셸 확장 DLL 이 일집 옆에 있으면 드롭 처리기도. 종료 코드 0.
    /// </summary>
    [Fact]
    public void 명령줄로_넣고_뺀다()
    {
        using var tmp = new TempDir("assoc");
        string exe = tmp.P("Iljip.exe");
        File.WriteAllBytes(exe, Array.Empty<byte>());
        File.WriteAllBytes(tmp.P(FileAssociationCommand.ShellExtensionDllName), Array.Empty<byte>());
        var svc = new TestServices();

        var run = FileAssociationCommand.Run(CommandLineParser.Parse(new[] { "--register-associations", "--drop-handler" }), svc.Services, exe)!;
        Assert.True(run.Ok);
        Assert.Equal(0, run.ExitCode);
        Assert.Equal(tmp.P(FileAssociationCommand.ShellExtensionDllName),
            svc.Registry.Value(FileAssociationRegistrar.DropHandlerClsidKey + @"\InprocServer32", ""));
        Assert.Equal(1, svc.Shell.AssociationsChangedNotified);
        Assert.Contains("\"associationsRegistered\": true", svc.Settings.Json);

        var off = FileAssociationCommand.Run(CommandLineParser.Parse(new[] { "--unregister-associations" }), svc.Services, exe)!;
        Assert.True(off.Ok);
        Assert.False(svc.Registry.KeyExists(@"Software\Classes\Iljip.zip"));
        Assert.Contains("\"associationsRegistered\": false", svc.Settings.Json);

        Assert.Null(FileAssociationCommand.Run(CommandLineParser.Parse(new[] { "--open", "a.zip" }), svc.Services, exe));
        Assert.False(FileAssociationCommand.Register(svc.Services, null, false).Ok);
    }

    /// <summary>
    /// '.z01'(PKZIP 나눔 앞 조각)도 연결에 들어가므로 두 번 눌러 '--open 이름.z01' 이 오면 같은 세트의 '이름.zip'(마지막 조각 — 나눔 ZIP 을 여는 파일)으로 연다.
    /// 짝 .zip 이 없거나 다른 형식이면 받은 그대로.
    /// </summary>
    [Fact]
    public async Task z01을_열면_짝_zip으로_연다()
    {
        using var tmp = new TempDir("z01");
        foreach (var v in FixtureCatalog.Get("zip_split").SplitVolumes)
            File.Copy(FixtureCatalog.PathOf(v), tmp.P(Path.GetFileName(v)));
        string z01 = tmp.P("zip_split.z01"), zip = tmp.P("zip_split.zip");
        Assert.Equal(zip, ArchiveVolumes.OpenTargetFor(z01));
        Assert.Equal(zip, ArchiveVolumes.OpenTargetFor(tmp.P("zip_split.z02")));
        Assert.Equal(zip, ArchiveVolumes.OpenTargetFor(zip));
        Assert.Equal(tmp.P("짝없음.z01"), ArchiveVolumes.OpenTargetFor(tmp.P("짝없음.z01")));
        Assert.Equal(@"C:\a\b.7z.001", ArchiveVolumes.OpenTargetFor(@"C:\a\b.7z.001"));

        var ui = new FakeUserInteraction();
        var vm = new MainViewModel(ui, null, new TestServices().Services);
        await VmRunner.Run(() => vm.ProcessCommandLineAsync(new[] { "--open", z01 }), "--open .z01");
        Assert.Equal(zip, vm.CurrentArchivePath);
        Assert.Empty(ui.Errors);
    }

    /// <summary>설정 shell.*(W2-12): 기본 꺼짐, 옛 settings.json(칸 없음·null)은 기본값으로, 모르는 칸은 남기고, 사본(설정 창)에도 들어간다.</summary>
    [Fact]
    public void 설정의_shell_칸()
    {
        var o = Iljip.Services.JsonSettingsStore.Options;
        var s = new AppSettings();
        Assert.False(s.Shell.AssociationsRegistered);
        Assert.Contains("\"shell\": {", System.Text.Json.JsonSerializer.Serialize(s, o));

        var old = System.Text.Json.JsonSerializer.Deserialize<AppSettings>("{\"extract\":{}}", o)!.Normalize();
        Assert.False(old.Shell.AssociationsRegistered);
        var nul = System.Text.Json.JsonSerializer.Deserialize<AppSettings>("{\"shell\":null}", o)!.Normalize();
        Assert.NotNull(nul.Shell);

        var extra = System.Text.Json.JsonSerializer.Deserialize<AppSettings>("{\"shell\":{\"associationsRegistered\":true,\"future\":1}}", o)!.Normalize();
        Assert.True(extra.Shell.AssociationsRegistered);
        Assert.Contains("\"future\": 1", System.Text.Json.JsonSerializer.Serialize(extra, o));
        Assert.True(extra.Clone().Shell.AssociationsRegistered);
    }

    // ───────────── 설치 프로그램(installer/Iljip.iss) ─────────────

    private static readonly Regex IssRegistryLine = new(
        @"^Root:\s*HKA;\s*Subkey:\s*""(?<key>[^""]*)"";\s*ValueType:\s*(?<type>\w+);\s*ValueName:\s*""(?<name>(?:[^""]|"""")*)"";\s*ValueData:\s*""(?<data>(?:[^""]|"""")*)""(?:;\s*Flags:\s*(?<flags>[\w ]+))?\s*$",
        RegexOptions.CultureInvariant);

    /// <summary>
    /// installer/Iljip.iss: ChangesAssociations=yes, [Registry] 는 Root: HKA(관리자 설치면 HKLM, 사용자 설치면 HKCU)로 등록기와 같은 키·값(설치 폴더 = {app}),
    /// ProgId·Applications·Capabilities 는 uninsdeletekey, 남과 같이 쓰는 값은 uninsdeletevalue, 아이콘 파일을 {app}\icons 에.
    /// (OpenWithProgids 는 Inno 관례대로 빈 문자열 — 등록기는 REG_NONE. Windows 는 값 이름만 본다)
    /// </summary>
    [Fact]
    public void 설치_스크립트가_같은_키를_쓴다()
    {
        string iss = RepoFiles.Read("installer", "Iljip.iss");
        Assert.Contains("\nChangesAssociations=yes", iss.Replace("\r\n", "\n"));
        Assert.Contains(@"Source: ""{#PublishDir}\icons\*.ico""; DestDir: ""{app}\icons""", iss);
        Assert.Contains(@"Excludes: ""icons\*""", iss);

        var lines = iss.Replace("\r\n", "\n").Split('\n').Where(l => l.StartsWith("Root: HKA;", StringComparison.Ordinal)).ToList();
        var parsed = new HashSet<(string, string, string, RegistryValueKind)>();
        var flags = new Dictionary<(string Key, string Name), string>();
        foreach (var line in lines)
        {
            if (line == @"Root: HKA; Subkey: ""Software\Iljip""; Flags: uninsdeletekeyifempty") continue;
            var m = IssRegistryLine.Match(line);
            Assert.True(m.Success, "[Registry] 줄 모양이 다름: " + line);
            Assert.Equal("string", m.Groups["type"].Value);
            string key = m.Groups["key"].Value, name = m.Groups["name"].Value.Replace("\"\"", "\""), data = m.Groups["data"].Value.Replace("\"\"", "\"");
            var kind = key.EndsWith(@"\OpenWithProgids", StringComparison.OrdinalIgnoreCase) && data.Length == 0 ? RegistryValueKind.None : RegistryValueKind.String;
            parsed.Add((key.ToLowerInvariant(), name, kind == RegistryValueKind.None ? "bytes:" : data, kind));
            flags[(key.ToLowerInvariant(), name)] = m.Groups["flags"].Value;
        }
        var expected = Expected(new FileAssociationOptions { ExePath = @"{app}\Iljip.exe" });
        Assert.Equal(expected, parsed);

        // 제거 플래그: 우리 키는 키째, 같이 쓰는 키는 우리 값만
        foreach (var e in FileAssociationRegistrar.Extensions)
        {
            Assert.Equal("uninsdeletekey", flags[($@"software\classes\{FileAssociationRegistrar.ProgIdFor(e)}".ToLowerInvariant(), "")]);
            Assert.Equal("uninsdeletevalue", flags[($@"software\classes\{e}\openwithprogids", FileAssociationRegistrar.ProgIdFor(e))]);
        }
        Assert.Equal("uninsdeletekey", flags[(FileAssociationRegistrar.ApplicationsKey.ToLowerInvariant(), "FriendlyAppName")]);
        Assert.Equal("uninsdeletekey", flags[(FileAssociationRegistrar.CapabilitiesKey.ToLowerInvariant(), "ApplicationName")]);
        Assert.Equal("uninsdeletevalue", flags[(FileAssociationRegistrar.RegisteredApplicationsKey.ToLowerInvariant(), "일집")]);
        // 기본 앱(UserChoice)·드롭 처리기(셸 확장 DLL 은 설치 프로그램이 넣지 않음)는 없다
        Assert.DoesNotContain(lines, l => l.Contains("UserChoice", StringComparison.OrdinalIgnoreCase) || l.Contains("DropHandler", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(lines, l => l.Contains(@"Software\Classes\.zip""; ValueType: string; ValueName: """"", StringComparison.OrdinalIgnoreCase));   // .zip 기본값(기본 앱)도 안 씀
        // 아이콘 파일이 저장소에 다 있다(설치 프로그램·셸 메뉴가 쓰는 것)
        foreach (var e in FileAssociationRegistrar.Extensions)
            Assert.True(File.Exists(RepoFiles.PathOf("Iljip", "icons", e.TrimStart('.') + ".ico")), e + " 아이콘 없음");
        Assert.True(File.Exists(RepoFiles.PathOf("Iljip", "icons", "iljip.ico")));
    }
}
