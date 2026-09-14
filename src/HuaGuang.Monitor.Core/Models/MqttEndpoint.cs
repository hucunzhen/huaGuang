using HuaGuang.Monitor.Services;

namespace HuaGuang.Monitor.Models;

/// <summary>一个 MQTT 发布/连接目标（Broker + 账号 + 主题）。</summary>
public sealed class MqttEndpoint
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "目标 1";
    public bool Enabled { get; set; } = true;
    public string Host { get; set; } = LineMqttDefaults.Host;
    public int Port { get; set; } = LineMqttDefaults.Port;
    public string ClientId { get; set; } = string.Empty;
    public string Username { get; set; } = LineMqttDefaults.Username;
    public string Password { get; set; } = LineMqttDefaults.Password;
    public bool UseTls { get; set; }
    public int Qos { get; set; }
    public string Topic { get; set; } = LineMqttDefaults.XianhePublishTopic;

    public MqttSettings ToSettings() => new()
    {
        Host = Host.Trim(),
        Port = Port,
        ClientId = ClientId.Trim(),
        Username = MqttCredentialNormalizer.NormalizeUsername(Username),
        Password = MqttCredentialNormalizer.NormalizePassword(Password),
        UseTls = UseTls,
        Qos = Qos,
        Topic = Topic
    };

    public static MqttEndpoint FromSettings(MqttSettings settings, string? name = null, string? id = null) => new()
    {
        Id = string.IsNullOrWhiteSpace(id) ? Guid.NewGuid().ToString("N") : id,
        Name = string.IsNullOrWhiteSpace(name) ? "目标 1" : name,
        Enabled = true,
        Host = settings.Host,
        Port = settings.Port,
        ClientId = settings.ClientId,
        Username = settings.Username,
        Password = settings.Password,
        UseTls = settings.UseTls,
        Qos = settings.Qos,
        Topic = settings.Topic
    };

    public void ApplyTo(MqttSettings settings)
    {
        settings.Host = Host;
        settings.Port = Port;
        settings.ClientId = ClientId;
        settings.Username = Username;
        settings.Password = Password;
        settings.UseTls = UseTls;
        settings.Qos = Qos;
        settings.Topic = Topic;
    }
}
