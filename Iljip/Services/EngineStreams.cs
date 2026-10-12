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

    /// <summary>처음 실패한 쓰기의 원래 예외(W2-02 — 7z.dll 은 이것을 HRESULT 로만 돌려주므로 '디스크 공간 모자람' 같은 원인을 여기서 되찾는다).</summary>
    public Exception? WriteError { get; private set; }

    public override void Write(byte[] buffer, int offset, int count) => Write(buffer.AsSpan(offset, count));

    public override void Write(ReadOnlySpan<byte> buffer)
    {
        _token.ThrowIfCancellationRequested();
        try
        {
            _inner.Write(buffer);
        }
        catch (Exception ex)
        {
            WriteFailed = true;
            WriteError ??= ex;
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
        catch (Exception ex)
        {
            WriteFailed = true;
            WriteError ??= ex;
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

/// <summary>
/// 7z 만들기(W2-02)의 원본 파일 하나를 7z.dll 에 넘기는 읽기 스트림(<c>CompressStreamDictionary</c> 의 값).
/// <list type="bullet">
/// <item>처음 읽을 때 연다(<see cref="SourceFileOpener.TryOpenRead"/> — FileShare.ReadWrite|Delete, 잠김이면 다시 시도) — 7z.dll 은 원본마다 스트림을
///   미리 받지만 실제로 읽는 것은 차례대로라, 처음부터 다 열면 파일 수만큼 핸들이 열린다. 끝까지 읽으면 바로 닫는다(한 번에 열린 원본은 1~4개).</item>
/// <item>읽을 때마다 취소를 확인하고(큰 파일 한가운데서도 멈춤 — 7z.dll 이 HRESULT 로 받아 1초 남짓에 멈춤, 실측) 읽은 바이트를 진행률에 알린다
///   (읽은 바이트 % 가 7z.dll 의 Compressing.PercentDone 과 1:1 로 맞음, 실측).</item>
/// <item>길이는 원본을 모을 때 잰 값으로 고정한다(7z.dll 이 그 크기를 미리 적음). 읽는 사이에 길이가 달라졌거나 열기·읽기가 막히면
///   <see cref="SourceUnreadableException"/>(쉬운 이유)을 알리고(onFailure) 던진다 — 7z.dll 은 콜백 예외를 HRESULT(0x8013153B)로만 돌려주므로
///   서비스가 이 기록으로 원인을 가른다(그 파일을 빼고 처음부터 다시 씀).</item>
/// </list>
/// 7z.dll(SharpSevenZip)은 이 스트림을 닫지 않는다 — 서비스가 끝에 모두 Dispose 한다.
/// </summary>
internal sealed class LazySourceStream : Stream
{
    private readonly string _path;
    private readonly long _length;
    private readonly CancellationToken _token;
    private readonly Action<int>? _onBytes;
    private readonly Action<SourceUnreadableException>? _onFailure;
    private readonly Action? _onFinished;
    private readonly object _gate = new();
    private Stream? _file;
    private long _position;
    private bool _finished;
    private bool _disposed;

    /// <param name="path">원본 파일.</param>
    /// <param name="length">원본을 모을 때 잰 길이(고정).</param>
    /// <param name="token">취소.</param>
    /// <param name="onBytes">읽은 바이트 수(진행률).</param>
    /// <param name="onFailure">원본 문제(열기·읽기 막힘, 길이 바뀜)를 던지기 직전에 알림.</param>
    /// <param name="onFinished">끝까지 읽고 닫았을 때(파일 하나 끝).</param>
    public LazySourceStream(string path, long length, CancellationToken token,
        Action<int>? onBytes = null, Action<SourceUnreadableException>? onFailure = null, Action? onFinished = null)
    {
        _path = path;
        _length = length;
        _token = token;
        _onBytes = onBytes;
        _onFailure = onFailure;
        _onFinished = onFinished;
    }

    /// <summary>원본 경로.</summary>
    public string SourcePath => _path;

    /// <summary>지금 원본 파일이 열려 있는지(시험용 — 한 번에 열린 원본 수를 세는 데 씀).</summary>
    internal bool IsOpen
    {
        get { lock (_gate) return _file is not null; }
    }

    public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));

    public override int Read(Span<byte> buffer)
    {
        _token.ThrowIfCancellationRequested();
        int n;
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_finished || buffer.Length == 0) return 0;
            try
            {
                var file = _file ??= Open();
                int want = (int)Math.Min(buffer.Length, _length - _position);
                n = want > 0 ? file.Read(buffer[..want]) : 0;
                if (n == 0)
                {
                    // 끝: 줄었으면 고정 길이보다 먼저 0, 늘었으면 파일 길이가 고정 길이보다 길다 — 둘 다 '바뀜'
                    if (_position != _length || file.Length != _length)
                        throw Fail(new SourceUnreadableException(_path, SourceFileOpener.ReasonChanged));
                    CloseFile();
                    _finished = true;
                }
                else
                {
                    _position += n;
                }
            }
            catch (Exception ex) when (ex is not SourceUnreadableException && SourceFileOpener.IsReadFailure(ex))
            {
                throw Fail(SourceFileOpener.ReadFailed(_path, ex));
            }
        }
        if (n > 0) _onBytes?.Invoke(n);
        else _onFinished?.Invoke();
        return n;
    }

    private Stream Open()
    {
        if (!SourceFileOpener.TryOpenRead(_path, out var stream, out var reason))
            throw Fail(new SourceUnreadableException(_path, reason ?? SourceFileOpener.ReasonOther));
        if (_position > 0) stream!.Position = _position;
        return stream!;
    }

    private SourceUnreadableException Fail(SourceUnreadableException ex)
    {
        CloseFile();
        _onFailure?.Invoke(ex);
        return ex;
    }

    private void CloseFile()
    {
        try { _file?.Dispose(); } catch { /* 닫기 실패는 무시 */ }
        _file = null;
    }

    public override bool CanRead => true;
    public override bool CanSeek => true;
    public override bool CanWrite => false;
    public override long Length => _length;

    public override long Position
    {
        get => _position;
        set => Seek(value, SeekOrigin.Begin);
    }

    public override long Seek(long offset, SeekOrigin origin)
    {
        lock (_gate)
        {
            long target = origin switch
            {
                SeekOrigin.Begin => offset,
                SeekOrigin.Current => _position + offset,
                SeekOrigin.End => _length + offset,
                _ => throw new ArgumentOutOfRangeException(nameof(origin)),
            };
            if (target < 0) throw new IOException("음수 위치로 옮길 수 없어요.");
            if (target == _position) return _position;
            // 7z.dll 이 되감으면(드묾) 다시 읽게 한다 — 이미 닫았으면 다음 읽기에서 다시 연다
            _position = target;
            _finished = false;
            if (_file is not null) _file.Position = target;
            return _position;
        }
    }

    public override void Flush() { }
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            lock (_gate)
            {
                _disposed = true;
                CloseFile();
            }
        }
        base.Dispose(disposing);
    }
}
