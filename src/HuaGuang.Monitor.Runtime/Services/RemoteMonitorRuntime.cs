using HuaGuang.Monitor.Ipc;
using HuaGuang.Monitor.Models;
using Microsoft.Extensions.Logging;

namespace HuaGuang.Monitor.Services;

public sealed class RemoteMonitorAcquisition : IMonitorAcquisition, IDisposable
{
    readonly MonitorIpcClient _client = new();
    readonly SettingsStore _settings;
    readonly ILogger<RemoteMonitorAcquisition> _logger;
    readonly object _gate = new();
    readonly Dictionary<string, TagSnapshot> _snapshots = new(StringComparer.Ordinal);
    readonly System.Timers.Timer _pollTimer;
    MonitorRuntimeState? _state;

    public RemoteMonitorAcquisition(SettingsStore settings, ILogger<RemoteMonitorAcquisition> logger)
    {
        _settings = settings;
        _logger = logger;
        _pollTimer = new System.Timers.Timer(800) { AutoReset = true };
        _pollTimer.Elapsed += (_, _) => _ = PollAsync();
        _pollTimer.Start();
        _ = PollAsync();
    }

    public bool IsRunning => _state?.IsRunning ?? false;
    public bool PlcConnected => _state?.PlcConnected ?? false;
    public bool MqttConnected => _state?.MqttConnected ?? false;
    public int MqttPendingCount => _state?.MqttPendingCount ?? 0;
    public string LastError => _state?.LastError ?? string.Empty;
    public string LastPayload => _state?.LastPayload ?? string.Empty;
    public string LastPublishNote => _state?.LastPublishNote ?? string.Empty;
    public DateTimeOffset? LastPublishTime => _state?.LastPublishTime;
    public double LastCycleElapsedMs => 0;
    public double LastPlcElapsedMs => _state?.LastPlcElapsedMs ?? 0;
    public double LastPublishElapsedMs => 0;
    public double LastWaitElapsedMs => _state?.LastWaitElapsedMs ?? 0;
    public int ActiveScanIntervalMs => _state?.ActiveScanIntervalMs ?? 0;
    public DateTimeOffset? LastCycleCompletedAt => _state?.LastCycleCompletedAt;
    public long CycleCount => _state?.CycleCount ?? 0;
    public IReadOnlyDictionary<string, TagSnapshot> LastSnapshots => _snapshots;

    public event EventHandler? ConnectionChanged;
    public event EventHandler<IReadOnlyList<TagSnapshot>>? TagsUpdated;

    public void RequestImmediatePublish() =>
        _ = SendAsync(new MonitorIpcRequest { Command = MonitorIpcCommand.RequestPublish });

    public Task StartAsync() =>
        SendAsync(new MonitorIpcRequest
        {
            Command = MonitorIpcCommand.Start,
            OperationMode = _settings.Current.OperationMode.ToString()
        }, MonitorIpcClient.CommandTimeout);

    public Task StopAsync() =>
        SendAsync(new MonitorIpcRequest { Command = MonitorIpcCommand.Stop }, MonitorIpcClient.CommandTimeout);

    async Task PollAsync()
    {
        try
        {
            var response = await _client.SendAsync(new MonitorIpcRequest { Command = MonitorIpcCommand.GetStatus })
                .ConfigureAwait(false);
            if (!response.Success || response.State is null)
            {
                return;
            }

            var previousRunning = _state?.IsRunning;
            var previousPlc = _state?.PlcConnected;
            var previousMqtt = _state?.MqttConnected;
            _state = response.State;

            if (previousRunning != _state.IsRunning ||
                previousPlc != _state.PlcConnected ||
                previousMqtt != _state.MqttConnected)
            {
                ConnectionChanged?.Invoke(this, EventArgs.Empty);
            }

            var updated = new List<TagSnapshot>();
            lock (_gate)
            {
                foreach (var item in _state.Snapshots)
                {
                    var snapshot = new TagSnapshot
                    {
                        TagId = item.TagId,
                        Name = item.Name,
                        Unit = item.Unit,
                        Value = JsonValueNormalizer.Normalize(item.Value),
                        Quality = item.Quality,
                        Timestamp = item.Timestamp
                    };
                    _snapshots[item.TagId] = snapshot;
                    updated.Add(snapshot);
                }
            }

            if (updated.Count > 0)
            {
                TagsUpdated?.Invoke(this, updated);
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "IPC 轮询失败");
        }
    }

    async Task SendAsync(MonitorIpcRequest request, TimeSpan? timeout = null)
    {
        var response = await _client.SendAsync(request, timeout).ConfigureAwait(false);
        if (!response.Success)
        {
            throw new InvalidOperationException(response.Error ?? "后台服务命令失败");
        }

        if (response.State is not null)
        {
            _state = response.State;
            ConnectionChanged?.Invoke(this, EventArgs.Empty);
            await PollAsync().ConfigureAwait(false);
        }
    }

    public void Dispose() => _pollTimer.Dispose();
}

