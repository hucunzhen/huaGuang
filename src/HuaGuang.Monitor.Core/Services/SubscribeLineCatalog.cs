using HuaGuang.Monitor.Models;

namespace HuaGuang.Monitor.Services;

/// <summary>
/// 订阅模式按报文里的设备/主题解析产线点表，不用订阅窗口当前选中的先河（等）点表硬套所有设备。
/// </summary>
public static class SubscribeLineCatalog
{
    /// <summary>由运行时注入：按产线名加载 Excel 点表。</summary>
    public static Func<string, IReadOnlyList<PlcTag>?>? LoadTags { get; set; }

    public static bool TryResolveLineName(string? deviceId, string? sourceTopic, out string lineName)
    {
        if (LineCatalog.TryResolveLineName(deviceId, out lineName))
        {
            return true;
        }

        var fromTopic = LineMqttDefaults.ResolveDeviceIdFromTopic(sourceTopic ?? string.Empty);
        return LineCatalog.TryResolveLineName(fromTopic, out lineName);
    }

    public static IReadOnlyList<PlcTag> ResolveTags(
        string? deviceId,
        string? sourceTopic,
        IReadOnlyList<PlcTag> fallbackTags)
    {
        if (!TryResolveLineName(deviceId, sourceTopic, out var lineName))
        {
            return fallbackTags;
        }

        var loaded = LoadTags?.Invoke(lineName);
        if (loaded is { Count: > 0 })
        {
            return loaded;
        }

        var seed = LineExcelConfigService.CreateSeedSettings(lineName).Tags;
        return seed.Count > 0 ? seed : fallbackTags;
    }
}
