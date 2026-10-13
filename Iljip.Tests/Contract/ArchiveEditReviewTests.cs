using System.Diagnostics;
using Iljip.Models;
using Iljip.Services;
using Iljip.Services.Editing;
using Iljip.Tests.TestSupport;
using SharpSevenZip;
using Xunit.Abstractions;

namespace Iljip.Tests.Contract;

/// <summary>
/// [W2-06 검증 지적 수정] 압축 고치기(I2-3)의 빈틈:
/// 반디집 ZIP 의 유니코드 경로(0x7075)와 목록 키가 어긋남 · 파일과 같은 이름의 폴더를 넣으면 둘 다 생김 · 원래부터 손상된 압축을 '고치다 생긴 문제'로 알림 ·
/// 읽은 뒤 바꾸기 전에 원본이 바뀌어도 덮어씀(잃어버린 갱신) · 다시 넣기의 '바꿔 넣기'(항목이 없으면 새로).
/// 7z 고치기의 진행·취소는 시간에 민감해 따로(<see cref="SevenZipEditProgressTests"/>).
/// </summary>
public class ArchiveEditReviewTests
{
    // ───────────────────────────── 유니코드 경로(0x7075) ─────────────────────────────

    /// <summary>
    /// 계약: 반디집 기본 ZIP(CP949 이름 + 0x7075 NFD UTF-8 이름 — 맥 이름처럼 CP949 로 못 적는 이름은 둘이 다름)도 목록에 보이는 이름으로
    /// 지우기·이름 바꾸기·같은 이름 넣기(묻고 덮어쓰기)·바꿔 넣기가 된다. 손대지 않은 항목은 바이트 그대로, 반디집이 푼다.
    /// (검증 지적: 키를 원시 이름으로만 만들어 '찾지 못했어요'·같은 이름을 묻지 않고 두 개가 됨)
    /// </summary>
    [Theory]
    [InlineData("delete")]
    [InlineData("rename")]
    [InlineData("add-same")]
    [InlineData("replace")]
    public async Task 유니코드_경로가_있는_반디집_ZIP도_목록_이름으로_고친다(string op)
    {
        using var t = new TempDir("eunip");
        var target = ArchiveEditTests.Prepare("nfd_bandizip_default", t);
        var service = Svc.For(target.Path);
        var before = (await service.ListEntriesAsync(target.Path)).Select(e => TreeAssert.NormalizeEntryPath(e.Path)).ToList();
        var beforeRaw = ZipInspect.Read(target.Path);
        string english = before.Single(k => k.EndsWith("/english.txt", StringComparison.Ordinal));
        string folder = EditPlanner<object>.ParentOf(english);
        // 재료 확인: 목록 이름(0x7075, NFD)과 원시 CP949 이름(호환 자모)이 다르다 — 지적을 재현하는 재료
        Assert.DoesNotContain(english, beforeRaw.Select(e => TreeAssert.NormalizeEntryPath(e.Name)));

        string src = t.Dir("넣을 것");
        string newFile = Path.Combine(src, "english.txt");
        File.WriteAllText(newFile, "새 영어 내용");
        var resolver = new AlwaysResolver(CollisionChoice.Overwrite);
        EditOp edit = op switch
        {
            "delete" => new EditOp.Delete(new[] { english }),
            "rename" => new EditOp.Rename(english, "영어.txt"),
            "add-same" => new EditOp.Add(new[] { newFile }, folder),
            _ => new EditOp.Add(new[] { newFile }, folder, AsName: "english.txt", ReplaceExisting: true),
        };
        using (ExtractionScope.Begin(resolver))
            await new ZipArchiveEditor().EditAsync(target.Path, new[] { edit }, null, null, CancellationToken.None);

        var after = (await service.ListEntriesAsync(target.Path)).Select(e => TreeAssert.NormalizeEntryPath(e.Path)).ToList();
        var expected = op switch
        {
            "delete" => before.Where(k => k != english).ToList(),
            "rename" => before.Select(k => k == english ? folder + "/영어.txt" : k).ToList(),
            _ => before.ToList(),
        };
        Assert.Equal(expected.OrderBy(k => k, StringComparer.Ordinal), after.OrderBy(k => k, StringComparer.Ordinal));
        Assert.Equal(op == "add-same" ? 1 : 0, resolver.Questions.Count);
        if (op == "add-same") Assert.Equal(english, resolver.Questions[0].TargetPath);

        // 손대지 않은 항목은 바이트 그대로(0x7075 포함)
        var afterPrints = ZipInspect.Read(target.Path).Select(e => e.Fingerprint).ToHashSet(StringComparer.Ordinal);
        foreach (var e in beforeRaw.Where(e => !e.Name.EndsWith("english.txt", StringComparison.Ordinal)))
            Assert.True(afterPrints.Contains(e.Fingerprint), "손대지 않은 항목이 바뀜: " + e.Name);

        // 풀린 파일이 목록의 파일과 같고(NFC 로 견줌), 넣은 내용이 그 자리에
        static string Nfc(string s) => s.Normalize(System.Text.NormalizationForm.FormC);
        var listedFiles = (await service.ListEntriesAsync(target.Path)).Where(e => !e.IsDirectory)
            .Select(e => Nfc(TreeAssert.NormalizeEntryPath(e.Path))).OrderBy(k => k, StringComparer.Ordinal).ToList();
        var files = await ArchiveEditTests.ExtractFiles(target.Path, null, t.Dir("out"));
        Assert.Equal(listedFiles, files.Keys.Select(Nfc).OrderBy(k => k, StringComparer.Ordinal).ToList());
        if (op is "add-same" or "replace")
            Assert.Equal(TreeAssert.Sha256(newFile), files.Single(kv => Nfc(kv.Key) == Nfc(english)).Value);
        ArchiveEditTests.NoLeftovers(t.Root);
    }

