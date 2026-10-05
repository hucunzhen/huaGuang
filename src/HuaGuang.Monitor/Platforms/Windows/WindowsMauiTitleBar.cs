using HuaGuang.Monitor.Services;
using Microsoft.Maui.Controls;

namespace HuaGuang.Monitor.Platforms.Windows;

/// <summary>使用 MAUI <see cref="TitleBar"/> 在系统标题栏左侧显示 icon + 标题（不替换 WinUI Content）。</summary>
static class WindowsMauiTitleBar
{
    public static void Apply(Window window)
    {
        var windowTitle = MonitorProcessInstance.WindowTitle;
        window.Title = windowTitle;

        ImageSource? icon = null;
        var pngPath = WindowsAppIcon.ResolveBrandPngPath();
        if (!string.IsNullOrWhiteSpace(pngPath))
        {
            icon = ImageSource.FromFile(pngPath);
        }

        window.TitleBar = new TitleBar
        {
            Title = windowTitle,
            Icon = icon,
            ForegroundColor = Color.FromArgb("#FFFFFF"),
            BackgroundColor = Color.FromArgb("#0B1522")
        };

        StartupBootstrapLog.Write(
            pngPath is null
                ? "WindowsMauiTitleBar: TitleBar without Icon (png missing)"
                : $"WindowsMauiTitleBar: TitleBar icon={pngPath}");
    }
}
