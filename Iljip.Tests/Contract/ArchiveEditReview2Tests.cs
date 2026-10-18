using Iljip.Models;
using Iljip.Services;
using Iljip.Services.Editing;
using Iljip.Tests.TestSupport;

namespace Iljip.Tests.Contract;

/// <summary>
/// [W2-06 검증 지적 수정 2차] 압축 고치기(I2-3)의 빈틈(편집기 쪽):
/// 빈 7z(일집으로 모두 지운 결과 포함)에 넣기·새 폴더가 '손상됐어요' · 다시 넣기(바꿔 넣기)가 압축에 있던 Windows 에 못 쓰는 이름(':'·끝 점)을 거절 ·
/// 일집 풀기 엔진이 못 푸는 정상 ZIP 을 '원래부터 손상'이라고 알림 · ZIP 검사 동안 진행 막대가 멈춤.
/// 화면 쪽(명령줄 열기와 '다시 넣을까요?'의 교착·넣는 중 닫기)은 <see cref="EntryReinsertTests"/>.
/// </summary>
public class ArchiveEditReview2Tests
{
    // ───────────────────────────── 빈 7z ─────────────────────────────

    /// <summary>자료/x.txt 하나뿐인 7z 를 일집으로 모두 지워 빈 7z 로 만든다(검증 지적의 재료 — 32바이트).</summary>
    private static async Task<string> EmptySevenZip(TempDir t)
    {
        string root = t.Dir("원본", "자료");
        File.WriteAllText(Path.Combine(root, "x.txt"), "x");
        string arc = SevenZipTestArchives.SevenZip(root, t.P("빈.7z"));
        await new SevenZipArchiveEditor().EditAsync(arc, new EditOp[] { new EditOp.Delete(new[] { "자료" }) }, null, null, CancellationToken.None);
        Assert.Empty(await Svc.For(arc).ListEntriesAsync(arc));
        return arc;
    }

    /// <summary>
    /// 계약: 항목이 하나도 없는 7z(일집으로 모두 지운 결과)에도 파일 넣기·새 폴더·새 폴더 안에 넣기(빈 폴더만 있는 7z)가 된다 —
    /// 예전엔 솔리드 정보가 없어 '압축 파일이 손상됐어요. 다시 받아 보세요.'로 멀쩡한 압축을 손상이라 알리고 고치지 못했다. 반디집도 결과를 푼다.
    /// </summary>
    [Theory]
    [InlineData("add")]
    [InlineData("newfolder")]
    [InlineData("folder-then-add")]
    public async Task 빈_7z에도_넣기와_새_폴더가_된다(string op)
    {
        using var t = new TempDir("e7empty");
        string arc = await EmptySevenZip(t);
        string src = t.Dir("넣을 것");
        string file = Path.Combine(src, "새 파일.txt");
        File.WriteAllText(file, "빈 7z 에 넣은 내용");
        var editor = new SevenZipArchiveEditor();

        if (op is "newfolder" or "folder-then-add")
            await editor.EditAsync(arc, new EditOp[] { new EditOp.NewFolder("새 폴더") }, null, null, CancellationToken.None);
        if (op == "add")
            await editor.EditAsync(arc, new EditOp[] { new EditOp.Add(new[] { file }, string.Empty) }, null, null, CancellationToken.None);
        if (op == "folder-then-add")
            await editor.EditAsync(arc, new EditOp[] { new EditOp.Add(new[] { file }, "새 폴더") }, null, null, CancellationToken.None);

        var list = (await Svc.For(arc).ListEntriesAsync(arc)).Select(e => (TreeAssert.NormalizeEntryPath(e.Path), e.IsDirectory))
            .OrderBy(e => e.Item1, StringComparer.Ordinal).ToList();
        var expected = op switch
        {
            "add" => new[] { ("새 파일.txt", false) },
            "newfolder" => new[] { ("새 폴더", true) },
            _ => new[] { ("새 폴더", true), ("새 폴더/새 파일.txt", false) },
        };
        Assert.Equal(expected, list);
        if (op != "newfolder")
        {
            var files = await ArchiveEditTests.ExtractFiles(arc, null, t.Dir("out"));
            Assert.Equal(TreeAssert.Sha256(file), Assert.Single(files).Value);
        }
        if (BandizipCli.Available) Assert.Equal(0, await BandizipCli.TestAsync(arc, "x"));
        ArchiveEditTests.NoLeftovers(t.Root);
    }

