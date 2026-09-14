using HuaGuang.Monitor.Services.Watchdog;

namespace HuaGuang.Monitor.Services;

/// <summary>用户明确选择「退出程序」时调用（会写入正常退出，守护不会自动再开界面）。</summary>
public static class UiApplicationExit
{
    public static void RequestGracefulShutdownAndQuit()
    {
#if WINDOWS
        HuaGuang.Monitor.Platforms.Windows.WindowsUiShutdownState.IsProgramExitRequested = true;
#endif
        WatchdogHeartbeat.StopGraceful(WatchdogConstants.UiRole);
        if (Application.Current is not null)
        {
            Application.Current.Quit();
        }
    }
}
