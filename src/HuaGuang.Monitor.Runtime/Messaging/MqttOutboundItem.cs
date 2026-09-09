namespace HuaGuang.Monitor.Messaging;

public sealed class MqttOutboundItem
{
    public required string Payload { get; init; }
    public required IReadOnlyList<MqttPublishTarget> Targets { get; init; }
    public Action? OnPublished { get; init; }
}
