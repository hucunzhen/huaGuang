using CommunityToolkit.Mvvm.ComponentModel;
using HuaGuang.Monitor.Models;
using HuaGuang.Monitor.Services;

namespace HuaGuang.Monitor.ViewModels;

public partial class MqttEndpointViewModel : ObservableObject
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    [ObservableProperty] string name = "目标 1";
    [ObservableProperty] bool enabled = true;
    [ObservableProperty] string host = LineMqttDefaults.Host;
    [ObservableProperty] string port = LineMqttDefaults.Port.ToString();
    [ObservableProperty] string clientId = string.Empty;
    [ObservableProperty] string username = LineMqttDefaults.Username;
    [ObservableProperty] string password = LineMqttDefaults.Password;
    [ObservableProperty] bool useTls;
    [ObservableProperty] string qos = "0";
    [ObservableProperty] string topic = LineMqttDefaults.XianhePublishTopic;

    public static MqttEndpointViewModel FromModel(MqttEndpoint endpoint) => new()
    {
        Id = endpoint.Id,
        Name = endpoint.Name,
        Enabled = endpoint.Enabled,
        Host = endpoint.Host,
        Port = endpoint.Port.ToString(),
        ClientId = endpoint.ClientId,
        Username = endpoint.Username,
        Password = endpoint.Password,
        UseTls = endpoint.UseTls,
        Qos = endpoint.Qos.ToString(),
        Topic = endpoint.Topic
    };

    public MqttEndpoint ToModel() => new()
    {
        Id = string.IsNullOrWhiteSpace(Id) ? Guid.NewGuid().ToString("N") : Id,
        Name = Name.Trim(),
        Enabled = Enabled,
        Host = Host.Trim(),
        Port = ParseInt(Port, LineMqttDefaults.Port, 1, 65535),
        ClientId = ClientId.Trim(),
        Username = Username.Trim(),
        Password = Password,
        UseTls = UseTls,
        Qos = ParseInt(Qos, 0, 0, 2),
        Topic = Topic.Trim()
    };

    public void CopyDefaultsFromLine(string? lineName)
    {
        Host = LineMqttDefaults.Host;
        Port = LineMqttDefaults.Port.ToString();
        Username = LineMqttDefaults.Username;
        Password = LineMqttDefaults.Password;
        ClientId = LineMqttDefaults.ResolveClientIdForLine(lineName);
        Topic = LineMqttDefaults.ResolvePublishTopic(lineName);
    }

    static int ParseInt(string text, int fallback, int min, int max)
    {
        if (!int.TryParse(text, out var value))
        {
            value = fallback;
        }

        return Math.Clamp(value, min, max);
    }
}
