using System.Text;
using Iljip.Services;
using Iljip.Tests.TestSupport;

namespace Iljip.Tests.Contract;

/// <summary>
/// [W2-01, B-086] 형식 라우팅 표(설계 §1): <see cref="ArchiveServiceLocator.Resolve"/> 는 확장자(가장 긴 것부터, .gz·.bz2·.xz 는 안쪽 tar 판별),
/// <see cref="ArchiveServiceLocator.ResolveByContent"/>(일집 안의 열기)는 확장자로 못 고를 때만 앞부분 서명을 보고,
/// <see cref="ArchiveServiceLocator.ResolveForAutomation"/>(창 없이 풀기)은 거기에 'ZIP 기반 문서는 늘 미지원'을 더한다.
/// 드롭 분류·저장 대상·셸 확장(<see cref="ArchiveServiceLocator.ResolveByExtension"/>)은 파일을 읽지 않는다.
/// </summary>
public class LocatorTests
{
    private static readonly ArchiveServiceLocator Locator = new();

    public static TheoryData<string, ArchiveKind> FixtureKinds => new()
    {
        { "zip_default", ArchiveKind.Zip },
        { "zip_split", ArchiveKind.Unknown },   // 분할 ZIP 의 마지막 조각(.zip)은 서명 없이 이어지는 데이터 → 확장자로
        { "7z_default", ArchiveKind.SevenZip },
        { "7z_header_encrypted", ArchiveKind.SevenZip },
        { "tar_default", ArchiveKind.Tar },
        { "tgz_default", ArchiveKind.TarGz },
        { "targz_default", ArchiveKind.TarGz },
        { "txz_two_step", ArchiveKind.TarXz },
        { "tbz2_python", ArchiveKind.TarBz2 },
        { "gz_single", ArchiveKind.GZip },
        { "bz2_single_python", ArchiveKind.BZip2 },
        { "xz_single", ArchiveKind.Xz },
        { "iso_default", ArchiveKind.Iso },
        { "lzh_default", ArchiveKind.Lzh },
        { "zipx_default", ArchiveKind.Zip },
        { "7z_split", ArchiveKind.SevenZip },
        { "zip_numbered_split", ArchiveKind.Zip },
    };

    /// <summary>계약: 픽스처마다 내용으로 알아본 종류가 맞다.</summary>
    [Theory]
    [MemberData(nameof(FixtureKinds))]
    public void 픽스처의_종류를_내용으로_알아본다(string id, ArchiveKind kind)
    {
        Assert.Equal(kind, ArchiveSniffer.Detect(FixtureCatalog.Get(id).FullPath));
    }

    /// <summary>계약: 서명만으로 알아보는 형식들과 '모름'(쓰레기·빈 파일·보통 exe).</summary>
    [Theory]
    [InlineData("rar5", ArchiveKind.Rar)]
    [InlineData("rar4", ArchiveKind.Rar)]
    [InlineData("egg", ArchiveKind.Egg)]
    [InlineData("alz", ArchiveKind.Alz)]
    [InlineData("cab", ArchiveKind.Cab)]
    [InlineData("wim", ArchiveKind.Wim)]
    [InlineData("zstd", ArchiveKind.Zstd)]
    [InlineData("garbage", ArchiveKind.Unknown)]
    [InlineData("empty", ArchiveKind.Unknown)]
    [InlineData("plain_exe", ArchiveKind.Unknown)]
    public void 서명으로_알아본다(string how, ArchiveKind kind)
    {
        using var tmp = new TempDir("sniff");
        string p = tmp.P("x.bin");
        File.WriteAllBytes(p, Sample(how));
        Assert.Equal(kind, ArchiveSniffer.Detect(p));
    }