    /// <summary>계약(반디집 실물): 유니코드 경로 ZIP 을 이름 바꾸기·지우기 한 결과를 반디집(bz t)이 푼다.</summary>
    [BandizipCliFact]
    public async Task 유니코드_경로_ZIP을_고친_결과를_반디집이_푼다()
    {
        using var t = new TempDir("eunipbz");
        var target = ArchiveEditTests.Prepare("nfd_bandizip_default", t);
        var list = (await Svc.For(target.Path).ListEntriesAsync(target.Path)).Select(e => TreeAssert.NormalizeEntryPath(e.Path)).ToList();
        string english = list.Single(k => k.EndsWith("/english.txt", StringComparison.Ordinal));
        string other = list.First(k => k.EndsWith(".txt", StringComparison.Ordinal) && k != english);
        await new ZipArchiveEditor().EditAsync(target.Path,
            new EditOp[] { new EditOp.Delete(new[] { english }), new EditOp.Rename(other, "이름바꿈.txt") }, null, null, CancellationToken.None);
        Assert.Equal(0, await BandizipCli.TestAsync(target.Path, "x"));
    }

    // ───────────────────────────── 파일과 같은 이름의 폴더 ─────────────────────────────

    /// <summary>
    /// 계약: 파일 '보고서' 가 있는 자리에 폴더 '보고서'(안.txt·빈 하위 폴더)를 넣으면 폴더째 '보고서 (1)' 로 비켜 넣는다 — 같은 이름의 파일과 폴더가
    /// 함께 생기지 않고(Windows 에 풀 수 없음 — 검증 지적), 원래 파일은 그대로, 풀기가 된다.
    /// </summary>
    [Theory]
    [InlineData("zip")]
    [InlineData("7z")]
    public async Task 파일과_같은_이름의_폴더를_넣으면_폴더째_비켜_넣는다(string kind)
    {
        using var t = new TempDir("efold");
        string root = t.Dir("원본", "자료");
        File.WriteAllText(Path.Combine(root, "보고서"), "원래 파일");
        string arc = kind == "zip" ? t.P("a.zip") : t.P("a.7z");
        if (kind == "zip") await new ZipArchiveService().CompressAsync(new[] { root }, arc);
        else SevenZipTestArchives.SevenZip(root, arc);
        string add = t.Dir("넣기", "보고서");
        File.WriteAllText(Path.Combine(add, "안.txt"), "폴더 안");
        Directory.CreateDirectory(Path.Combine(add, "빈"));

        await ArchiveServiceLocator.EditorFor(arc)!.EditAsync(arc, new EditOp[] { new EditOp.Add(new[] { add }, "자료") }, null, null, CancellationToken.None);

        var list = await Svc.For(arc).ListEntriesAsync(arc);
        var files = list.Where(e => !e.IsDirectory).Select(e => TreeAssert.NormalizeEntryPath(e.Path)).OrderBy(k => k, StringComparer.Ordinal).ToArray();
        Assert.Equal(new[] { "자료/보고서", "자료/보고서 (1)/안.txt" }, files);
        Assert.Contains(list, e => e.IsDirectory && TreeAssert.NormalizeEntryPath(e.Path) == "자료/보고서 (1)/빈");
        Assert.DoesNotContain(list, e => TreeAssert.NormalizeEntryPath(e.Path).StartsWith("자료/보고서/", StringComparison.Ordinal));
        var extracted = await ArchiveEditTests.ExtractFiles(arc, null, t.Dir("out"));
        Assert.Equal(files, extracted.Keys.ToArray());
        Assert.Equal("원래 파일", File.ReadAllText(t.P("out", "자료", "보고서")));
        Assert.Equal("폴더 안", File.ReadAllText(t.P("out", "자료", "보고서 (1)", "안.txt")));
    }

