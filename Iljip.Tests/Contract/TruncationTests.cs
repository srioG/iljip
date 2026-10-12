using System.Text;
using Iljip.Services;
using Iljip.Tests.TestSupport;

namespace Iljip.Tests.Contract;

/// <summary>
/// [W2-01, 1차 W1-B 검증 지적 (a)] 끝이 잘린 압축(받다 만 파일)·빠진 조각이 '완료'로 끝나며 파일이 조용히 빠지지 않는다.
/// 7z.dll 은 머리 사이에서 잘린 RAR4·LZH 를 '읽은 데까지만' 열고 오류를 내지 않는다 — 잘린 뒤쪽 항목은 목록에 아예 없어
/// 항목별 성공·실패로는 알 수 없다(다운로드가 덜 된 RAR 이 '압축 해제 완료' + 파일 일부 누락).
/// W2-01: 열 때(목록·풀기 모두) 형식별 구조 검사(<see cref="ArchiveCompleteness"/> — RAR4 ENDARC 규칙·RAR5 끝 머리·LZH 끝 표시 0 바이트)로 잘림을 알아내고,
/// 풀기는 임시 폴더에 푼 뒤 모두 맞을 때만 옮긴다 → 잘린 압축은 '끝부분이 잘려' 오류이고 대상에는 아무것도 남지 않는다
/// (1차 W1-B 는 '풀 수 있는 앞 파일은 풀어 둔다'였다 — 설계 §2 '전부 아니면 전무'로 바꿈).
/// RAR 는 실물 픽스처가 없어 규격대로 만든 합성 RAR(<see cref="RawRar"/>)로 시험한다.
/// </summary>
public class TruncationTests
{
    private static readonly RawRar.Item[] RarItems =
    {
        new("한글 폴더", null),
        new("한글 폴더/가나다 라.txt", Encoding.UTF8.GetBytes("안녕하세요 RAR5")),
        new("readme.txt", Encoding.ASCII.GetBytes(string.Concat(Enumerable.Repeat("plain readme ", 50)))),
        new("빈파일.txt", Array.Empty<byte>()),
        new("마지막.bin", Enumerable.Range(0, 3000).Select(i => (byte)(i * 31 + 7)).ToArray()),
    };

    private static Dictionary<string, string> Expected(IEnumerable<RawRar.Item> items) =>
        items.Where(i => !i.IsDir).ToDictionary(i => i.Name, i => TreeAssert.Sha256(i.Data!), StringComparer.Ordinal);

    /// <summary>온전한 압축은 모든 파일이 원본과 같게 풀린다(합성 작성기 교차 확인).</summary>
    private static void AssertComplete(string dest, IReadOnlyDictionary<string, string> expected, string context)
    {
        var files = TreeAssert.Files(dest);
        Assert.True(files.Count == expected.Count, $"{context}: 파일 수 {files.Count}/{expected.Count}");
        foreach (var rel in files)
        {
            Assert.True(expected.TryGetValue(rel, out var want), $"{context}: 기대에 없는 파일 {rel}");
            Assert.True(want == TreeAssert.Sha256(Path.Combine(dest, rel)), $"{context}: 내용이 원본과 다름 {rel}");
        }
    }

    /// <summary>
    /// 잘린 압축의 판정: 예외가 나야 하고(조용한 '완료' 금지), 손상·조각 문제(암호 문제 아님, '압축 파일 아님' 문구 없음)이며,
    /// 대상 폴더에는 아무것도 남지 않는다 — 파일도, 임시 폴더도, 이번에 새로 만든 대상 폴더도.
    /// </summary>
    private static void AssertReportedAndNothingLeft(Exception? ex, string parent, string context)
    {
        Assert.True(ex is not null, $"{context}: 잘린 압축인데 예외 없이 끝남(조용한 부분 해제)");
        Assert.True(ex is CorruptArchiveException or VolumeMissingException, $"{context}: 잘린 압축의 예외가 손상·조각 없음이 아님: {ex!.GetType().Name} {ex.Message}");
        Assert.False(Internals.IsPasswordRelated(ex), $"{context}: 잘림을 암호 문제로 오해함");
        Assert.DoesNotContain("압축 파일이 아닌", ErrorMessages.ToUserMessage(ex));
        Assert.False(Directory.Exists(parent), $"{context}: 대상에 무언가 남음(파일·임시 폴더·새로 만든 폴더)");
    }

