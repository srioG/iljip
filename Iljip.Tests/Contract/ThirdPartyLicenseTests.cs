using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using Iljip.Services;
using Iljip.Tests.TestSupport;

namespace Iljip.Tests.Contract;

/// <summary>
/// [IJ3-03 — I2-15] 7-Zip 라이선스 원문 대조: 사용자가 허락해 7-zip.org 에서 받은 License.txt(6031바이트, SHA-256 3A184AA1…, 'Copyright (C) 1999-2025')를
/// <c>Iljip\licenses\7-Zip-License.txt</c> 로 바이트 그대로 넣고, 예전에 기억으로 적었던 BSD·unRAR 문구(저작권 연도 없는 줄·소스 파일 이름·'public domain' 줄·
/// 1999-2026)를 원문으로 바로잡았다. BSD 부분 파일·안내(THIRD-PARTY-NOTICES.txt)의 7-Zip 부분은 원문 줄을 그대로 잘라 옮긴 것이어야 한다.
/// 동봉 7z.dll 은 26.03 이라 원문의 연도(1999-2025)와 다르다 — 원문은 고치지 않고 안내에 판 정보를 덧붙인다.
/// 안내와 라이선스 파일이 빌드 출력·발행(publish) 출력에 같은 내용으로 들어가는지도 본다(LGPL·BSD 는 바이너리와 함께 고지해야 함).
/// 기존 고지 계약(파일 목록·'일집 정보' 창 글)은 <see cref="SevenZipLibraryTests"/>.
/// </summary>
public class ThirdPartyLicenseTests
{
    /// <summary>받은 원문의 SHA-256(CRLF 그대로 — 7-zip.org 의 License.txt).</summary>
    private const string OriginalSha256 = "3A184AA13DC8AD30734E28FF901B478CCBCB5B41BE52427A5E7609C8FD9E5DDB";

    private static string LicensePath(string name) => RepoFiles.PathOf("Iljip", "licenses", name);

    /// <summary>줄 끝을 CRLF 로 맞춘 글(git 이 LF 로 꺼낸 작업 폴더에서도 같은 값이 되게).</summary>
    private static string Crlf(string text) => text.Replace("\r\n", "\n").Replace("\n", "\r\n");

    private static string ReadCrlf(string path) => Crlf(File.ReadAllText(path, new UTF8Encoding(false)));

    private static string Original => ReadCrlf(LicensePath("7-Zip-License.txt"));

    /// <summary>계약: 7-Zip-License.txt 는 받은 원문 그대로다(크기·해시·저작권 줄). 줄 끝만 CRLF 로 맞춰 비교한다.</summary>
    [Fact]
    public void 라이선스_7Zip_파일은_받은_원문_그대로다()
    {
        byte[] bytes = Encoding.ASCII.GetBytes(Original);
        Assert.Equal(6031, bytes.Length);
        Assert.Equal(OriginalSha256, Convert.ToHexString(SHA256.HashData(bytes)));
        Assert.Contains("  7-Zip Copyright (C) 1999-2025 Igor Pavlov.\r\n", Original);
        Assert.Contains("Redistributions in binary form must reproduce related license information from this file.", Original);
        Assert.False(File.ReadAllBytes(LicensePath("7-Zip-License.txt")).Take(3).SequenceEqual(new byte[] { 0xEF, 0xBB, 0xBF }), "원문에 없는 BOM 이 붙음");
    }

    /// <summary>
    /// 계약: BSD 3·2조항 파일은 원문에서 그 절을 그대로 자른 것이다(머리말·소스 파일 이름을 더하지 않음) — 저작권 줄은 원문의 연도 그대로
    /// ('Copyright (c) 2023-2025 Igor Pavlov.' — 예전 기억본은 연도 없이 'Copyright (c) Igor Pavlov.').
    /// </summary>
    [Theory]
    [InlineData("7-Zip-BSD-3-Clause.txt", "BSD 3-clause License in 7-Zip code", "Copyright (c) 2015-2016, Apple Inc. All rights reserved.")]
    [InlineData("7-Zip-BSD-2-Clause.txt", "BSD 2-clause License in 7-Zip code", "Copyright (c) 2012-2021 Yann Collet.")]
    public void BSD_부분_파일은_원문의_그_절을_그대로_자른_것이다(string file, string firstLine, string copyright)
    {
        string body = ReadCrlf(LicensePath(file));
        Assert.StartsWith(firstLine + "\r\n", body);
        Assert.EndsWith("SOFTWARE, EVEN IF ADVISED OF THE POSSIBILITY OF SUCH DAMAGE.\r\n", body);
        Assert.True(Original.Contains(body, StringComparison.Ordinal), $"{file}: 원문에 그대로 있는 글이 아님(옮겨 적다 바뀜)");
        Assert.Contains(copyright, body);
        Assert.Contains("  Copyright (c) 2023-2025 Igor Pavlov.\r\n", body);
        foreach (var memo in new[] { "Copyright (c) Igor Pavlov.", "LzfseDecoder.cpp", "ZstdDec.c", "Xxh64.c", "—" })
            Assert.DoesNotContain(memo, body);
    }

