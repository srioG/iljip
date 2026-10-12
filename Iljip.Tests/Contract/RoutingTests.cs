using Iljip.Models;
using Iljip.Services;
using Iljip.Tests.TestSupport;

namespace Iljip.Tests.Contract;

/// <summary>
/// [범위 9] ArchiveServiceLocator: 등록 확장자(대소문자 무관) → 정해진 서비스, 미등록 → null.
/// 엔진을 바꿔 확장자가 늘면 '미등록' 쪽 InlineData 를 옮기고 KnownGapTests 의 Skip 을 지울 것.
/// </summary>
public class RoutingTests
{
    private static readonly ArchiveServiceLocator Locator = new();

    [Theory, Trait(Kinds.Key, Kinds.Record)]
    [InlineData("a.zip", typeof(ZipArchiveService))]
    [InlineData("A.ZIP", typeof(ZipArchiveService))]
    [InlineData("한글 이름.Zip", typeof(ZipArchiveService))]
    [InlineData(@"C:\폴더.zip\안\b.7z", typeof(SevenZipArchiveService))]
    [InlineData("b.7Z", typeof(SevenZipArchiveService))]
    [InlineData("c.tar", typeof(TarArchiveService))]
    [InlineData("d.gz", typeof(GZipArchiveService))]
    [InlineData("e.BZ2", typeof(BZip2ArchiveService))]
    [InlineData("f.rar", typeof(RarArchiveService))]
    [InlineData("g.egg", typeof(EggAlzArchiveService))]
    [InlineData("h.ALZ", typeof(EggAlzArchiveService))]
    // 현재 동작: 합성 확장자는 마지막 확장자로만 판단 → .tar.gz 는 GZ 처리기(I2-9), .tar.bz2 는 BZ2 처리기
    [InlineData("i.tar.gz", typeof(GZipArchiveService))]
    [InlineData("j.tar.bz2", typeof(BZip2ArchiveService))]
    // 분할 ZIP 의 마지막 조각(.zip)은 ZIP 처리기 — .z01 형제를 SharpCompress 가 알아서 찾는다(zip_split 통과)
    [InlineData("k.zip", typeof(ZipArchiveService))]
    public void 등록된_확장자는_정해진_서비스로_간다(string path, Type expected)
    {
        Assert.IsType(expected, Locator.Resolve(path));
    }

    [Theory, Trait(Kinds.Key, Kinds.Record)]
    [InlineData("a.tgz")]
    [InlineData("a.tbz2")]
    [InlineData("a.tbz")]
    [InlineData("a.txz")]
    [InlineData("a.tar.xz")]
    [InlineData("a.xz")]
    [InlineData("a.zipx")]
    [InlineData("a.iso")]
    [InlineData("a.lzh")]
    [InlineData("a.cab")]
    [InlineData("a.jar")]
    [InlineData("a.7z.001")]
    [InlineData("a.zip.001")]
    [InlineData("a.z01")]
    [InlineData("a.exe")]
    [InlineData("a.txt")]
    [InlineData("확장자없음")]
    [InlineData("")]
    public void 미등록_확장자는_null(string path)
    {
        Assert.Null(Locator.Resolve(path));
    }

    [Fact, Trait(Kinds.Key, Kinds.Record)]
    public void 등록_확장자_전체_목록이_고정되어_있다()
    {
        Assert.Equal(
            new[] { ".7z", ".alz", ".bz2", ".egg", ".gz", ".rar", ".tar", ".zip" },
            Locator.AllSupportedExtensions.OrderBy(x => x, StringComparer.Ordinal).ToArray());
    }

    [Fact]
    public void 새_압축의_기본은_ZIP()
    {
        Assert.IsType<ZipArchiveService>(Locator.GetDefaultForCompression());
    }

    /// <summary>저장 창 필터: 만들 수 있는 형식(ZIP·TAR·GZ·BZ2)만, 이 순서. 7z 생성이 생기면(I2-1) 여기에 추가돼야 함.</summary>
    [Fact, Trait(Kinds.Key, Kinds.Record)]
    public void 저장_필터는_만들_수_있는_형식만()
    {
        Assert.Equal(
            "ZIP 압축 파일 (*.zip)|*.zip|TAR 묶음 파일 (*.tar)|*.tar|GZIP 압축 파일 (*.gz)|*.gz|BZIP2 압축 파일 (*.bz2)|*.bz2",
            Locator.BuildSaveFilter());
    }

    /// <summary>Register 로 같은 확장자를 다시 등록하면 나중 것이 이긴다(엔진 교체 때 끼워 넣는 방식).</summary>
    [Fact]
    public void 나중에_등록한_서비스가_이긴다()
    {
        var locator = new ArchiveServiceLocator();
        var fake = new FakeService(".zip", ".tgz");
        locator.Register(fake);
        Assert.Same(fake, locator.Resolve("x.ZIP"));
        Assert.Same(fake, locator.Resolve("x.tgz"));
        Assert.IsType<SevenZipArchiveService>(locator.Resolve("x.7z"));
    }

    /// <summary>
    /// ResolveByExtension 은 파일을 읽지 않는다(끌어 놓기·명령줄 경로 분류·저장 형식 고르기용) — 내용이 압축이 아니어도, 없는 파일이어도 확장자대로.
    /// 속이 ZIP 인 .docx 는 압축 파일로 보지 않는다(끌어 놓으면 압축할 목록으로).
    /// </summary>
    [Fact]
    public void ResolveByExtension은_확장자만_본다()
    {
        using var tmp = new TempDir("rbe");
        string fake = tmp.P("가짜.zip");
        File.WriteAllText(fake, "압축 아님");
        Assert.IsType<ZipArchiveService>(Locator.ResolveByExtension(fake));
        Assert.IsType<TarArchiveService>(Locator.ResolveByExtension(tmp.P("없음.TAR")));
        Assert.Null(Locator.ResolveByExtension(tmp.P("보고서.docx")));
        Assert.Null(Locator.ResolveByExtension(""));
    }

    /// <summary>ArchiveEntry.Name: 폴더 항목('a/b/')도 마지막 이름('b')을 돌려준다 — 화면 목록 표시 계약.</summary>
    [Theory]
    [InlineData("a/b/", "b")]
    [InlineData("a/b", "b")]
    [InlineData("파일.txt", "파일.txt")]
    [InlineData("폴더/", "폴더")]
    public void ArchiveEntry_이름은_마지막_조각(string path, string name)
    {
        Assert.Equal(name, new ArchiveEntry { Path = path }.Name);
    }

    private sealed class FakeService(params string[] exts) : IArchiveService
    {
        public IReadOnlyList<string> SupportedExtensions { get; } = exts;
        public Task<IReadOnlyList<ArchiveEntry>> ListEntriesAsync(string a, string? p = null, CancellationToken c = default) => throw new NotImplementedException();
        public Task ExtractAsync(string a, string d, string? p = null, IProgress<ArchiveProgress>? pr = null, CancellationToken c = default) => throw new NotImplementedException();
        public Task ExtractEntriesAsync(string a, string d, IReadOnlyCollection<string> e, string? p = null, IProgress<ArchiveProgress>? pr = null, CancellationToken c = default) => throw new NotImplementedException();
        public Task<CompressResult> CompressAsync(IEnumerable<string> s, string a, CompressionOptions? o = null, IProgress<ArchiveProgress>? pr = null, CancellationToken c = default) => throw new NotImplementedException();
    }
}
