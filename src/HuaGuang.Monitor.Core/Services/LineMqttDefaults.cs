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
    public const string ZhidahyClientId = "ZDHYFJ";
    public const string ShanghaihyClientId = "SHHYFJ";
    public const string MeshyClientId = "MESHYFJ";
    public const string S7TestClientId = "S7TEST";
    public const string QimaoZl028aClientId = "QMZL028A";
    public const string QimaoZl028bClientId = "QMZL028B";
    public const string QimaoZl028cClientId = "QMZL028C";
    public const string QimaoZl027aClientId = "QMZL027A";
    public const string QimaoZl027bClientId = "QMZL027B";
    public const string QimaoZl027cClientId = "QMZL027C";
    public const string Dingxing10ClientId = "HGDXJ10";
    public const string Dingxing11ClientId = "HGDXJ11";
    public const string HongbeiClientId = "HGHBJ";

    public const string XianhePublishTopic = "/RRJFHJ/XHRRJFHJ/properties/report";
    public const string HuadiPublishTopic = "/RRJFHJ/HDRRJFHJ/properties/report";
    public const string SafenPublishTopic = "/RRJFHJ/SFHFJ/properties/report";
    public const string PingbanPublishTopic = "/RRJFHJ/PBHFJ/properties/report";
    public const string CyhyPublishTopic = "/RRJFHJ/CYHYFJ/properties/report";
    public const string ZhidahyPublishTopic = "/RRJFHJ/ZDHYFJ/properties/report";
    public const string ShanghaihyPublishTopic = "/RRJFHJ/SHHYFJ/properties/report";
    public const string MeshyPublishTopic = "/RRJFHJ/MESHYFJ/properties/report";
    public const string S7TestPublishTopic = "/RRJFHJ/S7TEST/properties/report";
    public const string QimaoZl028aPublishTopic = "/RRJFHJ/QMZL028A/properties/report";
    public const string QimaoZl028bPublishTopic = "/RRJFHJ/QMZL028B/properties/report";
    public const string QimaoZl028cPublishTopic = "/RRJFHJ/QMZL028C/properties/report";
    public const string QimaoZl027aPublishTopic = "/RRJFHJ/QMZL027A/properties/report";
    public const string QimaoZl027bPublishTopic = "/RRJFHJ/QMZL027B/properties/report";
    public const string QimaoZl027cPublishTopic = "/RRJFHJ/QMZL027C/properties/report";
    public const string Dingxing10PublishTopic = "/RRJFHJ/HGDXJ10/properties/report";
    public const string Dingxing11PublishTopic = "/RRJFHJ/HGDXJ11/properties/report";
    public const string HongbeiPublishTopic = "/RRJFHJ/HGHBJ/properties/report";

    public static IReadOnlyList<string> SubscribeTopics { get; } =
    [
        XianhePublishTopic,
        HuadiPublishTopic,
        SafenPublishTopic,
        PingbanPublishTopic,
        CyhyPublishTopic,
        ZhidahyPublishTopic,
        ShanghaihyPublishTopic,
        MeshyPublishTopic,
        QimaoZl028aPublishTopic,
        QimaoZl028bPublishTopic,
        QimaoZl028cPublishTopic,
        QimaoZl027aPublishTopic,
        QimaoZl027bPublishTopic,
        QimaoZl027cPublishTopic,
        Dingxing10PublishTopic,
        Dingxing11PublishTopic,
        HongbeiPublishTopic,
        S7TestPublishTopic
    ];

    public static string ResolvePublishTopic(string? lineName) => lineName switch
    {
        "华迪热熔胶复合机" => HuadiPublishTopic,
        "撒粉复合机" => SafenPublishTopic,
        "平板复合机" => PingbanPublishTopic,
        "C型火焰复合机" => CyhyPublishTopic,
        LineCatalog.ZhidahyLineName => ZhidahyPublishTopic,
        LineCatalog.ShanghaihyLineName => ShanghaihyPublishTopic,
        LineCatalog.MeshyLineName => MeshyPublishTopic,
        LineCatalog.QimaoZl028aLineName => QimaoZl028aPublishTopic,
        LineCatalog.QimaoZl028bLineName => QimaoZl028bPublishTopic,
        LineCatalog.QimaoZl028cLineName => QimaoZl028cPublishTopic,
        LineCatalog.QimaoZl027aLineName => QimaoZl027aPublishTopic,
        LineCatalog.QimaoZl027bLineName => QimaoZl027bPublishTopic,
        LineCatalog.QimaoZl027cLineName => QimaoZl027cPublishTopic,
        LineCatalog.Dingxing10LineName => Dingxing10PublishTopic,
        LineCatalog.Dingxing11LineName => Dingxing11PublishTopic,
        LineCatalog.HongbeiLineName => HongbeiPublishTopic,
        LineCatalog.S7TestLineName => S7TestPublishTopic,
        _ => XianhePublishTopic
    };

    public static string ResolveClientIdForLine(string? lineName) => lineName switch
    {
        "华迪热熔胶复合机" => HuadiClientId,
        "撒粉复合机" => SafenClientId,
        "平板复合机" => PingbanClientId,
        "C型火焰复合机" => CyhyClientId,
        LineCatalog.ZhidahyLineName => ZhidahyClientId,
        LineCatalog.ShanghaihyLineName => ShanghaihyClientId,
        LineCatalog.MeshyLineName => MeshyClientId,
        LineCatalog.QimaoZl028aLineName => QimaoZl028aClientId,
        LineCatalog.QimaoZl028bLineName => QimaoZl028bClientId,
        LineCatalog.QimaoZl028cLineName => QimaoZl028cClientId,
        LineCatalog.QimaoZl027aLineName => QimaoZl027aClientId,
        LineCatalog.QimaoZl027bLineName => QimaoZl027bClientId,
        LineCatalog.QimaoZl027cLineName => QimaoZl027cClientId,
        LineCatalog.Dingxing10LineName => Dingxing10ClientId,
        LineCatalog.Dingxing11LineName => Dingxing11ClientId,
        LineCatalog.HongbeiLineName => HongbeiClientId,
        LineCatalog.S7TestLineName => S7TestClientId,
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
        ZhidahyClientId => LineCatalog.ZhidahyLineName,
        ShanghaihyClientId => LineCatalog.ShanghaihyLineName,
        MeshyClientId => LineCatalog.MeshyLineName,
        QimaoZl028aClientId => LineCatalog.QimaoZl028aLineName,
        QimaoZl028bClientId => LineCatalog.QimaoZl028bLineName,
        QimaoZl028cClientId => LineCatalog.QimaoZl028cLineName,
        QimaoZl027aClientId => LineCatalog.QimaoZl027aLineName,
        QimaoZl027bClientId => LineCatalog.QimaoZl027bLineName,
        QimaoZl027cClientId => LineCatalog.QimaoZl027cLineName,
        Dingxing10ClientId => LineCatalog.Dingxing10LineName,
        Dingxing11ClientId => LineCatalog.Dingxing11LineName,
        HongbeiClientId => LineCatalog.HongbeiLineName,
        S7TestClientId => LineCatalog.S7TestLineName,
        _ => null
    };

    public static string ResolveDeviceDisplayName(string deviceId) =>
        string.IsNullOrWhiteSpace(deviceId) ? deviceId : deviceId.Trim();
}
