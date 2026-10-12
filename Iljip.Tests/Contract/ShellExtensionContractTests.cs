using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Iljip.Services;
using Iljip.Tests.TestSupport;

namespace Iljip.Tests.Contract;

/// <summary>
/// [I0-1] 셸 확장(C++ Iljip.ShellExtension)·설치 매니페스트(packaging/Package.appxmanifest)·본체(C# 명령줄) 사이의 약속.
/// 소스를 읽어 대조한다(C++ 빌드 없이도 돈다): CLSID 5개가 셋 다 같고, 메뉴 제목·넘기는 키워드가 맞는지.
/// 빌드된 DLL 이 있으면(<see cref="ShellDllFactAttribute"/>) 실제로 불러 메뉴 제목(GetTitle)이 C# 이름 규칙과 같은지도 본다 — Invoke 는 절대 부르지 않는다.
/// </summary>
public class ShellExtensionContractTests
{
    private static readonly XNamespace Com = "http://schemas.microsoft.com/appx/manifest/com/windows10";
    private static readonly XNamespace Desktop4 = "http://schemas.microsoft.com/appx/manifest/desktop/windows10/4";

    internal static readonly Guid ExtractHereClsid = new("2A4F7C91-8E6D-4B3A-9C52-F1D8E9A7B3C4");
    internal static readonly Guid ExtractToFolderClsid = new("F5964E4F-2EC5-4DC7-BA37-C04AD4DB456F");

    private static string Header => RepoFiles.Read("Iljip.ShellExtension", "IljipCommands.h");
    private static string Source => RepoFiles.Read("Iljip.ShellExtension", "IljipCommands.cpp");
    private static XDocument Manifest => XDocument.Parse(RepoFiles.Read("packaging", "Package.appxmanifest"));

    /// <summary>헤더의 __declspec(uuid) — 클래스 이름 → CLSID.</summary>
    private static Dictionary<string, Guid> DeclaredClasses() =>
        Regex.Matches(Header, @"__declspec\(uuid\(""([0-9A-Fa-f\-]{36})""\)\)\s*(\w+)")
             .ToDictionary(m => m.Groups[2].Value, m => Guid.Parse(m.Groups[1].Value));

    [Fact]
    public void 헤더_uuid_다섯개와_constexpr_GUID와_매니페스트_클래스가_같다()
    {
        var declared = DeclaredClasses();
        Assert.Equal(5, declared.Count);
        Assert.Equal(ExtractToFolderClsid, declared["ExtractToFolderCommand"]);
        Assert.Equal(ExtractHereClsid, declared["ExtractHereCommand"]);

        // constexpr GUID CLSID_x = { 0x…, 0x…, 0x…, { 0x.. ×8 } } — 같은 값이어야 한다(손으로 옮겨 적는 곳)
        var constants = Regex.Matches(Header,
                @"constexpr\s+GUID\s+CLSID_\w+\s*=\s*\{\s*0x([0-9A-Fa-f]+)\s*,\s*0x([0-9A-Fa-f]+)\s*,\s*0x([0-9A-Fa-f]+)\s*,\s*\{([^}]*)\}")
            .Select(m =>
            {
                var tail = m.Groups[4].Value.Split(',').Select(b => Convert.ToByte(b.Trim()[2..], 16)).ToArray();
                return new Guid(Convert.ToUInt32(m.Groups[1].Value, 16), Convert.ToUInt16(m.Groups[2].Value, 16), Convert.ToUInt16(m.Groups[3].Value, 16),
                    tail[0], tail[1], tail[2], tail[3], tail[4], tail[5], tail[6], tail[7]);
            }).ToHashSet();
        Assert.Equal(declared.Values.ToHashSet(), constants);

        var manifestClasses = Manifest.Descendants(Com + "Class").Select(c => Guid.Parse((string)c.Attribute("Id")!)).ToList();
        Assert.Equal(5, manifestClasses.Count);
        Assert.Equal(declared.Values.ToHashSet(), manifestClasses.ToHashSet());
    }

    [Fact]
    public void 모든_cpp_클래스가_COM으로_등록된다()
    {
        foreach (var name in DeclaredClasses().Keys)
        {
            Assert.Contains($"CoCreatableClass({name});", Source);
            Assert.Contains($"template class IljipCommandBase<{name}>;", Source);
        }
    }

