using Iljip.Models;
using Iljip.Services;
using Iljip.Services.Editing;
using Iljip.Tests.TestSupport;
using SharpSevenZip;

namespace Iljip.Tests.Contract;

/// <summary>
/// [W2-06, I2-3] 압축 고치기(넣기·지우기·이름 바꾸기·새 폴더) — ZIP·7z 만.
/// 대상: ZIP(반디집 기본·CP949 이름·ZipCrypto·ZipCrypto 한글 암호·AES-256·AES 한글 암호·데이터 서술자), 7z(반디집 기본·파일 이름 암호화·한글 암호, 7z.dll 로 만든 솔리드·내용만 암호).
/// 고친 뒤: 목록이 기대대로, 풀린 파일 SHA 가 기대대로, (ZIP) 손대지 않은 항목은 이름 바이트·플래그·압축된 데이터까지 그대로, 새 이름은 UTF-8(bit 11),
/// 암호 압축은 여전히 암호가 필요하고 같은 방식(같은 암호 바이트·AES 세기)이며, (7z) 파일 이름 암호화가 그대로, 반디집(bz t)이 푼다.
/// 실패(취소·잠김·쓰기 실패·검사 실패)하면 원본은 바이트 그대로이고 임시 파일·예비 파일·임시 폴더가 남지 않는다.
/// </summary>
public class ArchiveEditTests
{
    public static readonly string[] ZipTargetIds =
    {
        "zip_default", "zip_cp949", "zip_zipcrypto", "zip_zipcrypto_korean_pw", "zip_aes256", "zip_aes256_korean_pw", "zip_data_descriptor",
    };

    public static readonly string[] SevenZipTargetIds =
    {
        "7z_default", "7z_header_encrypted", "7z_header_encrypted_korean_pw", "7z_solid_made", "7z_password_made",
    };

    public static readonly string[] OpNames = { "add", "delete", "rename", "newfolder" };

    public static IEnumerable<object[]> ZipCases => from t in ZipTargetIds from o in OpNames select new object[] { t, o };
    public static IEnumerable<object[]> SevenZipCases => from t in SevenZipTargetIds from o in OpNames select new object[] { t, o };
    public static IEnumerable<object[]> AllTargets => ZipTargetIds.Concat(SevenZipTargetIds).Select(t => new object[] { t });

    // ───────────────────────────── 대상 만들기 ─────────────────────────────

    internal sealed record Target(string Path, string? Password, bool IsZip);

    internal static Target Prepare(string id, TempDir t)
    {
        switch (id)
        {
            case "zip_data_descriptor":
            {
                string dd = t.P("서술자.zip");
                ZipInspect.WriteWithDataDescriptors(dd,
                    ("자료/가.txt", Text("가나다라 ", 300)),
                    ("자료/나.txt", Text("마바사 ", 200)),
                    ("다.bin", Random(5000, 1)),
                    ("빈.txt", Array.Empty<byte>()));
                return new Target(dd, null, true);
            }
            case "7z_solid_made":
                return new Target(SevenZipTestArchives.SevenZip(MakeTree(t.Dir("src7")), t.P("솔리드.7z")), null, false);
            case "7z_password_made":
                return new Target(SevenZipTestArchives.SevenZip(MakeTree(t.Dir("src7p")), t.P("내용암호.7z"), password: FixtureCatalog.DefaultPassword),
                    FixtureCatalog.DefaultPassword, false);
            default:
            {
                var fx = FixtureCatalog.Get(id);
                Assert.True(fx.Exists, "픽스처 없음: " + fx.FullPath);
                string dest = t.P(Path.GetFileName(fx.FullPath));
                File.Copy(fx.FullPath, dest);
                File.SetAttributes(dest, FileAttributes.Normal);
                return new Target(dest, fx.Password, id.StartsWith("zip", StringComparison.Ordinal));
            }
        }
    }

    /// <summary>자료/{견적서.txt, 하위/표.csv, 그림.bin, 빈 폴더/} — 7z 시험 재료.</summary>
    internal static string MakeTree(string parent)
    {
        string root = Path.Combine(parent, "자료");
        Directory.CreateDirectory(Path.Combine(root, "하위"));
        Directory.CreateDirectory(Path.Combine(root, "빈 폴더"));
        File.WriteAllBytes(Path.Combine(root, "견적서.txt"), Text("견적 금액 1,000원\n", 500));
        File.WriteAllBytes(Path.Combine(root, "하위", "표.csv"), Text("가,나,다\n", 800));
        File.WriteAllBytes(Path.Combine(root, "그림.bin"), Random(20000, 7));
        return root;
    }

    internal static byte[] Text(string s, int repeat) => System.Text.Encoding.UTF8.GetBytes(string.Concat(Enumerable.Repeat(s, repeat)));

    internal static byte[] Random(int n, int seed)
    {
        var b = new byte[n];
        new Random(seed).NextBytes(b);
        return b;
    }

    // ───────────────────────────── 계획(고치기 + 기대) ─────────────────────────────

    internal sealed class Plan
    {
        public required List<EditOp> Ops { get; init; }
        public required SortedDictionary<string, string> ExpectedFiles { get; init; }
        public List<string> RequiredDirs { get; } = new();
        public List<string> GoneKeys { get; } = new();
        public HashSet<string> Affected { get; } = new(StringComparer.Ordinal);
        public List<string> NewKeys { get; } = new();
    }