    /// <summary>
    /// 계약: 안내(THIRD-PARTY-NOTICES.txt)의 7-Zip 부분 — 원문의 '파일별 라이선스' 머리와 unRAR 제한을 원문 줄 그대로 싣고, 원문 전체 파일을 가리키며,
    /// 원문 연도(1999-2025)와 동봉 판(26.03)이 다르다는 판 안내를 덧붙인다. 예전에 기억으로 적은 말(1999-2026·'public domain'·소스 파일 이름·요약 문장)은 없다.
    /// </summary>
    [Fact]
    public void 안내의_7_Zip_부분은_원문_줄을_그대로_싣고_판_안내를_덧붙인다()
    {
        string notices = ReadCrlf(RepoFiles.PathOf("Iljip", ThirdPartyNotices.NoticesFileName));
        string[] lines = Original.Split("\r\n");
        string Block(string first, string last)
        {
            int i = Array.FindIndex(lines, l => l.StartsWith(first, StringComparison.Ordinal));
            int j = Array.FindIndex(lines, i, l => l == last);
            Assert.True(i >= 0 && j >= i, $"원문에서 '{first}' 절을 못 찾음");
            return string.Join("\r\n", lines[i..(j + 1)]);
        }
        string head = Block("  7-Zip Copyright (C)", "    organization. You don't need to register or pay for 7-Zip.");
        string unrar = Block("unRAR license restriction", "  not be used to develop a RAR (WinRAR) compatible archiver.");
        Assert.Contains(head, notices);
        Assert.Contains(unrar, notices);
        Assert.Contains("licenses\\7-Zip-License.txt", notices);
        Assert.Contains("\"1999-2025\"", notices);
        Assert.Contains("the bundled 7z.dll is the later version 26.03", notices);
        Assert.Contains("함께 넣은 7z.dll 은 그 뒤 판인 26.03", notices);
        foreach (var memo in new[] { "1999-2026", "public domain", "LzfseDecoder.cpp", "ZstdDec.c", "Xxh64.c", "Rar* files", "summary of the 7-Zip License.txt",
                                     "is included in the 7-Zip source package" })
            Assert.DoesNotContain(memo, notices);
    }

    /// <summary>
    /// 계약: 빌드 출력(일집 본체 출력 폴더 — 재배포의 원본, 그리고 시험 출력)에 안내와 라이선스 파일이 저장소와 같은 내용으로 모두 들어간다(7-Zip-License.txt 포함).
    /// </summary>
    [Fact]
    public void 빌드_출력에_안내와_라이선스가_그대로_들어간다()
    {
        string iljipOut = IljipBuildOutput();
        Assert.True(File.Exists(Path.Combine(iljipOut, "Iljip.dll")), "일집 본체 빌드 출력을 찾지 못함: " + iljipOut);
        // 빌드 출력은 증분 빌드라 예전 판의 파일이 남을 수 있어 개수는 보지 않는다(발행 시험은 새 폴더라 개수까지)
        AssertNoticesCopied(iljipOut, "빌드 출력", exactSet: false);
        AssertNoticesCopied(AppContext.BaseDirectory, "시험 출력", exactSet: false);
    }

