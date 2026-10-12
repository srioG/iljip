namespace Iljip.Services;

/// <summary>
/// 확장자별 IArchiveService 라우터.
/// </summary>
public sealed class ArchiveServiceLocator
{
    private readonly Dictionary<string, IArchiveService> _byExtension =
        new(StringComparer.OrdinalIgnoreCase);

    public ArchiveServiceLocator()
    {
        Register(new ZipArchiveService());
        Register(new SevenZipArchiveService());
        Register(new TarArchiveService());
        Register(new GZipArchiveService());
        Register(new BZip2ArchiveService());
        Register(new RarArchiveService());
        Register(new EggAlzArchiveService());   // EGG/ALZ 해제(EggDotNet) — 반디집 의존 제거용
    }

    public void Register(IArchiveService service)
    {
        foreach (var ext in service.SupportedExtensions)
            _byExtension[ext] = service;
    }

    public IReadOnlyList<string> AllSupportedExtensions =>
        _byExtension.Keys.ToList();

    /// <summary>
    /// 열기·풀기 직전에 쓰는 라우팅. 미지원이면 null. 지금은 <see cref="ResolveByExtension"/> 와 같다
    /// (내용(서명)을 먼저 보는 판별은 W1-B(7z.dll·ArchiveSniffer)와 같이 들어온다 — 그때 이 메서드만 바뀐다).
    /// </summary>
    public IArchiveService? Resolve(string archivePath) => ResolveByExtension(archivePath);

    /// <summary>
    /// 파일을 읽지 않고 확장자만 보는 라우팅. 미지원이면 null.
    /// 끌어 놓기·명령줄 경로 분류(압축인지 압축할 파일인지), 저장할 형식 고르기, 열기 창 필터처럼 '내용을 보면 안 되는' 곳에서 쓴다
    /// (속이 ZIP 인 .docx·.xlsx 를 압축하려고 끌어 놓았는데 '열기'가 돼 버리지 않게).
    /// </summary>
    public IArchiveService? ResolveByExtension(string path)
    {
        string ext = System.IO.Path.GetExtension(path ?? string.Empty);
        return _byExtension.TryGetValue(ext, out var svc) ? svc : null;
    }

    /// <summary>새 압축 파일을 만들 때 기본 서비스 (ZIP).</summary>
    public IArchiveService GetDefaultForCompression() => _byExtension[".zip"];

    /// <summary>SaveFileDialog용 필터 문자열 (압축 가능 포맷만).</summary>
    public string BuildSaveFilter()
    {
        // 7Z, RAR은 해제만 가능하므로 저장 필터에서 제외
        return string.Join("|",
            "ZIP 압축 파일 (*.zip)|*.zip",
            "TAR 묶음 파일 (*.tar)|*.tar",
            "GZIP 압축 파일 (*.gz)|*.gz",
            "BZIP2 압축 파일 (*.bz2)|*.bz2");
    }
}