    /// <summary>같은 넣기 안에서 같은 폴더의 파일들은 모두 같은 '이름 (1)' 로, 빈 폴더 자신이 파일과 이름이 같아도 비켜 넣는다(계획만).</summary>
    [Fact]
    public void 비켜_넣기는_한_넣기_안에서_같은_이름으로_한다()
    {
        var planner = new EditPlanner<int>(new[]
        {
            new EditPlanner<int>.Item { Source = 0, OriginalKey = "보고서", IsDirectory = false, Size = 1 },
            new EditPlanner<int>.Item { Source = 1, OriginalKey = "빈", IsDirectory = false, Size = 1 },
        });
        var moved = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        Assert.Equal("보고서 (1)/가.txt", planner.AvoidFiles("", "보고서/가.txt", lastIsFolder: false, moved));
        planner.AddFile("a", "1", "보고서 (1)/가.txt", 1, null, 1, null);
        Assert.Equal("보고서 (1)/하위/나.txt", planner.AvoidFiles("", "보고서/하위/나.txt", lastIsFolder: false, moved));
        Assert.Equal("빈 (1)", planner.AvoidFiles("", "빈", lastIsFolder: true, moved));
        Assert.Equal("보고서", planner.AvoidFiles("", "보고서", lastIsFolder: false, moved));   // 파일 자신은 같은 이름 질문으로(여기서는 그대로)
        Assert.Equal("다른/가.txt", planner.AvoidFiles("", "다른/가.txt", lastIsFolder: false, moved));
    }

    // ───────────────────────────── 원래부터 손상 ─────────────────────────────

