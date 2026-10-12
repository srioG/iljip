namespace Iljip.Models;

/// <summary>압축하면서 건너뛴 원본 하나(못 연 파일, 못 들어간 폴더)와 그 이유(쉬운 한국어).</summary>
public sealed record SkippedSource(string Path, string Reason);

/// <summary>
/// 압축 결과(I0-2). 다른 프로그램이 잠근 파일처럼 못 읽은 원본은 압축 전체를 실패시키지 않고 건너뛰고 여기에 적는다 —
/// 화면이 끝에 '건너뛴 파일 N개'를 알린다(결과 압축 파일은 지우지 않음).
/// </summary>
public sealed class CompressResult
{
    public static readonly CompressResult Empty = new(Array.Empty<SkippedSource>());

    public CompressResult(IReadOnlyList<SkippedSource> skipped) => Skipped = skipped;

    /// <summary>건너뛴 원본(없으면 빈 목록).</summary>
    public IReadOnlyList<SkippedSource> Skipped { get; }

    /// <summary>
    /// 나눠 만든 조각들(차례대로 — ZIP 은 이름.z01 … 이름.zip, 7Z 는 이름.7z.001 …). 나누지 않았으면(또는 다 만든 결과가 조각 크기 이하라 한 파일로 둔 경우) 빈 목록 — W2-03.
    /// 같은 이름의 조각이 이미 있으면 세트 전체가 '이름 (2)' 로 바뀌므로, 실제 이름은 이것을 볼 것.
    /// </summary>
    public IReadOnlyList<string> Volumes { get; init; } = Array.Empty<string>();

    /// <summary>나눠 만든 세트에서 열 조각(ZIP 은 마지막 이름.zip, 7Z 는 첫 이름.7z.001). 나누지 않았으면 null — W2-03.</summary>
    public string? OpenPath { get; init; }
}
