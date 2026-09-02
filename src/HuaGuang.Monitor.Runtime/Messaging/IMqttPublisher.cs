using HuaGuang.Monitor.Models;

namespace HuaGuang.Monitor.Messaging;

public interface IMqttPublisher : IAsyncDisposable
{
    bool IsConnected { get; }
    event EventHandler<bool>? ConnectionChanged;
    Task ConnectAsync(MqttSettings settings, string? lineName, CancellationToken cancellationToken);
    Task DisconnectAsync();
    Task PublishAsync(string topic, string payload, int qos, CancellationToken cancellationToken);
}
