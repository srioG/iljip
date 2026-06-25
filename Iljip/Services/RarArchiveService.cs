using Iljip.Models;

namespace Iljip.Services;

/// <summary>
/// RAR 포맷. SharpCompress는 RAR 4/5 해제만 지원 (RAR 압축은 라이선스상 미지원).
/// 해제와 항목 목록 읽기는 베이스의 ArchiveFactory.Open으로 자동 처리됨.
/// </summary>
public sealed class RarArchiveService : SharpCompressArchiveServiceBase
{
    public override IReadOnlyList<string> SupportedExtensions { get; } = new[] { ".rar" };

    public override Task CompressAsync(
        IEnumerable<string> sourcePaths,
        string archivePath,
        CompressionOptions? options = null,
        IProgress<ArchiveProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException(
            "RAR 압축은 라이선스 제한으로 지원하지 않아요. (해제만 가능)\n" +
            "압축은 ZIP을 사용해주세요.");
    }
}
