using System.Text.Json;
using HuaGuang.Monitor.Messaging;
using HuaGuang.Monitor.Models;
using HuaGuang.Monitor.Services.Logging;
using Microsoft.Extensions.Logging;
using MQTTnet;
using MQTTnet.Protocol;

namespace HuaGuang.Monitor.Services;

public sealed class SubscriptionService : IMonitorSubscription, IAsyncDisposable
{
    const int MaxTrackedDevices = 64;
    static readonly TimeSpan DeviceRetention = TimeSpan.FromMinutes(30);
    const int MaxPayloadLength = 4096;
    static readonly TimeSpan HeartbeatInterval = TimeSpan.FromMinutes(5);
    static readonly TimeSpan ReconnectInitialDelay = TimeSpan.FromSeconds(2);
    static readonly TimeSpan ReconnectMaxDelay = TimeSpan.FromSeconds(60);

    readonly SettingsStore _settingsStore;
    readonly ILogger<SubscriptionService> _logger;
    readonly MqttClientFactory _factory = new();
    readonly Dictionary<string, RemoteDeviceState> _devices = new(StringComparer.Ordinal);
    readonly object _devicesGate = new();
    readonly SemaphoreSlim _gate = new(1, 1);
    readonly SemaphoreSlim _reconnectGate = new(1, 1);
    readonly CoalescedEvent _devicesUpdatedCoalescer;
    readonly object _sessionGate = new();
    IMqttClient? _client;
    IReadOnlyList<string> _sessionTopics = [];
    int _sessionId;
    bool _ignoreDisconnect;
    DateTimeOffset? _lastMessageAt;
    DateTimeOffset? _connectedAt;
    CancellationTokenSource? _heartbeatCts;
    Task? _heartbeatTask;

    public SubscriptionService(SettingsStore settingsStore, ILogger<SubscriptionService> logger)
    {
        _settingsStore = settingsStore;
        _logger = logger;
        _devicesUpdatedCoalescer = new CoalescedEvent(
            TimeSpan.FromMilliseconds(300),
            () => DevicesUpdated?.Invoke(this, EventArgs.Empty));
    }

    public bool IsRunning { get; private set; }
    public bool IsConnected => _client?.IsConnected == true;
    public string LastError { get; private set; } = string.Empty;
    public string LastPayload { get; private set; } = string.Empty;
    public int WebClientCount => SubscribeWebSessions.ActiveCount;
    public IReadOnlyList<string> ActiveSubscribeTopics { get; private set; } = [];

    public IReadOnlyDictionary<string, RemoteDeviceState> Devices
    {
        get
        {
            lock (_devicesGate)
            {
                return _devices.ToDictionary(
                    pair => pair.Key,
                    pair => RemoteDeviceStateHelper.Clone(pair.Value),
                    StringComparer.Ordinal);
            }
        }
    }

    public event EventHandler? DevicesUpdated;
    public event EventHandler<RemoteTelemetryEventArgs>? TelemetryReceived;
    public event EventHandler? ConnectionChanged;

    public IEnumerable<RemoteDeviceState> GetDevices(string? topicFilter)
    {
        lock (_devicesGate)
        {
            IEnumerable<RemoteDeviceState> query = _devices.Values;
            if (!string.IsNullOrWhiteSpace(topicFilter) &&
                topicFilter != SubscribeTopicHelper.AllTopicsLabel)
            {
                query = query.Where(device => MqttTopicMatcher.IsMatch(device.SourceTopic, topicFilter));
            }

            // 必须在锁内物化并克隆，避免 MQTT 写 Tags/字典时网页枚举抛错。
            return query
                .OrderBy(device => device.DeviceId, StringComparer.Ordinal)
                .Select(RemoteDeviceStateHelper.Clone)
                .ToList();
        }
    }