    private static byte[] Sample(string how)
    {
        byte[] pad = Enumerable.Range(0, 2000).Select(i => (byte)(i * 13 + 1)).ToArray();
        byte[] head = how switch
        {
            "rar5" => new byte[] { 0x52, 0x61, 0x72, 0x21, 0x1A, 0x07, 0x01, 0x00 },
            "rar4" => new byte[] { 0x52, 0x61, 0x72, 0x21, 0x1A, 0x07, 0x00 },
            "egg" => "EGGA"u8.ToArray(),
            "alz" => new byte[] { 0x41, 0x4C, 0x5A, 0x01 },
            "cab" => "MSCF\0\0\0\0"u8.ToArray(),
            "wim" => "MSWIM\0\0\0"u8.ToArray(),
            "zstd" => new byte[] { 0x28, 0xB5, 0x2F, 0xFD },
            "garbage" => Array.Empty<byte>(),
            "empty" => Array.Empty<byte>(),
            "plain_exe" => "MZ"u8.ToArray(),
            _ => throw new ArgumentException(how),
        };
        return how == "empty" ? Array.Empty<byte>() : head.Concat(pad).ToArray();
    }

    /// <summary>
    /// 계약(B-086, 설계 §1 '찾는 순서'): 모르는 확장자로 이름이 바뀐 압축(.bin·.dat·.img)은 확장자로는 못 고르고(Resolve = null),
    /// 일집 안의 열기(ResolveByContent)는 내용대로 연다.
    /// </summary>
    [Theory]
    [InlineData("7z_default", "사진.bin", typeof(SevenZipArchiveService))]
    [InlineData("zip_default", "문서.dat", typeof(ZipArchiveService))]
    [InlineData("tgz_default", "묶음.bin", typeof(CompressedTarArchiveService))]
    [InlineData("tar_default", "묶음.dat", typeof(TarArchiveService))]
    [InlineData("iso_default", "디스크.img", typeof(SevenZipFormatsArchiveService))]
    [InlineData("lzh_default", "옛날.bin", typeof(SevenZipFormatsArchiveService))]
    public async Task 이름이_바뀐_압축은_내용으로_열린다(string id, string name, Type expected)
    {
        var fx = FixtureCatalog.Get(id);
        using var tmp = new TempDir("rename");
        string p = tmp.P(name);
        File.Copy(fx.FullPath, p);
        Assert.Null(Locator.Resolve(p));
        Assert.Null(Locator.ResolveByExtension(p));
        var svc = Locator.ResolveByContent(p);
        Assert.IsType(expected, svc);
        Assert.IsType(expected, Locator.ResolveForAutomation(p));
        TreeAssert.ListEquals(await svc!.ListEntriesAsync(p), fx.Expected, context: name);
    }

    /// <summary>
    /// 계약(설계 §1): 확장자를 아는 파일은 확장자가 먼저다 — 7z 를 '.zip' 으로 바꿔 두면 ZIP 처리기로 간다(내용 판별은 확장자로 못 고를 때만).
    /// </summary>
    [Fact]
    public void 아는_확장자는_확장자가_먼저다()
    {
        using var tmp = new TempDir("extfirst");
        string p = tmp.P("사진.zip");
        File.Copy(FixtureCatalog.Get("7z_default").FullPath, p);
        Assert.IsType<ZipArchiveService>(Locator.Resolve(p));
        Assert.IsType<ZipArchiveService>(Locator.ResolveByContent(p));
    }

    /// <summary>
    /// 계약(라우팅 표 '.gz·.bz2(안이 tar, 내용 판별) → 압축된 tar'): 이름이 .gz·.bz2·.xz 라도 안에 tar 가 들었으면 tar 안 내용을 바로 연다.
    /// 안이 tar 가 아니면(gz_single 등) 단일 파일 처리기 그대로. 끌어 놓기 분류(ResolveByExtension)는 파일을 읽지 않아 단일 파일 처리기.
    /// </summary>
    [Theory]
    [InlineData("targz_default", "묶음.gz", typeof(CompressedTarArchiveService))]
    [InlineData("tbz2_python", "묶음.bz2", typeof(CompressedTarArchiveService))]
    [InlineData("txz_two_step", "묶음.xz", typeof(CompressedTarArchiveService))]
    [InlineData("gz_single", "한개.gz", typeof(GZipArchiveService))]
    [InlineData("bz2_single_python", "한개.bz2", typeof(BZip2ArchiveService))]
    [InlineData("xz_single", "한개.xz", typeof(SevenZipFormatsArchiveService))]
    public async Task 이름이_gz_bz2_xz여도_안이_tar면_tar_안을_연다(string id, string name, Type expected)
    {
        var fx = FixtureCatalog.Get(id);
        using var tmp = new TempDir("gzname");
        string p = tmp.P(name);
        File.Copy(fx.FullPath, p);
        var svc = Locator.Resolve(p);
        Assert.IsType(expected, svc);
        Assert.IsNotType<CompressedTarArchiveService>(Locator.ResolveByExtension(p));
        var entries = await svc!.ListEntriesAsync(p);
        if (expected == typeof(CompressedTarArchiveService))
            TreeAssert.ListEquals(entries, fx.Expected, context: name);
        else
            Assert.Single(entries);
    }

