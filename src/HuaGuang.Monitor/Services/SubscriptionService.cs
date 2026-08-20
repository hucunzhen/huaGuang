using System.Text.Json;
using HuaGuang.Monitor.Messaging;
using HuaGuang.Monitor.Models;
using MQTTnet;
using MQTTnet.Protocol;

namespace HuaGuang.Monitor.Services;

public sealed class SubscriptionService : IAsyncDisposable
{
    readonly SettingsStore _settingsStore;
    readonly MqttClientFactory _factory = new();
    readonly Dictionary<string, RemoteDeviceState> _devices = new(StringComparer.Ordinal);
    readonly SemaphoreSlim _gate = new(1, 1);
    IMqttClient? _client;

    public SubscriptionService(SettingsStore settingsStore)
    {
        _settingsStore = settingsStore;
    }

    public bool IsRunning { get; private set; }
    public bool IsConnected => _client?.IsConnected == true;
    public string LastError { get; private set; } = string.Empty;
    public string LastPayload { get; private set; } = string.Empty;
    public IReadOnlyList<string> ActiveSubscribeTopics { get; private set; } = [];

    public IReadOnlyDictionary<string, RemoteDeviceState> Devices => _devices;

    public event EventHandler? DevicesUpdated;
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
            await DisconnectAsync().ConfigureAwait(false);
            ConnectionChanged?.Invoke(this, EventArgs.Empty);
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
        }
        finally
        {
            _gate.Release();
        }
    }

    async Task ConnectAsync(AppSettings settings, IReadOnlyList<string> topics)
    {
        await DisconnectAsync().ConfigureAwait(false);

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

        var options = MqttConnectionFactory.BuildOptions(settings.Mqtt, "sub");
        await client.ConnectAsync(options, CancellationToken.None).ConfigureAwait(false);

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
            var deviceId = root.TryGetProperty("deviceId", out var deviceEl)
                ? deviceEl.GetString()
                : ExtractDeviceIdFromTopic(message.Topic);
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

            state.LastPayload = payload;
            state.ReceivedAt = DateTimeOffset.Now;
            state.Quality = root.TryGetProperty("quality", out var qualityEl)
                ? qualityEl.GetString() ?? "Good"
                : "Good";
            state.PlcHost = root.TryGetProperty("plcHost", out var plcEl)
                ? plcEl.GetString() ?? string.Empty
                : string.Empty;
            state.Simulator = root.TryGetProperty("simulator", out var simEl) && simEl.GetBoolean();
            state.Timestamp = root.TryGetProperty("timestamp", out var tsEl) &&
                              DateTimeOffset.TryParse(tsEl.GetString(), out var parsed)
                ? parsed
                : state.ReceivedAt;

            state.Tags.Clear();
            if (root.TryGetProperty("tags", out var tagsEl) && tagsEl.ValueKind == JsonValueKind.Object)
            {
                foreach (var property in tagsEl.EnumerateObject())
                {
                    state.Tags[property.Name] = JsonElementToObject(property.Value);
                }
            }

            _devices[deviceKey] = state;
            LastPayload = payload;
            LastError = string.Empty;
            DevicesUpdated?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            LastError = $"解析遥测失败：{ex.Message}";
            DevicesUpdated?.Invoke(this, EventArgs.Empty);
        }
    }

    static string? ExtractDeviceIdFromTopic(string topic)
    {
        var parts = topic.Split('/', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length >= 2 ? parts[^2] : null;
    }

    static object? JsonElementToObject(JsonElement element) => element.ValueKind switch
    {
        JsonValueKind.String => element.GetString(),
        JsonValueKind.Number => element.TryGetInt64(out var integer) && !element.GetRawText().Contains('.')
            ? integer
            : element.GetDouble(),
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        JsonValueKind.Null => null,
        _ => element.GetRawText()
    };

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
