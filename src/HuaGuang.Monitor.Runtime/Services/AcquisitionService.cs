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
    DateTimeOffset? _lastPublishScheduleTime;
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
    public bool MqttConnected => _mqttOutbound.AllEnabledTargetsConnected(CurrentSettings);

    public string MqttTargetsStatus => _mqttOutbound.BuildTargetsStatus(CurrentSettings);
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
    public int ActivePublishIntervalMs { get; private set; }
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

            await EnsureLoopThreadStoppedAsync().ConfigureAwait(false);

            await _settingsStore.LoadAsyncIfChanged().ConfigureAwait(false);
            var settings = CurrentSettings;
            MqttEndpointCatalog.Normalize(settings);
            var excelPath = LineConfigPaths.GetLineExcelPath(settings.LineName);
            _logger.LogInformation(
                "启动采集 line={LineName} excel={ExcelPath} simulator={Simulator} scanMs={ScanMs} publishMs={PublishMs} plc={Plc} mqtt={Mqtt} mqttTargets={TargetCount}",
                settings.LineName,
                excelPath,
                settings.UseSimulator,
                settings.ScanIntervalMs,
                settings.PublishIntervalMs,
                LogFormatting.DescribePlc(settings.Plc),
                LogFormatting.DescribeMqtt(settings.Mqtt, settings.LineName),
                settings.MqttEndpoints.Count);
            foreach (var endpoint in settings.MqttEndpoints.Where(endpoint => endpoint.Enabled))
            {
                _logger.LogInformation(
                    "启动采集 MQTT 目标 name={TargetName} {Mqtt}",
                    endpoint.Name,
                    LogFormatting.DescribeMqtt(endpoint.ToSettings(), settings.LineName));
            }

            await _mqttOutbound.ResetConnectionsAsync().ConfigureAwait(false);

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
            MonitorRuntimeOperatorControl.SetPausedByOperator(false);
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

            await EnsureLoopThreadStoppedAsync().ConfigureAwait(false);

            await _plc.DisconnectAsync().ConfigureAwait(false);
            _backgroundLease?.Dispose();
            _backgroundLease = null;
            ResetPublishBaseline();
            _plcConnectRetryAfter = DateTimeOffset.MinValue;
            await _mqttOutbound.StopAsync().ConfigureAwait(false);
            MonitorRuntimeOperatorControl.SetPausedByOperator(true);
            ConnectionChanged?.Invoke(this, EventArgs.Empty);
            _logger.LogInformation("采集已停止 cycleCount={CycleCount}", CycleCount);
        }
        finally
        {
            _gate.Release();
        }
    }

    async Task EnsureLoopThreadStoppedAsync()
    {
        var thread = _loopThread;
        if (thread is null)
        {
            return;
        }

        if (thread.IsAlive)
        {
            if (_cts is not null && !_cts.IsCancellationRequested)
            {
                await _cts.CancelAsync().ConfigureAwait(false);
            }

            if (!thread.Join(TimeSpan.FromSeconds(5)))
            {
                _logger.LogWarning("采集线程未在 5 秒内结束，PLC 连接可能处于异常状态");
            }
        }

        _loopThread = null;
        _cts?.Dispose();
        _cts = null;
    }

    void RunLoop(CancellationToken cancellationToken)
    {
        var clock = ScanMonotonicClock.Create();

        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var cycleStartMs = clock.ElapsedMs;
                var intervalMs = AcquisitionTiming.ResolveScanIntervalMs(CurrentSettings);
                ActiveScanIntervalMs = intervalMs;
                ActivePublishIntervalMs = AcquisitionTiming.ResolvePublishIntervalMs(CurrentSettings);
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
                plcValues = await _plc.ReadTagsAsync(plcTags, cancellationToken).ConfigureAwait(false);
                if (plcValues.Count == 0 && plcTags.Count > 0)
                {
                    allGood = false;
                    _plcError = "PLC: 所有点位读取均失败，请核对 S7 地址与 DB 是否存在于 PLC。";
                    _logger.LogWarning("PLC 批量读取无有效数据 tagCount={TagCount}", plcTags.Count);
                }
                else
                {
                    _plcError = string.Empty;
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
                    MqttEndpointCatalog.Normalize(settings);
                    var endpoints = MqttEndpointCatalog.GetEnabledPublishEndpoints(settings);
                    if (endpoints.Count == 0)
                    {
                        LastPublishNote = "未发布：没有已启用的 MQTT 目标";
                    }
                    else
                    {
                        var payload = MqttPayloadMapper.BuildPayload(settings, values, allGood);
                        var targets = endpoints.Select(endpoint => new MqttPublishTarget
                        {
                            EndpointId = endpoint.Id,
                            Topic = MqttEndpointCatalog.ResolveTopic(endpoint, settings),
                            Qos = endpoint.Qos
                        }).ToList();
                        var tagsForPublish = enabledTags;
                        var valuesForPublish = values;
                        lock (_publishStateGate)
                        {
                            _lastPublishScheduleTime = DateTimeOffset.Now;
                        }

                        for (var i = 0; i < targets.Count; i++)
                        {
                            var target = targets[i];
                            _mqttOutbound.Enqueue(new MqttOutboundItem
                            {
                                Payload = payload,
                                Targets = [target],
                                OnPublished = i == targets.Count - 1
                                    ? () => OnMqttPublished(tagsForPublish, valuesForPublish, allGood)
                                    : null
                            });
                        }

                        _logger.LogDebug(
                            "MQTT 入队 targets={TargetCount} bytes={Bytes} tagCount={TagCount} payload={Payload}",
                            targets.Count,
                            payload.Length,
                            values.Count,
                            LogFormatting.Truncate(payload));
                        LastPublishNote = MqttPendingCount > 0
                            ? $"待发送 {MqttPendingCount} 条"
                            : string.Empty;
                    }
                }
                else
                {
                    LastPublishNote = BuildSkipNote(settings);
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
            PlcSettingsHelper.Normalize(settings.Plc);
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
            _lastPublishScheduleTime = null;
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
        var publishIntervalMs = AcquisitionTiming.ResolvePublishIntervalMs(settings);

        lock (_publishStateGate)
        {
            if (!_lastPublishScheduleTime.HasValue)
            {
                return true;
            }

            if (IsPublishIntervalElapsed(publishIntervalMs))
            {
                return true;
            }

            if (threshold <= 0)
            {
                return false;
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

    bool IsPublishIntervalElapsed(int publishIntervalMs) =>
        !_lastPublishScheduleTime.HasValue ||
        (DateTimeOffset.Now - _lastPublishScheduleTime.Value).TotalMilliseconds >= publishIntervalMs;

    void UpdatePublishedTemperatures(IReadOnlyList<PlcTag> enabledTags, IReadOnlyDictionary<string, object?> values)
    {
        foreach (var tag in enabledTags.Where(t => t.IsTemperature && !t.IsManual))
        {
            if (values.TryGetValue(tag.Name, out var value) && ValueFormatting.TryAsDouble(value, out var current))
            {
                _lastPublishedTemperatures[tag.Id] = current;
            }
        }
    }

    static string BuildSkipNote(AppSettings settings)
    {
        var publishSeconds = AcquisitionTiming.ResolvePublishIntervalMs(settings) / 1000.0;
        if (settings.TemperaturePublishThresholdC <= 0)
        {
            return $"未发布：距上次发布未满 {publishSeconds:G} 秒";
        }

        return $"未发布：温度变化未达 {settings.TemperaturePublishThresholdC:G}℃ 且未满 {publishSeconds:G} 秒发布周期";
    }

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