    /// <summary>
    /// 계약: 발행(dotnet publish — packaging\Build.ps1·일집-dist 재배포가 쓰는 길) 출력에도 안내와 라이선스 파일이 같은 내용으로 모두 들어간다.
    /// 이미 빌드된 본체를 다시 빌드하지 않고(--no-build — 인터넷·NuGet 받기 없음) 시험 임시 폴더에 발행해 본다(저장소 안 bin 에는 아무것도 만들지 않음).
    /// </summary>
    [Fact]
    public async Task 발행_출력에도_안내와_라이선스가_그대로_들어간다()
    {
        string configuration = new DirectoryInfo(AppContext.BaseDirectory.TrimEnd('\\', '/')).Parent!.Name;   // …\bin\<구성>\<TFM>\
        using var tmp = new TempDir("publish");
        string outDir = tmp.P("발행");
        string dotnet = Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") is { Length: > 0 } host && File.Exists(host) ? host : "dotnet";
        async Task<(int Code, string Output)> Publish()
        {
            var psi = new ProcessStartInfo(dotnet)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            foreach (var a in new[] { "publish", RepoFiles.PathOf("Iljip", "Iljip.csproj"), "-c", configuration, "--no-build", "-o", outDir, "-nologo", "-v", "q" })
                psi.ArgumentList.Add(a);
            psi.Environment["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1";
            psi.Environment["DOTNET_NOLOGO"] = "1";
            using var p = Process.Start(psi)!;
            var stdout = p.StandardOutput.ReadToEndAsync();
            var stderr = p.StandardError.ReadToEndAsync();
            using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(3));
            try
            {
                await p.WaitForExitAsync(cts.Token);
            }
            catch (OperationCanceledException)
            {
                try { p.Kill(entireProcessTree: true); } catch { /* 이미 끝남 */ }
                Assert.Fail("dotnet publish 가 3분 안에 끝나지 않음");
            }
            return (p.ExitCode, $"{await stdout}\n{await stderr}");
        }
        // 3차 통합 점검: UTC 전체 실행에서 한 번 'MSB3491 … obj\…\PublishOutputs.*.txt — 다른 프로세스가 사용 중'으로 실패(회사 PC 보안 프로그램이
        // 막 쓴 파일을 검사하는 순간 — 같은 시험이 Debug·Release 에서는 통과). 그 잠깐 잠김일 때만 두 번 더 해 본다(판정은 그대로).
        var (code, output) = await Publish();
        for (int retry = 0; retry < 2 && code != 0 && output.Contains("MSB3491", StringComparison.Ordinal); retry++)
        {
            await Task.Delay(1000);
            (code, output) = await Publish();
        }
        Assert.True(code == 0, $"dotnet publish 실패({code}):\n{output}");
        Assert.True(File.Exists(Path.Combine(outDir, "Iljip.exe")), "발행 출력에 Iljip.exe 가 없음");
        Assert.True(File.Exists(Path.Combine(outDir, "x64", "7z.dll")), "발행 출력에 x64\\7z.dll 이 없음");
        AssertNoticesCopied(outDir, "발행 출력", exactSet: true);
        // 저장소 안 bin 에는 아무것도 생기지 않았다(재사용장치가 먼저 집어 감 — BuildOutputTests)
        foreach (var config in new[] { "Release", "Debug" })
        {
            string bin = RepoFiles.PathOf("Iljip", "bin", config);
            Assert.False(Directory.Exists(bin) && Directory.EnumerateFiles(bin, "Iljip.exe", SearchOption.AllDirectories).Any(), "저장소 안에 Iljip.exe 가 생김: " + bin);
        }
    }

    /// <summary>계약: 본체 프로젝트가 안내·라이선스 파일을 빌드 출력과 발행 출력 둘 다에 넣고, 단일 파일로 묶지 않게 적혀 있다.</summary>
    [Fact]
    public void 프로젝트가_안내와_라이선스를_빌드와_발행에_넣게_적혀_있다()
    {
        string csproj = File.ReadAllText(RepoFiles.PathOf("Iljip", "Iljip.csproj"));
        Assert.Contains("<None Update=\"THIRD-PARTY-NOTICES.txt\" CopyToOutputDirectory=\"PreserveNewest\" CopyToPublishDirectory=\"PreserveNewest\" ExcludeFromSingleFile=\"true\" />", csproj);
        Assert.Contains("<None Update=\"licenses\\*.txt\" CopyToOutputDirectory=\"PreserveNewest\" CopyToPublishDirectory=\"PreserveNewest\" ExcludeFromSingleFile=\"true\" />", csproj);
    }

    /// <summary><paramref name="dir"/> 에 안내와 저장소의 모든 라이선스 파일이 바이트까지 같게 있는지(<paramref name="exactSet"/> 면 다른 라이선스 파일도 없어야 함).</summary>
    private static void AssertNoticesCopied(string dir, string context, bool exactSet)
    {
        string notices = Path.Combine(dir, ThirdPartyNotices.NoticesFileName);
        Assert.True(File.Exists(notices), $"{context}: {ThirdPartyNotices.NoticesFileName} 가 없음 — {dir}");
        Assert.Equal(File.ReadAllBytes(RepoFiles.PathOf("Iljip", ThirdPartyNotices.NoticesFileName)), File.ReadAllBytes(notices));
        var sources = Directory.GetFiles(RepoFiles.PathOf("Iljip", "licenses"), "*.txt");
        Assert.Contains(sources, s => Path.GetFileName(s) == "7-Zip-License.txt");
        foreach (var src in sources)
        {
            string copied = Path.Combine(dir, ThirdPartyNotices.LicensesFolderName, Path.GetFileName(src));
            Assert.True(File.Exists(copied), $"{context}: 라이선스 파일이 없음 — {copied}");
            Assert.True(File.ReadAllBytes(src).SequenceEqual(File.ReadAllBytes(copied)), $"{context}: 라이선스 파일 내용이 저장소와 다름 — {copied}");
        }
        if (exactSet)
            Assert.Equal(sources.Length, Directory.GetFiles(Path.Combine(dir, ThirdPartyNotices.LicensesFolderName), "*.txt").Length);
    }

    /// <summary>일집 본체의 빌드 출력 폴더(<c>&lt;출력 루트&gt;\Iljip\bin\&lt;구성&gt;\&lt;TFM&gt;\</c> — 시험 출력 폴더의 형제, <see cref="IljipExe"/> 와 같은 규칙).</summary>
    private static string IljipBuildOutput()
    {
        var tfm = new DirectoryInfo(AppContext.BaseDirectory.TrimEnd('\\', '/'));
        var cfg = tfm.Parent!;
        var root = cfg.Parent!.Parent!.Parent!;
        return Path.Combine(root.FullName, "Iljip", "bin", cfg.Name, tfm.Name);
    }
}
