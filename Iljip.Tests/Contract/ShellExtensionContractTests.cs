using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Iljip.Services;
using Iljip.Tests.TestSupport;
using ComDataObject = System.Runtime.InteropServices.ComTypes.IDataObject;

namespace Iljip.Tests.Contract;

/// <summary>
/// [I0-1·W2-12] 셸 확장(C++ Iljip.ShellExtension)·설치 매니페스트(packaging/Package.appxmanifest)·본체(C# 명령줄·<see cref="ShellMenu"/>·
/// <see cref="ShellCommandLine"/>·<see cref="FileAssociationRegistrar"/>) 사이의 약속.
/// 소스를 읽어 대조한다(C++ 빌드 없이도 돈다): CLSID 가 헤더·constexpr·매니페스트·C# 에서 같고, 메뉴 제목·넘기는 키워드·압축 확장자·명령줄 규칙이 맞는지.
/// 빌드된 DLL 이 있으면(<see cref="ShellDllFactAttribute"/>) 실제로 불러 메뉴 제목·하위 메뉴(GetTitle·GetState·EnumSubCommands)·끌어 놓기 메뉴(QueryContextMenu)·
/// 드롭 처리기(DragEnter)가 C# 규칙과 같은지도 본다 — Invoke·InvokeCommand·Drop(Iljip.exe 실행)은 절대 부르지 않는다. 설치·등록도 하지 않는다.
/// </summary>
public class ShellExtensionContractTests
{
    private static readonly XNamespace Foundation = "http://schemas.microsoft.com/appx/manifest/foundation/windows10";
    private static readonly XNamespace Uap10 = "http://schemas.microsoft.com/appx/manifest/uap/windows10/10";
    private static readonly XNamespace Com = "http://schemas.microsoft.com/appx/manifest/com/windows10";
    private static readonly XNamespace Desktop4 = "http://schemas.microsoft.com/appx/manifest/desktop/windows10/4";
    private static readonly XNamespace Desktop5 = "http://schemas.microsoft.com/appx/manifest/desktop/windows10/5";
    private static readonly XNamespace Desktop9 = "http://schemas.microsoft.com/appx/manifest/desktop/windows10/9";

    // 옛 메뉴(W2-12 전 — DLL 에만 남김)
    internal static readonly Guid ExtractHereClsid = new("2A4F7C91-8E6D-4B3A-9C52-F1D8E9A7B3C4");
    internal static readonly Guid ExtractToFolderClsid = new("F5964E4F-2EC5-4DC7-BA37-C04AD4DB456F");

    // W2-12
    internal static readonly Guid RootClsid = new("C0EE1823-ADD5-4327-92D8-F940142AD913");
    internal static readonly Guid BackgroundClsid = new("A0714267-8EDA-4F03-8E4D-411FBB979391");
    internal static readonly Guid DragDropClsid = new("1AE50C95-17B6-40F9-918B-69099428FF2A");
    internal static readonly Guid DropHandlerClsid = new("61C3F459-E40B-441D-9F76-F56C70DFF8C1");

    /// <summary>헤더의 COM 클래스 전부(이름 → CLSID, 매니페스트에 선언하는가). 드롭 처리기는 매니페스트로 못 해 C# 파일 연결이 레지스트리로.</summary>
    private static readonly (string Name, Guid Clsid, bool InManifest)[] ExpectedClasses =
    {
        ("OpenWithIljipCommand", new Guid("B3E8A7D5-1C9F-4E2A-B6A1-D39C8F2E5B71"), false),
        ("ExtractHereCommand", ExtractHereClsid, false),
        ("ExtractToFolderCommand", ExtractToFolderClsid, false),
        ("ExtractToCommand", new Guid("7D3E9C2A-5B1F-4A8C-93E6-C2A8F5D7B1E9"), false),
        ("CompressWithIljipCommand", new Guid("F9E2C1A8-3D7B-4E5A-91C8-B5F7D2E9A3C6"), false),
        ("IljipRootCommand", RootClsid, true),
        ("IljipBackgroundCommand", BackgroundClsid, true),
        ("IljipDragDropHandler", DragDropClsid, true),
        ("IljipDropHandler", DropHandlerClsid, false),
    };

    /// <summary>C++ 소스(.cpp) 이름 — 빌드된 DLL 이 이것보다 오래되면 DLL 시험은 건너뜀(<see cref="ShellDll.SkipReason"/>).</summary>
    internal static readonly string[] CppSources = { "IljipCommands.cpp", "IljipLaunch.cpp", "IljipMenu.cpp", "IljipDragDrop.cpp", "dllmain.cpp" };

    private static string Header => RepoFiles.Read("Iljip.ShellExtension", "IljipCommands.h");
    private static string Source => RepoFiles.Read("Iljip.ShellExtension", "IljipCommands.cpp");
    private static string Launch => RepoFiles.Read("Iljip.ShellExtension", "IljipLaunch.cpp");
    private static string MenuSource => RepoFiles.Read("Iljip.ShellExtension", "IljipMenu.cpp");
    private static string DragSource => RepoFiles.Read("Iljip.ShellExtension", "IljipDragDrop.cpp");
    private static string AllSources => string.Join("\n", CppSources.Select(s => RepoFiles.Read("Iljip.ShellExtension", s)));
    private static XDocument Manifest => XDocument.Parse(RepoFiles.Read("packaging", "Package.appxmanifest"));

    /// <summary>헤더의 __declspec(uuid) — 클래스 이름 → CLSID.</summary>
    private static Dictionary<string, Guid> DeclaredClasses() =>
        Regex.Matches(Header, @"__declspec\(uuid\(""([0-9A-Fa-f\-]{36})""\)\)\s*(\w+)")
             .ToDictionary(m => m.Groups[2].Value, m => Guid.Parse(m.Groups[1].Value));

