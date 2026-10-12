using Iljip.Services;
using SharpSevenZip;

namespace Iljip.Tests.TestSupport;

/// <summary>
/// 시험 재료 만들기: 7z·xz 를 7z.dll 로 직접 만든다 — '풀기' 시험에 쓸 압축만(일집과 다른 길로 만든 재료라 풀기 시험이 만들기 버그에 흔들리지 않게).
/// 일집 자신의 7z 만들기(W2-02)는 SevenZipCreateTests 가 본다.
/// </summary>
public static class SevenZipTestArchives
{
    /// <summary>
    /// root 폴더를 폴더 이름까지 담아 7z 로 만든다(빈 폴더 포함). password 가 있으면 암호를 걸고,
    /// encryptNames 면 파일 이름까지 감춘다(-mhe, 반디집 '파일 이름 암호화'와 같은 모양).
    /// </summary>
    /// <param name="store">true 면 압축하지 않고 담기만(큰 시험 재료를 빨리 만들 때).</param>
    public static string SevenZip(string root, string archive, string? password = null, bool encryptNames = false, bool store = false)
    {
        SevenZipLibrary.EnsureLoaded();
        Assert.True(SevenZipLibrary.IsAvailable, "7z.dll 을 불러오지 못해 시험 재료를 만들 수 없음");
        root = Path.GetFullPath(root).TrimEnd('\\');
        string parent = Path.GetDirectoryName(root)!;
        int rootLength = parent.EndsWith('\\') ? parent.Length : parent.Length + 1;
        var items = Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
            .Concat(Directory.EnumerateDirectories(root, "*", SearchOption.AllDirectories)
                .Where(d => !Directory.EnumerateFileSystemEntries(d).Any()))
            .Select(Path.GetFullPath)
            .ToArray();
        var c = new SharpSevenZipCompressor
        {
            ArchiveFormat = OutArchiveFormat.SevenZip,
            CompressionLevel = store ? CompressionLevel.None : CompressionLevel.Normal,
            EncryptHeaders = password is not null && encryptNames,
        };
        if (password is null) c.CompressFiles(archive, rootLength, items);
        else c.CompressFilesEncrypted(archive, rootLength, password, items);
        return archive;
    }

    /// <summary>파일 하나를 .xz 로(7z.dll — 내용은 그 파일 하나).</summary>
    public static string Xz(string file, string archive)
    {
        SevenZipLibrary.EnsureLoaded();
        Assert.True(SevenZipLibrary.IsAvailable, "7z.dll 을 불러오지 못해 시험 재료를 만들 수 없음");
        var c = new SharpSevenZipCompressor { ArchiveFormat = OutArchiveFormat.XZ };
        c.CompressFiles(archive, Path.GetFullPath(file));
        return archive;
    }

    /// <summary>7z 의 압축된 데이터 한가운데(앞 32바이트 시작 머리 뒤, 끝의 목록 머리 앞) 한 바이트를 뒤집은 사본.</summary>
    public static string CorruptMiddle(string arc, string to)
    {
        var b = File.ReadAllBytes(arc);
        b[32 + (b.Length - 32) / 3] ^= 0x01;
        File.WriteAllBytes(to, b);
        return to;
    }
}
