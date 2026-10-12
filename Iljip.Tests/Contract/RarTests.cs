using Iljip.Services;
using Iljip.Tests.TestSupport;

namespace Iljip.Tests.Contract;

/// <summary>
/// RAR 해제(통합 점검 2026-10-12). 반디집·이 PC 모두 RAR 을 만들 수 없어 그동안 RAR 은 '깨진 파일 거절'·라우팅만 자동으로 봤다.
/// 이제 테스트 안의 최소 작성기(<see cref="RawRar5"/>·<see cref="RawRar4"/> — 저장 방식, 암호·머리 암호·유니코드/CP949 이름)로 만든
/// 합성 RAR 로 목록·해제·암호·손상을 시험한다. 작성기가 맞는지는 반디집 콘솔(bz.exe t)로 먼저 확인한다(반디집이 없는 PC 에선 그 하나만 건너뜀).
/// 이 점검에서 찾아 고친 것: RAR4 암호 파일을 암호 없이·틀린 암호로 풀면 묻지도 않고 쓰레기 파일을 '완료'로 남김,
/// RAR4 머리 암호의 틀린 암호가 영어 오류 창(다시 묻지 않음), RAR 머리 암호 문구가 7z 와 다름, 암호 없는 RAR 의 CRC 오류가 영어 문구,
/// RAR5 유닉스 시각이 목록에 UTC 로 보임.
/// ★ 진짜 WinRAR 산출물(압축된 데이터·RAR5 의 HMAC CRC·BLAKE2)은 확인하지 못했다 — MANUAL_CHECKLIST 6절.
/// </summary>
public class RarTests
{
    public static TheoryData<string> AllRar => new() { "rar5", "rar5_unix", "rar5_enc", "rar5_hdr", "rar4", "rar4_cp949", "rar4_enc", "rar4_hdr" };
    public static TheoryData<string> EncryptedRar => new() { "rar5_enc", "rar5_hdr", "rar4_enc", "rar4_hdr" };

    /// <summary>작성기 검증: 합성 RAR 을 반디집 콘솔이 '모두 맞게' 풀고(종료 코드 0), 틀린 암호면 실패한다.</summary>
    [BandizipCliFact]
    public async Task 합성_RAR을_반디집도_맞게_푼다()
    {
        using var tmp = new TempDir("rarbz");
        foreach (var kind in SyntheticArchives.Kinds.Where(k => k.StartsWith("rar")))
        {
            var b = SyntheticArchives.Build(kind, tmp.Root);
            Assert.True(await BandizipCli.TestAsync(b.Path, b.Password ?? "unused") == 0, $"{kind}: 반디집이 합성 RAR 을 맞게 풀지 못함(작성기 오류)");
            if (b.Password is not null)
                Assert.True(await BandizipCli.TestAsync(b.Path, "wrong") != 0, $"{kind}: 반디집이 틀린 암호로도 풀었음(암호화가 안 됨)");
        }
        // 한글 암호(RAR5 = UTF-8, RAR4 = UTF-16 — 둘 다 반디집과 같은 바이트)
        string k5 = tmp.P("k5.rar"), k4 = tmp.P("k4.rar");
        RawRar5.Write(k5, new[] { new RawRar5.Item("a.txt", new byte[] { 1, 2, 3 }) }, FixtureCatalog.KoreanPassword);
        RawRar4.Write(k4, new[] { new RawRar5.Item("a.txt", new byte[] { 1, 2, 3 }) }, FixtureCatalog.KoreanPassword);
        Assert.Equal(0, await BandizipCli.TestAsync(k5, FixtureCatalog.KoreanPassword));
        Assert.Equal(0, await BandizipCli.TestAsync(k4, FixtureCatalog.KoreanPassword));
    }

    /// <summary>계약: 목록(파일 정확히·폴더는 기대 안의 것)과 해제 결과(폴더·빈 파일·한글 이름·sha256)가 원본과 같고, 풀린 파일의 수정 시각도 맞다.</summary>
    [Theory]
    [MemberData(nameof(AllRar))]
    public async Task RAR_목록과_해제가_원본과_같다(string kind)
    {
        using var tmp = new TempDir("rar");
        var b = SyntheticArchives.Build(kind, tmp.Root);
        var svc = Svc.For(b.Path);
        Assert.IsType<RarArchiveService>(svc);

        var list = await svc.ListEntriesAsync(b.Path, b.Password);
        TreeAssert.ListFilesEqual(list, b.Expected, kind);

        string dest = tmp.P("out");
        await svc.ExtractAsync(b.Path, dest, b.Password);
        TreeAssert.ExtractedTreeEquals(dest, b.Expected, context: kind);

        DateTime want = SyntheticArchives.ExpectedLocalMtime(b.MtimeWallClock!.Value);
        foreach (var f in TreeAssert.Files(dest))
            Assert.True((File.GetLastWriteTime(Path.Combine(dest, f)) - want).Duration() <= TimeSpan.FromSeconds(2),
                $"{kind} {f}: 수정 시각 {File.GetLastWriteTime(Path.Combine(dest, f)):yyyy-MM-dd HH:mm:ss} — 기대 {want:yyyy-MM-dd HH:mm:ss}");
    }

