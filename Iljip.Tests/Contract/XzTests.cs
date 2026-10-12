using System.Diagnostics;
using System.Text;
using Iljip.Services;
using Iljip.Tests.TestSupport;

namespace Iljip.Tests.Contract;

/// <summary>
/// [W2-01, 1차 W1-B 검증 지적 (b)] 끝이 잘린 xz 에서 CPU 100%·끝없는 대기(취소도 안 먹고 창 없이 풀기 --extract-to 가 영영 안 끝남)가 없다.
/// 원인(실측): SharpCompress 0.50.5 의 XZStream 이 블록 채움·검사값 구간이 잘린 xz 의 Read 안에서 끝없이 돈다
/// (단독 xz 301,000바이트를 300,965 에서, tar.xz 203,008바이트를 202,975 에서 자르면 재현 — 끝에서 33~35바이트 앞).
/// W2-01: XZStream 은 코드 어디에서도 쓰지 않고 xz 는 7z.dll 로만 푼다. 열 때 꼬리(스트림 끝 'YZ' + 확인값)를 먼저 봐서 잘림을 바로 알린다.
/// 원본은 1차 실험과 비슷한 모양(앞 절반 난수 + 뒤 절반 반복 글)으로 그때그때 만든다(<see cref="SevenZipTestArchives"/>).
/// </summary>
public class XzTests
{
    /// <summary>잘린 파일 하나를 다루는 데 넉넉한 한도(실제로는 수십 ms). 넘으면 '끝없이 도는' 회귀.</summary>
    private static readonly TimeSpan Limit = TimeSpan.FromSeconds(10);

    private static string MakeData(TempDir tmp, string name, int size)
    {
        var data = new byte[size];
        new Random(20261013).NextBytes(data.AsSpan(0, size / 2));
        var text = Encoding.UTF8.GetBytes("일집 xz 잘림 시험 0123456789\n");
        for (int i = size / 2; i < size; i++) data[i] = text[(i - size / 2) % text.Length];
        string p = Path.Combine(tmp.Dir("src"), name);
        File.WriteAllBytes(p, data);
        return p;
    }

    /// <summary>끝쪽 120바이트는 한 바이트씩(1차 실험의 멈춤 자리 포함), 앞쪽은 고르게 30곳.</summary>
    private static IEnumerable<int> Cuts(int length) =>
        Enumerable.Range(length - 120, 120).Concat(Enumerable.Range(1, 30).Select(k => (int)((long)length * k / 31)))
                  .Where(c => c > 0 && c < length).Distinct().OrderBy(c => c);

    /// <summary>
    /// 계약: 끝이 잘린 단독 .xz 는 어디서 잘려도 금방(10초 안) '끝부분이 잘려' 로 알리고 아무것도 남기지 않는다(새로 만든 대상 폴더도).
    /// 목록 보기도 금방 같은 오류로 끝난다. 온전한 것은 그대로 풀린다(이름 = 압축 이름에서 .xz 를 뗀 것).
    /// </summary>
    [Fact]
    public async Task 끝이_잘린_xz는_어디서_잘려도_금방_잘렸다고_알리고_아무것도_남기지_않는다()
    {
        using var tmp = new TempDir("xzcut");
        string src = MakeData(tmp, "자료.bin", 600_000);
        string xz = SevenZipTestArchives.Xz(src, tmp.P("자료.bin.xz"));
        var bytes = File.ReadAllBytes(xz);
        string full = tmp.P("full");
        await Svc.For(xz).ExtractAsync(xz, full).WaitAsync(Limit);
        Assert.Equal(new[] { "자료.bin" }, TreeAssert.Files(full));
        Assert.Equal(TreeAssert.Sha256(src), TreeAssert.Sha256(Path.Combine(full, "자료.bin")));

        foreach (int cut in Cuts(bytes.Length))
        {
            string arc = tmp.P($"자료{cut}.bin.xz");
            File.WriteAllBytes(arc, bytes[..cut]);
            string parent = tmp.P($"새 폴더 {cut}");
            var ex = await Svc.Catch(() => Svc.For(arc).ExtractAsync(arc, Path.Combine(parent, "out"))).WaitAsync(Limit);
            var corrupt = Assert.IsAssignableFrom<CorruptArchiveException>(ex);
            Assert.True(corrupt.IsTruncated, $"xz {cut}/{bytes.Length}: 잘림으로 알리지 않음 — {corrupt.Message}");
            Assert.False(Internals.IsPasswordRelated(ex!));
            Assert.False(Directory.Exists(parent), $"xz {cut}/{bytes.Length}: 대상에 무언가 남음");
            var listEx = await Svc.Catch(() => Svc.For(arc).ListEntriesAsync(arc)).WaitAsync(Limit);
            Assert.IsAssignableFrom<CorruptArchiveException>(listEx);
        }
    }

