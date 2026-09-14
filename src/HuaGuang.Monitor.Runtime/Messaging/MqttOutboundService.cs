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
    readonly Dictionary<string, string> _lastConnectErrors = new(StringComparer.Ordinal);
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

    /// <summary>所有已启用目标均已连接（多目标时比 <see cref="IsConnected"/> 更严格）。</summary>
    public bool AllEnabledTargetsConnected(AppSettings settings)
    {
        MqttEndpointCatalog.Normalize(settings);
        var enabled = MqttEndpointCatalog.GetEnabledPublishEndpoints(settings);
        if (enabled.Count == 0)
        {
            return false;
        }

        lock (_publisherGate)
        {
            foreach (var endpoint in enabled)
            {
                var desired = endpoint.ToSettings();
                if (!_publishers.TryGetValue(endpoint.Id, out var publisher)
                    || !publisher.IsConnected
                    || !publisher.MatchesConnection(desired))
                {
                    return false;
                }
            }
        }

        return true;
    }

    public string BuildTargetsStatus(AppSettings settings)
    {
        MqttEndpointCatalog.Normalize(settings);
        var enabled = MqttEndpointCatalog.GetEnabledPublishEndpoints(settings);
        if (enabled.Count == 0)
        {
            return "MQTT：无已启用目标";
        }

        var lines = new List<string> { $"MQTT 目标 {enabled.Count} 个（各自独立连接/发布）" };
        lock (_publisherGate)
        {
            foreach (var endpoint in enabled)
            {
                var desired = endpoint.ToSettings();
                _publishers.TryGetValue(endpoint.Id, out var publisher);
                var connected = publisher?.IsConnected == true && publisher.MatchesConnection(desired);
                var detail = connected
                    ? "已连接"
                    : _lastConnectErrors.TryGetValue(endpoint.Id, out var err) && !string.IsNullOrWhiteSpace(err)
                        ? err
                        : "尚未连接";
                lines.Add(
                    $"· {endpoint.Name} {desired.Host}:{desired.Port} clientId={desired.ClientId} user={desired.Username} → {detail}");
            }
        }

        return string.Join(Environment.NewLine, lines);
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

    public async Task ResetConnectionsAsync()
    {
        _connectRetryAfter.Clear();
        _lastConnectErrors.Clear();
        await DisconnectAllAsync().ConfigureAwait(false);
        StateChanged?.Invoke(this, EventArgs.Empty);
        _logger.LogInformation("MQTT 连接已重置（将按最新配置重新连接各目标）");
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
                _lastConnectErrors.Remove(id);
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
        MqttEndpointCatalog.ValidatePublishCredentials(endpoint);

        var desired = endpoint.ToSettings();
        var publisher = GetPublisher(endpoint.Id);
        if (publisher.IsConnected && publisher.MatchesConnection(desired))
        {
            _connectRetryAfter.Remove(endpoint.Id);
            _lastConnectErrors.Remove(endpoint.Id);
            return;
        }

        if (_connectRetryAfter.TryGetValue(endpoint.Id, out var retryAfter)
            && DateTimeOffset.UtcNow < retryAfter)
        {
            var detail = _lastConnectErrors.TryGetValue(endpoint.Id, out var lastError)
                ? lastError
                : "连接尚未成功";
            throw new InvalidOperationException($"MQTT 目标「{endpoint.Name}」暂未连接：{detail}");
        }

        try
        {
            _logger.LogInformation(
                "MQTT 连接尝试 target={TargetName} line={LineName} {Mqtt}",
                endpoint.Name,
                lineName,
                LogFormatting.DescribeMqtt(desired, lineName));
            publisher.ConnectAsync(desired, lineName, cancellationToken)
                .ConfigureAwait(false)
                .GetAwaiter()
                .GetResult();
            _connectRetryAfter.Remove(endpoint.Id);
            _lastConnectErrors.Remove(endpoint.Id);
            _logger.LogInformation(
                "MQTT 已连接 target={TargetName} line={LineName} {Mqtt}",
                endpoint.Name,
                lineName,
                LogFormatting.DescribeMqtt(desired, lineName));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _connectRetryAfter[endpoint.Id] = DateTimeOffset.UtcNow.AddSeconds(30);
            _lastConnectErrors[endpoint.Id] = ex.Message;
            _logger.LogWarning(
                ex,
                "MQTT 连接失败 target={TargetName} line={LineName} {Mqtt}",
                endpoint.Name,
                lineName,
                LogFormatting.DescribeMqtt(desired, lineName));
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
