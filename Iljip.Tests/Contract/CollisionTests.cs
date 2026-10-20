using Iljip.Tests.TestSupport;

namespace Iljip.Tests.Contract;

/// <summary>
/// 해제 대상에 같은 이름이 이미 있을 때: 덮어쓰지 않고 '이름 (1).확장자'로 비켜 쓴다(원본 비파괴).
/// 엔진을 7z.dll 등으로 바꾸면 기본값이 '덮어쓰기'인 경우가 많아 꼭 지켜야 할 계약.
/// </summary>
public class CollisionTests
{
    [Fact]
    public async Task 같은_곳에_두번_풀면_기존_파일을_덮지_않고_이름1로_비켜쓴다()
    {
        var fx = FixtureCatalog.Get("zip_default");
        using var tmp = new TempDir("col");
        string dest = tmp.P("out");
        var svc = Svc.For(fx.FullPath);
        await svc.ExtractAsync(fx.FullPath, dest);

        // 사용자가 풀린 파일을 고쳐 둔 상황
        string mine = Path.Combine(dest, "일집_샘플", "readme.txt");
        File.WriteAllText(mine, "사용자가 고친 내용");
        string mineSha = TreeAssert.Sha256(mine);

        await svc.ExtractAsync(fx.FullPath, dest);

        Assert.Equal(mineSha, TreeAssert.Sha256(mine));   // 덮어쓰지 않음
        string renamed = Path.Combine(dest, "일집_샘플", "readme (1).txt");
        Assert.True(File.Exists(renamed));
        Assert.Equal(fx.Expected.Single(e => e.Path == "일집_샘플/readme.txt").Sha256, TreeAssert.Sha256(renamed));
        Assert.True(File.Exists(Path.Combine(dest, "일집_샘플", "하위폴더", "2단계", "3단계", "깊은 파일 [끝] (1).txt")));
        Assert.True(File.Exists(Path.Combine(dest, "일집_샘플", "빈파일 (1).txt")));
    }

    [Fact]
    public async Task 세번째는_이름2()
    {
        var fx = FixtureCatalog.Get("tar_default");
        using var tmp = new TempDir("col3");
        string dest = tmp.P("out");
        var svc = Svc.For(fx.FullPath);
        for (int i = 0; i < 3; i++)
            await svc.ExtractAsync(fx.FullPath, dest);
        Assert.True(File.Exists(Path.Combine(dest, "일집_샘플", "견적서_2026 (2).txt")));
        Assert.False(File.Exists(Path.Combine(dest, "일집_샘플", "견적서_2026 (3).txt")));
    }

    /// <summary>파일 이름과 같은 '폴더'가 이미 있어도 멈추지 않고 비켜 쓴다.</summary>
    [Fact]
    public async Task 같은_이름의_폴더가_있어도_비켜쓴다()
    {
        var fx = FixtureCatalog.Get("zip_default");
        using var tmp = new TempDir("cold");
        string dest = tmp.P("out");
        Directory.CreateDirectory(Path.Combine(dest, "일집_샘플", "견적서_2026.txt"));
        await Svc.For(fx.FullPath).ExtractAsync(fx.FullPath, dest);
        Assert.True(Directory.Exists(Path.Combine(dest, "일집_샘플", "견적서_2026.txt")));
        Assert.True(File.Exists(Path.Combine(dest, "일집_샘플", "견적서_2026 (1).txt")));
    }

