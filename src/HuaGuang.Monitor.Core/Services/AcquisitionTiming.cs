using HuaGuang.Monitor.Models;

namespace HuaGuang.Monitor.Services;

public static class AcquisitionTiming
{
    public const int MinIntervalMs = 200;
    public const int MaxScanIntervalMs = 60_000;
    public const int MaxPublishIntervalMs = 3_600_000;

    public static int ResolveScanIntervalMs(AppSettings settings) =>
        Math.Clamp(settings.ScanIntervalMs, MinIntervalMs, MaxScanIntervalMs);

    public static int ResolvePublishIntervalMs(AppSettings settings) =>
        Math.Clamp(settings.PublishIntervalMs, MinIntervalMs, MaxPublishIntervalMs);
}
