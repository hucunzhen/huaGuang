using System.ServiceProcess;
using HuaGuang.Monitor.Ipc;

namespace HuaGuang.Monitor.Platforms.Windows;

static class WindowsServiceHelper
{
    public static bool IsMonitorServiceInstalled()
    {
        try
        {
            using var service = new ServiceController(MonitorIpcConstants.CurrentServiceName);
            _ = service.Status;
            return true;
        }
        catch
        {
            return false;
        }
    }

    public static bool IsMonitorServiceRunning()
    {
        try
        {
            using var service = new ServiceController(MonitorIpcConstants.CurrentServiceName);
            return service.Status == ServiceControllerStatus.Running;
        }
        catch
        {
            return false;
        }
    }
}
