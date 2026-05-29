namespace Iljip.Models;

/// <summary>
/// 압축 파일 내부의 단일 항목을 나타냄.
/// 압축 해제 시 표시용, 압축 시에는 소스 경로 표현용으로 양쪽에서 재사용.
/// </summary>
public sealed class ArchiveEntry
{
    /// <summary>압축파일 내 상대 경로(슬래시 구분). 예: "folder/file.txt"</summary>
    public required string Path { get; init; }

    /// <summary>파일/폴더의 표시명 (마지막 세그먼트)</summary>
    public string Name => System.IO.Path.GetFileName(Path) is var n && string.IsNullOrEmpty(n)
        ? Path
        : n;

    /// <summary>원본 크기 (bytes)</summary>
    public long Size { get; init; }

    /// <summary>압축 후 크기 (bytes). 압축 전이면 -1</summary>
    public long CompressedSize { get; init; } = -1;

    /// <summary>폴더 여부</summary>
    public bool IsDirectory { get; init; }

    /// <summary>마지막 수정 시간</summary>
    public DateTime? LastModified { get; init; }

    /// <summary>압축률 (%) - 표시용</summary>
    public double CompressionRatio =>
        Size <= 0 || CompressedSize < 0
            ? 0
            : (1.0 - (double)CompressedSize / Size) * 100.0;

    /// <summary>로컬 파일 시스템 경로 (압축 시 소스 추적용, 표시 안 함)</summary>
    public string? SourcePath { get; init; }
}
