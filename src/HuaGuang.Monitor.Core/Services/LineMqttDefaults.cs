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

    public static void MigrateLegacySettings(AppSettings settings)
    {
        var legacyBroker = string.IsNullOrWhiteSpace(settings.Mqtt.Host) ||
                           settings.Mqtt.Host is "127.0.0.1" or "localhost";
        var legacyPort = settings.Mqtt.Port is 0 or 1883;
        var legacyTopic = string.IsNullOrWhiteSpace(settings.Mqtt.Topic) ||
                          settings.Mqtt.Topic.Contains("{deviceId}", StringComparison.OrdinalIgnoreCase) ||
                          settings.Mqtt.Topic.StartsWith("monitor/", StringComparison.OrdinalIgnoreCase);

        if (legacyBroker && legacyPort)
        {
            ApplyBroker(settings.Mqtt);
        }
        else
        {
            EnsureCredentials(settings.Mqtt);
        }

        if (legacyTopic)
        {
            settings.Mqtt.Topic = ResolvePublishTopic(settings.LineName);
        }

        if (settings.SubscribeTopics.Count == 0 ||
            settings.SubscribeTopics.All(topic =>
                topic.StartsWith("monitor/", StringComparison.OrdinalIgnoreCase)))
        {
            ApplySubscribeTopics(settings);
        }

        if (IsLegacyClientId(settings))
        {
            settings.Mqtt.ClientId = ResolveClientIdForLine(settings.LineName);
        }
    }

    /// <summary>现场常只改 Broker 地址，账号密码仍为空；与 MQTTX 手动填账号不一致时连接会失败。</summary>
    public static void EnsureCredentials(MqttSettings mqtt)
    {
        if (!string.IsNullOrWhiteSpace(mqtt.Username))
        {
            return;
        }

        if (IsProductionBroker(mqtt.Host, mqtt.Port))
        {
            mqtt.Username = Username;
            mqtt.Password = Password;
        }
    }

    public static bool IsProductionBroker(string? host, int port) =>
        string.Equals(host?.Trim(), Host, StringComparison.OrdinalIgnoreCase) && port == Port;

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

    static bool IsLegacyClientId(AppSettings settings)
    {
        var clientId = settings.Mqtt.ClientId?.Trim();
        if (string.IsNullOrWhiteSpace(clientId))
        {
            return true;
        }

        if (string.Equals(clientId, settings.DeviceId, StringComparison.Ordinal))
        {
            return true;
        }

        return clientId is "先河热熔胶复合机" or "华迪热熔胶复合机" or "撒粉复合机" or "平板复合机" or "C型火焰复合机";
    }
}
