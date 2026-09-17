namespace HuaGuang.Monitor.Services.Watchdog;

public static class WindowsUiWatchPolicy
{
    public const string StartupRegistryValueName = "IndustrialMonitor";

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

        return IsStartupRunRegistered();
    }

    public static bool IsStartupRunRegistered()
    {
        if (!OperatingSystem.IsWindows())
        {
            return false;
        }

        try
        {
            var value = Microsoft.Win32.Registry.CurrentUser
                .OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run")
                ?.GetValue(StartupRegistryValueName) as string;
            return !string.IsNullOrWhiteSpace(value);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// 已配置开机自启时，系统 Run 项会在登录时启动界面；开机后短时间内守护不再重复拉起。
    /// 若 10 分钟内有 UI 心跳后进程消失，仍视为异常退出，由守护立即恢复。
    /// </summary>
    public static bool ShouldDeferUiLaunchToStartupRegistry(WatchdogRoleState? uiState)
    {
        if (!IsStartupRunRegistered())
        {
            return false;
        }

        if (uiState?.LastHeartbeatUtc > DateTimeOffset.UtcNow.AddMinutes(-10))
        {
            return false;
        }

        var bootAge = TimeSpan.FromMilliseconds(Environment.TickCount64);
        return bootAge < TimeSpan.FromMinutes(3);
    }
}
