using System.ServiceProcess;
using HuaGuang.Monitor.Ipc;

namespace HuaGuang.Monitor.Platforms.Windows;

static class WindowsServiceHelper
{
    public static bool IsMonitorServiceRunning()
    {
        try
        {
            using var service = new ServiceController(MonitorIpcConstants.ServiceName);
            return service.Status == ServiceControllerStatus.Running;
        }
        catch
        {
            return false;
        }
    }
}
