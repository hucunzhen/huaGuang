using System.Net;
using HuaGuang.Monitor.Models;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace HuaGuang.Monitor.Services;

/// <summary>
/// 订阅服务上的只读网页。采集实例不监听。可以查看和导出历史，没有启停、改点接口。
/// </summary>
public sealed class SubscribeWebHost : BackgroundService
{
    public const int Port = 8088;

    readonly SettingsStore _settings;
    readonly SubscriptionService _subscription;
    readonly HistoryStore _history;
    readonly ILogger<SubscribeWebHost> _logger;
    readonly SemaphoreSlim _exportGate = new(1, 1);
    readonly SemaphoreSlim _requestGate = new(8, 8);

    public SubscribeWebHost(
        SettingsStore settings,
        SubscriptionService subscription,
        HistoryStore history,
        ILogger<SubscribeWebHost> logger)
    {
        _settings = settings;
        _subscription = subscription;
        _history = history;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Yield();
        _settings.TryLoad();
        while (!stoppingToken.IsCancellationRequested)
        {
            if (!IsSubscribeMode())
            {
                await DelayAsync(TimeSpan.FromSeconds(5), stoppingToken).ConfigureAwait(false);
                continue;
            }

            var listener = new HttpListener();
            listener.Prefixes.Add($"http://+:{Port}/");
            try
            {
                listener.Start();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "订阅网页未能监听 {Port}，5 秒后重试", Port);
                listener.Close();
                await DelayAsync(TimeSpan.FromSeconds(5), stoppingToken).ConfigureAwait(false);
                continue;
            }

            _logger.LogInformation("订阅只读网页 http://<工控机>:{Port}/", Port);
            try
            {
                await ServeAsync(listener, stoppingToken).ConfigureAwait(false);
            }
            finally
            {
                listener.Close();
            }
        }
    }

    async Task ServeAsync(HttpListener listener, CancellationToken stoppingToken)
    {
        using var stop = stoppingToken.Register(() => listener.Close());
        while (!stoppingToken.IsCancellationRequested && IsSubscribeMode())
        {
            HttpListenerContext context;
            try
            {
                context = await listener.GetContextAsync().ConfigureAwait(false);
            }
            catch (Exception) when (stoppingToken.IsCancellationRequested || !listener.IsListening)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "订阅网页接受连接失败");
                continue;
            }

            var model = CreateModel();
            _ = Task.Run(async () =>
            {
                var acquired = false;
                try
                {
                    acquired = await _requestGate.WaitAsync(TimeSpan.FromSeconds(8)).ConfigureAwait(false);
                    if (!acquired)
                    {
                        try
                        {
                            context.Response.StatusCode = 503;
                            context.Response.Close();
                        }
                        catch
                        {
                        }

                        return;
                    }

                    await SubscribeWebHttp.HandleAsync(context, model, _exportGate, stoppingToken)
                        .ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "订阅网页请求处理失败");
                    try
                    {
                        context.Response.Abort();
                    }
                    catch
                    {
                    }
                }
                finally
                {
                    if (acquired)
                    {
                        _requestGate.Release();
                    }
                }
            }, CancellationToken.None);
        }
    }

    SubscribeWebReadModel CreateModel() =>
        new()
        {
            Status = detailDeviceId => SubscribeWebSnapshot.Create(
                _settings.Current,
                _subscription.IsRunning,
                _subscription.IsConnected,
                _subscription.LastError,
                _subscription.GetDevicesForWeb(detailDeviceId),
                detailDeviceId),
            Settings = () => _settings.Current,
            ReloadSettings = () =>
            {
                _settings.TryLoad();
                return _settings.Current;
            },
            History = _history,
            Logger = _logger
        };

    bool IsSubscribeMode()
    {
        try
        {
            if (_settings.Current.OperationMode == AppOperationMode.Subscribe)
            {
                return true;
            }

            if (string.IsNullOrWhiteSpace(_settings.Current.LineName))
            {
                _settings.TryLoad();
            }

            return _settings.Current.OperationMode == AppOperationMode.Subscribe
                || MonitorProcessInstance.IsSubscribeDashboardHost;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "读取订阅模式失败");
            return MonitorProcessInstance.IsSubscribeDashboardHost;
        }
    }

    static async Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
    }
}