    [Fact]
    public void 헤더_uuid와_constexpr_GUID와_매니페스트_클래스가_같다()
    {
        var declared = DeclaredClasses();
        Assert.Equal(ExpectedClasses.Select(c => (c.Name, c.Clsid)).OrderBy(c => c.Name),
                     declared.Select(d => (d.Key, d.Value)).OrderBy(c => c.Key));

        // constexpr GUID CLSID_x = { 0x…, 0x…, 0x…, { 0x.. ×8 } } — 같은 값이어야 한다(손으로 옮겨 적는 곳)
        var constants = Regex.Matches(Header,
                @"constexpr\s+GUID\s+CLSID_\w+\s*=\s*\{\s*0x([0-9A-Fa-f]+)\s*,\s*0x([0-9A-Fa-f]+)\s*,\s*0x([0-9A-Fa-f]+)\s*,\s*\{([^}]*)\}")
            .Select(m =>
            {
                var tail = m.Groups[4].Value.Split(',').Select(b => Convert.ToByte(b.Trim()[2..], 16)).ToArray();
                return new Guid(Convert.ToUInt32(m.Groups[1].Value, 16), Convert.ToUInt16(m.Groups[2].Value, 16), Convert.ToUInt16(m.Groups[3].Value, 16),
                    tail[0], tail[1], tail[2], tail[3], tail[4], tail[5], tail[6], tail[7]);
            }).ToList();
        Assert.Equal(declared.Count, constants.Count);
        Assert.Equal(declared.Values.ToHashSet(), constants.ToHashSet());

        // 매니페스트에는 '일집 ▶'·'일집 열기'·끌어 놓기 메뉴 셋만(옛 5개는 DLL 에만, 드롭 처리기는 레지스트리)
        var manifestClasses = Manifest.Descendants(Com + "Class").ToList();
        Assert.Equal(ExpectedClasses.Where(c => c.InManifest).Select(c => c.Clsid).ToHashSet(),
                     manifestClasses.Select(c => Guid.Parse((string)c.Attribute("Id")!)).ToHashSet());
        Assert.Equal(3, manifestClasses.Count);
        Assert.All(manifestClasses, c =>
        {
            Assert.Equal("Iljip.ShellExtension.dll", (string?)c.Attribute("Path"));
            Assert.Equal("STA", (string?)c.Attribute("ThreadingModel"));
        });

        // 드롭 처리기 CLSID 는 C# 파일 연결(레지스트리 등록)과 같은 값
        Assert.Equal(DropHandlerClsid, FileAssociationRegistrar.DropHandlerClsid);
        Assert.Equal(FileAssociationCommand.ShellExtensionDllName, (string?)manifestClasses[0].Attribute("Path"));
    }

    /// <summary>모든 COM 클래스는 .cpp 어딘가에서 CoCreatableClass 로 등록되고(네임스페이스 접두사 없이), 베이스 템플릿을 쓰는 것은 명시적 인스턴스화가 있다.</summary>
    [Fact]
    public void 모든_cpp_클래스가_COM으로_등록된다()
    {
        string all = AllSources;
        var templated = Regex.Matches(Header, @"(\w+)\s*:\s*public\s+IljipCommandBase<(\w+)>").Select(m => m.Groups[1].Value).ToHashSet();
        Assert.Equal(new[] { "CompressWithIljipCommand", "ExtractHereCommand", "ExtractToCommand", "ExtractToFolderCommand", "IljipBackgroundCommand", "OpenWithIljipCommand" },
                     templated.OrderBy(n => n, StringComparer.Ordinal));
        foreach (var name in DeclaredClasses().Keys)
        {
            Assert.Single(Regex.Matches(all, $@"^CoCreatableClass\({name}\);", RegexOptions.Multiline));
            if (templated.Contains(name))
                Assert.Contains($"template class IljipCommandBase<{name}>;", Source);
        }
        Assert.DoesNotContain("CoCreatableClass(IljipShell::", all);
        // 새 소스는 프로젝트에 들어 있고 셸 API 라이브러리를 링크한다
        string vcxproj = RepoFiles.Read("Iljip.ShellExtension", "Iljip.ShellExtension.vcxproj");
        foreach (var src in CppSources)
            Assert.Contains($"<ClCompile Include=\"{src}\"", vcxproj);
        Assert.Contains("shell32.lib", vcxproj);
    }

    /// <summary>
    /// 매니페스트(W2-12): 모든 파일('*' — desktop4)·폴더('Directory' — desktop5)에 '일집 ▶' 하나, 폴더 안 빈 곳('Directory\Background')에 '일집 열기'.
    /// 확장자마다 따로 두지 않는다('*' 와 겹쳐 두 번 나옴 — 압축 확장자는 DLL 이 고름). 오른쪽 끌기 메뉴는 desktop9 고전 끌어 놓기 처리기(폴더·드라이브).
    /// 외부 위치 패키지(AllowExternalContent·mediumIL·win32App — EntryPoint 없음), 0.4.0.0(0.3.0 의 옛 메뉴를 업그레이드로 바꿔 끼움).
    /// </summary>
    [Fact]
    public void 매니페스트_메뉴가_클래스와_맞는다()
    {
        var doc = Manifest;
        var declared = DeclaredClasses().Values.ToHashSet();
        var itemTypes = doc.Descendants().Where(e => e.Name.LocalName == "ItemType").ToList();
        var menus = itemTypes.Select(it => (
                Ns: it.Name.Namespace,
                Type: (string)it.Attribute("Type")!,
                Verbs: it.Elements().Where(v => v.Name.LocalName == "Verb").Select(v => ((string)v.Attribute("Id")!, Guid.Parse((string)v.Attribute("Clsid")!))).ToArray()))
            .ToList();
        Assert.Equal(3, menus.Count);
        Assert.Equal((Desktop4, "*"), (menus[0].Ns, menus[0].Type));
        Assert.Equal(new[] { ("IljipRoot", RootClsid) }, menus[0].Verbs);
        Assert.Equal((Desktop5, "Directory"), (menus[1].Ns, menus[1].Type));
        Assert.Equal(new[] { ("IljipRoot", RootClsid) }, menus[1].Verbs);
        Assert.Equal((Desktop5, @"Directory\Background"), (menus[2].Ns, menus[2].Type));
        Assert.Equal(new[] { ("IljipBackground", BackgroundClsid) }, menus[2].Verbs);
        Assert.All(menus.SelectMany(m => m.Verbs), v => Assert.Contains(v.Item2, declared));
        Assert.DoesNotContain(menus, m => m.Type.StartsWith('.'));
        Assert.Single(doc.Descendants(Desktop4 + "FileExplorerContextMenus"));

        var drag = doc.Descendants(Desktop9 + "Extension").Single();
        Assert.Equal("windows.fileExplorerClassicDragDropContextMenuHandler", (string?)drag.Attribute("Category"));
        Assert.Equal(new[] { ("Directory", DragDropClsid), ("Drive", DragDropClsid) },
            drag.Descendants(Desktop9 + "ExtensionHandler").Select(h => ((string)h.Attribute("Type")!, Guid.Parse((string)h.Attribute("Clsid")!))).ToArray());

        var identity = doc.Root!.Element(Foundation + "Identity")!;
        Assert.Equal(("Iljip.ShellExtension", "0.4.0.0"), ((string?)identity.Attribute("Name"), (string?)identity.Attribute("Version")));
        Assert.Equal("true", (string?)doc.Descendants(Uap10 + "AllowExternalContent").Single());
        var app = doc.Descendants(Foundation + "Application").Single();
        Assert.Equal(("Iljip.exe", "mediumIL", "win32App"),
            ((string?)app.Attribute("Executable"), (string?)app.Attribute(Uap10 + "TrustLevel"), (string?)app.Attribute(Uap10 + "RuntimeBehavior")));
        Assert.Null(app.Attribute("EntryPoint"));
        var ignorable = ((string)doc.Root!.Attribute("IgnorableNamespaces")!).Split(' ');
        foreach (var ns in new[] { "uap", "uap10", "com", "desktop4", "desktop5", "desktop9", "rescap" })
            Assert.Contains(ns, ignorable);
    }

