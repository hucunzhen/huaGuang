using System.Diagnostics;
using HuaGuang.Monitor.Models;
using HuaGuang.Monitor.Services;
using HuaGuang.Monitor.Services.Logging;
using Microsoft.Extensions.Logging;

namespace HuaGuang.Monitor.Messaging;

/// <summary>
/// 后台发送 MQTT，采集线程只入队，连接/发布失败时缓存待重试。
/// </summary>
public sealed class MqttOutboundService : IDisposable
{
    const int MaxPending = 128;

    readonly SettingsStore _settingsStore;
    readonly ILogger<MqttOutboundService> _logger;
    readonly ILoggerFactory _loggerFactory;
    readonly object _queueGate = new();
    readonly object _publisherGate = new();
    readonly Dictionary<string, MqttPublisher> _publishers = new(StringComparer.Ordinal);
    readonly Dictionary<string, DateTimeOffset> _connectRetryAfter = new(StringComparer.Ordinal);
    readonly Queue<MqttOutboundItem> _queue = new();
    readonly AutoResetEvent _signal = new(false);

    CancellationTokenSource? _cts;
    Thread? _workerThread;
    bool _isRunning;

    public MqttOutboundService(
        SettingsStore settingsStore,
        ILoggerFactory loggerFactory,
        ILogger<MqttOutboundService> logger)
    {
        _settingsStore = settingsStore;
        _loggerFactory = loggerFactory;
        _logger = logger;
    }

    public bool IsConnected
    {
        get
        {
            lock (_publisherGate)
            {
                return _publishers.Values.Any(publisher => publisher.IsConnected);
            }
        }
    }

    public int PendingCount
    {
        get
        {
            lock (_queueGate)
            {
                return _queue.Count;
            }
        }
    }

    public string LastError { get; private set; } = string.Empty;
    public string LastPayload { get; private set; } = string.Empty;
    public DateTimeOffset? LastPublishTime { get; private set; }
    public double LastPublishElapsedMs { get; private set; }

    public event EventHandler? StateChanged;

    public void Start()
    {
        if (_isRunning)
        {
            return;
        }

        _cts = new CancellationTokenSource();
        _isRunning = true;
        _connectRetryAfter.Clear();
        _workerThread = new Thread(() => RunWorker(_cts.Token))
        {
            IsBackground = true,
            Name = "MqttOutbound",
            Priority = ThreadPriority.Normal
        };
        _workerThread.Start();
        _logger.LogInformation("MQTT 发送线程已启动");
    }

    public async Task StopAsync()
    {
        if (!_isRunning)
        {
            return;
        }

        _isRunning = false;
        if (_cts is not null)
        {
            await _cts.CancelAsync().ConfigureAwait(false);
        }

        _signal.Set();
        if (_workerThread is { IsAlive: true })
        {
            _workerThread.Join(TimeSpan.FromSeconds(5));
        }

        _workerThread = null;
        _cts?.Dispose();
        _cts = null;

        lock (_queueGate)
        {
            _queue.Clear();
        }

        await DisconnectAllAsync().ConfigureAwait(false);
        StateChanged?.Invoke(this, EventArgs.Empty);
        _logger.LogInformation("MQTT 发送线程已停止");
    }

    public void Enqueue(MqttOutboundItem item)
    {
        if (item.Targets.Count == 0)
        {
            return;
        }

        lock (_queueGate)
        {
            _queue.Enqueue(item);
            while (_queue.Count > MaxPending)
            {
                _queue.Dequeue();
            }
        }

        _signal.Set();
    }

    void RunWorker(CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                if (!TryDequeue(out var item))
                {
                    _signal.WaitOne(500);
                    continue;
                }

                try
                {
                    var settings = _settingsStore.Current;
                    MqttEndpointCatalog.Normalize(settings);
                    PrunePublishers(settings.MqttEndpoints.Select(endpoint => endpoint.Id));

                    var started = Stopwatch.GetTimestamp();
                    foreach (var target in item.Targets)
                    {
                        var endpoint = settings.MqttEndpoints.FirstOrDefault(e => e.Id == target.EndpointId)
                                       ?? throw new InvalidOperationException($"找不到 MQTT 目标 {target.EndpointId}。");

                        EnsureConnected(endpoint, settings.LineName, cancellationToken);
                        var publisher = GetPublisher(endpoint.Id);
                        publisher.PublishAsync(target.Topic, item.Payload, target.Qos, cancellationToken)
                            .ConfigureAwait(false)
                            .GetAwaiter()
                            .GetResult();
                    }

                    LastPublishElapsedMs = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
                    LastPayload = TruncatePayload(item.Payload);
                    LastPublishTime = DateTimeOffset.Now;
                    LastError = string.Empty;
                    item.OnPublished?.Invoke();
                    StateChanged?.Invoke(this, EventArgs.Empty);
                    _logger.LogDebug(
                        "MQTT 发布成功 targets={TargetCount} elapsedMs={ElapsedMs:0} bytes={Bytes}",
                        item.Targets.Count,
                        LastPublishElapsedMs,
                        item.Payload.Length);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    RequeueFront(item);
                    break;
                }
                catch (Exception ex)
                {
                    LastError = $"MQTT: {ex.Message}";
                    _logger.LogWarning(
                        ex,
                        "MQTT 发布失败 targets={TargetCount} pending={Pending}",
                        item.Targets.Count,
                        PendingCount);
                    RequeueFront(item);
                    StateChanged?.Invoke(this, EventArgs.Empty);
                    _signal.WaitOne(1000);
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // 正常停止
        }
    }

