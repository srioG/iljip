using System.Globalization;
using System.IO;

namespace Iljip.Services;

/// <summary>
/// PKZIP 나눔(이름.z01 … 이름.zip) 세트를 '보통 ZIP 한 개'처럼 보여 주는 읽기 전용·위치 이동 가능 스트림(W2-03).
/// = 조각을 차례로 이어 붙인 앞부분(첫 조각 맨 앞 나눔 표시 4바이트는 뺌) + 디스크 번호 0·이어 붙인 위치로 고쳐 쓴 중앙 디렉터리·끝 머리(메모리).
/// <para>
/// 왜: SharpCompress 0.50.5 는 나눔 ZIP 의 중앙 디렉터리를 마지막 조각(.zip) 안에서만 읽는다(ZipArchive.LoadEntries — 목록이 조각을 넘으면 못 읽음,
/// 일집이 항목이 아주 많은 ZIP 을 나누면 그렇게 된다). 그리고 마지막 조각만 보는 암호 판정(<see cref="ZipPasswordProbe"/>)·AES 직접 해제
/// (<see cref="WinZipAes"/>)는 나눔 ZIP 을 판정하지 못해, 반디집·일집이 만든 한글 ZipCrypto 암호(CP949) 나눔 ZIP 을 맞는 암호로도 못 풀었다.
/// 이 스트림으로 열면 숫자 분할(.zip.001 — <see cref="SplitVolumeStream"/>)처럼 main ZIP 처리기가 보통 ZIP 과 똑같이 다룬다.
/// </para>
/// 마지막 조각인지는 끝 머리의 '이 디스크 번호'(ZIP64 면 위치 표의 '전체 디스크 수')로 안다. 앞 조각(.z01 …)이 하나라도 없으면 그 이름으로
/// <see cref="VolumeMissingException"/>. 모양이 틀리면 <see cref="CorruptArchiveException"/>. 조각 파일은 열어 두고 Dispose 때 닫는다.
/// </summary>
internal sealed class SpannedZipStream : Stream
{
    /// <summary>중앙 디렉터리 크기 한도(메모리로 고쳐 씀 — ZipPasswordProbe 와 같은 값).</summary>
    private const long MaxDirectoryBytes = 256L * 1024 * 1024;

    /// <summary>나눔 세트로 볼 조각 수 한도(.z01 … .z999 — 그보다 크면 깨진 끝 머리로 보고 보통 ZIP 처럼 엔진에 맡김).</summary>
    private const long MaxDisks = 1000;

    private readonly SplitVolumeStream _parts;
    private readonly long _skip;
    private readonly long _dataLength;
    private readonly byte[] _directory;
    private long _position;

    private SpannedZipStream(SplitVolumeStream parts, Layout layout)
    {
        _parts = parts;
        _skip = layout.Skip;
        _dataLength = layout.DataLength;
        _directory = layout.Directory;
    }

    /// <summary>모은 조각 경로(.z01 … .zip 차례).</summary>
    public IReadOnlyList<string> Volumes => _parts.Volumes;

    /// <summary>
    /// <paramref name="zipPath"/> 가 PKZIP 나눔 세트의 마지막 조각(.zip)이면 세트의 조각 수(2 이상), 아니면 0.
    /// 끝(최대 64KB)만 읽고, 못 읽으면 0(보통 ZIP 길로 — 진짜 오류는 그쪽이 알림).
    /// </summary>
    public static int DiskCount(string zipPath)
    {
        try
        {
            if (!File.Exists(zipPath)) return 0;
            using var fs = new FileStream(zipPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 1 << 12);
            var end = ZipDirectory.FindEnd(fs);
            if (end is null) return 0;
            long disks = end.TotalDisks;
            return disks is >= 2 and <= MaxDisks ? (int)disks : 0;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return 0;
        }
    }

    /// <summary>나눔 세트의 마지막 조각인가(<see cref="DiskCount"/> ≥ 2).</summary>
    public static bool IsLastVolume(string zipPath) => DiskCount(zipPath) >= 2;

    /// <summary>앞 조각 이름: '이름.zip' 의 <paramref name="index"/>(1부터) 번째 → '이름.z01' ….</summary>
    internal static string PrecedingVolumePath(string zipPath, int index)
    {
        string ext = Path.GetExtension(zipPath);
        string stem = ext.Length > 0 ? zipPath[..^ext.Length] : zipPath;
        return stem + ".z" + index.ToString("00", CultureInfo.InvariantCulture);
    }

    /// <summary>마지막 조각(<paramref name="zipPath"/>)으로 세트를 연다. 앞 조각이 없으면 그 이름으로 <see cref="VolumeMissingException"/>.</summary>
    public static SpannedZipStream Open(string zipPath)
    {
        int disks = DiskCount(zipPath);
        if (disks < 2) throw new ArgumentException("나눔 ZIP 의 마지막 조각이 아니에요.", nameof(zipPath));
        var paths = new List<string>(disks);
        for (int i = 1; i < disks; i++)
        {
            string p = PrecedingVolumePath(zipPath, i);
            if (!File.Exists(p)) throw new VolumeMissingException(Path.GetFileName(p));
            paths.Add(p);
        }
        paths.Add(zipPath);
        var parts = SplitVolumeStream.OpenParts(paths);
        try
        {
            return new SpannedZipStream(parts, LayoutOf(paths, parts));
        }
        catch
        {
            parts.Dispose();
            throw;
        }
    }