    /// <summary>
    /// 같은 압축 파일을 같은 폴더에 '동시에' 여러 번 풀어도 모두 성공하고, 서로 다른 이름(원래 이름, 이름 (1) …)으로 하나씩 생긴다.
    /// 예전엔 '있는지 확인 → 만들기' 사이에 다른 해제가 끼어들어 진 쪽이 '다른 프로세스가 사용 중' 오류로 실패했다
    /// (6개 동시에 2개만 성공 — W1-A 1차 검증). 지금은 만들기를 '없을 때만 만들기'로 한 번에 한다(OutputFiles).
    /// SharpCompress 계열(gz·zip)과 EGG 서비스가 각자 해제 코드를 가지므로 둘 다 본다.
    /// </summary>
    [Theory]
    [InlineData("gz")]
    [InlineData("zip")]
    [InlineData("egg")]
    public async Task 같은_폴더에_동시에_여러번_풀어도_모두_다른_이름으로_풀린다(string kind)
    {
        const int n = 8;
        using var tmp = new TempDir("colpar");
        var payload = new byte[300_000];
        new Random(8).NextBytes(payload);
        string arc = tmp.P("src." + kind);
        switch (kind)
        {
            case "gz": RawGz.Write(tmp.P("src.txt.gz"), null, payload); arc = tmp.P("src.txt.gz"); break;
            case "zip": RawZip.Write(arc, new RawZip.Item(System.Text.Encoding.UTF8.GetBytes("src.txt"), payload)); break;
            default: RawEgg.Write(arc, new[] { new RawEgg.Item("src.txt", payload) }); break;
        }
        string dest = tmp.Dir("out");
        var svc = Svc.For(arc);
        await Task.WhenAll(Enumerable.Range(0, n).Select(_ => Task.Run(() => svc.ExtractAsync(arc, dest))));

        var want = new[] { "src.txt" }.Concat(Enumerable.Range(1, n - 1).Select(i => $"src ({i}).txt")).OrderBy(x => x, StringComparer.Ordinal);
        Assert.Equal(want, TreeAssert.Files(dest));
        string sha = TreeAssert.Sha256(payload);
        Assert.All(TreeAssert.Files(dest), f => Assert.Equal(sha, TreeAssert.Sha256(Path.Combine(dest, f))));
    }

    /// <summary>bz2 단일 스트림 경로도 같은 규칙.</summary>
    [Fact]
    public async Task bz2도_덮어쓰지_않는다()
    {
        var fx = FixtureCatalog.Get("bz2_single_python");
        using var tmp = new TempDir("colb");
        string dest = tmp.P("out");
        var svc = Svc.For(fx.FullPath);
        await svc.ExtractAsync(fx.FullPath, dest);
        await svc.ExtractAsync(fx.FullPath, dest);
        Assert.Equal(new[] { "견적서_2026 (1).txt", "견적서_2026.txt" }, TreeAssert.Files(dest));
    }

    // ───────────── W2-05(I1-5): 같은 이름일 때 덮어쓰기·건너뛰기·이름 바꾸기·취소 + '남은 파일도 똑같이' ─────────────
    // 화면이 해제를 ExtractionScope 로 감쌌을 때만 묻는다(위 기존 시험들은 범위가 없어 예전 규칙 '이름 (1)' 그대로 — 창 없이 풀기 계약).
    // 형식별로 두 길을 다 본다: 직접 쓰는 길(zip·tar — SharpCompress, egg — EggDotNet), 7z.dll 임시 폴더에서 옮기는 길(7z·rar).

    public static TheoryData<string> Kinds => new() { "zip", "7z", "rar", "tar", "egg" };

    private static readonly byte[] NewQuote = System.Text.Encoding.UTF8.GetBytes("새 견적서 — 압축 안 내용 0123456789\r\n");
    private static readonly byte[] NewReadme = System.Text.Encoding.UTF8.GetBytes("new readme from archive\r\n");
    private static readonly byte[] Big = Enumerable.Range(0, 120_000).Select(i => (byte)(i * 13 + (i >> 7))).ToArray();
    private static readonly byte[] Mine = System.Text.Encoding.UTF8.GetBytes("사용자가 고친 내용(지워지면 안 됨)");

    private static (string Name, byte[] Data)[] ThreeFiles() => new[]
    {
        ("자료/견적서.txt", NewQuote),
        ("자료/하위/난수.bin", Big),
        ("readme.txt", NewReadme),
    };

