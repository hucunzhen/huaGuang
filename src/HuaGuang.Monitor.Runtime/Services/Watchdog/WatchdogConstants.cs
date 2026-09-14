namespace HuaGuang.Monitor.Services.Watchdog;

public static class WatchdogConstants
{
    public const string UiRole = "ui";
    public const string AcquisitionRole = "acquisition";

    public const string WatchdogWindowsServiceName = "HuaGuangMonitorWatchdog";
    public const string WatchdogDisplayName = "工业监控守护服务";
    public const string WatchdogDescription = "监控采集服务与界面进程，异常退出时自动重启";

    public const string UiProcessName = "HuaGuang.Monitor";
    public const string AcquisitionProcessName = "HuaGuang.Monitor.Service";
    public const string WatchdogRole = "watchdog";
    public const string WatchdogProcessName = "HuaGuang.Monitor.Watchdog.Service";

    public const string UiExeFileName = "HuaGuang.Monitor.exe";
    public const string WatchdogExeFileName = "HuaGuang.Monitor.Watchdog.Service.exe";
}
