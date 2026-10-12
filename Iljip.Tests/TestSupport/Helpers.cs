using System.Reflection;
using Iljip.Models;
using Iljip.Services;
using Iljip.ViewModels;

namespace Iljip.Tests.TestSupport;

/// <summary>진행률 보고를 '그 자리에서 바로' 기록하는 IProgress (Progress&lt;T&gt; 처럼 다른 스레드로 미루지 않음).</summary>
public sealed class ProgressRecorder : IProgress<ArchiveProgress>
{
    private readonly object _gate = new();
    private readonly Action<ArchiveProgress>? _onReport;
    public List<ArchiveProgress> Reports { get; } = new();

    public ProgressRecorder(Action<ArchiveProgress>? onReport = null) => _onReport = onReport;

    public void Report(ArchiveProgress value)
    {
        lock (_gate) Reports.Add(value);
        _onReport?.Invoke(value);
    }
}

/// <summary>왕복 테스트용 원본 폴더(한글·공백·기호·빈 파일·빈 폴더·깊은 폴더·바이너리).</summary>
public static class SampleTree
{
    public const string RootName = "일집 왕복 원본";
    /// <summary>ZIP(DOS 시각)은 2초 단위라 짝수 초로 맞춘다.</summary>
    public static readonly DateTime Mtime = new(2025, 3, 4, 5, 6, 8, DateTimeKind.Local);

    /// <summary>parent 아래에 RootName 폴더를 만들고 그 경로를 돌려준다.</summary>
    public static string Create(string parent)
    {
        string root = Path.Combine(parent, RootName);
        Directory.CreateDirectory(Path.Combine(root, "빈 폴더"));
        Directory.CreateDirectory(Path.Combine(root, "하위", "깊이2", "깊이3", "깊이4", "깊이5"));
        File.WriteAllText(Path.Combine(root, "한글 파일.txt"), "안녕하세요, 일집 왕복 시험입니다.\r\n두 번째 줄\r\n");
        File.WriteAllText(Path.Combine(root, "Report (final) v2.txt"), "space and parens\n");
        File.WriteAllText(Path.Combine(root, "기호 #&+~!'@ 파일.txt"), "symbols");
        File.WriteAllBytes(Path.Combine(root, "빈파일.txt"), Array.Empty<byte>());
        File.WriteAllText(Path.Combine(root, "English.TXT"), "plain ascii");
        File.WriteAllText(Path.Combine(root, "하위", "깊이2", "깊이3", "깊이4", "깊이5", "깊은 파일 [끝].txt"), "deep");
        File.WriteAllText(Path.Combine(root, "하위", "반복.txt"), string.Concat(Enumerable.Repeat("반복 압축 잘 됨 0123456789\n", 4000)));
        var rnd = new Random(20261012);
        var bin = new byte[65536 + 123];
        rnd.NextBytes(bin);
        File.WriteAllBytes(Path.Combine(root, "하위", "바이너리.bin"), bin);
        foreach (var f in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
            File.SetLastWriteTime(f, Mtime);
        return root;
    }

    /// <summary>
    /// 일집 ZIP 의 '목록' 기대값: 파일 + 빈 폴더만. (현재 동작: 파일이 든 폴더는 따로 항목을 만들지 않고,
    /// 해제할 때 파일 경로에서 폴더가 생긴다 — 반디집 ZIP 은 모든 폴더 항목을 넣는 점이 다름)
    /// </summary>
    public static List<ExpectedEntry> FilesAndEmptyDirs(IEnumerable<ExpectedEntry> all)
    {
        var list = all.ToList();
        return list.Where(e => !e.IsDir || !list.Any(o => o.Path.StartsWith(e.Path + "/", StringComparison.Ordinal))).ToList();
    }

    /// <summary>원본 폴더의 기대 목록(부모 기준 상대경로, 폴더 포함).</summary>
    public static List<ExpectedEntry> ExpectedOf(string root)
    {
        string parent = Path.GetDirectoryName(root)!;
        var list = new List<ExpectedEntry> { new(Path.GetFileName(root), 0, null, true) };
        foreach (var p in Directory.EnumerateFileSystemEntries(root, "*", SearchOption.AllDirectories))
        {
            string rel = Path.GetRelativePath(parent, p).Replace('\\', '/');
            list.Add(File.Exists(p)
                ? new ExpectedEntry(rel, new FileInfo(p).Length, TreeAssert.Sha256(p), false)
                : new ExpectedEntry(rel, 0, null, true));
        }
        return list;
    }
}

/// <summary>본체의 private 정적 메서드를 '읽기만' 하는 리플렉션 도우미(본체 코드는 건드리지 않음).</summary>
public static class Internals
{
    /// <summary>MainViewModel.IsPasswordRelated — 해제 실패를 '암호 문제'로 보고 암호 창을 다시 띄울지 정하는 판정.</summary>
    public static bool IsPasswordRelated(Exception ex)
    {
        var m = typeof(MainViewModel).GetMethod("IsPasswordRelated", BindingFlags.NonPublic | BindingFlags.Static)
                ?? throw new MissingMethodException("MainViewModel.IsPasswordRelated 가 없어짐 — 이름이 바뀌었으면 테스트도 고칠 것");
        return (bool)m.Invoke(null, new object[] { ex })!;
    }
}

public static class Svc
{
    public static readonly ArchiveServiceLocator Locator = new();

    /// <summary>
    /// 일집 안의 열기와 같은 규칙으로 처리기를 고른다(W2-01 — 확장자 먼저, 모르는 확장자면 앞부분 서명: 자동 풀림 .exe·이름이 바뀐 디스크 이미지 등).
    /// 확장자로 고를 수 있는 파일은 예전(Resolve)과 같은 처리기다.
    /// </summary>
    public static IArchiveService For(string path) =>
        Locator.ResolveByContent(path) ?? throw new Xunit.Sdk.XunitException($"Resolve 결과 null: {Path.GetFileName(path)}");

    /// <summary>예외를 잡아 돌려준다(없으면 null). 비동기·동기 throw 모두.</summary>
    public static async Task<Exception?> Catch(Func<Task> action)
    {
        try { await action(); return null; }
        catch (Exception ex) { return ex; }
    }
}
