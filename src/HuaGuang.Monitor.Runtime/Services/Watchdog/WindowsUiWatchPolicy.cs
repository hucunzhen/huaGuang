namespace HuaGuang.Monitor.Services.Watchdog;

public static class WindowsUiWatchPolicy
{
    public static bool ShouldWatchUi()
    {
        if (!OperatingSystem.IsWindows())
        {
            return false;
        }

        var uiState = WatchdogStateStore.Read(WatchdogConstants.UiRole);
        if (uiState?.LastHeartbeatUtc > DateTimeOffset.UtcNow.AddHours(-24))
        {
            return true;
        }

        if (!Environment.UserInteractive)
        {
            return false;
        }

        try
        {
            var value = Microsoft.Win32.Registry.CurrentUser
                .OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run")
                ?.GetValue("IndustrialMonitor") as string;
            return !string.IsNullOrWhiteSpace(value);
        }
        catch
        {
            return false;
        }
    }
}
