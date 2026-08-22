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
            var matched = MqttPayloadMapper.MatchCatalogTag(pair.Key, catalogTags, profile);
            if (matched is not null)
            {
                if (seen.Contains(matched.Name))
                {
                    continue;
                }

                seen.Add(matched.Name);
                yield return (matched.Name, pair.Value, matched);
                continue;
            }

            if (seen.Contains(pair.Key))
            {
                continue;
            }

            yield return (pair.Key, pair.Value, null);
        }
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
