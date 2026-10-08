namespace HuaGuang.Monitor.Ipc;

public static class MonitorIpcConstants
{
    public const string DefaultPipeName = "HuaGuang.Monitor.Runtime.v1";
    public const int DefaultTcpPort = 18788;
    public const string DefaultServiceName = "HuaGuangMonitor";
    public const string ServiceDisplayName = "工业监控采集服务";

    /// <summary>主服务管道名（兼容旧常量引用）。</summary>
    public const string PipeName = DefaultPipeName;

    public const int TcpPort = DefaultTcpPort;

    public const string ServiceName = DefaultServiceName;

    public static string CurrentPipeName => Services.MonitorProcessInstance.IpcPipeName;

    public static int CurrentTcpPort => Services.MonitorProcessInstance.IpcTcpPort;

    public static string CurrentServiceName => Services.MonitorProcessInstance.WindowsServiceName;
}

public readonly record struct MonitorIpcEndpoint(string PipeName, int TcpPort)
{
    public static MonitorIpcEndpoint Current =>
        new(MonitorIpcConstants.CurrentPipeName, MonitorIpcConstants.CurrentTcpPort);

    public static MonitorIpcEndpoint ForInstance(string instanceId) =>
        new(
            Services.MonitorProcessInstance.IpcPipeNameFor(instanceId),
            Services.MonitorProcessInstance.IpcTcpPortFor(instanceId));
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
    /// <summary>Start 时可选，与界面当前运行模式一致，避免磁盘配置滞后。</summary>
    public string? OperationMode { get; set; }
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
    /// <summary>界面或 IPC 已发送 Stop，守护服务不应因 AutoStart 再次拉起。</summary>
    public bool OperatorStopRequested { get; set; }
    public bool PlcConnected { get; set; }
    public bool MqttConnected { get; set; }
    public string MqttTargetsStatus { get; set; } = string.Empty;
    public int MqttPendingCount { get; set; }
    public string LastError { get; set; } = string.Empty;
    public string LastPayload { get; set; } = string.Empty;
    public string LastPublishNote { get; set; } = string.Empty;
    public double LastPlcElapsedMs { get; set; }
    public double LastWaitElapsedMs { get; set; }
    public int ActiveScanIntervalMs { get; set; }
    public int ActivePublishIntervalMs { get; set; }
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
