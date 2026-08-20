using HuaGuang.Monitor.Models;
using MQTTnet;

namespace HuaGuang.Monitor.Messaging;

static class MqttConnectionFactory
{
    public static MqttClientOptions BuildOptions(MqttSettings settings, string clientIdSuffix)
    {
        var clientId = string.IsNullOrWhiteSpace(settings.ClientId)
            ? $"monitor-{clientIdSuffix}-{Guid.NewGuid():N}"[..22]
            : $"{settings.ClientId}-{clientIdSuffix}";

        var builder = new MqttClientOptionsBuilder()
            .WithTcpServer(settings.Host, settings.Port)
            .WithClientId(clientId)
            .WithCleanSession()
            .WithTimeout(TimeSpan.FromSeconds(5))
            .WithKeepAlivePeriod(TimeSpan.FromSeconds(30))
            .WithProtocolVersion(MQTTnet.Formatter.MqttProtocolVersion.V311);

        if (!string.IsNullOrWhiteSpace(settings.Username))
        {
            builder.WithCredentials(settings.Username, settings.Password);
        }

        if (settings.UseTls)
        {
            builder.WithTlsOptions(tls => tls.UseTls());
        }

        return builder.Build();
    }
}