    /// <summary>
    /// 계약: 목록의 수정 시각은 현지 시각이다(화면이 그대로 찍음). RAR5 의 유닉스 시각은 엔진이 UTC 로 줘서 예전엔 9시간 이르게 보였다.
    /// RAR4 는 DOS 벽시계, 나머지는 절대 시각을 현지로.
    /// </summary>
    [Theory]
    [MemberData(nameof(AllRar))]
    public async Task RAR_목록의_수정시각은_현지시각(string kind)
    {
        using var tmp = new TempDir("rart");
        var b = SyntheticArchives.Build(kind, tmp.Root);
        var list = await Svc.For(b.Path).ListEntriesAsync(b.Path, b.Password);
        DateTime want = SyntheticArchives.ExpectedLocalMtime(b.MtimeWallClock!.Value);
        Assert.All(list, e =>
        {
            Assert.NotNull(e.LastModified);
            Assert.NotEqual(DateTimeKind.Utc, e.LastModified!.Value.Kind);
            Assert.True((e.LastModified.Value - want).Duration() <= TimeSpan.FromSeconds(2), $"{kind} {e.Path}: {e.LastModified:yyyy-MM-dd HH:mm:ss} — 기대 {want:yyyy-MM-dd HH:mm:ss}");
        });
    }

    /// <summary>
    /// 계약: 암호 없음 → <see cref="PasswordRequiredException"/>, 틀린 암호 → <see cref="WrongPasswordException"/>, 화면이 암호 문제로 알아보고,
    /// 대상 폴더도 남지 않는다. RAR4 는 예전에 암호 없이도·틀린 암호로도 암호화된 바이트(쓰레기)를 그대로 써서 '완료'로 끝났다.
    /// 머리 암호면 7z 헤더 암호와 같은 문구('파일 이름까지 암호가 걸려 있어요…') — RAR4 머리 암호의 틀린 암호는 예전엔 'Unknown Rar Header' 영어 오류였다.
    /// </summary>
    [Theory]
    [MemberData(nameof(EncryptedRar))]
    public async Task RAR_암호가_없거나_틀리면_암호예외_그리고_아무것도_안_남는다(string kind)
    {
        using var tmp = new TempDir("rarpw");
        var b = SyntheticArchives.Build(kind, tmp.Root);
        var svc = Svc.For(b.Path);

        var none = await Svc.Catch(() => svc.ExtractAsync(b.Path, tmp.P("새 폴더", "없음"), null));
        var req = Assert.IsType<PasswordRequiredException>(none);
        Assert.Equal(b.HeaderEncrypted, req.IsHeaderEncrypted);
        Assert.Equal(b.HeaderEncrypted ? PasswordRequiredException.HeaderMessage : PasswordRequiredException.DefaultMessage, req.Message);
        Assert.True(Internals.IsPasswordRelated(none!));

        var wrong = await Svc.Catch(() => svc.ExtractAsync(b.Path, tmp.P("새 폴더", "틀림"), "wrong-password"));
        var w = Assert.IsType<WrongPasswordException>(wrong);
        Assert.Equal(b.HeaderEncrypted, w.IsHeaderEncrypted);
        Assert.StartsWith(WrongPasswordException.DefaultMessage, w.Message);
        Assert.True(Internals.IsPasswordRelated(wrong!));

        Assert.False(Directory.Exists(tmp.P("새 폴더")), $"{kind}: 암호 오류인데 새로 만든 폴더가 남음");
    }

    /// <summary>계약: 머리 암호 RAR 은 암호 없이 목록을 읽으면 '파일 이름까지 암호' 예외, 틀린 암호도 암호 예외(화면이 열기 단계에서 암호를 묻는 근거).</summary>
    [Theory]
    [InlineData("rar5_hdr")]
    [InlineData("rar4_hdr")]
    public async Task 머리암호_RAR_목록은_암호없이_암호예외(string kind)
    {
        using var tmp = new TempDir("rarh");
        var b = SyntheticArchives.Build(kind, tmp.Root);
        var svc = Svc.For(b.Path);
        Assert.True(Assert.IsType<PasswordRequiredException>(await Svc.Catch(() => svc.ListEntriesAsync(b.Path, null))).IsHeaderEncrypted);
        Assert.True(Assert.IsType<WrongPasswordException>(await Svc.Catch(() => svc.ListEntriesAsync(b.Path, "wrong-password"))).IsHeaderEncrypted);
    }

    /// <summary>계약: 이름이 보이는 암호 RAR(파일만 암호)은 암호 없이도 목록이 읽힌다(ZIP·7z 와 같음 — 열 때는 묻지 않고 풀 때 묻는다).</summary>
    [Theory]
    [InlineData("rar5_enc")]
    [InlineData("rar4_enc")]
    public async Task 이름이_보이는_암호_RAR은_암호없이_목록이_읽힌다(string kind)
    {
        using var tmp = new TempDir("rarl");
        var b = SyntheticArchives.Build(kind, tmp.Root);
        TreeAssert.ListFilesEqual(await Svc.For(b.Path).ListEntriesAsync(b.Path, null), b.Expected, kind);
    }

