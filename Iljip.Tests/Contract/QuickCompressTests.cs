using System.IO.Compression;
using Iljip.Models;
using Iljip.Services;
using Iljip.Tests.TestSupport;
using Iljip.ViewModels;

namespace Iljip.Tests.Contract;

/// <summary>
/// [W2-12 I1-2] 창 없이 바로 압축(탐색기 우클릭 '"이름.zip"으로 압축'·'"이름.7z"로 압축' = '--compress-zip'·'--compress-7z'):
/// 압축 창을 띄우지 않고 첫 원본이 있는 폴더에 '이름.zip/.7z'(이름은 압축 창 제안·메뉴 제목과 같은 규칙, 있으면 '이름 (2)').
/// 수준·솔리드·쓸모없는 파일 빼기·'다 만들고 시험하기'는 마지막 설정(바꾸지 않음), 암호·나누기·날짜·원본 지우기는 쓰지 않는다.
/// 메인 창 없이 진행 창만 — 되면 조용히, 뺀 파일·건너뛴 파일이 있을 때만 안내 하나. 화면은 가짜.
/// </summary>
public class QuickCompressTests
{
    private static Task Run(Func<Task> action, string what) => VmRunner.Run(action, what);

    private static List<string> ZipNames(string zip)
    {
        using var z = ZipFile.OpenRead(zip);
        return z.Entries.Where(e => !e.FullName.EndsWith('/')).Select(e => e.FullName).OrderBy(n => n, StringComparer.Ordinal).ToList();
    }

    private static async Task<List<string>> NamesIn(string archive) =>
        (await Svc.For(archive).ListEntriesAsync(archive, null))
            .Where(e => !e.IsDirectory)
            .Select(e => TreeAssert.NormalizeEntryPath(e.Path))
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToList();

    /// <summary>폴더 하나 → 옆에 '폴더이름.zip'(안은 '폴더이름\…'), 압축 창·질문·안내 없음, 설정 저장 없음, 메인 창 없이(ShowMainWindow false).</summary>
    [Fact]
    public async Task 폴더_하나를_바로_zip으로()
    {
        using var tmp = new TempDir("qzip1");
        string folder = tmp.Dir("보고서 2026");
        File.WriteAllText(Path.Combine(folder, "본문.txt"), "본문");
        Directory.CreateDirectory(Path.Combine(folder, "그림"));
        File.WriteAllText(Path.Combine(folder, "그림", "a.txt"), "그림");
        var ui = new FakeUserInteraction();
        var svc = new TestServices();
        var vm = new MainViewModel(ui, null, svc.Services);
        Assert.False(CommandLineParser.Parse(new[] { "--compress-zip", folder }).ShowMainWindow);
        Assert.Equal("\"보고서 2026.zip\"으로 압축", ShellMenu.ItemsFor(new[] { new ShellMenu.Item(folder, true) })[1].Title);   // 메뉴 제목과 같은 이름

        await Run(() => vm.ProcessShellCommandAsync(new[] { "--compress-zip", folder }), "바로 압축");

        string zip = Path.Combine(tmp.Root, "보고서 2026.zip");
        Assert.Equal(new[] { "보고서 2026/그림/a.txt", "보고서 2026/본문.txt" }, ZipNames(zip));
        Assert.Empty(ui.CompressionPrompts);
        Assert.Empty(ui.Errors);
        Assert.Empty(ui.Warnings);
        Assert.Empty(ui.Infos);
        Assert.Empty(ui.PasswordPrompts);
        Assert.Single(ui.ProgressSessions);
        Assert.Equal(new[] { MainViewModel.QuickCompressTitle }, ui.ProgressTitles);
        Assert.Equal(0, svc.Settings.SaveCount);   // 마지막 설정을 읽기만
        Assert.Equal($"압축 완료: {zip}", vm.StatusText);
        Assert.False(vm.IsArchiveMode);            // 메인 창의 보던 것은 그대로(만든 압축을 열지 않음)
        Assert.Empty(svc.Shell.OpenedFolders);
    }

    /// <summary>파일 여럿 → 그 폴더 이름.zip, 같은 이름이 있으면 묻지 않고 '이름 (2).zip'(있던 것은 그대로).</summary>
    [Fact]
    public async Task 여럿은_폴더_이름으로_같은_이름이면_2()
    {
        using var tmp = new TempDir("qzip2");
        string dir = tmp.Dir("견적");
        string a = Path.Combine(dir, "가.txt"), b = Path.Combine(dir, "나.txt");
        File.WriteAllText(a, "가");
        File.WriteAllText(b, "나");
        string existing = Path.Combine(dir, "견적.zip");
        File.WriteAllText(existing, "예전 압축");
        var ui = new FakeUserInteraction();
        var svc = new TestServices();
        var items = new[] { new ShellMenu.Item(a, false), new ShellMenu.Item(b, false) };
        Assert.Equal("\"견적.zip\"으로 압축", ShellMenu.ItemsFor(items)[1].Title);

        await Run(() => new MainViewModel(ui, null, svc.Services).ProcessShellCommandAsync(new[] { "--compress-zip", a, b }), "바로 압축");

        Assert.Equal("예전 압축", File.ReadAllText(existing));
        Assert.Equal(new[] { "가.txt", "나.txt" }, ZipNames(Path.Combine(dir, "견적 (2).zip")));
        Assert.Empty(ui.CollisionPrompts);
        Assert.Empty(ui.ChoicePrompts);
        Assert.Empty(ui.Errors);
    }