    // ───────────────────────────── 다시 넣기: 압축에 있던 이름 그대로 ─────────────────────────────

    /// <summary>
    /// 계약: 다시 넣기(바꿔 넣기 — <see cref="EditOp.Add.ReplaceExisting"/>)는 압축에 있던 원래 이름이 Windows 에 못 쓰는 이름(':' 든 이름·끝 점 — 맥·리눅스에서 만든 압축)이어도
    /// 그 이름 그대로 바꿔 넣는다(검증 지적: 사용자가 이름을 바꾼 적이 없는데 '이름에 쓸 수 없는 글자가 있어요'로 못 넣음). 다른 항목은 그대로, 같은 이름이 둘이 되지 않는다.
    /// 사용자가 고르는 이름(넣기의 다른 이름·이름 바꾸기)은 예전 규칙 그대로 막는다.
    /// </summary>
    [Theory]
    [InlineData("zip", "회의:메모.txt")]
    [InlineData("zip", "끝점.")]
    [InlineData("7z", "회의:메모.txt")]
    [InlineData("7z", "끝점.")]
    public async Task 다시_넣기는_압축에_있던_이름을_그대로_쓴다(string kind, string oddName)
    {
        using var t = new TempDir("eodd");
        string root = t.Dir("원본", "자료");
        File.WriteAllText(Path.Combine(root, "표.csv"), "가,나");
        string arc = kind == "zip" ? t.P("a.zip") : t.P("a.7z");
        if (kind == "zip") await new ZipArchiveService().CompressAsync(new[] { root }, arc);
        else SevenZipTestArchives.SevenZip(root, arc);
        var editor = ArchiveServiceLocator.EditorFor(arc)!;
        string src = t.Dir("고친 것");
        string first = Path.Combine(src, "1.txt");
        string second = Path.Combine(src, "2.txt");
        File.WriteAllText(first, "처음 넣은 내용");
        File.WriteAllText(second, "고쳐서 다시 넣은 내용");
        string key = "자료/" + oddName;

        // 그 이름이 아직 없을 때(연 뒤에 지움) — 제자리에 새로, 있을 때 — 바꿔 넣기
        await editor.EditAsync(arc, new EditOp[] { new EditOp.Add(new[] { first }, "자료", AsName: oddName, ReplaceExisting: true) }, null, null, CancellationToken.None);
        await editor.EditAsync(arc, new EditOp[] { new EditOp.Add(new[] { second }, "자료", AsName: oddName, ReplaceExisting: true) }, null, null, CancellationToken.None);

        var list = (await Svc.For(arc).ListEntriesAsync(arc)).Where(e => !e.IsDirectory).Select(e => TreeAssert.NormalizeEntryPath(e.Path))
            .OrderBy(k => k, StringComparer.Ordinal).ToArray();
        Assert.Equal(new[] { key, "자료/표.csv" }.OrderBy(k => k, StringComparer.Ordinal).ToArray(), list);
        Assert.Equal("고쳐서 다시 넣은 내용", ReadEntryText(arc, key));
        if (BandizipCli.Available) Assert.Equal(0, await BandizipCli.TestAsync(arc, "x"));

        // 사용자가 고르는 이름은 예전처럼 막는다
        var ex = await Svc.Catch(() => editor.EditAsync(arc, new EditOp[] { new EditOp.Add(new[] { first }, "자료", AsName: oddName) }, null, null, CancellationToken.None));
        Assert.Equal(EditPlanner<object>.ValidateName(oddName), Assert.IsType<ArchiveEditException>(ex).Message);
        ArchiveEditTests.NoLeftovers(t.Root);
    }

