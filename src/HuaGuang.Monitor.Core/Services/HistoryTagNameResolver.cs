using HuaGuang.Monitor.Models;

namespace HuaGuang.Monitor.Services;

/// <summary>订阅历史：MQTT 字段简写与点表中文名称对齐。</summary>
public static class HistoryTagNameResolver
{
    public static string ResolveDisplayName(
        string storedName,
        IReadOnlyList<PlcTag>? catalogTags,
        MqttPayloadProfile? profile)
    {
        if (string.IsNullOrWhiteSpace(storedName) || catalogTags is null || catalogTags.Count == 0)
        {
            return storedName;
        }

        profile ??= new MqttPayloadProfile();
        return TagDisplayOrder.TryResolveCatalogTag(storedName, catalogTags, profile, out var matched)
            ? matched.Name
            : storedName;
    }

    /// <summary>订阅历史：保存报文中出现的全部字段，仅做名称映射，不按点表启用状态过滤。</summary>
    public static IReadOnlyList<TagSnapshot> CreateSubscribeSnapshots(
        IReadOnlyDictionary<string, object?> remoteTags,
        IReadOnlyList<PlcTag> catalogTags,
        MqttPayloadProfile profile,
        string quality,
        DateTimeOffset timestamp)
    {
        if (remoteTags.Count == 0)
        {
            return [];
        }

        var snapshots = new List<TagSnapshot>(remoteTags.Count);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var pair in remoteTags.OrderBy(entry => entry.Key, StringComparer.Ordinal))
        {
            var displayName = ResolveDisplayName(pair.Key, catalogTags, profile);
            if (!seen.Add(displayName))
            {
                continue;
            }

            TagDisplayOrder.TryResolveCatalogTag(pair.Key, catalogTags, profile, out var catalogTag);
            snapshots.Add(new TagSnapshot
            {
                TagId = catalogTag?.Id ?? pair.Key,
                Name = displayName,
                Unit = catalogTag?.Unit ?? string.Empty,
                Value = pair.Value,
                Quality = quality,
                Timestamp = timestamp
            });
        }

        return snapshots;
    }
}