    internal static Plan MakePlan(string op, TempDir t, IReadOnlyList<ArchiveEntry> before, SortedDictionary<string, string> beforeFiles)
    {
        var keys = before.Select(e => TreeAssert.NormalizeEntryPath(e.Path)).ToList();
        var fileKeys = before.Where(e => !e.IsDirectory).Select(e => TreeAssert.NormalizeEntryPath(e.Path)).OrderBy(k => k, StringComparer.Ordinal).ToList();
        var files = new SortedDictionary<string, string>(beforeFiles, StringComparer.Ordinal);
        switch (op)
        {
            case "add":
            {
                string src = t.Dir("넣을 것");
                string file = Path.Combine(src, "새 파일.txt");
                File.WriteAllBytes(file, Text("새로 넣은 내용 ", 400));
                string folder = Path.Combine(src, "추가 폴더");
                Directory.CreateDirectory(Path.Combine(folder, "빈"));
                File.WriteAllBytes(Path.Combine(folder, "안.txt"), Random(3000, 3));
                files["새 파일.txt"] = TreeAssert.Sha256(file);
                files["추가 폴더/안.txt"] = TreeAssert.Sha256(Path.Combine(folder, "안.txt"));
                var plan = new Plan { Ops = new() { new EditOp.Add(new[] { file, folder }, string.Empty) }, ExpectedFiles = files };
                plan.RequiredDirs.Add("추가 폴더/빈");
                plan.NewKeys.AddRange(new[] { "새 파일.txt", "추가 폴더/안.txt", "추가 폴더/빈" });
                return plan;
            }
            case "delete":
            {
                string victim = fileKeys.First(k => before.First(e => TreeAssert.NormalizeEntryPath(e.Path) == k).Size > 0);
                files.Remove(victim);
                var plan = new Plan { Ops = new() { new EditOp.Delete(new[] { victim }) }, ExpectedFiles = files };
                plan.GoneKeys.Add(victim);
                plan.Affected.Add(victim);
                return plan;
            }
            case "rename":
            {
                string? folder = keys.Where(k => k.Contains('/')).Select(k => k[..k.IndexOf('/')]).OrderBy(k => k, StringComparer.Ordinal).FirstOrDefault();
                string from, to;
                if (folder is not null)
                {
                    from = folder;
                    to = "바뀐 폴더";
                }
                else
                {
                    from = fileKeys[0];
                    to = "바뀐 이름" + Path.GetExtension(from);
                }
                string Move(string k) => k == from ? to : to + k[from.Length..];
                bool Under(string k) => k == from || k.StartsWith(from + "/", StringComparison.Ordinal);
                var moved = new SortedDictionary<string, string>(StringComparer.Ordinal);
                foreach (var (k, sha) in files) moved[Under(k) ? Move(k) : k] = sha;
                var plan = new Plan { Ops = new() { new EditOp.Rename(from, to) }, ExpectedFiles = moved };
                plan.GoneKeys.Add(from);
                foreach (var k in keys.Where(Under))
                {
                    plan.Affected.Add(k);
                    plan.NewKeys.Add(Move(k));
                }
                return plan;
            }
            case "newfolder":
            {
                var plan = new Plan { Ops = new() { new EditOp.NewFolder("새 폴더/안쪽") }, ExpectedFiles = files };
                plan.RequiredDirs.Add("새 폴더/안쪽");
                plan.NewKeys.Add("새 폴더/안쪽");
                return plan;
            }
            default:
                throw new ArgumentOutOfRangeException(nameof(op));
        }
    }

    /// <summary>풀어서 파일만(상대 경로 → SHA).</summary>
    internal static async Task<SortedDictionary<string, string>> ExtractFiles(string archive, string? password, string dest)
    {
        await Svc.For(archive).ExtractAsync(archive, dest, password);
        var snap = TreeAssert.Snapshot(dest);
        var files = new SortedDictionary<string, string>(StringComparer.Ordinal);
        foreach (var (k, v) in snap)
            if (v is not null) files[k] = v;
        return files;
    }

    /// <summary>폴더에 고치기의 임시 파일(.~이름…tmp/.bak)이나 임시 폴더(.일집-임시-…)가 남지 않았다.</summary>
    internal static void NoLeftovers(string folder)
    {
        var left = Directory.EnumerateFileSystemEntries(folder)
            .Select(Path.GetFileName)
            .Where(n => n!.StartsWith(".~", StringComparison.Ordinal) || n.StartsWith(StagingFolder.Prefix, StringComparison.Ordinal))
            .ToList();
        Assert.True(left.Count == 0, "남은 임시 파일: " + string.Join(", ", left));
    }

    // ───────────────────────────── 계약: 고치기 ─────────────────────────────