    private static async Task CutAndCheck(TempDir tmp, byte[] bytes, int cut, string ext, string context, string? password = null, bool checkList = true)
    {
        string arc = tmp.P($"t{cut}{ext}");
        File.WriteAllBytes(arc, bytes[..cut]);
        string parent = tmp.P($"새 폴더 {cut}");
        var ex = await Svc.Catch(() => Svc.For(arc).ExtractAsync(arc, Path.Combine(parent, "out"), password));
        AssertReportedAndNothingLeft(ex, parent, $"{context} {cut}/{bytes.Length}바이트에서 자름");
        if (checkList)
        {
            // 목록 단계에서도 잘림을 알린다(화면이 반쪽 목록을 보여 주고 '열기 성공'으로 끝나지 않게)
            var listEx = await Svc.Catch(() => Svc.For(arc).ListEntriesAsync(arc, password));
            Assert.True(listEx is CorruptArchiveException or VolumeMissingException or ArchivePasswordException,
                $"{context} {cut}: 목록이 잘림을 알리지 않음 — {listEx?.GetType().Name ?? "예외 없음"}");
        }
    }

    /// <summary>
    /// 계약: 끝이 잘린 RAR5 는 어디서 잘려도(머리 한가운데·데이터 한가운데·머리와 머리 사이) 오류이고 아무것도 남기지 않는다.
    /// 예전(1차 W1-B 이전): 837개 위치 중 152곳에서 '일부만 풀리고 성공'. RAR5 는 끝 머리(type 5)가 꼭 있어 머리 사이에서 잘려도 알아챈다.
    /// </summary>
    [Fact]
    public async Task 끝이_잘린_RAR5는_어디서_잘려도_오류이고_아무것도_남기지_않는다()
    {
        using var tmp = new TempDir("tr5");
        var rar = RawRar.Rar5(RarItems);
        string full = tmp.P("full.rar");
        File.WriteAllBytes(full, rar.Bytes);
        string fullOut = tmp.P("full_out");
        await Svc.For(full).ExtractAsync(full, fullOut);
        AssertComplete(fullOut, Expected(RarItems), "온전한 RAR5");

        // 한 바이트씩 전부(작은 합성 RAR — 구조 검사가 7z.dll 보다 먼저 막아 빠르다)
        foreach (int cut in Enumerable.Range(8, rar.Bytes.Length - 8))
            await CutAndCheck(tmp, rar.Bytes, cut, ".rar", "RAR5", checkList: cut % 97 == 0 || rar.BlockEnds.Contains(cut));
    }

    /// <summary>
    /// 계약: 끝이 잘린 RAR4 도 어디서 잘려도 알린다 — 머리 시작 바로 앞(이전 항목이 끝난 자리)에서 정확히 잘린 경우까지.
    /// RAR 3.x 이상(풀 때 필요한 판 ≥ 29)은 -en 이 아니면 늘 끝 머리(ENDARC)를 쓰므로 그것이 없으면 잘림으로 본다(설계 §2 RAR4 규칙).
    /// 1차 W1-B 의 '남은 한계'(머리 사이 잘림은 앞 항목만 풀고 성공)를 막은 것.
    /// </summary>
    [Fact]
    public async Task 끝이_잘린_RAR4는_머리_사이에서_잘려도_알린다()
    {
        using var tmp = new TempDir("tr4");
        var files = new[] { ("hello.txt", Encoding.ASCII.GetBytes("hello rar4")), ("sub\\deep.txt", Encoding.ASCII.GetBytes("deep")),
                            ("big.bin", Enumerable.Range(0, 2000).Select(i => (byte)i).ToArray()) };
        var rar = RawRar.Rar4(files);
        var expected = files.ToDictionary(f => f.Item1.Replace('\\', '/'), f => TreeAssert.Sha256(f.Item2), StringComparer.Ordinal);
        string full = tmp.P("full.rar");
        File.WriteAllBytes(full, rar.Bytes);
        await Svc.For(full).ExtractAsync(full, tmp.P("full_out"));
        AssertComplete(tmp.P("full_out"), expected, "온전한 RAR4");

        foreach (int cut in Enumerable.Range(7, rar.Bytes.Length - 7))
            await CutAndCheck(tmp, rar.Bytes, cut, ".rar", "RAR4", checkList: cut % 97 == 0 || rar.BlockEnds.Contains(cut));
        // 머리 경계(항목이 끝난 자리) — 주 머리만 남은 것(항목 0개)도 잘림
        foreach (int cut in rar.BlockEnds.SkipLast(1))
        {
            string arc = tmp.P($"b{cut}.rar");
            File.WriteAllBytes(arc, rar.Bytes[..cut]);
            var ex = Assert.IsType<CorruptArchiveException>(await Svc.Catch(() => Svc.For(arc).ExtractAsync(arc, tmp.P($"b{cut}"))));
            Assert.True(ex.IsTruncated, $"RAR4 머리 경계 {cut}: 잘림으로 알리지 않음 — {ex.Message}");
            Assert.Contains("끝부분이 잘려", ErrorMessages.ToUserMessage(ex));
        }
    }