    /// <summary>
    /// 계약: 원래 ZIP 의 압축된 데이터가 이미 손상됐으면 고치기(새 폴더)는 '원래 압축 파일에 이미 손상된 곳이 있어서…(고치다 생긴 문제가 아니에요)'로 알리고
    /// 원본은 그대로, 남는 파일 없음(검증 지적: '고친 압축 파일을 검사했더니…'로 고치기 탓처럼 보임). 원래가 멀쩡하면 예전 문구 그대로(ArchiveEditTests).
    /// </summary>
    [Fact]
    public async Task 원래부터_손상된_ZIP을_고치면_원래부터_손상이라고_알린다()
    {
        using var t = new TempDir("edmg");
        var target = ArchiveEditTests.Prepare("zip_default", t);
        var bytes = File.ReadAllBytes(target.Path);
        var e0 = ZipInspect.Read(target.Path).First(e => !e.IsDir && e.CompressedSize > 4);
        int at = IndexOf(bytes, e0.Data);
        bytes[at + e0.Data.Length / 2] ^= 0x55;
        File.WriteAllBytes(target.Path, bytes);
        string sha = TreeAssert.Sha256(target.Path);

        var ex = await Svc.Catch(() => new ZipArchiveEditor().EditAsync(target.Path, new EditOp[] { new EditOp.NewFolder("새 폴더") }, null, null, CancellationToken.None));

        var edit = Assert.IsType<ArchiveEditException>(ex);
        Assert.Equal(ArchiveTester.OriginalDamagedMessage, edit.Message);
        Assert.DoesNotContain("압축 파일이 아닌", edit.Message);   // 재사용장치 분류 말 금지
        Assert.Equal(sha, TreeAssert.Sha256(target.Path));
        ArchiveEditTests.NoLeftovers(t.Root);
    }

    private static int IndexOf(byte[] haystack, byte[] needle)
    {
        for (int i = 0; i + needle.Length <= haystack.Length; i++)
            if (haystack.AsSpan(i, needle.Length).SequenceEqual(needle)) return i;
        throw new InvalidOperationException("못 찾음");
    }

    // ───────────────────────────── 잃어버린 갱신 ─────────────────────────────

    /// <summary>
    /// 계약: 고치는 사이(읽은 뒤 바꾸기 전)에 다른 프로그램이 원본을 바꾸면 고친 것을 넣지 않고 '고치는 동안 다른 곳에서 이 압축 파일이 바뀌어서…' —
    /// 다른 프로그램이 쓴 내용이 그대로 남고 임시 파일도 없다(검증 지적: 말없이 덮어씀).
    /// </summary>
    [Theory]
    [InlineData("zip")]
    [InlineData("7z")]
    public async Task 고치는_사이에_원본이_바뀌면_덮어쓰지_않고_알린다(string kind)
    {
        using var t = new TempDir("elost");
        var target = ArchiveEditTests.Prepare(kind == "zip" ? "zip_default" : "7z_default", t);
        var other = System.Text.Encoding.UTF8.GetBytes("다른 프로그램이 그사이에 쓴 내용");
        var tester = new ChangingTester(target.Path, other);
        IArchiveEditor editor = kind == "zip" ? new ZipArchiveEditor(tester) : new SevenZipArchiveEditor(tester);

        var ex = await Svc.Catch(() => editor.EditAsync(target.Path, new EditOp[] { new EditOp.NewFolder("새 폴더") }, null, null, CancellationToken.None));

        var edit = Assert.IsType<ArchiveEditException>(ex);
        Assert.Equal(ArchiveSwap.ChangedMessage, edit.Message);
        Assert.Equal(other, File.ReadAllBytes(target.Path));
        ArchiveEditTests.NoLeftovers(t.Root);
    }

