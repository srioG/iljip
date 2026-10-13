namespace Iljip.Services.Editing;

/// <summary>
/// 압축 속에서 꺼내 연 파일들의 감시(W2-06 — I3-3). 파일 바뀜 알림(<see cref="IFileChangeSource"/>)이 오면 <see cref="QuietPeriod"/>(1초) 동안
/// 더 오지 않을 때까지 기다렸다가(저장 한 번에 알림이 여러 번 옴) 뒤 스레드에서 내용(SHA-256)을 재서 정말 바뀌었을 때만 <c>onChanged</c> 를 부른다 —
/// 시각만 바뀐 저장(내용 같음)은 묻지 않는다. 같은 파일의 알림은 겹치지 않는다(앞 처리가 끝나기 전에 온 알림은 그 뒤에 다시 본다).
/// </summary>
internal sealed class OpenedEntryWatcher : IDisposable
{
    private readonly IFileChangeSource _source;
    private readonly Func<OpenedEntry, Task> _onChanged;
    private readonly object _gate = new();
    private readonly Dictionary<OpenedEntry, State> _watched = new(ReferenceEqualityComparer.Instance);
    private readonly List<Task> _pending = new();

    /// <summary>처리마다 '조용히 기다림·내용 재기'가 끝났는가(묻기로 넘겼거나 끝남) — <see cref="WhenSettledAsync"/>.</summary>
    private readonly List<Task> _settling = new();

    private bool _disposed;

    private sealed class State
    {
        public IDisposable? Subscription;
        public CancellationTokenSource? Debounce;
        public Task Running = Task.CompletedTask;
    }

    public OpenedEntryWatcher(IFileChangeSource source, Func<OpenedEntry, Task> onChanged)
    {
        _source = source;
        _onChanged = onChanged;
    }

    /// <summary>마지막 알림 뒤 이만큼 조용하면 본다(기본 1초 — 시험은 줄인다).</summary>
    public TimeSpan QuietPeriod { get; set; } = TimeSpan.FromSeconds(1);

    public void Watch(OpenedEntry entry)
    {
        lock (_gate)
        {
            if (_disposed || _watched.ContainsKey(entry)) return;
            var state = new State();
            _watched[entry] = state;
            state.Subscription = _source.Watch(entry.TempPath, () => OnRaw(entry));
        }
    }

    public void Unwatch(OpenedEntry entry)
    {
        State? state;
        lock (_gate)
        {
            if (!_watched.Remove(entry, out state)) return;
            state.Debounce?.Cancel();
        }
        try { state.Subscription?.Dispose(); } catch { /* 무시 */ }
    }

    private void OnRaw(OpenedEntry entry)
    {
        lock (_gate)
        {
            if (_disposed || !_watched.TryGetValue(entry, out var state)) return;
            state.Debounce?.Cancel();
            var cts = new CancellationTokenSource();
            state.Debounce = cts;
            var previous = state.Running;
            var settled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var task = RunAsync(entry, previous, cts.Token, settled);
            state.Running = task;
            _pending.Add(task);
            _pending.RemoveAll(t => t.IsCompleted);
            _settling.Add(settled.Task);
            _settling.RemoveAll(t => t.IsCompleted);
        }
    }

    private async Task RunAsync(OpenedEntry entry, Task previous, CancellationToken ct, TaskCompletionSource settled)
    {
        try
        {
            await Task.Delay(QuietPeriod, ct).ConfigureAwait(false);
            // 앞 처리가 아직 묻거나 넣는 중이면 그것이 끝난 뒤에 본다 — 그동안은 '가라앉음'(묻기·넣기는 기다리지 않는다 — WhenSettledAsync)
            if (!previous.IsCompleted) settled.TrySetResult();
            try { await previous.ConfigureAwait(false); } catch { /* 앞 처리의 오류는 그쪽에서 */ }
            if (ct.IsCancellationRequested) return;
            bool modified = await Task.Run(entry.IsModified, ct).ConfigureAwait(false);
            if (!modified || ct.IsCancellationRequested) return;
            settled.TrySetResult();   // 여기서부터는 묻기·다시 넣기(명령 차례를 기다릴 수 있음)
            await _onChanged(entry).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // 조용해지기 전에 또 바뀜 — 다음 알림이 본다
        }
        catch (Exception ex)
        {
            Logger.LogError("고친 파일 확인 실패: " + entry.TempPath, ex);
        }
        finally
        {
            settled.TrySetResult();
        }
    }

    /// <summary>
    /// 지금 기다리는 '조용히 기다림·내용 재기'가 모두 끝날 때까지 — 묻기·다시 넣기로 넘어간 것은 기다리지 않는다(W2-06 검증 지적 2차).
    /// 압축을 떠나기 전(다른 압축 열기·비우기)에 쓴다: 다시 넣기는 명령 차례(_commandGate)를 기다리는데, 명령줄 열기는 차례를 쥔 채 여기서 기다리므로
    /// <see cref="WhenIdleAsync"/>(묻기·넣기까지 기다림)를 쓰면 서로를 기다려 멈춘다. 부를 때 있던 것만 기다린다(그 뒤 알림은 보지 않음).
    /// </summary>
    public Task WhenSettledAsync()
    {
        Task[] tasks;
        lock (_gate)
        {
            _settling.RemoveAll(t => t.IsCompleted);
            tasks = _settling.ToArray();
        }
        return tasks.Length == 0 ? Task.CompletedTask : Task.WhenAll(tasks);
    }

    /// <summary>기다리는 확인·처리(묻기·다시 넣기 포함)가 모두 끝날 때까지(시험용 — 명령 차례를 쥔 흐름에서 부르면 다시 넣기와 서로 기다려 멈춤).</summary>
    public async Task WhenIdleAsync()
    {
        while (true)
        {
            Task[] tasks;
            lock (_gate)
            {
                _pending.RemoveAll(t => t.IsCompleted);
                tasks = _pending.ToArray();
            }
            if (tasks.Length == 0) return;
            try { await Task.WhenAll(tasks).ConfigureAwait(false); } catch { /* 각자 로그 */ }
        }
    }

    public void Dispose()
    {
        List<State> states;
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            states = _watched.Values.ToList();
            _watched.Clear();
        }
        foreach (var s in states)
        {
            s.Debounce?.Cancel();
            try { s.Subscription?.Dispose(); } catch { /* 무시 */ }
        }
    }
}
