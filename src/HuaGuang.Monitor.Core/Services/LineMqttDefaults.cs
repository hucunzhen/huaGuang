using HuaGuang.Monitor.Models;

namespace HuaGuang.Monitor.Services;

public static class LineMqttDefaults
{
    public const string Host = "192.168.16.18";
    public const int Port = 1888;
    public const string Username = "hg_iot";
    public const string Password = "Hg@Iot2026";

    public const string XianheClientId = "XHRRJFHJ";
    public const string HuadiClientId = "HDRRJFHJ";
    public const string SafenClientId = "SFHFJ";
    public const string PingbanClientId = "PBHFJ";
    public const string CyhyClientId = "CYHYFJ";

    public const string XianhePublishTopic = "/RRJFHJ/XHRRJFHJ/properties/report";
    public const string HuadiPublishTopic = "/RRJFHJ/HDRRJFHJ/properties/report";
    public const string SafenPublishTopic = "/RRJFHJ/SFHFJ/properties/report";
    public const string PingbanPublishTopic = "/RRJFHJ/PBHFJ/properties/report";
    public const string CyhyPublishTopic = "/RRJFHJ/CYHYFJ/properties/report";

    public static IReadOnlyList<string> SubscribeTopics { get; } =
    [
        XianhePublishTopic,
        HuadiPublishTopic,
        SafenPublishTopic,
        PingbanPublishTopic,
        CyhyPublishTopic
    ];

    public static string ResolvePublishTopic(string? lineName) => lineName switch
    {
        "华迪热熔胶复合机" => HuadiPublishTopic,
        "撒粉复合机" => SafenPublishTopic,
        "平板复合机" => PingbanPublishTopic,
        "C型火焰复合机" => CyhyPublishTopic,
        _ => XianhePublishTopic
    };

    public static string ResolveClientIdForLine(string? lineName) => lineName switch
    {
        "华迪热熔胶复合机" => HuadiClientId,
        "撒粉复合机" => SafenClientId,
        "平板复合机" => PingbanClientId,
        "C型火焰复合机" => CyhyClientId,
        _ => XianheClientId
    };

    public static void ApplyBroker(MqttSettings mqtt)
    {
        mqtt.Host = Host;
        mqtt.Port = Port;
        mqtt.Username = Username;
        mqtt.Password = Password;
    }

    public static void ApplySubscribeTopics(AppSettings settings)
    {
        settings.SubscribeTopics = SubscribeTopics.ToList();
        settings.SubscribeTopic = SubscribeTopics[0];
    }

    public static (string Username, string Password) ResolveCredentials(MqttSettings mqtt) =>
        (mqtt.Username?.Trim() ?? string.Empty, mqtt.Password ?? string.Empty);

    public static string ResolveClientId(MqttSettings mqtt, string? lineName = null)
    {
        if (!string.IsNullOrWhiteSpace(mqtt.ClientId))
        {
            return mqtt.ClientId.Trim();
        }

        return ResolveClientIdForLine(lineName);
    }

    /// <summary>从 MQTT 主题推断 deviceId（产线名）；平台 topic 中 clientId 段会映射为产线名。</summary>
    public static string? ResolveDeviceIdFromTopic(string topic)
    {
        var segment = MqttTopicDeviceId.Extract(topic);
        if (string.IsNullOrWhiteSpace(segment))
        {
            return null;
        }

        return ResolveLineNameFromClientId(segment) ?? segment.Trim();
    }

    public static string? ResolveLineNameFromClientId(string? clientId) => clientId?.Trim() switch
    {
        XianheClientId => "先河热熔胶复合机",
        HuadiClientId => "华迪热熔胶复合机",
        SafenClientId => "撒粉复合机",
        PingbanClientId => "平板复合机",
        CyhyClientId => "C型火焰复合机",
        _ => null
    };

    public static string ResolveDeviceDisplayName(string deviceId) =>
        string.IsNullOrWhiteSpace(deviceId) ? deviceId : deviceId.Trim();
}