    [Fact]
    public void 여기에_풀기_메뉴_제목()
    {
        Assert.Equal("여기에 풀기 (일집)", TitleLiteral("ExtractHereCommand"));   // 옛 메뉴(DLL 에만 남김)
        Assert.Equal(ShellMenu.BackgroundTitle, TitleLiteral("IljipBackgroundCommand"));
    }

    private static string TitleLiteral(string className)
    {
        var m = Regex.Match(Header, className + @"\s*:\s*public[^{]*\{(?<body>.*?)\};", RegexOptions.Singleline);
        Assert.True(m.Success, $"{className} 를 헤더에서 못 찾음");
        var t = Regex.Match(m.Groups["body"].Value, @"PCWSTR\s+Title\(\)[^{]*\{\s*return\s+L""(?<t>[^""]*)""");
        Assert.True(t.Success, $"{className}.Title() 을 못 찾음");
        return t.Groups["t"].Value;
    }

    /// <summary>C++ 가 Iljip.exe 에 넘기는 키워드(L"--…")는 모두 C# 해석기가 알아본다(오타·이름 바꿈 방지). '--list-file' 은 해석 전에 펼치는 표시.</summary>
    [Fact]
    public void cpp가_넘기는_키워드를_모두_알아본다()
    {
        var keywords = Regex.Matches(AllSources, @"L""(--[a-z0-9\-]+)").Select(m => m.Groups[1].Value).Distinct().ToList();
        foreach (var k in new[] { "--extract-here", "--extract-to-folder", "--extract-smart", "--compress-zip", "--compress-7z", "--compress-to",
                     "--extract-to-folder-in", "--add-to", "--open", "--extract", "--compress", "--list-file" })
            Assert.Contains(k, keywords);
        foreach (var k in keywords)
        {
            if (k == CommandLineParser.ListFileKeyword) continue;
            Assert.Contains(k, CommandLineParser.Keywords);
            var cmd = CommandLineParser.Parse(new[] { k, @"C:\a b\x.zip", @"C:\dest" });
            Assert.NotEqual(CommandVerb.Paths, cmd.Verb);
            Assert.NotEqual(CommandVerb.None, cmd.Verb);
            Assert.False(cmd.Verb is CommandVerb.RegisterAssociations or CommandVerb.UnregisterAssociations, "셸 확장이 파일 연결을 바꾸면 안 됨");
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

    // ───────────── W2-12: '일집 ▶' 하위 메뉴·명령줄·끌어 놓기(소스 대조) ─────────────

    /// <summary>C++ 압축 확장자 목록은 C# <see cref="ShellMenu.ArchiveExtensions"/> 와 같은 차례·같은 값.</summary>
    [Fact]
    public void cpp_압축_확장자_목록이_CSharp과_같다()
    {
        var m = Regex.Match(MenuSource, @"ArchiveExtensions\[\]\s*=\s*\{(?<body>[^}]*)\}", RegexOptions.Singleline);
        Assert.True(m.Success, "C++ ArchiveExtensions 를 못 찾음");
        var list = Regex.Matches(m.Groups["body"].Value, @"L""(\.[a-z0-9]+)""").Select(x => x.Groups[1].Value).ToArray();
        Assert.Equal(ShellMenu.ArchiveExtensions, list);
    }

    /// <summary>
    /// C++ 하위 메뉴 차례·제목·동사가 C# <see cref="ShellMenu.ItemsFor"/> 와 같다(SubCommandsFor·SubCommandTitle·SubCommandVerb 소스 대조).
    /// 루트는 '일집'·ECF_HASSUBCOMMANDS·EnumSubCommands 구현·아이콘(icons\iljip.ico, 없으면 Iljip.exe,0).
    /// </summary>
    [Fact]
    public void cpp_하위_메뉴가_CSharp_규칙과_같다()
    {
        string src = MenuSource;
        var kinds = new (string Kind, ShellMenu.Item[] Sample)[]
        {
            ("OneArchive", new[] { new ShellMenu.Item(@"C:\a\보고서.zip", false) }),
            ("ManyArchives", new[] { new ShellMenu.Item(@"C:\a\x.zip", false), new ShellMenu.Item(@"C:\a\y.7z", false) }),
            ("FilesOrFolders", new[] { new ShellMenu.Item(@"C:\a\보고서.xlsx", false) }),
        };
        var verbBlock = Regex.Match(src, @"const wchar_t\* SubCommandVerb\(SubCommandId id\)\s*\{(?<b>.*?)\n    \}", RegexOptions.Singleline).Groups["b"].Value;
        Assert.NotEmpty(verbBlock);
        foreach (var (kind, sample) in kinds)
        {
            var ids = Regex.Match(src, $@"case SelectionKind::{kind}:\s*return \{{(?<ids>[^}}]*)\}};").Groups["ids"].Value
                .Split(',').Select(s => s.Trim().Replace("SubCommandId::", "")).ToArray();
            var expected = ShellMenu.ItemsFor(sample);
            Assert.Equal(expected.Count, ids.Length);
            // 제목에 들어가는 이름(압축 하나 — 풀 폴더 이름, 파일·폴더 — 만들 압축 이름)
            string name = kind == "OneArchive"
                ? ShellMenu.ShortenForMenu(ArchiveNaming.FolderNameFor(sample[0].Path))
                : ShellMenu.ShortenForMenu(ShellMenu.CompressBaseName(sample));
            for (int i = 0; i < ids.Length; i++)
            {
                // 제목: 고정 제목은 글자 그대로, 이름이 들어가는 제목은 이름 뒤 글자('"\ 에 풀기'·'.zip"으로 압축')가 C++ 문자열 상수에 그대로
                string title = expected[i].Title;
                int at = title.StartsWith('"') ? title.IndexOf(name, StringComparison.Ordinal) : -1;
                string fixedPart = at > 0 ? title[(at + name.Length)..] : title;
                if (at > 0) Assert.Equal("\"", title[..at]);
                string cpp = TitleSourceFor(src, ids[i]);
                Assert.True(cpp.Contains("L\"" + CppLiteral(fixedPart) + "\"", StringComparison.Ordinal), $"{ids[i]} 제목: C# '{title}' / C++ {cpp}");
                if (at > 0) Assert.Contains("L\"\\\"\" + ShortenForMenu(", cpp);
                // 동사
                var verb = Regex.Match(verbBlock, $@"case SubCommandId::{ids[i]}:(?:\s*case SubCommandId::\w+:)*\s*return L""(?<v>--[a-z0-9\-]+)""");
                Assert.True(verb.Success, ids[i] + " 동사 없음");
                Assert.Equal(expected[i].Verb, verb.Groups["v"].Value);
            }
        }
        Assert.Contains("SHStrDupW(L\"일집\", title)", src);
        Assert.Equal("일집", ShellMenu.RootTitle);
        Assert.Matches(@"IljipRootCommand::GetFlags\(EXPCMDFLAGS\* flags\) noexcept\s*\{\s*\*flags = ECF_HASSUBCOMMANDS;", src);
        Assert.Contains("L\"\\\\icons\\\\iljip.ico\"", src);
        Assert.Contains("IljipExePath() + L\",0\"", src);
        var enumBody = Regex.Match(src, @"IljipRootCommand::EnumSubCommands\(IEnumExplorerCommand\*\* commands\) noexcept\s*\{(?<b>.*?)\n    \}", RegexOptions.Singleline).Groups["b"].Value;
        Assert.Contains("MakeAndInitialize<SubCommandEnumerator>", enumBody);
        Assert.DoesNotContain("E_NOTIMPL", enumBody);
        // 여는 것은 첫 경로 하나, 나머지는 고른 것 모두를 한 명령줄로
        Assert.Contains("if (id == SubCommandId::Open)", src);
        Assert.Contains("LaunchIljipCommand(SubCommandVerb(id), {}, paths)", src);

        static string CppLiteral(string s) => s.Replace("\\", "\\\\").Replace("\"", "\\\"");

        static string TitleSourceFor(string src, string id)
        {
            var m = Regex.Match(src, $@"case SubCommandId::{id}:(?<b>.*?)(?=case SubCommandId::|\n        \}})", RegexOptions.Singleline);
            Assert.True(m.Success, id + " 제목 없음");
            return m.Groups["b"].Value;
        }
    }

    /// <summary>
    /// C++ 명령줄 규칙(IljipLaunch.cpp)은 C# <see cref="ShellCommandLine"/> 과 같다: 최대 30,000자, 목록 파일 %LOCALAPPDATA%\Iljip\temp\args\&lt;guid&gt;.lst(UTF-8·CRLF·CREATE_NEW),
    /// '--list-file', 따옴표 규칙(닫는 따옴표 앞 역슬래시 두 배). 옛 명령도 같은 따옴표 함수를 쓴다.
    /// </summary>
    [Fact]
    public void cpp_명령줄_규칙이_CSharp과_같다()
    {
        var max = Regex.Match(Header, @"constexpr size_t MaxCommandLineChars = (\d+);");
        Assert.True(max.Success);
        Assert.Equal(ShellCommandLine.MaxChars, int.Parse(max.Groups[1].Value));

        string src = Launch;
        Assert.Contains("FOLDERID_LocalAppData", src);
        Assert.Contains("folder += L\"\\\\Iljip\\\\temp\\\\args\";", src);
        // C# 쪽: %LOCALAPPDATA%\Iljip(AppPaths.Root) \temp(TempRoot) \args
        Assert.Equal(Path.Combine(AppPaths.TempRoot, "args"), ShellCommandLine.ListFileFolder);
        string appPaths = RepoFiles.Read("Iljip", "Services", "AppPaths.cs");
        Assert.Contains("Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData)", appPaths);
        Assert.Contains("return Path.Combine(baseDir, \"Iljip\");", appPaths);
        Assert.Contains("public static string TempRoot => Path.Combine(Root, \"temp\");", appPaths);
        Assert.Contains($"L\"{ShellCommandLine.ListFileExtension}\"", src);
        Assert.Contains("CREATE_NEW", src);
        Assert.Contains("CP_UTF8", src);
        Assert.Contains("utf8 += \"\\r\\n\";", src);
        Assert.Contains($"line += L\"{CommandLineParser.ListFileKeyword}\";", src);
        Assert.Contains("if (head.size() + tail.size() <= MaxCommandLineChars)", src);
        // 따옴표: 안의 '"' 앞은 2n+1, 끝은 2n
        Assert.Contains("r.append(slashes * 2 + 1, L'\\\\');", src);
        Assert.Contains("r.append(slashes * 2, L'\\\\');", src);
        Assert.Contains("return QuoteCommandArg(path);", Source);
    }

    /// <summary>오른쪽 끌기 메뉴 제목·동사, 드롭 처리기 동사(C++ IljipDragDrop.cpp) — C# 제목·키워드와 같다.</summary>
    [Fact]
    public void cpp_끌어_놓기가_CSharp과_같다()
    {
        string src = DragSource;
        Assert.Contains($"DragCompressTitle = L\"{ShellMenu.DragCompressTitle}\"", src);
        Assert.Contains($"DragExtractTitle = L\"{ShellMenu.DragExtractTitle}\"", src);
        Assert.Contains($"LaunchIljipCommand(L\"{CommandLineParser.CompressToKeyword}\", {{ m_target }}, m_sources)", src);
        Assert.Contains($"LaunchIljipCommand(L\"{CommandLineParser.ExtractToFolderInKeyword}\", {{ m_target }}, m_sources)", src);
        Assert.Contains($"LaunchIljipCommand(L\"{CommandLineParser.AddToKeyword}\", {{ m_archive }}, files)", src);
        Assert.Contains("return ext == L\".zip\" || ext == L\".7z\";", src);
        Assert.Equal(new[] { ".zip", ".7z" }, FileAssociationRegistrar.DropHandlerExtensions);
        Assert.Contains("*clsid = CLSID_IljipDropHandler;", src);
    }

    // ───────────── 빌드된 DLL 로 실제 메뉴 확인(선택 — DLL 이 없거나 소스보다 오래되면 건너뜀) ─────────────

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

    /// <summary>
    /// '일집 ▶'(실제 DLL): 제목·ECF_HASSUBCOMMANDS·아이콘, 고른 것(압축 하나 — 여러 이름 / 압축 여럿 / 파일 / 폴더 / 섞임)마다 GetState 뒤 EnumSubCommands 의
    /// 제목 차례가 C# <see cref="ShellMenu.ItemsFor"/> 와 같다. 아무것도 안 고르면 숨음. GetState 없이 부르면 하위 메뉴가 각자 숨는다.
    /// 하위 메뉴의 Invoke 는 부르지 않는다.
    /// </summary>
    [ShellDllFact]
    public void DLL_일집_하위_메뉴가_CSharp_규칙과_같다()
    {
        using var tmp = new TempDir("shroot");
        string Make(string name)
        {
            string p = tmp.P(name);
            File.WriteAllBytes(p, Array.Empty<byte>());
            return p;
        }
        string zip = Make("보고서 2026.zip"), tgz = Make("자료.tar.gz"), part = Make("묶음.7z.001"), egg = Make("알집.EGG");
        string longZip = Make(new string('가', 45) + ".zip");
        string xlsx = Make("견적서.2026.xlsx"), exe = Make("setup.exe");
        string folder = tmp.Dir("사진 모음");
        string zipFolder = tmp.Dir("폴더인데.zip");

        var selections = new List<string[]>
        {
            new[] { zip }, new[] { tgz }, new[] { part }, new[] { egg }, new[] { longZip },
            new[] { zip, tgz, egg },
            new[] { xlsx }, new[] { exe }, new[] { folder }, new[] { zipFolder }, new[] { zip, xlsx }, new[] { folder, xlsx },
        };

        Sta.Run(() =>
        {
            using var root = ShellDll.Create(RootClsid);
            Assert.Equal(ShellMenu.RootTitle, root.Title(zip));
            Assert.Equal(ECF_HASSUBCOMMANDS, root.Flags());
            string module = Path.GetDirectoryName(ShellDll.DllPath)!;
            string ico = Path.Combine(module, "icons", "iljip.ico");
            Assert.Equal(File.Exists(ico) ? ico : Path.Combine(module, "Iljip.exe") + ",0", root.Icon(zip));
            Assert.Equal(RootClsid, root.CanonicalName());
            Assert.Equal(ECS_HIDDEN, root.State());   // 고른 것이 없음

            foreach (var paths in selections)
            {
                var expected = ShellMenu.ItemsFor(paths.Select(p => new ShellMenu.Item(p, Directory.Exists(p))).ToList()).Select(i => i.Title).ToArray();
                using var r = ShellDll.Create(RootClsid);
                Assert.Equal(ECS_ENABLED, r.State(paths));
                var subs = r.SubCommands();
                try
                {
                    Assert.Equal(expected, subs.Select(s => s.Title(paths)).ToArray());
                    Assert.All(subs, s => Assert.Equal(ECS_ENABLED, s.State(paths)));
                    Assert.All(subs, s => Assert.Equal(0u, s.Flags()));
                    // 하위 메뉴가 항목 없이 불려도(Windows 판에 따라) 루트가 본 것으로 같은 제목
                    Assert.Equal(expected, subs.Select(s => s.Title()).ToArray());
                }
                finally
                {
                    foreach (var s in subs) s.Dispose();
                }
            }

            // GetState 없이 EnumSubCommands — 하위 메뉴 11개를 다 주고 각자 GetState 에서 안 맞으면 숨는다
            using var fresh = ShellDll.Create(RootClsid);
            var all = fresh.SubCommands();
            try
            {
                Assert.Equal(11, all.Count);
                var visible = all.Where(s => s.State(new[] { zip }) == ECS_ENABLED).Select(s => s.Title(zip)).ToArray();
                Assert.Equal(ShellMenu.ItemsFor(new[] { new ShellMenu.Item(zip, false) }).Select(i => i.Title), visible);
            }
            finally
            {
                foreach (var s in all) s.Dispose();
            }

            using var background = ShellDll.Create(BackgroundClsid);
            Assert.Equal(ShellMenu.BackgroundTitle, background.Title());
            Assert.Equal(ShellMenu.BackgroundTitle, background.Title(folder));
        });
    }

    /// <summary>
    /// 오른쪽 끌기 메뉴(실제 DLL — 고전 IShellExtInit·IContextMenu): 압축만 끌면 '일집: 여기에 풀기'가 먼저, '일집: 여기에 압축'은 늘.
    /// 압축이 아닌 것(폴더 포함)이 섞이면 압축만. 기본 동작만 묻는 때(CMF_DEFAULTONLY)는 넣지 않는다. 동사 이름. InvokeCommand 는 부르지 않는다.
    /// 메뉴는 화면에 띄우지 않는다(CreatePopupMenu 로 만든 빈 메뉴에 넣고 글만 읽음).
    /// </summary>
    [ShellDllFact]
    public void DLL_오른쪽_끌기_메뉴()
    {
        using var tmp = new TempDir("shdrag");
        string target = tmp.Dir("놓을 곳");
        string a = tmp.P("가.zip"), b = tmp.P("나.7z"), t = tmp.P("글.txt");
        foreach (var p in new[] { a, b, t }) File.WriteAllBytes(p, Array.Empty<byte>());
        string folderZip = tmp.Dir("폴더.zip");

        Sta.Run(() =>
        {
            Assert.Equal(new[] { (ShellMenu.DragExtractTitle, 1u), (ShellMenu.DragCompressTitle, 0u) }, DragMenu(target, new[] { a, b }, out int added));
            Assert.Equal(2, added);
            Assert.Equal(new[] { (ShellMenu.DragCompressTitle, 0u) }, DragMenu(target, new[] { a, t }, out added));
            Assert.Equal(1, added);
            Assert.Equal(new[] { (ShellMenu.DragCompressTitle, 0u) }, DragMenu(target, new[] { folderZip }, out added));
            Assert.Empty(DragMenu(target, new[] { a }, out added, flags: CMF_DEFAULTONLY));
            Assert.Equal(0, added);

            using var handler = ShellDll.CreateDragDrop(target, new[] { a });
            Assert.Equal("IljipCompressHere", handler.Verb(0));
            Assert.Equal("IljipExtractHere", handler.Verb(1));
            using var mixed = ShellDll.CreateDragDrop(target, new[] { a, t });
            Assert.Equal("IljipCompressHere", mixed.Verb(0));
            Assert.Null(mixed.Verb(1));   // 압축만 끈 게 아니면 '풀기' 없음
        });
    }

    private static (string Title, uint Offset)[] DragMenu(string target, string[] sources, out int added, uint flags = 0)
    {
        using var handler = ShellDll.CreateDragDrop(target, sources);
        return handler.Menu(flags, out added);
    }

    /// <summary>
    /// 드롭 처리기(실제 DLL — IPersistFile·IDropTarget): zip·7z 에 파일을 끌어 오면 '복사'(원본은 그대로), 옮기기만 허락되면·다른 형식이면·파일이 아닌 것이면 안 됨.
    /// Drop(Iljip.exe 실행)은 부르지 않는다.
    /// </summary>
    [ShellDllFact]
    public void DLL_드롭_처리기()
    {
        using var tmp = new TempDir("shdrop");
        string zip = tmp.P("넣을 곳.zip"), seven = tmp.P("넣을 곳.7z"), rar = tmp.P("못 넣음.rar"), file = tmp.P("넣을 파일.txt");
        foreach (var p in new[] { zip, seven, rar, file }) File.WriteAllBytes(p, Array.Empty<byte>());

        Sta.Run(() =>
        {
            using var drop = ShellDll.CreateDropHandler();
            Assert.Equal(DropHandlerClsid, drop.ClassId());
            drop.Load(zip);
            Assert.Equal(zip, drop.CurFile());
            Assert.Equal(DROPEFFECT_COPY, drop.DragEnter(new[] { file }, DROPEFFECT_COPY | DROPEFFECT_MOVE | DROPEFFECT_LINK));
            Assert.Equal(DROPEFFECT_COPY, drop.DragOver(DROPEFFECT_COPY | DROPEFFECT_MOVE));
            Assert.Equal(DROPEFFECT_NONE, drop.DragOver(DROPEFFECT_MOVE));          // 옮기기만 — 안 됨(원본을 지우지 않음)
            drop.DragLeave();
            Assert.Equal(DROPEFFECT_NONE, drop.DragOver(DROPEFFECT_COPY));          // 떠난 뒤
            Assert.Equal(DROPEFFECT_NONE, drop.DragEnterText(DROPEFFECT_COPY));     // 파일이 아닌 것(글)

            drop.Load(seven);
            Assert.Equal(DROPEFFECT_COPY, drop.DragEnter(new[] { file }, DROPEFFECT_COPY));
            drop.DragLeave();
            drop.Load(rar);
            Assert.Equal(DROPEFFECT_NONE, drop.DragEnter(new[] { file }, DROPEFFECT_COPY));   // 고칠 수 없는 형식
            drop.DragLeave();
        });
    }

    private const uint ECF_HASSUBCOMMANDS = 0x1;
    private const uint ECS_ENABLED = 0x0;
    private const uint ECS_HIDDEN = 0x2;
    private const uint CMF_DEFAULTONLY = 0x1;
    private const uint DROPEFFECT_NONE = 0, DROPEFFECT_COPY = 1, DROPEFFECT_MOVE = 2, DROPEFFECT_LINK = 4;
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
/// 셸 확장 DLL 을 테스트 프로세스에 불러 메뉴 글만 읽는 도우미(설치·등록 없이 DllGetClassObject 로 직접 만듦).
/// Invoke·InvokeCommand·Drop(Iljip.exe 실행)은 부르지 않는다. 불러온 DLL 은 테스트가 끝날 때까지 내려놓지 않는다(살아 있는 COM 객체가 있으면 위험).
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
            foreach (var src in ShellExtensionContractTests.CppSources.Concat(new[] { "IljipCommands.h", "pch.h" }))
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

    /// <summary>DLL 의 클래스 공장으로 <paramref name="clsid"/> 를 만들어 <paramref name="iid"/> 인터페이스 포인터를 돌려준다(부르는 쪽이 Release).</summary>
    private static IntPtr CreateRaw(Guid clsid, Guid iid)
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
            Marshal.ThrowExceptionForHR(factory.CreateInstance(IntPtr.Zero, ref iid, out IntPtr p));
            return p;
        }
        finally
        {
            Marshal.FinalReleaseComObject(factory);
        }
    }

