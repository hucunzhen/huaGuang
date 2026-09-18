using HuaGuang.Monitor.Services;

namespace HuaGuang.Monitor.Platforms.Windows;

/// <summary>安装/升级时允许 UI 真正退出，避免 CloseGuard 导致 Inno Setup 卡住。</summary>
static class WindowsInstallerExitHelper
{
    public const string FlagFileName = "installer-force-ui-exit.flag";

    public static string FlagFilePath =>
        Path.Combine(WindowsSharedDataDirectory.Resolve(), FlagFileName);

    public static bool IsInstallerExitRequested()
    {
        try
        {
            return File.Exists(FlagFilePath);
        }
        catch
        {
            return false;
        }
    }

    public static void ClearFlagIfPresent()
    {
        try
        {
            if (File.Exists(FlagFilePath))
            {
                File.Delete(FlagFilePath);
            }
        }
        catch
        {
        }
    }

    public static void RequestFullApplicationExit()
    {
        WindowsUiShutdownState.IsProgramExitRequested = true;
        ClearFlagIfPresent();
        if (Microsoft.Maui.Controls.Application.Current is not null)
        {
            Microsoft.Maui.Controls.Application.Current.Quit();
        }
    }
}
