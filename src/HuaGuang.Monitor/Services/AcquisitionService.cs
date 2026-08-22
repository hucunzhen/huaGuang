using HuaGuang.Monitor.Messaging;
using HuaGuang.Monitor.Models;
using HuaGuang.Monitor.Protocols;

namespace HuaGuang.Monitor.Services;

public sealed class AcquisitionService : IDisposable
{
    readonly SettingsStore _settingsStore;
    readonly IPlcClient _plc;
    readonly IMqttPublisher _mqtt;
    readonly SemaphoreSlim _gate = new(1, 1);
    readonly Dictionary<string, double> _lastPublishedTemperatures = new(StringComparer.Ordinal);
    CancellationTokenSource? _cts;
    Task? _loop;
    bool _initialPublishDone;

    public AcquisitionService(SettingsStore settingsStore, IPlcClient plc, IMqttPublisher mqtt)
    {
        _settingsStore = settingsStore;
        _plc = plc;
        _mqtt = mqtt;
        _mqtt.ConnectionChanged += (_, connected) => ConnectionChanged?.Invoke(this, EventArgs.Empty);
    }

    public bool IsRunning { get; private set; }
    public bool PlcConnected => _plc.IsConnected || CurrentSettings.UseSimulator;
    public bool MqttConnected => _mqtt.IsConnected;
    public string LastError { get; private set; } = string.Empty;
    public string LastPayload { get; private set; } = string.Empty;
    public string LastPublishNote { get; private set; } = string.Empty;
    public DateTimeOffset? LastPublishTime { get; private set; }

    AppSettings CurrentSettings => _settingsStore.Current;

    public event EventHandler? ConnectionChanged;
    public event EventHandler<IReadOnlyList<TagSnapshot>>? TagsUpdated;

    public async Task StartAsync()
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (IsRunning)
            {
                return;
            }

            _cts = new CancellationTokenSource();
            IsRunning = true;
            LastError = string.Empty;
            LastPublishNote = string.Empty;
            ResetPublishBaseline();
            _loop = Task.Run(() => RunAsync(_cts.Token));
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
            if (_cts is not null)
            {
                await _cts.CancelAsync().ConfigureAwait(false);
            }

            if (_loop is not null)
            {
                try
                {
                    await _loop.ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    // 正常停止
                }
            }

