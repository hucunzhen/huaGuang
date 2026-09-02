namespace HuaGuang.Monitor.Ipc;

public static class MonitorIpcConstants
{
    public const string PipeName = "HuaGuang.Monitor.Runtime.v1";
    public const string ServiceName = "HuaGuangMonitor";
    public const string ServiceDisplayName = "工业监控采集服务";
}

public enum MonitorIpcCommand
{
    Ping,
    GetStatus,
    Start,
    Stop,
    ReloadSettings,
    RequestPublish,
    RefreshTopics,
    InjectTelemetry
}

public sealed class MonitorIpcRequest
{
    public MonitorIpcCommand Command { get; set; }
    public string? TopicFilter { get; set; }
    public string? Topic { get; set; }
    public string? Payload { get; set; }
}

public sealed class MonitorIpcResponse
{
    public bool Success { get; set; }
    public string? Error { get; set; }
    public MonitorRuntimeState? State { get; set; }
}

public sealed class MonitorRuntimeState
{
    public string OperationMode { get; set; } = "Acquisition";
    public bool IsRunning { get; set; }
    public bool PlcConnected { get; set; }
    public bool MqttConnected { get; set; }
    public int MqttPendingCount { get; set; }
    public string LastError { get; set; } = string.Empty;
    public string LastPayload { get; set; } = string.Empty;
    public string LastPublishNote { get; set; } = string.Empty;
    public double LastPlcElapsedMs { get; set; }
    public double LastWaitElapsedMs { get; set; }
    public int ActiveScanIntervalMs { get; set; }
    public long CycleCount { get; set; }
    public DateTimeOffset? LastCycleCompletedAt { get; set; }
    public DateTimeOffset? LastPublishTime { get; set; }
    public IReadOnlyList<string> ActiveSubscribeTopics { get; set; } = [];
    public List<TagSnapshotState> Snapshots { get; set; } = [];
    public List<RemoteDeviceStateDto> Devices { get; set; } = [];
}

public sealed class TagSnapshotState
{
    public string TagId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Unit { get; set; } = string.Empty;
    public object? Value { get; set; }
    public string Quality { get; set; } = "Good";
    public DateTimeOffset Timestamp { get; set; }
}

public sealed class RemoteDeviceStateDto
{
    public string DeviceKey { get; set; } = string.Empty;
    public string DeviceId { get; set; } = string.Empty;
    public string SourceTopic { get; set; } = string.Empty;
    public DateTimeOffset Timestamp { get; set; }
    public string Quality { get; set; } = "Good";
    public string PlcHost { get; set; } = string.Empty;
    public bool Simulator { get; set; }
    public Dictionary<string, object?> Tags { get; set; } = new(StringComparer.Ordinal);
    public DateTimeOffset ReceivedAt { get; set; }
}
