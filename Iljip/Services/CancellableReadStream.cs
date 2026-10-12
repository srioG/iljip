using System.IO;

namespace Iljip.Services;

/// <summary>
/// 읽기 전용 스트림 래퍼: 읽을 때마다 먼저 취소를 확인하고, 지금까지 읽은 바이트 수를 콜백으로 알린다.
/// 라이브러리(TarWriter 등)가 원본 파일을 한 번에 통째로 복사하는 동안에도
/// 취소가 바로 먹히고(B6) 파일 안에서의 진행률이 오르게 하려는 것이다.
/// 원본 경로를 주면 읽기 실패(다른 프로그램이 파일의 한 부분을 잠금 등)를 <see cref="SourceUnreadableException"/> 으로 바꾼다(I0-2 —
/// 라이브러리 안에서 난 읽기 오류와 압축 파일 쓰기 오류를 밖에서 가릴 수 없어서 여기서 표시한다).
/// 안쪽 스트림은 이 래퍼가 소유한다(Dispose 때 같이 닫음).
/// </summary>
public sealed class CancellableReadStream : Stream
{
    private readonly Stream _inner;
    private readonly CancellationToken _token;
    private readonly Action<long>? _onRead;
    private readonly string? _sourcePath;

    /// <summary>지금까지 읽은 바이트 수.</summary>
    public long BytesRead { get; private set; }

    /// <param name="onRead">읽을 때마다 '누적 읽은 바이트 수'로 불린다(읽은 게 0이면 안 불림).</param>
    /// <param name="sourcePath">압축할 원본 경로 — 주면 읽기 실패를 <see cref="SourceUnreadableException"/> 으로 바꾼다.</param>
    public CancellableReadStream(Stream inner, CancellationToken token, Action<long>? onRead = null, string? sourcePath = null)
    {
        _inner = inner;
        _token = token;
        _onRead = onRead;
        _sourcePath = sourcePath;
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

    /// <summary>안쪽 읽기 실패를 원본 읽기 실패로 바꿀지(원본 경로를 받았을 때만).</summary>
    private bool IsSourceFailure(Exception ex) => _sourcePath is not null && SourceFileOpener.IsReadFailure(ex);

    public override int Read(byte[] buffer, int offset, int count)
    {
        _token.ThrowIfCancellationRequested();
        int read;
        try { read = _inner.Read(buffer, offset, count); }
        catch (Exception ex) when (IsSourceFailure(ex)) { throw SourceFileOpener.ReadFailed(_sourcePath!, ex); }
        return Count(read);
    }

    public override int Read(Span<byte> buffer)
    {
        _token.ThrowIfCancellationRequested();
        int read;
        try { read = _inner.Read(buffer); }
        catch (Exception ex) when (IsSourceFailure(ex)) { throw SourceFileOpener.ReadFailed(_sourcePath!, ex); }
        return Count(read);
    }

    public override int ReadByte()
    {
        _token.ThrowIfCancellationRequested();
        int b;
        try { b = _inner.ReadByte(); }
        catch (Exception ex) when (IsSourceFailure(ex)) { throw SourceFileOpener.ReadFailed(_sourcePath!, ex); }
        if (b >= 0) Count(1);
        return b;
    }

    public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
    {
        _token.ThrowIfCancellationRequested();
        int read;
        try { read = await _inner.ReadAsync(buffer.AsMemory(offset, count), cancellationToken).ConfigureAwait(false); }
        catch (Exception ex) when (IsSourceFailure(ex)) { throw SourceFileOpener.ReadFailed(_sourcePath!, ex); }
        return Count(read);
    }

    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        _token.ThrowIfCancellationRequested();
        int read;
        try { read = await _inner.ReadAsync(buffer, cancellationToken).ConfigureAwait(false); }
        catch (Exception ex) when (IsSourceFailure(ex)) { throw SourceFileOpener.ReadFailed(_sourcePath!, ex); }
        return Count(read);
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
