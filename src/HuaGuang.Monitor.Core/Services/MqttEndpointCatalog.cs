using HuaGuang.Monitor.Models;

namespace HuaGuang.Monitor.Services;

public static class MqttEndpointCatalog
{
    public static void PrepareForExport(AppSettings settings)
    {
        if (settings.MqttEndpoints.Count == 0)
        {
            settings.MqttEndpoints.Add(MqttEndpoint.FromSettings(settings.Mqtt, "默认"));
            return;
        }

        if (settings.MqttEndpoints.Count == 1)
        {
            var endpoint = settings.MqttEndpoints[0];
            var synced = MqttEndpoint.FromSettings(settings.Mqtt, endpoint.Name, endpoint.Id);
            synced.Enabled = endpoint.Enabled;
            settings.MqttEndpoints[0] = synced;
        }
    }

    public static void Normalize(AppSettings settings)
    {
        if (settings.MqttEndpoints.Count == 0)
        {
            settings.MqttEndpoints.Add(MqttEndpoint.FromSettings(settings.Mqtt, "默认"));
        }

        for (var i = 0; i < settings.MqttEndpoints.Count; i++)
        {
            var endpoint = settings.MqttEndpoints[i];
            if (string.IsNullOrWhiteSpace(endpoint.Id))
            {
                endpoint.Id = Guid.NewGuid().ToString("N");
            }

            if (string.IsNullOrWhiteSpace(endpoint.Name))
            {
                endpoint.Name = $"目标 {i + 1}";
            }
        }

        GetPrimary(settings).ApplyTo(settings.Mqtt);
    }

    public static MqttEndpoint GetPrimary(AppSettings settings) =>
        settings.MqttEndpoints.FirstOrDefault(e => e.Enabled)
        ?? settings.MqttEndpoints[0];

    public static IReadOnlyList<MqttEndpoint> GetEnabledPublishEndpoints(AppSettings settings) =>
        settings.MqttEndpoints.Where(e => e.Enabled).ToList();

    public static string ResolveTopic(MqttEndpoint endpoint, AppSettings settings) =>
        endpoint.Topic.Replace("{deviceId}", settings.DeviceId, StringComparison.OrdinalIgnoreCase);
}