    /// <summary>
    /// 계약(엄격 규칙의 경계): 끝 머리가 없어도 풀 때 필요한 판이 29 미만(RAR 2.x 이전 — 끝 머리를 쓰지 않던 시절)인 RAR4 는 온전한 것으로 보고 다 푼다.
    /// 판 29 인데 끝 머리가 없으면(-en 으로 만든 드문 파일 포함) 잘림으로 알린다 — 그런 파일은 MANUAL_CHECKLIST(실물 WinRAR) 로 확인.
    /// </summary>
    [Fact]
    public async Task 끝머리_없는_RAR4는_옛_판이면_풀고_새_판이면_잘림()
    {
        using var tmp = new TempDir("tr4e");
        var files = new[] { ("a.txt", Encoding.ASCII.GetBytes("aaaa")), ("b.txt", Encoding.ASCII.GetBytes("bbbb")) };
        var expected = files.ToDictionary(f => f.Item1, f => TreeAssert.Sha256(f.Item2), StringComparer.Ordinal);

        string old = tmp.P("old.rar");
        File.WriteAllBytes(old, RawRar.Rar4(files, withEndArc: false, unpVer: 20).Bytes);
        await Svc.For(old).ExtractAsync(old, tmp.P("old_out"));
        AssertComplete(tmp.P("old_out"), expected, "끝 머리 없는 판 20 RAR4");

        string modern = tmp.P("modern.rar");
        File.WriteAllBytes(modern, RawRar.Rar4(files, withEndArc: false).Bytes);
        var ex = Assert.IsType<CorruptArchiveException>(await Svc.Catch(() => Svc.For(modern).ExtractAsync(modern, tmp.P("새 폴더", "out"))));
        Assert.True(ex.IsTruncated);
        Assert.False(Directory.Exists(tmp.P("새 폴더")));
        Assert.IsType<CorruptArchiveException>(await Svc.Catch(() => Svc.For(modern).ListEntriesAsync(modern)));
    }

    /// <summary>
    /// 계약: 끝이 잘린 ZIPX(반디집 픽스처)·LZH(반디집 픽스처)도 조용히 일부만 풀리지 않고, 대상에 아무것도 남기지 않는다.
    /// LZH 는 항목 머리 사이에서 정확히 잘린 경우를 7z.dll 이 알아채지 못해(끝 표시 0 바이트를 요구하지 않음 — 7-Zip 과 같음, 2,026곳 중 7곳)
    /// 일집이 끝 표시를 요구한다(엄격 — 설계 §2). 그래서 LZH 는 모든 위치를 본다. ZIPX 는 97 바이트 간격(실측 5,351곳 모두 7z.dll 이 알림).
    /// 적어도 한 번은 '끝부분이 잘려' 로 알린다.
    /// </summary>
    [Theory]
    [InlineData("zipx_default", 97)]
    [InlineData("lzh_default", 1)]
    public async Task 끝이_잘린_ZIPX와_LZH는_오류이고_아무것도_남기지_않는다(string id, int step)
    {
        var fx = FixtureCatalog.Get(id);
        var bytes = File.ReadAllBytes(fx.FullPath);
        var expected = fx.ExpectedFiles.ToDictionary(e => e.Path, e => e.Sha256!, StringComparer.Ordinal);
        using var tmp = new TempDir("trx");
        string full = tmp.P("full_out");
        await Svc.For(fx.FullPath).ExtractAsync(fx.FullPath, full);
        AssertComplete(full, expected, id + " 온전한 것");

        bool sawTruncated = false;
        foreach (int cut in Enumerable.Range(1, (bytes.Length - 1) / step).Select(k => k * step))
        {
            string arc = tmp.P($"t{cut}{Path.GetExtension(fx.File)}");
            File.WriteAllBytes(arc, bytes[..cut]);
            string parent = tmp.P($"새 폴더 {cut}");
            var ex = await Svc.Catch(() => Svc.For(arc).ExtractAsync(arc, Path.Combine(parent, "out")));
            AssertReportedAndNothingLeft(ex, parent, $"{id} {cut}/{bytes.Length}바이트에서 자름");
            sawTruncated |= ex is CorruptArchiveException { IsTruncated: true };
        }
        Assert.True(sawTruncated, $"{id}: 잘림을 '끝부분이 잘려' 로 알린 경우가 하나도 없음");
    }

