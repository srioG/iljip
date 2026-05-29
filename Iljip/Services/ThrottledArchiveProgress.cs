using System.Diagnostics;
using Iljip.Models;

namespace Iljip.Services;

/// <summary>
/// 진행률 보고를 시간 기준으로 솎아내는 IProgress 구현.
///
/// 압축/해제 서비스는 64KB 청크마다 진행률을 보고하므로, 대용량 파일에서는
/// 초당 수백~수천 건이 UI 스레드로 마샬링되어 UI가 밀린다.
/// 이 래퍼는 워커 스레드에서 먼저 시간 간격(<see cref="_intervalMs"/>)으로 걸러내고,
/// 통과한 것만 캡처된 SynchronizationContext(보통 UI)로 Post 한다.
///
/// 단, 완료 보고(CurrentFile이 비어 있음)는 항상 통과시켜 진행바가 99%에서 멈추지 않도록 한다.
/// </summary>
public sealed class ThrottledArchiveProgress : IProgress<ArchiveProgress>
{
    private readonly Action<ArchiveProgress> _handler;
    private readonly SynchronizationContext? _context;
    private readonly Stopwatch _stopwatch = Stopwatch.StartNew();
    private readonly long _intervalMs;
    private long _lastReportMs = long.MinValue;

    /// <param name="handler">실제 UI 갱신 동작.</param>
    /// <param name="intervalMs">최소 보고 간격(ms). 기본 40ms ≈ 초당 25회.</param>
    public ThrottledArchiveProgress(Action<ArchiveProgress> handler, long intervalMs = 40)
    {
        _handler = handler;
        _intervalMs = intervalMs;
        // 생성된 스레드(보통 UI)의 동기화 컨텍스트를 캡처해 그쪽으로 마샬링.
        _context = SynchronizationContext.Current;
    }

    public void Report(ArchiveProgress value)
    {
        bool isFinal = string.IsNullOrEmpty(value.CurrentFile);

        if (!isFinal)
        {
            long now = _stopwatch.ElapsedMilliseconds;
            if (now - _lastReportMs < _intervalMs)
                return;
            _lastReportMs = now;
        }

        if (_context is not null)
            _context.Post(static (state) =>
            {
                var (handler, val) = ((Action<ArchiveProgress>, ArchiveProgress))state!;
                handler(val);
            }, (_handler, value));
        else
            _handler(value);
    }
}
