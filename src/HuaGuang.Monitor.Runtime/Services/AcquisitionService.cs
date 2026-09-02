using System.Diagnostics;
using HuaGuang.Monitor.Messaging;
using HuaGuang.Monitor.Models;
using HuaGuang.Monitor.Protocols;
using HuaGuang.Monitor.Services.Logging;
using Microsoft.Extensions.Logging;

namespace HuaGuang.Monitor.Services;

public sealed class AcquisitionService : IMonitorAcquisition, IDisposable
{
    readonly SettingsStore _settingsStore;
    readonly IPlcClient _plc;
    readonly MqttOutboundService _mqttOutbound;
    readonly IAcquisitionBackgroundGuard _backgroundGuard;
    readonly ILogger<AcquisitionService> _logger;
    readonly SemaphoreSlim _gate = new(1, 1);
    readonly object _publishStateGate = new();
    readonly Dictionary<string, double> _lastPublishedTemperatures = new(StringComparer.Ordinal);
    readonly Dictionary<string, TagSnapshot> _lastSnapshots = new(StringComparer.Ordinal);
    CancellationTokenSource? _cts;
    Thread? _loopThread;
    IDisposable? _backgroundLease;
    bool _initialPublishDone;
    int _forcePublishSignal;
    string _plcError = string.Empty;
    DateTimeOffset _plcConnectRetryAfter = DateTimeOffset.MinValue;

    public AcquisitionService(
        SettingsStore settingsStore,
        IPlcClient plc,
        MqttOutboundService mqttOutbound,
        IAcquisitionBackgroundGuard backgroundGuard,
        ILogger<AcquisitionService> logger)
    {
        _settingsStore = settingsStore;
        _plc = plc;
        _mqttOutbound = mqttOutbound;
        _backgroundGuard = backgroundGuard;
        _logger = logger;
        _mqttOutbound.StateChanged += (_, _) => ConnectionChanged?.Invoke(this, EventArgs.Empty);
    }

    public bool IsRunning { get; private set; }
    public bool PlcConnected => !CurrentSettings.UseSimulator && _plc.IsConnected;
    public bool MqttConnected => _mqttOutbound.IsConnected;
    public int MqttPendingCount => _mqttOutbound.PendingCount;
    public string LastError => string.IsNullOrWhiteSpace(_plcError) ? _mqttOutbound.LastError : _plcError;
    public string LastPayload => _mqttOutbound.LastPayload;
    public string LastPublishNote { get; private set; } = string.Empty;
    public DateTimeOffset? LastPublishTime => _mqttOutbound.LastPublishTime;
    public double LastCycleElapsedMs { get; private set; }
    public double LastPlcElapsedMs { get; private set; }
    public double LastPublishElapsedMs => _mqttOutbound.LastPublishElapsedMs;
    public double LastWaitElapsedMs { get; private set; }
    public int ActiveScanIntervalMs { get; private set; }
    public DateTimeOffset? LastCycleCompletedAt { get; private set; }
    public long CycleCount { get; private set; }
    public IReadOnlyDictionary<string, TagSnapshot> LastSnapshots => _lastSnapshots;

    AppSettings CurrentSettings => _settingsStore.Current;

    public event EventHandler? ConnectionChanged;
    public event EventHandler<IReadOnlyList<TagSnapshot>>? TagsUpdated;

    public void RequestImmediatePublish() =>
        Interlocked.Increment(ref _forcePublishSignal);

    public async Task StartAsync()
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (IsRunning)
            {
                return;
            }

            var settings = CurrentSettings;
            _logger.LogInformation(
                "启动采集 line={LineName} simulator={Simulator} intervalMs={IntervalMs} plc={Plc} mqtt={Mqtt}",
                settings.LineName,
                settings.UseSimulator,
                settings.ScanIntervalMs,
                LogFormatting.DescribePlc(settings.Plc),
                LogFormatting.DescribeMqtt(settings.Mqtt, settings.LineName));

            if (CurrentSettings.UseSimulator && _plc.IsConnected)
            {
                await _plc.DisconnectAsync().ConfigureAwait(false);
            }

            _cts = new CancellationTokenSource();
            IsRunning = true;
            _plcError = string.Empty;
            _plcConnectRetryAfter = DateTimeOffset.MinValue;
            LastPublishNote = string.Empty;
            ResetPublishBaseline();
            _mqttOutbound.Start();
            _backgroundLease = _backgroundGuard.Begin();
            _loopThread = new Thread(() => RunLoop(_cts.Token))
            {
                IsBackground = true,
                Name = "AcquisitionLoop",
                Priority = ThreadPriority.AboveNormal
            };
            _loopThread.Start();
            ConnectionChanged?.Invoke(this, EventArgs.Empty);
            _logger.LogInformation("采集线程已启动");
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

            await _plc.DisconnectAsync().ConfigureAwait(false);

            if (_loopThread is not null)
            {
                if (_loopThread.IsAlive)
                {
                    _loopThread.Join(TimeSpan.FromSeconds(3));
                }

                _loopThread = null;
            }