    /// <summary>
    /// 계약: 같은 ZIP 을 두 고치기가 동시에 고쳐도, '됐다'고 돌아온 고치기의 내용은 마지막 압축에 남아 있다(하나가 다른 하나를 말없이 덮어쓰지 않음 —
    /// 겹친 쪽은 '바뀌어서'·'다른 프로그램이 쓰고 있어서'로 실패). 압축은 풀린다.
    /// </summary>
    [Fact]
    public async Task 동시에_고쳐도_된다고_한_고치기는_남는다()
    {
        using var t = new TempDir("econc");
        var target = ArchiveEditTests.Prepare("zip_default", t);
        string src = t.Dir("넣을 것");
        var names = new[] { "하나.txt", "둘.txt", "셋.txt" };
        foreach (var n in names) File.WriteAllBytes(Path.Combine(src, n), ArchiveEditTests.Random(400_000, n.Length));
        var tasks = names.Select(n => Svc.Catch(() => new ZipArchiveEditor().EditAsync(target.Path,
            new EditOp[] { new EditOp.Add(new[] { Path.Combine(src, n) }, string.Empty) }, null, null, CancellationToken.None))).ToList();
        var errors = await Task.WhenAll(tasks);

        var after = (await Svc.For(target.Path).ListEntriesAsync(target.Path)).Select(e => TreeAssert.NormalizeEntryPath(e.Path)).ToHashSet(StringComparer.Ordinal);
        for (int i = 0; i < names.Length; i++)
        {
            if (errors[i] is null) Assert.Contains(names[i], after);
            else Assert.Contains(Assert.IsType<ArchiveEditException>(errors[i]).Message, new[] { ArchiveSwap.ChangedMessage, ArchiveSwap.LockedMessage });
        }
        Assert.Contains(errors, e => e is null);
        await ArchiveEditTests.ExtractFiles(target.Path, null, t.Dir("out"));
        ArchiveEditTests.NoLeftovers(t.Root);
    }

    // ───────────────────────────── 바꿔 넣기(다시 넣기) ─────────────────────────────

    /// <summary>
    /// 계약(I3-3 다시 넣기의 바탕): 'ReplaceExisting' 넣기는 같은 이름 파일을 묻지 않고 바꿔 넣고, 그 항목이 없으면(연 뒤에 압축에서 지움) 원래 자리에 새로 넣는다.
    /// 같은 이름의 '폴더'는 지우지 않고 파일을 비켜 넣는다.
    /// </summary>
    [Theory]
    [InlineData("zip")]
    [InlineData("7z")]
    public async Task 바꿔_넣기는_묻지_않고_바꾸고_없으면_새로_넣는다(string kind)
    {
        using var t = new TempDir("erepl");
        string root = t.Dir("원본", "자료");
        File.WriteAllText(Path.Combine(root, "보고서.txt"), "처음");
        File.WriteAllText(Path.Combine(root, "표.csv"), "가,나");
        Directory.CreateDirectory(Path.Combine(root, "폴더.txt"));
        File.WriteAllText(Path.Combine(root, "폴더.txt", "안.txt"), "안");
        string arc = kind == "zip" ? t.P("a.zip") : t.P("a.7z");
        if (kind == "zip") await new ZipArchiveService().CompressAsync(new[] { root }, arc);
        else SevenZipTestArchives.SevenZip(root, arc);
        var editor = ArchiveServiceLocator.EditorFor(arc)!;
        string snap = t.Dir("사본");
        File.WriteAllText(Path.Combine(snap, "아무 이름.tmp"), "고친 내용");
        var resolver = new AlwaysResolver(CollisionChoice.Skip);   // 물으면 건너뛰기 — 묻지 않아야 바꿔 넣힘

        using (ExtractionScope.Begin(resolver))
            await editor.EditAsync(arc, new EditOp[] { new EditOp.Add(new[] { Path.Combine(snap, "아무 이름.tmp") }, "자료", AsName: "보고서.txt", ReplaceExisting: true) },
                null, null, CancellationToken.None);
        Assert.Empty(resolver.Questions);
        var three = Sorted("자료/보고서.txt", "자료/표.csv", "자료/폴더.txt/안.txt");
        var files = await ArchiveEditTests.ExtractFiles(arc, null, t.Dir("o1"));
        Assert.Equal(three, files.Keys.ToArray());
        Assert.Equal("고친 내용", File.ReadAllText(t.P("o1", "자료", "보고서.txt")));

        // 연 뒤에 압축에서 지웠다 → 원래 자리에 새로
        await editor.EditAsync(arc, new EditOp[] { new EditOp.Delete(new[] { "자료/보고서.txt" }) }, null, null, CancellationToken.None);
        File.WriteAllText(Path.Combine(snap, "아무 이름.tmp"), "또 고친 내용");
        await editor.EditAsync(arc, new EditOp[] { new EditOp.Add(new[] { Path.Combine(snap, "아무 이름.tmp") }, "자료", AsName: "보고서.txt", ReplaceExisting: true) },
            null, null, CancellationToken.None);
        files = await ArchiveEditTests.ExtractFiles(arc, null, t.Dir("o2"));
        Assert.Equal(three, files.Keys.ToArray());
        Assert.Equal("또 고친 내용", File.ReadAllText(t.P("o2", "자료", "보고서.txt")));

        // 같은 이름이 '폴더'면 폴더는 그대로 두고 비켜 넣는다
        await editor.EditAsync(arc, new EditOp[] { new EditOp.Add(new[] { Path.Combine(snap, "아무 이름.tmp") }, "자료", AsName: "폴더.txt", ReplaceExisting: true) },
            null, null, CancellationToken.None);
        files = await ArchiveEditTests.ExtractFiles(arc, null, t.Dir("o3"));
        Assert.Equal(Sorted("자료/보고서.txt", "자료/폴더 (1).txt", "자료/폴더.txt/안.txt", "자료/표.csv"), files.Keys.ToArray());
        Assert.Equal("안", File.ReadAllText(t.P("o3", "자료", "폴더.txt", "안.txt")));
    }

