using Iljip.Models;

namespace Iljip.Services;

/// <summary>
/// 7Z 포맷.
/// SharpCompress는 7Z **해제만** 지원하고 압축 생성은 지원하지 않는다.
/// 해제는 베이스의 ArchiveFactory.Open으로 자동 처리.
/// </summary>
public sealed class SevenZipArchiveService : SharpCompressArchiveServiceBase
{
    public override IReadOnlyList<string> SupportedExtensions { get; } = new[] { ".7z" };

    public override Task CompressAsync(
        IEnumerable<string> sourcePaths,
        string archivePath,
        CompressionOptions? options = null,
        IProgress<ArchiveProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException(
            "7Z 압축 생성은 현재 지원하지 않아요. (해제만 가능)\n" +
            "압축은 ZIP이나 TAR을 사용해주세요.");
    }
}
