using Microsoft.Maui.Controls;

namespace HuaGuang.Monitor.Platforms.Windows;

/// <summary>使用 MAUI <see cref="TitleBar"/> 在系统标题栏左侧显示 icon + 标题（不替换 WinUI Content）。</summary>
static class WindowsMauiTitleBar
{
    const string WindowTitle = "工业监控";

    public static void Apply(Window window)
    {
        window.Title = WindowTitle;

        ImageSource? icon = null;
        var pngPath = WindowsAppIcon.ResolveBrandPngPath();
        if (!string.IsNullOrWhiteSpace(pngPath))
        {
            icon = ImageSource.FromFile(pngPath);
        }

        window.TitleBar = new TitleBar
        {
            Title = WindowTitle,
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
