using System.Diagnostics;
using System.Text;
using Iljip.Services;
using Iljip.Tests.TestSupport;

namespace Iljip.Tests.Contract;

/// <summary>
/// [I5-B] 창 없이 풀기(<c>Iljip.exe --extract-to &lt;압축&gt; &lt;폴더&gt;</c> — 재사용장치 계약)의 최종 감사 지적 둘을 실제 exe(이 빌드)로:
/// <list type="bullet">
/// <item>IX-F5: 없는 압축 경로·폴더·빈 문자열을 주면 예전엔 일반 실행으로 넘어가 메인 창과 '문제가 생겼어요' 창이 떠서 끝나지 않았다(재사용장치는 30분 기다림).
///   → 창 없이 쉬운 한국어 한 줄(표준 오류)·종료 코드 1('그 밖의 실패' — 재사용장치는 반디집으로 다시 풂, '압축 아님' 표지 없음)·오류 로그 'Headless extract failed: &lt;경로&gt;'.</item>
/// <item>IX-F2: tar 하드 링크를 0바이트 파일로 풀고 종료 0(말없이). 링크 복원은 결정 기록상 제외 → 풀린 파일은 그대로 두고 표준 오류 한 줄
///   '링크 N개는 내용 없이(0바이트) 만들었어요(b.txt → a.txt). …'·종료 코드 1(내용이 빠진 파일을 '성공'으로 넘기지 않게 — 재사용장치는 반디집으로 다시 풂)·오류 로그 줄.</item>
/// </list>
/// ★ 창이 뜰 수 있는 인자로는 exe 를 띄우지 않는다: 띄우기 전에 이 빌드의 판정(<see cref="StartupDecision.IsHeadlessExtract"/>)이 창 없이 풀기인지 먼저 본다
/// (고치기 전 판정이면 띄우지 않고 실패 — 사용자 화면에 창을 띄우는 시험 금지). exe 는 시험 자료 폴더로(<see cref="ModuleInit.ApplyTo"/>), 60초 제한.
/// </summary>
public class HeadlessNoticeTests
{
    private static readonly string[] NotArchiveMarkers = { "압축 파일이 아닌", "압축파일이 아닌", "not archive", "not an archive", "unsupported archive" };

    /// <summary>
    /// 계약(순수 판정 — 창을 띄우지 않음): 첫 인자가 '--extract-to' 면 압축 파일이 없거나 인자가 모자라도 창 없이 풀기다(뮤텍스와 상관없음).
    /// 인자 문제의 한 줄: 빈 압축·빈 폴더 → 사용법, 폴더를 줌 → '풀 압축 파일 대신 폴더를 받았어요: 경로', 없음 → '풀 압축 파일을 찾을 수 없어요: 경로', 맞으면 null.
    /// </summary>
    [Fact]
    public void 첫_인자가_extract_to_면_없는_경로도_창_없이_풀기다()
    {
        bool Exists(string p) => p == "있는.zip";
        bool IsDir(string p) => p == @"C:\폴더";
        foreach (bool mutex in new[] { true, false })
            foreach (var args in new[]
                     {
                         new[] { "--extract-to", "없는.zip", @"C:\곳" },
                         new[] { "--extract-to", @"C:\폴더", @"C:\곳" },
                         new[] { "--extract-to", "", @"C:\곳" },
                         new[] { "--extract-to", "있는.zip", "" },
                         new[] { "--extract-to", "있는.zip", " " },
                         new[] { "--extract-to", "있는.zip" },
                         new[] { "--extract-to" },
                         new[] { "--EXTRACT-TO", "없는.7z", @"C:\곳" },
                     })
            {
                Assert.Equal(StartupKind.Headless, StartupDecision.Decide(args, mutex, Exists));
                Assert.True(StartupDecision.IsHeadlessExtract(args));
            }
        Assert.False(StartupDecision.IsHeadlessExtract(new[] { "--elevated", "--extract-to", "없는.zip", @"C:\곳" }));
        Assert.False(StartupDecision.IsHeadlessExtract(new[] { "--extract-to-folder", "없는.zip" }));
        Assert.False(StartupDecision.IsHeadlessExtract(Array.Empty<string>()));

        string? P(params string[] a) => StartupDecision.HeadlessArgumentProblem(a, Exists, IsDir);
        Assert.Equal(StartupDecision.HeadlessUsageMessage, P("--extract-to", "", @"C:\곳"));
        Assert.Equal(StartupDecision.HeadlessUsageMessage, P("--extract-to", "있는.zip", " "));
        Assert.Equal(StartupDecision.HeadlessUsageMessage, P("--extract-to", "있는.zip"));
        Assert.Equal(StartupDecision.HeadlessUsageMessage, P("--extract-to"));
        Assert.Equal(StartupDecision.HeadlessFolderGivenMessage + @"C:\폴더", P("--extract-to", @"C:\폴더", @"C:\곳"));
        Assert.Equal(StartupDecision.HeadlessMissingArchiveMessage + "없는.zip", P("--extract-to", "없는.zip", @"C:\곳"));
        Assert.Null(P("--extract-to", "있는.zip", @"C:\곳"));
        foreach (var m in new[] { StartupDecision.HeadlessUsageMessage, StartupDecision.HeadlessFolderGivenMessage, StartupDecision.HeadlessMissingArchiveMessage })
        {
            Assert.True(ErrorCatalog.ContainsHangul(m), m);
            foreach (var marker in NotArchiveMarkers) Assert.DoesNotContain(marker, m, StringComparison.OrdinalIgnoreCase);
        }
    }