    /// <summary>압축 안 항목 하나의 글(디스크에 풀지 않고 — Windows 에 못 쓰는 이름이라). ZIP 은 .NET, 7z 는 7z.dll.</summary>
    private static string ReadEntryText(string arc, string key)
    {
        if (arc.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
        {
            using var z = System.IO.Compression.ZipFile.OpenRead(arc);
            using var r = new StreamReader(z.Entries.Single(e => e.FullName == key).Open());
            return r.ReadToEnd();
        }
        using var x = new SharpSevenZip.SharpSevenZipExtractor(arc);
        var f = x.ArchiveFileData.Single(f => TreeAssert.NormalizeEntryPath(f.FileName) == key);
        using var ms = new MemoryStream();
        x.ExtractFile(f.Index, ms);
        return System.Text.Encoding.UTF8.GetString(ms.ToArray());
    }

    /// <summary>계약(순수): 압축에 있던 이름 검사는 경로를 깨는 것(빈 이름·'.'·'..'·'/'·'\')만 막는다.</summary>
    [Theory]
    [InlineData("회의:메모.txt", true)]
    [InlineData("끝점.", true)]
    [InlineData("끝 빈칸 ", true)]
    [InlineData("CON.txt", true)]
    [InlineData("", false)]
    [InlineData(".", false)]
    [InlineData("..", false)]
    [InlineData("가/나", false)]
    [InlineData("가\\나", false)]
    public void 압축에_있던_이름_검사는_경로를_깨는_것만_막는다(string name, bool ok)
    {
        Assert.Equal(ok, EditPlanner<object>.ValidateExistingName(name) is null);
    }

    // ───────────────────────────── 일집이 못 푸는 정상 ZIP ─────────────────────────────

    /// <summary>
    /// 구조(머리·목록)는 맞는데 일집이 모르는 압축 방식(97 — WavPack)으로 적힌 ZIP(파일 둘, 암호 없음 — 암호를 알든 모르든 검사가 내용을 풀어 봄).
    /// 예전 재료(SharpZipLib 로 한 번에 흘려 쓴 ZipCrypto + 데이터 서술자 — 풀기 엔진이 ZIP 으로 알아보지 못했음)는 이제 일집이 풀고 고친다
    /// (<see cref="StreamedZipHeaders"/>·<c>StreamedEncryptedZipTests</c>) — 그래서 '일집이 못 푸는 정상 ZIP'의 예를 이것으로 바꿨다.
    /// </summary>
    private static string UnknownMethodZip(TempDir t)
    {
        string arc = t.P("모르는방식.zip");
        using (var fs = new FileStream(arc, FileMode.Create))
        using (var zos = new ICSharpCode.SharpZipLib.Zip.ZipOutputStream(fs))
        {
            zos.UseZip64 = ICSharpCode.SharpZipLib.Zip.UseZip64.Off;
            foreach (var (name, text) in new[] { ("가.txt", string.Concat(Enumerable.Repeat("가나다", 300))), ("다.txt", "다") })
            {
                byte[] data = System.Text.Encoding.UTF8.GetBytes(text);
                zos.PutNextEntry(new ICSharpCode.SharpZipLib.Zip.ZipEntry(name)
                {
                    DateTime = new DateTime(2024, 5, 6, 7, 8, 10), IsUnicodeText = true,
                    CompressionMethod = ICSharpCode.SharpZipLib.Zip.CompressionMethod.Stored, Size = data.Length,
                });
                zos.Write(data);
                zos.CloseEntry();
            }
        }
        // 방식 칸만 97 로(로컬 머리 +8, 목록 기록 +10) — 크기·CRC·이름·위치는 그대로라 구조는 맞다
        byte[] b = File.ReadAllBytes(arc);
        int eocd = b.Length - 22, p = (int)BitConverter.ToUInt32(b, eocd + 16);
        for (int n = BitConverter.ToUInt16(b, eocd + 10); n > 0; n--)
        {
            BitConverter.GetBytes((ushort)97).CopyTo(b, p + 10);
            BitConverter.GetBytes((ushort)97).CopyTo(b, (int)BitConverter.ToUInt32(b, p + 42) + 8);
            p += 46 + BitConverter.ToUInt16(b, p + 28) + BitConverter.ToUInt16(b, p + 30) + BitConverter.ToUInt16(b, p + 32);
        }
        File.WriteAllBytes(arc, b);
        return arc;
    }

    /// <summary>
    /// 계약: 구조는 맞는데 일집이 풀 수 없는 방식의 ZIP 을 고치려 하면 '원래 압축 파일에 이미 손상된 곳이 있어서…'(사실과 다름 — 검증 지적)가 아니라
    /// ‘이 압축 파일은 일집이 풀 수 없는 방식으로 만들어져 있어서…고치지 않았어요’로 알린다. 원본은 바이트 그대로, 남는 파일 없음.
    /// (재료: 모르는 압축 방식 — 예전 재료 '흘려 쓴 ZipCrypto' 는 이제 풀림. 그래서 '반디집은 원본을 정상으로 본다' 대신 일집의 구조 검사가 맞음을 본다)
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task 일집이_못_푸는_정상_ZIP은_손상이라고_하지_않는다(bool withPassword)
    {
        using var t = new TempDir("eunread");
        string arc = UnknownMethodZip(t);
        Assert.All(ZipInspect.Read(arc), e => Assert.Equal(97, e.Method));   // 재료 확인: 모르는 방식
        using (var fs = File.OpenRead(arc)) ZipRawRewriter.VerifyStructure(fs);   // 재료 확인: 구조(머리·목록·로컬 기록 자리와 이름)는 맞음
        Assert.NotNull(await Svc.Catch(() => Svc.For(arc).ExtractAsync(arc, t.Dir("풀기"), "pw1234")));   // 재료 확인: 일집 풀기도 실패
        string sha = TreeAssert.Sha256(arc);

        var ex = await Svc.Catch(() => new ZipArchiveEditor().EditAsync(arc, new EditOp[] { new EditOp.Delete(new[] { "다.txt" }) },
            withPassword ? "pw1234" : null, null, CancellationToken.None));

        var edit = Assert.IsType<ArchiveEditException>(ex);
        Assert.Equal(ArchiveTester.UnreadableMessage, edit.Message);
        Assert.DoesNotContain("손상", edit.Message);
        Assert.DoesNotContain("압축 파일이 아닌", edit.Message);   // 재사용장치 분류 말 금지
        Assert.Equal(sha, TreeAssert.Sha256(arc));
        ArchiveEditTests.NoLeftovers(t.Root);
    }

    /// <summary>
    /// 계약(순수): 원래 압축은 멀쩡한데 고친 결과만 일집이 못 푸는 모양이면 그것은 고치다 생긴 문제 — '고친 압축 파일을 검사했더니…'(일집이 못 푸는 방식이라고 원본 탓을 하지 않음).
    /// 원래 압축도 못 풀면 '일집이 풀 수 없는 방식'.
    /// </summary>
    [Fact]
    public void 결과만_못_풀면_고치다_생긴_문제로_알린다()
    {
        using var t = new TempDir("eunread2");
        var target = ArchiveEditTests.Prepare("zip_default", t);
        var keys = ZipInspect.Read(target.Path).Select(e => TreeAssert.NormalizeEntryPath(e.Name)).ToList();
        var unreadable = new FixedFailTester(ArchiveTester.UnreadableMessage);

        var ok = Assert.Throws<ArchiveEditException>(() => ArchiveTester.TestOrBlameOriginal(unreadable, target.Path, target.Path, EditableFormat.Zip,
            keys, keys, null, CancellationToken.None, null));
        Assert.Equal(ArchiveTester.FailedMessage, ok.Message);

        string unknown = UnknownMethodZip(t);
        var unknownKeys = ZipInspect.Read(unknown).Select(e => e.Name).ToList();
        var bad = Assert.Throws<ArchiveEditException>(() => ArchiveTester.TestOrBlameOriginal(new FixedFailTester(ArchiveTester.FailedMessage), unknown, unknown,
            EditableFormat.Zip, unknownKeys, unknownKeys, "pw1234", CancellationToken.None, null));
        Assert.Equal(ArchiveTester.UnreadableMessage, bad.Message);
    }

    private sealed class FixedFailTester : IArchiveTester
    {
        private readonly string _message;

        public FixedFailTester(string message) => _message = message;

        public void Test(string archivePath, EditableFormat format, IReadOnlyCollection<string> expectedKeys, string? password, CancellationToken cancellationToken) =>
            throw new ArchiveEditException(_message);
    }

    // ───────────────────────────── ZIP 검사 동안의 진행 ─────────────────────────────

    /// <summary>
    /// 계약: ZIP 고치기의 검사(모든 항목을 풀어 CRC 확인 — 큰 ZIP 은 다시 쓰기보다 오래 걸림) 동안에도 막대가 오른다 — '검사하는 중' 보고의 진행 값이 여럿이고
    /// 처음 값은 끝(100%)보다 작다(검증 지적: 다시 쓰기가 끝나면 100% 가 되어 검사 내내 멈춘 것처럼 보임). 진행은 줄지 않고 끝은 100%.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ZIP_검사_동안에도_진행_막대가_오른다(bool encrypted)
    {
        using var t = new TempDir("ezprog");
        string root = t.Dir("원본", "자료");
        var rnd = new Random(7);
        for (int i = 0; i < 4; i++)
        {
            var data = new byte[2 << 20];
            rnd.NextBytes(data);
            File.WriteAllBytes(Path.Combine(root, $"조각 {i}.bin"), data);
        }
        File.WriteAllText(Path.Combine(root, "작은.txt"), "지울 것");
        string arc = t.P("큰.zip");
        await new ZipArchiveService().CompressAsync(new[] { root }, arc,
            encrypted ? new CompressionOptions { Password = "pw1234", Encryption = ZipEncryption.ZipCrypto } : null);
        var rec = new ProgressRecorder();

        await new ZipArchiveEditor().EditAsync(arc, new EditOp[] { new EditOp.Delete(new[] { "자료/작은.txt" }) }, encrypted ? "pw1234" : null, rec, CancellationToken.None);

        var reports = rec.Reports.ToList();
        for (int i = 1; i < reports.Count; i++)
            Assert.True(reports[i].BytesProcessed >= reports[i - 1].BytesProcessed, $"진행률이 줄어듦: {reports[i - 1].BytesProcessed} → {reports[i].BytesProcessed}");
        Assert.Equal(reports[^1].TotalBytes, reports[^1].BytesProcessed);
        var testing = reports.Where(r => r.CurrentFile == EditSupport.StageTesting).ToList();
        Assert.NotEmpty(testing);
        Assert.True(testing[0].BytesProcessed < testing[0].TotalBytes, "검사를 시작할 때 이미 100%");
        int distinct = testing.Select(r => r.BytesProcessed).Distinct().Count();
        Assert.True(distinct >= 3, $"검사하는 동안 진행 값이 {distinct}개뿐");
        Assert.Equal(4, (await ArchiveEditTests.ExtractFiles(arc, encrypted ? "pw1234" : null, t.Dir("out"))).Count);
        ArchiveEditTests.NoLeftovers(t.Root);
    }
}
