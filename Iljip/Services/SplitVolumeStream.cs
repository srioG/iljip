using System.Buffers.Binary;
using System.IO;
using System.Text.RegularExpressions;

namespace Iljip.Services;

/// <summary>
/// 숫자 분할(이름.001, .002, …)을 하나로 이어 붙여 보여 주는 읽기 전용·위치 이동 가능 스트림(I2-2).
/// 숫자 분할은 원래 파일을 바이트 단위로 자른 것이라 그대로 이으면 원래 압축 파일이 된다(7z·zip 모두).
/// 첫 조각부터 번호가 빌 때까지 모으고, 빠진 조각은 <see cref="ThrowIfSevenZipIncomplete"/>·<see cref="ThrowIfZipIncomplete"/> 로 알아낸다.
/// 조각 파일은 모두 열어 두고(FileShare.Read) Dispose 때 닫는다.
/// </summary>
public sealed class SplitVolumeStream : Stream
{
    private static readonly Regex Numbered = new(@"\.(\d{3,})$", RegexOptions.CultureInvariant);

    private readonly List<FileStream> _parts;
    private readonly long[] _starts;
    private readonly long _length;
    private readonly string _baseName;
    private readonly int _digits;
    private long _position;

    /// <summary>모은 조각 경로(순서대로).</summary>
    public IReadOnlyList<string> Volumes { get; }

    /// <summary>이어지는 다음 조각의 파일 이름(빠진 조각을 알릴 때 씀).</summary>
    public string NextVolumeName => $"{Path.GetFileName(_baseName)}.{(Volumes.Count + 1).ToString().PadLeft(_digits, '0')}";

    /// <summary>'.001' 로 끝나는 첫 조각인가(대소문자 무관).</summary>
    public static bool IsNumberedFirstVolume(string path)
    {
        var m = Numbered.Match(Path.GetFileName(path));
        return m.Success && int.TryParse(m.Groups[1].Value, out int n) && n == 1;
    }

    private SplitVolumeStream(string baseName, int digits, List<string> volumes, List<FileStream> parts)
    {
        _baseName = baseName;
        _digits = digits;
        Volumes = volumes;
        _parts = parts;
        _starts = new long[parts.Count];
        long acc = 0;
        for (int i = 0; i < parts.Count; i++)
        {
            _starts[i] = acc;
            acc += parts[i].Length;
        }
        _length = acc;
    }

    /// <summary>첫 조각(이름.001)부터 이어지는 조각을 모두 열어 이어 붙인다.</summary>
    public static SplitVolumeStream Open(string firstVolumePath)
    {
        var m = Numbered.Match(firstVolumePath);
        if (!m.Success) throw new ArgumentException("숫자 분할의 첫 조각(.001)이 아니에요.", nameof(firstVolumePath));
        int digits = m.Groups[1].Value.Length;
        string baseName = firstVolumePath[..m.Index];
        var volumes = new List<string>();
        var parts = new List<FileStream>();
        try
        {
            for (int i = 1; ; i++)
            {
                string p = $"{baseName}.{i.ToString().PadLeft(digits, '0')}";
                if (!File.Exists(p)) break;
                parts.Add(new FileStream(p, FileMode.Open, FileAccess.Read, FileShare.Read));
                volumes.Add(p);
            }
            if (parts.Count == 0) throw new FileNotFoundException("첫 조각이 없어요.", firstVolumePath);
            return new SplitVolumeStream(baseName, digits, volumes, parts);
        }
        catch
        {
            foreach (var s in parts) s.Dispose();
            throw;
        }
    }

    /// <summary>7z: 시작 머리가 가리키는 '다음 머리'의 끝(32 + 위치 + 크기)이 모은 길이를 넘으면 모자란 것(<see cref="Incomplete"/>).</summary>
    public void ThrowIfSevenZipIncomplete()
    {
        if (_length < 32) throw Incomplete();
        var start = new byte[32];
        ReadExactAt(0, start);
        ulong end = 32 + BinaryPrimitives.ReadUInt64LittleEndian(start.AsSpan(12, 8)) + BinaryPrimitives.ReadUInt64LittleEndian(start.AsSpan(20, 8));
        if (end > (ulong)_length) throw Incomplete();
    }

    /// <summary>zip: 끝부분(64KB+22)에 ZIP 끝 머리(EOCD)가 없으면 모자란 것(<see cref="Incomplete"/>).</summary>
    public void ThrowIfZipIncomplete()
    {
        int tailLen = (int)Math.Min(_length, 65535 + 22);
        var tail = new byte[tailLen];
        ReadExactAt(_length - tailLen, tail);
        for (int i = tail.Length - 22; i >= 0; i--)
            if (tail[i] == 0x50 && tail[i + 1] == 0x4B && tail[i + 2] == 0x05 && tail[i + 3] == 0x06) return;
        throw Incomplete();
    }

    /// <summary>
    /// 이어 붙인 길이가 모자랄 때 무엇이 문제인지: 숫자 분할은 마지막 조각만 앞 조각보다 작다 → 조각이 둘 이상이고 마지막 조각이
    /// 첫 조각보다 작으면 그 조각이 마지막인데 덜 받아진 것(<see cref="VolumeIncompleteException"/>), 아니면 다음 번호 조각이 없는 것
    /// (<see cref="VolumeMissingException"/>). 조각이 하나뿐이면 가를 수 없어 '다음 조각 없음'으로 본다.
    /// </summary>
    private ArchiveException Incomplete() =>
        _parts.Count >= 2 && _parts[^1].Length < _parts[0].Length
            ? new VolumeIncompleteException(Path.GetFileName(Volumes[^1]))
            : new VolumeMissingException(NextVolumeName);

    private void ReadExactAt(long pos, byte[] buffer)
    {
        long saved = _position;
        _position = pos;
        int got = 0;
        while (got < buffer.Length)
        {
            int n = Read(buffer, got, buffer.Length - got);
            if (n <= 0) break;
            got += n;
        }
        _position = saved;
    }

    public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));

    public override int Read(Span<byte> buffer)
    {
        if (buffer.Length == 0 || _position >= _length) return 0;
        int idx = Array.BinarySearch(_starts, _position);
        if (idx < 0) idx = ~idx - 1;
        // 길이 0 조각은 건너뛴다
        while (idx < _parts.Count - 1 && _position >= _starts[idx] + _parts[idx].Length) idx++;
        var part = _parts[idx];
        long inPart = _position - _starts[idx];
        part.Position = inPart;
        int want = (int)Math.Min(buffer.Length, part.Length - inPart);
        int n = part.Read(buffer[..want]);
        _position += n;
        return n;
    }

    public override bool CanRead => true;
    public override bool CanSeek => true;
    public override bool CanWrite => false;
    public override long Length => _length;

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
            SeekOrigin.End => _length + offset,
            _ => throw new ArgumentOutOfRangeException(nameof(origin)),
        };
        return _position;
    }

    public override void Flush() { }
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    protected override void Dispose(bool disposing)
    {
        if (disposing)
            foreach (var p in _parts)
            {
                try { p.Dispose(); } catch { /* 닫기 실패는 무시 */ }
            }
        base.Dispose(disposing);
    }
}