    private static string[] Sorted(params string[] keys) => keys.OrderBy(k => k, StringComparer.Ordinal).ToArray();

    // ───────────────────────────── 시험 도구 ─────────────────────────────

    /// <summary>진짜 검사를 한 뒤 원본을 다른 내용으로 바꾼다(다른 프로그램이 그 사이에 쓴 것처럼 — 수정 시각도 1분 뒤로).</summary>
    private sealed class ChangingTester : IArchiveTester
    {
        private readonly string _original;
        private readonly byte[] _other;

        public ChangingTester(string original, byte[] other)
        {
            _original = original;
            _other = other;
        }

        public void Test(string archivePath, EditableFormat format, IReadOnlyCollection<string> expectedKeys, string? password, CancellationToken cancellationToken)
        {
            ArchiveTester.Instance.Test(archivePath, format, expectedKeys, password, cancellationToken);
            File.WriteAllBytes(_original, _other);
            File.SetLastWriteTimeUtc(_original, DateTime.UtcNow.AddMinutes(1));
        }
    }

    /// <summary>늘 같은 답을 하는 같은 이름 처리기(물은 것을 기록).</summary>
    internal sealed class AlwaysResolver : ICollisionResolver
    {
        private readonly CollisionChoice _choice;

        public AlwaysResolver(CollisionChoice choice) => _choice = choice;

        public List<CollisionQuestion> Questions { get; } = new();

        public CollisionChoice Resolve(CollisionQuestion question)
        {
            lock (Questions) Questions.Add(question);
            return _choice;
        }
    }
}

/// <summary>
/// 7z 고치기의 진행·취소(시간을 잼) — 다른 시험과 동시에 돌리지 않는다(CPU 를 나눠 쓰면 시간이 흔들림).
/// 재료: 솔리드 7z 16MB(4비트 무작위 — 다시 압축이 느린 경우, 빠름 수준으로 만들어 두고 고치기는 보통 수준으로 다시 압축).
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class SevenZipEditTimingCollection : ICollectionFixture<SevenZipEditSource>
{
    public const string Name = "7z 고치기 시간(따로 실행)";
}

/// <summary>솔리드 7z 재료(한 번 만들어 시험마다 사본).</summary>
public sealed class SevenZipEditSource : IDisposable
{
    public const int Files = 8;
    public const int FileMiB = 2;

