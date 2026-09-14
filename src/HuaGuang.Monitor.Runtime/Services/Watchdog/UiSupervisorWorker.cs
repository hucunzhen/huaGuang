using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace HuaGuang.Monitor.Services.Watchdog;

/// <summary>
/// 当未安装独立守护 Windows 服务时，由采集服务负责拉起 UI。
/// </summary>
public sealed class UiSupervisorWorker : BackgroundService
{
    readonly WatchdogSupervisor _supervisor;
    readonly ILogger<UiSupervisorWorker> _logger;

    public UiSupervisorWorker(WatchdogSupervisor supervisor, ILogger<UiSupervisorWorker> logger)
    {
        _supervisor = supervisor;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation(
            "UI 守护（采集服务内嵌）已启用 installRoot={InstallRoot}",
            WatchdogSupervisor.ResolveInstallRoot());

        while (!stoppingToken.IsCancellationRequested)
        {
            var options = WatchdogOptions.Load();
            try
            {
                if (!WatchdogWindowsServiceHelper.IsDedicatedWatchdogRunning())
                {
                    await _supervisor.RunUiCycleAsync(stoppingToken).ConfigureAwait(false);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex, "UI 守护巡检失败");
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