    /// <summary>계약: ZIP 고치기 — 목록·내용·손대지 않은 항목 바이트·새 이름 UTF-8·암호 방식 유지.</summary>
    [Theory]
    [MemberData(nameof(ZipCases))]
    public async Task ZIP_고치기(string targetId, string op)
    {
        using var t = new TempDir("ezip");
        var target = Prepare(targetId, t);
        var service = Svc.For(target.Path);
        var beforeList = await service.ListEntriesAsync(target.Path);
        var beforeFiles = await ExtractFiles(target.Path, target.Password, t.Dir("before"));
        var beforeRaw = ZipInspect.Read(target.Path);
        var beforeProbe = ZipPasswordProbe.Probe(target.Path, target.Password);
        var plan = MakePlan(op, t, beforeList, beforeFiles);

        var editor = ArchiveServiceLocator.EditorFor(target.Path);
        Assert.IsType<ZipArchiveEditor>(editor);
        var result = await editor!.EditAsync(target.Path, plan.Ops, target.Password, null, CancellationToken.None);

        await AssertCommon(target, plan, t);
        if (op == "add") Assert.Equal(2, result.AddedFiles);

        // 손대지 않은 항목은 바이트 그대로(이름 바이트·플래그·방식·CRC·시각·압축된 데이터)
        var afterRaw = ZipInspect.Read(target.Path);
        var afterPrints = afterRaw.Select(e => e.Fingerprint).ToHashSet(StringComparer.Ordinal);
        foreach (var e in beforeRaw.Where(e => !plan.Affected.Contains(TreeAssert.NormalizeEntryPath(e.Name))))
            Assert.True(afterPrints.Contains(e.Fingerprint), "손대지 않은 항목이 바뀜: " + e.Name);

        // 새로 쓴 이름은 UTF-8(bit 11), 로컬 머리 이름 = 중앙 디렉터리 이름
        foreach (var key in plan.NewKeys)
        {
            var e = afterRaw.Single(x => TreeAssert.NormalizeEntryPath(x.Name) == key);
            Assert.True(e.Utf8, "새 이름에 UTF-8 표시 없음: " + key);
            Assert.Equal(e.NameBytes, e.LocalNameBytes);
        }

        // 이름만 바꾼 항목은 이름 말고는 그대로(압축된 데이터·CRC·시각·방식·암호 여부·AES)
        if (op == "rename")
        {
            var rename = (EditOp.Rename)plan.Ops[0];
            foreach (var b in beforeRaw.Where(e => plan.Affected.Contains(TreeAssert.NormalizeEntryPath(e.Name))))
            {
                string oldKey = TreeAssert.NormalizeEntryPath(b.Name);
                string newKey = oldKey == rename.EntryKey ? rename.NewName : rename.NewName + oldKey[rename.EntryKey.Length..];
                var a = afterRaw.Single(x => TreeAssert.NormalizeEntryPath(x.Name) == newKey);
                Assert.Equal(TreeAssert.Sha256(b.Data), TreeAssert.Sha256(a.Data));
                Assert.Equal((b.Crc, b.DosTime, b.DosDate, b.Method, b.Encrypted, b.AesStrength, b.CompressedSize),
                             (a.Crc, a.DosTime, a.DosDate, a.Method, a.Encrypted, a.AesStrength, a.CompressedSize));
                Assert.Equal(b.Flags | 0x0800, a.Flags);
            }
        }

        // 암호 압축: 여전히 암호가 필요하고, 같은 방식·같은 암호 바이트
        if (beforeRaw.Any(e => e.Encrypted))
        {
            var afterProbe = ZipPasswordProbe.Probe(target.Path, target.Password);
            Assert.Equal(beforeProbe.Match, afterProbe.Match);
            Assert.Equal(beforeProbe.HasAes, afterProbe.HasAes);
            Assert.Equal(beforeProbe.HasZipCrypto, afterProbe.HasZipCrypto);
            var aes = beforeRaw.FirstOrDefault(e => e.Method == 99);
            foreach (var e in afterRaw.Where(e => op == "add" && !e.IsDir && plan.NewKeys.Contains(TreeAssert.NormalizeEntryPath(e.Name))))
            {
                Assert.True(e.Encrypted, "새 항목이 암호화되지 않음: " + e.Name);
                if (aes is not null)
                {
                    Assert.Equal(99, e.Method);
                    Assert.Equal(aes.AesStrength, e.AesStrength);
                    Assert.Equal(aes.AesVersion, e.AesVersion);
                }
                else
                {
                    Assert.NotEqual(99, e.Method);
                }
            }
            var ex = await Svc.Catch(() => service.ExtractAsync(target.Path, t.P("nopw"), null));
            Assert.IsAssignableFrom<ArchivePasswordException>(ex);
        }
        NoLeftovers(t.Root);
    }

    /// <summary>계약: 7z 고치기 — 목록·내용·암호·파일 이름 암호화·솔리드 유지.</summary>
    [Theory]
    [MemberData(nameof(SevenZipCases))]
    public async Task 칠z_고치기(string targetId, string op)
    {
        using var t = new TempDir("e7z");
        var target = Prepare(targetId, t);
        var service = Svc.For(target.Path);
        bool he = ArchiveHeaderProbe.IsSevenZipHeaderEncrypted(target.Path);
        bool solid = IsSolid(target.Path, target.Password);
        var beforeList = await service.ListEntriesAsync(target.Path, target.Password);
        var beforeFiles = await ExtractFiles(target.Path, target.Password, t.Dir("before"));
        var plan = MakePlan(op, t, beforeList, beforeFiles);

        var editor = ArchiveServiceLocator.EditorFor(target.Path);
        Assert.IsType<SevenZipArchiveEditor>(editor);
        var result = await editor!.EditAsync(target.Path, plan.Ops, target.Password, null, CancellationToken.None);

        await AssertCommon(target, plan, t);
        if (op == "add") Assert.Equal(2, result.AddedFiles);
        Assert.Equal(he, ArchiveHeaderProbe.IsSevenZipHeaderEncrypted(target.Path));
        if (solid) Assert.True(IsSolid(target.Path, target.Password), "솔리드가 풀림");
        if (target.Password is not null)
        {
            var ex = await Svc.Catch(() => service.ExtractAsync(target.Path, t.P("nopw"), null));
            Assert.IsAssignableFrom<ArchivePasswordException>(ex);
            using var x = new SharpSevenZipExtractor(target.Path, target.Password);
            Assert.All(x.ArchiveFileData.Where(f => !f.IsDirectory && f.Size > 0), f => Assert.True(f.Encrypted, "암호가 빠진 항목: " + f.FileName));
        }
        NoLeftovers(t.Root);
    }

    private static bool IsSolid(string path, string? password)
    {
        using var x = password is null ? new SharpSevenZipExtractor(path) : new SharpSevenZipExtractor(path, password);
        return x.IsSolid;
    }

