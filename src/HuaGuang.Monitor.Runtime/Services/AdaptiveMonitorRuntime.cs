using HuaGuang.Monitor.Ipc;
using HuaGuang.Monitor.Models;

namespace HuaGuang.Monitor.Services;

/// <summary>
/// Windows UI 进程：后台服务可用时走 IPC，否则在本进程内运行采集/订阅。
/// </summary>
public sealed class AdaptiveMonitorAcquisition : IMonitorAcquisition
{
    readonly AcquisitionService _local;
    readonly RemoteMonitorAcquisition _remote;
    readonly IBackgroundRuntimeLauncher _launcher;

    public AdaptiveMonitorAcquisition(
        AcquisitionService local,
        RemoteMonitorAcquisition remote,
        IBackgroundRuntimeLauncher launcher)
    {
        _local = local;
        _remote = remote;
        _launcher = launcher;
    }

    public bool IsUsingLocal => Active == _local;

    IMonitorAcquisition Active =>
        MonitorIpcClient.IsServiceAvailable() ? _remote : _local;

    public bool IsRunning => Active.IsRunning;
    public bool PlcConnected => Active.PlcConnected;
    public bool MqttConnected => Active.MqttConnected;
    public int MqttPendingCount => Active.MqttPendingCount;
    public string LastError => Active.LastError;
    public string LastPayload => Active.LastPayload;
    public string LastPublishNote => Active.LastPublishNote;
    public DateTimeOffset? LastPublishTime => Active.LastPublishTime;
    public double LastCycleElapsedMs => Active.LastCycleElapsedMs;
    public double LastPlcElapsedMs => Active.LastPlcElapsedMs;
    public double LastPublishElapsedMs => Active.LastPublishElapsedMs;
    public double LastWaitElapsedMs => Active.LastWaitElapsedMs;
    public int ActiveScanIntervalMs => Active.ActiveScanIntervalMs;
    public DateTimeOffset? LastCycleCompletedAt => Active.LastCycleCompletedAt;
    public long CycleCount => Active.CycleCount;
    public IReadOnlyDictionary<string, TagSnapshot> LastSnapshots => Active.LastSnapshots;

    public event EventHandler? ConnectionChanged
    {
        add
        {
            _local.ConnectionChanged += value;
            _remote.ConnectionChanged += value;
        }
        remove
        {
            _local.ConnectionChanged -= value;
            _remote.ConnectionChanged -= value;
        }
    }

    public event EventHandler<IReadOnlyList<TagSnapshot>>? TagsUpdated
    {
        add
        {
            _local.TagsUpdated += value;
            _remote.TagsUpdated += value;
        }
        remove
        {
            _local.TagsUpdated -= value;
            _remote.TagsUpdated -= value;
        }
    }

    public void RequestImmediatePublish() => Active.RequestImmediatePublish();

    public async Task StartAsync()
    {
        if (!MonitorIpcClient.IsServiceAvailable())
        {
            _launcher.EnsureRunning(TimeSpan.FromSeconds(12));
        }

        if (MonitorIpcClient.IsServiceAvailable())
        {
            await _remote.StartAsync().ConfigureAwait(false);
            return;
        }

        if (!_launcher.IsBackgroundPresent())
        {
            await _local.StartAsync().ConfigureAwait(false);
            return;
        }

        throw new InvalidOperationException(
            $"无法连接后台采集服务 IPC：{MonitorIpcClient.DescribeConnectionFailure()}");
    }

    public Task StopAsync() => Active.StopAsync();
}

public sealed class AdaptiveMonitorSubscription : IMonitorSubscription
{
    readonly SubscriptionService _local;
    readonly RemoteMonitorSubscription _remote;
    readonly IBackgroundRuntimeLauncher _launcher;

    public AdaptiveMonitorSubscription(
        SubscriptionService local,
        RemoteMonitorSubscription remote,
        IBackgroundRuntimeLauncher launcher)
    {
        _local = local;
        _remote = remote;
        _launcher = launcher;
    }

    public bool IsUsingLocal => Active == _local;

    IMonitorSubscription Active =>
        MonitorIpcClient.IsServiceAvailable() ? _remote : _local;

    public bool IsRunning => Active.IsRunning;
    public bool IsConnected => Active.IsConnected;
    public string LastError => Active.LastError;
    public string LastPayload => Active.LastPayload;
    public IReadOnlyList<string> ActiveSubscribeTopics => Active.ActiveSubscribeTopics;
    public IReadOnlyDictionary<string, RemoteDeviceState> Devices => Active.Devices;

    public event EventHandler? DevicesUpdated
    {
        add
        {
            _local.DevicesUpdated += value;
            _remote.DevicesUpdated += value;
        }
        remove
        {
            _local.DevicesUpdated -= value;
            _remote.DevicesUpdated -= value;
        }
    }

    public event EventHandler<RemoteTelemetryEventArgs>? TelemetryReceived
    {
        add
        {
            _local.TelemetryReceived += value;
            _remote.TelemetryReceived += value;
        }
        remove
        {
            _local.TelemetryReceived -= value;
            _remote.TelemetryReceived -= value;
        }
    }

    public event EventHandler? ConnectionChanged
    {
        add
        {
            _local.ConnectionChanged += value;
            _remote.ConnectionChanged += value;
        }
        remove
        {
            _local.ConnectionChanged -= value;
            _remote.ConnectionChanged -= value;
        }
    }

    public IEnumerable<RemoteDeviceState> GetDevices(string? topicFilter) =>
        Active.GetDevices(topicFilter);

    public async Task StartAsync()
    {
        if (!MonitorIpcClient.IsServiceAvailable())
        {
            _launcher.EnsureRunning(TimeSpan.FromSeconds(12));
        }

        if (MonitorIpcClient.IsServiceAvailable())
        {
            await _remote.StartAsync().ConfigureAwait(false);
            return;
        }

        if (!_launcher.IsBackgroundPresent())
        {
            await _local.StartAsync().ConfigureAwait(false);
            return;
        }

        throw new InvalidOperationException(
            $"无法连接后台采集服务 IPC：{MonitorIpcClient.DescribeConnectionFailure()}");
    }

    public Task StopAsync() => Active.StopAsync();

    public Task RefreshTopicsAsync() => Active.RefreshTopicsAsync();

    public void InjectTelemetry(string topic, string payload) =>
        Active.InjectTelemetry(topic, payload);

    public ValueTask DisposeAsync() => Active.DisposeAsync();
}
