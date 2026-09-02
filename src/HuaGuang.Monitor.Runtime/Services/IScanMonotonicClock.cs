namespace HuaGuang.Monitor.Services;

/// <summary>
/// 单调时钟，用于扫描周期补偿（Android 上比 Stopwatch 更可靠）。
/// </summary>
internal interface IScanMonotonicClock
{
    double ElapsedMs { get; }
}

internal static class ScanMonotonicClock
{
    public static IScanMonotonicClock Create() => new StopwatchScanMonotonicClock();
}

internal sealed class StopwatchScanMonotonicClock : IScanMonotonicClock
{
    readonly System.Diagnostics.Stopwatch _stopwatch = System.Diagnostics.Stopwatch.StartNew();

    public double ElapsedMs => _stopwatch.Elapsed.TotalMilliseconds;
}