    /// <summary>
    /// <paramref name="kind"/> 압축을 만든다. zip·7z·tar 는 일집 서비스로(7z 는 7z.dll), rar 는 합성 RAR5, egg 는 합성 EGG.
    /// </summary>
    internal static async Task<string> MakeArchive(string kind, string dir, IReadOnlyList<(string Name, byte[] Data)> files, string? password = null)
    {
        Directory.CreateDirectory(dir);
        string arc = Path.Combine(dir, "묶음." + kind);
        switch (kind)
        {
            case "rar":
                RawRar5.Write(arc, files.Select(f => new RawRar5.Item(f.Name, f.Data)), password);
                break;
            case "egg":
                RawEgg.Write(arc, files.Select(f => new RawEgg.Item(f.Name, f.Data, Password: password)));
                break;
            default:
                {
                    string src = Path.Combine(dir, "원본_" + kind);
                    foreach (var (name, data) in files)
                    {
                        string p = Path.Combine(src, name.Replace('/', Path.DirectorySeparatorChar));
                        Directory.CreateDirectory(Path.GetDirectoryName(p)!);
                        File.WriteAllBytes(p, data);
                    }
                    var tops = Directory.EnumerateFileSystemEntries(src).ToList();
                    Iljip.Services.IArchiveService svc = kind switch
                    {
                        "zip" => new Iljip.Services.ZipArchiveService(),
                        "7z" => new Iljip.Services.SevenZipArchiveService(),
                        _ => new Iljip.Services.TarArchiveService(),
                    };
                    await svc.CompressAsync(tops, arc, password is null ? null : new Iljip.Models.CompressionOptions { Password = password });
                    break;
                }
        }
        if (kind is "7z" or "rar")
            Assert.True(Iljip.Services.SevenZipLibrary.IsAvailable, "7z.dll 임시 폴더 길을 시험해야 함");
        return arc;
    }

    private static void Put(string dest, string rel, byte[] data)
    {
        string p = Path.Combine(dest, rel.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(p)!);
        File.WriteAllBytes(p, data);
    }

    private static string Sha(byte[] d) => TreeAssert.Sha256(d);

    /// <summary>대상 폴더에 덮어쓰기 임시 파일(.~이름….tmp)·풀기 임시 폴더(.일집-임시-…)가 남지 않았다.</summary>
    internal static void NoLeftovers(string dest, string context)
    {
        var left = Directory.EnumerateFileSystemEntries(dest, "*", SearchOption.AllDirectories)
            .Select(p => Path.GetFileName(p))
            .Where(n => n.StartsWith(".~", StringComparison.Ordinal) || n.StartsWith(Iljip.Services.StagingFolder.Prefix, StringComparison.Ordinal))
            .ToList();
        Assert.True(left.Count == 0, $"{context}: 임시 파일·폴더가 남음: {string.Join(", ", left)}");
    }

    private static Task ExtractIn(Iljip.Services.ICollisionResolver resolver, string arc, string dest, Action<Iljip.Services.ExtractionScope>? check = null)
    {
        return Run();

        async Task Run()
        {
            using var scope = Iljip.Services.ExtractionScope.Begin(resolver);
            await Svc.For(arc).ExtractAsync(arc, dest);
            check?.Invoke(scope);
        }
    }

    /// <summary>덮어쓰기: 같은 이름 두 개는 새 내용으로 바뀌고, 없던 파일은 새로 생기고, '(1)' 사본·임시 파일은 없다.</summary>
    [Theory]
    [MemberData(nameof(Kinds))]
    public async Task 같은_이름_덮어쓰기는_새_내용으로_바꾸고_임시파일을_남기지_않는다(string kind)
    {
        using var tmp = new TempDir("cow");
        string arc = await MakeArchive(kind, tmp.P("in"), ThreeFiles());
        string dest = tmp.Dir("out");
        Put(dest, "자료/견적서.txt", Mine);
        Put(dest, "readme.txt", Mine);

        int overwritten = -1;
        await ExtractIn(new Iljip.Services.FixedResolver(Iljip.Services.CollisionChoice.Overwrite), arc, dest, s => overwritten = s.Overwritten);

        Assert.Equal(2, overwritten);
        TreeAssert.ExtractedTreeEquals(dest, new ExpectedEntry[]
        {
            new("자료", 0, null, true),
            new("자료/견적서.txt", NewQuote.Length, Sha(NewQuote), false),
            new("자료/하위", 0, null, true),
            new("자료/하위/난수.bin", Big.Length, Sha(Big), false),
            new("readme.txt", NewReadme.Length, Sha(NewReadme), false),
        }, context: kind);
        // 덮어쓴 파일이 숨김(임시 파일 속성)으로 남지 않음
        Assert.False(File.GetAttributes(Path.Combine(dest, "readme.txt")).HasFlag(FileAttributes.Hidden), "덮어쓴 파일이 숨김으로 남음");
        NoLeftovers(dest, kind);
    }

