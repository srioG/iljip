using System.IO.Compression;
using System.Text;

namespace Iljip.Tests.TestSupport;

/// <summary>시험 안에서 작은 ZIP 을 만든다(.NET ZipArchive — 한글 이름은 UTF-8 표시). 파일 이름이 '/' 로 끝나면 폴더 항목.</summary>
public static class TestZip
{
    public static string Create(string path, params (string Name, byte[] Data)[] items)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using var fs = File.Create(path);
        using var zip = new ZipArchive(fs, ZipArchiveMode.Create, leaveOpen: false, entryNameEncoding: Encoding.UTF8);
        foreach (var (name, data) in items)
        {
            var e = zip.CreateEntry(name, CompressionLevel.Fastest);
            e.LastWriteTime = new DateTimeOffset(2026, 3, 4, 5, 6, 8, TimeSpan.FromHours(9));
            if (name.EndsWith('/')) continue;
            using var s = e.Open();
            s.Write(data);
        }
        return path;
    }

    /// <summary>글자 내용 파일.</summary>
    public static (string, byte[]) Text(string name, string text) => (name, Encoding.UTF8.GetBytes(text));

    /// <summary>바이트 내용 파일(시드로 만든 의사 난수 — 그림 자리 등).</summary>
    public static (string, byte[]) Bytes(string name, int length, int seed = 1)
    {
        var b = new byte[length];
        new Random(seed).NextBytes(b);
        return (name, b);
    }
}
