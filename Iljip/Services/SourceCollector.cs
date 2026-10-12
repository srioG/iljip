using System.IO;
using Iljip.Models;

namespace Iljip.Services;

/// <summary>압축할 원본을 훑은 결과: 담을 파일, 따로 기록할 빈 폴더, 건너뛴 것(I0-2).</summary>
internal sealed class SourceSet
{
    /// <summary>담을 파일(원본 경로, 압축 안 이름 — 부모 기준 상대, '/' 구분).</summary>
    public List<(string SourceFile, string EntryName)> Files { get; } = new();

    /// <summary>파일도 하위 폴더도 없는 '완전히 빈' 폴더(원본 경로, 압축 안 이름 — 끝에 '/').</summary>
    public List<(string SourceDir, string EntryName)> EmptyDirectories { get; } = new();

    /// <summary>못 들어간 폴더·없어진 원본.</summary>
    public List<SkippedSource> Skipped { get; } = new();
}

/// <summary>
/// 압축할 원본 모으기(ZIP·TAR 공용). 폴더 자체의 이름을 항목 루트로 둔다(반디집·알집 기본 동작과 같음).
/// 예전엔 Directory.EnumerateFiles(…, AllDirectories) 한 번이라 하위 폴더 하나만 권한이 없어도 압축 전체가 실패했다 —
/// 이제 폴더를 하나씩 열어 못 여는 폴더는 건너뛰고 <see cref="SourceSet.Skipped"/> 에 적는다(I0-2).
/// 차례는 예전과 같다: 원본 순서대로, 폴더 안은 너비 우선(.NET 재귀 열거와 같은 큐 순서), 숨김·시스템 파일 포함.
/// </summary>
internal static class SourceCollector
{
    // 예전 EnumerateFiles(path, "*", SearchOption) 와 같은 조건 — 숨김·시스템도 담고, 못 여는 폴더는 예외로 알려 준다.
    private static readonly EnumerationOptions OneLevel = new()
    {
        RecurseSubdirectories = false,
        IgnoreInaccessible = false,
        AttributesToSkip = 0,
        MatchType = MatchType.Win32,
        ReturnSpecialDirectories = false,
    };

    public static SourceSet Collect(IEnumerable<string> sourcePaths)
    {
        var set = new SourceSet();
        foreach (var src in sourcePaths)
        {
            if (string.IsNullOrEmpty(src)) continue;
            if (File.Exists(src))
            {
                set.Files.Add((src, Path.GetFileName(src)));
                continue;
            }
            if (!Directory.Exists(src))
            {
                // 담아 둔 뒤에 지워졌거나 옮겨진 원본 — 예전엔 말없이 빠졌다
                set.Skipped.Add(new SkippedSource(src, SourceFileOpener.ReasonMissing));
                continue;
            }

            string baseDir = src.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            string parent = Path.GetDirectoryName(baseDir) ?? string.Empty;
            string Rel(string p) => Path.GetRelativePath(parent, p).Replace(Path.DirectorySeparatorChar, '/');

            var pending = new Queue<string>();
            pending.Enqueue(baseDir);
            while (pending.Count > 0)
            {
                string dir = pending.Dequeue();
                List<FileSystemInfo> children;
                try
                {
                    children = new DirectoryInfo(dir).EnumerateFileSystemInfos("*", OneLevel).ToList();
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
                {
                    set.Skipped.Add(new SkippedSource(dir, SourceFileOpener.ReasonFor(ex)));
                    continue;
                }

                if (children.Count == 0)
                {
                    string rel = Rel(dir);
                    if (rel.Length > 0 && rel != ".")
                        set.EmptyDirectories.Add((dir, rel.TrimEnd('/') + "/"));
                    continue;
                }

                foreach (var child in children)
                {
                    string path = Path.Combine(dir, child.Name);
                    if ((child.Attributes & FileAttributes.Directory) != 0)
                        pending.Enqueue(path);
                    else
                        set.Files.Add((path, Rel(path)));
                }
            }
        }
        return set;
    }
}