    private static T CreateAs<T>(Guid clsid) where T : class
    {
        IntPtr p = CreateRaw(clsid, typeof(T).GUID);
        try { return (T)Marshal.GetObjectForIUnknown(p); }
        finally { Marshal.Release(p); }
    }

    public static Command Create(Guid clsid) => new(CreateAs<IExplorerCommand>(clsid));

    public sealed class Command : IDisposable
    {
        private readonly IExplorerCommand _command;

        public Command(IExplorerCommand command) => _command = command;

        /// <summary>고른 항목(파일 경로들, 없으면 null 배열)에 대한 메뉴 제목.</summary>
        public string Title(params string[] paths) => WithItems(paths, array =>
        {
            Marshal.ThrowExceptionForHR(_command.GetTitle(array, out IntPtr raw));
            return TakeString(raw)!;
        });

        public string? Icon(params string[] paths) => WithItems(paths, array =>
        {
            int hr = _command.GetIcon(array, out IntPtr raw);
            return hr < 0 ? null : TakeString(raw);
        });

        public uint State(params string[] paths) => WithItems(paths, array =>
        {
            Marshal.ThrowExceptionForHR(_command.GetState(array, false, out uint state));
            return state;
        });

        public uint Flags()
        {
            Marshal.ThrowExceptionForHR(_command.GetFlags(out uint flags));
            return flags;
        }

