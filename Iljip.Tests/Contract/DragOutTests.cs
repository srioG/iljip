using System.Buffers.Binary;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Text;
using Iljip.Models;
using Iljip.Services;
using Iljip.Tests.TestSupport;
using Iljip.ViewModels;

namespace Iljip.Tests.Contract;

/// <summary>
/// [W2-04 — I1-4] 목록에서 탐색기로 끌어 꺼내기의 논리를 창 없이 시험한다(실제 탐색기와 주고받는 것은 MANUAL_CHECKLIST 20절).
/// <list type="bullet">
/// <item><see cref="DragPayloadBuilder"/>: 고른 것 → 끌기 서술자(폴더 재귀·한글 이름·이름 겹침·위험한 이름·경로 길이).</item>
/// <item><see cref="LazyEntryExtractor"/>: 부르기 전에는 아무것도 꺼내지 않고, 부른 항목만, 한 번만 꺼낸다.</item>
/// <item><see cref="VirtualFileDataObject"/>: COM IDataObject 메서드를 직접 불러 — 형식 목록·FILEGROUPDESCRIPTORW 바이트 구조·FileContents IStream·비동기 모드.</item>
/// <item>화면: 끌기 전에만 암호를 묻는다(끄는 도중에는 묻지 않음).</item>
/// </list>
/// </summary>
public class DragOutTests
{
    private static readonly TimeSpan Limit = TimeSpan.FromSeconds(60);
    private static readonly FixtureInfo Zip = FixtureCatalog.Get("zip_default");

    private static async Task<List<ArchiveEntry>> ListAsync(string path, string? pw = null) =>
        (await Svc.For(path).ListEntriesAsync(path, pw)).ToList();

    private static ArchiveEntry Folder(string path) => new() { Path = path, IsDirectory = true, Size = -1, CompressedSize = -1 };

    private static ArchiveEntry File_(List<ArchiveEntry> all, string path) =>
        all.Single(e => !e.IsDirectory && TreeAssert.NormalizeEntryPath(e.Path) == path);

    private static long SizeOf(string path) => Zip.Expected.Single(e => e.Path == path).Size;

    // ───────────────────────────── DragPayloadBuilder ─────────────────────────────

    [Fact]
    public async Task 폴더는_안쪽까지_펼치고_한글_이름_그대로_서술자를_만든다()
    {
        var all = await ListAsync(Zip.FullPath);
        var payload = DragPayloadBuilder.Build(new[] { Folder("일집_샘플/하위폴더"), File_(all, "일집_샘플/readme.txt") }, all);

        Assert.Empty(payload.TooLong);
        Assert.Equal(new[]
        {
            ("하위폴더", "일집_샘플/하위폴더", true, 0L),
            ("하위폴더\\2단계", "일집_샘플/하위폴더/2단계", true, 0L),
            ("하위폴더\\2단계\\3단계", "일집_샘플/하위폴더/2단계/3단계", true, 0L),
            ("하위폴더\\보고서 최종.txt", "일집_샘플/하위폴더/보고서 최종.txt", false, SizeOf("일집_샘플/하위폴더/보고서 최종.txt")),
            ("하위폴더\\2단계\\3단계\\깊은 파일 [끝].txt", "일집_샘플/하위폴더/2단계/3단계/깊은 파일 [끝].txt", false, SizeOf("일집_샘플/하위폴더/2단계/3단계/깊은 파일 [끝].txt")),
            ("readme.txt", "일집_샘플/readme.txt", false, SizeOf("일집_샘플/readme.txt")),
        }, payload.Items.Select(i => (i.RelativePath, i.EntryKey, i.IsDirectory, i.Size)));
        Assert.All(payload.Items.Where(i => !i.IsDirectory), i => Assert.NotNull(i.LastModified));
    }

    [Fact]
    public void 폴더_항목이_없는_압축도_파일_경로에서_폴더를_만든다()
    {
        var all = new List<ArchiveEntry>
        {
            new() { Path = "상자/가/1.txt", Size = 1 },
            new() { Path = "상자/가/나/2.txt", Size = 2 },
        };
        var payload = DragPayloadBuilder.Build(new[] { Folder("상자") }, all);
        Assert.Equal(new[] { "상자", "상자\\가", "상자\\가\\나", "상자\\가\\1.txt", "상자\\가\\나\\2.txt" }, payload.Items.Select(i => i.RelativePath));
    }

    [Fact]
    public void 이름이_겹치면_비켜주고_위험한_이름은_다듬는다()
    {
        var all = new List<ArchiveEntry>
        {
            new() { Path = "a/보고서.txt", Size = 1 },
            new() { Path = "b/보고서.TXT", Size = 2 },
            new() { Path = "x/../../밖으로.txt", Size = 3 },
            new() { Path = "x/콜론:이름?.txt", Size = 4 },
            new() { Path = "x/점끝.", Size = 5 },
        };
        var payload = DragPayloadBuilder.Build(new[] { all[0], all[1], Folder("x") }, all);
        var rel = payload.Items.Select(i => i.RelativePath).ToList();
        Assert.Equal("보고서.txt", rel[0]);
        Assert.Equal("보고서 (2).TXT", rel[1]);   // Windows 는 대소문자를 가리지 않아 같은 이름
        Assert.Contains("x\\밖으로.txt", rel);   // '..' 는 버림 — 놓은 곳 밖에 쓰지 않음
        Assert.Contains("x\\콜론_이름_.txt", rel);
        Assert.Contains("x\\점끝", rel);
        Assert.All(rel, r => Assert.DoesNotContain("..", r.Split('\\')));
        Assert.Equal("x/../../밖으로.txt", payload.Items.Single(i => i.RelativePath == "x\\밖으로.txt").EntryKey);   // 꺼낼 때는 원래 열쇠
    }

    [Fact]
    public void 경로가_259글자를_넘는_항목은_빼고_알린다()
    {
        string deep = string.Join("/", Enumerable.Repeat("아주긴폴더이름입니다", 30));   // 30 × 11글자
        var all = new List<ArchiveEntry>
        {
            new() { Path = "짧음/a.txt", Size = 1 },
            new() { Path = "짧음/" + deep + "/끝.txt", Size = 2 },
        };
        var payload = DragPayloadBuilder.Build(new[] { Folder("짧음") }, all);
        Assert.All(payload.Items, i => Assert.True(i.RelativePath.Length <= DragPayloadBuilder.MaxRelativePathLength));
        Assert.Contains(payload.Items, i => i.RelativePath == "짧음\\a.txt");
        Assert.NotEmpty(payload.TooLong);
        Assert.DoesNotContain(payload.Items, i => i.EntryKey.EndsWith("끝.txt"));
    }

    // ───────────────────────────── LazyEntryExtractor ─────────────────────────────

    private static List<string> ContentFiles(string root) =>
        Directory.Exists(root)
            ? Directory.GetFiles(root, "*", SearchOption.AllDirectories).Where(f => Path.GetFileName(f) != ArchiveWorkspace.LockFileName).ToList()
            : new List<string>();

