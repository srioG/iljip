using System.IO;

namespace Iljip.Services;

/// <summary>
/// 압축 형식 → IArchiveService 라우터(W2-01 에서 형식 라우팅 표대로 다시 짬 — HANDOFF 'W2-01').
/// 찾는 순서:
/// <list type="number">
/// <item><see cref="Resolve"/>: 확장자로 — 등록된 확장자 중 가장 긴 것부터('.tar.gz' &gt; '.gz', '.7z.001' &gt; '.001').
///   단 단일 스트림 압축(.gz·.bz2·.xz)이고 파일이 있으면 안쪽이 tar 인지 보고 tar 면 압축된 tar 처리기로(라우팅 표의 '내용 판별' 행).</item>
/// <item><see cref="ResolveByContent"/>: 일집 안(열기 창·파일 연결 --open·창에 놓은 뒤의 열기)에서 1 이 null 일 때만 앞부분 서명을 본다
///   (이름이 '.bin' 으로 바뀐 7z, 자동 풀림 .exe 등 — B-086).</item>
/// <item><see cref="ResolveForAutomation"/>: 창 없이 풀기(--extract-to). 1 → 2. 단 속이 ZIP 인 문서(.docx·.xlsx·.hwpx…)는 늘 null.</item>
/// </list>
/// <see cref="ResolveByExtension"/> 은 파일을 읽지 않는다 — 끌어 놓기 분류(DropPolicy)·저장 대상·셸 확장과 같은 '확장자만' 규칙.
/// .z01·.z02 는 처리기가 없다(분할 ZIP 은 마지막 .zip 으로 연다 — ZIP 처리기가 형제 조각을 이어 붙임, W2-03 SpannedZipStream).
/// </summary>
public sealed class ArchiveServiceLocator
{
    private readonly Dictionary<string, IArchiveService> _byExtension =
        new(StringComparer.OrdinalIgnoreCase);

    // 내용으로 알아본 종류 → 그 종류의 대표 처리기를 고를 확장자(ResolveByContent 가 확장자로 못 고른 파일에만 씀).
    private static readonly Dictionary<ArchiveKind, string> CanonicalExtension = new()
    {
        [ArchiveKind.Zip] = ".zip",
        [ArchiveKind.SevenZip] = ".7z",
        [ArchiveKind.Rar] = ".rar",
        [ArchiveKind.Tar] = ".tar",
        [ArchiveKind.TarGz] = ".tar.gz",
        [ArchiveKind.TarBz2] = ".tar.bz2",
        [ArchiveKind.TarXz] = ".tar.xz",
        [ArchiveKind.GZip] = ".gz",
        [ArchiveKind.BZip2] = ".bz2",
        [ArchiveKind.Xz] = ".xz",
        [ArchiveKind.Iso] = ".iso",
        [ArchiveKind.Udf] = ".iso",
        [ArchiveKind.Cab] = ".cab",
        [ArchiveKind.Lzh] = ".lzh",
        [ArchiveKind.Wim] = ".wim",
        [ArchiveKind.Egg] = ".egg",
        [ArchiveKind.Alz] = ".alz",
    };

    private readonly SfxArchiveService _sfx = new();

    public ArchiveServiceLocator()
    {
        Register(new ZipArchiveService());
        Register(new SevenZipArchiveService());
        Register(new TarArchiveService());
        Register(new GZipArchiveService());
        Register(new BZip2ArchiveService());
        Register(new RarArchiveService());
        Register(new EggAlzArchiveService());   // EGG/ALZ 해제(EggDotNet) — 반디집 의존 제거용
        Register(new CompressedTarArchiveService());    // .tgz·.tar.gz·.tbz2·.tbz·.tar.bz2·.txz·.tar.xz → tar 안 내용을 바로(W2-01)
        Register(new SevenZipFormatsArchiveService());  // 7z.dll 전용 형식(zipx·iso·cab·lzh·lha·wim·xz — W2-01)
        // 자동 풀림 exe(SfxArchiveService)는 확장자로 등록하지 않는다 — 내용(MZ + 안쪽 zip/7z/rar/egg/alz)으로만 고른다.
    }

