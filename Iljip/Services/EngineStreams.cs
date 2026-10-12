using System.IO;

namespace Iljip.Services;

/// <summary>
/// 7z.dll 에 넘기는 입력 스트림 감싸개(위치 이동 가능): 읽을 때마다 취소를 확인하고(취소면 <see cref="OperationCanceledException"/>),
/// 읽은 뒤의 위치를 콜백으로 알린다(진행률 = 입력 위치 / 전체 길이).
/// 7z.dll 은 COM 콜백에서 난 예외를 HRESULT(0x8013153B)로 받아 20~80ms 안에 멈춘다(실측) — 그래서 큰 파일 한가운데서도 취소된다.
/// 안쪽 스트림은 이 감싸개가 소유한다.
/// </summary>
internal sealed class CancellableSeekableReadStream : Stream
{
    private readonly Stream _inner;
    private readonly CancellationToken _token;
    private readonly Action<long, long>? _onPosition;

    public CancellableSeekableReadStream(Stream inner, CancellationToken token, Action<long, long>? onPosition = null)
    {
        _inner = inner;
        _token = token;
        _onPosition = onPosition;
    }

    public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));

    public override int Read(Span<byte> buffer)
    {
        _token.ThrowIfCancellationRequested();
        int n = _inner.Read(buffer);
        _onPosition?.Invoke(_inner.Position, _inner.Length);
        return n;
    }

    public override bool CanRead => true;
    public override bool CanSeek => true;
    public override bool CanWrite => false;
    public override long Length => _inner.Length;

    public override long Position
    {
        get => _inner.Position;
        set => _inner.Position = value;
    }

    public override long Seek(long offset, SeekOrigin origin) => _inner.Seek(offset, origin);
    public override void Flush() { }
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    protected override void Dispose(bool disposing)
    {
        if (disposing) _inner.Dispose();
        base.Dispose(disposing);
    }
}

/// <summary>
/// 7z.dll 이 쓰는 출력(만들 압축 파일) 감싸개: 쓸 때마다 취소를 확인한다. 7z.dll 은 원본 파일을 스스로 열어 읽으므로
/// 읽기 쪽은 진행 알림(Compressing)에서만 끼어들 수 있고, 압축된 출력이 나올 때마다 여기서 멈춘다(던진 뒤 실측 1초 안).
/// ★ 출력은 LZMA2 블록이 끝나야 나온다 — 블록이 크면(7-Zip 기본 64~256MB) 수십 초 동안 쓰기가 없어 취소가 안 먹힌다
///   (그래서 SevenZipArchiveService.EngineParameters 가 블록을 줄인다). 안쪽 스트림은 소유하지 않는다.
/// </summary>
internal sealed class CancellableWriteStream : Stream
{
    private readonly Stream _inner;
    private readonly CancellationToken _token;

    public CancellableWriteStream(Stream inner, CancellationToken token)
    {
        _inner = inner;
        _token = token;
    }

    /// <summary>안쪽 스트림 쓰기가 실패했는지(디스크 가득 등 — 원본 쪽 문제와 가르는 데 씀).</summary>
    public bool WriteFailed { get; private set; }

    public override void Write(byte[] buffer, int offset, int count) => Write(buffer.AsSpan(offset, count));

    public override void Write(ReadOnlySpan<byte> buffer)
    {
        _token.ThrowIfCancellationRequested();
        try
        {
            _inner.Write(buffer);
        }
        catch
        {
            WriteFailed = true;
            throw;
        }
    }

    public override int Read(byte[] buffer, int offset, int count) => _inner.Read(buffer, offset, count);
    public override bool CanRead => _inner.CanRead;
    public override bool CanSeek => _inner.CanSeek;
    public override bool CanWrite => true;
    public override long Length => _inner.Length;

    public override long Position
    {
        get => _inner.Position;
        set => _inner.Position = value;
    }

    public override long Seek(long offset, SeekOrigin origin) => _inner.Seek(offset, origin);
    public override void SetLength(long value) => Guard(() => _inner.SetLength(value));
    public override void Flush() => Guard(_inner.Flush);

    private void Guard(Action action)
    {
        try
        {
            action();
        }
        catch
        {
            WriteFailed = true;
            throw;
        }
    }
}

/// <summary>
/// 큰 파일의 일부 [시작, 끝)만 처음부터인 것처럼 보여 주는 읽기 전용 스트림(7z 자동 풀림 exe 의 안쪽 7z 를 7z.dll 에 넘길 때).
/// 안쪽 스트림을 소유한다.
/// </summary>
internal sealed class OffsetStream : Stream
{
    private readonly Stream _inner;
    private readonly long _start;
    private readonly long _length;
    private long _position;

    public OffsetStream(Stream inner, long start)
    {
        _inner = inner;
        _start = start;
        _length = Math.Max(0, inner.Length - start);
    }

    public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));

    public override int Read(Span<byte> buffer)
    {
        if (_position >= _length || buffer.Length == 0) return 0;
        _inner.Position = _start + _position;
        int n = _inner.Read(buffer[..(int)Math.Min(buffer.Length, _length - _position)]);
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
        if (disposing) _inner.Dispose();
        base.Dispose(disposing);
    }
}