    /// <summary>
    /// 놓기 전에는 아무것도 꺼내지 않고, 처음 불릴 때 이 끌기의 파일을 한 번에 꺼낸 뒤(서비스 호출 1번) 같은·다른 항목 요청은 다시 꺼내지 않는다.
    /// (예전 계약은 '부른 항목만 하나씩'이었다 — 솔리드 압축에서 폴더를 끌면 파일 수의 제곱으로 느려져 W2-04 2차 검증에서 바꿈.)
    /// </summary>
    [Fact]
    public async Task 부르기_전에는_꺼내지_않고_처음_부를_때_끌어낸_파일을_한번에_꺼낸다()
    {
        using var tmp = new TempDir("lazy");
        var all = await ListAsync(Zip.FullPath);
        var payload = DragPayloadBuilder.Build(new[] { Folder("일집_샘플/하위폴더"), File_(all, "일집_샘플/readme.txt") }, all);
        string root = tmp.P("drag", "g1");
        var counting = new CountingService(Svc.For(Zip.FullPath));
        using var ex = new LazyEntryExtractor(counting, Zip.FullPath, null, payload.Items, root);

        Assert.False(Directory.Exists(root), "부르기 전에 폴더를 만듦");
        Assert.Equal(0, ex.ExtractionCount);

        int readme = payload.Items.ToList().FindIndex(i => i.RelativePath == "readme.txt");
        string p1 = ex.GetContent(readme);
        Assert.Equal(Zip.Expected.Single(e => e.Path == "일집_샘플/readme.txt").Sha256, TreeAssert.Sha256(p1));
        // 끌어낸 파일 3개(readme + 하위폴더 안 2개)가 한 번에 — 고른 것(폴더·파일) 열쇠만 넘김
        Assert.Equal(3, ContentFiles(root).Count);
        Assert.Equal(1, ex.ExtractionCount);
        Assert.Equal(new[] { "일집_샘플/하위폴더", "일집_샘플/readme.txt" }, Assert.Single(counting.Calls));

        Assert.Equal(p1, ex.GetContent(readme));   // 같은 항목 두 번 — 다시 꺼내지 않음
        int deep = payload.Items.ToList().FindIndex(i => i.RelativePath.EndsWith("깊은 파일 [끝].txt"));
        string p2 = ex.GetContent(deep);   // 다른 항목 — 꺼내 둔 것을 바로 줌
        Assert.Equal(Zip.Expected.Single(e => e.Path == "일집_샘플/하위폴더/2단계/3단계/깊은 파일 [끝].txt").Sha256, TreeAssert.Sha256(p2));
        int report = payload.Items.ToList().FindIndex(i => i.RelativePath == "하위폴더\\보고서 최종.txt");
        Assert.Equal(Zip.Expected.Single(e => e.Path == "일집_샘플/하위폴더/보고서 최종.txt").Sha256, TreeAssert.Sha256(ex.GetContent(report)));
        Assert.Equal(1, ex.ExtractionCount);
        Assert.Single(counting.Calls);
        Assert.Equal(3, ContentFiles(root).Count);

        Assert.Throws<ArgumentOutOfRangeException>(() => ex.GetContent(0));   // 폴더 항목은 내용이 없음
        Assert.Throws<ArgumentOutOfRangeException>(() => ex.GetContent(payload.Items.Count));

        ex.Dispose();
        Assert.False(Directory.Exists(root), "끝났는데 끌기 임시 폴더가 남음");
    }

    [Fact]
    public async Task 파일_하나만_끌면_그것만_꺼낸다()
    {
        using var tmp = new TempDir("lazyone");
        var all = await ListAsync(Zip.FullPath);
        var payload = DragPayloadBuilder.Build(new[] { File_(all, "일집_샘플/readme.txt") }, all);
        string root = tmp.P("drag", "g1");
        var counting = new CountingService(Svc.For(Zip.FullPath));
        using var ex = new LazyEntryExtractor(counting, Zip.FullPath, null, payload.Items, root);

        string p = ex.GetContent(0);
        Assert.Equal(Zip.Expected.Single(e => e.Path == "일집_샘플/readme.txt").Sha256, TreeAssert.Sha256(p));
        Assert.Equal(new[] { p }, ContentFiles(root));
        Assert.Equal(new[] { "일집_샘플/readme.txt" }, Assert.Single(counting.Calls));
        Assert.Equal(p, ex.GetContent(0));
        Assert.Equal(1, ex.ExtractionCount);
    }

    /// <summary>
    /// W2-04 2차 검증: 솔리드 7z(일집 7z 의 기본값, 반디집·7-Zip 도 기본 솔리드)의 폴더를 끌면, 예전엔 탐색기가 파일마다 내용을 달라고 할 때마다 그 하나만
    /// 꺼내느라 묶음 처음부터 다시 풀어 파일 수의 제곱으로 느려졌다(100KB × 300개: 95초, 한 번에 꺼내면 2초). 이제 첫 요청에 한 번만 꺼내고
    /// 나머지는 꺼내 둔 것을 준다 — 서비스 호출 수로 본다(시간은 PC 마다 달라 재지 않음).
    /// </summary>
    [Fact]
    public async Task 솔리드_7z_폴더를_끌면_첫_요청에_한번에_꺼내고_나머지는_다시_꺼내지_않는다()
    {
        using var tmp = new TempDir("lazysolid");
        string src = tmp.Dir("src", "사진 모음");
        var rnd = new Random(3);
        var originals = new Dictionary<string, byte[]>();
        for (int i = 0; i < 40; i++)
        {
            var data = new byte[20_000];
            for (int j = 0; j < data.Length; j++) data[j] = (byte)('a' + rnd.Next(16));
            string name = $"사진{i:D3}.jpg";
            File.WriteAllBytes(Path.Combine(src, name), data);
            originals[name] = data;
        }
        string arc = tmp.P("솔리드.7z");
        await new SevenZipArchiveService().CompressAsync(new[] { src }, arc, new CompressionOptions { Solid = true });
        var all = await ListAsync(arc);
        var payload = DragPayloadBuilder.Build(new[] { Folder("사진 모음") }, all);
        Assert.Equal(40, payload.Items.Count(i => !i.IsDirectory));

        var counting = new CountingService(Svc.For(arc));
        using var ex = new LazyEntryExtractor(counting, arc, null, payload.Items, tmp.P("drag"));
        for (int i = 0; i < payload.Items.Count; i++)
        {
            if (payload.Items[i].IsDirectory) continue;
            string name = Path.GetFileName(payload.Items[i].RelativePath);
            Assert.Equal(originals[name], File.ReadAllBytes(ex.GetContent(i)));
        }
        Assert.Equal(new[] { "사진 모음" }, Assert.Single(counting.Calls));   // 40개를 한 번에(예전: 40번)
        Assert.Equal(1, ex.ExtractionCount);
    }

    /// <summary>
    /// 폴더 안에 경로 공격 항목이 섞여 있으면, 폴더 열쇠로는 엔진이 꺼내기 전체를 거부하므로 안전한 파일의 열쇠만 넘겨 한 번에 꺼낸다(IJ3-01 —
    /// 예전엔 한 번에 꺼내기가 통째로 실패해 파일 5개를 하나씩 다시 꺼냈다: 솔리드 압축이면 파일 수의 제곱). 공격 항목만 불릴 때 따로 실패한다.
    /// </summary>
    [Fact]
    public async Task 경로_공격_항목이_섞인_폴더는_안전한_파일만_한번에_꺼내고_공격_항목만_실패한다()
    {
        using var tmp = new TempDir("lazyunsafe");
        string zip = BadFolderZip(tmp);
        var all = await ListAsync(zip);
        var payload = DragPayloadBuilder.Build(new[] { Folder("상자") }, all);
        string root = tmp.P("drag", "f");
        var counting = new CountingService(Svc.For(zip));
        using var ex = new LazyEntryExtractor(counting, zip, null, payload.Items, root);
        var failed = new List<string>();
        ex.ExtractionFailed += (item, _) => failed.Add(item.EntryKey);

        var results = new List<(string Rel, string? Text)>();
        for (int i = 0; i < payload.Items.Count; i++)
        {
            if (payload.Items[i].IsDirectory) continue;
            try { results.Add((payload.Items[i].RelativePath, File.ReadAllText(ex.GetContent(i)))); }
            catch (InvalidDataException) { results.Add((payload.Items[i].RelativePath, null)); }
        }

        Assert.Contains(("상자\\좋은1.txt", "좋음1"), results);
        Assert.Contains(("상자\\좋은2.txt", "좋음2"), results);
        Assert.Equal(3, results.Count(r => r.Text is null));   // 공격 항목 3개만 실패
        Assert.Equal(3, failed.Count);
        Assert.Equal(new[] { "상자/좋은1.txt", "상자/좋은2.txt" }, counting.Calls[0]);   // 멀쩡한 두 파일을 한 번에
        Assert.Equal(1 + 3, counting.Calls.Count);   // + 공격 항목이 불릴 때마다(엔진이 거부 — 아무것도 쓰지 않음)
        Assert.False(ex.IsCancelled);
        Assert.Empty(Directory.GetFiles(tmp.Root, "evil*", SearchOption.AllDirectories));
    }

