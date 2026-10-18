using System.Text;
using Iljip.Models;
using Iljip.Services;
using Iljip.Services.Editing;
using Iljip.Tests.TestSupport;
using Iljip.ViewModels;

namespace Iljip.Tests.Contract;

/// <summary>
/// [3차 통합 점검(2026-10-15)] IJ3-01(바로 열기·끌어 꺼내기·Ctrl+C·그림 미리 보기)·IJ3-03(인터넷 표시 MOTW)이 앞 묶음과 함께 쓰일 때 어긋나지 않는지 본다.
/// 묶음마다의 시험(<see cref="InternetMarkTests"/>·<see cref="EntryOpenTests"/>·<see cref="DragOutTests"/>·<see cref="ClipboardCopyTests"/>)은 자기 기능만 보므로 여기서는 겹치는 길만.
/// <list type="bullet">
/// <item>같은 이름 [이름 바꾸기](W2-05) × 인터넷 표시: '이름 (1)' 로 비켜 푼 사본에 붙고, 원래 있던 파일에는 안 붙는다(ZIP·7z.dll·EGG 길).</item>
/// <item>압축 고치기(W2-06 — 넣기) × 인터넷 표시: 고쳐 바꿔 넣은 압축도 원래 압축의 표시를 잃지 않고, 다시 풀면 넣은 파일까지 붙는다.</item>
/// <item>압축 속 파일 열기·다시 넣기(W2-06·IJ3-01) × 인터넷 표시: 연 파일에 붙고, 고쳐 다시 넣어도 압축의 표시는 그대로.</item>
/// <item>나눠 만들기(W2-03) × 인터넷 표시: 받은 조각 묶음(ZIP .z01…·7Z .001…)을 창에서 풀어도 붙는다.</item>
/// <item>자동 풀림 exe(W2-10 일집 스텁·7-Zip 자동 풀림) × 인터넷 표시: 받은 exe 를 일집 창에서 풀어도 붙는다.</item>
/// </list>
/// 창 없이 풀기(재사용장치)는 붙이지 않음 — 실제 exe 로 <see cref="ReuseDeviceContractTests"/>. 화면·셸·휴지통은 모두 가짜(진짜 탐색기·오피스는 부르지 않음).
/// </summary>
public class Wave3IntegrationTests
{
    /// <summary>메일에서 받은 파일에 Windows 가 붙이는 모양(한글 주소 — 바이트 그대로 옮기는지).</summary>
    private static readonly byte[] Mark = Encoding.UTF8.GetBytes(
        "[ZoneTransfer]\r\nZoneId=3\r\nReferrerUrl=https://mail.example.com/inbox\r\nHostUrl=https://files.example.com/받은 첨부/모음.zip\r\n");

    private static void SetMark(string path) => File.WriteAllBytes(path + ZoneIdentifier.StreamName, Mark);

    private static byte[]? MarkOf(string path) => ZoneIdentifier.Read(path);