    /// <summary>
    /// 계약(W2-01): 끝이 잘린 RAR 은 '끝부분이 잘려' 로 알리고, 그 앞에서 온전했던 파일도 대상에 옮기지 않는다(전부 아니면 전무).
    /// 1차 W1-B 는 앞 파일을 남겼다 — 받다 만 파일은 다시 받아 통째로 푸는 것이 맞다(반쪽 결과로 일을 이어 가지 않게).
    /// 이미 있던 대상 폴더의 원래 파일은 그대로 둔다.
    /// </summary>
    [Fact]
    public async Task 잘린_RAR은_잘렸다고_알리고_앞_파일도_옮기지_않는다()
    {
        using var tmp = new TempDir("trk");
        var rar = RawRar.Rar5(RarItems);
        string arc = tmp.P("반쯤.rar");
        File.WriteAllBytes(arc, rar.Bytes[..(rar.BlockEnds[^2] - 100)]);   // 마지막.bin 데이터 한가운데
        string dest = tmp.Dir("out");
        File.WriteAllText(Path.Combine(dest, "원래 파일.txt"), "그대로");
        var ex = Assert.IsType<CorruptArchiveException>(await Svc.Catch(() => Svc.For(arc).ExtractAsync(arc, dest)));
        Assert.True(ex.IsTruncated, ex.Message);
        Assert.Contains("끝부분이 잘려", ErrorMessages.ToUserMessage(ex));
        Assert.Equal(new[] { "원래 파일.txt" }, TreeAssert.Files(dest));
        Assert.False(Directory.EnumerateDirectories(dest, SevenZipEngine.StagingPrefix + "*").Any(), "임시 폴더가 남음");
    }

    /// <summary>
    /// 계약: 머리 암호 RAR5(맞는 암호)가 잘려도 암호 문제로 오해하지 않고 손상으로 알린다(예전엔 896~944 바이트에서 잘린 것이 '성공 2/3').
    /// 머리 암호는 구조 검사가 머리를 못 읽어 7z.dll 의 판단과 검증기(항목 수·크기·확인값)에 맡긴다.
    /// </summary>
    [Fact]
    public async Task 잘린_머리암호_RAR5는_맞는_암호로_손상을_알린다()
    {
        using var tmp = new TempDir("trh");
        const string pw = "Iljip-Test-1234";
        var bytes = RawRar.Rar5HeaderEncrypted(RarItems, pw);
        string full = tmp.P("full.rar");
        File.WriteAllBytes(full, bytes);
        await Svc.For(full).ExtractAsync(full, tmp.P("full_out"), pw);
        AssertComplete(tmp.P("full_out"), Expected(RarItems), "온전한 머리 암호 RAR5");
        foreach (int cut in new[] { bytes.Length / 2, bytes.Length - 40, bytes.Length - 8, bytes.Length - 1 })
            await CutAndCheck(tmp, bytes, cut, ".rar", "머리 암호 RAR5", pw, checkList: false);
    }

    /// <summary>
    /// 계약: 여러 조각 RAR 의 마지막 조각이 없으면, 조각 경계와 파일 경계가 맞아 앞 조각의 파일이 모두 맞게 풀려도 '완료'가 아니라
    /// 빠진 조각 이름으로 알리고 아무것도 옮기지 않는다(W2-01 — 전부 아니면 전무). 조각이 다 있으면 모두 풀린다.
    /// </summary>
    [Fact]
    public async Task 여러조각_RAR의_마지막_조각이_없으면_그_이름으로_알린다()
    {
        using var tmp = new TempDir("trv");
        string dir = tmp.Dir("vols");
        var p1 = RawRar.Rar5(new[] { new RawRar.Item("part1_file.txt", Encoding.ASCII.GetBytes("from part 1")) }, volume: true, moreVolumes: true);
        var p2 = RawRar.Rar5(new[] { new RawRar.Item("part2_file.txt", Encoding.ASCII.GetBytes("from part 2")) }, volume: true, volumeNumber: 1);
        File.WriteAllBytes(Path.Combine(dir, "자료.part1.rar"), p1.Bytes);

        string first = Path.Combine(dir, "자료.part1.rar");
        string parent = tmp.P("새 폴더");
        var ex = Assert.IsType<VolumeMissingException>(await Svc.Catch(() => Svc.For(first).ExtractAsync(first, Path.Combine(parent, "out"))));
        Assert.Equal("자료.part2.rar", ex.MissingVolumeName);
        Assert.Contains("자료.part2.rar", ErrorMessages.ToUserMessage(ex));
        Assert.False(Directory.Exists(parent), "조각이 빠졌는데 대상에 무언가 남음");
        Assert.Equal("자료.part2.rar", Assert.IsType<VolumeMissingException>(await Svc.Catch(() => Svc.For(first).ListEntriesAsync(first))).MissingVolumeName);

        File.WriteAllBytes(Path.Combine(dir, "자료.part2.rar"), p2.Bytes);
        string all = tmp.P("all");
        await Svc.For(first).ExtractAsync(first, all);
        Assert.Equal(new[] { "part1_file.txt", "part2_file.txt" }, TreeAssert.Files(all));
    }