    /// <summary>서명으로 모르는 내용(쓰레기)은 확장자대로 처리기를 고르고, 그 처리기가 오류로 알린다(암호 문제·'압축 아님'으로 오해하지 않게).</summary>
    [Fact]
    public async Task 모르는_내용은_확장자대로_고르고_오류로_알린다()
    {
        using var tmp = new TempDir("garb");
        string p = tmp.P("쓰레기.zip");
        File.WriteAllBytes(p, Sample("garbage"));
        var svc = Locator.Resolve(p);
        Assert.IsType<ZipArchiveService>(svc);
        var ex = await Svc.Catch(() => svc!.ListEntriesAsync(p));
        Assert.NotNull(ex);
        Assert.False(Internals.IsPasswordRelated(ex!));
        Assert.DoesNotContain("압축 파일이 아닌", ErrorMessages.ToUserMessage(ex!));
    }

    /// <summary>zstd 는 알아보지만 풀 수 없어 '미지원'(null) — 창 없이 풀기는 종료 코드 2. 서명도 확장자도 모르는 파일도 null.</summary>
    [Fact]
    public void zstd_내용과_모르는_내용은_미지원()
    {
        using var tmp = new TempDir("zst");
        string p = tmp.P("x.bin");
        File.WriteAllBytes(p, Sample("zstd"));
        Assert.Null(Locator.Resolve(p));
        Assert.Null(Locator.ResolveByContent(p));
        Assert.Null(Locator.ResolveForAutomation(p));
        string g = tmp.P("y.bin");
        File.WriteAllBytes(g, Sample("garbage"));
        Assert.Null(Locator.ResolveByContent(g));
    }

    /// <summary>
    /// 계약: 확장자만 보는 고르기는 파일 내용을 보지 않는다 — .docx(속은 ZIP)는 압축 파일로 고르지 않는다(드롭하면 '압축 대기'로).
    /// 일집 안에서 직접 열면(ResolveByContent) ZIP 으로 열려 문서 속을 볼 수 있다. 창 없이 풀기는 늘 미지원(종료 코드 2).
    /// </summary>
    [Fact]
    public void 확장자_고르기는_내용을_보지_않는다()
    {
        using var tmp = new TempDir("docx");
        string p = tmp.P("보고서.docx");
        File.Copy(FixtureCatalog.Get("zip_default").FullPath, p);
        Assert.Null(Locator.ResolveByExtension(p));
        Assert.Null(Locator.Resolve(p));
        Assert.IsType<ZipArchiveService>(Locator.ResolveByContent(p));
        Assert.Null(Locator.ResolveForAutomation(p));
    }