    /// <summary>
    /// 한 번에 꺼내기가 실패하면(폴더 안 손상 항목 하나 — 확인값이 맞지 않는 파일) 그 뒤 요청은 항목마다 따로 꺼낸다: 멀쩡한 파일은 받고 손상 항목만 실패.
    /// 실패한 한 번에 꺼내기의 폴더는 지우고, 손상 항목의 반쪽 파일은 어디에도 남지 않는다.
    /// </summary>
    [Fact]
    public async Task 한번에_꺼내기가_실패하면_항목마다_따로_꺼내_멀쩡한_파일은_준다()
    {
        using var tmp = new TempDir("lazyfallback");
        string zip = tmp.P("손상섞임.zip");
        RawZip.Write(zip,
            RawZip.Utf8("상자/좋은1.txt", "좋음1"),
            new RawZip.Item(Encoding.UTF8.GetBytes("상자/깨짐.txt"), Encoding.UTF8.GetBytes(new string('원', 4000)), true, Deflate(new string('딴', 4000))),
            RawZip.Utf8("상자/좋은2.txt", "좋음2"));
        var all = await ListAsync(zip);
        var payload = DragPayloadBuilder.Build(new[] { Folder("상자") }, all);
        string root = tmp.P("drag", "f");
        var counting = new CountingService(Svc.For(zip));
        using var ex = new LazyEntryExtractor(counting, zip, null, payload.Items, root);
        var failed = new List<string>();
        ex.ExtractionFailed += (item, _) => failed.Add(item.EntryKey);

        var results = new List<(string Rel, string? Text)>();
        for (int i = 0; i < payload.Items.Count; i++)
        {
            if (payload.Items[i].IsDirectory) continue;
            try { results.Add((payload.Items[i].RelativePath, File.ReadAllText(ex.GetContent(i)))); }
            catch (Exception e) when (e is not OperationCanceledException) { results.Add((payload.Items[i].RelativePath, null)); }
        }

        Assert.Contains(("상자\\좋은1.txt", "좋음1"), results);
        Assert.Contains(("상자\\좋은2.txt", "좋음2"), results);
        Assert.Equal(("상자\\깨짐.txt", (string?)null), Assert.Single(results, r => r.Text is null));
        Assert.Equal(new[] { "상자/깨짐.txt" }, failed);
        Assert.Equal(new[] { "상자" }, counting.Calls[0]);   // 먼저 한 번에 → 실패 → 항목마다(파일 3개)
        Assert.Equal(1 + 3, counting.Calls.Count);
        Assert.False(Directory.Exists(Path.Combine(root, LazyEntryExtractor.BatchFolderName)), "실패한 한 번에 꺼내기의 폴더가 남음");
        Assert.Empty(Directory.GetFiles(tmp.Root, "깨짐*", SearchOption.AllDirectories));   // 반쪽 파일 없음
    }

    private static byte[] Deflate(string text)
    {
        using var ms = new MemoryStream();
        using (var d = new System.IO.Compression.DeflateStream(ms, System.IO.Compression.CompressionLevel.Optimal, leaveOpen: true))
            d.Write(Encoding.UTF8.GetBytes(text));
        return ms.ToArray();
    }

    /// <summary>'상자' 폴더에 멀쩡한 파일 2개와 경로 공격 항목 3개('상자/../../evil1.txt' 등 — 엔진이 '안전하지 않은 경로'로 거부).</summary>
    private static string BadFolderZip(TempDir tmp)
    {
        string zip = tmp.P("섞임.zip");
        RawZip.Write(zip,
            new RawZip.Item(Encoding.UTF8.GetBytes("상자/좋은1.txt"), Encoding.UTF8.GetBytes("좋음1")),
            new RawZip.Item(Encoding.UTF8.GetBytes("상자/../../evil1.txt"), Encoding.UTF8.GetBytes("EVIL")),
            new RawZip.Item(Encoding.UTF8.GetBytes("상자/좋은2.txt"), Encoding.UTF8.GetBytes("좋음2")),
            new RawZip.Item(Encoding.UTF8.GetBytes("상자/../../../evil2.txt"), Encoding.UTF8.GetBytes("EVIL")),
            new RawZip.Item(Encoding.UTF8.GetBytes("상자/x/../../../evil3.txt"), Encoding.UTF8.GetBytes("EVIL")));
        return zip;
    }

    [Fact]
    public void 한번에_꺼낼_때는_고른_것의_열쇠만_넘긴다()
    {
        var items = new List<DragItem>
        {
            new("폴더", "a/폴더", true, 0, null),
            new("폴더\\하위", "a/폴더/하위", true, 0, null),
            new("폴더\\하위\\1.txt", "a/폴더/하위/1.txt", false, 1, null),
            new("폴더\\2.txt", "a/폴더/2.txt", false, 1, null),
            new("3.txt", "a/3.txt", false, 1, null),
            new("떨어진\\4.txt", "b/4.txt", false, 1, null),   // 맨 위 항목 밑에 들지 않는 파일(직접 만든 묶음) — 그 열쇠도
        };
        Assert.Equal(new[] { "a/폴더", "a/3.txt", "b/4.txt" }, LazyEntryExtractor.BatchSelectors(items));

        // 경로 공격 항목이 섞이면 폴더 열쇠 대신 안전한 파일 열쇠만(공격 항목은 빼서 엔진이 꺼내기 전체를 거부하지 않게)
        items.Add(new("폴더\\evil.txt", "a/폴더/../../../evil.txt", false, 1, null));
        Assert.Equal(new[] { "a/폴더/하위/1.txt", "a/폴더/2.txt", "a/3.txt", "b/4.txt" }, LazyEntryExtractor.BatchSelectors(items));
        Assert.True(LazyEntryExtractor.IsUnsafeKey("a/../../evil.txt"));
        Assert.True(LazyEntryExtractor.IsUnsafeKey("C:/Windows/evil.txt"));
        Assert.False(LazyEntryExtractor.IsUnsafeKey("a/../b.txt"));   // 안쪽에 머무는 '..' 는 안전(엔진도 풂)
        Assert.False(LazyEntryExtractor.IsUnsafeKey("회의 10:00.txt"));
    }

