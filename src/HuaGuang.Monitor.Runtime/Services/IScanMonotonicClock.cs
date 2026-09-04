namespace HuaGuang.Monitor.Services;

/// <summary>
/// 单调时钟，用于扫描周期补偿（Android 上比 Stopwatch 更可靠）。
/// </summary>
public interface IScanMonotonicClock
{
    double ElapsedMs { get; }
}

public static class ScanMonotonicClock
{
    static Func<IScanMonotonicClock>? _factory;

    public static void ConfigureFactory(Func<IScanMonotonicClock> factory) =>
        _factory = factory ?? throw new ArgumentNullException(nameof(factory));

    public static IScanMonotonicClock Create() => _factory?.Invoke() ?? new StopwatchScanMonotonicClock();
}

internal sealed class StopwatchScanMonotonicClock : IScanMonotonicClock
{
    readonly System.Diagnostics.Stopwatch _stopwatch = System.Diagnostics.Stopwatch.StartNew();

    public double ElapsedMs => _stopwatch.Elapsed.TotalMilliseconds;
}