            _cts?.Dispose();
            _cts = null;
            _loop = null;
            ResetPublishBaseline();
            await _plc.DisconnectAsync().ConfigureAwait(false);
            await _mqtt.DisconnectAsync().ConfigureAwait(false);
            ConnectionChanged?.Invoke(this, EventArgs.Empty);
        }
        finally
        {
            _gate.Release();
        }
    }

    async Task RunAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            var settings = CurrentSettings;
            try
            {
                await EnsurePlcAsync(settings, cancellationToken).ConfigureAwait(false);

                var enabledTags = settings.Tags.Where(t => t.Enabled).ToList();
                var snapshots = new List<TagSnapshot>();
                var values = new Dictionary<string, object?>();
                var allGood = true;

                foreach (var tag in enabledTags)
                {
                    try
                    {
                        object value;
                        if (tag.IsManual)
                        {
                            value = ValueFormatting.ResolveManualValue(tag);
                        }
                        else if (settings.UseSimulator)
                        {
                            value = Simulate(tag, settings.TemperaturePrecision);
                        }
                        else
                        {
                            value = await _plc.ReadAsync(tag, cancellationToken).ConfigureAwait(false);
                        }

                        value = ValueFormatting.ApplyTemperaturePrecision(tag, value, settings.TemperaturePrecision);
                        snapshots.Add(new TagSnapshot
                        {
                            TagId = tag.Id,
                            Name = tag.Name,
                            Unit = tag.Unit,
                            Value = value,
                            Quality = "Good",
                            Timestamp = DateTimeOffset.Now
                        });
                        values[tag.Name] = value;
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException)
                    {
                        allGood = false;
                        snapshots.Add(new TagSnapshot
                        {
                            TagId = tag.Id,
                            Name = tag.Name,
                            Unit = tag.Unit,
                            Quality = "Bad",
                            Error = ex.Message,
                            Timestamp = DateTimeOffset.Now
                        });
                        values[tag.Name] = null;
                        LastError = $"点位 {tag.Name}: {ex.Message}";
                        await _plc.DisconnectAsync().ConfigureAwait(false);
                    }
                }

                TagsUpdated?.Invoke(this, snapshots);

                try
                {
                    await EnsureMqttAsync(settings, cancellationToken).ConfigureAwait(false);
                    if (_mqtt.IsConnected && values.Count > 0)
                    {
                        if (ShouldPublish(settings, enabledTags, values))
                        {
                            var payload = MqttPayloadMapper.BuildPayload(settings, values, allGood);

                            var topic = settings.Mqtt.Topic.Replace("{deviceId}", settings.DeviceId, StringComparison.OrdinalIgnoreCase);
                            await _mqtt.PublishAsync(topic, payload, settings.Mqtt.Qos, cancellationToken).ConfigureAwait(false);
                            LastPayload = TruncatePayload(payload);
                            LastPublishTime = DateTimeOffset.Now;
                            LastPublishNote = string.Empty;
                            UpdatePublishedTemperatures(enabledTags, values);
                            if (allGood)
                            {
                                LastError = string.Empty;
                            }
                        }
                        else
                        {
                            LastPublishNote = BuildSkipNote(settings.TemperaturePublishThresholdC);
                        }
                    }
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    LastError = $"MQTT: {ex.Message}";
                }

                ConnectionChanged?.Invoke(this, EventArgs.Empty);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                LastError = ex.Message;
                ConnectionChanged?.Invoke(this, EventArgs.Empty);
            }

            try
            {
                var delay = Math.Clamp(settings.ScanIntervalMs, 200, 60_000);
                await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    async Task EnsurePlcAsync(AppSettings settings, CancellationToken cancellationToken)
    {
        if (settings.UseSimulator || _plc.IsConnected)
        {
            return;
        }

        if (!settings.Tags.Any(t => t.Enabled && !t.IsManual))
        {
            return;
        }

        await _plc.ConnectAsync(settings.Plc, cancellationToken).ConfigureAwait(false);
    }

    async Task EnsureMqttAsync(AppSettings settings, CancellationToken cancellationToken)
    {
        if (_mqtt.IsConnected)
        {
            return;
        }

        await _mqtt.ConnectAsync(settings.Mqtt, cancellationToken).ConfigureAwait(false);
    }

    void ResetPublishBaseline()
    {
        _initialPublishDone = false;
        _lastPublishedTemperatures.Clear();
    }

    bool ShouldPublish(AppSettings settings, IReadOnlyList<PlcTag> enabledTags, IReadOnlyDictionary<string, object?> values)
    {
        var threshold = settings.TemperaturePublishThresholdC;
        if (threshold <= 0)
        {
            return true;
        }

        if (!_initialPublishDone)
        {
            return true;
        }

        foreach (var tag in enabledTags.Where(t => t.IsTemperature && !t.IsManual))
        {
            if (!values.TryGetValue(tag.Name, out var value) || !ValueFormatting.TryAsDouble(value, out var current))
            {
                continue;
            }

            if (!_lastPublishedTemperatures.TryGetValue(tag.Id, out var previous))
            {
                return true;
            }

            if (Math.Abs(current - previous) >= threshold)
            {
                return true;
            }
        }

        return false;
    }

    void UpdatePublishedTemperatures(IReadOnlyList<PlcTag> enabledTags, IReadOnlyDictionary<string, object?> values)
    {
        _initialPublishDone = true;
        foreach (var tag in enabledTags.Where(t => t.IsTemperature && !t.IsManual))
        {
            if (values.TryGetValue(tag.Name, out var value) && ValueFormatting.TryAsDouble(value, out var current))
            {
                _lastPublishedTemperatures[tag.Id] = current;
            }
        }
    }

    static string BuildSkipNote(double threshold) =>
        $"未发布：温度变化未达 {threshold:G}℃ 阈值";

    static bool TryAsDouble(object? value, out double number) =>
        ValueFormatting.TryAsDouble(value, out number);

    static object Simulate(PlcTag tag, int temperaturePrecision)
    {
        if (tag.IsManual)
        {
            return ValueFormatting.ResolveManualValue(tag);
        }

        var wave = DateTime.UtcNow.TimeOfDay.TotalSeconds;
        var phase = tag.Address * 0.35;
        if (tag.DataType == TagDataType.Bool)
        {
            return ((int)wave / 4) % 2 == 0;
        }

        var analog = 40 + 12 * Math.Sin(wave / 6 + phase) + tag.Address;
        var scaled = analog * tag.Scale + tag.Offset;
        return tag.DataType switch
        {
            TagDataType.Int16 => Convert.ToInt16(Math.Clamp(scaled, short.MinValue, short.MaxValue)),
            TagDataType.UInt16 => Convert.ToUInt16(Math.Clamp(scaled, 0, ushort.MaxValue)),
            TagDataType.Int32 => Convert.ToInt32(Math.Clamp(scaled * 10, int.MinValue, int.MaxValue)),
            TagDataType.UInt32 => Convert.ToUInt32(Math.Max(0, Math.Floor(wave * 3 + tag.Address))),
            _ => ValueFormatting.ApplyDisplayPrecision(tag, Math.Round(scaled, 4), temperaturePrecision)
        };
    }

    static string TruncatePayload(string payload) =>
        payload.Length <= 4096
            ? payload
            : payload[..4096] + "…";

    public void Dispose()
    {
        _cts?.Cancel();
        _cts?.Dispose();
        _gate.Dispose();
    }
}