    /// <summary>목록(파일 정확히·필요한 폴더 있음·없어질 이름 없음) + 풀린 내용 SHA.</summary>
    private static async Task AssertCommon(Target target, Plan plan, TempDir t)
    {
        var service = Svc.For(target.Path);
        var after = await service.ListEntriesAsync(target.Path, target.Password);
        var afterFiles = after.Where(e => !e.IsDirectory).Select(e => TreeAssert.NormalizeEntryPath(e.Path)).OrderBy(k => k, StringComparer.Ordinal).ToList();
        Assert.Equal(plan.ExpectedFiles.Keys.ToList(), afterFiles);
        var afterDirs = after.Where(e => e.IsDirectory).Select(e => TreeAssert.NormalizeEntryPath(e.Path)).ToHashSet(StringComparer.Ordinal);
        foreach (var d in plan.RequiredDirs) Assert.Contains(d, afterDirs);
        var allKeys = after.Select(e => TreeAssert.NormalizeEntryPath(e.Path)).ToHashSet(StringComparer.Ordinal);
        foreach (var g in plan.GoneKeys) Assert.DoesNotContain(g, allKeys);
        var extracted = await ExtractFiles(target.Path, target.Password, t.Dir("after"));
        Assert.Equal(plan.ExpectedFiles, extracted);
    }

    /// <summary>계약: 넣기·이름 바꾸기·지우기·새 폴더를 한 번에 고친 결과를 반디집(bz t)이 맞는 암호로 푼다.</summary>
    [BandizipCliTheory]
    [MemberData(nameof(AllTargets))]
    public async Task 고친_압축을_반디집이_푼다(string targetId)
    {
        using var t = new TempDir("ebz");
        var target = Prepare(targetId, t);
        var service = Svc.For(target.Path);
        var list = await service.ListEntriesAsync(target.Path, target.Password);
        string src = t.Dir("넣을 것");
        File.WriteAllBytes(Path.Combine(src, "추가.txt"), Text("추가 ", 100));
        string victim = list.Where(e => !e.IsDirectory && e.Size > 0).Select(e => TreeAssert.NormalizeEntryPath(e.Path)).OrderBy(k => k, StringComparer.Ordinal).Last();
        string renameFrom = list.Where(e => !e.IsDirectory && e.Size > 0).Select(e => TreeAssert.NormalizeEntryPath(e.Path)).OrderBy(k => k, StringComparer.Ordinal).First();
        var ops = new List<EditOp>
        {
            new EditOp.Add(new[] { Path.Combine(src, "추가.txt") }, string.Empty),
            new EditOp.Delete(new[] { victim }),
            new EditOp.NewFolder("반디집 시험 폴더"),
        };
        if (renameFrom != victim) ops.Add(new EditOp.Rename(renameFrom, "이름바꿈" + Path.GetExtension(renameFrom)));
        await ArchiveServiceLocator.EditorFor(target.Path)!.EditAsync(target.Path, ops, target.Password, null, CancellationToken.None);
        Assert.Equal(0, await BandizipCli.TestAsync(target.Path, target.Password ?? "x"));
    }

    // ───────────────────────────── 계약: 실패해도 원본 그대로 ─────────────────────────────

    private static Target PrepareFailureTarget(string kind, TempDir t) =>
        kind == "zip" ? Prepare("zip_zipcrypto", t) : Prepare("7z_password_made", t);

    private static List<EditOp> BigAdd(TempDir t)
    {
        string src = t.Dir("큰 원본");
        string file = Path.Combine(src, "큰 파일.bin");
        File.WriteAllBytes(file, Random(8 * 1024 * 1024, 11));
        return new List<EditOp> { new EditOp.Add(new[] { file }, string.Empty) };
    }

    /// <summary>계약: 진행 중에 취소하면 원본은 바이트 그대로, 임시 파일 없음.</summary>
    [Theory]
    [InlineData("zip")]
    [InlineData("7z")]
    public async Task 중간에_취소하면_원본이_그대로다(string kind)
    {
        using var t = new TempDir("ecancel");
        var target = PrepareFailureTarget(kind, t);
        string sha = TreeAssert.Sha256(target.Path);
        using var cts = new CancellationTokenSource();
        var progress = new ProgressRecorder(p => { if (p.BytesProcessed > 0) cts.Cancel(); });
        var ex = await Svc.Catch(() => ArchiveServiceLocator.EditorFor(target.Path)!.EditAsync(target.Path, BigAdd(t), target.Password, progress, cts.Token));
        Assert.IsAssignableFrom<OperationCanceledException>(ex);
        Assert.Equal(sha, TreeAssert.Sha256(target.Path));
        NoLeftovers(t.Root);
    }

    /// <summary>계약: 다른 프로그램이 원본을 쓰지 못하게 잡고 있으면 시작 전에 알리고 원본은 그대로.</summary>
    [Theory]
    [InlineData("zip")]
    [InlineData("7z")]
    public async Task 잠긴_원본은_고치지_않고_알린다(string kind)
    {
        using var t = new TempDir("elock");
        var target = PrepareFailureTarget(kind, t);
        string sha = TreeAssert.Sha256(target.Path);
        Exception? ex;
        using (new FileStream(target.Path, FileMode.Open, FileAccess.Read, FileShare.Read))
            ex = await Svc.Catch(() => ArchiveServiceLocator.EditorFor(target.Path)!.EditAsync(
                target.Path, new EditOp[] { new EditOp.NewFolder("새 폴더") }, target.Password, null, CancellationToken.None));
        var edit = Assert.IsType<ArchiveEditException>(ex);
        Assert.Equal(ArchiveSwap.LockedMessage, edit.Message);
        Assert.Equal(sha, TreeAssert.Sha256(target.Path));
        NoLeftovers(t.Root);
    }

