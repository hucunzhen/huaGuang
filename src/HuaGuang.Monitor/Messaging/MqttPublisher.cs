using HuaGuang.Monitor.Models;
using MQTTnet;
using MQTTnet.Protocol;

namespace HuaGuang.Monitor.Messaging;

public sealed class MqttPublisher : IMqttPublisher
{
    readonly MqttClientFactory _factory = new();
    IMqttClient? _client;

    public bool IsConnected => _client?.IsConnected == true;

    public event EventHandler<bool>? ConnectionChanged;

    public async Task ConnectAsync(MqttSettings settings, CancellationToken cancellationToken)
    {
        await DisconnectAsync().ConfigureAwait(false);

        var client = _factory.CreateMqttClient();
        client.ConnectedAsync += _ =>
        {
            ConnectionChanged?.Invoke(this, true);
            return Task.CompletedTask;
        };
        client.DisconnectedAsync += _ =>
        {
            ConnectionChanged?.Invoke(this, false);
            return Task.CompletedTask;
        };

        var clientId = string.IsNullOrWhiteSpace(settings.ClientId)
            ? $"huaguang-{Guid.NewGuid():N}"[..20]
            : settings.ClientId;

        var options = new MqttClientOptionsBuilder()
            .WithTcpServer(settings.Host, settings.Port)
            .WithClientId(clientId)
            .WithCleanSession()
            .WithTimeout(TimeSpan.FromSeconds(5))
            .WithKeepAlivePeriod(TimeSpan.FromSeconds(30))
            .WithProtocolVersion(MQTTnet.Formatter.MqttProtocolVersion.V311);

        if (!string.IsNullOrWhiteSpace(settings.Username))
        {
            options.WithCredentials(settings.Username, settings.Password);
        }

        if (settings.UseTls)
        {
            options.WithTlsOptions(tls => tls.UseTls());
        }

        await client.ConnectAsync(options.Build(), cancellationToken).ConfigureAwait(false);
        _client = client;
    }

    public async Task DisconnectAsync()
    {
        if (_client is null)
        {
            return;
        }

        try
        {
            if (_client.IsConnected)
            {
                await _client.DisconnectAsync().ConfigureAwait(false);
            }
        }
        catch
        {
            // 断开时忽略网络异常
        }
        finally
        {
            _client.Dispose();
            _client = null;
            ConnectionChanged?.Invoke(this, false);
        }
    }

    public async Task PublishAsync(string topic, string payload, int qos, CancellationToken cancellationToken)
    {
        if (_client is not { IsConnected: true })
        {
            throw new InvalidOperationException("MQTT 未连接");
        }

        var level = qos switch
        {
            1 => MqttQualityOfServiceLevel.AtLeastOnce,
            2 => MqttQualityOfServiceLevel.ExactlyOnce,
            _ => MqttQualityOfServiceLevel.AtMostOnce
        };

        var message = new MqttApplicationMessageBuilder()
            .WithTopic(topic)
            .WithPayload(payload)
            .WithQualityOfServiceLevel(level)
            .Build();

        await _client.PublishAsync(message, cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask DisposeAsync() => await DisconnectAsync().ConfigureAwait(false);
}
