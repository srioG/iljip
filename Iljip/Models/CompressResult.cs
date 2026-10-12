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
}