    /// <summary>
    /// 계약: 암호 없는 RAR 의 데이터가 깨지면 CRC 오류(쉬운 문구의 <see cref="ChecksumMismatchException"/>)로 멈추고, 깨진 그 파일은 지우고 앞서 맞게 풀린 파일은 남긴다(ZIP 과 같음).
    /// 예전엔 엔진 문구 'file crc mismatch: …' 가 그대로 보였다.
    /// </summary>
    [Theory]
    [InlineData("rar5")]
    [InlineData("rar4")]
    public async Task 손상된_RAR은_CRC_오류로_알리고_앞_파일은_남긴다(string format)
    {
        using var tmp = new TempDir("rarc");
        string arc = tmp.P("손상.rar");
        var items = new[]
        {
            new RawRar5.Item("가.txt", System.Text.Encoding.ASCII.GetBytes("FIRST-FILE-CONTENT-0123456789")),
            new RawRar5.Item("나.txt", System.Text.Encoding.ASCII.GetBytes("SECOND-FILE-CONTENT-0123456789")),
        };
        if (format == "rar5") RawRar5.Write(arc, items); else RawRar4.Write(arc, items);
        var bytes = File.ReadAllBytes(arc);
        int at = IndexOf(bytes, System.Text.Encoding.ASCII.GetBytes("SECOND-FILE"));
        bytes[at + 3] ^= 0x20;
        File.WriteAllBytes(arc, bytes);

        string dest = tmp.P("out");
        var ex = await Svc.Catch(() => Svc.For(arc).ExtractAsync(arc, dest));
        var crc = Assert.IsType<ChecksumMismatchException>(ex);
        Assert.Contains("나.txt", crc.Message);
        Assert.False(Internals.IsPasswordRelated(ex!));
        Assert.Equal(new[] { "가.txt" }, TreeAssert.Files(dest));
    }

    /// <summary>계약: 진행률 0~100%·도중 보고·완료 보고 한 번(진행 막대가 움직임) — 암호·머리 암호 RAR 도.</summary>
    [Theory]
    [MemberData(nameof(AllRar))]
    public async Task RAR_해제_진행률이_0에서_100으로_끝난다(string kind)
    {
        using var tmp = new TempDir("rarp");
        var b = SyntheticArchives.Build(kind, tmp.Root);
        var rec = new ProgressRecorder();
        await Svc.For(b.Path).ExtractAsync(b.Path, tmp.P("out"), b.Password, rec);
        ProgressTests.AssertWellFormed(rec.Reports, kind, expectIntermediate: true);
        Assert.Equal(b.Expected.Where(e => !e.IsDir).Sum(e => e.Size), rec.Reports[^1].TotalBytes);
        Assert.Equal(4, rec.Reports[^1].TotalFiles);
    }

    /// <summary>
    /// 계약: 선택 해제는 '/' 로 골라도 된다 — 엔진이 RAR 항목 이름을 '\' 로 주지만(화면 폴더 트리·선택은 둘 다 처리) 결과는 같다.
    /// 머리 암호 RAR 도 받은 암호로.
    /// </summary>
    [Theory]
    [InlineData("rar5")]
    [InlineData("rar4_hdr")]
    public async Task RAR_선택해제는_슬래시로_골라도_된다(string kind)
    {
        using var tmp = new TempDir("rars");
        var b = SyntheticArchives.Build(kind, tmp.Root);
        string dest = tmp.P("out");
        await Svc.For(b.Path).ExtractEntriesAsync(b.Path, dest, new[] { "자료/하위", "readme.txt" }, b.Password);
        Assert.Equal(new[] { "readme.txt", "자료/하위/난수.bin" }, TreeAssert.Files(dest));
    }

    /// <summary>
    /// 현재 동작 기록: W2-01 부터 RAR 은 7z.dll 로 열어 항목 이름의 폴더 구분이 '/'(다른 형식과 같음)다.
    /// 7z.dll 을 못 쓰는 PC 의 대신 경로(SharpCompress)는 예전처럼 '\'. 화면·선택 해제는 '\'·'/' 를 같이 처리한다(위 선택 해제 계약).
    /// </summary>
    [Fact, Trait(Kinds.Key, Kinds.Record)]
    public async Task 기록_RAR_항목_이름의_폴더_구분()
    {
        using var tmp = new TempDir("rarr");
        var b = SyntheticArchives.Build("rar5", tmp.Root);
        var list = await Svc.For(b.Path).ListEntriesAsync(b.Path);
        Assert.Contains(list, e => e.Path == "자료/견적서.txt");
        using (SevenZipLibrary.SimulateUnavailableForTests())
        {
            var fallback = await Svc.For(b.Path).ListEntriesAsync(b.Path);
            Assert.Contains(fallback, e => e.Path == "자료\\견적서.txt");
        }
    }

    private static int IndexOf(byte[] haystack, byte[] needle)
    {
        for (int i = 0; i + needle.Length <= haystack.Length; i++)
            if (haystack.AsSpan(i, needle.Length).SequenceEqual(needle)) return i;
        return -1;
    }
}
