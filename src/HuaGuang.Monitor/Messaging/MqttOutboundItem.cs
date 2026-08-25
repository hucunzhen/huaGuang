namespace HuaGuang.Monitor.Messaging;

public sealed class MqttOutboundItem
{
    public required string Topic { get; init; }
    public required string Payload { get; init; }
    public required int Qos { get; init; }
    public Action? OnPublished { get; init; }
}
