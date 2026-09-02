using HuaGuang.Monitor.Models;

namespace HuaGuang.Monitor.Services;

public interface IMonitorAcquisition
{
    bool IsRunning { get; }
    bool PlcConnected { get; }
    bool MqttConnected { get; }
    int MqttPendingCount { get; }
    string LastError { get; }
    string LastPayload { get; }
    string LastPublishNote { get; }
    DateTimeOffset? LastPublishTime { get; }
    double LastCycleElapsedMs { get; }
    double LastPlcElapsedMs { get; }
    double LastPublishElapsedMs { get; }
    double LastWaitElapsedMs { get; }
    int ActiveScanIntervalMs { get; }
    DateTimeOffset? LastCycleCompletedAt { get; }
    long CycleCount { get; }
    IReadOnlyDictionary<string, TagSnapshot> LastSnapshots { get; }

    event EventHandler? ConnectionChanged;
    event EventHandler<IReadOnlyList<TagSnapshot>>? TagsUpdated;

    void RequestImmediatePublish();
    Task StartAsync();
    Task StopAsync();
}

public interface IMonitorSubscription : IAsyncDisposable
{
    bool IsRunning { get; }
    bool IsConnected { get; }
    string LastError { get; }
    string LastPayload { get; }
    IReadOnlyList<string> ActiveSubscribeTopics { get; }
    IReadOnlyDictionary<string, RemoteDeviceState> Devices { get; }

    event EventHandler? DevicesUpdated;
    event EventHandler<RemoteTelemetryEventArgs>? TelemetryReceived;
    event EventHandler? ConnectionChanged;

    IEnumerable<RemoteDeviceState> GetDevices(string? topicFilter);
    Task StartAsync();
    Task StopAsync();
    Task RefreshTopicsAsync();
    void InjectTelemetry(string topic, string payload);
}
