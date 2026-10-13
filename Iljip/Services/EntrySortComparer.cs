using System.Runtime.InteropServices;
using Iljip.Models;

namespace Iljip.Services;

/// <summary>
/// 목록 정렬(I0-5). 폴더가 언제나 먼저 오고(오름·내림 모두), 같은 종류끼리 고른 열로 정렬한다.
/// 값이 같으면 이름 오름차순으로 차례를 정한다(같은 크기 파일이 정렬할 때마다 뒤섞이지 않게).
/// 이름은 Windows 탐색기와 같은 '자연 정렬'(StrCmpLogicalW — 대소문자 무시, 숫자는 수 크기로: '2.txt' → '10.txt', '견적서_2' → '견적서_10',
/// 반디집도 같음). W1-D 처음엔 대소문자 무시 서수 비교(예전 목록 순서)였는데 '10.txt' 가 '2.txt' 앞에 와 1차 검증에서 바꿨다.
/// 수정한 날짜가 없는 항목은 방향과 상관없이 맨 뒤.
/// 열 이름은 화면 DataGrid 의 SortMemberPath(= 바인딩 경로)와 같다.
/// </summary>
public sealed class EntrySortComparer : IComparer<ArchiveEntry>
{
    public const string Name = nameof(ArchiveEntry.Name);
    public const string Size = nameof(ArchiveEntry.Size);
    public const string CompressedSize = nameof(ArchiveEntry.CompressedSize);
    public const string CompressionRatio = nameof(ArchiveEntry.CompressionRatio);
    public const string LastModified = nameof(ArchiveEntry.LastModified);

    /// <summary>'경로'(들어 있는 폴더 — 이름으로 찾을 때만 보이는 열, W2-09). 자연 정렬.</summary>
    public const string FolderPath = nameof(ArchiveEntry.FolderPath);

    /// <summary>정렬할 수 있는 열.</summary>
    public static IReadOnlyList<string> Members { get; } = new[] { Name, Size, CompressedSize, CompressionRatio, LastModified, FolderPath };

    public static bool IsSortable(string? member) => member is not null && Members.Contains(member);

    public EntrySortComparer(string member = Name, bool descending = false)
    {
        Member = IsSortable(member) ? member : Name;
        Descending = descending;
    }

    public string Member { get; }
    public bool Descending { get; }

    public int Compare(ArchiveEntry? x, ArchiveEntry? y)
    {
        if (ReferenceEquals(x, y)) return 0;
        if (x is null) return 1;
        if (y is null) return -1;

        // 폴더 먼저(방향과 무관)
        if (x.IsDirectory != y.IsDirectory) return x.IsDirectory ? -1 : 1;

        // 날짜 없음은 방향과 상관없이 맨 뒤
        if (Member == LastModified && x.LastModified.HasValue != y.LastModified.HasValue)
            return x.LastModified.HasValue ? -1 : 1;

        int c = Member switch
        {
            Size => x.Size.CompareTo(y.Size),
            CompressedSize => x.CompressedSize.CompareTo(y.CompressedSize),
            CompressionRatio => x.CompressionRatio.CompareTo(y.CompressionRatio),
            LastModified => Nullable.Compare(x.LastModified, y.LastModified),
            FolderPath => CompareNames(x.FolderPath, y.FolderPath),
            _ => ByName(x, y),
        };
        if (c != 0) return Descending ? -c : c;

        // 같은 값이면 이름 오름차순(방향과 무관), 그래도 같으면 전체 경로
        c = ByName(x, y);
        return c != 0 ? c : string.CompareOrdinal(x.Path, y.Path);
    }

    private static int ByName(ArchiveEntry x, ArchiveEntry y) => CompareNames(x.Name, y.Name);

    /// <summary>탐색기와 같은 이름 비교(자연 정렬). 같은 이름으로 보면(대소문자만 다름 등) 0.</summary>
    public static int CompareNames(string? a, string? b) => StrCmpLogicalW(a ?? string.Empty, b ?? string.Empty);

    // 탐색기가 이름 정렬에 쓰는 함수(shlwapi). 현재 사용자 언어 규칙을 따른다 — 그래서 한글·영문 차례도 그 PC 의 탐색기와 같다.
    [DllImport("shlwapi.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern int StrCmpLogicalW(string psz1, string psz2);
}
