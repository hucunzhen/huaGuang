namespace HuaGuang.Monitor.Platforms.Windows;

static class WindowsAppIcon
{
    const string WindowTitle = "工业监控";

    public static void Apply(Microsoft.Maui.Controls.Window window)
    {
        window.Title = WindowTitle;
        window.HandlerChanged += OnHandlerChanged;
        window.Activated += OnActivated;

        void OnHandlerChanged(object? sender, EventArgs e)
        {
            if (window.Handler?.PlatformView is not Microsoft.UI.Xaml.Window)
            {
                return;
            }

            window.HandlerChanged -= OnHandlerChanged;
            ApplyToNativeWindow(window);
        }

        void OnActivated(object? sender, EventArgs e) => ApplyToNativeWindow(window);
    }

    static void ApplyToNativeWindow(Microsoft.Maui.Controls.Window window)
    {
        if (window.Handler?.PlatformView is not Microsoft.UI.Xaml.Window nativeWindow)
        {
            return;
        }

        nativeWindow.Title = WindowTitle;

        var iconPath = ResolveIconPath();
        if (iconPath is null)
        {
            return;
        }

        try
        {
            nativeWindow.AppWindow.SetIcon(iconPath);
        }
        catch
        {
        }
    }

    static string? ResolveIconPath()
    {
        foreach (var path in EnumerateIconCandidates())
        {
            if (File.Exists(path))
            {
                return Path.GetFullPath(path);
            }
        }

        return null;
    }

    static IEnumerable<string> EnumerateIconCandidates()
    {
        var baseDir = AppContext.BaseDirectory;

        // 高质量图标（由构建/发布脚本从 logo.png 生成），避免使用 resizetizer 的小尺寸 appicon.ico。
        yield return Path.Combine(baseDir, "logo.ico");
        yield return Path.Combine(baseDir, "Resources", "AppIcon", "appicon.ico");
    }
}
