using System.IO;

namespace Iljip.Services;

/// <summary>
/// 방금 만든 압축 파일 시험(W2-07 — 한 화면 압축 창의 '다 만들고 시험하기'와 '원본을 휴지통으로' 전 확인, I3-4). W2-08 이 '--test'·압축 파일 시험으로 키운다.
/// 시험은 가짜를 넣어 '시험 통과/실패에 따라 원본을 지우는지'를 본다(Iljip.Tests/TestSupport/FakeCompressTester).
/// </summary>
public interface ICreatedArchiveTester
{
    /// <summary>
    /// <paramref name="archivePath"/>(나눠 만들었으면 여는 조각 — ZIP 은 이름.zip, 7Z 는 이름.7z.001)의 모든 항목을 끝까지 풀어 보며 확인값(CRC)까지 본다
    /// (디스크에 쓰지 않음). 틀리면 예외(일집 예외 — 손상·암호 등), 취소면 <see cref="OperationCanceledException"/>.
    /// </summary>
    void Test(string archivePath, string? password, CancellationToken cancellationToken);
}

/// <summary>
/// 기본 시험기 — W2-08 부터 압축 검사(<see cref="ArchiveTestService"/>, 화면의 '시험하기'와 같은 길)를 쓴다. 문제를 찾으면 <see cref="ArchiveTestFailedException"/>
/// (손상의 한 종류 — 원본을 지우지 않음, 문구는 찾은 문제 요약). 예전(W2-07)엔 BZ2 를 숨김 임시 폴더에 풀어 보고 지웠다 — 이제 디스크에 쓰지 않는다.
/// </summary>
public sealed class CreatedArchiveTester : ICreatedArchiveTester
{
    public static readonly CreatedArchiveTester Instance = new();

    private readonly ArchiveServiceLocator _locator = new();
    private readonly ArchiveTestService _tester;

    public CreatedArchiveTester() => _tester = new ArchiveTestService(_locator);

    public void Test(string archivePath, string? password, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var service = ServiceFor(archivePath) ?? throw new UnsupportedFormatException(Path.GetExtension(archivePath));
        // 자동 풀림 exe(W2-10)는 안쪽 ZIP 을 ZIP 처리기로(CP949 한글 ZipCrypto 암호·AES 인증값·CRC — SfxArchiveService.TestArchive), 순수 bzip2 도 검사기가 맡는다
        var report = _tester.Test(archivePath, password, progress: null, cancellationToken, service);
        if (!report.Ok) throw new ArchiveTestFailedException(report);
    }

    /// <summary>
    /// 만든 압축의 처리기: 마지막 확장자로(.zip·.7z·.tar·.gz·.bz2 — 일집이 만드는 압축은 늘 형식 확장자로 끝남). 'backup.tar.gz'(TAR 파일을 GZ 로 압축)는
    /// 만든 그대로 GZ 로 시험한다(W2-07 검증 지적 2차) — 가장 긴 확장자('.tar.gz')로 고르면 압축된 TAR 처리기가 안쪽 TAR 까지 풀어 보고,
    /// 그 TAR 를 일집이 못 읽으면 멀쩡한 GZ 를 '문제 있음'으로 알린다.
    /// 마지막 확장자로 못 고르면(나눈 7z 첫 조각 '.7z.001' 등) 가장 긴 확장자로.
    /// </summary>
    internal IArchiveService? ServiceFor(string archivePath) =>
        _locator.ForExtension(Path.GetExtension(archivePath)) ?? _locator.ResolveByExtension(archivePath);
}
