using System.Text.Encodings.Web;
using System.Text.Json;
using HuaGuang.Monitor.Messaging;
using HuaGuang.Monitor.Models;
using HuaGuang.Monitor.Protocols;

namespace HuaGuang.Monitor.Services;

public sealed class AcquisitionService : IDisposable
{
    static readonly JsonSerializerOptions PayloadJson = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    readonly SettingsStore _settingsStore;
    readonly IPlcClient _plc;
    readonly IMqttPublisher _mqtt;
    readonly SemaphoreSlim _gate = new(1, 1);
    CancellationTokenSource? _cts;
    Task? _loop;

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

                var snapshots = new List<TagSnapshot>();
                var values = new Dictionary<string, object?>();
                var allGood = true;

                foreach (var tag in settings.Tags.Where(t => t.Enabled))
                {
                    try
                    {
                        object value;
                        if (settings.UseSimulator)
                        {
                            value = Simulate(tag);
                        }
                        else
                        {
                            value = await _plc.ReadAsync(tag, cancellationToken).ConfigureAwait(false);
                        }

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
                        var payload = JsonSerializer.Serialize(new
                        {
                            deviceId = settings.DeviceId,
                            timestamp = DateTimeOffset.UtcNow,
                            simulator = settings.UseSimulator,
                            plcHost = settings.Plc.Host,
                            quality = allGood ? "Good" : "Uncertain",
                            tags = values
                        }, PayloadJson);

                        var topic = settings.Mqtt.Topic.Replace("{deviceId}", settings.DeviceId, StringComparison.OrdinalIgnoreCase);
                        await _mqtt.PublishAsync(topic, payload, settings.Mqtt.Qos, cancellationToken).ConfigureAwait(false);
                        LastPayload = payload;
                        LastPublishTime = DateTimeOffset.Now;
                        if (allGood)
                        {
                            LastError = string.Empty;
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

    static object Simulate(PlcTag tag)
    {
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
            _ => Math.Round(scaled, 2)
        };
    }

    public void Dispose()
    {
        _cts?.Cancel();
        _cts?.Dispose();
        _gate.Dispose();
    }
}