    public TempDir Tmp { get; } = new("7zedit");
    public string Archive { get; }

    public SevenZipEditSource()
    {
        string root = Tmp.Dir("원본", "자료");
        var rnd = new Random(20261013);
        var buf = new byte[1 << 20];
        for (int i = 0; i < Files; i++)
        {
            using var f = File.Create(Path.Combine(root, $"조각 {i}.bin"));
            for (int m = 0; m < FileMiB; m++)
            {
                rnd.NextBytes(buf);
                for (int k = 0; k < buf.Length; k++) buf[k] &= 0x0F;
                f.Write(buf);
            }
        }
        SevenZipLibrary.EnsureLoaded();
        Assert.True(SevenZipLibrary.IsAvailable, "7z.dll 을 불러오지 못해 시험 재료를 만들 수 없음");
        Archive = Tmp.P("솔리드.7z");
        var c = new SharpSevenZipCompressor(Tmp.Dir("만들기 임시"))
        {
            ArchiveFormat = OutArchiveFormat.SevenZip,
            CompressionLevel = CompressionLevel.Fast,
        };
        c.CustomParameters["s"] = "on";
        c.CustomParameters["mt"] = "4";
        string parent = Path.GetDirectoryName(root)!;
        c.CompressFiles(Archive, parent.Length + 1, Directory.GetFiles(root));
    }

    /// <summary>시험 하나가 쓸 사본(그 시험의 임시 폴더 안).</summary>
    public string CopyTo(TempDir t)
    {
        string p = t.P("솔리드.7z");
        File.Copy(Archive, p);
        return p;
    }

    public void Dispose() => Tmp.Dispose();
}

/// <summary>
/// [W2-06 검증 지적 수정] 솔리드 7z 에서 하나만 지워도 묶음 전체를 다시 압축한다(200MB 에 100초) — 그동안 진행 막대가 움직이고, [취소]가 3초 안에 먹혀야 한다
/// (W2-02 7z 만들기와 같은 기준). 검증 지적: 진행 0/0 '다시 정리하는 중'만, 취소를 1초에 눌러도 101초 뒤에야 멈춤.
/// </summary>
[Collection(SevenZipEditTimingCollection.Name)]
public class SevenZipEditProgressTests
{
    private static readonly TimeSpan CancelLimit = TimeSpan.FromSeconds(3);

    private readonly SevenZipEditSource _src;
    private readonly ITestOutputHelper _out;

    public SevenZipEditProgressTests(SevenZipEditSource src, ITestOutputHelper output)
    {
        _src = src;
        _out = output;
    }