        public Guid CanonicalName()
        {
            Marshal.ThrowExceptionForHR(_command.GetCanonicalName(out Guid g));
            return g;
        }

        /// <summary>하위 메뉴 전부(EnumSubCommands → Next). 부르는 쪽이 Dispose.</summary>
        public List<Command> SubCommands()
        {
            Marshal.ThrowExceptionForHR(_command.EnumSubCommands(out IntPtr pEnum));
            var e = (IEnumExplorerCommand)Marshal.GetObjectForIUnknown(pEnum);
            Marshal.Release(pEnum);
            var list = new List<Command>();
            try
            {
                var one = new IntPtr[1];
                while (e.Next(1, one, out uint fetched) == 0 && fetched == 1)
                {
                    var c = (IExplorerCommand)Marshal.GetObjectForIUnknown(one[0]);
                    Marshal.Release(one[0]);
                    list.Add(new Command(c));
                }
            }
            finally
            {
                Marshal.FinalReleaseComObject(e);
            }
            return list;
        }

        public void Dispose() => Marshal.FinalReleaseComObject(_command);
    }

    private static T WithItems<T>(string[] paths, Func<IShellItemArray?, T> body)
    {
        IShellItemArray? array = paths.Length == 0 ? null : ItemArray(paths);
        try { return body(array); }
        finally
        {
            if (array is not null) Marshal.FinalReleaseComObject(array);
        }
    }

