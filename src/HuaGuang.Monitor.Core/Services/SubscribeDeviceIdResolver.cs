using System.Text.Json;
using HuaGuang.Monitor.Models;

namespace HuaGuang.Monitor.Services;

/// <summary>订阅遥测：解析消息来自哪台设备（使用 deviceId，不用 MQTT clientId）。</summary>
public static class SubscribeDeviceIdResolver
{
    public static string? Resolve(
        string topic,
        JsonElement root,
        MqttPayloadProfile profile,
        TelemetryParseResult parsed,
        string? fallbackDeviceId = null)
    {
        var fromPayload = FirstNonEmpty(
            string.IsNullOrWhiteSpace(profile.DeviceIdPath) ? null : parsed.DeviceId,
            ReadRootDeviceId(root),
            ReadPropertiesDeviceId(root, profile));

        if (!string.IsNullOrWhiteSpace(fromPayload))
        {
            return fromPayload;
        }

        var fromTopic = LineMqttDefaults.ResolveDeviceIdFromTopic(topic);
        if (!string.IsNullOrWhiteSpace(fromTopic))
        {
            return fromTopic;
        }

        return string.IsNullOrWhiteSpace(fallbackDeviceId) ? null : fallbackDeviceId.Trim();
    }

    static string? ReadRootDeviceId(JsonElement root) =>
        root.ValueKind == JsonValueKind.Object &&
        root.TryGetProperty("deviceId", out var element) &&
        element.ValueKind == JsonValueKind.String
            ? element.GetString()
            : null;

    static string? ReadPropertiesDeviceId(JsonElement root, MqttPayloadProfile profile)
    {
        if (!string.Equals(profile.TagsPath, "properties", StringComparison.OrdinalIgnoreCase) ||
            root.ValueKind != JsonValueKind.Object ||
            !root.TryGetProperty("properties", out var properties) ||
            properties.ValueKind != JsonValueKind.Object ||
            !properties.TryGetProperty("deviceId", out var element) ||
            element.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        return element.GetString();
    }

    static string? FirstNonEmpty(params string?[] values)
    {
        foreach (var value in values)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value.Trim();
            }
        }

        return null;
    }
}
