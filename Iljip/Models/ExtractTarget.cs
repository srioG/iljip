using Iljip.Services;

namespace Iljip.Models;

// 풀기 창(W2-09 — I2-13)의 요청·답. '다른 곳에 풀기'(툴바 [압축 풀기]·Ctrl+E·선택 항목 풀기)·명령줄 '--extract'·열기 창에서 여러 개 고르기가
// 폴더 고르기 창 대신 이 창을 쓴다. 창 없이 풀기(--extract-to)·우클릭 '여기에/이름 폴더에 풀기'는 쓰지 않는다.

/// <summary>풀기 창에 넘기는 것(처음 값은 설정 extract.* — 부르는 쪽이 채움).</summary>
public sealed record ExtractTargetRequest
{
    /// <summary>풀 압축 파일(하나 또는 여럿, 전체 경로).</summary>
    public IReadOnlyList<string> Archives { get; init; } = Array.Empty<string>();

    /// <summary>고른 항목만 푸는가. 그러면 '다 풀고 압축 파일을 휴지통으로' 칸은 숨긴다(일부만 푼 압축은 지우지 않음).</summary>
    public bool SelectedOnly { get; init; }

    /// <summary>예전 폴더 고르기 창의 제목(가짜·대체 경로가 <see cref="IUserInteraction.PickFolder"/> 로 넘김).</summary>
    public string Title { get; init; } = "압축을 풀 폴더를 선택하세요";

    /// <summary>풀 곳의 처음 값 — 압축 파일이 있는 폴더.</summary>
    public string InitialFolder { get; init; } = string.Empty;

    /// <summary>최근에 푼 곳(지금 있는 폴더만, 가장 최근이 맨 앞 — 최대 10개, <see cref="ExtractSettings.VisibleRecentFolders"/>).</summary>
    public IReadOnlyList<string> RecentFolders { get; init; } = Array.Empty<string>();

    /// <summary>'압축파일명으로 폴더 만들기' 처음 값(extract.makeFolder — 기본 켬, 창에서 고른 값을 기억).</summary>
    public bool MakeFolder { get; init; } = true;

    /// <summary>'다 풀고 압축 파일을 휴지통으로' 처음 값(extract.deleteArchiveAfter — 기본 꺼짐, 창에서 켠 것은 이번에만).</summary>
    public bool DeleteArchiveAfter { get; init; }

    /// <summary>'다 풀면 폴더 열기' 처음 값(extract.dialogOpenFolderAfter — 기본 꺼짐, 창에서 고른 값을 기억).</summary>
    public bool OpenFolderAfter { get; init; }
}

/// <summary>풀기 창의 답. 취소하면 답이 없다(null).</summary>
/// <param name="Folder">고른 풀 곳(있는 폴더).</param>
/// <param name="MakeFolder">그 안에 압축 이름의 폴더를 만들어 풀지(이미 있으면 '이름 (2)' …, 합치지 않음).</param>
/// <param name="DeleteArchiveAfter">다 풀고 압축 파일(나뉜 조각 모두)을 휴지통으로(오류·건너뛴 파일·취소가 없을 때만 — W2-05 규칙 그대로).</param>
/// <param name="OpenFolderAfter">다 풀면 푼 폴더를 탐색기로 열지.</param>
public sealed record ExtractTarget(string Folder, bool MakeFolder, bool DeleteArchiveAfter, bool OpenFolderAfter)
{
    /// <summary>
    /// 압축 하나를 실제로 풀 곳: 폴더 만들기면 고른 폴더 안의 '압축 이름' 폴더(이미 있으면 '이름 (2)' …), 아니면 고른 폴더 그대로.
    /// 풀기 직전에 부른다(그사이 생긴 폴더와도 겹치지 않게).
    /// </summary>
    public string DestinationFor(string archivePath) =>
        MakeFolder ? ArchiveNaming.UniqueFolder(Folder, ArchiveNaming.FolderNameFor(archivePath)) : Folder;
}