    /// <summary>계약: 바꾸는 순간에 원본이 잠기면(검사 뒤) 바꾸지 않고 알린다 — 원본 그대로, 임시·예비 파일 없음.</summary>
    [Theory]
    [InlineData("zip")]
    [InlineData("7z")]
    public async Task 바꾸는_순간_잠기면_원본이_그대로다(string kind)
    {
        using var t = new TempDir("eswap");
        var target = PrepareFailureTarget(kind, t);
        string sha = TreeAssert.Sha256(target.Path);
        var tester = new LockingTester(target.Path);
        IArchiveEditor editor = kind == "zip" ? new ZipArchiveEditor(tester) : new SevenZipArchiveEditor(tester);
        Exception? ex;
        try
        {
            ex = await Svc.Catch(() => editor.EditAsync(target.Path, new EditOp[] { new EditOp.NewFolder("새 폴더") }, target.Password, null, CancellationToken.None));
        }
        finally
        {
            tester.Release();
        }
        var edit = Assert.IsType<ArchiveEditException>(ex);
        Assert.Equal(ArchiveSwap.LockedMessage, edit.Message);
        Assert.Equal(sha, TreeAssert.Sha256(target.Path));
        NoLeftovers(t.Root);
    }

    /// <summary>계약: 새 압축을 쓰다 실패(디스크 가득 등)하면 원본 그대로, 임시 파일 없음.</summary>
    [Theory]
    [InlineData("zip")]
    [InlineData("7z")]
    public async Task 쓰다_실패하면_원본이_그대로다(string kind)
    {
        using var t = new TempDir("ewrite");
        var target = PrepareFailureTarget(kind, t);
        string sha = TreeAssert.Sha256(target.Path);
        IArchiveEditor editor = kind == "zip"
            ? new ZipArchiveEditor { OutputWrapper = s => new FailingWriteStream(s, failAfter: 200) }
            : new SevenZipArchiveEditor { AfterPass = _ => throw new IOException("주입한 쓰기 실패") };
        string src = t.Dir("원본");
        File.WriteAllBytes(Path.Combine(src, "a.txt"), Text("a", 1000));
        var ex = await Svc.Catch(() => editor.EditAsync(target.Path,
            new EditOp[] { new EditOp.Add(new[] { Path.Combine(src, "a.txt") }, string.Empty) }, target.Password, null, CancellationToken.None));
        Assert.IsType<IOException>(ex);
        Assert.Equal(sha, TreeAssert.Sha256(target.Path));
        NoLeftovers(t.Root);
    }

    /// <summary>계약: 검사(고친 결과 시험)가 실패하면 원본 그대로, 임시 파일 없음.</summary>
    [Theory]
    [InlineData("zip")]
    [InlineData("7z")]
    public async Task 검사가_실패하면_원본이_그대로다(string kind)
    {
        using var t = new TempDir("etest");
        var target = PrepareFailureTarget(kind, t);
        string sha = TreeAssert.Sha256(target.Path);
        IArchiveEditor editor = kind == "zip" ? new ZipArchiveEditor(new FailingTester()) : new SevenZipArchiveEditor(new FailingTester());
        var ex = await Svc.Catch(() => editor.EditAsync(target.Path, new EditOp[] { new EditOp.NewFolder("새 폴더") }, target.Password, null, CancellationToken.None));
        var edit = Assert.IsType<ArchiveEditException>(ex);
        Assert.Equal(ArchiveTester.FailedMessage, edit.Message);
        Assert.Equal(sha, TreeAssert.Sha256(target.Path));
        NoLeftovers(t.Root);
    }

    /// <summary>계약: 진짜 검사기는 깨진 결과를 잡는다 — 목록이 계획과 다르면(예: 항목 하나가 빠짐) 실패.</summary>
    [Fact]
    public void 검사기는_목록이_계획과_다르면_실패한다()
    {
        using var t = new TempDir("etester");
        var target = Prepare("zip_default", t);
        var keys = ZipInspect.Read(target.Path).Select(e => TreeAssert.NormalizeEntryPath(e.Name)).ToList();
        ArchiveTester.Instance.Test(target.Path, EditableFormat.Zip, keys, null, CancellationToken.None);   // 그대로면 통과
        var ex = Assert.Throws<ArchiveEditException>(() =>
            ArchiveTester.Instance.Test(target.Path, EditableFormat.Zip, keys.Append("없는 항목.txt").ToList(), null, CancellationToken.None));
        Assert.Equal(ArchiveTester.FailedMessage, ex.Message);
        var bytes = File.ReadAllBytes(target.Path);
        var e0 = ZipInspect.Read(target.Path).First(e => !e.IsDir && e.CompressedSize > 4);
        int at = IndexOf(bytes, e0.Data);
        bytes[at + e0.Data.Length / 2] ^= 0x55;
        string broken = t.P("broken.zip");
        File.WriteAllBytes(broken, bytes);
        Assert.Throws<ArchiveEditException>(() => ArchiveTester.Instance.Test(broken, EditableFormat.Zip, keys, null, CancellationToken.None));
    }

    private static int IndexOf(byte[] haystack, byte[] needle)
    {
        for (int i = 0; i + needle.Length <= haystack.Length; i++)
            if (haystack.AsSpan(i, needle.Length).SequenceEqual(needle)) return i;
        throw new InvalidOperationException("못 찾음");
    }

    // ───────────────────────────── 계약: 고칠 수 없는 형식 ─────────────────────────────

