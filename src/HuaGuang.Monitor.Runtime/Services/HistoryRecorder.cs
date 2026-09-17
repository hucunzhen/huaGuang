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
        _acquisition.TagsUpdated += OnAcquisitionTagsUpdated;
        _subscription.TelemetryReceived += OnTelemetryReceived;
    }

    public async Task InitializeAsync()
    {
        await _store.InitializeAsync().ConfigureAwait(false);
        await PruneIfNeededAsync().ConfigureAwait(false);
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
            PlcHost = settings.Plc.Host,
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
            _settings.Current.Tags,
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

    public async Task PruneIfNeededAsync()
    {
        var days = Math.Clamp(_settings.Current.HistoryRetentionDays, 1, 365);
        var cutoff = DateTimeOffset.Now.AddDays(-days);
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

        _cts.Dispose();
    }
}

public sealed class RemoteTelemetryEventArgs : EventArgs
{
    public required RemoteDeviceState Device { get; init; }
    public required string PayloadJson { get; init; }
}