    /// <summary>
    /// 网页轮询快照。汇总拷贝全量点位供筛开关类；详情仅全量拷贝当前机台，其余只保留 Id。
    /// </summary>
    public IEnumerable<RemoteDeviceState> GetDevicesForWeb(string? detailDeviceId)
    {
        lock (_devicesGate)
        {
            var ordered = SubscribeDeviceOrder
                .Order(
                    _devices.Values,
                    device => device.DeviceId,
                    device => device.SourceTopic)
                .ToList();
            if (string.IsNullOrWhiteSpace(detailDeviceId))
            {
                return ordered.Select(RemoteDeviceStateHelper.Clone).ToList();
            }

            return ordered
                .Select(device =>
                    device.DeviceId.Equals(detailDeviceId, StringComparison.OrdinalIgnoreCase)
                        ? RemoteDeviceStateHelper.Clone(device)
                        : new RemoteDeviceState
                        {
                            DeviceKey = device.DeviceKey,
                            DeviceId = device.DeviceId,
                            SourceTopic = device.SourceTopic,
                            ReceivedAt = device.ReceivedAt,
                            Quality = device.Quality,
                            TagCountHint = device.Tags.Count,
                            Tags = new Dictionary<string, object?>(StringComparer.Ordinal)
                        })
                .ToList();
        }
    }

    public async Task StartAsync()
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (IsRunning)
            {
                return;
            }

            var settings = _settingsStore.Current;
            var topics = SubscribeTopicHelper.NormalizeTopics(settings.SubscribeTopics);
            if (topics.Count == 0)
            {
                throw new InvalidOperationException("请至少添加一个订阅主题，例如 monitor/+/telemetry。");
            }

