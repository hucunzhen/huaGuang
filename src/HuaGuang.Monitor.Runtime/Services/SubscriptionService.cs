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

    readonly SettingsStore _settingsStore;
    readonly ILogger<SubscriptionService> _logger;
    readonly MqttClientFactory _factory = new();
    readonly Dictionary<string, RemoteDeviceState> _devices = new(StringComparer.Ordinal);
    readonly SemaphoreSlim _gate = new(1, 1);
    IMqttClient? _client;

    public SubscriptionService(SettingsStore settingsStore, ILogger<SubscriptionService> logger)
    {
        _settingsStore = settingsStore;
        _logger = logger;
    }

    public bool IsRunning { get; private set; }
    public bool IsConnected => _client?.IsConnected == true;
    public string LastError { get; private set; } = string.Empty;
    public string LastPayload { get; private set; } = string.Empty;
    public IReadOnlyList<string> ActiveSubscribeTopics { get; private set; } = [];

    public IReadOnlyDictionary<string, RemoteDeviceState> Devices => _devices;

    public event EventHandler? DevicesUpdated;
    public event EventHandler<RemoteTelemetryEventArgs>? TelemetryReceived;
    public event EventHandler? ConnectionChanged;

    public IEnumerable<RemoteDeviceState> GetDevices(string? topicFilter)
    {
        var devices = _devices.Values;
        if (string.IsNullOrWhiteSpace(topicFilter) || topicFilter == SubscribeTopicHelper.AllTopicsLabel)
        {
            return devices.OrderBy(device => device.DeviceId, StringComparer.Ordinal);
        }

        return devices
            .Where(device => MqttTopicMatcher.IsMatch(device.SourceTopic, topicFilter))
            .OrderBy(device => device.DeviceId, StringComparer.Ordinal);
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
            SubscribeTopicHelper.Migrate(settings);
            var topics = SubscribeTopicHelper.NormalizeTopics(settings.SubscribeTopics);
            if (topics.Count == 0)
            {
                throw new InvalidOperationException("请至少添加一个订阅主题，例如 monitor/+/telemetry。");
            }

            await ConnectAsync(settings, topics).ConfigureAwait(false);
            IsRunning = true;
            LastError = string.Empty;
            ConnectionChanged?.Invoke(this, EventArgs.Empty);
            _logger.LogInformation(
                "启动订阅 line={LineName} topics={Topics} mqtt={Mqtt}",
                settings.LineName,
                string.Join(", ", topics),
                LogFormatting.DescribeMqtt(settings.Mqtt, settings.LineName));
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
            IsRunning = false;
            ActiveSubscribeTopics = [];
            _devices.Clear();
            await DisconnectAsync().ConfigureAwait(false);
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
            SubscribeTopicHelper.Migrate(settings);
            var topics = SubscribeTopicHelper.NormalizeTopics(settings.SubscribeTopics);
            if (topics.Count == 0)
            {
                throw new InvalidOperationException("请至少保留一个订阅主题。");
            }

            await ConnectAsync(settings, topics).ConfigureAwait(false);
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

    async Task ConnectAsync(AppSettings settings, IReadOnlyList<string> topics)
    {
        await DisconnectAsync().ConfigureAwait(false);
        _devices.Clear();

        var client = _factory.CreateMqttClient();
        client.ConnectedAsync += _ =>
        {
            ConnectionChanged?.Invoke(this, EventArgs.Empty);
            return Task.CompletedTask;
        };
        client.DisconnectedAsync += _ =>
        {
            ConnectionChanged?.Invoke(this, EventArgs.Empty);
            return Task.CompletedTask;
        };
        client.ApplicationMessageReceivedAsync += args =>
        {
            HandleMessage(args.ApplicationMessage);
            return Task.CompletedTask;
        };

        var options = MqttConnectionFactory.BuildOptions(settings.Mqtt, settings.LineName);
        await MqttConnectionFactory.ConnectClientAsync(
            client,
            options,
            settings.Mqtt,
            MqttTimeouts.Connect,
            CancellationToken.None).ConfigureAwait(false);

        var qos = settings.Mqtt.Qos switch
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
        _logger.LogInformation(
            "MQTT 订阅已连接 topics={Topics} mqtt={Mqtt}",
            string.Join(", ", topics),
            LogFormatting.DescribeMqtt(settings.Mqtt, settings.LineName));
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
            var deviceId = parsed.DeviceId ?? ExtractDeviceIdFromTopic(message.Topic);
            if (string.IsNullOrWhiteSpace(deviceId))
            {
                return;
            }

            var deviceKey = $"{message.Topic}::{deviceId}";
            var state = _devices.TryGetValue(deviceKey, out var existing)
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
            PruneDevices();
            LastPayload = TruncatePayload(payload);
            LastError = string.Empty;
            DevicesUpdated?.Invoke(this, EventArgs.Empty);
            TelemetryReceived?.Invoke(this, new RemoteTelemetryEventArgs
            {
                Device = state,
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
            DevicesUpdated?.Invoke(this, EventArgs.Empty);
        }
    }

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

    static string? ExtractDeviceIdFromTopic(string topic)
    {
        var parts = topic.Split('/', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length >= 2 ? parts[^2] : null;
    }

    async Task DisconnectAsync()
    {
        if (_client is null)
        {
            return;
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
            ConnectionChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync().ConfigureAwait(false);
        _gate.Dispose();
    }
}
