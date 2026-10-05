using HuaGuang.Monitor.Models;

namespace HuaGuang.Monitor.Services;

/// <summary>订阅模式独立 MQTT 账号（与采集发布目标权限分离）。</summary>
public static class MqttSubscribeAccount
{
    public const string DefaultClientId = "HG_SUB";

    public static MqttSettings CreateDefault() => new()
    {
        Host = LineMqttDefaults.Host,
        Port = LineMqttDefaults.Port,
        ClientId = DefaultClientId,
        Username = string.Empty,
        Password = string.Empty,
        UseTls = false,
        Qos = 0,
        Topic = string.Empty
    };

    public static MqttSettings Clone(MqttSettings source) => new()
    {
        Host = source.Host,
        Port = source.Port,
        ClientId = source.ClientId,
        Username = source.Username,
        Password = source.Password,
        UseTls = source.UseTls,
        Qos = source.Qos,
        Topic = source.Topic
    };

    public static void ApplyBrokerFromPublish(MqttSettings subscribe, MqttSettings publish)
    {
        subscribe.Host = publish.Host;
        subscribe.Port = publish.Port;
        subscribe.UseTls = publish.UseTls;
        subscribe.Qos = publish.Qos;
        subscribe.ClientId = DefaultClientId;
        subscribe.Username = string.Empty;
        subscribe.Password = string.Empty;
        subscribe.Topic = string.Empty;
    }

    /// <summary>旧 Excel 没有订阅账号时，先沿用采集账号，避免升级后订阅连不上。</summary>
    public static void SeedFromPublishIfMissing(AppSettings settings)
    {
        settings.SubscribeMqtt = Clone(settings.Mqtt);
        settings.SubscribeMqtt.Topic = string.Empty;
    }

    public static void Normalize(AppSettings settings)
    {
        settings.SubscribeMqtt ??= CreateDefault();
        if (string.IsNullOrWhiteSpace(settings.SubscribeMqtt.Host))
        {
            settings.SubscribeMqtt.Host = string.IsNullOrWhiteSpace(settings.Mqtt.Host)
                ? LineMqttDefaults.Host
                : settings.Mqtt.Host;
        }

        if (settings.SubscribeMqtt.Port <= 0)
        {
            settings.SubscribeMqtt.Port = settings.Mqtt.Port > 0 ? settings.Mqtt.Port : LineMqttDefaults.Port;
        }

        if (string.IsNullOrWhiteSpace(settings.SubscribeMqtt.ClientId))
        {
            settings.SubscribeMqtt.ClientId = DefaultClientId;
        }
    }

    public static void Validate(MqttSettings mqtt)
    {
        if (string.IsNullOrWhiteSpace(mqtt.Host))
        {
            throw new InvalidOperationException("订阅 MQTT 未配置 Broker 地址。");
        }

        if (string.IsNullOrWhiteSpace(mqtt.ClientId))
        {
            throw new InvalidOperationException("订阅 MQTT 未配置 ClientId。");
        }

        if (string.IsNullOrWhiteSpace(mqtt.Username))
        {
            throw new InvalidOperationException("订阅 MQTT 未配置用户名（与采集账号分开填写）。");
        }

        if (string.IsNullOrEmpty(mqtt.Password))
        {
            throw new InvalidOperationException("订阅 MQTT 未配置密码（与采集账号分开填写）。");
        }
    }
}
