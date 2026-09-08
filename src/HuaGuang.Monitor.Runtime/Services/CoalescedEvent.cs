namespace HuaGuang.Monitor.Services;

/// <summary>合并短时间内的重复通知，降低 UI/IPC 刷新频率。</summary>
public sealed class CoalescedEvent : IDisposable
{
    readonly Lock _gate = new();
    readonly TimeSpan _minInterval;
    readonly Action _raise;
    Timer? _timer;
    bool _pending;
    bool _disposed;

    public CoalescedEvent(TimeSpan minInterval, Action raise)
    {
        _minInterval = minInterval;
        _raise = raise;
    }

    public void Request()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            if (_timer is not null)
            {
                _pending = true;
                return;
            }

            _timer = new Timer(static state => ((CoalescedEvent)state!).Flush(), this, _minInterval, Timeout.InfiniteTimeSpan);
        }
    }

    public void Flush()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _timer?.Dispose();
            _timer = null;
            _pending = false;
        }

        _raise();

        lock (_gate)
        {
            if (_disposed || !_pending)
            {
                return;
            }

            _pending = false;
            _timer = new Timer(static state => ((CoalescedEvent)state!).Flush(), this, _minInterval, Timeout.InfiniteTimeSpan);
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            _disposed = true;
            _timer?.Dispose();
            _timer = null;
            _pending = false;
        }
    }
}