    private static string? TakeString(IntPtr raw)
    {
        if (raw == IntPtr.Zero) return null;
        try { return Marshal.PtrToStringUni(raw); }
        finally { Marshal.FreeCoTaskMem(raw); }
    }

    // ───── 오른쪽 끌기 메뉴(IShellExtInit + IContextMenu) ─────

    /// <summary>끌어 놓기 메뉴 처리기를 만들어 '놓을 폴더·끈 파일'로 Initialize 한다.</summary>
    public static DragDrop CreateDragDrop(string target, string[] sources)
    {
        var init = CreateAs<IShellExtInit>(new Guid("1AE50C95-17B6-40F9-918B-69099428FF2A"));
        IntPtr pidl = ILCreateFromPathW(target);
        if (pidl == IntPtr.Zero) throw new InvalidOperationException("셸 항목을 못 만듦: " + target);
        IntPtr data = FileDropData(sources);
        try
        {
            Marshal.ThrowExceptionForHR(init.Initialize(pidl, data, IntPtr.Zero));
        }
        finally
        {
            Marshal.Release(data);
            ILFree(pidl);
        }
        return new DragDrop(init);
    }

    public sealed class DragDrop : IDisposable
    {
        private readonly IShellExtInit _init;
        private readonly IContextMenu _menu;

