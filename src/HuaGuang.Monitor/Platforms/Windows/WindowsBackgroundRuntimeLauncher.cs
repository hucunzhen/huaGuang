using System.Diagnostics;
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
                _launchAttempted = TryLaunchProcess();
            }
        }

        return MonitorIpcClient.WaitForServiceAvailable(timeout ?? TimeSpan.FromSeconds(12));
    }

    public bool IsBackgroundPresent() =>
        IsServiceProcessRunning() || WindowsServiceHelper.IsMonitorServiceRunning();

    static bool TryLaunchProcess()
    {
        var exePath = ResolveServiceExePath();
        if (!File.Exists(exePath))
        {
            return false;
        }

        if (IsServiceProcessRunning())
        {
            return true;
        }

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = exePath,
                WorkingDirectory = Path.GetDirectoryName(exePath) ?? AppContext.BaseDirectory,
                UseShellExecute = false,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden
            });
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

    static bool IsServiceProcessRunning()
    {
        try
        {
            return Process.GetProcessesByName("HuaGuang.Monitor.Service").Length > 0;
        }
        catch
        {
            return false;
        }
    }
}