    /// <summary>건너뛰기: 원래 있던 두 파일은 바이트 그대로, 없던 파일만 생긴다. 건너뛴 수 2.</summary>
    [Theory]
    [MemberData(nameof(Kinds))]
    public async Task 같은_이름_건너뛰기는_원래_내용을_그대로_둔다(string kind)
    {
        using var tmp = new TempDir("cos");
        string arc = await MakeArchive(kind, tmp.P("in"), ThreeFiles());
        string dest = tmp.Dir("out");
        Put(dest, "자료/견적서.txt", Mine);
        Put(dest, "readme.txt", Mine);

        int skipped = -1;
        await ExtractIn(new Iljip.Services.FixedResolver(Iljip.Services.CollisionChoice.Skip), arc, dest, s => skipped = s.Skipped);

        Assert.Equal(2, skipped);
        TreeAssert.ExtractedTreeEquals(dest, new ExpectedEntry[]
        {
            new("자료", 0, null, true),
            new("자료/견적서.txt", Mine.Length, Sha(Mine), false),
            new("자료/하위", 0, null, true),
            new("자료/하위/난수.bin", Big.Length, Sha(Big), false),
            new("readme.txt", Mine.Length, Sha(Mine), false),
        }, context: kind);
        NoLeftovers(dest, kind);
    }

    /// <summary>이름 바꾸기: 원래 파일은 그대로, 새 내용은 '이름 (1).ext'(예전 동작과 같음).</summary>
    [Theory]
    [MemberData(nameof(Kinds))]
    public async Task 같은_이름_이름바꾸기는_이름1로_비켜쓴다(string kind)
    {
        using var tmp = new TempDir("cor");
        string arc = await MakeArchive(kind, tmp.P("in"), ThreeFiles());
        string dest = tmp.Dir("out");
        Put(dest, "자료/견적서.txt", Mine);
        Put(dest, "readme.txt", Mine);

        await ExtractIn(new Iljip.Services.FixedResolver(Iljip.Services.CollisionChoice.Rename), arc, dest);

        TreeAssert.ExtractedTreeEquals(dest, new ExpectedEntry[]
        {
            new("자료", 0, null, true),
            new("자료/견적서.txt", Mine.Length, Sha(Mine), false),
            new("자료/견적서 (1).txt", NewQuote.Length, Sha(NewQuote), false),
            new("자료/하위", 0, null, true),
            new("자료/하위/난수.bin", Big.Length, Sha(Big), false),
            new("readme.txt", Mine.Length, Sha(Mine), false),
            new("readme (1).txt", NewReadme.Length, Sha(NewReadme), false),
        }, context: kind);
    }

