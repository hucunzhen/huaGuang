namespace HuaGuang.Monitor.Services.Watchdog;

/// <summary>进程内心跳（UI 等非 Generic Host 场景）。</summary>
public static class WatchdogHeartbeat
{
    static Timer? _timer;
    static string? _role;
    static readonly Lock Gate = new();

    public static void Start(string role, TimeSpan? interval = null)
    {
        lock (Gate)
        {
            _role = role;
            _timer?.Dispose();
            var period = interval ?? TimeSpan.FromSeconds(15);
            WatchdogStateStore.WriteHeartbeat(role);
            _timer = new Timer(_ => WatchdogStateStore.WriteHeartbeat(role), null, period, period);
        }
    }

    public static void StopGraceful(string role)
    {
        lock (Gate)
        {
            _timer?.Dispose();
            _timer = null;
            if (_role == role)
            {
                _role = null;
            }

            WatchdogStateStore.MarkGracefulShutdown(role);
        }
    }
}