    /// <summary>
    /// 계약(B3 — 실제 exe): 없는 압축(.zip·.7z·.egg)·압축 대신 폴더·빈 압축 경로·빈 풀 폴더·인자 모자람이면 창 없이(60초 안에) 종료 코드 1,
    /// 표준 오류는 쉬운 한국어 한 줄('압축 아님' 표지 없음 — 재사용장치가 반디집으로 다시 풀 기회 유지), 풀 폴더에 아무것도 만들지 않는다.
    /// 없는 압축이면 오류 로그에 'Headless extract failed: &lt;그 경로&gt;'(재배포 뒤 지켜보기 명령이 찾는 줄)와 다음 줄 이유.
    /// </summary>
    [IljipExeFact]
    public async Task 창없이_풀기는_없는_경로나_빈_인자면_창_없이_1로_끝난다()
    {
        using var tmp = new TempDir("hlmiss");
        string folderGiven = tmp.Dir("받은 첨부 폴더");
        string missingZip = tmp.P("받은 첨부 " + Guid.NewGuid().ToString("N")[..8] + ".zip");
        var cases = new List<(string Name, string[] Args, string Expect)>
        {
            ("없는 zip", new[] { "--extract-to", missingZip, tmp.P("out1") }, StartupDecision.HeadlessMissingArchiveMessage + missingZip),
            ("없는 7z", new[] { "--extract-to", tmp.P("없는.7z"), tmp.P("out2") }, StartupDecision.HeadlessMissingArchiveMessage + tmp.P("없는.7z")),
            ("없는 egg", new[] { "--extract-to", tmp.P("없는.egg"), tmp.P("out3") }, StartupDecision.HeadlessMissingArchiveMessage + tmp.P("없는.egg")),
            ("폴더를 줌", new[] { "--extract-to", folderGiven, tmp.P("out4") }, StartupDecision.HeadlessFolderGivenMessage + folderGiven),
            ("빈 압축 경로", new[] { "--extract-to", "", tmp.P("out5") }, StartupDecision.HeadlessUsageMessage),
            ("빈 풀 폴더", new[] { "--extract-to", FixtureCatalog.Get("zip_default").FullPath, "" }, StartupDecision.HeadlessUsageMessage),
            ("인자 모자람", new[] { "--extract-to", missingZip }, StartupDecision.HeadlessUsageMessage),
        };

        foreach (var (name, args, expect) in cases)
        {
            var (code, err) = await RunIljip(args);
            Assert.True(code == 1, $"{name}: 종료 코드 {code}(1 이어야 함) — {err}");
            string line = err.Trim();
            Assert.Equal(expect, line);
            Assert.True(ErrorCatalog.ContainsHangul(line), $"{name}: 한국어가 아님 — {line}");
            Assert.DoesNotContain("\n", line);
            Assert.DoesNotContain("Headless", line);
            foreach (var m in NotArchiveMarkers)
                Assert.False(line.Contains(m, StringComparison.OrdinalIgnoreCase), $"{name}: '{m}' — 재사용장치가 반디집 재시도를 건너뜀: {line}");
        }
        foreach (var o in new[] { "out1", "out2", "out3", "out4", "out5" })
            Assert.False(Directory.Exists(tmp.P(o)), $"{o}: 풀 폴더를 만들었음");
        Assert.Empty(Directory.EnumerateFileSystemEntries(folderGiven));
        AssertLogged(missingZip, StartupDecision.HeadlessMissingArchiveMessage + missingZip);
    }

