using System.IO;
using System.Text.RegularExpressions;

namespace Iljip.Services;

/// <summary>
/// 압축 파일 하나가 쓰는 모든 조각 파일(W2-05 — '풀고 나서 압축 파일 지우기' I2-5 가 나뉜 압축이면 조각을 모두 휴지통으로).
/// 이름 규칙(W2-01·W2-03 과 같음)과 실제로 있는 파일로만 정한다 — 파일 내용을 읽는 것은 RAR 볼륨 표시·ZIP 나눔 끝 머리뿐.
/// 확실하지 않으면 그 파일 하나만 돌려준다(남의 파일을 지우지 않게).
/// </summary>
public static class ArchiveVolumes
{
    private static readonly Regex Numbered = new(@"^(?<base>.*)\.(?<n>\d{3,})$", RegexOptions.CultureInvariant);
    private static readonly Regex EggVolume = new(@"^(?<base>.*)\.vol(?<n>\d+)\.egg$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex RarPart = new(@"\.part\d+\.rar$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    /// <summary>
    /// <paramref name="archivePath"/>(열거나 푼 파일 — 첫 조각·나눔 ZIP 의 .zip 등)와 같은 세트의 조각 전부(있는 것만, 차례대로).
    /// 나뉘지 않은 압축이면 그 파일 하나.
    /// </summary>
    public static IReadOnlyList<string> AllParts(string archivePath)
    {
        string full = Path.GetFullPath(archivePath);
        string dir = Path.GetDirectoryName(full) ?? string.Empty;
        string name = Path.GetFileName(full);
        var result = new List<string>();
        try
        {
            // 숫자 분할 '이름.7z.001'·'이름.zip.001' …: 001 부터 번호가 빌 때까지
            var m = Numbered.Match(name);
            if (m.Success)
            {
                string b = m.Groups["base"].Value;
                int digits = m.Groups["n"].Value.Length;
                for (int i = 1; i < 100000; i++)
                {
                    string p = Path.Combine(dir, $"{b}.{i.ToString().PadLeft(digits, '0')}");
                    if (!File.Exists(p)) break;
                    result.Add(p);
                }
            }
            // PKZIP 나눔 '이름.z01 … 이름.zip'(끝 머리가 조각 수를 알려 줌 — W2-03)
            else if (name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) && SpannedZipStream.DiskCount(full) is var disks and >= 2)
            {
                for (int i = 1; i < disks; i++)
                {
                    string p = SpannedZipStream.PrecedingVolumePath(full, i);
                    if (File.Exists(p)) result.Add(p);
                }
                result.Add(full);
            }
            // 여러 조각 RAR('이름.partN.rar' 또는 볼륨 표시가 있는 '이름.rar' + '.r00' …)
            else if (name.EndsWith(".rar", StringComparison.OrdinalIgnoreCase)
                     && (RarPart.IsMatch(name) || ArchiveHeaderProbe.ProbeRar(full).IsVolume))
            {
                result.AddRange(ArchiveCompleteness.RarVolumeChain(full).Existing);
            }
            // 분할 EGG '이름.vol1.egg' …
            else if (EggVolume.Match(name) is { Success: true } e)
            {
                string b = e.Groups["base"].Value;
                for (int i = 1; i < 10000; i++)
                {
                    string p = Path.Combine(dir, $"{b}.vol{i}.egg");
                    if (!File.Exists(p)) break;
                    result.Add(p);
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            result.Clear();
        }
        // 규칙으로 못 모았거나(이름이 규칙과 다름) 고른 파일이 목록에 없으면 그 파일 하나만(남의 파일을 지우지 않게)
        if (result.Count == 0 || !result.Contains(full, StringComparer.OrdinalIgnoreCase))
            return new[] { full };
        return result;
    }
}