    /// <summary>계약: ZIP·7z 가 아닌 형식(RAR·TAR·GZ·EGG·ALZ·ISO·CAB·자동 풀림 exe·문서)과 나뉜 압축은 편집기가 없다(화면은 '고칠 수 없어요').</summary>
    [Theory]
    [InlineData("a.rar")]
    [InlineData("a.tar")]
    [InlineData("a.gz")]
    [InlineData("a.tar.gz")]
    [InlineData("a.tgz")]
    [InlineData("a.egg")]
    [InlineData("a.alz")]
    [InlineData("a.iso")]
    [InlineData("a.cab")]
    [InlineData("a.xz")]
    [InlineData("a.zipx")]
    [InlineData("a.exe")]
    [InlineData("a.docx")]
    [InlineData("a.zip.001")]
    [InlineData("a.7z.001")]
    [InlineData("a.z01")]
    public void 고칠_수_없는_형식은_편집기가_없다(string name)
    {
        Assert.Null(ArchiveServiceLocator.EditorFor(Path.Combine(TempDir.BaseRoot, "없는폴더", name)));
        Assert.False(ArchiveServiceLocator.CanEdit(Path.Combine(TempDir.BaseRoot, "없는폴더", name)));
    }

    /// <summary>계약: 나뉜 ZIP(.z01 … .zip 의 마지막 조각)·숫자 분할은 픽스처로도 편집기가 없다.</summary>
    [Theory]
    [InlineData("zip_split")]
    [InlineData("7z_split")]
    [InlineData("zip_numbered_split")]
    public void 나뉜_압축은_고칠_수_없다(string id)
    {
        var fx = FixtureCatalog.Get(id);
        Assert.True(fx.Exists);
        Assert.Null(ArchiveServiceLocator.EditorFor(fx.FullPath));
    }

    /// <summary>계약: 앞에 다른 것이 붙은 ZIP(자동 풀림 exe 를 .zip 으로 바꾼 것 등)은 고치려 하면 '고칠 수 없어요'(원본 그대로).</summary>
    [Fact]
    public async Task 앞에_다른_것이_붙은_ZIP은_고칠_수_없다()
    {
        using var t = new TempDir("esfx");
        var target = Prepare("zip_default", t);
        string odd = t.P("붙음.zip");
        File.WriteAllBytes(odd, new byte[] { (byte)'M', (byte)'Z' }.Concat(new byte[200]).Concat(File.ReadAllBytes(target.Path)).ToArray());
        string sha = TreeAssert.Sha256(odd);
        var ex = await Svc.Catch(() => new ZipArchiveEditor().EditAsync(odd, new EditOp[] { new EditOp.NewFolder("x") }, null, null, CancellationToken.None));
        Assert.IsType<ArchiveNotEditableException>(ex);
        Assert.Equal(ArchiveNotEditableException.DefaultMessage, ErrorMessages.ToUserMessage(ex!));
        Assert.Equal(sha, TreeAssert.Sha256(odd));
        NoLeftovers(t.Root);
    }

    // ───────────────────────────── 계약: 암호 ─────────────────────────────

    /// <summary>계약: 암호 ZIP 에 넣을 때 암호가 없으면 암호를 묻게(PasswordRequired), 틀리면 WrongPassword — 원본 그대로.</summary>
    [Theory]
    [InlineData("zip_zipcrypto")]
    [InlineData("zip_aes256_korean_pw")]
    [InlineData("7z_password_made")]
    [InlineData("7z_header_encrypted")]
    public async Task 암호_압축은_암호가_필요하다(string id)
    {
        using var t = new TempDir("epw");
        var target = Prepare(id, t);
        string sha = TreeAssert.Sha256(target.Path);
        string src = t.Dir("원본");
        File.WriteAllBytes(Path.Combine(src, "a.txt"), Text("a", 10));
        var ops = new EditOp[] { new EditOp.Add(new[] { Path.Combine(src, "a.txt") }, string.Empty) };
        var editor = ArchiveServiceLocator.EditorFor(target.Path)!;
        var none = await Svc.Catch(() => editor.EditAsync(target.Path, ops, null, null, CancellationToken.None));
        Assert.IsAssignableFrom<PasswordRequiredException>(none);
        var wrong = await Svc.Catch(() => editor.EditAsync(target.Path, ops, "틀린암호123", null, CancellationToken.None));
        Assert.IsType<WrongPasswordException>(wrong);
        Assert.Equal(sha, TreeAssert.Sha256(target.Path));
        NoLeftovers(t.Root);
    }

    /// <summary>계약: 암호 ZIP 의 지우기·이름 바꾸기는 암호 없이도 된다(암호 항목은 바이트 그대로 옮김) — 그래도 여전히 암호가 필요하다.</summary>
    [Fact]
    public async Task 암호_ZIP_지우기는_암호_없이도_된다()
    {
        using var t = new TempDir("epwdel");
        var target = Prepare("zip_aes256", t);
        var list = await Svc.For(target.Path).ListEntriesAsync(target.Path);
        string victim = list.First(e => !e.IsDirectory).Path;
        await new ZipArchiveEditor().EditAsync(target.Path, new EditOp[] { new EditOp.Delete(new[] { victim }) }, null, null, CancellationToken.None);
        var after = await Svc.For(target.Path).ListEntriesAsync(target.Path);
        Assert.Equal(list.Count - 1, after.Count);
        var ex = await Svc.Catch(() => Svc.For(target.Path).ExtractAsync(target.Path, t.P("x"), null));
        Assert.IsAssignableFrom<ArchivePasswordException>(ex);
        await Svc.For(target.Path).ExtractAsync(target.Path, t.P("y"), target.Password);
    }

    // ───────────────────────────── 계약: 같은 이름·이름 규칙 ─────────────────────────────

