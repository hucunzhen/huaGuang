using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace HuaGuang.Monitor.Services.Watchdog;

/// <summary>在 Generic Host（采集服务、守护服务）内写心跳。</summary>
public sealed class WatchdogRoleHeartbeatWorker : BackgroundService
{
    readonly string _role;
    readonly TimeSpan _interval;
    readonly ILogger<WatchdogRoleHeartbeatWorker> _logger;

    public WatchdogRoleHeartbeatWorker(string role, ILogger<WatchdogRoleHeartbeatWorker> logger, TimeSpan? interval = null)
    {
        _role = role;
        _logger = logger;
        _interval = interval ?? TimeSpan.FromSeconds(15);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                WatchdogStateStore.WriteHeartbeat(_role);
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "写入守护心跳失败 role={Role}", _role);
            }

            try
            {
                await Task.Delay(_interval, stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    public override Task StopAsync(CancellationToken cancellationToken)
    {
        try
        {
            WatchdogStateStore.MarkGracefulShutdown(_role);
        }
        catch
        {
        }

        return base.StopAsync(cancellationToken);
    }
}
