using System.Globalization;
using System.IO;

namespace Iljip.Services;

/// <summary>
/// 숫자 조각 자르기(W2-03 — 7Z 나눠 만들기 이름.7z.001 …). 7z 볼륨은 7-Zip 의 '나누기'와 같은 단순 절단이라(이어 붙이면 원래 7z —
/// <see cref="SplitVolumeStream"/> 가 그렇게 읽음) 다 만든 7z 하나를 바이트 그대로 자른다. 7z.dll 의 볼륨 쓰기(VolumeSize)는 경로 방식·새로 만들기에서만 되고
/// 빈 폴더 덧붙이기(W2-02)와 같이 못 쓰므로, 단일 7z 를 만든 뒤 자르는 한 길로 통일했다(설계 W2-03 §2 — 같은 바이트, 더 단순).
/// 뒤에서부터 잘라 내며(SetLength) 만들어 디스크를 원래 크기 + 조각 하나만큼만 더 쓰고, 첫 조각은 남은 앞부분을 이름만 바꾼다(복사 없음).
/// </summary>
internal static class VolumeSplitter
{
    /// <summary>조각 수 한도(.001 … .999).</summary>
    public const int MaxVolumes = 999;

    /// <summary>조각 이름: '이름.7z.001' …(<paramref name="index"/> 는 0부터).</summary>
    public static string VolumeName(string basePath, int index) => basePath + "." + (index + 1).ToString("000", CultureInfo.InvariantCulture);

    /// <summary>
    /// <paramref name="filePath"/> 를 <paramref name="volumeSize"/> 바이트 조각으로 자른다(<paramref name="basePath"/>.001 …). 원래 파일은 첫 조각이 된다.
    /// 조각이 <see cref="MaxVolumes"/> 개를 넘으면 자르기 전에 <see cref="TooManyVolumesException"/>.
    /// </summary>
    public static IReadOnlyList<string> Split(string filePath, string basePath, long volumeSize, CancellationToken ct, Action<long>? onBytes = null)
    {
        if (volumeSize < 1) throw new ArgumentOutOfRangeException(nameof(volumeSize));
        long length = new FileInfo(filePath).Length;
        long count = Math.Max(1, length / volumeSize + (length % volumeSize == 0 ? 0 : 1));
        if (count > MaxVolumes) throw new TooManyVolumesException(MaxVolumes);
        var names = Enumerable.Range(0, (int)count).Select(i => VolumeName(basePath, i)).ToList();
        using (var fs = new FileStream(filePath, FileMode.Open, FileAccess.ReadWrite, FileShare.None, 1 << 16))
        {
            for (int i = (int)count - 1; i >= 1; i--)
            {
                ct.ThrowIfCancellationRequested();
                long from = i * volumeSize, to = Math.Min(length, from + volumeSize);
                using (var output = new FileStream(names[i], FileMode.CreateNew, FileAccess.Write, FileShare.None, 1 << 16))
                    Copy(fs, from, to, output, ct, onBytes);
                fs.SetLength(from);
            }
        }
        ct.ThrowIfCancellationRequested();
        File.Move(filePath, names[0]);
        return names;
    }

    /// <summary><paramref name="source"/> 의 [from, to) 를 <paramref name="target"/> 에 옮겨 쓴다(1MB 씩, 그때마다 취소를 봄).</summary>
    internal static void Copy(Stream source, long from, long to, Stream target, CancellationToken ct, Action<long>? onBytes)
    {
        var buffer = new byte[1 << 20];
        source.Position = from;
        long left = to - from;
        while (left > 0)
        {
            ct.ThrowIfCancellationRequested();
            int n = source.Read(buffer, 0, (int)Math.Min(buffer.Length, left));
            if (n <= 0) throw new EndOfStreamException("조각으로 나눌 파일이 생각보다 짧아요.");
            target.Write(buffer, 0, n);
            left -= n;
            onBytes?.Invoke(n);
        }
    }
}