public sealed class RemoteMonitorSubscription : IMonitorSubscription, IDisposable
{
    readonly MonitorIpcClient _client = new();
    readonly SettingsStore _settings;
    readonly ILogger<RemoteMonitorSubscription> _logger;
    readonly Dictionary<string, RemoteDeviceState> _devices = new(StringComparer.Ordinal);
    readonly System.Timers.Timer _pollTimer;
    string? _topicFilter;
    MonitorRuntimeState? _state;

    public RemoteMonitorSubscription(SettingsStore settings, ILogger<RemoteMonitorSubscription> logger)
    {
        _settings = settings;
        _logger = logger;
        _pollTimer = new System.Timers.Timer(800) { AutoReset = true };
        _pollTimer.Elapsed += (_, _) => _ = PollAsync();
        _pollTimer.Start();
        _ = PollAsync();
    }

    public bool IsRunning => _state?.IsRunning ?? false;
    public bool IsConnected => _state?.MqttConnected ?? false;
    public string LastError => _state?.LastError ?? string.Empty;
    public string LastPayload => _state?.LastPayload ?? string.Empty;
    public IReadOnlyList<string> ActiveSubscribeTopics => _state?.ActiveSubscribeTopics ?? [];
    public IReadOnlyDictionary<string, RemoteDeviceState> Devices => _devices;

    public event EventHandler? DevicesUpdated;
    public event EventHandler<RemoteTelemetryEventArgs>? TelemetryReceived;
    public event EventHandler? ConnectionChanged;

    public IEnumerable<RemoteDeviceState> GetDevices(string? topicFilter)
    {
        _topicFilter = topicFilter;
        if (string.IsNullOrWhiteSpace(topicFilter) || topicFilter == SubscribeTopicHelper.AllTopicsLabel)
        {
            return _devices.Values.OrderBy(device => device.DeviceId, StringComparer.Ordinal);
        }

        return _devices.Values
            .Where(device => MqttTopicMatcher.IsMatch(device.SourceTopic, topicFilter))
            .OrderBy(device => device.DeviceId, StringComparer.Ordinal);
    }

    public Task StartAsync() =>
        SendAsync(new MonitorIpcRequest
        {
            Command = MonitorIpcCommand.Start,
            OperationMode = _settings.Current.OperationMode.ToString()
        }, MonitorIpcClient.CommandTimeout);

    public Task StopAsync() =>
        SendAsync(new MonitorIpcRequest { Command = MonitorIpcCommand.Stop }, MonitorIpcClient.CommandTimeout);

    public Task RefreshTopicsAsync() =>
        SendAsync(new MonitorIpcRequest { Command = MonitorIpcCommand.RefreshTopics }, MonitorIpcClient.CommandTimeout);

    public void InjectTelemetry(string topic, string payload) =>
        _ = SendAsync(new MonitorIpcRequest
        {
            Command = MonitorIpcCommand.InjectTelemetry,
            Topic = topic,
            Payload = payload
        });

    async Task PollAsync()
    {
        try
        {
            var response = await _client.SendAsync(new MonitorIpcRequest
            {
                Command = MonitorIpcCommand.GetStatus,
                TopicFilter = _topicFilter
            }).ConfigureAwait(false);

            if (!response.Success || response.State is null)
            {
                return;
            }

            var previousRunning = _state?.IsRunning;
            var previousConnected = _state?.MqttConnected;
            _state = response.State;
            var changed = false;

            var keys = _state.Devices.Select(device => device.DeviceKey).ToHashSet(StringComparer.Ordinal);
            foreach (var stale in _devices.Keys.Where(key => !keys.Contains(key)).ToList())
            {
                _devices.Remove(stale);
                changed = true;
            }

            foreach (var dto in _state.Devices)
            {
                var device = new RemoteDeviceState
                {
                    DeviceKey = dto.DeviceKey,
                    DeviceId = dto.DeviceId,
                    SourceTopic = dto.SourceTopic,
                    Timestamp = dto.Timestamp,
                    Quality = dto.Quality,
                    PlcHost = dto.PlcHost,
                    Simulator = dto.Simulator,
                    ReceivedAt = dto.ReceivedAt,
                    Tags = dto.Tags.ToDictionary(
                        pair => pair.Key,
                        pair => JsonValueNormalizer.Normalize(pair.Value),
                        StringComparer.Ordinal)
                };
                _devices[device.DeviceKey] = device;
                changed = true;
            }

            if (previousRunning != _state.IsRunning || previousConnected != _state.MqttConnected)
            {
                ConnectionChanged?.Invoke(this, EventArgs.Empty);
            }

            if (changed)
            {
                DevicesUpdated?.Invoke(this, EventArgs.Empty);
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "IPC 轮询失败");
        }
    }

    async Task SendAsync(MonitorIpcRequest request, TimeSpan? timeout = null)
    {
        var response = await _client.SendAsync(request, timeout).ConfigureAwait(false);
        if (!response.Success)
        {
            throw new InvalidOperationException(response.Error ?? "后台服务命令失败");
        }

        if (response.State is not null)
        {
            _state = response.State;
            ConnectionChanged?.Invoke(this, EventArgs.Empty);
            await PollAsync().ConfigureAwait(false);
        }
    }

    public void Dispose() => _pollTimer.Dispose();

    public ValueTask DisposeAsync()
    {
        Dispose();
        return ValueTask.CompletedTask;
    }
}