    /// <summary>
    /// 계약: 끝이 잘린 .tar.xz·.txz 도 어디서 잘려도 금방 '끝부분이 잘려' 로 알리고 아무것도 남기지 않는다(안쪽 tar 를 펴는 처리기 — 설계 §4).
    /// </summary>
    [Theory]
    [InlineData(".tar.xz")]
    [InlineData(".txz")]
    public async Task 끝이_잘린_tar_xz도_금방_잘렸다고_알리고_아무것도_남기지_않는다(string ext)
    {
        using var tmp = new TempDir("txzcut");
        string src = MakeData(tmp, "자료.bin", 400_000);
        string tar = tmp.P("묶음.tar");
        await new TarArchiveService().CompressAsync(new[] { src }, tar);
        string txz = SevenZipTestArchives.Xz(tar, tmp.P("묶음" + ext));
        var bytes = File.ReadAllBytes(txz);
        string full = tmp.P("full");
        await Svc.For(txz).ExtractAsync(txz, full).WaitAsync(Limit);
        Assert.Equal(new[] { "자료.bin" }, TreeAssert.Files(full));   // 안쪽 tar 의 내용까지 바로 펼침(.tar 파일 아님)

        foreach (int cut in Cuts(bytes.Length))
        {
            string arc = tmp.P($"t{cut}{ext}");
            File.WriteAllBytes(arc, bytes[..cut]);
            string parent = tmp.P($"새 폴더 {cut}");
            var ex = await Svc.Catch(() => Svc.For(arc).ExtractAsync(arc, Path.Combine(parent, "out"))).WaitAsync(Limit);
            var corrupt = Assert.IsAssignableFrom<CorruptArchiveException>(ex);
            Assert.True(corrupt.IsTruncated, $"{ext} {cut}/{bytes.Length}: 잘림으로 알리지 않음 — {corrupt.Message}");
            Assert.False(Directory.Exists(parent), $"{ext} {cut}/{bytes.Length}: 대상에 무언가 남음");
            Assert.IsAssignableFrom<CorruptArchiveException>(await Svc.Catch(() => Svc.For(arc).ListEntriesAsync(arc)).WaitAsync(Limit));
        }
    }

    /// <summary>
    /// 계약: 큰 xz·tar.xz 풀기를 도중에 취소하면 2초 안에 멈추고(OperationCanceledException), 쓰던 파일·임시 폴더·새로 만든 대상 폴더를 남기지 않는다.
    /// (7z.dll 은 출력 스트림이 던진 취소를 받아 멈춘다 — 실측 1초 안)
    /// </summary>
    [Theory]
    [InlineData(".xz")]
    [InlineData(".tar.xz")]
    public async Task 큰_xz_풀기를_취소하면_2초_안에_멈추고_아무것도_남기지_않는다(string ext)
    {
        using var tmp = new TempDir("xzcancel");
        string src = MakeData(tmp, "큰.bin", 24 * 1024 * 1024);
        string input = src;
        if (ext == ".tar.xz")
        {
            input = tmp.P("큰.tar");
            await new TarArchiveService().CompressAsync(new[] { src }, input);
        }
        string arc = SevenZipTestArchives.Xz(input, tmp.P("큰" + (ext == ".xz" ? ".bin.xz" : ext)));

        using var cts = new CancellationTokenSource();
        var sinceCancel = new Stopwatch();
        var progress = new ProgressRecorder(p =>
        {
            if (p.BytesProcessed > 0 && !cts.IsCancellationRequested)
            {
                sinceCancel.Start();
                cts.Cancel();
            }
        });
        string parent = tmp.P("새 폴더");
        var ex = await Svc.Catch(() => Svc.For(arc).ExtractAsync(arc, Path.Combine(parent, "out"), null, progress, cts.Token))
                          .WaitAsync(TimeSpan.FromSeconds(30));
        sinceCancel.Stop();
        Assert.IsAssignableFrom<OperationCanceledException>(ex);
        Assert.True(sinceCancel.Elapsed < TimeSpan.FromSeconds(2), $"{ext}: 취소 뒤 {sinceCancel.Elapsed.TotalSeconds:F1}초 걸림");
        Assert.False(Directory.Exists(parent), $"{ext}: 취소했는데 대상에 무언가 남음");
    }

