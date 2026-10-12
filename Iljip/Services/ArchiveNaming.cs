using System.IO;

namespace Iljip.Services;

/// <summary>
/// 압축 파일 이름에서 '풀린 결과의 이름'을 정하는 규칙 모음.
/// (단일 스트림 형식의 항목 이름 — 이후 '여기에 풀기' 폴더 이름 규칙도 이곳에 모은다)
/// </summary>
public static class ArchiveNaming
{
    /// <summary>이름을 정할 수 없을 때 쓰는 기본 이름.</summary>
    public const string FallbackName = "압축풀림";

    /// <summary>
    /// 원본 이름을 저장하지 않는 단일 스트림(.gz 의 FNAME 없음, 순수 .bz2)의 항목 이름:
    /// 압축 파일 이름에서 마지막 확장자만 뗀다. 예) "견적서_2026.txt.gz" → "견적서_2026.txt".
    /// 결과가 비거나(".gz") 점·공백뿐이면("..gz" → ".", "...gz" → "..") <see cref="FallbackName"/> —
    /// '.'·'..' 는 폴더 자신·위 폴더를 뜻하고, Windows 는 이름 끝의 점·공백을 지워 버려 결국 빈 이름이 된다.
    /// </summary>
    public static string SingleStreamName(string archivePath)
    {
        string name = Path.GetFileNameWithoutExtension(archivePath);
        return string.IsNullOrWhiteSpace(name.TrimEnd('.', ' ')) ? FallbackName : name;
    }
}
