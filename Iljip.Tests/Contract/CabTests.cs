using System.Text;
using Iljip.Services;
using Iljip.Tests.TestSupport;

namespace Iljip.Tests.Contract;

/// <summary>
/// [W2-01] CAB(.cab — 7z.dll): 한글 이름 목록·풀기, 끝이 잘린 CAB 은 머리의 전체 크기(cbCabinet)로 알아보고 '끝부분이 잘려' + 아무것도 남기지 않음.
/// 반디집·파이썬 픽스처에 CAB 이 없어 규격대로 만든 압축 없는 CAB(<see cref="RawCab"/>)으로 시험한다(실물 CAB — 윈도 업데이트·드라이버 — 은 MANUAL_CHECKLIST).
/// </summary>
public class CabTests
{
    private static List<(string Name, byte[] Data)> Files()
    {
        var bin = new byte[70_000];   // 32KB 블록 셋에 걸침
        new Random(11).NextBytes(bin);
        return new()
        {
            ("한글폴더/견적서 (최종).txt", Encoding.UTF8.GetBytes(string.Concat(Enumerable.Repeat("견적 금액 1,234,000원\r\n", 200)))),
            ("한글폴더/하위/data.bin", bin),
            ("readme.txt", Encoding.ASCII.GetBytes("plain readme")),
        };
    }

    [Fact]
    public async Task CAB이_한글_이름으로_열리고_풀린다()
    {
        using var tmp = new TempDir("cab");
        var files = Files();
        string cab = tmp.P("드라이버.cab");
        File.WriteAllBytes(cab, RawCab.Build(files));
        var svc = Svc.For(cab);
        Assert.IsType<SevenZipFormatsArchiveService>(svc);
        var listed = (await svc.ListEntriesAsync(cab)).Where(e => !e.IsDirectory).Select(e => e.Path).OrderBy(p => p, StringComparer.Ordinal);
        Assert.Equal(files.Select(f => f.Name).OrderBy(p => p, StringComparer.Ordinal), listed);

        string dest = tmp.P("out");
        await svc.ExtractAsync(cab, dest);
        Assert.Equal(files.Select(f => f.Name).OrderBy(p => p, StringComparer.Ordinal), TreeAssert.Files(dest));
        foreach (var (name, data) in files)
            Assert.Equal(TreeAssert.Sha256(data), TreeAssert.Sha256(Path.Combine(dest, name.Replace('/', Path.DirectorySeparatorChar))));
    }

    /// <summary>
    /// 계약(W2-01): 끝이 잘린 CAB 은 어디서 잘려도 '끝부분이 잘려' 로 알리고(목록도) 아무것도 남기지 않는다 — 머리의 전체 크기가 파일보다 크면 잘림.
    /// </summary>
    [Fact]
    public async Task 잘린_CAB은_잘렸다고_알리고_아무것도_남기지_않는다()
    {
        using var tmp = new TempDir("cabcut");
        var bytes = RawCab.Build(Files());
        foreach (int cut in Enumerable.Range(1, bytes.Length - 1).Where(c => c < 300 || c % 997 == 0 || c > bytes.Length - 20))
        {
            string arc = tmp.P($"t{cut}.cab");
            File.WriteAllBytes(arc, bytes[..cut]);
            string parent = tmp.P($"새 폴더 {cut}");
            var ex = await Svc.Catch(() => Svc.For(arc).ExtractAsync(arc, Path.Combine(parent, "out")));
            var corrupt = Assert.IsAssignableFrom<CorruptArchiveException>(ex);
            Assert.True(corrupt.IsTruncated, $"CAB {cut}/{bytes.Length}: 잘림으로 알리지 않음 — {corrupt.Message}");
            Assert.False(Internals.IsPasswordRelated(ex!));
            Assert.False(Directory.Exists(parent), $"CAB {cut}/{bytes.Length}: 대상에 무언가 남음");
            if (cut % 997 == 0)
                Assert.True(Assert.IsAssignableFrom<CorruptArchiveException>(await Svc.Catch(() => Svc.For(arc).ListEntriesAsync(arc))).IsTruncated);
        }
    }
}
