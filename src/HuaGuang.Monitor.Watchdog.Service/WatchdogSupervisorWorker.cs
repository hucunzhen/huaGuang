using HuaGuang.Monitor.Services;
using HuaGuang.Monitor.Services.Watchdog;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace HuaGuang.Monitor.Watchdog.Service;

public sealed class WatchdogSupervisorWorker : BackgroundService
{
    readonly WatchdogSupervisor _supervisor;
    readonly ILogger<WatchdogSupervisorWorker> _logger;

    public WatchdogSupervisorWorker(WatchdogSupervisor supervisor, ILogger<WatchdogSupervisorWorker> logger)
    {
        _supervisor = supervisor;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("工业监控守护服务已启动 installRoot={InstallRoot}", WatchdogSupervisor.ResolveInstallRoot());

        while (!stoppingToken.IsCancellationRequested)
        {
            var options = WatchdogOptions.Load();
            try
            {
                await _supervisor.RunCycleAsync(stoppingToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex, "守护巡检失败");
            }

            var delaySeconds = Math.Clamp(options.PollIntervalSeconds, 5, 300);
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(delaySeconds), stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }
}
