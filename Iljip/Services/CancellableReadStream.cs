using System.IO;

namespace Iljip.Services;

/// <summary>
/// 읽기 전용 스트림 래퍼: 읽을 때마다 먼저 취소를 확인하고, 지금까지 읽은 바이트 수를 콜백으로 알린다.
/// 라이브러리(TarWriter 등)가 원본 파일을 한 번에 통째로 복사하는 동안에도
/// 취소가 바로 먹히고(B6) 파일 안에서의 진행률이 오르게 하려는 것이다.
/// 안쪽 스트림은 이 래퍼가 소유한다(Dispose 때 같이 닫음).
/// </summary>
public sealed class CancellableReadStream : Stream
{
    private readonly Stream _inner;
    private readonly CancellationToken _token;
    private readonly Action<long>? _onRead;

    /// <summary>지금까지 읽은 바이트 수.</summary>
    public long BytesRead { get; private set; }

    /// <param name="onRead">읽을 때마다 '누적 읽은 바이트 수'로 불린다(읽은 게 0이면 안 불림).</param>
    public CancellableReadStream(Stream inner, CancellationToken token, Action<long>? onRead = null)
    {
        _inner = inner;
        _token = token;
        _onRead = onRead;
    }

    private int Count(int read)
    {
        if (read > 0)
        {
            BytesRead += read;
            _onRead?.Invoke(BytesRead);
        }
        return read;
    }

    public override int Read(byte[] buffer, int offset, int count)
    {
        _token.ThrowIfCancellationRequested();
        return Count(_inner.Read(buffer, offset, count));
    }

    public override int Read(Span<byte> buffer)
    {
        _token.ThrowIfCancellationRequested();
        return Count(_inner.Read(buffer));
    }

    public override int ReadByte()
    {
        _token.ThrowIfCancellationRequested();
        int b = _inner.ReadByte();
        if (b >= 0) Count(1);
        return b;
    }

    public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
    {
        _token.ThrowIfCancellationRequested();
        return Count(await _inner.ReadAsync(buffer.AsMemory(offset, count), cancellationToken).ConfigureAwait(false));
    }

    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        _token.ThrowIfCancellationRequested();
        return Count(await _inner.ReadAsync(buffer, cancellationToken).ConfigureAwait(false));
    }

    public override bool CanRead => true;
    public override bool CanSeek => false;   // 앞으로만 읽는다(읽은 양 = 진행률이 되게)
    public override bool CanWrite => false;
    public override long Length => _inner.Length;

    public override long Position
    {
        get => _inner.Position;
        set => throw new NotSupportedException();
    }

    public override void Flush() { }
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    protected override void Dispose(bool disposing)
    {
        if (disposing) _inner.Dispose();
        base.Dispose(disposing);
    }
}
