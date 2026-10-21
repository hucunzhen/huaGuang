using HuaGuang.Monitor.Ipc;
using HuaGuang.Monitor.Services;

namespace HuaGuang.Monitor.Platforms.Windows;

public sealed class WindowsBackgroundRuntimeLauncher : IBackgroundRuntimeLauncher
{
    static readonly object Gate = new();
    static bool _launchAttempted;

    public bool EnsureRunning(TimeSpan? timeout = null)
    {
        if (MonitorIpcClient.IsServiceAvailable())
        {
            return true;
        }

        lock (Gate)
        {
            if (MonitorIpcClient.IsServiceAvailable())
            {
                return true;
            }

            if (!_launchAttempted)
            {
                _launchAttempted = MonitorProcessInstance.IsIsolated
                    ? WindowsInstanceBackgroundHost.EnsureRunning(timeout ?? TimeSpan.FromSeconds(3))
                    : TryStartWindowsService();
            }
        }

        if (MonitorIpcClient.IsServiceAvailable())
        {
            return true;
        }

        // 独立实例已在 EnsureRunning 里等过 IPC，不要再空等一整轮。
        if (MonitorProcessInstance.IsIsolated)
        {
            return false;
        }

        return MonitorIpcClient.WaitForServiceAvailable(timeout ?? TimeSpan.FromSeconds(12));
    }

    public bool IsBackgroundPresent() =>
        MonitorIpcClient.IsServiceAvailable() || WindowsServiceHelper.IsMonitorServiceRunning();

    static bool TryStartWindowsService()
    {
        try
        {
            using var service = new System.ServiceProcess.ServiceController(MonitorIpcConstants.CurrentServiceName);
            if (service.Status == System.ServiceProcess.ServiceControllerStatus.Running)
            {
                return true;
            }

            service.Start();
            return true;
        }
        catch
        {
            return false;
        }
    }

    internal static string ResolveServiceExePath()
    {
        var baseDir = AppContext.BaseDirectory;
        var candidates = new[]
        {
            Path.Combine(baseDir, "service", "HuaGuang.Monitor.Service.exe"),
            Path.Combine(baseDir, "HuaGuang.Monitor.Service.exe")
        };

        foreach (var candidate in candidates)
        {
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return candidates[0];
    }
}