        public DragDrop(IShellExtInit init)
        {
            _init = init;
            _menu = (IContextMenu)init;
        }

        /// <summary>빈 팝업 메뉴(화면에 띄우지 않음)에 넣게 한 뒤 (제목, 첫 번호에서 떨어진 값) 차례. <paramref name="added"/> = 돌려준 HRESULT 의 코드(쓴 번호 수).</summary>
        public (string Title, uint Offset)[] Menu(uint flags, out int added)
        {
            const uint first = 100;
            IntPtr menu = CreatePopupMenu();
            try
            {
                int hr = _menu.QueryContextMenu(menu, 0, first, first + 50, flags);
                Assert.True(hr >= 0, $"QueryContextMenu 실패 0x{hr:X8}");
                added = hr & 0xFFFF;
                int n = GetMenuItemCount(menu);
                var items = new List<(string, uint)>();
                for (int i = 0; i < n; i++)
                {
                    var sb = new System.Text.StringBuilder(256);
                    GetMenuStringW(menu, (uint)i, sb, sb.Capacity, MF_BYPOSITION);
                    items.Add((sb.ToString(), GetMenuItemID(menu, i) - first));
                }
                return items.ToArray();
            }
            finally
            {
                DestroyMenu(menu);
            }
        }

        /// <summary>그 번호(첫 번호에서 떨어진 값)의 동사 이름(GCS_VERBW). 없으면 null.</summary>
        public string? Verb(uint offset)
        {
            const uint GCS_VERBW = 4;
            IntPtr buffer = Marshal.AllocHGlobal(512);
            try
            {
                int hr = _menu.GetCommandString((UIntPtr)offset, GCS_VERBW, IntPtr.Zero, buffer, 256);
                return hr < 0 ? null : Marshal.PtrToStringUni(buffer);
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }

        public void Dispose() => Marshal.FinalReleaseComObject(_init);
    }