    /// <summary>계약: 같은 이름이 있으면 범위가 없을 때 '이름 (1)' 로 넣는다(원래 파일은 그대로).</summary>
    [Fact]
    public async Task 같은_이름은_기본으로_비켜_넣는다()
    {
        using var t = new TempDir("ecol");
        string src = t.Dir("원본");
        File.WriteAllText(Path.Combine(src, "a.txt"), "원래");
        string arc = t.P("a.zip");
        await new ZipArchiveService().CompressAsync(new[] { Path.Combine(src, "a.txt") }, arc);
        string add = t.Dir("넣기");
        File.WriteAllText(Path.Combine(add, "a.txt"), "새것");
        await new ZipArchiveEditor().EditAsync(arc, new EditOp[] { new EditOp.Add(new[] { Path.Combine(add, "a.txt") }, string.Empty) }, null, null, CancellationToken.None);
        var files = await ExtractFiles(arc, null, t.Dir("out"));
        Assert.Equal(new[] { "a (1).txt", "a.txt" }, files.Keys.ToArray());
        Assert.Equal("원래", File.ReadAllText(t.P("out", "a.txt")));
        Assert.Equal("새것", File.ReadAllText(t.P("out", "a (1).txt")));
    }

    /// <summary>계약: 같은 이름 처리기(W2-05 범위)가 있으면 그 답대로 — 덮어쓰기는 바꿔 넣고, 건너뛰기는 넣지 않는다.</summary>
    [Theory]
    [InlineData("zip", CollisionChoice.Overwrite)]
    [InlineData("zip", CollisionChoice.Skip)]
    [InlineData("7z", CollisionChoice.Overwrite)]
    [InlineData("7z", CollisionChoice.Skip)]
    public async Task 같은_이름은_처리기에_묻는다(string kind, CollisionChoice choice)
    {
        using var t = new TempDir("ecol2");
        string src = t.Dir("원본", "자료");
        File.WriteAllText(Path.Combine(src, "a.txt"), "원래");
        File.WriteAllText(Path.Combine(src, "b.txt"), "그대로");
        string arc = kind == "zip" ? t.P("a.zip") : t.P("a.7z");
        if (kind == "zip") await new ZipArchiveService().CompressAsync(new[] { src }, arc);
        else SevenZipTestArchives.SevenZip(src, arc);
        string add = t.Dir("넣기");
        File.WriteAllText(Path.Combine(add, "a.txt"), "새것");
        var resolver = new RecordingResolver(choice);
        EditResult result;
        using (ExtractionScope.Begin(resolver))
            result = await ArchiveServiceLocator.EditorFor(arc)!.EditAsync(arc,
                new EditOp[] { new EditOp.Add(new[] { Path.Combine(add, "a.txt") }, "자료") }, null, null, CancellationToken.None);
        Assert.Single(resolver.Questions);
        Assert.Equal("자료/a.txt", resolver.Questions[0].TargetPath);
        var files = await ExtractFiles(arc, null, t.Dir("out"));
        Assert.Equal(new[] { "자료/a.txt", "자료/b.txt" }, files.Keys.ToArray());
        Assert.Equal(choice == CollisionChoice.Overwrite ? "새것" : "원래", File.ReadAllText(t.P("out", "자료", "a.txt")));
        Assert.Equal(choice == CollisionChoice.Skip ? 1 : 0, result.CollisionSkipped);
    }

    /// <summary>계약: 같은 이름 창의 [취소]는 고치기 전체 취소(원본 그대로).</summary>
    [Fact]
    public async Task 같은_이름_취소는_전체_취소다()
    {
        using var t = new TempDir("ecol3");
        string src = t.Dir("원본");
        File.WriteAllText(Path.Combine(src, "a.txt"), "원래");
        string arc = t.P("a.zip");
        await new ZipArchiveService().CompressAsync(new[] { Path.Combine(src, "a.txt") }, arc);
        string sha = TreeAssert.Sha256(arc);
        string add = t.Dir("넣기");
        File.WriteAllText(Path.Combine(add, "a.txt"), "새것");
        Exception? ex;
        using (ExtractionScope.Begin(new RecordingResolver(CollisionChoice.Cancel)))
            ex = await Svc.Catch(() => new ZipArchiveEditor().EditAsync(arc,
                new EditOp[] { new EditOp.Add(new[] { Path.Combine(add, "a.txt") }, string.Empty) }, null, null, CancellationToken.None));
        Assert.IsAssignableFrom<OperationCanceledException>(ex);
        Assert.Equal(sha, TreeAssert.Sha256(arc));
        NoLeftovers(t.Root);
    }

    /// <summary>계약: 이름 규칙 — 빈 이름·못 쓰는 글자·끝 점·이미 있는 이름은 고치지 않고 쉬운 문구로 알린다.</summary>
    [Theory]
    [InlineData("", "이름을 넣어 주세요.")]
    [InlineData("a/b", "이름에 쓸 수 없는 글자가 있어요: \\ / : * ? \" < > |")]
    [InlineData("a?.txt", "이름에 쓸 수 없는 글자가 있어요: \\ / : * ? \" < > |")]
    [InlineData("끝점.", "이름 끝에는 점(.)이나 빈칸을 쓸 수 없어요.")]
    [InlineData("B.TXT", "‘B.TXT’ 이름이 이미 있어요. 다른 이름을 골라 주세요.")]
    public async Task 이름_바꾸기_규칙(string newName, string message)
    {
        using var t = new TempDir("ename");
        string src = t.Dir("원본");
        File.WriteAllText(Path.Combine(src, "a.txt"), "a");
        File.WriteAllText(Path.Combine(src, "b.txt"), "b");
        string arc = t.P("a.zip");
        await new ZipArchiveService().CompressAsync(new[] { Path.Combine(src, "a.txt"), Path.Combine(src, "b.txt") }, arc);
        string sha = TreeAssert.Sha256(arc);
        var ex = await Svc.Catch(() => new ZipArchiveEditor().EditAsync(arc, new EditOp[] { new EditOp.Rename("a.txt", newName) }, null, null, CancellationToken.None));
        var edit = Assert.IsType<ArchiveEditException>(ex);
        Assert.Equal(message, edit.Message);
        Assert.Equal(sha, TreeAssert.Sha256(arc));
    }