    private static List<string> FilesUnder(string dir) => Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories).ToList();

    private static void AssertAllMarked(string dir, string context)
    {
        var files = FilesUnder(dir);
        Assert.NotEmpty(files);
        foreach (var f in files)
            Assert.True(MarkOf(f) is { } m && m.SequenceEqual(Mark), $"{context}: 표시가 없거나 원본과 다름: {f}");
    }

    private static async Task<bool> Extract(MainViewModel vm, string archive, string dest)
    {
        bool ok = false;
        await VmRunner.Run(async () => ok = await vm.ExtractToFolderAsync(archive, dest, null), "풀기");
        return ok;
    }

    /// <summary>a.txt·b.txt·폴더/c.txt 를 담은 ZIP 또는 7Z(일집 서비스로 만듦).</summary>
    private static async Task<string> MakeAbc(TempDir t, string format)
    {
        string src = t.Dir("원본");
        File.WriteAllText(Path.Combine(src, "a.txt"), "압축 속 a");
        File.WriteAllText(Path.Combine(src, "b.txt"), "압축 속 b");
        string arc = Path.Combine(t.Dir("받은 첨부"), "받은." + format);
        var files = new[] { Path.Combine(src, "a.txt"), Path.Combine(src, "b.txt") };
        if (format == "zip") await new ZipArchiveService().CompressAsync(files, arc);
        else await new SevenZipArchiveService().CompressAsync(files, arc);
        return arc;
    }

    // ───────────── 같은 이름 [이름 바꾸기](W2-05) × 인터넷 표시(IJ3-03) ─────────────

    /// <summary>
    /// 계약: 풀 곳에 같은 이름이 있어 [이름 바꾸기]를 고르면 압축 내용은 'a (1).txt' 로 풀리고 그 사본에 표시가 붙는다 — 원래 있던 'a.txt' 는 내용도 표시도 그대로.
    /// SharpCompress 직접 쓰기(ZIP)·7z.dll 임시 폴더에서 옮기기(7Z) 두 길 모두(이름 바꾼 경로를 '이번에 만든 파일'로 적는지).
    /// </summary>
    [Theory]
    [InlineData("zip")]
    [InlineData("7z")]
    public async Task 같은_이름을_이름_바꾸기로_비켜_풀면_사본에만_표시가_붙는다(string format)
    {
        using var t = new TempDir("w3ren");
        string arc = await MakeAbc(t, format);
        SetMark(arc);
        string dest = t.Dir("풀 곳");
        string userA = Path.Combine(dest, "a.txt");
        File.WriteAllText(userA, "사용자 a");
        var ui = new FakeUserInteraction();
        ui.CollisionAnswers.Enqueue(new CollisionAnswer(CollisionChoice.Rename, false));

        Assert.True(await Extract(new MainViewModel(ui, null, new TestServices().Services), arc, dest), string.Join(" | ", ui.Errors));

        Assert.Single(ui.CollisionPrompts);
        Assert.Equal("사용자 a", File.ReadAllText(userA));
        Assert.Null(MarkOf(userA));
        string copy = Path.Combine(dest, "a (1).txt");
        Assert.Equal("압축 속 a", File.ReadAllText(copy));
        Assert.Equal(Mark, MarkOf(copy));
        Assert.Equal(Mark, MarkOf(Path.Combine(dest, "b.txt")));
        Assert.Equal(3, FilesUnder(dest).Count);
        CollisionTests.NoLeftovers(dest, format);
    }

    /// <summary>계약: EGG(EggDotNet 길)도 같다 — 폴더 안의 같은 이름을 [이름 바꾸기]로 비켜 푼 사본에 붙고 원래 파일은 그대로.</summary>
    [Fact]
    public async Task EGG도_이름_바꾸기로_비켜_푼_사본에만_표시가_붙는다()
    {
        using var t = new TempDir("w3renegg");
        var b = SyntheticArchives.Build("egg", t.Dir("받은 첨부"));
        SetMark(b.Path);
        var victim = b.Expected.First(e => !e.IsDir && e.Size > 0);
        string dest = t.Dir("풀 곳");
        string userFile = Path.Combine(dest, victim.Path.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(userFile)!);
        File.WriteAllText(userFile, "사용자 파일");
        var ui = new FakeUserInteraction();
        ui.CollisionAnswers.Enqueue(new CollisionAnswer(CollisionChoice.Rename, false));

        Assert.True(await Extract(new MainViewModel(ui, null, new TestServices().Services), b.Path, dest), string.Join(" | ", ui.Errors));

        Assert.Single(ui.CollisionPrompts);
        Assert.Equal("사용자 파일", File.ReadAllText(userFile));
        Assert.Null(MarkOf(userFile));
        string copy = Path.Combine(Path.GetDirectoryName(userFile)!,
            Path.GetFileNameWithoutExtension(userFile) + " (1)" + Path.GetExtension(userFile));
        Assert.True(File.Exists(copy), "비켜 푼 사본이 없음: " + copy);
        Assert.Equal(victim.Sha256, TreeAssert.Sha256(copy));
        foreach (var f in FilesUnder(dest).Where(f => !string.Equals(f, userFile, StringComparison.OrdinalIgnoreCase)))
            Assert.True(MarkOf(f) is { } m && m.SequenceEqual(Mark), "표시가 없음: " + f);
    }

    // ───────────── 압축 고치기(W2-06) × 인터넷 표시(IJ3-03) ─────────────

    /// <summary>
    /// 계약: 인터넷에서 받은 압축에 파일을 넣어 고쳐도(임시 파일에 쓰고 바꿔 넣기 — <see cref="ArchiveSwap"/>) 압축 파일의 표시는 바이트 그대로 남는다
    /// (고친다고 받은 파일이 '내 PC 에서 만든 것'이 되지 않게). 그 압축을 창에서 다시 풀면 넣은 파일까지 모두 붙는다.
    /// </summary>
    [Theory]
    [InlineData("zip")]
    [InlineData("7z")]
    public async Task 표시가_있는_압축을_고쳐도_표시가_남고_다시_풀면_넣은_파일까지_붙는다(string format)
    {
        using var t = new TempDir("w3edit");
        string arc = await MakeAbc(t, format);
        SetMark(arc);
        string add = Path.Combine(t.Dir("넣을 것"), "새 문서.txt");
        File.WriteAllText(add, "내가 넣은 내용");

        var editor = ArchiveServiceLocator.EditorFor(arc);
        Assert.NotNull(editor);
        await editor!.EditAsync(arc, new EditOp[] { new EditOp.Add(new[] { add }, string.Empty) }, null, null, CancellationToken.None);

        Assert.Equal(Mark, MarkOf(arc));
        Assert.DoesNotContain(Directory.EnumerateFiles(Path.GetDirectoryName(arc)!), f => ArchiveSwap.IsLeftover(f, arc));

        string dest = t.Dir("풀 곳");
        var ui = new FakeUserInteraction();
        Assert.True(await Extract(new MainViewModel(ui, null, new TestServices().Services), arc, dest), string.Join(" | ", ui.Errors));
        Assert.Equal("내가 넣은 내용", File.ReadAllText(Path.Combine(dest, "새 문서.txt")));
        Assert.Equal(3, FilesUnder(dest).Count);
        AssertAllMarked(dest, format + " 고친 뒤");
    }

    // ───────────── 압축 속 파일 열기·다시 넣기(W2-06·IJ3-01) × 인터넷 표시(IJ3-03) ─────────────

    /// <summary>
    /// 계약: 인터넷에서 받은 압축 속 파일을 열면(바로 열기) 연 사본에 표시가 붙는다(오피스 '제한된 보기'). 그 파일을 고쳐 저장해 [넣기]로 다시 넣어도
    /// 묻기는 한 번이고(표시를 붙인 것을 '고침'으로 보지 않음), 압축에는 새 내용이 들어가며 압축 파일의 표시는 그대로다.
    /// </summary>
    [Theory]
    [InlineData("zip")]
    [InlineData("7z")]
    public async Task 표시가_있는_압축에서_연_파일을_다시_넣어도_압축의_표시는_그대로다(string format)
    {
        using var t = new TempDir("w3reins");
        string arc = await MakeAbc(t, format);
        SetMark(arc);
        var ts = new TestServices();
        var ui = new FakeUserInteraction { ChooseByLabel = "넣기" };
        var vm = new MainViewModel(ui, null, ts.Services) { EntryChangeQuietPeriod = TimeSpan.FromMilliseconds(30) };
        try
        {
            await VmRunner.Run(() => vm.OpenArchiveAsync(arc), "열기");
            var entry = vm.Entries.Single(e => TreeAssert.NormalizeEntryPath(e.Path) == "a.txt");
            bool opened = false;
            await VmRunner.Run(async () => opened = await vm.OpenEntryAsync(entry), "안의 파일 열기");
            Assert.True(opened, string.Join(" | ", ui.Errors));
            string openedPath = Assert.Single(ts.Shell.OpenedFiles);
            Assert.Equal(Mark, MarkOf(openedPath));
            Assert.Equal("압축 속 a", File.ReadAllText(openedPath));

            File.WriteAllText(openedPath, "받은 뒤 고친 a");
            Assert.Equal(1, ts.FileChanges.Raise(openedPath));
            await VmRunner.Run(() => vm.WhenEntryChecksIdleAsync(), "바뀜 확인");

            Assert.Single(ui.ChoicePrompts);
            Assert.Empty(ui.Errors);
            Assert.Equal(Mark, MarkOf(arc));
            string check = t.Dir("확인");
            await Svc.For(arc).ExtractAsync(arc, check);
            Assert.Equal("받은 뒤 고친 a", File.ReadAllText(Path.Combine(check, "a.txt")));
            Assert.Equal("압축 속 b", File.ReadAllText(Path.Combine(check, "b.txt")));
        }
        finally
        {
            vm.Shutdown();
        }
    }

    // ───────────── 나눠 만들기(W2-03) × 인터넷 표시(IJ3-03) ─────────────

    /// <summary>
    /// 계약: 메일로 받은 조각 묶음(조각마다 표시 — 일집이 나눠 만든 ZIP '.z01 … .zip'·7Z '.7z.001 …')을 일집 창에서 풀면 풀린 파일마다 표시가 붙는다.
    /// 조각 파일들의 표시는 그대로.
    /// </summary>
    [Theory]
    [InlineData("zip", 8000)]
    [InlineData("7z", 16 * 1024)]
    public async Task 나눠_만든_조각_묶음도_창에서_풀면_표시가_붙는다(string format, long volume)
    {
        using var t = new TempDir("w3split");
        string root = SampleTree.Create(t.Dir("src"));
        string arc = Path.Combine(t.Dir("받은 첨부"), "조각." + format);
        var options = new CompressionOptions { SplitBytes = volume };
        var result = format == "zip"
            ? await new ZipArchiveService().CompressAsync(new[] { root }, arc, options)
            : await new SevenZipArchiveService().CompressAsync(new[] { root }, arc, options);
        Assert.True(result.Volumes.Count >= 3, "조각 수: " + result.Volumes.Count);
        foreach (var v in result.Volumes) SetMark(v);

        string dest = t.Dir("풀 곳");
        var ui = new FakeUserInteraction();
        Assert.True(await Extract(new MainViewModel(ui, null, new TestServices().Services), result.OpenPath!, dest), string.Join(" | ", ui.Errors));

        TreeAssert.ExtractedTreeEquals(dest, SampleTree.ExpectedOf(root), context: format + " 조각");
        AssertAllMarked(dest, format + " 조각");
        foreach (var v in result.Volumes) Assert.Equal(Mark, MarkOf(v));
    }

    // ───────────── 자동 풀림 exe(W2-10·W2-01) × 인터넷 표시(IJ3-03) ─────────────

    /// <summary>
    /// 계약: 받은 자동 풀림 exe(일집이 만든 것 — ZIP 꼬리, 7-Zip 이 만든 것 — 7z.dll)를 실행하지 않고 일집 창에서 풀어도 풀린 파일에 표시가 붙는다.
    /// (받는 사람이 exe 를 두 번 눌러 풀 때 — 일집 스텁 — 는 옮기지 않음: HANDOFF '향후 작업'.)
    /// </summary>
    [Fact]
    public async Task 일집이_만든_자동풀림_exe를_창에서_풀어도_표시가_붙는다()
    {
        using var t = new TempDir("w3sfx");
        string root = SampleTree.Create(t.Dir("src"));
        string exe = Path.Combine(t.Dir("받은 첨부"), "자료 모음.exe");
        await SfxBuilder.CreateAsync(new[] { root }, exe, null);
        SetMark(exe);

        string dest = t.Dir("풀 곳");
        var ui = new FakeUserInteraction();
        Assert.True(await Extract(new MainViewModel(ui, null, new TestServices().Services), exe, dest), string.Join(" | ", ui.Errors));

        TreeAssert.ExtractedTreeEquals(dest, SampleTree.ExpectedOf(root), context: "일집 자동 풀림");
        AssertAllMarked(dest, "일집 자동 풀림");
    }

    /// <summary>계약: 7-Zip 이 만든 자동 풀림 exe(픽스처 sfx_default — 7z.dll 길)도 같다. 그 픽스처는 git 제외(local/)라 없으면 건너뛴다(I4-1 G2).</summary>
    [FixtureFact("local/sfx_default.exe")]
    public async Task 다른_프로그램이_만든_자동풀림_exe를_창에서_풀어도_표시가_붙는다()
    {
        var fx = FixtureCatalog.Get("sfx_default");
        using var t = new TempDir("w3sfx7");
        string exe = Path.Combine(t.Dir("받은 첨부"), Path.GetFileName(fx.FullPath));
        File.Copy(fx.FullPath, exe);
        SetMark(exe);

        string dest = t.Dir("풀 곳");
        var ui = new FakeUserInteraction();
        Assert.True(await Extract(new MainViewModel(ui, null, new TestServices().Services), exe, dest), string.Join(" | ", ui.Errors));

        TreeAssert.ExtractedTreeEquals(dest, fx.Expected, context: "sfx_default");
        AssertAllMarked(dest, "sfx_default");
    }

    // ───────────── 오류 문구 전수(소스) — 새 기능까지 ─────────────

    /// <summary>
    /// 계약(소스): 본체가 화면에 보이는 글(오류·경고·알림 창, 상태 줄, 선택 창, 진행 창, 메시지 상자)과 던지는 예외 문구에 **영어만으로 된 글이 없다** —
    /// 새 기능(IJ3-01 끌기·Ctrl+C·미리 보기, IJ3-03 인터넷 표시)까지 본체 전체 .cs 와 XAML(Text·Content·ToolTip·Header·Title)을 훑는다.
    /// 예외는 셋뿐: 창 없이 풀기의 'Unsupported archive type'(재사용장치가 읽는 약속 — <see cref="ReuseDeviceContractTests"/>), 로그에만 남는 예외 하나,
    /// 보간 안의 <c>string.Join</c> 인자(문구가 아님 — 정규식이 따옴표를 나눠 읽음). 엔진이 던진 영어는 <see cref="ErrorMessages"/> 가 바꾼다(묶음마다의 시험).
    /// </summary>
    [Fact]
    public void 화면과_예외_문구에_영어만으로_된_글이_없다()
    {
        var uiCall = new System.Text.RegularExpressions.Regex(
            @"(ShowError|ShowWarning|ShowInfo|StatusText\s*=|ProgressText\s*=|Message\s*=|\.Choose\(|ShowProgress\(|throw new|new [\w.]*Exception\(|MessageBox\.Show|Console\.(Error\.)?Write)");
        var literal = new System.Text.RegularExpressions.Regex("\\$?@?\"(?:[^\"\\\\]|\\\\.)*\"");
        var hole = new System.Text.RegularExpressions.Regex(@"\{[^}]*\}");
        var english = new System.Text.RegularExpressions.Regex("[A-Za-z]{3,}");
        var hangul = new System.Text.RegularExpressions.Regex("[가-힣]");
        var allowed = new (string File, string Text)[]
        {
            ("App.xaml.cs", "Unsupported archive type"),        // 재사용장치 약속(종료 코드 2)
            ("ZoneIdentifier.cs", "Zone.Identifier too large"), // 로그에만 남는 예외(화면에 안 나감)
            ("MainViewModel.Shell.cs", ", notes)}"),            // $"…{string.Join("\n\n", notes)}" 를 정규식이 나눠 읽은 조각
        };
        var hits = new List<string>();
        string sep = Path.DirectorySeparatorChar.ToString();
        foreach (var file in Directory.EnumerateFiles(RepoFiles.PathOf("Iljip"), "*.*", SearchOption.AllDirectories))
        {
            string rel = Path.GetRelativePath(RepoFiles.Root, file);
            if (rel.Contains(sep + "obj" + sep) || rel.Contains(sep + "bin" + sep)) continue;
            string name = Path.GetFileName(file);
            bool cs = name.EndsWith(".cs", StringComparison.OrdinalIgnoreCase);
            bool xaml = name.EndsWith(".xaml", StringComparison.OrdinalIgnoreCase);
            if (!cs && !xaml) continue;
            int lineNo = 0;
            foreach (var line in File.ReadLines(file))
            {
                lineNo++;
                if (cs)
                {
                    if (line.TrimStart().StartsWith("//", StringComparison.Ordinal) || !uiCall.IsMatch(line)) continue;
                    foreach (System.Text.RegularExpressions.Match m in literal.Matches(line))
                    {
                        string plain = hole.Replace(m.Value, string.Empty);
                        if (!english.IsMatch(plain) || hangul.IsMatch(plain)) continue;
                        if (allowed.Any(a => a.File == name && m.Value.Contains(a.Text, StringComparison.Ordinal))) continue;
                        hits.Add($"{rel}:{lineNo}: {m.Value}");
                    }
                }
                else
                {
                    foreach (System.Text.RegularExpressions.Match m in System.Text.RegularExpressions.Regex.Matches(line, "\\b(Text|Content|ToolTip|Header|Title)=\"([^\"{]*)\""))
                    {
                        string v = m.Groups[2].Value;
                        if (english.IsMatch(v) && !hangul.IsMatch(v)) hits.Add($"{rel}:{lineNo}: {m.Value}");
                    }
                }
            }
        }
        Assert.True(hits.Count == 0, "영어만으로 된 화면·예외 문구:\n  " + string.Join("\n  ", hits));
    }

    // ───────────── 재배포 준비 — 셸 확장 설치 스크립트 ─────────────

    /// <summary>
    /// 계약(소스): 셸 확장 패키지 설치(<c>packaging\Install.ps1</c>·<c>Rebuild.ps1</c>)는 <c>-SystemVolume</c> 을 줄 때만 C:(시스템 볼륨)에 넣는다 —
    /// Windows '새 앱 저장 위치'를 D: 로 바꿔 둔 PC 는 0x80070032('시스템 볼륨에 설치되어 있지 않아…')로 실패할 수 있어서(3차 통합 점검에서 이 PC 의
    /// OneDrive 패키지가 그렇게 실패하는 것을 확인). 주지 않으면 예전과 똑같이 기본 볼륨, 0x80070032 면 다시 할 명령을 쉬운 말로 알린다.
    /// PowerShell 5.1 이 한글을 읽게 UTF-8 BOM 은 그대로(HANDOFF '알려진 함정' 1). 실제 설치는 사람이 할 일(시험은 설치하지 않음).
    /// </summary>
    [Fact]
    public void 셸_확장_설치는_시스템_볼륨을_고를_수_있고_기본은_그대로다()
    {
        string install = RepoFiles.Read("packaging", "Install.ps1");
        string rebuild = RepoFiles.Read("packaging", "Rebuild.ps1");
        Assert.Contains("[switch]$SystemVolume", install);
        Assert.Contains("[switch]$SystemVolume", rebuild);
        Assert.Contains("Install.ps1\" -BuildDir $BuildDir -SystemVolume:$SystemVolume", rebuild);
        Assert.Matches(@"if \(\$SystemVolume\) \{\s*\$systemVolume = Get-AppxVolume \| Where-Object \{ \$_\.IsSystemVolume \}", install);
        Assert.Single(System.Text.RegularExpressions.Regex.Matches(install, @"\$addArgs\.Volume ="));   // -SystemVolume 안에서만
        Assert.Contains("Add-AppxPackage @addArgs", install);
        Assert.DoesNotContain("Set-AppxDefaultVolume", install + rebuild);   // PC 전체 설정은 바꾸지 않음(사람이 정할 일)
        Assert.Contains("0x80070032", install);
        Assert.Contains(".\\Rebuild.ps1 -SystemVolume", install);
        foreach (var f in new[] { "Install.ps1", "Rebuild.ps1" })
        {
            byte[] b = File.ReadAllBytes(RepoFiles.PathOf("packaging", f));
            Assert.True(b.Length > 3 && b[0] == 0xEF && b[1] == 0xBB && b[2] == 0xBF, f + ": UTF-8 BOM 이 없음(PowerShell 5.1 이 한글을 못 읽음)");
        }
    }
}