    /// <summary>
    /// 취소: 같은 이름 창에서 [취소]하면 취소(OperationCanceledException)로 끝나고, 이번에 만든 파일·폴더는 하나도 남지 않으며
    /// 원래 있던 파일은 바이트 그대로. 질문 창에는 있는 파일·새 파일의 크기가 보인다.
    /// </summary>
    [Theory]
    [MemberData(nameof(Kinds))]
    public async Task 같은_이름_취소는_이번에_만든_파일을_모두_지우고_원래_파일은_그대로(string kind)
    {
        using var tmp = new TempDir("coc");
        string arc = await MakeArchive(kind, tmp.P("in"), ThreeFiles());
        string dest = tmp.Dir("out");
        Put(dest, "readme.txt", Mine);   // 이것만 원래 있음 — 다른 파일(자료/…)은 이번에 만들어졌다가 지워져야 함
        var before = TreeAssert.Snapshot(dest);
        var ui = new FakeUserInteraction();
        ui.CollisionAnswers.Enqueue(new(Iljip.Services.CollisionChoice.Cancel, false));

        var ex = await Svc.Catch(() => ExtractIn(new Iljip.Services.AskingResolver(ui), arc, dest));

        Assert.IsType<Iljip.Services.CollisionCancelledException>(ex);
        Assert.IsAssignableFrom<OperationCanceledException>(ex);
        var q = Assert.Single(ui.CollisionPrompts);
        Assert.Equal(Path.Combine(dest, "readme.txt"), q.TargetPath);
        Assert.Equal(Mine.Length, q.ExistingSize);
        Assert.Equal(NewReadme.Length, q.IncomingSize);
        Assert.Equal(before, TreeAssert.Snapshot(dest));   // 원래 있던 파일만, 바이트 그대로(이번에 만든 것 0개)
    }

    /// <summary>같은 이름 10개에 ‘남은 파일도 똑같이’(덮어쓰기)를 고르면 질문은 1번, 10개 모두 새 내용. 첫 질문의 '남은 파일'은 9.</summary>
    [Theory]
    [MemberData(nameof(Kinds))]
    public async Task 남은_파일도_똑같이를_고르면_한번만_묻는다(string kind)
    {
        using var tmp = new TempDir("coa");
        var files = Enumerable.Range(1, 10)
            .Select(i => ($"열개/파일{i:00}.txt", System.Text.Encoding.UTF8.GetBytes($"새 내용 {i} — {kind}")))
            .ToArray();
        string arc = await MakeArchive(kind, tmp.P("in"), files);
        string dest = tmp.Dir("out");
        foreach (var (name, _) in files) Put(dest, name, Mine);
        var ui = new FakeUserInteraction();
        ui.CollisionAnswers.Enqueue(new(Iljip.Services.CollisionChoice.Overwrite, ApplyToAll: true));

        await ExtractIn(new Iljip.Services.AskingResolver(ui), arc, dest);

        var q = Assert.Single(ui.CollisionPrompts);
        Assert.Equal(9, q.Remaining);
        foreach (var (name, data) in files)
            Assert.Equal(Sha(data), TreeAssert.Sha256(Path.Combine(dest, name.Replace('/', Path.DirectorySeparatorChar))));
        Assert.Equal(10, TreeAssert.Files(dest).Count);
        NoLeftovers(dest, kind);
    }

