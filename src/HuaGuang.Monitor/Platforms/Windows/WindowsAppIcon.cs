namespace HuaGuang.Monitor.Platforms.Windows;

static class WindowsAppIcon
{
    public static void Apply(Microsoft.Maui.Controls.Window window)
    {
        window.HandlerChanged += OnHandlerChanged;

        void OnHandlerChanged(object? sender, EventArgs e)
        {
            if (window.Handler?.PlatformView is not Microsoft.UI.Xaml.Window nativeWindow)
                return;

            window.HandlerChanged -= OnHandlerChanged;

            var iconPath = ResolveIconPath();
            if (iconPath is not null)
                nativeWindow.AppWindow.SetIcon(iconPath);
        }
    }

    static string? ResolveIconPath()
    {
        foreach (var name in new[] { "appicon.ico", "logo.ico" })
        {
            var path = Path.Combine(AppContext.BaseDirectory, name);
            if (File.Exists(path))
                return path;
        }

        return null;
    }
}
