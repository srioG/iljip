using System.Runtime.CompilerServices;

namespace Iljip.Tests.TestSupport;

/// <summary>
/// 저장소 소스 파일 읽기(XAML·C++·매니페스트 '계약' 확인용). 빌드 산출물은 저장소 밖(D:\Python\일집-build)으로 가므로
/// 이 파일의 컴파일 시 경로(CallerFilePath)에서 저장소 루트를 거슬러 찾는다: …\일집\Iljip.Tests\TestSupport\RepoFiles.cs → …\일집.
/// </summary>
public static class RepoFiles
{
    public static string Root { get; } = FindRoot();

    private static string FindRoot([CallerFilePath] string thisFile = "")
    {
        var dir = new DirectoryInfo(Path.GetDirectoryName(thisFile)!);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Iljip.sln")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException($"저장소 루트(Iljip.sln)를 못 찾음: {thisFile}");
    }

    /// <summary>저장소 루트 기준 경로.</summary>
    public static string PathOf(params string[] parts) => Path.Combine(new[] { Root }.Concat(parts).ToArray());

    /// <summary>저장소 파일 내용(UTF-8).</summary>
    public static string Read(params string[] parts) => File.ReadAllText(PathOf(parts));
}
