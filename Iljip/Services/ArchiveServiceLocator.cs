namespace Iljip.Services;

/// <summary>
/// 확장자별 IArchiveService 라우터.
/// </summary>
public sealed class ArchiveServiceLocator
{
    private readonly Dictionary<string, IArchiveService> _byExtension =
        new(StringComparer.OrdinalIgnoreCase);

    private readonly List<IArchiveService> _all = new();

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
        _all.Add(service);
        foreach (var ext in service.SupportedExtensions)
            _byExtension[ext] = service;
    }

    public IReadOnlyList<string> AllSupportedExtensions =>
        _byExtension.Keys.ToList();

    /// <summary>파일 경로의 확장자를 보고 적절한 서비스를 반환. 미지원이면 null.</summary>
    public IArchiveService? Resolve(string archivePath)
    {
        string ext = System.IO.Path.GetExtension(archivePath);
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