    /// <summary>보통 ZIP 으로 보이게 고친 모양: 앞에서 뺄 바이트(나눔 표시), 데이터 길이, 고쳐 쓴 중앙 디렉터리 + 끝 머리.</summary>
    private sealed record Layout(long Skip, long DataLength, byte[] Directory);

    // AES 항목은 항목마다 스트림을 새로 연다(WinZipAes.OpenEntry) — 같은 세트의 모양을 다시 만들지 않게 마지막 하나를 기억한다(조각 크기·시각이 같을 때만).
    private static readonly object CacheGate = new();
    private static string? _cacheKey;
    private static Layout? _cacheLayout;

    private static Layout LayoutOf(IReadOnlyList<string> paths, SplitVolumeStream parts)
    {
        string key = string.Join("|", paths.Select(p =>
        {
            var fi = new FileInfo(p);
            return $"{fi.FullName}:{fi.Length}:{fi.LastWriteTimeUtc.Ticks}";
        }));
        lock (CacheGate)
        {
            if (key == _cacheKey && _cacheLayout is not null) return _cacheLayout;
        }
        Layout layout;
        try
        {
            layout = Build(parts);
        }
        catch (Exception ex) when (ex is InvalidDataException or EndOfStreamException or ArgumentOutOfRangeException)
        {
            throw new CorruptArchiveException(ex);
        }
        lock (CacheGate)
        {
            _cacheKey = key;
            _cacheLayout = layout;
        }
        return layout;
    }

    private static Layout Build(SplitVolumeStream parts)
    {
        long Abs(long disk, long offset) =>
            disk >= 0 && disk < parts.PartCount && offset >= 0
                ? parts.PartStart((int)disk) + offset
                : throw new InvalidDataException("ZIP 조각 번호가 맞지 않음");

        var end = ZipDirectory.FindEnd(parts) ?? throw new InvalidDataException("ZIP 끝 머리를 찾지 못함");
        if (end.HasZip64Locator) ZipDirectory.ApplyZip64(parts, end, Abs(end.Zip64Disk, end.Zip64Offset));
        long cdAbs = Abs(end.CdDisk, end.CdOffset);
        if (end.CdSize < 0 || end.CdSize > MaxDirectoryBytes || cdAbs + end.CdSize > end.DirectoryLimit)
            throw new InvalidDataException("ZIP 중앙 디렉터리 위치가 맞지 않음");
        var records = ZipDirectory.ParseCentral(ZipDirectory.ReadAt(parts, cdAbs, (int)end.CdSize), end.TotalEntries);

        uint first = parts.Length >= 4 ? ZipDirectory.U32(ZipDirectory.ReadAt(parts, 0, 4), 0) : 0;
        long skip = first is ZipDirectory.SpanSig or ZipDirectory.TempSpanSig ? 4 : 0;
        long dataLength = cdAbs - skip;
        if (dataLength < 0) throw new InvalidDataException("ZIP 중앙 디렉터리 위치가 맞지 않음");

        using var dir = new MemoryStream();
        foreach (var r in records)
        {
            long at = Abs(r.DiskStart, r.LocalOffset) - skip;
            if (at < 0 || at >= dataLength) throw new InvalidDataException("ZIP 로컬 머리 위치가 맞지 않음");
            dir.Write(r.Rewrite(0, at));
        }
        long cdSize = dir.Length;
        bool zip64 = records.Count >= ushort.MaxValue || cdSize >= uint.MaxValue || dataLength >= uint.MaxValue;
        dir.Write(ZipDirectory.BuildEnd(zip64, 0, 0, records.Count, records.Count, cdSize, dataLength, end.Comment,
            zip64EndOffset: dataLength + cdSize, totalDisks: 1));
        return new Layout(skip, dataLength, dir.ToArray());
    }

    public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));

    public override int Read(Span<byte> buffer)
    {
        if (buffer.Length == 0 || _position >= Length) return 0;
        int n;
        if (_position < _dataLength)
        {
            int want = (int)Math.Min(buffer.Length, _dataLength - _position);
            _parts.Position = _skip + _position;
            n = _parts.Read(buffer[..want]);
            if (n <= 0) return 0;
        }
        else
        {
            int at = (int)(_position - _dataLength);
            n = Math.Min(buffer.Length, _directory.Length - at);
            _directory.AsSpan(at, n).CopyTo(buffer);
        }
        _position += n;
        return n;
    }

    public override bool CanRead => true;
    public override bool CanSeek => true;
    public override bool CanWrite => false;
    public override long Length => _dataLength + _directory.Length;

    public override long Position
    {
        get => _position;
        set => _position = value >= 0 ? value : throw new IOException("음수 위치로 옮길 수 없어요.");
    }

    public override long Seek(long offset, SeekOrigin origin)
    {
        Position = origin switch
        {
            SeekOrigin.Begin => offset,
            SeekOrigin.Current => _position + offset,
            SeekOrigin.End => Length + offset,
            _ => throw new ArgumentOutOfRangeException(nameof(origin)),
        };
        return _position;
    }

    public override void Flush() { }
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    protected override void Dispose(bool disposing)
    {
        if (disposing) _parts.Dispose();
        base.Dispose(disposing);
    }
}