    /// <summary>
    /// 계약(화면 흐름): 진행 창 [취소](가짜 창의 '처음 바이트에서 취소')로 큰 xz 풀기를 멈추면 2초 안에 '압축 해제 취소됨'이 되고
    /// 오류 창 없이, 대상에 아무것도 남지 않는다.
    /// </summary>
    [Fact]
    public async Task 화면에서_큰_xz_풀기를_취소하면_2초_안에_멈춘다()
    {
        using var tmp = new TempDir("xzvm");
        string src = MakeData(tmp, "큰.bin", 24 * 1024 * 1024);
        string xz = SevenZipTestArchives.Xz(src, tmp.P("큰.bin.xz"));
        var ui = new FakeUserInteraction { CancelOnFirstBytes = true };
        var vm = new Iljip.ViewModels.MainViewModel(ui);
        string parent = tmp.P("새 폴더");
        var sw = Stopwatch.StartNew();
        bool done = await vm.ExtractToFolderAsync(xz, Path.Combine(parent, "out")).WaitAsync(TimeSpan.FromSeconds(30));
        sw.Stop();
        Assert.False(done);
        Assert.Equal("압축 해제 취소됨", vm.StatusText);
        Assert.Empty(ui.Errors);
        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(2), $"취소까지 {sw.Elapsed.TotalSeconds:F1}초");
        Assert.False(Directory.Exists(parent), "취소했는데 대상에 무언가 남음");
    }

    /// <summary>
    /// 계약: 잘린 xz 의 내용 판별(안이 tar 인지 — 열기 창·파일 연결·창 없이 풀기가 처리기를 고를 때)도 2초 안에 끝난다.
    /// 1차 W1-B 의 판별은 XZStream 으로 앞 2MB 를 읽어 잘린 파일에서 같은 무한 루프에 빠졌다. 지금은 7z.dll 로 512바이트만 받는다.
    /// </summary>
    [Fact]
    public async Task 잘린_xz의_내용_판별이_2초_안에_끝난다()
    {
        using var tmp = new TempDir("xzsniff");
        string src = MakeData(tmp, "자료.bin", 400_000);
        string tar = tmp.P("묶음.tar");
        await new TarArchiveService().CompressAsync(new[] { src }, tar);
        var bytes = File.ReadAllBytes(SevenZipTestArchives.Xz(tar, tmp.P("묶음.tar.xz")));
        foreach (int cut in new[] { bytes.Length - 33, bytes.Length - 35, bytes.Length - 1, bytes.Length / 2, 700 })
        {
            string arc = tmp.P($"이름바뀜{cut}.xz");   // 확장자만 .xz — 안이 tar 인지 내용으로 본다
            File.WriteAllBytes(arc, bytes[..cut]);
            bool inner = await Task.Run(() => ArchiveSniffer.SingleStreamInnerIsTar(arc, ".xz")).WaitAsync(TimeSpan.FromSeconds(2));
            Assert.True(inner, $"{cut}: 잘렸어도 앞 512바이트로 안쪽 tar 를 알아봐야 함");
            var svc = await Task.Run(() => Svc.Locator.ResolveForAutomation(arc)).WaitAsync(TimeSpan.FromSeconds(2));
            Assert.IsType<CompressedTarArchiveService>(svc);
            string bin = tmp.P($"이름{cut}.bin");   // 확장자를 모르는 파일 — 서명으로
            File.Copy(arc, bin);
            Assert.Equal(ArchiveKind.TarXz, await Task.Run(() => ArchiveSniffer.Detect(bin)).WaitAsync(TimeSpan.FromSeconds(2)));
        }
    }

    /// <summary>
    /// 구조 계약(설계 §3): SharpCompress 의 XZStream 은 본체 코드 어디에서도 쓰지 않는다(잘린 xz 에서 끝없이 도는 해제기 — 취소도 안 먹음).
    /// </summary>
    [Fact]
    public void 본체는_SharpCompress_XZStream을_쓰지_않는다()
    {
        string root = Path.Combine(RepoFiles.Root, "Iljip");
        var users = Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar)
                        && !f.Contains(Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar))
            .Where(f => System.Text.RegularExpressions.Regex.IsMatch(
                string.Join("\n", File.ReadAllLines(f).Where(l => !l.TrimStart().StartsWith("//", StringComparison.Ordinal))),
                @"\bXZStream\b|Compressors\.Xz\b"))
            .Select(f => Path.GetRelativePath(root, f))
            .ToList();
        Assert.True(users.Count == 0, "XZStream 을 쓰는 코드: " + string.Join(", ", users));
    }
}
