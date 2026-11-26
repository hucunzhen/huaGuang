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
    public DateTimeOffset ReceivedAt { get; set; } = DateTimeOffset.Now;

    /// <summary>网页机台卡用：稀疏克隆时保留真实点位数。</summary>
    public int TagCountHint { get; set; }

    public string DisplayLabel => DeviceId;
}
