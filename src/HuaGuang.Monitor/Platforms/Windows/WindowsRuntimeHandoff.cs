using HuaGuang.Monitor.Ipc;
using HuaGuang.Monitor.Models;
using HuaGuang.Monitor.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace HuaGuang.Monitor.Platforms.Windows;

static class WindowsRuntimeHandoff
{
    static int _handoffAttempted;

    public static void TryHandoffAcquisitionToService()
    {
        if (Interlocked.Exchange(ref _handoffAttempted, 1) != 0)
        {
            return;
        }

        var services = MauiProgram.Services;
        var settings = services.GetService<SettingsStore>();
        if (settings?.Current.OperationMode != AppOperationMode.Acquisition)
        {
            return;
        }

        var acquisition = services.GetService<IMonitorAcquisition>();
        if (acquisition?.IsRunning != true)
        {
            return;
        }

        var logger = services.GetService<ILoggerFactory>()?.CreateLogger("WindowsRuntimeHandoff");
        var launcher = services.GetService<IBackgroundRuntimeLauncher>();
        if (launcher?.EnsureRunning(TimeSpan.FromSeconds(12)) != true)
        {
            if (WindowsServiceHelper.IsMonitorServiceRunning())
            {
                logger?.LogWarning(
                    "Windows 服务「工业监控采集服务」正在运行，但界面无法连接后台 IPC。请更新 service 目录下的服务程序并重启该服务。");
            }
            else
            {
                logger?.LogWarning("界面关闭时后台采集进程不可用，MQTT 将随界面退出而停止");
            }

            return;
        }

        var local = services.GetService<AcquisitionService>();
        try
        {
            if (local?.IsRunning == true)
            {
                local.StopAsync().GetAwaiter().GetResult();
            }

            var response = new MonitorIpcClient().SendAsync(new MonitorIpcRequest
            {
                Command = MonitorIpcCommand.Start,
                OperationMode = AppOperationMode.Acquisition.ToString()
            }).GetAwaiter().GetResult();

            if (!response.Success)
            {
                logger?.LogWarning("界面关闭时交给后台采集失败：{Error}", response.Error ?? "未知错误");
                return;
            }

            logger?.LogInformation("界面已关闭，采集与 MQTT 推送已在后台进程中继续运行");
        }
        catch (Exception ex)
        {
            logger?.LogWarning(ex, "界面关闭时交给后台采集失败");
        }
    }
}