    public void Register(IArchiveService service)
    {
        foreach (var ext in service.SupportedExtensions)
            _byExtension[ext] = service;
    }

    public IReadOnlyList<string> AllSupportedExtensions =>
        _byExtension.Keys.ToList();

    /// <summary>
    /// 열기·풀기 직전에 쓰는 라우팅(확장자 기준). 미지원이면 null.
    /// 확장자가 단일 스트림 압축(.gz·.bz2·.xz)이고 파일이 있으면 안쪽 512바이트가 tar 머리인지 보고(<see cref="ArchiveSniffer.SingleStreamInnerIsTar"/>)
    /// tar 면 압축된 tar 처리기(안의 내용을 바로 펼침), 아니면 단일 파일 처리기.
    /// </summary>
    public IArchiveService? Resolve(string archivePath)
    {
        string? ext = MatchExtension(archivePath);
        if (ext is null || !_byExtension.TryGetValue(ext, out var svc)) return null;
        if (IsSingleStreamExtension(ext) && File.Exists(archivePath)
            && ArchiveSniffer.SingleStreamInnerIsTar(archivePath, ext)
            && _byExtension.TryGetValue(".tar" + ext, out var compressedTar))
            return compressedTar;
        return svc;
    }

    /// <summary>
    /// 일집 안의 열기(열기 창·파일 연결 --open·창에 놓은 뒤)용: <see cref="Resolve"/> 가 null 일 때만 앞부분 서명으로 고른다(B-086).
    /// 자동 풀림 exe 는 여기서만 고른다. zstd 처럼 알아보지만 못 푸는 것과 서명을 모르는 파일은 null.
    /// </summary>
    public IArchiveService? ResolveByContent(string archivePath)
    {
        var byExt = Resolve(archivePath);
        if (byExt is not null) return byExt;
        if (string.IsNullOrEmpty(archivePath) || !File.Exists(archivePath)) return null;

        var kind = ArchiveSniffer.Detect(archivePath);
        switch (kind)
        {
            case ArchiveKind.ZipSfx:
            case ArchiveKind.SevenZipSfx:
            case ArchiveKind.RarSfx:
            case ArchiveKind.EggSfx:
            case ArchiveKind.AlzSfx:
                return _sfx;
        }
        return CanonicalExtension.TryGetValue(kind, out var canonical) && _byExtension.TryGetValue(canonical, out var svc) ? svc : null;
    }

