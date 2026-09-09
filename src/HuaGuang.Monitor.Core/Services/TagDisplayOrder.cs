using HuaGuang.Monitor.Models;

namespace HuaGuang.Monitor.Services;

public static class TagDisplayOrder
{
    public static IEnumerable<(string Name, object? Value, PlcTag? CatalogTag)> OrderRemoteTags(
        IReadOnlyDictionary<string, object?> remoteTags,
        IReadOnlyList<PlcTag> catalogTags,
        MqttPayloadProfile? profile = null,
        bool includeDisabledCatalogTags = false)
    {
        profile ??= new MqttPayloadProfile();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var catalogTag in catalogTags.Where(tag => includeDisabledCatalogTags || tag.Enabled))
        {
            if (!TryGetRemoteValue(remoteTags, catalogTag, profile, out var value))
            {
                continue;
            }

            seen.Add(catalogTag.Name);
            yield return (catalogTag.Name, value, catalogTag);
        }

        foreach (var pair in remoteTags.OrderBy(entry => entry.Key, StringComparer.Ordinal))
        {
            if (TryResolveCatalogTag(pair.Key, catalogTags, profile, out var catalogTag))
            {
                if (seen.Contains(catalogTag.Name))
                {
                    continue;
                }

                if (!includeDisabledCatalogTags && !catalogTag.Enabled)
                {
                    continue;
                }

                seen.Add(catalogTag.Name);
                yield return (catalogTag.Name, pair.Value, catalogTag);
                continue;
            }

            if (seen.Contains(pair.Key))
            {
                continue;
            }

            yield return (pair.Key, pair.Value, null);
        }
    }

    public static bool TryResolveCatalogTag(
        string remoteKey,
        IReadOnlyList<PlcTag> catalogTags,
        MqttPayloadProfile profile,
        out PlcTag catalogTag)
    {
        var matched = MqttPayloadMapper.MatchCatalogTag(remoteKey, catalogTags, profile);
        if (matched is not null)
        {
            catalogTag = matched;
            return true;
        }

        if (MqttFieldMappingCatalog.TryResolveTagNameByField(remoteKey, out var tagName))
        {
            matched = catalogTags.FirstOrDefault(tag =>
                tag.Name.Equals(tagName, StringComparison.Ordinal));
            if (matched is not null)
            {
                catalogTag = matched;
                return true;
            }

            catalogTag = new PlcTag
            {
                Name = tagName,
                MqttField = remoteKey.Trim()
            };
            return true;
        }

        catalogTag = null!;
        return false;
    }

    static bool TryGetRemoteValueByKey(
        IReadOnlyDictionary<string, object?> remoteTags,
        string key,
        out object? value)
    {
        if (remoteTags.TryGetValue(key, out value))
        {
            return true;
        }

        foreach (var pair in remoteTags)
        {
            if (string.Equals(pair.Key, key, StringComparison.OrdinalIgnoreCase))
            {
                value = pair.Value;
                return true;
            }
        }

        value = null;
        return false;
    }

    static bool TryGetRemoteValue(
        IReadOnlyDictionary<string, object?> remoteTags,
        PlcTag catalogTag,
        MqttPayloadProfile profile,
        out object? value)
    {
        var mqttField = MqttPayloadMapper.ResolveFieldKey(catalogTag, profile);
        if (TryGetRemoteValueByKey(remoteTags, mqttField, out value))
        {
            return true;
        }

        return TryGetRemoteValueByKey(remoteTags, catalogTag.Name, out value);
    }
}