    /// <summary>
    /// 압축 처리기가 쓰는 오류 문구에 '압축 파일이 아닌'이 들어가면 재사용장치가 '압축 아님'으로 잘못 분류한다
    /// (재사용장치_processor.py 491행) — 일집 예외 문구에는 넣지 않는다.
    /// </summary>
    [Fact]
    public void 일집_예외_문구에_압축파일이_아닌이라는_말이_없다()
    {
        var all = new Exception[]
        {
            new CorruptArchiveException(), new CorruptArchiveException(null, "a/b.txt"),
            new PasswordRequiredException(), new PasswordRequiredException(true), new WrongPasswordException(), new WrongPasswordException(true),
            new WrongPasswordException(mayBeCorrupt: true),
            new VolumeMissingException("x.7z.002"), new VolumeIncompleteException("x.7z.003"), new UnsupportedMethodException("PPMd"),
            new EngineUnavailableException(), CorruptArchiveException.Truncated(), CorruptArchiveException.Truncated(null, "a/b.txt"),
            new ChecksumMismatchException("a/b.txt"), new UnsupportedFormatException(".zst"),
            new SourceUnreadableException(@"C:\a.txt", "다른 프로그램이 쓰고 있음"),
        };
        foreach (var ex in all)
        {
            string m = ErrorMessages.ToUserMessage(ex);
            Assert.DoesNotContain("압축 파일이 아닌", m);
            Assert.DoesNotContain("압축파일이 아닌", m);
            Assert.DoesNotContain("not an archive", m, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("not archive", m, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("unsupported archive", m, StringComparison.OrdinalIgnoreCase);
            Assert.Equal(ex.Message, m);   // 일집 예외는 문구 그대로
        }
        // 미지원 형식만 종료 코드 2(재사용장치가 '압축 아님'으로 분류 — stderr 는 App 이 영어로 따로 씀)
        Assert.Equal(2, ErrorMessages.HeadlessExitCode(new UnsupportedFormatException(".zst")));
        Assert.Equal(1, ErrorMessages.HeadlessExitCode(new CorruptArchiveException()));
    }

    /// <summary>일반 예외의 쉬운 문구.</summary>
    [Fact]
    public void 일반_예외도_쉬운_문구로_바꾼다()
    {
        Assert.Equal("취소했어요.", ErrorMessages.ToUserMessage(new OperationCanceledException()));
        Assert.Equal("디스크 공간이 모자라요.", ErrorMessages.ToUserMessage(new IOException("full", unchecked((int)0x80070070))));
        Assert.StartsWith("위험한 경로가 들어 있어", ErrorMessages.ToUserMessage(new InvalidDataException(ErrorMessages.UnsafePathPrefix + "../x")));
        Assert.Contains(@"C:\잠긴 폴더", ErrorMessages.ToUserMessage(new UnauthorizedAccessException(@"Access to the path 'C:\잠긴 폴더' is denied.")));
        Assert.Equal("이미 한국어 문구", ErrorMessages.ToUserMessage(new InvalidDataException("이미 한국어 문구")));
        Assert.StartsWith("알 수 없는 오류가 났어요", ErrorMessages.ToUserMessage(new InvalidOperationException("boom")));
    }

    /// <summary>풀린 파일 옮기기(임시 폴더 → 대상)도 같은 이름이 있으면 '이름 (1)'로 비켜 간다.</summary>
    [Fact]
    public void 옮기기도_같은_이름은_비켜간다()
    {
        using var tmp = new TempDir("move");
        string dest = tmp.Dir("dest");
        File.WriteAllText(Path.Combine(dest, "a.txt"), "원래");
        string s1 = tmp.P("s1.txt");
        File.WriteAllText(s1, "새것");
        string moved = OutputFiles.MoveNew(s1, Path.Combine(dest, "a.txt"));
        Assert.Equal(Path.Combine(dest, "a (1).txt"), moved);
        Assert.Equal("원래", File.ReadAllText(Path.Combine(dest, "a.txt"), Encoding.UTF8));
        Assert.Equal("새것", File.ReadAllText(moved, Encoding.UTF8));
        Assert.False(File.Exists(s1));
    }

    /// <summary>
    /// 계약(W1-B 검증 지적): 풀린 파일을 옮길 때 다른 프로그램(DLP·백신)이 그 파일을 잠깐 잡고 있으면(공유 위반) 조금 기다렸다 다시 해서 옮긴다.
    /// 예전엔 '이미 있음'만 다시 해 보고 공유 위반은 바로 실패했다.
    /// </summary>
    [Fact]
    public async Task 옮기기는_잠깐_잠긴_파일을_기다렸다_옮긴다()
    {
        using var tmp = new TempDir("movelock");
        string dest = tmp.Dir("dest");
        string s1 = tmp.P("s1.txt");
        File.WriteAllText(s1, "검사 중");
        var holder = new FileStream(s1, FileMode.Open, FileAccess.Read, FileShare.Read);   // 지우기·옮기기 허용 안 함 = 백신이 여는 것처럼
        var release = Task.Run(async () =>
        {
            await Task.Delay(250);
            holder.Dispose();
        });
        string moved = OutputFiles.MoveNew(s1, Path.Combine(dest, "a.txt"));
        await release;
        Assert.Equal(Path.Combine(dest, "a.txt"), moved);
        Assert.Equal("검사 중", File.ReadAllText(moved, Encoding.UTF8));
        Assert.False(File.Exists(s1));
    }

    /// <summary>계약: 계속 잠겨 있으면 몇 번(최대 1.5초 남짓) 다시 해 본 뒤 그 오류를 그대로 알리고, 원본(임시 폴더의 파일)은 남는다.</summary>
    [Fact]
    public void 옮기기는_계속_잠긴_파일이면_몇_번_뒤_포기한다()
    {
        using var tmp = new TempDir("movelock2");
        string dest = tmp.Dir("dest");
        string s1 = tmp.P("s1.txt");
        File.WriteAllText(s1, "계속 잠김");
        var sw = System.Diagnostics.Stopwatch.StartNew();
        Exception? ex;
        using (new FileStream(s1, FileMode.Open, FileAccess.Read, FileShare.Read))
            ex = Record.Exception(() => OutputFiles.MoveNew(s1, Path.Combine(dest, "a.txt")));
        sw.Stop();
        Assert.IsAssignableFrom<IOException>(ex);
        Assert.Contains("다른 프로그램이 파일을 쓰고 있어요", ErrorMessages.ToUserMessage(ex!));
        Assert.True(sw.ElapsedMilliseconds >= OutputFiles.MoveLockDelayMs, "다시 해 보지 않고 바로 포기함");
        Assert.True(File.Exists(s1));
        Assert.False(File.Exists(Path.Combine(dest, "a.txt")));
    }

    /// <summary>
    /// 계약(W1-B 검증 지적): 경로에 한글이 든 영어 .NET 문구를 그대로 보이지 않는다(회사 PC 경로는 거의 한글).
    /// 따옴표 안(경로)의 한글은 '서비스가 쓴 한국어 문구'로 치지 않고, 흔한 입출력 오류는 쉬운 한국어로 바꾼다.
    /// </summary>
    [Fact]
    public void 한글_경로가_든_영어_문구는_한국어로_바꾼다()
    {
        string path = @"C:\사용자\바탕 화면\한글폴더";
        var exists = new IOException($"Cannot create '{path}' because a file or directory with the same name already exists.", unchecked((int)0x800700B7));
        Assert.Equal($"같은 이름의 파일이나 폴더가 이미 있어 만들 수 없어요: {path}", ErrorMessages.ToUserMessage(exists));

        var locked = new IOException($"The process cannot access the file '{path}\\견적서.xlsx' because it is being used by another process.", unchecked((int)0x80070020));
        Assert.Equal($"다른 프로그램이 파일을 쓰고 있어요. 그 프로그램을 닫고 다시 해 보세요: {path}\\견적서.xlsx", ErrorMessages.ToUserMessage(locked));

        Assert.Equal($"파일이나 폴더를 찾을 수 없어요: {path}", ErrorMessages.ToUserMessage(new DirectoryNotFoundException($"Could not find a part of the path '{path}'.")));
        Assert.Equal("이 폴더에 쓸 권한이 없어요.", ErrorMessages.ToUserMessage(new UnauthorizedAccessException("Access is denied.")));

        // 알 수 없는 영어 문구는 '알 수 없는 오류'로 시작한다(한글 경로가 들어 있어도)
        Assert.StartsWith("알 수 없는 오류가 났어요: ", ErrorMessages.ToUserMessage(new IOException($"Something odd at '{path}'.")));
        // 서비스가 쓴 한국어 문구(따옴표 안 이름이 있어도)는 그대로
        Assert.Equal("'가.txt' 를 옮기지 못했어요", ErrorMessages.ToUserMessage(new IOException("'가.txt' 를 옮기지 못했어요")));
        Assert.Equal("고유한 출력 경로를 만들 수 없습니다: C:\\가\\나.txt", ErrorMessages.ToUserMessage(new IOException("고유한 출력 경로를 만들 수 없습니다: C:\\가\\나.txt")));
    }

    /// <summary>
    /// 계약(W1-B 검증 지적): 탐색기 우클릭 메뉴(패키지 매니페스트)에 일집이 풀 수 있는 확장자가 모두 있다
    /// ('.tar.gz'·'.7z.001' 처럼 점이 둘인 것은 끝 확장자 '.gz'·'.001' 로). 우클릭 때는 확장자로만 고르고 파일을 읽지 않는다.
    /// </summary>
    [Fact]
    public void 우클릭_메뉴에_풀_수_있는_확장자가_모두_있다()
    {
        string manifest = File.ReadAllText(Path.Combine(RepoRoot(), "packaging", "Package.appxmanifest"));
        var types = System.Text.RegularExpressions.Regex.Matches(manifest, "ItemType Type=\"([^\"]+)\"").Select(m => m.Groups[1].Value)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var ext in Locator.AllSupportedExtensions)
        {
            string last = ext[ext.LastIndexOf('.')..];
            Assert.True(types.Contains(last), $"우클릭 메뉴에 {last} 가 없음({ext} 를 풀 수 있는데)");
        }
    }

    /// <summary>
    /// 계약(W1-B 2차 검증 지적): 자동화(창 없이 풀기)용 고르기는 속이 ZIP 인 문서(.xlsx 등)를 압축으로 보지 않는다(미지원 = 종료 코드 2 계약).
    /// 화면의 열기는 그대로 문서 속을 볼 수 있다(엑셀 속 그림 꺼내기 등). 이름만 바꾼 진짜 압축은 자동화에서도 내용대로.
    /// </summary>
    [Fact]
    public void 자동화용_고르기는_ZIP기반_문서를_압축으로_보지_않는다()
    {
        using var tmp = new TempDir("locdoc");
        string xlsx = tmp.P("보고서.xlsx");
        RawZip.Write(xlsx, RawZip.Utf8("[Content_Types].xml", "<Types/>"));
        Assert.Equal(ArchiveKind.Zip, ArchiveSniffer.Detect(xlsx));
        Assert.Null(Locator.ResolveForAutomation(xlsx));
        Assert.IsType<ZipArchiveService>(Locator.ResolveByContent(xlsx));
        foreach (var ext in new[] { ".docx", ".pptx", ".hwpx", ".odt", ".ods", ".odp", ".jar", ".apk", ".epub" })
        {
            string doc = tmp.P("문서" + ext);
            File.Copy(xlsx, doc);
            Assert.Null(Locator.ResolveForAutomation(doc));
        }
        string dat = tmp.P("자료.dat");
        RawZip.Write(dat, RawZip.Utf8("a.txt", "a"));
        Assert.IsType<ZipArchiveService>(Locator.ResolveForAutomation(dat));
    }

    /// <summary>
    /// 계약(W2-01): 7z 만들기는 W2-02 몫이라 저장 창에 7Z 가 없다 — 7z.dll 을 쓸 수 있든 없든(못 쓰는 PC 에서 옵션 창까지 지난 뒤에야 오류가 나지 않게).
    /// </summary>
    [Fact]
    public void 저장_형식에_7Z가_없다()
    {
        Assert.DoesNotContain("*.7z", Locator.BuildSaveFilter());
        using (SevenZipLibrary.SimulateUnavailableForTests())
        {
            string filter = Locator.BuildSaveFilter();
            Assert.DoesNotContain("*.7z", filter);
            Assert.Contains("*.zip", filter);
            Assert.Equal(0, filter.Split('|').Length % 2);   // 이름|패턴 짝
        }
    }

    /// <summary>'여기에 풀기' 폴더 이름은 압축 확장자(가장 긴 것)를 뗀 이름이다.</summary>
    [Theory]
    [InlineData(@"C:\a\견적.7z.001", "견적")]
    [InlineData(@"C:\a\자료.tar.gz", "자료")]
    [InlineData(@"C:\a\자료.tgz", "자료")]
    [InlineData(@"C:\a\v1.2 보고서.zip", "v1.2 보고서")]
    [InlineData(@"C:\a\설치.exe", "설치")]
    [InlineData(@"C:\a\디스크.iso", "디스크")]
    public void 여기에_풀기_폴더_이름은_압축_확장자를_뗀다(string path, string expected) =>
        Assert.Equal(expected, Locator.NameWithoutArchiveExtension(path));

    private static string RepoRoot([System.Runtime.CompilerServices.CallerFilePath] string here = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(here)!, "..", ".."));
}