    /// <summary>
    /// 계약: 숫자 분할의 마지막 조각이 덜 받아졌으면(앞 조각보다 작고 모자람) '다음 조각이 없다'가 아니라 그 조각이 덜 받아졌다고 알린다.
    /// 예전엔 '.003' 을 절반으로 자르면 "'s.7z.004' 조각이 없어요" 라고 해 사용자를 헷갈리게 했다. 파일은 남지 않는다.
    /// </summary>
    [Theory]
    [InlineData("7z_split")]
    [InlineData("zip_numbered_split")]
    public async Task 숫자분할_마지막_조각이_잘리면_그_조각이_덜_받아졌다고_알린다(string id)
    {
        var fx = FixtureCatalog.Get(id);
        using var tmp = new TempDir("trs");
        string dir = tmp.Dir("vols");
        foreach (var v in fx.SplitVolumes)
            File.Copy(FixtureCatalog.PathOf(v), Path.Combine(dir, Path.GetFileName(v)));
        string last = Path.Combine(dir, Path.GetFileName(fx.SplitVolumes[^1]));
        var bytes = File.ReadAllBytes(last);
        File.WriteAllBytes(last, bytes[..(bytes.Length / 2)]);
        string first = Path.Combine(dir, Path.GetFileName(fx.File));
        string dest = tmp.P("out");

        foreach (var e in new[] { await Svc.Catch(() => Svc.For(first).ListEntriesAsync(first)), await Svc.Catch(() => Svc.For(first).ExtractAsync(first, dest)) })
        {
            var vi = Assert.IsType<VolumeIncompleteException>(e);
            Assert.Equal(Path.GetFileName(last), vi.VolumeName);
            Assert.Contains("덜 받아졌어요", ErrorMessages.ToUserMessage(vi));
            Assert.False(Internals.IsPasswordRelated(vi));
        }
        TreeAssert.NoFiles(dest);
    }

    /// <summary>
    /// 계약(1차 W1-A 검증 지적 (e)): 끝이 잘린 .gz(단일 파일, 트레일러 CRC32·ISIZE 가 없거나 모자람)는 어디서 잘려도 오류이고 반쪽 파일을 남기지 않는다.
    /// (CorruptArchiveTests 가 몇 가지 잘림을 보고, 여기서는 모든 위치를 본다)
    /// </summary>
    [Fact]
    public async Task 끝이_잘린_gz는_어디서_잘려도_반쪽_파일을_남기지_않는다()
    {
        var fx = FixtureCatalog.Get("gz_single");
        var bytes = File.ReadAllBytes(fx.FullPath);
        using var tmp = new TempDir("trgz");
        int step = Math.Max(1, bytes.Length / 400);
        foreach (int cut in Enumerable.Range(1, bytes.Length - 1).Where(c => c % step == 0 || c > bytes.Length - 12))
        {
            string arc = tmp.P($"t{cut}.gz");
            File.WriteAllBytes(arc, bytes[..cut]);
            string dest = tmp.P($"o{cut}");
            var ex = await Svc.Catch(() => Svc.For(arc).ExtractAsync(arc, dest));
            Assert.True(ex is not null, $"gz {cut}/{bytes.Length}: 잘렸는데 예외 없이 끝남");
            Assert.False(Internals.IsPasswordRelated(ex!));
            Assert.DoesNotContain("압축 파일이 아닌", ErrorMessages.ToUserMessage(ex!));
            TreeAssert.NoFiles(dest);
        }
    }
}
