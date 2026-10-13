using System.IO;
using SharpSevenZip;

namespace Iljip.Services;

/// <summary>
/// 방금 만든 압축 파일 시험(W2-07 — 한 화면 압축 창의 '다 만들고 시험하기'와 '원본을 휴지통으로' 전 확인, I3-4). W2-08 이 '--test'·압축 파일 시험으로 키운다.
/// 시험은 가짜를 넣어 '시험 통과/실패에 따라 원본을 지우는지'를 본다(Iljip.Tests/TestSupport/FakeCompressTester).
/// </summary>
public interface ICreatedArchiveTester
{
    /// <summary>
    /// <paramref name="archivePath"/>(나눠 만들었으면 여는 조각 — ZIP 은 이름.zip, 7Z 는 이름.7z.001)의 모든 항목을 끝까지 풀어 보며 확인값(CRC)까지 본다
    /// (디스크에 쓰지 않음 — BZ2 만 숨김 임시 폴더에 풀어 보고 지움). 틀리면 예외(일집 예외 — 손상·암호 등), 취소면 <see cref="OperationCanceledException"/>.
    /// </summary>
    void Test(string archivePath, string? password, CancellationToken cancellationToken);
}

/// <summary>
/// 기본 시험기. 7Z: 7z.dll 로 열어 시험(Check — 나눈 7z 는 조각을 이어 붙인 스트림으로). ZIP(나눈 것 포함)·TAR·GZ: 풀기와 같은 길로 끝까지 풀어 보기
/// (<see cref="SharpCompressArchiveServiceBase.TestEntriesSync"/> — ZIP 은 CRC·AES 인증값, GZ 는 CRC·크기). BZ2(그 밖): 숨김 임시 폴더에 풀어 보고 지운다.
/// </summary>
public sealed class CreatedArchiveTester : ICreatedArchiveTester
{
    public static readonly CreatedArchiveTester Instance = new();

    private readonly ArchiveServiceLocator _locator = new();

    public void Test(string archivePath, string? password, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        string? pw = string.IsNullOrEmpty(password) ? null : password;
        var service = ServiceFor(archivePath) ?? throw new UnsupportedFormatException(Path.GetExtension(archivePath));
        switch (service)
        {
            case SevenZipArchiveService:
                TestSevenZip(archivePath, pw, cancellationToken);
                return;
            case BZip2ArchiveService:
                // 일집이 만든 .bz2(순수 bzip2 스트림)는 형식 판별(ArchiveFactory)로 열리지 않는다 — 풀기 길로 시험
                TestByExtracting(service, archivePath, pw, cancellationToken);
                return;
            case SharpCompressArchiveServiceBase sc:
                sc.TestEntriesSync(archivePath, pw, _ => true, cancellationToken);
                return;
            default:
                TestByExtracting(service, archivePath, pw, cancellationToken);
                return;
        }
    }

    /// <summary>
    /// 만든 압축의 처리기: 마지막 확장자로(.zip·.7z·.tar·.gz·.bz2 — 일집이 만드는 압축은 늘 형식 확장자로 끝남). 'backup.tar.gz'(TAR 파일을 GZ 로 압축)는
    /// 만든 그대로 GZ 로 시험한다(W2-07 검증 지적 2차) — 가장 긴 확장자('.tar.gz')로 고르면 압축된 TAR 처리기가 안쪽 TAR 를 숨김 임시 폴더에 다 풀고
    /// 그 안의 파일까지 또 풀어(내용의 두 배를 디스크에 씀) '디스크에 쓰지 않음' 약속을 어기고, 그 TAR 를 일집이 못 읽으면 멀쩡한 GZ 를 '문제 있음'으로 알린다.
    /// 마지막 확장자로 못 고르면(나눈 7z 첫 조각 '.7z.001' 등) 가장 긴 확장자로.
    /// </summary>
    internal IArchiveService? ServiceFor(string archivePath) =>
        _locator.ForExtension(Path.GetExtension(archivePath)) ?? _locator.ResolveByExtension(archivePath);

    /// <summary>7z: 7z.dll 로 열어 모든 항목을 시험(Check). 읽기마다 취소를 본다(큰 솔리드 7z 도 곧 멈춤 — W2-06 의 고친 7z 검사와 같은 방법).</summary>
    private static void TestSevenZip(string path, string? password, CancellationToken ct)
    {
        SevenZipLibrary.ThrowIfUnavailable();
        Stream raw = SplitVolumeStream.IsNumberedFirstVolume(path)
            ? SplitVolumeStream.Open(path)
            : new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 1 << 16);
        using var input = new CancellableSeekableReadStream(raw, ct);
        if (input.Length == 0) throw new CorruptArchiveException();
        using var x = new SharpSevenZipExtractor(input, password ?? string.Empty, leaveOpen: true, InArchiveFormat.SevenZip);
        bool anyFile = x.ArchiveFileData.Any(f => !f.IsDirectory);
        ct.ThrowIfCancellationRequested();
        if (anyFile && !x.Check())
        {
            ct.ThrowIfCancellationRequested();   // 읽기에서 취소가 나면 Check 는 false 만 돌려준다
            throw new CorruptArchiveException();
        }
    }

    /// <summary>
    /// 압축 파일이 있는 폴더 안 숨김 임시 폴더(<see cref="StagingFolder"/> — 같은 볼륨)에 풀어 보고 폴더째 지운다(풀기 길의 확인값 검사를 그대로 씀).
    /// </summary>
    private static void TestByExtracting(IArchiveService service, string path, string? password, CancellationToken ct)
    {
        string folder = Path.GetDirectoryName(Path.GetFullPath(path)) ?? throw new IOException("압축 파일이 있는 폴더를 알 수 없어요: " + path);
        using var staging = StagingFolder.Create(folder);
        service.ExtractAsync(path, staging.FullPath, password, progress: null, cancellationToken: ct).GetAwaiter().GetResult();
    }
}
