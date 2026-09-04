namespace HuaGuang.Monitor.Services;

public static class MqttTopicDeviceId
{
    /// <summary>
    /// 从 MQTT 主题提取设备标识。
    /// 支持 /RRJFHJ/{clientId}/properties/report 与 monitor/{deviceId}/telemetry。
    /// </summary>
    public static string? Extract(string topic)
    {
        var parts = topic.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length >= 4 &&
            parts[^1].Equals("report", StringComparison.OrdinalIgnoreCase) &&
            parts[^2].Equals("properties", StringComparison.OrdinalIgnoreCase))
        {
            return parts[^3];
        }

        return parts.Length >= 2 ? parts[^2] : null;
    }
}
