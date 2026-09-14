using System.ServiceProcess;
using HuaGuang.Monitor.Services.Watchdog;

namespace HuaGuang.Monitor.Platforms.Windows;

static class WatchdogServiceHelper
{
    public static bool IsWatchdogServiceRunning()
    {
        try
        {
            using var service = new ServiceController(WatchdogConstants.WatchdogWindowsServiceName);
            return service.Status == ServiceControllerStatus.Running;
        }
        catch
        {
            return false;
        }
    }
}
