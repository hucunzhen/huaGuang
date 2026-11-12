using System.Threading.Channels;
using HuaGuang.Monitor.Models;
using Microsoft.Extensions.Logging;

namespace HuaGuang.Monitor.Services;

public sealed class HistoryRecorder : IAsyncDisposable
{
    const int MaxPayloadLength = 4096;

    readonly HistoryStore _store;
    readonly SettingsStore _settings;
    readonly AcquisitionService _acquisition;
    readonly SubscriptionService _subscription;
    readonly ILogger<HistoryRecorder> _logger;
    readonly Channel<HistorySampleWriteRequest> _channel;
    readonly CancellationTokenSource _cts = new();
    readonly Task _writer;
    readonly Task _pruneLoop;

    public HistoryRecorder(
        HistoryStore store,
        SettingsStore settings,
        AcquisitionService acquisition,
        SubscriptionService subscription,
        ILogger<HistoryRecorder> logger)
    {
        _store = store;
        _settings = settings;
        _acquisition = acquisition;
        _subscription = subscription;
        _logger = logger;
        _channel = Channel.CreateUnbounded<HistorySampleWriteRequest>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false
        });
        _writer = Task.Run(WriteLoopAsync);
        _pruneLoop = Task.Run(PruneLoopAsync);
        _acquisition.TagsUpdated += OnAcquisitionTagsUpdated;
        _subscription.TelemetryReceived += OnTelemetryReceived;
    }

    public async Task InitializeAsync()
    {
        await ApplyStorageLocationAsync().ConfigureAwait(false);
        await PruneIfNeededAsync().ConfigureAwait(false);
    }

    public async Task ApplyStorageLocationAsync()
    {
        var (shard, legacy) = AppPaths.ResolveHistoryLocation(_settings.Current.HistoryDirectory);
        _store.Relocate(shard, legacy);
        await _store.InitializeAsync().ConfigureAwait(false);
        _logger.LogInformation("历史数据目录 shard={Shard} legacy={Legacy}", shard, legacy);
    }

    void OnAcquisitionTagsUpdated(object? sender, IReadOnlyList<TagSnapshot> snapshots)
    {
        if (!ShouldRecord() || snapshots.Count == 0)
        {
            return;
        }

        var settings = _settings.Current;
        Enqueue(new HistorySampleWriteRequest
        {
            RecordedAt = DateTimeOffset.Now,
            DeviceId = settings.DeviceId,
            OperationMode = AppOperationMode.Acquisition,
            Quality = snapshots.All(snapshot => snapshot.Quality == "Good") ? "Good" : "Bad",
            PlcHost = PlcEndpointCatalog.DescribeHosts(settings),
            Simulator = settings.UseSimulator,
            PayloadJson = TruncatePayload(_acquisition.LastPayload),
            Tags = snapshots
        });
    }

    void OnTelemetryReceived(object? sender, RemoteTelemetryEventArgs e)
    {
        if (!ShouldRecord())
        {
            return;
        }

        var snapshots = HistoryTagNameResolver.CreateSubscribeSnapshots(
            e.Device.Tags,
            SubscribeLineCatalog.ResolveTags(e.Device.DeviceId, e.Device.SourceTopic, _settings.Current.Tags),
            _settings.Current.MqttPayload ?? new MqttPayloadProfile(),
            e.Device.Quality,
            e.Device.Timestamp);
        if (snapshots.Count == 0)
        {
            return;
        }

        Enqueue(new HistorySampleWriteRequest
        {
            RecordedAt = e.Device.ReceivedAt,
            SourceTimestamp = e.Device.Timestamp,
            DeviceId = e.Device.DeviceId,
            SourceTopic = e.Device.SourceTopic,
            OperationMode = AppOperationMode.Subscribe,
            Quality = e.Device.Quality,
            PlcHost = e.Device.PlcHost,
            Simulator = e.Device.Simulator,
            PayloadJson = TruncatePayload(e.PayloadJson),
            Tags = snapshots
        });
    }

    void Enqueue(HistorySampleWriteRequest request)
    {
        _ = _channel.Writer.TryWrite(request);
    }

    async Task WriteLoopAsync()
    {
        var reader = _channel.Reader;
        var token = _cts.Token;
        while (await reader.WaitToReadAsync(token).ConfigureAwait(false))
        {
            while (reader.TryRead(out var request))
            {
                try
                {
                    await _store.AppendAsync(request).ConfigureAwait(false);
                }
                catch
                {
                    // 历史写入失败不影响采集
                }
            }
        }
    }

    async Task PruneLoopAsync()
    {
        var token = _cts.Token;
        while (!token.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(DelayUntilAfterLocalMidnight(DateTimeOffset.Now), token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }

            await PruneIfNeededAsync().ConfigureAwait(false);
        }
    }

    public async Task PruneIfNeededAsync()
    {
        var days = Math.Clamp(_settings.Current.HistoryRetentionDays, 1, 365);
        var cutoff = RetentionCutoff(DateTimeOffset.Now, days);
        try
        {
            var deleted = await _store.PruneOlderThanAsync(cutoff).ConfigureAwait(false);
            if (deleted > 0)
            {
                _logger.LogInformation(
                    "历史记录已清理 deleted={Deleted} retentionDays={Days} cutoff={Cutoff:O}",
                    deleted,
                    days,
                    cutoff);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "历史记录清理失败（服务继续运行）retentionDays={Days} cutoff={Cutoff:O}",
                days,
                cutoff);
        }
    }

    /// <summary>
    /// 保留 <paramref name="retentionDays"/> 个完整自然日，再加今天。
    /// 保留 1 天时，截止点是昨天 0 点，因此只删除 2 天以前的分日文件。
    /// </summary>
    internal static DateTimeOffset RetentionCutoff(DateTimeOffset now, int retentionDays)
    {
        var days = Math.Clamp(retentionDays, 1, 365);
        var oldestKept = now.ToLocalTime().Date.AddDays(-days);
        return new DateTimeOffset(oldestKept, TimeZoneInfo.Local.GetUtcOffset(oldestKept));
    }

    internal static TimeSpan DelayUntilAfterLocalMidnight(DateTimeOffset now)
    {
        var local = now.ToLocalTime();
        var next = local.Date.AddDays(1).AddMinutes(1);
        var delay = next - local.DateTime;
        return delay < TimeSpan.FromSeconds(1) ? TimeSpan.FromDays(1) : delay;
    }

    bool ShouldRecord() => _settings.Current.EnableHistoryRecording;

    static string TruncatePayload(string payload) =>
        string.IsNullOrWhiteSpace(payload)
            ? string.Empty
            : payload.Length <= MaxPayloadLength
                ? payload
                : payload[..MaxPayloadLength] + "…";

    public async ValueTask DisposeAsync()
    {
        _acquisition.TagsUpdated -= OnAcquisitionTagsUpdated;
        _subscription.TelemetryReceived -= OnTelemetryReceived;
        _channel.Writer.TryComplete();
        _cts.Cancel();
        try
        {
            await _writer.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }

        try
        {
            await _pruneLoop.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }

        _cts.Dispose();
    }
}

public sealed class RemoteTelemetryEventArgs : EventArgs
{
    public required RemoteDeviceState Device { get; init; }
    public required string PayloadJson { get; init; }
}
