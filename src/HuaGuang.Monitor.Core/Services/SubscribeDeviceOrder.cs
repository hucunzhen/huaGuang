namespace HuaGuang.Monitor.Services;

/// <summary>
/// 订阅汇总设备排序：火焰复合 → 其他复合 → 烘焙机 → 定型机 → 起毛机 → 其余。
/// </summary>
public static class SubscribeDeviceOrder
{
    public const int FlameCompound = 0;
    /// <summary>其他复合，以及未识别机台一并归入本档。</summary>
    public const int OtherCompound = 1;
    public const int Hongbei = 2;
    public const int Dingxing = 3;
    public const int Qimao = 4;

    public static int GetGroupRank(string? deviceId, string? sourceTopic = null)
    {
        var label = ResolveLabel(deviceId, sourceTopic);
        if (string.IsNullOrWhiteSpace(label))
        {
            return OtherCompound;
        }

        if (IsFlameCompound(label))
        {
            return FlameCompound;
        }

        if (IsHongbei(label))
        {
            return Hongbei;
        }

        if (IsDingxing(label))
        {
            return Dingxing;
        }

        if (IsQimao(label))
        {
            return Qimao;
        }

        // 其余复合机、未登记设备都跟「其他复合」一起排在火焰复合之后。
        return OtherCompound;
    }

    public static IEnumerable<T> Order<T>(
        IEnumerable<T> items,
        Func<T, string?> deviceIdSelector,
        Func<T, string?>? sourceTopicSelector = null)
    {
        return items
            .OrderBy(item => GetGroupRank(
                deviceIdSelector(item),
                sourceTopicSelector?.Invoke(item)))
            .ThenBy(item => deviceIdSelector(item) ?? string.Empty, StringComparer.Ordinal);
    }

    static string ResolveLabel(string? deviceId, string? sourceTopic)
    {
        if (SubscribeLineCatalog.TryResolveLineName(deviceId, sourceTopic, out var lineName))
        {
            return lineName;
        }

        if (!string.IsNullOrWhiteSpace(deviceId))
        {
            return deviceId.Trim();
        }

        return sourceTopic?.Trim() ?? string.Empty;
    }

    static bool IsFlameCompound(string label) =>
        label.Contains("火焰", StringComparison.Ordinal)
        || label is LineCatalog.ZhidahyLineName
            or LineCatalog.ShanghaihyLineName
            or LineCatalog.MeshyLineName
            or "C型火焰复合机";

    static bool IsHongbei(string label) =>
        string.Equals(label, LineCatalog.HongbeiLineName, StringComparison.Ordinal)
        || label.Contains("烘焙", StringComparison.Ordinal);

    static bool IsDingxing(string label) =>
        string.Equals(label, LineCatalog.Dingxing10LineName, StringComparison.Ordinal)
        || string.Equals(label, LineCatalog.Dingxing11LineName, StringComparison.Ordinal)
        || LineCatalog.IsRuianLine(label)
        || (label.Contains("定型", StringComparison.Ordinal) && !IsHongbei(label));

    static bool IsQimao(string label) =>
        LineCatalog.IsQimaoLine(label)
        || label.Contains("起毛", StringComparison.Ordinal);
}