    /// <summary>메뉴 Verb 의 Clsid 는 모두 선언된 클래스, '여기에 풀기'가 있는 형식에는 '"이름"\ 에 풀기'도 있다.</summary>
    [Fact]
    public void 매니페스트_메뉴가_클래스와_맞는다()
    {
        var declared = DeclaredClasses().Values.ToHashSet();
        var itemTypes = Manifest.Descendants(Desktop4 + "ItemType").ToList();
        Assert.NotEmpty(itemTypes);
        foreach (var it in itemTypes)
        {
            var verbs = it.Elements(Desktop4 + "Verb").ToDictionary(v => (string)v.Attribute("Id")!, v => Guid.Parse((string)v.Attribute("Clsid")!));
            Assert.All(verbs.Values, clsid => Assert.Contains(clsid, declared));
            if (verbs.TryGetValue("IljipExtractHere", out var here))
            {
                Assert.Equal(ExtractHereClsid, here);
                Assert.True(verbs.TryGetValue("IljipExtractToFolder", out var toFolder), $"{it.Attribute("Type")}: '이름 폴더에 풀기' 메뉴 없음");
                Assert.Equal(ExtractToFolderClsid, toFolder);
            }
        }
        // 지금 등록 형식(.egg·.alz 와 새 형식은 I1-2 범위)
        var withFolderVerb = itemTypes.Where(it => it.Elements(Desktop4 + "Verb").Any(v => (string)v.Attribute("Id")! == "IljipExtractToFolder"))
                                      .Select(it => (string)it.Attribute("Type")!).ToArray();
        Assert.Equal(new[] { ".zip", ".7z", ".rar", ".tar", ".gz", ".bz2" }, withFolderVerb);
    }

    [Fact]
    public void 여기에_풀기_메뉴_제목()
    {
        Assert.Equal("여기에 풀기 (일집)", TitleLiteral("ExtractHereCommand"));
    }

    private static string TitleLiteral(string className)
    {
        var m = Regex.Match(Header, className + @"\s*:\s*public[^{]*\{(?<body>.*?)\};", RegexOptions.Singleline);
        Assert.True(m.Success, $"{className} 를 헤더에서 못 찾음");
        var t = Regex.Match(m.Groups["body"].Value, @"PCWSTR\s+Title\(\)[^{]*\{\s*return\s+L""(?<t>[^""]*)""");
        Assert.True(t.Success, $"{className}.Title() 을 못 찾음");
        return t.Groups["t"].Value;
    }

    /// <summary>C++ 가 Iljip.exe 에 넘기는 키워드(L"--…")는 모두 C# 해석기가 알아본다(오타·이름 바꿈 방지).</summary>
    [Fact]
    public void cpp가_넘기는_키워드를_모두_알아본다()
    {
        var keywords = Regex.Matches(Source, @"L""(--[a-z\-]+)").Select(m => m.Groups[1].Value).Distinct().ToList();
        Assert.Contains("--extract-here", keywords);
        Assert.Contains("--extract-to-folder", keywords);
        foreach (var k in keywords)
        {
            Assert.Contains(k, CommandLineParser.Keywords);
            var cmd = CommandLineParser.Parse(new[] { k, @"C:\a b\x.zip", @"C:\dest" });
            Assert.NotEqual(CommandVerb.Paths, cmd.Verb);
            Assert.NotEqual(CommandVerb.None, cmd.Verb);
        }
    }

    /// <summary>C++ 이름 규칙(FolderNameFor)이 C# 과 같은 단계를 다 갖고 있다(실제 값 대조는 아래 DLL 테스트).</summary>
    [Fact]
    public void cpp_이름_규칙에_묶음확장자와_숫자분할_처리가_있다()
    {
        var m = Regex.Match(Source, @"std::wstring FolderNameFor\(const std::wstring& fileName\)\s*\{(?<body>.*?)\n    \}", RegexOptions.Singleline);
        Assert.True(m.Success, "C++ FolderNameFor 를 못 찾음");
        string body = m.Groups["body"].Value;
        foreach (var s in new[] { "L\".tar.gz\"", "L\".tar.bz2\"", "L\".tar.xz\"", "L\".part\"", "L\".vol\"", "'.001'", ArchiveNaming.FallbackName })
            Assert.Contains(s, body);
    }