    /// <summary>
    /// 7z: 파일 하나 '보고서.xlsx' → '보고서.7z'. 마지막 설정(수준·솔리드)을 그대로 쓰고 바꾸지 않는다 — 요청은 날짜·암호·나누기·원본 지우기 없음.
    /// </summary>
    [Fact]
    public async Task 파일_하나를_바로_7z로_마지막_설정대로()
    {
        using var tmp = new TempDir("q7z");
        string file = Path.Combine(tmp.Root, "보고서.xlsx");
        File.WriteAllText(file, string.Concat(Enumerable.Repeat("가나다라 ", 2000)));
        var svc = TestServices.WithCompress(c =>
        {
            c.Level = CompressionLevelOption.Maximum;
            c.Solid = false;
            c.DateInName = DateNameFormat.YearMonthDay;   // 창 없이 바로 압축은 날짜를 붙이지 않는다
            c.TestAfter = true;
        });
        svc.CompressTester.Pass();
        var ui = new FakeUserInteraction();
        var vm = new MainViewModel(ui, null, svc.Services);

        var request = vm.BuildQuickCompressRequest(new[] { file }, CompressionFormat.SevenZip);
        Assert.Equal((tmp.Root, "보고서", CompressionFormat.SevenZip), (request.OutputFolder, request.BaseName, request.Format));
        Assert.Equal((CompressionLevelOption.Maximum, false, (string?)null), (request.Options.Level, request.Options.Solid, request.Options.Password));
        Assert.Equal((DateNameFormat.None, false, false, false, true), (request.DateInName, request.DeleteOriginalsAfter, request.OverwriteExisting, request.EachSeparately, request.TestAfter));

        await Run(() => vm.ProcessShellCommandAsync(new[] { "--compress-7z", file }), "바로 7z");

        string seven = Path.Combine(tmp.Root, "보고서.7z");
        Assert.Equal(new[] { "보고서.xlsx" }, await NamesIn(seven));
        Assert.Equal(new[] { seven }, svc.CompressTester.Tested);   // '다 만들고 시험하기'(마지막 설정)
        Assert.Empty(ui.CompressionPrompts);
        Assert.Empty(ui.Errors);
        Assert.Equal(0, svc.Settings.SaveCount);
        Assert.Equal(CompressionLevelOption.Maximum, vm.Settings.Compress.Level);
    }

    /// <summary>쓸모없는 파일 빼기(설정 켬 — 기본): Thumbs.db 는 빼고, 뺐다고 안내 하나(무엇을 뺐는지).</summary>
    [Fact]
    public async Task 뺀_파일이_있으면_한_번_알린다()
    {
        using var tmp = new TempDir("qzip3");
        string folder = tmp.Dir("사진");
        File.WriteAllText(Path.Combine(folder, "a.txt"), "a");
        File.WriteAllText(Path.Combine(folder, "Thumbs.db"), "썸네일");
        var ui = new FakeUserInteraction();
        var svc = new TestServices();

        await Run(() => new MainViewModel(ui, null, svc.Services).ProcessShellCommandAsync(new[] { "--compress-zip", folder }), "바로 압축");

        Assert.Equal(new[] { "사진/a.txt" }, ZipNames(Path.Combine(tmp.Root, "사진.zip")));
        string info = Assert.Single(ui.Infos);
        Assert.StartsWith("‘사진.zip’을 만들었어요.", info);
        Assert.Contains("Thumbs.db", info);
        Assert.Empty(ui.Errors);
    }

    /// <summary>원본이 그새 없어졌으면 쉬운 한국어 오류 하나(아무것도 만들지 않음).</summary>
    [Fact]
    public async Task 원본이_없으면_알린다()
    {
        using var tmp = new TempDir("qzip4");
        string gone = Path.Combine(tmp.Root, "없는 폴더");
        var ui = new FakeUserInteraction();

        await Run(() => new MainViewModel(ui, null, new TestServices().Services).ProcessShellCommandAsync(new[] { "--compress-zip", gone }), "바로 압축");

        string err = Assert.Single(ui.Errors);
        Assert.StartsWith("압축할 파일을 찾을 수 없어요.", err);
        Assert.Contains(gone, err);
        Assert.Empty(Directory.EnumerateFileSystemEntries(tmp.Root));
        Assert.Empty(ui.ProgressSessions);
    }

    /// <summary>해석: '--compress-zip'·'--compress-7z' 는 뒤의 경로를 모두 원본으로(경로가 없으면 None). 다시 만든 인자도 같다.</summary>
    [Fact]
    public void 명령줄_해석()
    {
        var zip = CommandLineParser.Parse(new[] { "--compress-zip", @"C:\a", @"C:\b.txt" });
        Assert.Equal(CommandVerb.CompressZip, zip.Verb);
        Assert.Equal(new[] { @"C:\a", @"C:\b.txt" }, zip.Paths);
        var seven = CommandLineParser.Parse(new[] { "--compress-7z", @"C:\a" });
        Assert.Equal(CommandVerb.Compress7z, seven.Verb);
        Assert.Equal(new[] { "--compress-zip", @"C:\a", @"C:\b.txt" }, CommandLineParser.ToArgs(zip));
        Assert.Equal(CommandVerb.None, CommandLineParser.Parse(new[] { "--compress-zip" }).Verb);
    }
}
