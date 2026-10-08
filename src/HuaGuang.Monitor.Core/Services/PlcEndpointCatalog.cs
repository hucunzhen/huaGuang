using HuaGuang.Monitor.Models;

namespace HuaGuang.Monitor.Services;

public static class PlcEndpointCatalog
{
    public static void PrepareForExport(AppSettings settings)
    {
        Normalize(settings);
    }

    public static void Normalize(AppSettings settings)
    {
        if (settings.PlcEndpoints.Count == 0)
        {
            settings.PlcEndpoints.Add(PlcEndpoint.FromSettings(settings.Plc, "PLC 1"));
        }

        var seenIds = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 0; i < settings.PlcEndpoints.Count; i++)
        {
            var endpoint = settings.PlcEndpoints[i];
            if (string.IsNullOrWhiteSpace(endpoint.Id) || !seenIds.Add(endpoint.Id))
            {
                endpoint.Id = Guid.NewGuid().ToString("N");
                seenIds.Add(endpoint.Id);
            }

            if (string.IsNullOrWhiteSpace(endpoint.Name))
            {
                endpoint.Name = $"PLC {i + 1}";
            }

            var connection = endpoint.ToSettings();
            endpoint.Protocol = connection.Protocol;
            endpoint.Model = connection.Model;
            endpoint.Host = connection.Host;
            endpoint.Port = connection.Port;
            endpoint.Station = connection.Station;
            endpoint.TimeoutMs = connection.TimeoutMs;
            endpoint.Rack = connection.Rack;
            endpoint.Slot = connection.Slot;
            endpoint.CpuType = connection.CpuType;
        }

        GetPrimary(settings).ApplyTo(settings.Plc);
        foreach (var tag in settings.Tags.Where(tag => tag.IsPlc))
        {
            tag.PlcId = Find(settings, tag.PlcId).Id;
        }
    }

    public static PlcEndpoint GetPrimary(AppSettings settings) =>
        settings.PlcEndpoints.FirstOrDefault(endpoint => endpoint.Enabled)
        ?? settings.PlcEndpoints[0];

    public static IReadOnlyList<PlcEndpoint> GetEnabled(AppSettings settings) =>
        settings.PlcEndpoints.Where(endpoint => endpoint.Enabled).ToList();

    public static PlcEndpoint Resolve(AppSettings settings, string? idOrName)
    {
        if (settings.PlcEndpoints.Count == 0)
        {
            Normalize(settings);
        }

        return Find(settings, idOrName);
    }

    static PlcEndpoint Find(AppSettings settings, string? idOrName)
    {
        if (!string.IsNullOrWhiteSpace(idOrName))
        {
            var match = settings.PlcEndpoints.FirstOrDefault(endpoint =>
                string.Equals(endpoint.Id, idOrName, StringComparison.Ordinal)
                || string.Equals(endpoint.Name, idOrName, StringComparison.OrdinalIgnoreCase));
            if (match is not null)
            {
                return match;
            }
        }

        return GetPrimary(settings);
    }

    public static IReadOnlyList<PlcEndpoint> GetUsedByTags(AppSettings settings, IEnumerable<PlcTag> tags)
    {
        Normalize(settings);
        var ids = new HashSet<string>(StringComparer.Ordinal);
        var used = new List<PlcEndpoint>();
        foreach (var tag in tags.Where(tag => tag.Enabled && tag.IsPlc))
        {
            var endpoint = Resolve(settings, tag.PlcId);
            if (!endpoint.Enabled || !ids.Add(endpoint.Id))
            {
                continue;
            }

            used.Add(endpoint);
        }

        return used;
    }

    public static IReadOnlyList<string> NormalizeAndNames(AppSettings settings)
    {
        Normalize(settings);
        return settings.PlcEndpoints.Select(endpoint => endpoint.Name).ToList();
    }

    public static string DescribeHosts(AppSettings settings)
    {
        Normalize(settings);
        var hosts = GetEnabled(settings)
            .Select(endpoint => endpoint.Host.Trim())
            .Where(host => !string.IsNullOrWhiteSpace(host))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        return hosts.Count == 0 ? settings.Plc.Host : string.Join(",", hosts);
    }
}