    bool TryDequeue(out MqttOutboundItem item)
    {
        lock (_queueGate)
        {
            if (_queue.Count == 0)
            {
                item = null!;
                return false;
            }

            item = _queue.Dequeue();
            return true;
        }
    }

    void RequeueFront(MqttOutboundItem item)
    {
        lock (_queueGate)
        {
            var pending = _queue.ToList();
            _queue.Clear();
            _queue.Enqueue(item);
            foreach (var existing in pending)
            {
                _queue.Enqueue(existing);
            }
        }
    }

    MqttPublisher GetPublisher(string endpointId)
    {
        lock (_publisherGate)
        {
            if (!_publishers.TryGetValue(endpointId, out var publisher))
            {
                publisher = new MqttPublisher(_loggerFactory.CreateLogger<MqttPublisher>());
                publisher.ConnectionChanged += (_, _) => StateChanged?.Invoke(this, EventArgs.Empty);
                _publishers[endpointId] = publisher;
            }

            return publisher;
        }
    }

    void PrunePublishers(IEnumerable<string> activeEndpointIds)
    {
        var active = activeEndpointIds.ToHashSet(StringComparer.Ordinal);
        List<MqttPublisher>? removed = null;
        lock (_publisherGate)
        {
            foreach (var id in _publishers.Keys.Where(id => !active.Contains(id)).ToList())
            {
                if (_publishers.Remove(id, out var publisher))
                {
                    removed ??= [];
                    removed.Add(publisher);
                }

                _connectRetryAfter.Remove(id);
            }
        }

        if (removed is null)
        {
            return;
        }

        foreach (var publisher in removed)
        {
            publisher.DisconnectAsync().ConfigureAwait(false).GetAwaiter().GetResult();
            publisher.DisposeAsync().AsTask().ConfigureAwait(false).GetAwaiter().GetResult();
        }
    }

    void EnsureConnected(MqttEndpoint endpoint, string lineName, CancellationToken cancellationToken)
    {
        var publisher = GetPublisher(endpoint.Id);
        if (publisher.IsConnected)
        {
            _connectRetryAfter.Remove(endpoint.Id);
            return;
        }

        if (_connectRetryAfter.TryGetValue(endpoint.Id, out var retryAfter)
            && DateTimeOffset.UtcNow < retryAfter)
        {
            throw new InvalidOperationException($"MQTT 目标「{endpoint.Name}」暂未连接，稍后重试。");
        }

        try
        {
            publisher.ConnectAsync(endpoint.ToSettings(), lineName, cancellationToken)
                .ConfigureAwait(false)
                .GetAwaiter()
                .GetResult();
            _connectRetryAfter.Remove(endpoint.Id);
            _logger.LogInformation(
                "MQTT 已连接 target={TargetName} line={LineName} {Mqtt}",
                endpoint.Name,
                lineName,
                LogFormatting.DescribeMqtt(endpoint.ToSettings(), lineName));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _connectRetryAfter[endpoint.Id] = DateTimeOffset.UtcNow.AddSeconds(30);
            _logger.LogWarning(ex, "MQTT 连接失败 target={TargetName} line={LineName}", endpoint.Name, lineName);
            throw;
        }
    }

    async Task DisconnectAllAsync()
    {
        List<MqttPublisher> publishers;
        lock (_publisherGate)
        {
            publishers = _publishers.Values.ToList();
            _publishers.Clear();
            _connectRetryAfter.Clear();
        }

        foreach (var publisher in publishers)
        {
            await publisher.DisconnectAsync().ConfigureAwait(false);
            await publisher.DisposeAsync().ConfigureAwait(false);
        }
    }

    static string TruncatePayload(string payload) =>
        payload.Length <= 4096
            ? payload
            : payload[..4096] + "…";

    public void Dispose()
    {
        _cts?.Cancel();
        _signal.Dispose();
        StopAsync().GetAwaiter().GetResult();
    }
}