    /// <summary>
    /// IJ3-01: 끌기 묶음 만들기·한 번에 꺼낼 열쇠 고르기가 고른 폴더 수 × 압축 항목 수로 느려지지 않는다(예전: 고른 폴더마다 압축 전체를 다시 훑음 —
    /// 폴더 2만 개·항목 10만 개면 수십억 번). 시간은 PC 마다 달라, 제곱이면 몇 분 걸릴 크기에서 넉넉한 한도로만 본다.
    /// </summary>
    [Fact]
    public void 폴더_수천_개를_끌어도_묶음_만들기와_열쇠_고르기는_한_번만_훑는다()
    {
        const int folders = 20_000;
        var all = new List<ArchiveEntry>(folders * 5);
        var selected = new List<ArchiveEntry>(folders);
        for (int f = 0; f < folders; f++)
        {
            var dir = new ArchiveEntry { Path = $"뿌리/폴더{f:D5}/", IsDirectory = true };
            all.Add(dir);
            selected.Add(dir);
            for (int k = 0; k < 4; k++) all.Add(new ArchiveEntry { Path = $"뿌리/폴더{f:D5}/속/{k}.txt", Size = k + 1 });
        }
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var payload = DragPayloadBuilder.Build(selected, all);
        var keys = LazyEntryExtractor.BatchSelectors(payload.Items);
        sw.Stop();

        Assert.Equal(folders * (1 + 1 + 4), payload.Items.Count);   // 폴더·'속'·파일 4개
        Assert.Equal(folders, keys.Count);   // 고른 폴더의 열쇠만
        Assert.Equal(("폴더00007\\속\\3.txt", "뿌리/폴더00007/속/3.txt"),
            payload.Items.Where(i => i.RelativePath == "폴더00007\\속\\3.txt").Select(i => (i.RelativePath, i.EntryKey)).Single());
        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(20), $"너무 느림: {sw.Elapsed}");
    }

    [Fact]
    public async Task 암호_압축은_끌기_전에_받은_암호로_꺼내고_암호가_없으면_실패를_알린다()
    {
        using var tmp = new TempDir("lazypw");
        var fx = FixtureCatalog.Get("zip_aes256");
        var all = await ListAsync(fx.FullPath);
        var payload = DragPayloadBuilder.Build(new[] { File_(all, "일집_샘플/readme.txt") }, all);
        Assert.True(File_(all, "일집_샘플/readme.txt").IsEncrypted, "목록이 암호 항목을 알려 줘야 끌기 전에 물을 수 있음");

        using (var ok = new LazyEntryExtractor(Svc.For(fx.FullPath), fx.FullPath, fx.Password, payload.Items, tmp.P("ok")))
            Assert.Equal(fx.Expected.Single(e => e.Path == "일집_샘플/readme.txt").Sha256, TreeAssert.Sha256(ok.GetContent(0)));

        using var bad = new LazyEntryExtractor(Svc.For(fx.FullPath), fx.FullPath, null, payload.Items, tmp.P("bad"));
        Exception? reported = null;
        bad.ExtractionFailed += (_, e) => reported = e;
        Assert.ThrowsAny<ArchivePasswordException>(() => bad.GetContent(0));
        Assert.IsAssignableFrom<ArchivePasswordException>(reported);
        Assert.Empty(ContentFiles(tmp.P("bad")));
    }

    // ───────────────────────────── VirtualFileDataObject(COM 직접 호출) ─────────────────────────────

    private static async Task<(VirtualFileDataObject Data, string Root)> MakeData(TempDir tmp)
    {
        var all = await ListAsync(Zip.FullPath);
        var payload = DragPayloadBuilder.Build(new[] { Folder("일집_샘플/하위폴더"), File_(all, "일집_샘플/readme.txt") }, all);
        string root = tmp.P("drag", Guid.NewGuid().ToString("N"));
        var ex = new LazyEntryExtractor(Svc.For(Zip.FullPath), Zip.FullPath, null, payload.Items, root);
        return (new VirtualFileDataObject(ex), root);
    }

    private static FORMATETC Fmt(short cf, TYMED tymed, int lindex = -1) => VirtualFileDataObject.Format(cf, tymed, lindex);

    [Fact]
    public async Task 형식_목록은_서술자_내용_복사효과뿐이고_CF_HDROP은_없다()
    {
        using var tmp = new TempDir("vfo1");
        var (data, _) = await MakeData(tmp);
        using var __ = data;
        ComIDataObjectAssert(data);

        var f = Fmt(VirtualFileDataObject.CfFileDescriptorW, TYMED.TYMED_HGLOBAL);
        Assert.Equal(0, data.QueryGetData(ref f));
        f = Fmt(VirtualFileDataObject.CfFileContents, TYMED.TYMED_ISTREAM, 0);
        Assert.Equal(0, data.QueryGetData(ref f));
        f = Fmt(VirtualFileDataObject.CfPreferredDropEffect, TYMED.TYMED_HGLOBAL);
        Assert.Equal(0, data.QueryGetData(ref f));
        f = Fmt(15 /* CF_HDROP */, TYMED.TYMED_HGLOBAL);
        Assert.Equal(VirtualFileDataObject.DV_E_FORMATETC, data.QueryGetData(ref f));   // 지연 CF_HDROP 은 쓰지 않음(DragEnter 때 다 꺼내 버림)
        f = Fmt(VirtualFileDataObject.CfFileDescriptorW, TYMED.TYMED_ISTREAM);
        Assert.Equal(VirtualFileDataObject.DV_E_TYMED, data.QueryGetData(ref f));

        var en = data.EnumFormatEtc(DATADIR.DATADIR_GET);
        var buf = new FORMATETC[10];
        var fetched = new int[1];
        en.Next(10, buf, fetched);
        var names = buf.Take(fetched[0]).Select(x => (ushort)x.cfFormat).ToList();
        Assert.Equal(new[] { (ushort)VirtualFileDataObject.CfFileDescriptorW, (ushort)VirtualFileDataObject.CfFileContents, (ushort)VirtualFileDataObject.CfPreferredDropEffect }, names);
        Assert.Throws<COMException>(() => data.EnumFormatEtc(DATADIR.DATADIR_SET));
    }

    /// <summary>COM 쪽에서 보이는 모습: IDataObject·IDataObjectAsyncCapability 를 QueryInterface 로 찾을 수 있다(탐색기가 비동기 모드를 찾는 길).</summary>
    private static void ComIDataObjectAssert(VirtualFileDataObject data)
    {
        IntPtr unk = Marshal.GetIUnknownForObject(data);
        try
        {
            foreach (var iid in new[] { new Guid("0000010E-0000-0000-C000-000000000046"), typeof(IDataObjectAsyncCapability).GUID })
            {
                Guid g = iid;
                Assert.Equal(0, Marshal.QueryInterface(unk, ref g, out IntPtr ppv));
                Marshal.Release(ppv);
            }
        }
        finally
        {
            Marshal.Release(unk);
        }
    }

    /// <summary>테스트 쪽 독립 판독기 — FILEGROUPDESCRIPTORW(UINT 개수 + 592바이트 × 개수).</summary>
    private static List<(uint Flags, uint Attr, long Size, long WriteTime, string Name)> ParseDescriptor(byte[] b)
    {
        uint n = BinaryPrimitives.ReadUInt32LittleEndian(b);
        Assert.Equal(4 + n * 592, (uint)b.Length);
        var list = new List<(uint, uint, long, long, string)>();
        for (int i = 0; i < n; i++)
        {
            var d = b.AsSpan(4 + i * 592, 592);
            uint flags = BinaryPrimitives.ReadUInt32LittleEndian(d);
            uint attr = BinaryPrimitives.ReadUInt32LittleEndian(d[36..]);
            long wt = BinaryPrimitives.ReadInt64LittleEndian(d[56..]);
            long size = ((long)BinaryPrimitives.ReadUInt32LittleEndian(d[64..]) << 32) | BinaryPrimitives.ReadUInt32LittleEndian(d[68..]);
            string name = Encoding.Unicode.GetString(d.Slice(72, 520));
            list.Add((flags, attr, size, wt, name[..name.IndexOf('\0')]));
        }
        return list;
    }

    [Fact]
    public async Task 서술자는_HGLOBAL에_개수와_한글_이름_폴더표시_크기를_담는다()
    {
        using var tmp = new TempDir("vfo2");
        var (data, root) = await MakeData(tmp);
        using var __ = data;
        var f = Fmt(VirtualFileDataObject.CfFileDescriptorW, TYMED.TYMED_HGLOBAL);
        data.GetData(ref f, out var medium);
        try
        {
            Assert.Equal(TYMED.TYMED_HGLOBAL, medium.tymed);
            var parsed = ParseDescriptor(VirtualFileDataObject.ReadHGlobal(medium.unionmember));
            Assert.Equal(data.Items.Select(i => i.RelativePath), parsed.Select(p => p.Name));
            for (int i = 0; i < parsed.Count; i++)
            {
                var (flags, attr, size, wt, _) = parsed[i];
                var item = data.Items[i];
                Assert.True((flags & 0x80000000) != 0, "FD_UNICODE");
                Assert.True((flags & 0x4) != 0, "FD_ATTRIBUTES");
                if (item.IsDirectory)
                {
                    Assert.Equal(0x10u, attr & 0x10);
                    Assert.Equal(0u, flags & 0x40);   // 폴더는 크기 없음
                }
                else
                {
                    Assert.Equal(0u, attr & 0x10);
                    Assert.True((flags & 0x40) != 0 && (flags & 0x4000) != 0, "파일은 FD_FILESIZE·FD_PROGRESSUI");
                    Assert.Equal(item.Size, size);
                    Assert.True((flags & 0x20) != 0, "FD_WRITESTIME");
                    var lm = item.LastModified!.Value;
                    Assert.Equal(lm.Kind == DateTimeKind.Utc ? lm.ToFileTimeUtc() : lm.ToFileTime(), wt);
                }
            }
        }
        finally
        {
            VirtualFileDataObject.Release(ref medium);
        }
        Assert.False(Directory.Exists(root), "서술자만 달라고 했는데 꺼냄");

        f = Fmt(VirtualFileDataObject.CfPreferredDropEffect, TYMED.TYMED_HGLOBAL);
        data.GetData(ref f, out medium);
        try { Assert.Equal(new byte[] { 1, 0, 0, 0 }, VirtualFileDataObject.ReadHGlobal(medium.unionmember)); }
        finally { VirtualFileDataObject.Release(ref medium); }
    }

    /// <summary>GetData(FileContents) 를 받은 순간에 꺼낸다 — 파일이 여럿이면 그때 이 끌기의 파일을 한 번에(서비스 호출 1번), 다음 요청은 다시 꺼내지 않음.</summary>
    [Fact]
    public async Task 내용은_GetData를_받은_순간에_꺼내_IStream으로_준다()
    {
        using var tmp = new TempDir("vfo3");
        var (data, root) = await MakeData(tmp);
        using var __ = data;
        int readme = data.Items.ToList().FindIndex(i => i.RelativePath == "readme.txt");
        Assert.Empty(ContentFiles(root));

        var f = Fmt(VirtualFileDataObject.CfFileContents, TYMED.TYMED_ISTREAM, readme);
        data.GetData(ref f, out var medium);
        byte[] got;
        try
        {
            Assert.Equal(TYMED.TYMED_ISTREAM, medium.tymed);
            var stream = (IStream)Marshal.GetObjectForIUnknown(medium.unionmember);
            stream.Stat(out var stat, 0);
            Assert.Equal(SizeOf("일집_샘플/readme.txt"), stat.cbSize);
            Assert.Equal("readme.txt", stat.pwcsName);
            got = ReadAll(stream, (int)stat.cbSize);
        }
        finally
        {
            VirtualFileDataObject.Release(ref medium);
        }
        Assert.Equal(Zip.Expected.Single(e => e.Path == "일집_샘플/readme.txt").Sha256, TreeAssert.Sha256(got));
        Assert.Equal(data.Items.Count(i => !i.IsDirectory), ContentFiles(root).Count);   // 끌어낸 파일을 한 번에
        Assert.Equal(1, data.Extractor.ExtractionCount);

        // 폴더 번호·범위 밖은 DV_E_LINDEX
        var dirFmt = Fmt(VirtualFileDataObject.CfFileContents, TYMED.TYMED_ISTREAM, 0);
        var err = Assert.Throws<COMException>(() => data.GetData(ref dirFmt, out _));
        Assert.Equal(VirtualFileDataObject.DV_E_LINDEX, err.HResult);
        Assert.Equal(1, data.Extractor.ExtractionCount);

        // 작은 파일은 HGLOBAL 로도(ISTREAM 을 못 받는 곳)
        var hg = Fmt(VirtualFileDataObject.CfFileContents, TYMED.TYMED_HGLOBAL, readme);
        data.GetData(ref hg, out medium);
        try { Assert.Equal(got, VirtualFileDataObject.ReadHGlobal(medium.unionmember)[..got.Length]); }
        finally { VirtualFileDataObject.Release(ref medium); }
        Assert.Equal(1, data.Extractor.ExtractionCount);
    }

    private static byte[] ReadAll(IStream s, int length)
    {
        var buf = new byte[length + 16];
        IntPtr pcb = Marshal.AllocHGlobal(4);
        try
        {
            int total = 0;
            while (true)
            {
                var chunk = new byte[4096];
                s.Read(chunk, chunk.Length, pcb);
                int n = Marshal.ReadInt32(pcb);
                if (n == 0) break;
                Array.Copy(chunk, 0, buf, total, n);
                total += n;
            }
            return buf[..total];
        }
        finally
        {
            Marshal.FreeHGlobal(pcb);
        }
    }

    [Fact]
    public async Task 비동기_모드는_처음부터_켜져_있고_EndOperation에서_임시폴더를_지운다()
    {
        using var tmp = new TempDir("vfo4");
        var (data, root) = await MakeData(tmp);
        bool finished = false;
        data.Finished += _ => finished = true;

        data.GetAsyncMode(out bool async);
        Assert.True(async, "탐색기가 자기 스레드에서 당겨 가게 비동기 모드여야 함");
        data.StartOperation(IntPtr.Zero);
        data.InOperation(out bool inOp);
        Assert.True(inOp);

        int readme = data.Items.ToList().FindIndex(i => i.RelativePath == "readme.txt");
        var f = Fmt(VirtualFileDataObject.CfFileContents, TYMED.TYMED_ISTREAM, readme);
        data.GetData(ref f, out var medium);
        Assert.True(Directory.Exists(root));
        // 탐색기가 스트림을 아직 놓지 않은 채 끝을 알려도 닫고 지운다
        data.EndOperation(0, IntPtr.Zero, 1);
        VirtualFileDataObject.Release(ref medium);

        data.InOperation(out inOp);
        Assert.False(inOp);
        Assert.True(finished);
        Assert.True(data.IsFinished);
        Assert.False(Directory.Exists(root), "끌기가 끝났는데 임시 폴더가 남음");
    }

    [Fact]
    public async Task 받는_쪽이_넣은_값은_보관했다가_돌려준다()
    {
        using var tmp = new TempDir("vfo5");
        var (data, _) = await MakeData(tmp);
        using var __ = data;
        short cf = VirtualFileDataObject.Native.Register("Performed DropEffect");
        var f = Fmt(cf, TYMED.TYMED_HGLOBAL);
        Assert.Equal(VirtualFileDataObject.DV_E_FORMATETC, data.QueryGetData(ref f));
        var m = VirtualFileDataObject.HGlobalMedium(new byte[] { 1, 0, 0, 0 });
        data.SetData(ref f, ref m, true);   // 넘긴 HGLOBAL 은 우리가 놓음
        Assert.Equal(0, data.QueryGetData(ref f));
        data.GetData(ref f, out var back);
        try { Assert.Equal(new byte[] { 1, 0, 0, 0 }, VirtualFileDataObject.ReadHGlobal(back.unionmember)); }
        finally { VirtualFileDataObject.Release(ref back); }
    }

    /// <summary>
    /// W2-04 1차 검증: 탐색기는 FileContents 스트림으로 놓은 곳에 새 파일을 만들어, 임시 파일에 붙인 인터넷 표식이 옮겨지지 않았다.
    /// 원본 압축에 표식이 있으면 셸 형식 'ZoneIdentifier'(영역 번호 DWORD)를 함께 실어 탐색기가 붙이게 한다(실제 반영은 손 점검 20절). 표식이 없으면 주지 않는다.
    /// </summary>
    [Fact]
    public async Task 인터넷_표식이_있는_압축이면_ZoneIdentifier_형식으로_영역을_알린다()
    {
        using var tmp = new TempDir("vfozone");
        string arc = tmp.P("메일첨부.zip");
        File.Copy(Zip.FullPath, arc);
        File.WriteAllText(arc + ZoneIdentifier.StreamName, "[ZoneTransfer]\r\nZoneId=3\r\nHostUrl=https://mail.example.com/\r\n");
        var all = await ListAsync(arc);
        var payload = DragPayloadBuilder.Build(new[] { File_(all, "일집_샘플/readme.txt") }, all);
        string root = tmp.P("drag", "z");
        using var data = new VirtualFileDataObject(new LazyEntryExtractor(Svc.For(arc), arc, null, payload.Items, root));

        Assert.Equal(3, data.ZoneId);
        var f = Fmt(VirtualFileDataObject.CfZoneIdentifier, TYMED.TYMED_HGLOBAL);
        Assert.Equal(0, data.QueryGetData(ref f));
        data.GetData(ref f, out var medium);
        try { Assert.Equal(new byte[] { 3, 0, 0, 0 }, VirtualFileDataObject.ReadHGlobal(medium.unionmember)[..4]); }
        finally { VirtualFileDataObject.Release(ref medium); }
        var en = data.EnumFormatEtc(DATADIR.DATADIR_GET);
        var buf = new FORMATETC[10];
        var fetched = new int[1];
        en.Next(10, buf, fetched);
        Assert.Contains((ushort)VirtualFileDataObject.CfZoneIdentifier, buf.Take(fetched[0]).Select(x => (ushort)x.cfFormat));
        Assert.False(Directory.Exists(root), "표식만 알렸는데 꺼냄");

        // 표식이 없는 압축은 'ZoneIdentifier' 를 주지 않는다
        var (plain, _) = await MakeData(tmp);
        using var ___ = plain;
        Assert.Null(plain.ZoneId);
        var g = Fmt(VirtualFileDataObject.CfZoneIdentifier, TYMED.TYMED_HGLOBAL);
        Assert.Equal(VirtualFileDataObject.DV_E_FORMATETC, plain.QueryGetData(ref g));
        Assert.Throws<COMException>(() => plain.GetData(ref g, out _));
    }

    /// <summary>
    /// 대소문자만 다른 두 파일이 든 폴더를 끌면 서술자는 'Report.txt'·'report (2).txt'. 하나씩 꺼내도 풀기 엔진은 둘 다 꺼내므로(고른 열쇠가 대소문자 무시),
    /// 각 항목에 제 내용을 줘야 한다(예전엔 둘 다 앞엣것 내용일 수 있었음).
    /// </summary>
    [Fact]
    public async Task 대소문자만_다른_두_파일을_끌면_각각_제_내용을_준다()
    {
        using var tmp = new TempDir("lazycase");
        string zip = TestZip.Create(tmp.P("리눅스.zip"), TestZip.Text("대소/Report.txt", "R1"), TestZip.Text("대소/report.txt", "R2"));
        var all = await ListAsync(zip);
        var payload = DragPayloadBuilder.Build(new[] { Folder("대소") }, all);
        using var ex = new LazyEntryExtractor(Svc.For(zip), zip, null, payload.Items, tmp.P("drag", "c"));
        var got = payload.Items.Select((it, i) => (it, i)).Where(x => !x.it.IsDirectory)
            .Select(x => (x.it.RelativePath, File.ReadAllText(ex.GetContent(x.i)))).ToList();
        Assert.Equal(new[] { ("대소\\Report.txt", "R1"), ("대소\\report (2).txt", "R2") }, got);
    }

    /// <summary>
    /// 대상 경로에 '~' 가 있으면 .NET 이 짧은 이름을 풀면서 이미 있는 파일의 실제 대소문자를 돌려줘, 엔진이 뒤엣것('report.txt')을 'Report (1).txt' 로
    /// 비켜 쓴다(W2-04 2차 — 안의 파일 열기의 옆 폴더 이름을 '1~' 로 했을 때 실제로 앞엣것 내용을 열었음). 그래도 고른 것의 자리를 찾는다.
    /// </summary>
    [Fact]
    public async Task 엔진이_뒤엣것을_앞엣것_대소문자로_비켜_써도_고른_것의_자리를_찾는다()
    {
        using var tmp = new TempDir("locatecase");
        string zip = TestZip.Create(tmp.P("리눅스.zip"), TestZip.Text("대소/Report.txt", "R1"), TestZip.Text("대소/report.txt", "R2"));
        string dest = tmp.P("물결~", "꺼냄");
        await Svc.For(zip).ExtractEntriesAsync(zip, dest, new[] { "대소/report.txt" });
        var names = Directory.GetFiles(Path.Combine(dest, "대소")).Select(Path.GetFileName).OrderBy(n => n, StringComparer.Ordinal).ToList();
        Assert.Equal(2, names.Count);

        Assert.Equal("R2", File.ReadAllText(LazyEntryExtractor.LocateExtracted(dest, "대소/report.txt", isDirectory: false)!));
        Assert.Equal("R1", File.ReadAllText(LazyEntryExtractor.LocateExtracted(dest, "대소/Report.txt", isDirectory: false)!));
        Assert.Equal("R2", File.ReadAllText(LazyEntryExtractor.FindExtracted(dest, "대소/report.txt")!));

        // 디스크 이름만으로도(엔진과 상관없이): 'Report.txt'(R1)·'Report (1).txt'(R2) 에서 'report.txt' 는 비켜 쓴 쪽
        string dir = tmp.Dir("손으로", "대소");
        File.WriteAllText(Path.Combine(dir, "Report.txt"), "R1");
        File.WriteAllText(Path.Combine(dir, "Report (1).txt"), "R2");
        string root = Path.GetDirectoryName(dir)!;
        var claimed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        string a = LazyEntryExtractor.LocateExtracted(root, "대소/Report.txt", false, claimed)!;
        claimed.Add(a);
        string b = LazyEntryExtractor.LocateExtracted(root, "대소/report.txt", false, claimed)!;
        Assert.Equal(("R1", "R2"), (File.ReadAllText(a), File.ReadAllText(b)));
    }

    // ───────────────────────────── 화면(MainViewModel) ─────────────────────────────

    [Fact]
    public async Task 화면은_끌기_데이터만_만들고_놓기_전에는_아무것도_꺼내지_않는다()
    {
        using var tmp = new TempDir("vmdrag");
        using var _ = TestServices.UseAppRoot(tmp);
        var ts = new TestServices();
        var ui = new FakeUserInteraction();
        var vm = new MainViewModel(ui, services: ts.Services);
        string dragRoot = AppPaths.DragRoot;
        VirtualFileDataObject? data = null;
        await VmRunner.Run(async () =>
        {
            await vm.OpenArchiveAsync(Zip.FullPath);
            vm.NavigateToFolder("일집_샘플");
            var sel = vm.VisibleEntries.Where(e => e.Name is "하위폴더" or "readme.txt").ToList();
            Assert.False(vm.DragNeedsPassword(sel));
            data = vm.CreateDragData(sel);
        }, "끌기 데이터");

        Assert.NotNull(data);
        Assert.Contains(data!.Items, i => i.RelativePath == "하위폴더\\2단계\\3단계\\깊은 파일 [끝].txt");
        Assert.False(Directory.Exists(dragRoot) && Directory.EnumerateFileSystemEntries(dragRoot).Any(), "놓기 전에 꺼냄");
        Assert.Empty(ui.ProgressSessions);

        // 놓은 순간(탐색기의 GetData)에 꺼낸다 — 다른 스레드에서 와도 됨
        int readme = data.Items.ToList().FindIndex(i => i.RelativePath == "readme.txt");
        await Task.Run(() =>
        {
            var f = Fmt(VirtualFileDataObject.CfFileContents, TYMED.TYMED_ISTREAM, readme);
            data.GetData(ref f, out var medium);
            VirtualFileDataObject.Release(ref medium);
        }).WaitAsync(Limit);
        Assert.Equal(new[] { "readme.txt", "깊은 파일 [끝].txt", "보고서 최종.txt" }, ContentFiles(dragRoot).Select(Path.GetFileName).OrderBy(n => n, StringComparer.Ordinal));   // 끌어낸 파일을 한 번에
        Assert.Empty(ui.ProgressSessions);   // 합쳐 작으면 진행 창 없이

        vm.Shutdown();   // 앱을 닫으면 남은 끌기도 정리
        Assert.True(data.IsFinished);
        Assert.Empty(ContentFiles(dragRoot));
    }

    [Fact]
    public async Task 암호가_필요하면_끌기_전에만_묻고_다시_끌게_한다()
    {
        using var tmp = new TempDir("vmdragpw");
        using var _ = TestServices.UseAppRoot(tmp);
        var fx = FixtureCatalog.Get("zip_aes256");
        var ts = new TestServices();
        var ui = new FakeUserInteraction("틀림", fx.Password);
        var vm = new MainViewModel(ui, services: ts.Services);
        string dragRoot = AppPaths.DragRoot;
        VirtualFileDataObject? data = null;
        await VmRunner.Run(async () =>
        {
            await vm.OpenArchiveAsync(fx.FullPath);
            vm.NavigateToFolder("일집_샘플");
            var sel = vm.VisibleEntries.Where(e => e.Name is "readme.txt" or "data").ToList();
            Assert.True(vm.DragNeedsPassword(sel));
            Assert.True(await vm.EnsurePasswordForDragAsync(sel));
            Assert.False(vm.DragNeedsPassword(sel));
            data = vm.CreateDragData(sel);
        }, "끌기 전 암호");

        Assert.Equal(new[] { PasswordPromptReason.Required, PasswordPromptReason.Wrong }, ui.PasswordPrompts.Select(p => p.Reason));
        Assert.Equal("암호를 확인했어요. 다시 끌어 주세요.", vm.StatusText);
        Assert.False(Directory.Exists(dragRoot) && Directory.EnumerateFileSystemEntries(dragRoot).Any(), "암호 확인에 쓴 사본이 남음");

        int csv = data!.Items.ToList().FindIndex(i => i.RelativePath == "data\\sample.csv");
        var f = Fmt(VirtualFileDataObject.CfFileContents, TYMED.TYMED_ISTREAM, csv);
        data.GetData(ref f, out var medium);
        VirtualFileDataObject.Release(ref medium);
        // 끌어낸 파일을 한 번에 — 모두 끌기 전에 받은 암호로 맞게 풀림
        string all = Path.Combine(data.Extractor.WorkRoot, LazyEntryExtractor.BatchFolderName);
        var got = ContentFiles(dragRoot);
        Assert.Equal(data.Items.Count(i => !i.IsDirectory), got.Count);
        Assert.Contains(got, p => Path.GetFileName(p) == "sample.csv");
        foreach (var p in got)
        {
            string entry = Path.GetRelativePath(all, p).Replace('\\', '/');
            Assert.Equal(fx.Expected.Single(e => e.Path == entry).Sha256, TreeAssert.Sha256(p));
        }
        Assert.Equal(2, ui.PasswordPrompts.Count);   // 끄는 도중에는 묻지 않음
        data.Dispose();
    }

    /// <summary>
    /// 끌어낸 파일이 합쳐 8MB 이상이면 놓은 순간 꺼내는 동안 진행 창을 하나 띄우고, [취소]면 탐색기에 E_ABORT — 반쪽 파일을 남기지 않는다.
    /// 취소한 뒤 탐색기가 다른 항목을 달라고 해도 꺼내지 않고 바로 E_ABORT(IJ3-01 — 예전엔 남은 항목을 하나씩 따로 꺼내 솔리드 압축이면 오래 걸리고
    /// 큰 항목마다 진행 창이 또 떴다). 취소하지 않았으면 다른 항목은 꺼내 둔 것을 바로 준다.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task 큰_항목은_꺼내는_동안_진행창을_띄우고_취소하면_반쪽을_남기지_않는다(bool cancel)
    {
        using var tmp = new TempDir("vmdragbig");
        using var root = TestServices.UseAppRoot(tmp);
        var big = new byte[(int)MainViewModel.DragProgressThreshold + 1234];
        new Random(5).NextBytes(big.AsSpan(0, 4096));
        string zip = TestZip.Create(tmp.P("큰것.zip"), ("영상/큰 파일.bin", big), TestZip.Text("영상/작은.txt", "x"));
        var ui = new FakeUserInteraction { CancelProgress = cancel };
        var vm = new MainViewModel(ui, services: new TestServices().Services);
        string dragRoot = AppPaths.DragRoot;
        VirtualFileDataObject? data = null;
        await VmRunner.Run(async () =>
        {
            await vm.OpenArchiveAsync(zip);
            vm.NavigateToFolder("영상");
            data = vm.CreateDragData(vm.VisibleEntries.ToList());
        }, "큰 항목 끌기");

        int bigIndex = data!.Items.ToList().FindIndex(i => i.RelativePath == "큰 파일.bin");
        int smallIndex = data.Items.ToList().FindIndex(i => i.RelativePath == "작은.txt");
        var f = Fmt(VirtualFileDataObject.CfFileContents, TYMED.TYMED_ISTREAM, bigIndex);
        if (cancel)
        {
            var err = Assert.Throws<COMException>(() => data.GetData(ref f, out _));
            Assert.Equal(VirtualFileDataObject.E_ABORT, err.HResult);
            Assert.Empty(ContentFiles(dragRoot));   // 반쪽 파일 없음
            Assert.Empty(ui.Errors);                 // 취소는 오류 창 없음
        }
        else
        {
            data.GetData(ref f, out var medium);
            VirtualFileDataObject.Release(ref medium);
            var got = ContentFiles(dragRoot);
            Assert.Equal(2, got.Count);   // 끌어낸 두 파일을 한 번에
            Assert.Equal(TreeAssert.Sha256(big), TreeAssert.Sha256(Assert.Single(got, p => Path.GetFileName(p) == "큰 파일.bin")));
        }
        var session = Assert.Single(ui.ProgressSessions);   // 큰 꺼내기에만
        Assert.True(session.Reports.Count > 0 || cancel, "진행 창에 보고가 없음");

        var s = Fmt(VirtualFileDataObject.CfFileContents, TYMED.TYMED_ISTREAM, smallIndex);
        if (cancel)
        {
            var again = Assert.Throws<COMException>(() => data.GetData(ref s, out _));
            Assert.Equal(VirtualFileDataObject.E_ABORT, again.HResult);   // 취소한 끌기는 더 꺼내지 않음
            Assert.True(data.Extractor.IsCancelled);
            Assert.Empty(ContentFiles(dragRoot));
            Assert.Empty(ui.Errors);
        }
        else
        {
            data.GetData(ref s, out var sm);
            VirtualFileDataObject.Release(ref sm);
        }
        Assert.Single(ui.ProgressSessions);   // 진행 창은 처음 한 번뿐
        Assert.Equal(1, data.Extractor.ExtractionCount);
        data.Dispose();
        Assert.Empty(ContentFiles(dragRoot));
    }

    /// <summary>
    /// 파일 하나하나는 8MB 보다 작아도 합쳐 크면(사진 폴더 등) 한 번에 꺼내는 동안 진행 창을 하나 띄운다 — 예전엔 항목마다 따로 재서 창이 뜨지 않았고,
    /// 사용자는 탐색기 진행 창이 아주 느리게 차는 것만 봤다(W2-04 2차 검증).
    /// </summary>
    [Fact]
    public async Task 작은_파일이_여럿이라도_합쳐_크면_진행창을_하나_띄운다()
    {
        using var tmp = new TempDir("vmdragsum");
        using var root = TestServices.UseAppRoot(tmp);
        var parts = Enumerable.Range(1, 3).Select(i =>
        {
            var b = new byte[3 * 1024 * 1024];
            new Random(i).NextBytes(b.AsSpan(0, 4096));
            return ($"사진/{i}.jpg", b);
        }).ToArray();
        string zip = TestZip.Create(tmp.P("사진.zip"), parts);
        var ui = new FakeUserInteraction();
        var vm = new MainViewModel(ui, services: new TestServices().Services);
        VirtualFileDataObject? data = null;
        await VmRunner.Run(async () =>
        {
            await vm.OpenArchiveAsync(zip);
            data = vm.CreateDragData(vm.VisibleEntries.Where(e => e.Name == "사진").ToList());
        }, "사진 폴더 끌기");

        Assert.All(data!.Items.Where(i => !i.IsDirectory), i => Assert.True(i.Size < MainViewModel.DragProgressThreshold));
        for (int i = 0; i < data.Items.Count; i++)
        {
            if (data.Items[i].IsDirectory) continue;
            var f = Fmt(VirtualFileDataObject.CfFileContents, TYMED.TYMED_ISTREAM, i);
            data.GetData(ref f, out var medium);
            VirtualFileDataObject.Release(ref medium);
        }
        var session = Assert.Single(ui.ProgressSessions);   // 첫 요청 때 한 번
        Assert.NotEmpty(session.Reports);
        Assert.Equal(1, data.Extractor.ExtractionCount);
        Assert.Equal("끌어낸 파일 3개를 꺼내는 중...", MainViewModel.DragProgressTitle(data.Items.Where(i => !i.IsDirectory).ToList()));
        data.Dispose();
    }

    /// <summary>
    /// 경로 공격 항목이 여럿 든 폴더를 끌면 탐색기가 항목마다 내용을 달라고 해 실패가 여러 번 난다. 예전엔 실패마다 오류 창이 하나씩 떴다
    /// (탐색기도 제 오류 창을 띄워 겹침 — W2-04 2차 검증). 이제 끌기 한 번에 오류 창은 한 번, 알림줄에 못 꺼낸 개수.
    /// </summary>
    [Fact]
    public async Task 끌기_중_여러_항목이_실패해도_오류_창은_한번만_띄운다()
    {
        using var tmp = new TempDir("vmdragerr");
        using var root = TestServices.UseAppRoot(tmp);
        string zip = BadFolderZip(tmp);
        var ui = new FakeUserInteraction();
        var vm = new MainViewModel(ui, services: new TestServices().Services);
        VirtualFileDataObject? data = null;
        await VmRunner.Run(async () =>
        {
            await vm.OpenArchiveAsync(zip);
            data = vm.CreateDragData(vm.VisibleEntries.Where(e => e.IsDirectory && e.Name == "상자").ToList());
        }, "섞인 폴더 끌기");

        var hr = new List<int>();
        for (int i = 0; i < data!.Items.Count; i++)
        {
            if (data.Items[i].IsDirectory) continue;
            var f = Fmt(VirtualFileDataObject.CfFileContents, TYMED.TYMED_ISTREAM, i);
            try
            {
                data.GetData(ref f, out var medium);
                VirtualFileDataObject.Release(ref medium);
                hr.Add(0);
            }
            catch (COMException ex)
            {
                hr.Add(ex.HResult);
            }
        }
        Assert.Equal(2, hr.Count(h => h == 0));   // 멀쩡한 두 파일은 받음
        Assert.Equal(3, hr.Count(h => h == VirtualFileDataObject.E_FAIL));   // 공격 항목 셋은 탐색기에 실패로
        string err = Assert.Single(ui.Errors);   // 오류 창은 한 번
        Assert.StartsWith("끌어 꺼내지 못했어요:", err);
        Assert.Contains("위험한 경로", err);
        Assert.Equal("오류 발생 — 끌어 꺼내지 못한 파일 3개", vm.StatusText);
        data.Dispose();
    }

    [Fact]
    public async Task 경로가_너무_긴_항목이_있으면_알리고_끌지_않는다()
    {
        using var tmp = new TempDir("vmdraglong");
        using var _ = TestServices.UseAppRoot(tmp);
        string deep = string.Join("/", Enumerable.Repeat("아주긴폴더이름입니다", 30));
        string zip = TestZip.Create(tmp.P("긴경로.zip"), TestZip.Text("상자/a.txt", "a"), TestZip.Text("상자/" + deep + "/끝.txt", "z"));
        var ui = new FakeUserInteraction();
        var vm = new MainViewModel(ui, services: new TestServices().Services);
        VirtualFileDataObject? data = null;
        await VmRunner.Run(async () =>
        {
            await vm.OpenArchiveAsync(zip);
            data = vm.CreateDragData(vm.VisibleEntries.Where(e => e.Name == "상자").ToList());
        }, "긴 경로");
        Assert.Null(data);
        Assert.Contains("경로가 너무 긴 항목", Assert.Single(ui.Warnings));
    }

    /// <summary>진짜 서비스로 풀되 ExtractEntriesAsync 를 부른 열쇠를 차례로 적는 감싸개(꺼내기를 몇 번 맡겼는지 본다).</summary>
    private sealed class CountingService(IArchiveService inner) : IArchiveService
    {
        private readonly List<string[]> _calls = new();

        public IReadOnlyList<string[]> Calls
        {
            get { lock (_calls) return _calls.ToList(); }
        }

        public IReadOnlyList<string> SupportedExtensions => inner.SupportedExtensions;

        public Task<IReadOnlyList<ArchiveEntry>> ListEntriesAsync(string archivePath, string? password = null, CancellationToken cancellationToken = default) =>
            inner.ListEntriesAsync(archivePath, password, cancellationToken);

        public Task ExtractAsync(string archivePath, string destinationFolder, string? password = null, IProgress<ArchiveProgress>? progress = null, CancellationToken cancellationToken = default) =>
            inner.ExtractAsync(archivePath, destinationFolder, password, progress, cancellationToken);

        public Task ExtractEntriesAsync(string archivePath, string destinationFolder, IReadOnlyCollection<string> entryPaths, string? password = null,
            IProgress<ArchiveProgress>? progress = null, CancellationToken cancellationToken = default)
        {
            lock (_calls) _calls.Add(entryPaths.ToArray());
            return inner.ExtractEntriesAsync(archivePath, destinationFolder, entryPaths, password, progress, cancellationToken);
        }

        public Task<CompressResult> CompressAsync(IEnumerable<string> sourcePaths, string archivePath, CompressionOptions? options = null, IProgress<ArchiveProgress>? progress = null, CancellationToken cancellationToken = default) =>
            inner.CompressAsync(sourcePaths, archivePath, options, progress, cancellationToken);
    }
}