    // ───────────── 빌드된 DLL 로 실제 메뉴 제목 확인(선택 — DLL 이 없거나 소스보다 오래되면 건너뜀) ─────────────

    [ShellDllFact]
    public void DLL_메뉴_제목이_CSharp_이름_규칙과_같다()
    {
        using var tmp = new TempDir("shdll");
        var names = ArchiveNamingTests.FolderNames.Select(row => (string)row[0])
            .Append(new string('가', 45) + ".zip")              // 40자 넘는 이름은 '…' 로 줄임
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        var files = names.Select(n =>
        {
            string p = tmp.P(n);
            File.WriteAllBytes(p, Array.Empty<byte>());   // 내용은 안 읽는다(이름만) — 빈 파일
            return p;
        }).ToList();

        Sta.Run(() =>
        {
            using var toFolder = ShellDll.Create(ExtractToFolderClsid);
            foreach (var f in files)
            {
                string folder = ArchiveNaming.FolderNameFor(f);
                string shown = folder.Length > 40 ? folder[..39] + "…" : folder;
                Assert.Equal($"\"{shown}\"\\ 에 풀기 (일집)", toFolder.Title(f));
            }
            Assert.Equal("각각 이름 폴더에 풀기 (일집)", toFolder.Title(files[0], files[1]));
            Assert.Equal("이름 폴더에 풀기 (일집)", toFolder.Title());

            using var here = ShellDll.Create(ExtractHereClsid);
            Assert.Equal("여기에 풀기 (일집)", here.Title(files[0]));
            Assert.Equal("여기에 풀기 (일집)", here.Title());
        });
    }
}

/// <summary>빌드된 셸 확장 DLL(D:\Python\일집-build\Release\x64\Iljip.ShellExtension)이 없거나 C++ 소스보다 오래되면 건너뛴다.</summary>
public sealed class ShellDllFactAttribute : FactAttribute
{
    public ShellDllFactAttribute()
    {
        string? why = ShellDll.SkipReason();
        if (why is not null) Skip = why;
    }
}

/// <summary>
/// 셸 확장 DLL 을 테스트 프로세스에 불러 IExplorerCommand.GetTitle 만 부르는 도우미(설치·등록 없이 DllGetClassObject 로 직접 만듦).
/// Invoke(Iljip.exe 실행)는 부르지 않는다. 불러온 DLL 은 테스트가 끝날 때까지 내려놓지 않는다(살아 있는 COM 객체가 있으면 위험).
/// </summary>
internal static class ShellDll
{
    public static readonly string DllPath = @"D:\Python\일집-build\Release\x64\Iljip.ShellExtension\Iljip.ShellExtension.dll";

    public static string? SkipReason()
    {
        if (!Environment.Is64BitProcess) return "64비트 테스트 프로세스에서만(셸 확장은 x64)";
        if (!File.Exists(DllPath)) return $"빌드된 셸 확장 DLL 없음: {DllPath} (packaging\\WinSdk\\WinSdk.csproj 주석의 MSBuild 명령으로 빌드)";
        try
        {
            var dll = File.GetLastWriteTimeUtc(DllPath);
            foreach (var src in new[] { "IljipCommands.cpp", "IljipCommands.h", "dllmain.cpp", "pch.h" })
                if (File.GetLastWriteTimeUtc(RepoFiles.PathOf("Iljip.ShellExtension", src)) > dll)
                    return $"빌드된 DLL 이 소스({src})보다 오래됨 — 다시 빌드하면 이 테스트가 돈다";
        }
        catch (Exception ex)
        {
            return "소스·DLL 시각 확인 실패: " + ex.Message;
        }
        return null;
    }

    private static IntPtr _module;
    private static readonly object Gate = new();

