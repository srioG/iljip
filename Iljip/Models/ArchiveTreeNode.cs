using System.Collections.ObjectModel;

namespace Iljip.Models;

/// <summary>
/// 압축 파일 내부의 폴더 계층을 나타내는 트리 노드 (좌측 폴더 트리용).
/// </summary>
public sealed class ArchiveTreeNode
{
    public required string Name { get; init; }

    /// <summary>압축 내 폴더 경로(슬래시 구분). 루트는 빈 문자열. 예: "docs/img"</summary>
    public string FullPath { get; init; } = string.Empty;

    public bool IsRoot { get; init; }

    /// <summary>하위 폴더 노드들.</summary>
    public ObservableCollection<ArchiveTreeNode> Children { get; } = new();

    /// <summary>이 폴더 바로 아래의 파일들(하위 폴더 제외).</summary>
    public List<ArchiveEntry> Files { get; } = new();

    /// <summary>TreeView 펼침 상태 (루트는 기본 펼침).</summary>
    public bool IsExpanded { get; set; }
}