            await ConnectAsync(settings, topics, clearDevices: true, bumpSession: true).ConfigureAwait(false);
            IsRunning = true;
            LastError = string.Empty;
            MonitorRuntimeOperatorControl.SetPausedByOperator(false);
            StartHeartbeat();
            ConnectionChanged?.Invoke(this, EventArgs.Empty);
            _logger.LogInformation(
                "启动订阅 line={LineName} topics={Topics} mqtt={Mqtt}",
                settings.LineName,
                string.Join(", ", topics),
                LogFormatting.DescribeMqtt(settings.SubscribeMqtt, settings.LineName));
        }
        catch (Exception ex)
        {
            IsRunning = false;
            ActiveSubscribeTopics = [];
            LastError = ex.Message;
            ConnectionChanged?.Invoke(this, EventArgs.Empty);
            _logger.LogWarning(ex, "启动订阅失败");
            throw;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task StopAsync()
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            var wasRunning = IsRunning;
            IsRunning = false;
            Interlocked.Increment(ref _sessionId);
            StopHeartbeat();
            ActiveSubscribeTopics = [];
            lock (_devicesGate)
            {
                _devices.Clear();
            }

            _devicesUpdatedCoalescer.Flush();
            await DisconnectAsync(intentional: true).ConfigureAwait(false);
            if (wasRunning)
            {
                MonitorRuntimeOperatorControl.SetPausedByOperator(true);
            }

            ConnectionChanged?.Invoke(this, EventArgs.Empty);
            _logger.LogInformation("订阅已停止");
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task RefreshTopicsAsync()
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (!IsRunning)
            {
                return;
            }

            var settings = _settingsStore.Current;
            var topics = SubscribeTopicHelper.NormalizeTopics(settings.SubscribeTopics);
            if (topics.Count == 0)
            {
                throw new InvalidOperationException("请至少保留一个订阅主题。");
            }

            await ConnectAsync(settings, topics, clearDevices: true, bumpSession: true).ConfigureAwait(false);
            LastError = string.Empty;
            ConnectionChanged?.Invoke(this, EventArgs.Empty);
            _logger.LogInformation(
                "订阅主题已刷新 topics={Topics}",
                string.Join(", ", topics));
        }
        finally
        {
            _gate.Release();
        }
    }

    async Task ConnectAsync(
        AppSettings settings,
        IReadOnlyList<string> topics,
        bool clearDevices,
        bool bumpSession)
    {
        await DisconnectAsync(intentional: true).ConfigureAwait(false);
        if (clearDevices)
        {
            lock (_devicesGate)
            {
                _devices.Clear();
            }
        }

        // 自动重连复用同一 session，避免把进行中的重连循环误判为过期而停掉。
        var sessionId = bumpSession
            ? Interlocked.Increment(ref _sessionId)
            : Volatile.Read(ref _sessionId);
        if (sessionId == 0)
        {
            sessionId = Interlocked.Increment(ref _sessionId);
        }

        _sessionTopics = topics.ToList();

        var client = _factory.CreateMqttClient();
        client.ConnectedAsync += _ =>
        {
            _connectedAt = DateTimeOffset.Now;
            ConnectionChanged?.Invoke(this, EventArgs.Empty);
            return Task.CompletedTask;
        };
        client.DisconnectedAsync += args =>
        {
            ConnectionChanged?.Invoke(this, EventArgs.Empty);
            OnUnexpectedDisconnect(args, sessionId);
            return Task.CompletedTask;
        };
        client.ApplicationMessageReceivedAsync += args =>
        {
            HandleMessage(args.ApplicationMessage);
            return Task.CompletedTask;
        };

        MqttSubscribeAccount.Normalize(settings);
        MqttSubscribeAccount.Validate(settings.SubscribeMqtt);
        var options = MqttConnectionFactory.BuildOptions(settings.SubscribeMqtt, settings.LineName);
        await MqttConnectionFactory.ConnectClientAsync(
            client,
            options,
            settings.SubscribeMqtt,
            MqttTimeouts.Connect,
            CancellationToken.None).ConfigureAwait(false);

        var qos = settings.SubscribeMqtt.Qos switch
        {
            1 => MqttQualityOfServiceLevel.AtLeastOnce,
            2 => MqttQualityOfServiceLevel.ExactlyOnce,
            _ => MqttQualityOfServiceLevel.AtMostOnce
        };

        foreach (var topic in topics)
        {
            await client.SubscribeAsync(new MqttTopicFilterBuilder()
                .WithTopic(topic)
                .WithQualityOfServiceLevel(qos)
                .Build()).ConfigureAwait(false);
        }

        _client = client;
        ActiveSubscribeTopics = topics;
        _connectedAt = DateTimeOffset.Now;
        _logger.LogInformation(
            "MQTT 订阅已连接 topics={Topics} mqtt={Mqtt}",
            string.Join(", ", topics),
            LogFormatting.DescribeMqtt(settings.SubscribeMqtt, settings.LineName));
    }

    void OnUnexpectedDisconnect(MqttClientDisconnectedEventArgs args, int sessionId)
    {
        bool ignore;
        lock (_sessionGate)
        {
            ignore = _ignoreDisconnect || !IsRunning || sessionId != _sessionId;
        }

        if (ignore)
        {
            return;
        }

        _logger.LogWarning(
            args.Exception,
            "MQTT 订阅意外断开 reason={Reason} wasConnected={WasConnected}，将自动重连",
            args.Reason,
            args.ClientWasConnected);

        _ = RecoverConnectionAsync(sessionId);
    }

    async Task RecoverConnectionAsync(int sessionId)
    {
        if (!await _reconnectGate.WaitAsync(0).ConfigureAwait(false))
        {
            return;
        }

        var delay = ReconnectInitialDelay;
        try
        {
            while (IsRunning && sessionId == Volatile.Read(ref _sessionId))
            {
                try
                {
                    await Task.Delay(delay).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    return;
                }

                if (!IsRunning || sessionId != Volatile.Read(ref _sessionId))
                {
                    return;
                }

                if (_client?.IsConnected == true)
                {
                    return;
                }

                await _gate.WaitAsync().ConfigureAwait(false);
                try
                {
                    if (!IsRunning || sessionId != Volatile.Read(ref _sessionId))
                    {
                        return;
                    }

                    if (_client?.IsConnected == true)
                    {
                        return;
                    }

                    var settings = _settingsStore.Current;
                    var topics = _sessionTopics.Count > 0
                        ? _sessionTopics
                        : SubscribeTopicHelper.NormalizeTopics(settings.SubscribeTopics);
                    if (topics.Count == 0)
                    {
                        LastError = "自动重连失败：没有订阅主题。";
                        _logger.LogWarning("{Error}", LastError);
                        ConnectionChanged?.Invoke(this, EventArgs.Empty);
                        return;
                    }

                    await ConnectAsync(settings, topics, clearDevices: false, bumpSession: false)
                        .ConfigureAwait(false);
                    LastError = string.Empty;
                    ConnectionChanged?.Invoke(this, EventArgs.Empty);
                    _logger.LogInformation(
                        "MQTT 订阅已自动重连 topics={Topics}",
                        string.Join(", ", topics));
                    return;
                }
                catch (Exception ex)
                {
                    LastError = $"MQTT 重连失败：{ex.Message}";
                    ConnectionChanged?.Invoke(this, EventArgs.Empty);
                    _logger.LogWarning(ex, "MQTT 订阅自动重连失败，{Delay}s 后重试", delay.TotalSeconds);
                }
                finally
                {
                    _gate.Release();
                }

                delay = TimeSpan.FromSeconds(Math.Min(delay.TotalSeconds * 2, ReconnectMaxDelay.TotalSeconds));
            }
        }
        finally
        {
            _reconnectGate.Release();
        }
    }

    void StartHeartbeat()
    {
        StopHeartbeat();
        _heartbeatCts = new CancellationTokenSource();
        var token = _heartbeatCts.Token;
        _heartbeatTask = Task.Run(() => HeartbeatLoopAsync(token), token);
    }

    void StopHeartbeat()
    {
        try
        {
            _heartbeatCts?.Cancel();
        }
        catch
        {
        }

        _heartbeatCts?.Dispose();
        _heartbeatCts = null;
        _heartbeatTask = null;
    }

    async Task HeartbeatLoopAsync(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(HeartbeatInterval, token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                return;
            }

            if (!IsRunning)
            {
                continue;
            }

            var lastAge = _lastMessageAt is { } at
                ? (DateTimeOffset.Now - at).TotalSeconds
                : (double?)null;
            var connectedAge = _connectedAt is { } connected
                ? (DateTimeOffset.Now - connected).TotalSeconds
                : (double?)null;
            int deviceCount;
            lock (_devicesGate)
            {
                deviceCount = _devices.Count;
            }

            _logger.LogInformation(
                "订阅心跳 connected={Connected} devices={Devices} topics={Topics} lastMessageAgeSec={LastAge} connectedAgeSec={ConnectedAge} lastError={LastError}",
                IsConnected,
                deviceCount,
                ActiveSubscribeTopics.Count,
                lastAge is null ? "—" : lastAge.Value.ToString("0"),
                connectedAge is null ? "—" : connectedAge.Value.ToString("0"),
                string.IsNullOrWhiteSpace(LastError) ? "—" : LastError);
        }
    }

    /// <summary>Diagnostics only: inject telemetry without a live MQTT broker.</summary>
    public void InjectTelemetry(string topic, string payload)
    {
        var message = new MqttApplicationMessageBuilder()
            .WithTopic(topic)
            .WithPayload(payload)
            .Build();
        HandleMessage(message);
    }

    void HandleMessage(MqttApplicationMessage message)
    {
        try
        {
            var payload = message.ConvertPayloadToString() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(payload))
            {
                return;
            }

            using var doc = JsonDocument.Parse(payload);
            var root = doc.RootElement;
            var settings = _settingsStore.Current;
            var profile = settings.MqttPayload ?? new MqttPayloadProfile();
            var parsed = MqttPayloadMapper.Parse(root, profile);
            var deviceId = SubscribeDeviceIdResolver.Resolve(
                message.Topic,
                root,
                profile,
                parsed,
                settings.DeviceId);
            if (string.IsNullOrWhiteSpace(deviceId))
            {
                _logger.LogWarning("忽略遥测：无法解析 deviceId topic={Topic}", message.Topic);
                return;
            }

            var deviceKey = $"{message.Topic}::{deviceId}";
            RemoteDeviceState state;
            lock (_devicesGate)
            {
                state = _devices.TryGetValue(deviceKey, out var existing)
                    ? existing
                    : new RemoteDeviceState
                    {
                        DeviceKey = deviceKey,
                        DeviceId = deviceId,
                        SourceTopic = message.Topic
                    };

                state.ReceivedAt = DateTimeOffset.Now;
                state.Quality = parsed.Quality;
                state.PlcHost = parsed.PlcHost;
                state.Simulator = parsed.Simulator;
                state.Timestamp = parsed.Timestamp;

                state.Tags.Clear();
                foreach (var pair in parsed.Tags)
                {
                    state.Tags[pair.Key] = pair.Value;
                }

                _devices[deviceKey] = state;
                _lastMessageAt = state.ReceivedAt;
                PruneDevices();
            }

            if (state.Tags.Count == 0)
            {
                _logger.LogWarning(
                    "遥测未解析到点位 topic={Topic} tagsPath={TagsPath}",
                    message.Topic,
                    profile.TagsPath);
            }

            LastPayload = TruncatePayload(payload);
            LastError = string.Empty;
            RequestDevicesUpdated();
            TelemetryReceived?.Invoke(this, new RemoteTelemetryEventArgs
            {
                Device = RemoteDeviceStateHelper.Clone(state),
                PayloadJson = LastPayload
            });
        }
        catch (Exception ex)
        {
            LastError = $"解析遥测失败：{ex.Message}";
            _logger.LogWarning(
                ex,
                "解析遥测失败 topic={Topic} payload={Payload}",
                message.Topic,
                LogFormatting.Truncate(message.ConvertPayloadToString()));
            RequestDevicesUpdated();
        }
    }

    void RequestDevicesUpdated() => _devicesUpdatedCoalescer.Request();

    static string TruncatePayload(string payload) =>
        payload.Length <= MaxPayloadLength
            ? payload
            : payload[..MaxPayloadLength] + "…";

    void PruneDevices()
    {
        if (_devices.Count == 0)
        {
            return;
        }

        var cutoff = DateTimeOffset.Now - DeviceRetention;
        List<string>? staleKeys = null;
        foreach (var pair in _devices)
        {
            if (pair.Value.ReceivedAt >= cutoff)
            {
                continue;
            }

            staleKeys ??= [];
            staleKeys.Add(pair.Key);
        }

        if (staleKeys is not null)
        {
            foreach (var key in staleKeys)
            {
                _devices.Remove(key);
            }
        }

        if (_devices.Count <= MaxTrackedDevices)
        {
            return;
        }

        foreach (var key in _devices
                     .OrderBy(pair => pair.Value.ReceivedAt)
                     .Take(_devices.Count - MaxTrackedDevices)
                     .Select(pair => pair.Key)
                     .ToList())
        {
            _devices.Remove(key);
        }
    }

    async Task DisconnectAsync(bool intentional)
    {
        if (_client is null)
        {
            return;
        }

        lock (_sessionGate)
        {
            _ignoreDisconnect = intentional;
        }

        try
        {
            if (_client.IsConnected)
            {
                await _client.DisconnectAsync().ConfigureAwait(false);
            }
        }
        catch
        {
        }
        finally
        {
            _client.Dispose();
            _client = null;
            _connectedAt = null;
            lock (_sessionGate)
            {
                _ignoreDisconnect = false;
            }

            ConnectionChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync().ConfigureAwait(false);
        _devicesUpdatedCoalescer.Dispose();
        _reconnectGate.Dispose();
        _gate.Dispose();
    }
}