            _cts?.Dispose();
            _cts = null;
            _backgroundLease?.Dispose();
            _backgroundLease = null;
            ResetPublishBaseline();
            _plcConnectRetryAfter = DateTimeOffset.MinValue;
            await _mqttOutbound.StopAsync().ConfigureAwait(false);
            ConnectionChanged?.Invoke(this, EventArgs.Empty);
            _logger.LogInformation("采集已停止 cycleCount={CycleCount}", CycleCount);
        }
        finally
        {
            _gate.Release();
        }
    }

    void RunLoop(CancellationToken cancellationToken)
    {
        var clock = ScanMonotonicClock.Create();

        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var cycleStartMs = clock.ElapsedMs;
                var intervalMs = Math.Clamp(CurrentSettings.ScanIntervalMs, 200, 60_000);
                ActiveScanIntervalMs = intervalMs;
                var targetNextMs = cycleStartMs + intervalMs;

                RunCycleAsync(CurrentSettings, cancellationToken).ConfigureAwait(false).GetAwaiter().GetResult();

                LastCycleElapsedMs = clock.ElapsedMs - cycleStartMs;
                LastCycleCompletedAt = DateTimeOffset.Now;
                CycleCount++;
                ConnectionChanged?.Invoke(this, EventArgs.Empty);

                var waitStartMs = clock.ElapsedMs;
                ScanIntervalDelay.WaitUntil(targetNextMs, clock, cancellationToken);
                LastWaitElapsedMs = clock.ElapsedMs - waitStartMs;

                _logger.LogDebug(
                    "采集周期 cycle={Cycle} intervalMs={IntervalMs} workMs={WorkMs:0} waitMs={WaitMs:0} pendingMqtt={Pending} plcConnected={PlcConnected} mqttConnected={MqttConnected} simulator={Simulator}",
                    CycleCount,
                    intervalMs,
                    LastCycleElapsedMs,
                    LastWaitElapsedMs,
                    MqttPendingCount,
                    PlcConnected,
                    MqttConnected,
                    CurrentSettings.UseSimulator);

                ConnectionChanged?.Invoke(this, EventArgs.Empty);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // 正常停止
        }
    }

    async Task RunCycleAsync(AppSettings settings, CancellationToken cancellationToken)
    {
        LastPlcElapsedMs = 0;

        try
        {
            var enabledTags = settings.Tags.Where(t => t.Enabled).ToList();
            var plcTags = enabledTags.Where(tag => !tag.IsManual).ToList();

            if (settings.UseSimulator)
            {
                if (_plc.IsConnected)
                {
                    await _plc.DisconnectAsync().ConfigureAwait(false);
                }
            }
            else if (!await TryEnsurePlcAsync(settings, cancellationToken).ConfigureAwait(false))
            {
                if (plcTags.Count > 0)
                {
                    PublishPlcTagFailures(plcTags, _plcError);
                    return;
                }
            }

            var plcStarted = Stopwatch.GetTimestamp();
            var snapshots = new List<TagSnapshot>();
            var values = new Dictionary<string, object?>(StringComparer.Ordinal);
            var allGood = true;
            IReadOnlyDictionary<string, object?> plcValues = new Dictionary<string, object?>(StringComparer.Ordinal);

            if (plcTags.Count > 0 && !settings.UseSimulator)
            {
                try
                {
                    plcValues = await _plc.ReadTagsAsync(plcTags, cancellationToken).ConfigureAwait(false);
                    _plcError = string.Empty;
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    allGood = false;
                    _plcError = $"PLC: {ex.Message}";
                    _logger.LogWarning(ex, "PLC 批量读取失败 tagCount={TagCount}", plcTags.Count);
                    await _plc.DisconnectAsync().ConfigureAwait(false);
                    _plcConnectRetryAfter = DateTimeOffset.UtcNow.AddSeconds(5);
                    PublishPlcTagFailures(plcTags, ex.Message, plcStarted);
                    return;
                }
            }

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
                    else if (!plcValues.TryGetValue(tag.Name, out var plcValue) || plcValue is null)
                    {
                        throw new InvalidOperationException($"未读取到点位 {tag.Name}。");
                    }
                    else
                    {
                        value = plcValue;
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
                    _plcError = $"点位 {tag.Name}: {ex.Message}";
                }
            }

            LastPlcElapsedMs = Stopwatch.GetElapsedTime(plcStarted).TotalMilliseconds;
            RememberSnapshots(snapshots);
            TagsUpdated?.Invoke(this, snapshots);

            if (values.Count > 0)
            {
                if (ShouldPublish(settings, enabledTags, values))
                {
                    var payload = MqttPayloadMapper.BuildPayload(settings, values, allGood);
                    var topic = settings.Mqtt.Topic.Replace("{deviceId}", settings.DeviceId, StringComparison.OrdinalIgnoreCase);
                    var tagsForPublish = enabledTags;
                    var valuesForPublish = values;
                    _mqttOutbound.Enqueue(new MqttOutboundItem
                    {
                        Topic = topic,
                        Payload = payload,
                        Qos = settings.Mqtt.Qos,
                        OnPublished = () => OnMqttPublished(tagsForPublish, valuesForPublish, allGood)
                    });
                    _logger.LogDebug(
                        "MQTT 入队 topic={Topic} bytes={Bytes} tagCount={TagCount} payload={Payload}",
                        topic,
                        payload.Length,
                        values.Count,
                        LogFormatting.Truncate(payload));
                    LastPublishNote = MqttPendingCount > 0
                        ? $"待发送 {MqttPendingCount} 条"
                        : string.Empty;
                }
                else
                {
                    LastPublishNote = BuildSkipNote(settings.TemperaturePublishThresholdC);
                    _logger.LogDebug("MQTT 跳过发布 reason={Reason}", LastPublishNote);
                }
            }

            ConnectionChanged?.Invoke(this, EventArgs.Empty);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _plcError = ex.Message;
            _logger.LogError(ex, "采集周期异常");
            ConnectionChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    void OnMqttPublished(IReadOnlyList<PlcTag> enabledTags, IReadOnlyDictionary<string, object?> values, bool allGood)
    {
        lock (_publishStateGate)
        {
            UpdatePublishedTemperatures(enabledTags, values);
            if (allGood)
            {
                _plcError = string.Empty;
            }
        }

        LastPublishNote = MqttPendingCount > 0
            ? $"待发送 {MqttPendingCount} 条"
            : string.Empty;
        ConnectionChanged?.Invoke(this, EventArgs.Empty);
    }

    async Task<bool> TryEnsurePlcAsync(AppSettings settings, CancellationToken cancellationToken)
    {
        if (settings.UseSimulator || _plc.IsConnected)
        {
            return true;
        }

        if (!settings.Tags.Any(t => t.Enabled && !t.IsManual))
        {
            return true;
        }

        if (DateTimeOffset.UtcNow < _plcConnectRetryAfter)
        {
            return false;
        }

        try
        {
            await _plc.ConnectAsync(settings.Plc, cancellationToken).ConfigureAwait(false);
            _plcConnectRetryAfter = DateTimeOffset.MinValue;
            _plcError = string.Empty;
            _logger.LogInformation("PLC 已连接 {Plc}", LogFormatting.DescribePlc(settings.Plc));
            return true;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _plcError = $"PLC: {ex.Message}";
            _plcConnectRetryAfter = DateTimeOffset.UtcNow.AddSeconds(5);
            _logger.LogWarning(ex, "PLC 连接失败 {Plc}", LogFormatting.DescribePlc(settings.Plc));
            await _plc.DisconnectAsync().ConfigureAwait(false);
            return false;
        }
    }

    void PublishPlcTagFailures(IReadOnlyList<PlcTag> plcTags, string error, long? plcStarted = null)
    {
        var snapshots = plcTags.Select(tag => new TagSnapshot
        {
            TagId = tag.Id,
            Name = tag.Name,
            Unit = tag.Unit,
            Quality = "Bad",
            Error = error,
            Timestamp = DateTimeOffset.Now
        }).ToList();

        if (plcStarted.HasValue)
        {
            LastPlcElapsedMs = Stopwatch.GetElapsedTime(plcStarted.Value).TotalMilliseconds;
        }

        RememberSnapshots(snapshots);
        TagsUpdated?.Invoke(this, snapshots);
        ConnectionChanged?.Invoke(this, EventArgs.Empty);
    }

    void ResetPublishBaseline()
    {
        lock (_publishStateGate)
        {
            _initialPublishDone = false;
            _lastPublishedTemperatures.Clear();
        }

        _lastSnapshots.Clear();
    }

    void RememberSnapshots(IReadOnlyList<TagSnapshot> snapshots)
    {
        foreach (var snapshot in snapshots)
        {
            _lastSnapshots[snapshot.TagId] = snapshot;
        }
    }

    bool ShouldPublish(AppSettings settings, IReadOnlyList<PlcTag> enabledTags, IReadOnlyDictionary<string, object?> values)
    {
        if (Interlocked.Exchange(ref _forcePublishSignal, 0) > 0)
        {
            return true;
        }

        var threshold = settings.TemperaturePublishThresholdC;
        if (threshold <= 0)
        {
            return true;
        }

        lock (_publishStateGate)
        {
            if (!_initialPublishDone)
            {
                return true;
            }

            var intervalMs = Math.Clamp(settings.ScanIntervalMs, 200, 60_000);
            if (LastPublishTime.HasValue &&
                (DateTimeOffset.Now - LastPublishTime.Value).TotalMilliseconds >= intervalMs)
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

    static object Simulate(PlcTag tag, int temperaturePrecision)
    {
        if (tag.IsManual)
        {
            return ValueFormatting.ResolveManualValue(tag);
        }

        var wave = DateTime.UtcNow.TimeOfDay.TotalSeconds;
        var phase = tag.Address * 0.35;
        if (RunStatusFormatting.IsRunStatusTag(tag))
        {
            return (int)((int)wave / 5) % 3;
        }

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

    public void Dispose()
    {
        _cts?.Cancel();
        _cts?.Dispose();
        _backgroundLease?.Dispose();
        _gate.Dispose();
    }
}
