using HuaGuang.Monitor.Models;

namespace HuaGuang.Monitor.Services;

public static class MqttEndpointCatalog
{
    public static void PrepareForExport(AppSettings settings)
    {
        if (settings.MqttEndpoints.Count == 0)
        {
            settings.MqttEndpoints.Add(MqttEndpoint.FromSettings(settings.Mqtt, "默认"));
        }
    }

    public static void Normalize(AppSettings settings)
    {
        if (settings.MqttEndpoints.Count == 0)
        {
            settings.MqttEndpoints.Add(MqttEndpoint.FromSettings(settings.Mqtt, "默认"));
        }

        var seenIds = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 0; i < settings.MqttEndpoints.Count; i++)
        {
            var endpoint = settings.MqttEndpoints[i];
            if (string.IsNullOrWhiteSpace(endpoint.Id) || !seenIds.Add(endpoint.Id))
            {
                endpoint.Id = Guid.NewGuid().ToString("N");
                seenIds.Add(endpoint.Id);
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

    public static void ValidatePublishCredentials(MqttEndpoint endpoint)
    {
        if (string.IsNullOrWhiteSpace(endpoint.Host))
        {
            throw new InvalidOperationException($"MQTT 目标「{endpoint.Name}」未配置 Broker 地址。");
        }

        if (string.IsNullOrWhiteSpace(endpoint.ClientId))
        {
            throw new InvalidOperationException(
                $"MQTT 目标「{endpoint.Name}」未配置 ClientId，请在设置或 Excel「MQTT目标」中填写。");
        }

        if (string.IsNullOrWhiteSpace(endpoint.Username))
        {
            throw new InvalidOperationException(
                $"MQTT 目标「{endpoint.Name}」未配置用户名，请在设置或 Excel「MQTT目标」对应行第 6 列填写。");
        }

        if (string.IsNullOrEmpty(endpoint.Password))
        {
            throw new InvalidOperationException(
                $"MQTT 目标「{endpoint.Name}」未配置密码，请在设置或 Excel「MQTT目标」对应行第 7 列填写后保存。");
        }
    }
}