    /// <summary>
    /// 계약: 솔리드 7z 지우기 — 진행률은 줄지 않고, 총량이 처음부터 0 이 아니며, '다시 정리하는 중'(다시 압축) 동안 중간 값이 여럿 나오고, 검사 단계를 거쳐 끝은 100%.
    /// 결과는 맞게 지워졌고 솔리드 그대로.
    /// </summary>
    [Fact]
    public async Task 솔리드_7z_지우기는_다시_압축하는_동안_진행률이_오른다()
    {
        using var t = new TempDir("e7zprog");
        string arc = _src.CopyTo(t);
        long size = new FileInfo(arc).Length;
        var times = new List<long>();
        var sw = Stopwatch.StartNew();
        var rec = new ProgressRecorder(_ => { lock (times) times.Add(sw.ElapsedMilliseconds); });

        await new SevenZipArchiveEditor().EditAsync(arc, new EditOp[] { new EditOp.Delete(new[] { "자료/조각 3.bin" }) }, null, rec, CancellationToken.None);
        sw.Stop();

        var reports = rec.Reports.ToList();
        Assert.NotEmpty(reports);
        Assert.All(reports, r => Assert.True(r.TotalBytes > 0, "총량이 0 인 보고"));
        for (int i = 1; i < reports.Count; i++)
            Assert.True(reports[i].BytesProcessed >= reports[i - 1].BytesProcessed, $"진행률이 줄어듦: {reports[i - 1].BytesProcessed} → {reports[i].BytesProcessed}");
        Assert.Equal(reports[^1].TotalBytes, reports[^1].BytesProcessed);
        // 다시 압축하는 동안(사본 복사 = 압축 크기만큼 뒤, 지우기 단계 = 그다음 압축 크기만큼)의 중간 값
        var during = reports.Where(r => r.CurrentFile == EditSupport.StageRewriting && r.BytesProcessed > size && r.BytesProcessed < 2 * size)
            .Select(r => r.BytesProcessed).Distinct().Count();
        _out.WriteLine($"기록_7z_솔리드_지우기: 압축 {size / 1048576.0:F1}MB, {sw.Elapsed.TotalSeconds:F1}초, 보고 {reports.Count}개, 다시 압축 중 서로 다른 값 {during}개");
        Assert.True(during >= 3, $"다시 압축하는 동안 진행 값이 {during}개뿐");
        Assert.Contains(reports, r => r.CurrentFile == EditSupport.StageTesting);

        using var x = new SharpSevenZipExtractor(arc);
        Assert.True(x.IsSolid);
        var names = x.ArchiveFileData.Where(f => !f.IsDirectory).Select(f => TreeAssert.NormalizeEntryPath(f.FileName)).ToList();
        Assert.Equal(SevenZipEditSource.Files - 1, names.Count);
        Assert.DoesNotContain("자료/조각 3.bin", names);
        ArchiveEditTests.NoLeftovers(t.Root);
    }

    /// <summary>
    /// 계약: 솔리드 7z 지우기를 다시 압축하는 도중(결과가 쓰이기 시작한 뒤) 취소 → 3초 안에 <see cref="OperationCanceledException"/>,
    /// 원본은 바이트 그대로, 임시 파일·임시 폴더 없음.
    /// </summary>
    [Fact]
    public async Task 솔리드_7z_지우기를_도중에_취소하면_3초_안에_멈추고_원본이_그대로다()
    {
        using var t = new TempDir("e7zcancel");
        string arc = _src.CopyTo(t);
        long size = new FileInfo(arc).Length;
        string sha = TreeAssert.Sha256(arc);
        using var cts = new CancellationTokenSource();
        var cancelledAt = new Stopwatch();
        long cancelValue = -1;
        var rec = new ProgressRecorder(p =>
        {
            if (p.CurrentFile == EditSupport.StageRewriting && p.BytesProcessed > size && !cts.IsCancellationRequested)
            {
                Interlocked.Exchange(ref cancelValue, p.BytesProcessed);
                cancelledAt.Start();
                cts.Cancel();
            }
        });

        var ex = await Svc.Catch(() => new SevenZipArchiveEditor().EditAsync(arc, new EditOp[] { new EditOp.Delete(new[] { "자료/조각 3.bin" }) }, null, rec, cts.Token));
        cancelledAt.Stop();

        _out.WriteLine($"기록_7z_솔리드_지우기_취소: 지우기 단계 {(cancelValue - size) * 100.0 / size:F0}% 에서 취소 → {cancelledAt.Elapsed.TotalMilliseconds:F0}ms 뒤 멈춤");
        Assert.True(cts.IsCancellationRequested, "다시 압축하는 동안의 진행 보고가 없어 취소하지 못함");
        Assert.IsAssignableFrom<OperationCanceledException>(ex);
        Assert.True(cancelledAt.Elapsed < CancelLimit, $"취소가 {cancelledAt.Elapsed.TotalSeconds:F1}초 뒤에야 먹힘");
        Assert.Equal(sha, TreeAssert.Sha256(arc));
        ArchiveEditTests.NoLeftovers(t.Root);
    }
}
