using System.Diagnostics;
using System.Text;
using Iljip.Tests.TestSupport;

namespace Iljip.Tests.Contract;

/// <summary>
/// 재사용장치(D:\Python\재사용장치\System_code\재사용장치_processor.py)가 일집에 기대는 약속 — 통합 점검(2026-10-12)에서 일집-dist 재배포 전에 고정.
/// 재사용장치는 메일 첨부의 egg·alz(파이썬 라이브러리가 없으면 7z·rar 도)를 <c>Iljip.exe --extract-to &lt;압축&gt; &lt;임시폴더&gt;</c> 로 풀고:
/// <list type="bullet">
/// <item>종료 코드 0 이면 임시 폴더 내용을 가져간다.</item>
/// <item>0 이 아니면 출력(표준 오류)에 '압축 파일이 아닌'·'압축파일이 아닌'·'not archive'·'not an archive'·'unsupported archive' 가 있으면
///   '압축 파일 아님'으로 보고 반디집으로 다시 풀지 않는다. 그 밖의 실패는 반디집으로 다시 푼다(491행).</item>
/// </list>
/// 그래서 일집의 실패 문구에 저 말이 들어가면 멀쩡한(암호·손상) 압축을 반디집으로 다시 풀 기회가 사라진다.
/// 실제 Iljip.exe(이 빌드)를 창 없이 띄워 확인한다 — 일집-dist 를 바꾸면 매일 egg 해제가 이 동작으로 바뀐다.
/// </summary>
public class ReuseDeviceContractTests
{
    /// <summary>재사용장치가 '압축 파일 아님'으로 분류하는 말(processor.py 491~493행과 같게).</summary>
    private static readonly string[] NotArchiveMarkers = { "압축 파일이 아닌", "압축파일이 아닌", "not archive", "not an archive", "unsupported archive" };

    /// <summary>계약: egg·alz·rar(RAR4·RAR5)·분할 egg 를 창 없이 풀고 종료 코드 0, 내용·한글 이름이 맞다.</summary>
    [IljipExeFact]
    public async Task 창없이_풀기는_EGG_ALZ_RAR도_푼다()
    {
        using var tmp = new TempDir("rdok");
        var cases = new List<(string Name, string Path, List<ExpectedEntry> Expected)>();
        foreach (var kind in new[] { "egg", "alz", "rar5", "rar4", "rar4_cp949" })
        {
            var b = SyntheticArchives.Build(kind, tmp.Dir("in_" + kind));
            cases.Add((kind, b.Path, b.Expected));
        }
        // 분할 egg(.vol1.egg ~ .vol3.egg) — 첫 조각만 넘긴다
        var src = tmp.Dir("split_src", "분할 자료");
        File.WriteAllText(Path.Combine(src, "견적서.txt"), "분할 견적서 합성 데이터");
        var big = new byte[150_000];
        new Random(5).NextBytes(big);
        File.WriteAllBytes(Path.Combine(src, "난수.bin"), big);
        var parts = RawEgg.WriteSplit(Path.Combine(tmp.Dir("in_split"), "분할"), RawEgg.FromFolder(src), parts: 3);
        cases.Add(("egg_split", parts[0], SampleTree.ExpectedOf(src)));

        foreach (var (name, path, expected) in cases)
        {
            string dest = tmp.Dir("out_" + name);   // 재사용장치는 tempfile.mkdtemp() 로 만든 빈 폴더를 넘긴다
            var (code, err) = await RunIljip(path, dest);
            Assert.True(code == 0, $"{name}: 종료 코드 {code} — {err}");
            TreeAssert.ExtractedTreeEquals(dest, expected, context: name);
        }
    }

    /// <summary>
    /// 계약: 암호·손상으로 못 풀면 종료 코드 1, 임시 폴더에 파일을 남기지 않고, 실패 문구가 '압축 파일 아님'으로 오인되지 않는다(반디집으로 다시 풀 기회 유지).
    /// 예전 배포본은 잘린 egg 를 종료 코드 0(짧은 파일)으로 끝냈다 — 이제는 1 이라 재사용장치가 반디집으로 다시 푼다.
    /// </summary>
    [IljipExeFact]
    public async Task 창없이_풀기가_실패하면_1이고_압축아님으로_오인되지_않는다()
    {
        using var tmp = new TempDir("rdng");
        var cases = new List<(string Name, string Path)>();
        foreach (var kind in new[] { "egg_enc", "rar4_enc", "rar5_enc", "rar5_hdr", "rar4_hdr" })
            cases.Add((kind, SyntheticArchives.Build(kind, tmp.Dir("in_" + kind)).Path));

        // 끝이 잘린 egg(받다 만 첨부)
        var whole = SyntheticArchives.Build("egg", tmp.Dir("in_cut"));
        var bytes = File.ReadAllBytes(whole.Path);
        File.WriteAllBytes(whole.Path, bytes[..(bytes.Length - 60_000)]);
        cases.Add(("egg_truncated", whole.Path));

        foreach (var (name, path) in cases)
        {
            string dest = tmp.Dir("out_" + name);
            var (code, err) = await RunIljip(path, dest);
            Assert.True(code == 1, $"{name}: 종료 코드 {code}(1 이어야 함) — {err}");
            Assert.False(string.IsNullOrWhiteSpace(err), $"{name}: 실패 이유가 출력되지 않음");
            foreach (var m in NotArchiveMarkers)
                Assert.False(err.Contains(m, StringComparison.OrdinalIgnoreCase), $"{name}: 실패 문구에 '{m}' — 재사용장치가 '압축 파일 아님'으로 보고 반디집 재시도를 건너뜀: {err}");
            if (!name.EndsWith("truncated", StringComparison.Ordinal))
                TreeAssert.NoFiles(dest, name);   // 암호 오류는 만든 것을 모두 지운다(잘린 파일은 앞서 다 풀린 파일이 남을 수 있음 — 재사용장치는 실패면 임시 폴더를 버림)
        }
    }

