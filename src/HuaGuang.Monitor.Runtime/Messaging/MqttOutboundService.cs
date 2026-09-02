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
    readonly IMqttPublisher _mqtt;
    readonly ILogger<MqttOutboundService> _logger;
    readonly object _queueGate = new();
    readonly Queue<MqttOutboundItem> _queue = new();
    readonly AutoResetEvent _signal = new(false);

    CancellationTokenSource? _cts;
    Thread? _workerThread;
    DateTimeOffset _connectRetryAfter = DateTimeOffset.MinValue;
    bool _isRunning;

    public MqttOutboundService(
        SettingsStore settingsStore,
        IMqttPublisher mqtt,
        ILogger<MqttOutboundService> logger)
    {
        _settingsStore = settingsStore;
        _mqtt = mqtt;
        _logger = logger;
        _mqtt.ConnectionChanged += (_, _) => StateChanged?.Invoke(this, EventArgs.Empty);
    }

    public bool IsConnected => _mqtt.IsConnected;

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
        _connectRetryAfter = DateTimeOffset.MinValue;
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

        await _mqtt.DisconnectAsync().ConfigureAwait(false);
        StateChanged?.Invoke(this, EventArgs.Empty);
        _logger.LogInformation("MQTT 发送线程已停止");
    }

    public void Enqueue(MqttOutboundItem item)
    {
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
                    EnsureConnected(settings, cancellationToken);
                    var started = Stopwatch.GetTimestamp();
                    _mqtt.PublishAsync(item.Topic, item.Payload, item.Qos, cancellationToken)
                        .ConfigureAwait(false)
                        .GetAwaiter()
                        .GetResult();

                    LastPublishElapsedMs = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
                    LastPayload = TruncatePayload(item.Payload);
                    LastPublishTime = DateTimeOffset.Now;
                    LastError = string.Empty;
                    item.OnPublished?.Invoke();
                    StateChanged?.Invoke(this, EventArgs.Empty);
                    _logger.LogDebug(
                        "MQTT 发布成功 topic={Topic} elapsedMs={ElapsedMs:0} bytes={Bytes}",
                        item.Topic,
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
                    _connectRetryAfter = DateTimeOffset.UtcNow.AddSeconds(30);
                    _logger.LogWarning(
                        ex,
                        "MQTT 发布失败 topic={Topic} pending={Pending}",
                        item.Topic,
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

    void EnsureConnected(AppSettings settings, CancellationToken cancellationToken)
    {
        if (_mqtt.IsConnected)
        {
            _connectRetryAfter = DateTimeOffset.MinValue;
            return;
        }

        if (DateTimeOffset.UtcNow < _connectRetryAfter)
        {
            throw new InvalidOperationException("MQTT 暂未连接，稍后重试。");
        }

        try
        {
            _mqtt.ConnectAsync(settings.Mqtt, settings.LineName, cancellationToken).ConfigureAwait(false).GetAwaiter().GetResult();
            _connectRetryAfter = DateTimeOffset.MinValue;
            _logger.LogInformation(
                "MQTT 已连接 line={LineName} {Mqtt}",
                settings.LineName,
                LogFormatting.DescribeMqtt(settings.Mqtt, settings.LineName));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _connectRetryAfter = DateTimeOffset.UtcNow.AddSeconds(30);
            _logger.LogWarning(ex, "MQTT 连接失败 line={LineName}", settings.LineName);
            throw;
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
