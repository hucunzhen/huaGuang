namespace HuaGuang.Monitor.Models;

public sealed class RemoteDeviceState
{
    public required string DeviceKey { get; init; }
    public required string DeviceId { get; init; }
    public required string SourceTopic { get; init; }
    public DateTimeOffset Timestamp { get; set; }
    public string Quality { get; set; } = "Good";
    public string PlcHost { get; set; } = string.Empty;
    public bool Simulator { get; set; }
    public Dictionary<string, object?> Tags { get; set; } = new(StringComparer.Ordinal);
    public string LastPayload { get; set; } = string.Empty;
    public DateTimeOffset ReceivedAt { get; set; } = DateTimeOffset.Now;

    public string DisplayLabel => $"{DeviceId} ({SourceTopic})";
}