    /// <summary>
    /// 덮어쓰기 원자성: 덮어쓸 파일을 다른 프로그램이 잡고 있으면(FileShare.None — 엑셀·DLP 흉내) 원래 파일은 바이트 그대로이며
    /// 덮어쓰기 임시 파일(.~…tmp)·풀기 임시 폴더가 남지 않는다.
    /// I5-B(최종 감사 IA-OPENFILE-OVERWRITE): 예전엔 여기서 풀기 전체가 오류('덮어쓰지 못했어요 … 쓸 권한이 없거나' + 관리자 권한 권유)로 멈춰 뒤 파일을 풀지 않았다 —
    /// 이제 그 파일만 건너뛰고(건너뛴 수 1, 알릴 것 '다른 프로그램이 쓰는 중이라 못 바꾼 파일 1개' + 이름) 나머지는 다 푼다(형식마다 두 길 모두).
    /// </summary>
    [Theory]
    [MemberData(nameof(Kinds))]
    public async Task 덮어쓸_파일이_잠겨_있으면_그_파일만_건너뛰고_원본은_그대로(string kind)
    {
        using var tmp = new TempDir("col");
        string arc = await MakeArchive(kind, tmp.P("in"), ThreeFiles());
        string dest = tmp.Dir("out");
        Put(dest, "readme.txt", Mine);
        string locked = Path.Combine(dest, "readme.txt");

        Exception? ex;
        Iljip.Services.ExtractionScope? seen = null;
        using (new FileStream(locked, FileMode.Open, FileAccess.Read, FileShare.None))
            ex = await Svc.Catch(() => ExtractIn(new Iljip.Services.FixedResolver(Iljip.Services.CollisionChoice.Overwrite), arc, dest, s => seen = s));

        Assert.True(ex is null, $"{kind}: 잠긴 파일 하나 때문에 풀기가 멈춤 — {ex?.Message}");
        Assert.Equal(Sha(Mine), TreeAssert.Sha256(locked));
        Assert.Equal(Sha(NewQuote), TreeAssert.Sha256(Path.Combine(dest, "자료", "견적서.txt")));
        Assert.Equal(Sha(Big), TreeAssert.Sha256(Path.Combine(dest, "자료", "하위", "난수.bin")));
        Assert.Equal(1, seen!.Skipped);
        string? message = seen.Notes.ToMessage();
        Assert.NotNull(message);
        Assert.Contains("다른 프로그램이 쓰는 중이라 못 바꾼 파일 1개", message);
        Assert.Contains("readme.txt", message);
        NoLeftovers(dest, kind);
    }

    /// <summary>
    /// 압축 안에 같은 이름이 두 번 있으면(이번 해제가 만든 파일과 겹침) 묻지 않고 예전처럼 '이름 (1)' — 새로 푼 내용끼리 덮어쓰지 않는다.
    /// 직접 쓰는 길(.zip)과 7z.dll 임시 폴더 길(.zipx) 둘 다.
    /// </summary>
    [Theory]
    [InlineData(".zip")]
    [InlineData(".zipx")]
    public async Task 압축_안의_같은_이름_두개는_묻지_않고_비켜쓴다(string ext)
    {
        using var tmp = new TempDir("codup");
        string arc = tmp.P("중복" + ext);
        RawZip.Write(arc, RawZip.Utf8("a.txt", "first"), RawZip.Utf8("a.txt", "second"));
        string dest = tmp.Dir("out");
        var ui = new FakeUserInteraction();
        ui.CollisionAnswers.Enqueue(new(Iljip.Services.CollisionChoice.Overwrite, true));

        await ExtractIn(new Iljip.Services.AskingResolver(ui), arc, dest);

        Assert.Empty(ui.CollisionPrompts);
        Assert.Equal(new[] { "a (1).txt", "a.txt" }, TreeAssert.Files(dest));
        var contents = TreeAssert.Files(dest).Select(f => File.ReadAllText(Path.Combine(dest, f))).OrderBy(x => x, StringComparer.Ordinal).ToArray();
        Assert.Equal(new[] { "first", "second" }, contents);
    }

    /// <summary>raw bz2(단일 스트림)도 같은 이름 처리를 따른다 — 덮어쓰기·건너뛰기.</summary>
    [Theory]
    [InlineData(Iljip.Services.CollisionChoice.Overwrite)]
    [InlineData(Iljip.Services.CollisionChoice.Skip)]
    public async Task bz2_단일_스트림도_같은_이름_처리를_따른다(Iljip.Services.CollisionChoice choice)
    {
        var fx = FixtureCatalog.Get("bz2_single_python");
        using var tmp = new TempDir("cobz");
        string dest = tmp.Dir("out");
        Put(dest, "견적서_2026.txt", Mine);

        await ExtractIn(new Iljip.Services.FixedResolver(choice), fx.FullPath, dest);

        Assert.Equal(new[] { "견적서_2026.txt" }, TreeAssert.Files(dest));
        string expected = choice == Iljip.Services.CollisionChoice.Overwrite ? fx.Expected.Single(e => !e.IsDir).Sha256! : Sha(Mine);
        Assert.Equal(expected, TreeAssert.Sha256(Path.Combine(dest, "견적서_2026.txt")));
        NoLeftovers(dest, "bz2");
    }
}
