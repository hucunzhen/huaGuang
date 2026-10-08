using HuaGuang.Monitor.Services;
using Microsoft.Maui.Controls;
using Microsoft.UI.Windowing;
using WinUiWindow = Microsoft.UI.Xaml.Window;
using WinColor = Windows.UI.Color;

namespace HuaGuang.Monitor.Platforms.Windows;

/// <summary>使用 MAUI <see cref="TitleBar"/> 在系统标题栏左侧显示 icon + 标题（不替换 WinUI Content）。</summary>
static class WindowsMauiTitleBar
{
    static readonly Color TitleBarBackground = Color.FromArgb("#0B1522");
    static readonly Color TitleBarForeground = Color.FromArgb("#FFFFFF");

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
            ForegroundColor = TitleBarForeground,
            BackgroundColor = TitleBarBackground
        };

        window.HandlerChanged += OnHandlerChanged;
        window.Activated += OnActivated;

        void OnHandlerChanged(object? sender, EventArgs e)
        {
            if (window.Handler?.PlatformView is not WinUiWindow)
            {
                return;
            }

            window.HandlerChanged -= OnHandlerChanged;
            ApplyCaptionButtonColors(window);
        }

        void OnActivated(object? sender, EventArgs e) => ApplyCaptionButtonColors(window);

        StartupBootstrapLog.Write(
            pngPath is null
                ? "WindowsMauiTitleBar: TitleBar without Icon (png missing)"
                : $"WindowsMauiTitleBar: TitleBar icon={pngPath}");
    }

    static void ApplyCaptionButtonColors(Window window)
    {
        if (window.Handler?.PlatformView is not WinUiWindow nativeWindow)
        {
            return;
        }

        try
        {
            var titleBar = nativeWindow.AppWindow.TitleBar;
            // 深色标题栏上系统默认按钮常是深色图标，几乎看不见。
            var white = WinColor.FromArgb(255, 232, 241, 248);
            var muted = WinColor.FromArgb(255, 138, 160, 181);
            var rest = WinColor.FromArgb(255, 21, 37, 54);
            var hover = WinColor.FromArgb(255, 58, 96, 128);
            var pressed = WinColor.FromArgb(255, 46, 196, 182);
            var chrome = WinColor.FromArgb(255, 11, 21, 34);

            titleBar.BackgroundColor = chrome;
            titleBar.InactiveBackgroundColor = chrome;
            titleBar.ForegroundColor = white;
            titleBar.InactiveForegroundColor = muted;
            titleBar.ButtonBackgroundColor = rest;
            titleBar.ButtonForegroundColor = white;
            titleBar.ButtonHoverBackgroundColor = hover;
            titleBar.ButtonHoverForegroundColor = WinColor.FromArgb(255, 255, 255, 255);
            titleBar.ButtonPressedBackgroundColor = pressed;
            titleBar.ButtonPressedForegroundColor = WinColor.FromArgb(255, 11, 21, 34);
            titleBar.ButtonInactiveBackgroundColor = rest;
            titleBar.ButtonInactiveForegroundColor = muted;
        }
        catch (Exception ex)
        {
            StartupBootstrapLog.Write("WindowsMauiTitleBar: caption button colors failed", ex);
        }
    }
}
