namespace HuaGuang.Monitor.Messaging;

public sealed class MqttPublishTarget
{
    public required string EndpointId { get; init; }
    public required string Topic { get; init; }
    public required int Qos { get; init; }
}
