using System.Security.Cryptography;
using System.Text;
using Iljip.Models;

namespace Iljip.Tests.TestSupport;

/// <summary>폴더 내용 스냅샷·비교, 목록 비교 도우미.</summary>
public static class TreeAssert
{
    public static string Sha256(string file)
    {
        using var s = File.OpenRead(file);
        return Convert.ToHexString(SHA256.HashData(s)).ToLowerInvariant();
    }

    public static string Sha256(byte[] data) => Convert.ToHexString(SHA256.HashData(data)).ToLowerInvariant();

    /// <summary>
    /// root 아래 전체를 '상대경로('/' 구분) → sha256(파일) 또는 null(폴더)' 로 만든다.
    /// nfc=true 면 경로를 NFC 로 바꿔 담는다(NFD 이름 비교용).
    /// </summary>
    public static SortedDictionary<string, string?> Snapshot(string root, bool nfc = false)
    {
        var map = new SortedDictionary<string, string?>(StringComparer.Ordinal);
        if (!Directory.Exists(root)) return map;
        foreach (var p in Directory.EnumerateFileSystemEntries(root, "*", SearchOption.AllDirectories))
        {
            string rel = Path.GetRelativePath(root, p).Replace('\\', '/');
            if (nfc) rel = rel.Normalize(NormalizationForm.FormC);
            map[rel] = File.Exists(p) ? Sha256(p) : null;
        }
        return map;
    }

    /// <summary>root 아래 '파일'만 상대경로로.</summary>
    public static List<string> Files(string root) =>
        !Directory.Exists(root)
            ? new List<string>()
            : Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
                .Select(p => Path.GetRelativePath(root, p).Replace('\\', '/'))
                .OrderBy(x => x, StringComparer.Ordinal).ToList();

    /// <summary>
    /// 해제 결과(dest)가 기대 목록과 '정확히' 같은지: 폴더·파일 집합 + 파일 sha256.
    /// 기대에 없는 파일이 하나라도 있으면 실패(대상 폴더 오염 금지).
    /// </summary>
    /// <param name="optional">있어도 되고 없어도 되는 경로(예: 지금은 빠지지만 고쳐지면 생길 빈 폴더). 있으면 종류·내용은 맞아야 한다.</param>
    public static void ExtractedTreeEquals(string dest, IEnumerable<ExpectedEntry> expected, bool nfc = false, string? context = null,
                                           IReadOnlyCollection<string>? optional = null)
    {
        var actual = Snapshot(dest, nfc);
        var exp = expected.ToDictionary(e => e.Path, e => e.IsDir ? null : e.Sha256, StringComparer.Ordinal);
        var problems = new List<string>();
        foreach (var (path, sha) in exp)
        {
            if (!actual.TryGetValue(path, out var got))
            {
                if (optional?.Contains(path) != true) problems.Add($"없음: {path}");
                continue;
            }
            if (sha is null && got is not null) problems.Add($"폴더여야 하는데 파일: {path}");
            else if (sha is not null && got is null) problems.Add($"파일이어야 하는데 폴더: {path}");
            else if (sha is not null && sha != got) problems.Add($"내용 다름(sha256): {path}");
        }
        foreach (var path in actual.Keys)
            if (!exp.ContainsKey(path)) problems.Add($"기대에 없는 항목이 생김: {Escape(path)}");
        Assert.True(problems.Count == 0, $"{context} 해제 결과 불일치 {problems.Count}건:\n  " + string.Join("\n  ", problems));
    }

    /// <summary>
    /// ListEntriesAsync 결과가 기대 목록과 같은지: (폴더 여부, 경로, 파일 크기) 집합 + 개수.
    /// 경로는 '\'→'/' 와 끝 '/' 제거로 정규화한다(zip·tar 는 폴더 끝에 '/', 7z 는 없음 — 둘 다 허용하는 게 현재 계약).
    /// </summary>
    public static void ListEquals(IReadOnlyList<ArchiveEntry> actual, IEnumerable<ExpectedEntry> expected, bool nfc = false, string? context = null)
    {
        static string Key(bool dir, string path, long size) => dir ? $"D {path}" : $"F {path} ({size}B)";
        var exp = expected.Select(e => Key(e.IsDir, e.Path, e.Size)).OrderBy(x => x, StringComparer.Ordinal).ToList();
        var got = actual.Select(e =>
        {
            string p = NormalizeEntryPath(e.Path);
            if (nfc) p = p.Normalize(NormalizationForm.FormC);
            return Key(e.IsDirectory, p, e.Size);
        }).OrderBy(x => x, StringComparer.Ordinal).ToList();

        var missing = exp.Except(got).ToList();
        var extra = got.Except(exp).ToList();
        Assert.True(missing.Count == 0 && extra.Count == 0 && exp.Count == got.Count,
            $"{context} 목록 불일치 (기대 {exp.Count}개, 실제 {got.Count}개)\n  빠짐: {string.Join(" | ", missing)}\n  더 있음: {string.Join(" | ", extra.Select(Escape))}");
    }

    /// <summary>
    /// 엔진과 무관한 목록 계약: '파일' 항목(경로·크기)은 기대와 정확히 같고, '폴더' 항목은 있어도 없어도 되지만
    /// 있다면 기대 목록에 있는 폴더여야 한다(엔진마다 폴더 항목을 넣는 방식이 달라 — 일집 ZIP 은 빈 폴더만, 반디집 ZIP 은 전부, 일집 TAR 는 없음).
    /// 정확한 모양은 '기록_' 테스트가 따로 고정한다.
    /// </summary>
    public static void ListFilesEqual(IReadOnlyList<ArchiveEntry> actual, IEnumerable<ExpectedEntry> expected, string? context = null)
    {
        var expList = expected.ToList();
        ListEquals(actual.Where(e => !e.IsDirectory).ToList(), expList.Where(e => !e.IsDir), context: context);
        var expDirs = expList.Where(e => e.IsDir).Select(e => e.Path).ToHashSet(StringComparer.Ordinal);
        var strayDirs = actual.Where(e => e.IsDirectory).Select(e => NormalizeEntryPath(e.Path)).Where(p => !expDirs.Contains(p)).ToList();
        Assert.True(strayDirs.Count == 0, $"{context} 기대에 없는 폴더 항목: {string.Join(" | ", strayDirs.Select(Escape))}");
    }

    public static string NormalizeEntryPath(string p) => p.Replace('\\', '/').TrimEnd('/');

    /// <summary>예외 실패 시 대상 폴더에 '파일'이 하나도 없어야 한다(폴더는 남을 수 있음 — 현재 동작).</summary>
    public static void NoFiles(string dest, string? context = null)
    {
        var files = Files(dest);
        Assert.True(files.Count == 0, $"{context} 실패했는데 대상 폴더에 파일이 남음: {string.Join(", ", files)}");
    }

    /// <summary>한글 자모(NFD)처럼 눈으로 구분 안 되는 문자를 \uXXXX 로 보이게.</summary>
    public static string Escape(string s) =>
        string.Concat(s.Select(c => (c >= 0x1100 && c <= 0x11FF) ? $"\\u{(int)c:X4}" : c.ToString()));
}
