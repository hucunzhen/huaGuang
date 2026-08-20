using HuaGuang.Monitor.Models;

namespace HuaGuang.Monitor.Services;

public static class TagDisplayOrder
{
    public static IEnumerable<(string Name, object? Value, PlcTag? CatalogTag)> OrderRemoteTags(
        IReadOnlyDictionary<string, object?> remoteTags,
        IReadOnlyList<PlcTag> catalogTags)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var catalogTag in catalogTags.Where(tag => tag.Enabled))
        {
            if (!remoteTags.TryGetValue(catalogTag.Name, out var value))
            {
                continue;
            }

            seen.Add(catalogTag.Name);
            yield return (catalogTag.Name, value, catalogTag);
        }

        foreach (var pair in remoteTags.OrderBy(entry => entry.Key, StringComparer.Ordinal))
        {
            if (seen.Contains(pair.Key))
            {
                continue;
            }

            yield return (pair.Key, pair.Value, null);
        }
    }
}