    // ───── 드롭 처리기(IPersistFile + IDropTarget) ─────

    public static DropHandler CreateDropHandler() => new(CreateAs<IPersistFile>(new Guid("61C3F459-E40B-441D-9F76-F56C70DFF8C1")));

    public sealed class DropHandler : IDisposable
    {
        private readonly IPersistFile _file;
        private readonly IDropTarget _target;

        public DropHandler(IPersistFile file)
        {
            _file = file;
            _target = (IDropTarget)file;
        }

        public Guid ClassId()
        {
            Marshal.ThrowExceptionForHR(_file.GetClassID(out Guid g));
            return g;
        }

        public void Load(string archive) => Marshal.ThrowExceptionForHR(_file.Load(archive, 0));

        public string? CurFile()
        {
            Marshal.ThrowExceptionForHR(_file.GetCurFile(out IntPtr raw));
            return TakeString(raw);
        }

        public uint DragEnter(string[] files, uint allowed)
        {
            IntPtr data = FileDropData(files);
            try
            {
                uint effect = allowed;
                Marshal.ThrowExceptionForHR(_target.DragEnter(data, 0, default, ref effect));
                return effect;
            }
            finally
            {
                Marshal.Release(data);
            }
        }

        /// <summary>파일이 아닌 것(글)을 끌어 옴.</summary>
        public uint DragEnterText(uint allowed)
        {
            var obj = new System.Windows.DataObject(System.Windows.DataFormats.UnicodeText, "그냥 글");
            IntPtr data = Marshal.GetComInterfaceForObject(obj, typeof(ComDataObject));
            try
            {
                uint effect = allowed;
                Marshal.ThrowExceptionForHR(_target.DragEnter(data, 0, default, ref effect));
                return effect;
            }
            finally
            {
                Marshal.Release(data);
            }
        }

        public uint DragOver(uint allowed)
        {
            uint effect = allowed;
            Marshal.ThrowExceptionForHR(_target.DragOver(0, default, ref effect));
            return effect;
        }

        public void DragLeave() => Marshal.ThrowExceptionForHR(_target.DragLeave());

        public void Dispose() => Marshal.FinalReleaseComObject(_file);
    }

    /// <summary>CF_HDROP 을 주는 데이터(WPF DataObject 의 COM IDataObject 포인터 — 부르는 쪽이 Release).</summary>
    private static IntPtr FileDropData(string[] files)
    {
        var obj = new System.Windows.DataObject(System.Windows.DataFormats.FileDrop, files);
        return Marshal.GetComInterfaceForObject(obj, typeof(ComDataObject));
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

    private const uint MF_BYPOSITION = 0x400;

    [DllImport("user32.dll")]
    private static extern IntPtr CreatePopupMenu();

    [DllImport("user32.dll")]
    private static extern bool DestroyMenu(IntPtr hMenu);

    [DllImport("user32.dll")]
    private static extern int GetMenuItemCount(IntPtr hMenu);

    [DllImport("user32.dll")]
    private static extern uint GetMenuItemID(IntPtr hMenu, int nPos);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetMenuStringW(IntPtr hMenu, uint uIDItem, System.Text.StringBuilder lpString, int cchMax, uint flags);

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

    /// <summary>IEnumExplorerCommand(vtable 차례 그대로).</summary>
    [ComImport, Guid("a88826f8-186f-4987-aade-ea0cef8fbfe8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IEnumExplorerCommand
    {
        [PreserveSig] int Next(uint celt, [Out, MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 0)] IntPtr[] pUICommand, out uint pceltFetched);
        [PreserveSig] int Skip(uint celt);
        [PreserveSig] int Reset();
        [PreserveSig] int Clone(out IntPtr ppenum);
    }

    /// <summary>IShellExtInit.</summary>
    [ComImport, Guid("000214E8-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IShellExtInit
    {
        [PreserveSig] int Initialize(IntPtr pidlFolder, IntPtr pdtobj, IntPtr hkeyProgID);
    }

    /// <summary>IContextMenu(vtable 차례 그대로). InvokeCommand 는 자리만 — 부르지 않는다.</summary>
    [ComImport, Guid("000214E4-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IContextMenu
    {
        [PreserveSig] int QueryContextMenu(IntPtr hmenu, uint indexMenu, uint idCmdFirst, uint idCmdLast, uint uFlags);
        [PreserveSig] int InvokeCommand(IntPtr pici);
        [PreserveSig] int GetCommandString(UIntPtr idCmd, uint uType, IntPtr pReserved, IntPtr pszName, uint cchMax);
    }

    /// <summary>IPersistFile(IPersist.GetClassID 포함, vtable 차례 그대로).</summary>
    [ComImport, Guid("0000010b-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IPersistFile
    {
        [PreserveSig] int GetClassID(out Guid pClassID);
        [PreserveSig] int IsDirty();
        [PreserveSig] int Load([MarshalAs(UnmanagedType.LPWStr)] string pszFileName, uint dwMode);
        [PreserveSig] int Save([MarshalAs(UnmanagedType.LPWStr)] string? pszFileName, [MarshalAs(UnmanagedType.Bool)] bool fRemember);
        [PreserveSig] int SaveCompleted([MarshalAs(UnmanagedType.LPWStr)] string? pszFileName);
        [PreserveSig] int GetCurFile(out IntPtr ppszFileName);
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct POINTL
    {
        public int X;
        public int Y;
    }

    /// <summary>IDropTarget(vtable 차례 그대로). Drop 은 자리만 — 부르지 않는다.</summary>
    [ComImport, Guid("00000122-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IDropTarget
    {
        [PreserveSig] int DragEnter(IntPtr pDataObj, uint grfKeyState, POINTL pt, ref uint pdwEffect);
        [PreserveSig] int DragOver(uint grfKeyState, POINTL pt, ref uint pdwEffect);
        [PreserveSig] int DragLeave();
        [PreserveSig] int Drop(IntPtr pDataObj, uint grfKeyState, POINTL pt, ref uint pdwEffect);
    }
}