    /// <summary>
    /// 계약(B4 — 실제 exe): 하드 링크 든 GNU tar·tgz 를 창 없이 풀면 종료 코드 1, 표준 오류 한 줄 '링크 1개는 내용 없이(0바이트) 만들었어요(b.txt → a.txt). 일집은 링크를 되살리지 않아요.'
    /// ('압축 아님' 표지 없음 — 재사용장치는 반디집으로 다시 풂), 풀린 파일은 그대로(a.txt 12바이트·b.txt 0바이트), 오류 로그에 경로와 그 줄.
    /// 링크가 없는 tar 는 예전처럼 종료 코드 0·표준 오류 없음.
    /// </summary>
    [IljipExeFact]
    public async Task 창없이_풀기는_tar_링크를_알리고_1로_끝난다()
    {
        using var tmp = new TempDir("hllink");
        foreach (var ext in new[] { ".tar", ".tgz" })
        {
            string arc = ExtractSafetyNoticeTests.MakeHardLinkTar(tmp.Dir("in" + ext.TrimStart('.') + Guid.NewGuid().ToString("N")[..6]), ext);
            string dest = tmp.Dir("out" + ext.TrimStart('.'));
            var (code, err) = await RunIljip(new[] { "--extract-to", arc, dest });
            Assert.True(code == 1, $"{ext}: 종료 코드 {code}(1 이어야 함) — {err}");
            string line = err.Trim();
            Assert.Equal("링크 1개는 내용 없이(0바이트) 만들었어요(b.txt → a.txt). 일집은 링크를 되살리지 않아요.", line);
            foreach (var m in NotArchiveMarkers)
                Assert.False(line.Contains(m, StringComparison.OrdinalIgnoreCase), $"{ext}: '{m}': {line}");
            Assert.Equal(12, new FileInfo(Path.Combine(dest, "a.txt")).Length);
            Assert.Equal(0, new FileInfo(Path.Combine(dest, "b.txt")).Length);
            CollisionTests.NoLeftovers(dest, ext);
            AssertLogged(arc, line);
        }

        string plainDest = tmp.Dir("out_plain");
        var (code0, err0) = await RunIljip(new[] { "--extract-to", FixtureCatalog.Get("tar_default").FullPath, plainDest });
        Assert.True(code0 == 0, $"링크 없는 tar: 종료 코드 {code0} — {err0}");
        Assert.True(string.IsNullOrWhiteSpace(err0), err0);
    }

    /// <summary>시험 자료 폴더의 오류 로그에 'Headless extract failed: &lt;경로&gt;' 줄과 바로 다음 줄(이유)이 있다(자정 언저리를 위해 앞뒤 하루도 봄).</summary>
    private static void AssertLogged(string archivePath, string reason)
    {
        string logDir = Path.Combine(ModuleInit.TestAppDataRoot, "logs");
        string head = Iljip.App.HeadlessFailureLogHead + archivePath;
        DateTime osToday = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, TestTimeZone.OsLocal).Date;
        foreach (int day in new[] { 0, -1, 1 })
        {
            string file = Path.Combine(logDir, $"error-{osToday.AddDays(day):yyyyMMdd}.log");
            if (!File.Exists(file)) continue;
            using var fs = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var reader = new StreamReader(fs, Encoding.UTF8);
            string? line;
            while ((line = reader.ReadLine()) is not null)
            {
                if (!line.EndsWith(head, StringComparison.Ordinal)) continue;
                Assert.Equal(reason, reader.ReadLine());
                return;
            }
        }
        Assert.Fail($"오류 로그({logDir})에 '{head}' 줄이 없음");
    }

    /// <summary>Iljip.exe 를 실행하고 (종료 코드, 표준 출력+오류 — 재사용장치처럼 UTF-8 → CP949 순으로 읽음). 창 없이 풀기 판정이 아니면 띄우지 않는다.</summary>
    internal static async Task<(int Code, string Output)> RunIljip(string[] args)
    {
        Assert.True(StartupDecision.IsHeadlessExtract(args), "창 없이 풀기 판정이 아님 — 창이 뜰 수 있어 실행하지 않음: " + string.Join(" ", args));
        var psi = new ProcessStartInfo(IljipExe.Path!)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        ModuleInit.ApplyTo(psi);   // exe 의 로그·설정도 시험 자료 폴더로(사용자 진짜 로그에 쓰지 않게)
        foreach (var a in args) psi.ArgumentList.Add(a);
        using var p = Process.Start(psi)!;
        var outTask = ReadAll(p.StandardOutput.BaseStream);
        var errTask = ReadAll(p.StandardError.BaseStream);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        try
        {
            await p.WaitForExitAsync(cts.Token);
        }
        catch (OperationCanceledException)
        {
            try { p.Kill(entireProcessTree: true); } catch { /* 이미 끝남 */ }
            Assert.Fail("Iljip.exe 가 60초 안에 끝나지 않음(창이 떴을 수 있음): " + string.Join(" ", args));
        }
        byte[] raw = (await outTask).Concat(await errTask).ToArray();
        return (p.ExitCode, Decode(raw));
    }

    private static async Task<byte[]> ReadAll(Stream s)
    {
        using var ms = new MemoryStream();
        await s.CopyToAsync(ms);
        return ms.ToArray();
    }

    private static string Decode(byte[] raw)
    {
        try { return new UTF8Encoding(false, throwOnInvalidBytes: true).GetString(raw); }
        catch (DecoderFallbackException) { return Encoding.GetEncoding(949).GetString(raw); }
    }
}
