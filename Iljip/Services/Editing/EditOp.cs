using Iljip.Models;

namespace Iljip.Services.Editing;

/// <summary>
/// 압축 파일 고치기 한 가지(W2-06, I2-3). 한 번의 <see cref="IArchiveEditor.EditAsync"/> 에 여러 개를 차례대로 줄 수 있다
/// (I3-3 '다시 넣기'는 바꿔 넣기 하나 — <see cref="Add"/>(…, ReplaceExisting: true)). 항목 키는 목록에 보이는 압축 안 경로('/' 구분 — 폴더는 끝 '/' 가 있어도 없어도 됨).
/// </summary>
public abstract record EditOp
{
    private EditOp()
    {
    }

    /// <summary>
    /// 파일·폴더를 압축 안 <paramref name="TargetFolder"/>(빈 문자열 = 맨 위)에 넣는다. 폴더는 그 이름째(반디집과 같음).
    /// <paramref name="AsName"/> 를 주면(원본이 파일 하나일 때만) 그 이름으로 넣는다 — 다시 넣기에서 원래 이름을 지키려고(임시 파일 이름이 다를 수 있음).
    /// 같은 이름이 이미 있으면 같은 이름 처리기(W2-05 <see cref="ExtractionScope"/>)에 묻는다 — 범위가 없으면 '이름 (1)'.
    /// </summary>
    /// <remarks>
    /// <paramref name="ReplaceExisting"/> 면 같은 이름 파일을 묻지 않고 바꿔 넣는다(I3-3 다시 넣기 — 그 항목이 없으면(연 뒤에 압축에서 지웠거나 이름을 바꿈)
    /// 원래 자리에 새로 넣는다. 같은 이름의 '폴더'는 지우지 않고 비켜 넣는다). 이때 <paramref name="AsName"/> 은 압축에 있던 원래 이름이라
    /// Windows 에 못 쓰는 이름(':' 든 이름·끝 점)도 그대로 쓴다(<see cref="EditPlanner{TSource}.ValidateExistingName"/>).
    /// </remarks>
    public sealed record Add(IReadOnlyList<string> Sources, string TargetFolder, string? AsName = null, bool ReplaceExisting = false) : EditOp;

    /// <summary>항목을 지운다. 폴더면 그 아래 전부.</summary>
    public sealed record Delete(IReadOnlyList<string> EntryKeys) : EditOp;

    /// <summary>항목 이름(마지막 조각)을 바꾼다. 폴더면 그 아래 전부의 경로가 따라 바뀐다. <paramref name="NewName"/> 에 '/'·'\' 는 못 쓴다.</summary>
    public sealed record Rename(string EntryKey, string NewName) : EditOp;

    /// <summary>빈 폴더를 만든다(<paramref name="Path"/> = 압축 안 경로). 이미 있으면 아무것도 안 한다.</summary>
    public sealed record NewFolder(string Path) : EditOp;
}

/// <summary>고치기 결과.</summary>
public sealed class EditResult
{
    public static readonly EditResult Empty = new();

    /// <summary>새로 넣은 파일 수(폴더 제외).</summary>
    public int AddedFiles { get; init; }

    /// <summary>같은 이름이라 '건너뛰기'로 넣지 않은 파일 수.</summary>
    public int CollisionSkipped { get; init; }

    /// <summary>다른 프로그램이 잠가 못 읽어 넣지 못한 원본(I0-2 와 같은 안내).</summary>
    public IReadOnlyList<SkippedSource> Skipped { get; init; } = Array.Empty<SkippedSource>();
}

/// <summary>
/// 압축 파일 고치기(W2-06, I2-3). 원본은 같은 폴더의 숨김 임시 파일(<c>.~이름.8글자.tmp</c>)에 새로 쓰고, 다 쓴 결과를 검사(<see cref="IArchiveTester"/> —
/// 목록 + 모든 항목의 확인값)해 맞을 때만 원본과 바꾼다(<see cref="ArchiveSwap"/>). 중간에 실패·취소하면 원본은 바이트 그대로이고 임시 파일도 남지 않는다.
/// 암호 압축은 같은 암호·같은 방식(ZIP 의 AES 세기·ZipCrypto, 7z 의 파일 이름 암호화)을 지킨다.
/// </summary>
public interface IArchiveEditor
{
    /// <summary>이 파일을 이 편집기로 고칠 수 있는 형식인가(확장자·나뉨만 본다 — 내용 문제는 <see cref="EditAsync"/> 가 <see cref="ArchiveNotEditableException"/>).</summary>
    bool CanEdit(string path);

    /// <summary>
    /// 고친다. 암호가 필요한데 없으면 <see cref="PasswordRequiredException"/>(목록까지 잠긴 7z 는 <see cref="ListPasswordRequiredException"/>), 틀리면
    /// <see cref="WrongPasswordException"/> — 화면은 암호를 묻고 다시 부른다. 같은 이름 창의 [취소]·진행 창 [취소]는 <see cref="OperationCanceledException"/>.
    /// </summary>
    Task<EditResult> EditAsync(
        string archivePath,
        IReadOnlyList<EditOp> ops,
        string? password,
        IProgress<ArchiveProgress>? progress,
        CancellationToken cancellationToken);
}
