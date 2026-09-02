using HuaGuang.Monitor.Models;
using HuaGuang.Monitor.Services;
using HuaGuang.Monitor.Services.Logging;
using Microsoft.Extensions.Logging;
using MQTTnet;
using MQTTnet.Protocol;

namespace HuaGuang.Monitor.Messaging;

public sealed class MqttPublisher : IMqttPublisher
{
    readonly MqttClientFactory _factory = new();
    readonly ILogger<MqttPublisher> _logger;
    IMqttClient? _client;

    public MqttPublisher(ILogger<MqttPublisher> logger) => _logger = logger;

    public bool IsConnected => _client?.IsConnected == true;

    public event EventHandler<bool>? ConnectionChanged;

    public async Task ConnectAsync(MqttSettings settings, string? lineName, CancellationToken cancellationToken)
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

        var options = MqttConnectionFactory.BuildOptions(settings, lineName);
        try
        {
            await MqttConnectionFactory.ConnectClientAsync(
                client,
                options,
                settings,
                MqttTimeouts.Connect,
                cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "MQTT 连接失败 {Mqtt}",
                LogFormatting.DescribeMqtt(settings, lineName));
            client.Dispose();
            throw;
        }

        _client = client;
        _logger.LogInformation(
            "MQTT 客户端已连接 {Mqtt}",
            LogFormatting.DescribeMqtt(settings, lineName));
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
            _logger.LogInformation("MQTT 客户端已断开");
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

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(MqttTimeouts.Publish);
        try
        {
            await _client.PublishAsync(message, timeoutCts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException($"MQTT 发布超时（{MqttTimeouts.Publish.TotalSeconds:G} 秒）");
        }
    }

    public async ValueTask DisposeAsync() => await DisconnectAsync().ConfigureAwait(false);
}