    /// <summary>계약: 대소문자만 바꾸는 이름 바꾸기는 된다(자기 자신은 '이미 있음'이 아님).</summary>
    [Fact]
    public async Task 대소문자만_바꾸기는_된다()
    {
        using var t = new TempDir("ecase");
        string src = t.Dir("원본");
        File.WriteAllText(Path.Combine(src, "a.txt"), "a");
        string arc = t.P("a.zip");
        await new ZipArchiveService().CompressAsync(new[] { Path.Combine(src, "a.txt") }, arc);
        await new ZipArchiveEditor().EditAsync(arc, new EditOp[] { new EditOp.Rename("a.txt", "A.txt") }, null, null, CancellationToken.None);
        var list = await Svc.For(arc).ListEntriesAsync(arc);
        Assert.Equal("A.txt", Assert.Single(list).Path);
    }

    /// <summary>계약: 없는 항목을 지우면 '찾지 못했어요'(목록을 새로 읽으라는 안내) — 원본 그대로.</summary>
    [Fact]
    public async Task 없는_항목_지우기는_알린다()
    {
        using var t = new TempDir("enf");
        var target = Prepare("zip_default", t);
        string sha = TreeAssert.Sha256(target.Path);
        var ex = await Svc.Catch(() => new ZipArchiveEditor().EditAsync(target.Path, new EditOp[] { new EditOp.Delete(new[] { "없는/파일.txt" }) }, null, null, CancellationToken.None));
        var edit = Assert.IsType<ArchiveEditException>(ex);
        Assert.Contains("찾지 못했어요", edit.Message);
        Assert.Equal(sha, TreeAssert.Sha256(target.Path));
    }

    /// <summary>계약: 고친 ZIP 의 넣은 파일은 원래 압축의 수준을 따른다(저장만 ZIP 에 넣으면 저장만).</summary>
    [Fact]
    public async Task 저장만_ZIP에는_저장만으로_넣는다()
    {
        using var t = new TempDir("estore");
        var target = Prepare("zip_store_l0", t);
        string src = t.Dir("원본");
        File.WriteAllBytes(Path.Combine(src, "글.txt"), Text("반복 반복 ", 2000));
        await new ZipArchiveEditor().EditAsync(target.Path, new EditOp[] { new EditOp.Add(new[] { Path.Combine(src, "글.txt") }, string.Empty) }, null, null, CancellationToken.None);
        var added = ZipInspect.Read(target.Path).Single(e => e.Name == "글.txt");
        Assert.Equal(0, added.Method);
    }

    // ───────────────────────────── 시험 도구 ─────────────────────────────

    /// <summary>늘 실패하는 검사기.</summary>
    private sealed class FailingTester : IArchiveTester
    {
        public void Test(string archivePath, EditableFormat format, IReadOnlyCollection<string> expectedKeys, string? password, CancellationToken cancellationToken) =>
            throw new ArchiveEditException(ArchiveTester.FailedMessage, new InvalidDataException("가짜 검사 실패"));
    }

    /// <summary>진짜 검사를 한 뒤 원본을 잠근다(다른 프로그램이 바꾸는 순간에 잡은 것처럼). <see cref="Release"/> 로 푼다.</summary>
    private sealed class LockingTester : IArchiveTester
    {
        private readonly string _original;
        private FileStream? _hold;

        public LockingTester(string original) => _original = original;

        public void Test(string archivePath, EditableFormat format, IReadOnlyCollection<string> expectedKeys, string? password, CancellationToken cancellationToken)
        {
            ArchiveTester.Instance.Test(archivePath, format, expectedKeys, password, cancellationToken);
            _hold = new FileStream(_original, FileMode.Open, FileAccess.Read, FileShare.Read);
        }

        public void Release() => _hold?.Dispose();
    }

    /// <summary>정해진 바이트를 쓴 뒤 쓰기에 실패하는 스트림(디스크 가득 흉내).</summary>
    private sealed class FailingWriteStream : Stream
    {
        private readonly Stream _inner;
        private readonly long _failAfter;
        private long _written;

        public FailingWriteStream(Stream inner, long failAfter)
        {
            _inner = inner;
            _failAfter = failAfter;
        }

        public override bool CanRead => false;
        public override bool CanSeek => _inner.CanSeek;
        public override bool CanWrite => true;
        public override long Length => _inner.Length;
        public override long Position { get => _inner.Position; set => _inner.Position = value; }
        public override void Flush() => _inner.Flush();
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => _inner.Seek(offset, origin);
        public override void SetLength(long value) => _inner.SetLength(value);

        public override void Write(byte[] buffer, int offset, int count)
        {
            if (_written + count > _failAfter) throw new IOException("디스크 공간이 모자라요.") { HResult = unchecked((int)0x80070070) };
            _inner.Write(buffer, offset, count);
            _written += count;
        }

        public override void Write(ReadOnlySpan<byte> buffer)
        {
            if (_written + buffer.Length > _failAfter) throw new IOException("디스크 공간이 모자라요.") { HResult = unchecked((int)0x80070070) };
            _inner.Write(buffer);
            _written += buffer.Length;
        }
    }

    /// <summary>늘 같은 답을 하는 같은 이름 처리기(물은 것을 기록).</summary>
    private sealed class RecordingResolver : ICollisionResolver
    {
        private readonly CollisionChoice _choice;

        public RecordingResolver(CollisionChoice choice) => _choice = choice;

        public List<CollisionQuestion> Questions { get; } = new();

        public CollisionChoice Resolve(CollisionQuestion question)
        {
            lock (Questions) Questions.Add(question);
            return _choice;
        }
    }
}
