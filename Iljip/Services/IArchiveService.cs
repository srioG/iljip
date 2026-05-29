using Iljip.Models;

namespace Iljip.Services;

/// <summary>
/// 압축/해제 기능 추상화. 포맷별 구현체(ZIP/7Z/...)가 이를 따른다.
/// MVP 단계는 ZIP 만 구현, 차후 포맷 확장 시 이 인터페이스를 통해 라우팅.
/// </summary>
public interface IArchiveService
{
    /// <summary>이 서비스가 지원하는 파일 확장자들 (.zip, .7z 등 점 포함)</summary>
    IReadOnlyList<string> SupportedExtensions { get; }

    /// <summary>
    /// 압축 파일을 열어 내부 항목 목록을 읽음.
    /// </summary>
    Task<IReadOnlyList<ArchiveEntry>> ListEntriesAsync(
        string archivePath,
        string? password = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 압축 파일을 지정 폴더에 해제.
    /// </summary>
    Task ExtractAsync(
        string archivePath,
        string destinationFolder,
        string? password = null,
        IProgress<ArchiveProgress>? progress = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 압축 파일에서 선택한 항목들만 해제.
    /// </summary>
    /// <param name="entryPaths">추출할 항목 경로. 폴더 경로면 그 하위 전체가 포함됨.</param>
    Task ExtractEntriesAsync(
        string archivePath,
        string destinationFolder,
        IReadOnlyCollection<string> entryPaths,
        string? password = null,
        IProgress<ArchiveProgress>? progress = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 파일/폴더들을 압축 파일로 생성.
    /// </summary>
    /// <param name="sourcePaths">파일 또는 폴더 경로 모음</param>
    /// <param name="archivePath">생성될 압축 파일 경로</param>
    /// <param name="options">압축 옵션 (수준, 비밀번호 등). null이면 기본값.</param>
    Task CompressAsync(
        IEnumerable<string> sourcePaths,
        string archivePath,
        CompressionOptions? options = null,
        IProgress<ArchiveProgress>? progress = null,
        CancellationToken cancellationToken = default);
}
