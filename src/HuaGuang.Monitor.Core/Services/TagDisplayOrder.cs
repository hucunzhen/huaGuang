using HuaGuang.Monitor.Models;

namespace HuaGuang.Monitor.Services;

public static class TagDisplayOrder
{
    public static IEnumerable<(string Name, object? Value, PlcTag? CatalogTag)> OrderRemoteTags(
        IReadOnlyDictionary<string, object?> remoteTags,
        IReadOnlyList<PlcTag> catalogTags,
        MqttPayloadProfile? profile = null)
    {
        profile ??= new MqttPayloadProfile();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var catalogTag in catalogTags.Where(tag => tag.Enabled))
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

                if (!catalogTag.Enabled)
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
        }

        catalogTag = null!;
        return false;
    }

    static bool TryGetRemoteValue(
        IReadOnlyDictionary<string, object?> remoteTags,
        PlcTag catalogTag,
        MqttPayloadProfile profile,
        out object? value)
    {
        var mqttField = MqttPayloadMapper.ResolveFieldKey(catalogTag, profile);
        if (remoteTags.TryGetValue(mqttField, out value))
        {
            return true;
        }

        return remoteTags.TryGetValue(catalogTag.Name, out value);
    }
}
