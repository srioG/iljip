using System.IO;

namespace Iljip.Services;

/// <summary>
/// 파일이 바뀌었다는 알림(W2-06 — I3-3 압축 속 파일을 고쳐 저장하면 '다시 넣을까요?'). 실제는 <see cref="FileChangeSource"/>(FileSystemWatcher + 2초 확인),
/// 시험은 가짜(Iljip.Tests/TestSupport/FakeFileChangeSource — 시험이 직접 '바뀜'을 알림). 알림은 아무 스레드에서나 올 수 있고, 한 번 저장에 여러 번 올 수 있다
/// (받는 쪽 <see cref="Editing.OpenedEntryWatcher"/> 가 1초 조용해질 때까지 기다린 뒤 내용(SHA-256)으로 정말 바뀌었는지 본다).
/// </summary>
public interface IFileChangeSource
{
    /// <summary><paramref name="filePath"/> 가 바뀔 때마다 <paramref name="onChanged"/> 를 부른다. 돌려준 것을 Dispose 하면 그만 본다.</summary>
    IDisposable Watch(string filePath, Action onChanged);
}

/// <summary>
/// 실제 파일 감시: 그 파일이 든 폴더의 FileSystemWatcher(쓰기·크기·이름 바꿔 저장 — 오피스는 임시 파일에 쓴 뒤 이름을 바꿔 저장한다)에
/// 2초마다 크기·수정 시각 확인을 더한다(네트워크 드라이브·보안 프로그램 때문에 FileSystemWatcher 알림이 빠지는 경우 대비).
/// </summary>
public sealed class FileChangeSource : IFileChangeSource
{
    /// <summary>크기·수정 시각을 확인하는 간격.</summary>
    public static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(2);

    public IDisposable Watch(string filePath, Action onChanged) => new Watcher(Path.GetFullPath(filePath), onChanged);

    private sealed class Watcher : IDisposable
    {
        private readonly string _path;
        private readonly string _name;
        private readonly Action _onChanged;
        private readonly FileSystemWatcher? _fsw;
        private readonly Timer _poll;
        private readonly object _gate = new();
        private (long Length, DateTime WriteUtc) _last;
        private bool _disposed;

        public Watcher(string path, Action onChanged)
        {
            _path = path;
            _name = Path.GetFileName(path);
            _onChanged = onChanged;
            _last = Stamp();
            try
            {
                _fsw = new FileSystemWatcher(Path.GetDirectoryName(path)!)
                {
                    IncludeSubdirectories = false,
                    NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.FileName | NotifyFilters.CreationTime,
                };
                _fsw.Changed += (_, e) => OnEvent(e.Name);
                _fsw.Created += (_, e) => OnEvent(e.Name);
                _fsw.Renamed += (_, e) => OnEvent(e.Name);
                _fsw.EnableRaisingEvents = true;
            }
            catch (Exception ex)
            {
                // 감시를 못 걸어도 2초 확인이 대신한다
                Logger.LogError("파일 감시를 걸지 못함(2초 확인으로 대신함): " + path, ex);
                _fsw?.Dispose();
                _fsw = null;
            }
            _poll = new Timer(_ => Poll(), null, PollInterval, PollInterval);
        }

        private void OnEvent(string? name)
        {
            if (!string.Equals(name, _name, StringComparison.OrdinalIgnoreCase)) return;
            lock (_gate) _last = Stamp();
            Raise();
        }

        private void Poll()
        {
            var now = Stamp();
            bool changed;
            lock (_gate)
            {
                changed = now != _last;
                _last = now;
            }
            if (changed) Raise();
        }

        private void Raise()
        {
            if (Volatile.Read(ref _disposed)) return;
            try
            {
                _onChanged();
            }
            catch (Exception ex)
            {
                Logger.LogError("파일 바뀜 알림 처리 실패: " + _path, ex);
            }
        }

        private (long, DateTime) Stamp()
        {
            try
            {
                var fi = new FileInfo(_path);
                return fi.Exists ? (fi.Length, fi.LastWriteTimeUtc) : (-1, default);
            }
            catch
            {
                return (-1, default);
            }
        }

        public void Dispose()
        {
            Volatile.Write(ref _disposed, true);
            try { _poll.Dispose(); } catch { /* 무시 */ }
            try { _fsw?.Dispose(); } catch { /* 무시 */ }
        }
    }
}