    /// <summary>
    /// 계약(W2-02, I1-7): 목록까지 잠긴 압축(일집·py7zr 이 만든 헤더 암호 7z, 머리 암호 RAR5)을 창 없이 풀면 암호를 묻지 않고(창 없음 — 60초 안에 끝남)
    /// 종료 코드 1, 실패 문구는 '파일 목록까지 암호로 잠긴…'(한국어 한 줄)이며 '압축 파일 아님' 표지가 없고, 임시 폴더에 아무것도 남지 않는다.
    /// (헤드리스는 설정을 읽지 않고 암호를 명령줄로 받지 않는다 — 재사용장치는 실패면 반디집으로 다시 푼다)
    /// </summary>
    [IljipExeFact]
    public async Task 창없이_풀기는_목록까지_잠긴_압축이면_묻지_않고_1로_끝난다()
    {
        using var tmp = new TempDir("rdlist");
        var cases = new List<(string Name, string Path)>();
        string src = SampleTree.Create(tmp.Dir("원본"));
        string iljip = tmp.P("일집_이름암호.7z");
        await new Iljip.Services.SevenZipArchiveService().CompressAsync(new[] { src }, iljip,
            new Iljip.Models.CompressionOptions { Password = FixtureCatalog.KoreanPassword, EncryptFileNames = true });
        cases.Add(("iljip7z", iljip));
        cases.Add(("py7z", Py7zrSamples.Write(tmp.Dir("in_py7z"), koreanPassword: false).Path));
        cases.Add(("rar5_hdr", SyntheticArchives.Build("rar5_hdr", tmp.Dir("in_rar5_hdr")).Path));

        foreach (var (name, path) in cases)
        {
            string dest = tmp.Dir("out_" + name);
            var (code, err) = await RunIljip(path, dest);
            Assert.True(code == 1, $"{name}: 종료 코드 {code}(1 이어야 함) — {err}");
            Assert.Contains(Iljip.Services.ListPasswordRequiredException.ListMessage, err);
            foreach (var m in NotArchiveMarkers)
                Assert.False(err.Contains(m, StringComparison.OrdinalIgnoreCase), $"{name}: 실패 문구에 '{m}': {err}");
            TreeAssert.NoFiles(dest, name);
        }
    }

    /// <summary>
    /// 계약(소스): 본체의 문구에 재사용장치가 '압축 파일 아님'으로 읽는 말이 없다 — ArchiveExceptions.cs 머리 주석의 약속을 테스트로.
    /// 예외: App.xaml.cs 의 'Unsupported archive type'(종료 코드 2, 확장자 자체를 모를 때 — 재사용장치가 일집에 넘기는 egg·alz·rar·7z 는 모두 등록됨).
    /// </summary>
    [Fact]
    public void 본체_문구에_압축아님으로_읽히는_말이_없다()
    {
        var hits = new List<string>();
        foreach (var file in Directory.EnumerateFiles(RepoFiles.PathOf("Iljip"), "*.cs", SearchOption.AllDirectories))
        {
            string rel = Path.GetRelativePath(RepoFiles.Root, file);
            if (rel.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}") || rel.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")) continue;
            int lineNo = 0;
            foreach (var line in File.ReadLines(file))
            {
                lineNo++;
                string code = line.TrimStart();
                if (code.StartsWith("//", StringComparison.Ordinal) || code.StartsWith("///", StringComparison.Ordinal)) continue;   // 주석(경고 문구 자체)은 제외
                if (rel.EndsWith("App.xaml.cs", StringComparison.Ordinal) && line.Contains("Unsupported archive type", StringComparison.Ordinal)) continue;
                // 문자열 안만 본다(코드의 'is not ArchiveException' 같은 것은 문구가 아님)
                foreach (System.Text.RegularExpressions.Match lit in System.Text.RegularExpressions.Regex.Matches(line, "\"(?:[^\"\\\\]|\\\\.)*\""))
                    foreach (var m in NotArchiveMarkers)
                        if (lit.Value.Contains(m, StringComparison.OrdinalIgnoreCase)) hits.Add($"{rel}:{lineNo}: {code}");
            }
        }
        Assert.True(hits.Count == 0, "재사용장치가 '압축 파일 아님'으로 읽는 문구:\n  " + string.Join("\n  ", hits));
    }

    /// <summary>Iljip.exe --extract-to 를 실행하고 (종료 코드, 표준 출력+오류 — 재사용장치처럼 UTF-8 → CP949 순으로 읽음).</summary>
    private static async Task<(int Code, string Output)> RunIljip(string archive, string dest)
    {
        Assert.True(File.Exists(archive), "창 없이 풀기 조건이 아님 — 창이 뜰 수 있어 실행하지 않음");
        var psi = new ProcessStartInfo(IljipExe.Path!)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        psi.ArgumentList.Add("--extract-to");
        psi.ArgumentList.Add(archive);
        psi.ArgumentList.Add(dest);
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
            Assert.Fail("Iljip.exe 가 60초 안에 끝나지 않음(창이 떴을 수 있음)");
        }
        byte[] raw = (await outTask).Concat(new byte[] { (byte)'\n' }).Concat(await errTask).ToArray();
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
