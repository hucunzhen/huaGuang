using System.Diagnostics;

namespace HuaGuang.Monitor.Services.Watchdog;

static class WatchdogWindowsServiceHelper
{
    public static bool IsDedicatedWatchdogRunning()
    {
        if (!OperatingSystem.IsWindows())
        {
            return false;
        }

        return IsWindowsServiceRunning(WatchdogConstants.WatchdogWindowsServiceName);
    }

    static bool IsWindowsServiceRunning(string serviceName)
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "sc.exe"),
                Arguments = $"query \"{serviceName}\"",
                RedirectStandardOutput = true,
                CreateNoWindow = true,
                UseShellExecute = false
            });
            if (process is null)
            {
                return false;
            }

            var output = process.StandardOutput.ReadToEnd();
            process.WaitForExit(10_000);
            return output.Contains("RUNNING", StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }
}