    public static Command Create(Guid clsid)
    {
        IntPtr module;
        lock (Gate)
        {
            if (_module == IntPtr.Zero) _module = NativeLibrary.Load(DllPath);
            module = _module;
        }
        var getClassObject = Marshal.GetDelegateForFunctionPointer<DllGetClassObjectFn>(NativeLibrary.GetExport(module, "DllGetClassObject"));
        Guid iidFactory = typeof(IClassFactory).GUID;
        Marshal.ThrowExceptionForHR(getClassObject(ref clsid, ref iidFactory, out IntPtr pFactory));
        var factory = (IClassFactory)Marshal.GetObjectForIUnknown(pFactory);
        Marshal.Release(pFactory);
        try
        {
            Guid iidCommand = typeof(IExplorerCommand).GUID;
            Marshal.ThrowExceptionForHR(factory.CreateInstance(IntPtr.Zero, ref iidCommand, out IntPtr pCommand));
            var command = (IExplorerCommand)Marshal.GetObjectForIUnknown(pCommand);
            Marshal.Release(pCommand);
            return new Command(command);
        }
        finally
        {
            Marshal.FinalReleaseComObject(factory);
        }
    }

    public sealed class Command : IDisposable
    {
        private readonly IExplorerCommand _command;

        public Command(IExplorerCommand command) => _command = command;

        /// <summary>고른 항목(파일 경로들, 없으면 null 배열)에 대한 메뉴 제목.</summary>
        public string Title(params string[] paths)
        {
            IShellItemArray? array = paths.Length == 0 ? null : ItemArray(paths);
            try
            {
                Marshal.ThrowExceptionForHR(_command.GetTitle(array, out IntPtr raw));
                try { return Marshal.PtrToStringUni(raw)!; }
                finally { Marshal.FreeCoTaskMem(raw); }
            }
            finally
            {
                if (array is not null) Marshal.FinalReleaseComObject(array);
            }
        }

        public void Dispose() => Marshal.FinalReleaseComObject(_command);
    }

    private static IShellItemArray ItemArray(string[] paths)
    {
        var pidls = paths.Select(p =>
        {
            IntPtr pidl = ILCreateFromPathW(p);
            if (pidl == IntPtr.Zero) throw new InvalidOperationException("셸 항목을 못 만듦: " + p);
            return pidl;
        }).ToArray();
        try
        {
            Marshal.ThrowExceptionForHR(SHCreateShellItemArrayFromIDLists((uint)pidls.Length, pidls, out var array));
            return array;
        }
        finally
        {
            foreach (var p in pidls) ILFree(p);
        }
    }

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int DllGetClassObjectFn(ref Guid clsid, ref Guid iid, out IntPtr ppv);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr ILCreateFromPathW(string pszPath);

    [DllImport("shell32.dll")]
    private static extern void ILFree(IntPtr pidl);

    [DllImport("shell32.dll")]
    private static extern int SHCreateShellItemArrayFromIDLists(uint cidl, [In] IntPtr[] rgpidl, out IShellItemArray ppsiItemArray);

    [ComImport, Guid("00000001-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IClassFactory
    {
        [PreserveSig] int CreateInstance(IntPtr pUnkOuter, ref Guid riid, out IntPtr ppvObject);
        [PreserveSig] int LockServer([MarshalAs(UnmanagedType.Bool)] bool fLock);
    }

    [ComImport, Guid("b63ea76d-1f85-456f-a19c-48159efa858b"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IShellItemArray
    {
    }

    /// <summary>IExplorerCommand(vtable 차례 그대로). Invoke 는 자리만 — 부르지 않는다.</summary>
    [ComImport, Guid("a08ce4d0-fa25-44ab-b57c-c7b1c323e0b9"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IExplorerCommand
    {
        [PreserveSig] int GetTitle(IShellItemArray? psiItemArray, out IntPtr ppszName);
        [PreserveSig] int GetIcon(IShellItemArray? psiItemArray, out IntPtr ppszIcon);
        [PreserveSig] int GetToolTip(IShellItemArray? psiItemArray, out IntPtr ppszInfotip);
        [PreserveSig] int GetCanonicalName(out Guid pguidCommandName);
        [PreserveSig] int GetState(IShellItemArray? psiItemArray, [MarshalAs(UnmanagedType.Bool)] bool fOkToBeSlow, out uint pCmdState);
        [PreserveSig] int Invoke(IShellItemArray? psiItemArray, IntPtr pbc);
        [PreserveSig] int GetFlags(out uint pFlags);
        [PreserveSig] int EnumSubCommands(out IntPtr ppEnum);
    }
}
