using System.IO;
using System.Text.RegularExpressions;

namespace Iljip.Services;

/// <summary>
/// 압축 파일 이름에서 '풀린 결과의 이름'을 정하는 규칙 모음.
/// (단일 스트림 형식의 항목 이름, '"이름"\ 에 풀기' 폴더 이름과 그 폴더를 못 채웠을 때의 정리)
/// ★ <see cref="FolderNameFor"/> 는 셸 확장(C++ Iljip.ShellExtension/IljipCommands.cpp 의 FolderNameFor)에도 똑같이 있다 —
///   메뉴 제목과 실제로 생기는 폴더 이름이 같아야 하므로 한쪽을 고치면 다른 쪽도 고칠 것(ShellExtensionContractTests 가 대조).
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

    // 숫자 분할 조각 접미사('.001') — '보고서.7z.001' → '보고서.7z'
    private static readonly Regex NumberedVolume = new(@"\.[0-9]{3}$", RegexOptions.CultureInvariant);
    // RAR '.part1'·EGG '.vol1' 조각 표시(마지막 확장자 바로 앞) — '보고서.part1.rar' → '보고서.rar'
    private static readonly Regex PartOrVolume = new(@"\.(part|vol)[0-9]+(?=\.[^.]*$)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    // 확장자 두 개를 한 번에 떼는 묶음 압축
    private static readonly string[] CompoundExtensions = { ".tar.gz", ".tar.bz2", ".tar.xz" };

    /// <summary>
    /// '"이름"\ 에 풀기'(--extract-to-folder)가 만들 폴더 이름. 파일 이름(문자열)만 보고 내용은 읽지 않는다. 차례로 뗀다:
    /// ① 숫자 분할 접미사 '.001' ② '.partN'(rar)·'.volN'(egg) ③ '.tar.gz'·'.tar.bz2'·'.tar.xz' 를 통째로, 아니면 마지막 확장자 하나.
    /// 끝의 점·공백은 지우고(Windows 가 어차피 지움), 비면 <see cref="FallbackName"/>.
    /// 예) 'x.zip'·'x.tar.gz'·'x.tgz'·'x.7z.001'·'x.part1.rar'·'x.vol1.egg' → 'x', '.zip' → '압축풀림', '이름 있는.한글.zip' → '이름 있는.한글'.
    /// </summary>
    public static string FolderNameFor(string archivePath)
    {
        string name = Path.GetFileName(archivePath ?? string.Empty);
        name = NumberedVolume.Replace(name, string.Empty);
        name = PartOrVolume.Replace(name, string.Empty);

        string? compound = CompoundExtensions.FirstOrDefault(e => name.EndsWith(e, StringComparison.OrdinalIgnoreCase));
        if (compound is not null)
            name = name[..^compound.Length];
        else
        {
            int dot = name.LastIndexOf('.');
            if (dot >= 0) name = name[..dot];
        }

        name = name.TrimEnd('.', ' ');
        return string.IsNullOrWhiteSpace(name) ? FallbackName : name;
    }

    /// <summary>
    /// <paramref name="parent"/> 안에 새로 쓸 폴더 경로: '이름' 이 폴더로도 파일로도 없으면 그대로, 있으면 '이름 (2)', '이름 (3)' …
    /// (기존 폴더에 말없이 합쳐 풀지 않는다 — 반디집은 오류를 내지만 일집은 새 번호 폴더. 번호가 (2)부터인 것은 반디집·탐색기 관례)
    /// 확인만 하고 만들지는 않는다(풀기가 만든다 — 암호 오류면 그 폴더까지 되돌릴 수 있게).
    /// </summary>
    public static string UniqueFolder(string parent, string name)
    {
        string first = Path.Combine(parent, name);
        if (!Exists(first)) return first;
        for (int i = 2; i < int.MaxValue; i++)
        {
            string candidate = Path.Combine(parent, $"{name} ({i})");
            if (!Exists(candidate)) return candidate;
        }
        return Path.Combine(parent, $"{name} ({Guid.NewGuid():N})");   // 사실상 도달 불가

        static bool Exists(string p) => Directory.Exists(p) || File.Exists(p);
    }

    /// <summary>
    /// <see cref="UniqueFolder"/> 로 고른 새 폴더에 풀다가 취소·손상으로 멈췄을 때: 그 안에 파일이 하나도 없으면(빈 하위 폴더뿐) 아래에서부터 지운다
    /// (W1-D 1차 검증 — 빈 '이름 (2)' 가 남아 다음에 풀면 '이름 (3)' 으로 번호가 밀렸다). 파일이 하나라도 있으면 그 줄기는 남긴다
    /// (앞서 다 풀린 파일은 남긴다는 계약 그대로). 폴더 지우기는 비어 있을 때만 되는 방식이라 그새 누가 넣은 파일을 지우는 일은 없고,
    /// 연결 지점(정션·심볼릭 링크)은 따라 들어가지 않고 남긴다. 실패는 조용히 넘긴다. 다 지웠으면 true.
    /// </summary>
    public static bool RemoveFolderIfNoFiles(string folder)
    {
        try
        {
            var dir = new DirectoryInfo(folder);
            if (!dir.Exists || (dir.Attributes & FileAttributes.ReparsePoint) != 0) return false;

            bool allGone = true;
            foreach (var child in dir.EnumerateFileSystemInfos().ToList())
            {
                bool isPlainFolder = (child.Attributes & FileAttributes.Directory) != 0
                                     && (child.Attributes & FileAttributes.ReparsePoint) == 0;
                if (!isPlainFolder || !RemoveFolderIfNoFiles(child.FullName))
                    allGone = false;
            }
            if (!allGone) return false;
            dir.Delete(recursive: false);   // 비었을 때만 지워진다
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            return false;
        }
    }
}