    /// <summary>
    /// 속이 ZIP 인 문서 형식(오피스·한글·ODF·전자책 등). 내용 판별로는 ZIP 이지만 사용자에게는 문서다.
    /// </summary>
    private static readonly HashSet<string> ZipBasedDocumentExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".docx", ".docm", ".dotx", ".dotm", ".xlsx", ".xlsm", ".xlsb", ".xltx", ".xltm", ".xlam",
        ".pptx", ".pptm", ".potx", ".potm", ".ppsx", ".ppsm", ".ppam", ".vsdx", ".vsdm",
        ".hwpx", ".odt", ".ods", ".odp", ".odg", ".odf", ".ott", ".ots", ".otp", ".epub", ".xps", ".oxps",
        ".jar", ".apk",
    };

    /// <summary>속이 ZIP 인 문서 확장자인지(.xlsx·.docx·.hwpx·.jar·.apk 등).</summary>
    public static bool IsZipBasedDocument(string path) => ZipBasedDocumentExtensions.Contains(Path.GetExtension(path ?? string.Empty));

    /// <summary>
    /// 창 없이 풀기(--extract-to, 재사용장치 같은 자동화)용: <see cref="ResolveByContent"/> 와 같지만 속이 ZIP 인 문서(.xlsx 등)는 늘 null(미지원).
    /// 자동화 계약('압축이 아니면 영어 문구 + 종료 코드 2')을 지킨다 — 문서 속 XML 을 풀어 '성공'으로 끝나지 않게.
    /// </summary>
    public IArchiveService? ResolveForAutomation(string archivePath) =>
        IsZipBasedDocument(archivePath) ? null : ResolveByContent(archivePath);

    /// <summary>확장자만 보고 고른다(파일을 읽지 않음 — 끌어 놓기 분류·저장 대상·셸 확장과 같은 규칙). 미지원이면 null.</summary>
    public IArchiveService? ResolveByExtension(string path)
    {
        string? ext = MatchExtension(path);
        return ext is not null && _byExtension.TryGetValue(ext, out var svc) ? svc : null;
    }

    /// <summary>
    /// 확장자 하나(점 포함, 예: '.gz')에 등록된 처리기 — 파일 이름의 가장 긴 확장자를 보지 않는다. 없으면 null.
    /// 만들 형식으로 처리기를 고를 때 쓴다(W2-07 검증 지적 2차): 'backup.tar' 를 GZ 로 만든 'backup.tar.gz' 를 이름으로 고르면
    /// '.tar.gz'(압축된 TAR — 풀기만 됨) 처리기가 골라진다.
    /// </summary>
    /// <remarks>
    /// '.exe' 는 자동 풀림 처리기(W2-10 — 압축 창의 '자동 풀림 EXE' 형식으로 만들기, 만든 exe 시험·목록). 확장자 표(<see cref="Resolve"/>)에는 넣지 않는다 —
    /// 열기·풀기·끌어 놓기·우클릭에서 .exe 를 확장자로 압축으로 치지 않는 규칙은 그대로(내용으로만 — <see cref="ResolveByContent"/>).
    /// </remarks>
    public IArchiveService? ForExtension(string? extension) =>
        string.Equals(extension, ".exe", StringComparison.OrdinalIgnoreCase) ? _sfx
        : !string.IsNullOrEmpty(extension) && _byExtension.TryGetValue(extension, out var svc) ? svc : null;

    /// <summary>
    /// 등록된 확장자 중 파일 이름 끝과 맞는 가장 긴 것('a.tar.gz' → '.tar.gz', 'a.7z.001' → '.7z.001', '보고서.v2.tar.gz' → '.tar.gz'). 없으면 null.
    /// 폴더 이름의 점은 보지 않는다('C:\폴더.zip\안\b.7z' → '.7z'). 숫자 조각은 첫 조각 '.001' 만 등록돼 있다(.002 등은 null).
    /// </summary>
    private string? MatchExtension(string? archivePath)
    {
        if (string.IsNullOrEmpty(archivePath)) return null;
        string name = Path.GetFileName(archivePath);
        for (int i = name.IndexOf('.'); i >= 0; i = name.IndexOf('.', i + 1))
        {
            string cand = name[i..];
            if (_byExtension.ContainsKey(cand)) return cand;
        }
        return null;
    }

    private static bool IsSingleStreamExtension(string ext) =>
        ext.Equals(".gz", StringComparison.OrdinalIgnoreCase)
        || ext.Equals(".bz2", StringComparison.OrdinalIgnoreCase)
        || ext.Equals(".xz", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// 압축 파일 이름에서 압축 확장자를 뗀 이름. 등록된 가장 긴 확장자를 뗀다
    /// ('견적.7z.001' → '견적', '자료.tar.gz' → '자료'). 모르는 확장자(자동 풀림 .exe 등)는 마지막 확장자만 뗀다. 파일을 읽지 않는다.
    /// (우클릭 '"이름"\ 에 풀기' 의 폴더 이름은 <see cref="ArchiveNaming.FolderNameFor"/> — C++ 셸 확장과 같은 규칙)
    /// </summary>
    public string NameWithoutArchiveExtension(string archivePath)
    {
        string name = Path.GetFileName(archivePath);
        string? ext = MatchExtension(archivePath);
        string stem = ext is not null && name.Length > ext.Length ? name[..^ext.Length] : string.Empty;
        return string.IsNullOrWhiteSpace(stem) ? Path.GetFileNameWithoutExtension(name) : stem;
    }

    /// <summary>새 압축 파일을 만들 때 기본 서비스 (ZIP).</summary>
    public IArchiveService GetDefaultForCompression() => _byExtension[".zip"];

    /// <summary>
    /// SaveFileDialog용 필터 문자열 (압축 가능 포맷만). RAR·EGG/ALZ·zipx·iso 등은 해제만 가능하므로 제외.
    /// 7Z(W2-02)는 7z.dll 로만 만든다 — 7z.dll 을 쓸 수 있을 때만 넣는다(보안 프로그램이 막은 PC 에서 옵션 창까지 지난 뒤에야 오류가 나지 않게).
    /// </summary>
    public string BuildSaveFilter()
    {
        var filters = new List<string> { "ZIP 압축 파일 (*.zip)|*.zip" };
        if (SevenZipLibrary.IsAvailable) filters.Add("7Z 압축 파일 (*.7z)|*.7z");
        filters.Add("TAR 묶음 파일 (*.tar)|*.tar");
        filters.Add("GZIP 압축 파일 (*.gz)|*.gz");
        filters.Add("BZIP2 압축 파일 (*.bz2)|*.bz2");
        return string.Join("|", filters);
    }

    // ───── W2-06(I2-3 압축 고치기)에서 더한 것 ─────

    /// <summary>
    /// 이 압축 파일을 고치는 편집기(확장자만 본다 — 파일을 읽지 않음). 고칠 수 없는 형식이면 null — 화면은 <see cref="ArchiveNotEditableException"/> 문구.
    /// <list type="bullet">
    /// <item>.zip(나뉘지 않은 것 — .zip.001·.z01 나눔 제외) → <see cref="Editing.ZipArchiveEditor"/>(앞에 실행 파일이 붙은 ZIP 은 고칠 때 알아내 거절).</item>
    /// <item>.7z(.7z.001 나눔 제외, 7z.dll 을 쓸 수 있을 때) → <see cref="Editing.SevenZipArchiveEditor"/>.</item>
    /// <item>그 밖(RAR·EGG·ALZ·TAR·GZ·ISO·자동 풀림 exe·속이 ZIP 인 문서 .docx 등) → null.</item>
    /// </list>
    /// </summary>
    public static Editing.IArchiveEditor? EditorFor(string? archivePath)
    {
        if (string.IsNullOrEmpty(archivePath)) return null;
        if (Editing.ZipArchiveEditor.IsEditablePath(archivePath)) return new Editing.ZipArchiveEditor();
        if (Editing.SevenZipArchiveEditor.IsEditablePath(archivePath)) return new Editing.SevenZipArchiveEditor();
        return null;
    }

    /// <summary>이 압축 파일을 고칠 수 있는가(<see cref="EditorFor"/> 가 null 이 아님).</summary>
    public static bool CanEdit(string? archivePath) => EditorFor(archivePath) is not null;

    /// <summary>
    /// 고칠 수 없을 때의 안내: 7z(나뉘지 않은 .7z)인데 7z.dll 을 못 불러온 PC 면 그 이유(<see cref="ArchiveNotEditableException.SevenZipEngineMessage"/>),
    /// 그 밖은 '이 형식은 고칠 수 없어요. ZIP이나 7Z로 다시 압축하면…'.
    /// </summary>
    public static string NotEditableMessage(string? archivePath) =>
        archivePath is not null && archivePath.EndsWith(".7z", StringComparison.OrdinalIgnoreCase) && !SevenZipLibrary.IsAvailable
            ? ArchiveNotEditableException.SevenZipEngineMessage
            : ArchiveNotEditableException.DefaultMessage;
}
