using HuaGuang.Monitor.Models;

namespace HuaGuang.Monitor.Services;

public static class LineMqttDefaults
{
    public const string Host = "192.168.16.18";
    public const int Port = 1888;
    public const string Username = "hg_iot";
    public const string Password = "Hg@Iot2026";

    public const string XianhePublishTopic = "/RRJFHJ/XHRRJFHJ/properties/report";
    public const string HuadiPublishTopic = "/RRJFHJ/HDRRJFHJ/properties/report";

    public static IReadOnlyList<string> SubscribeTopics { get; } =
    [
        XianhePublishTopic,
        HuadiPublishTopic
    ];

    public static string ResolvePublishTopic(string? lineName) =>
        lineName == "华迪热熔胶复合机" ? HuadiPublishTopic : XianhePublishTopic;

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
    }
}
