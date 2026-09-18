using System.Runtime.InteropServices;
using Microsoft.UI.Windowing;
using WinRT.Interop;
using WinUiWindow = Microsoft.UI.Xaml.Window;

namespace HuaGuang.Monitor.Platforms.Windows;

/// <summary>任务栏 / 系统标题栏图标（SetIcon + WM_SETICON；不替换 WinUI Content）。</summary>
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
            if (window.Handler?.PlatformView is not WinUiWindow)
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
        if (window.Handler?.PlatformView is not WinUiWindow nativeWindow)
        {
            return;
        }

        nativeWindow.Title = WindowTitle;
        var appWindow = nativeWindow.AppWindow;

        var iconPath = ResolveIconPath();
        if (iconPath is null)
        {
            StartupBootstrapLog.Write("WindowsAppIcon: no icon file (disk or embedded)");
            return;
        }

        try
        {
            appWindow.SetIcon(iconPath);
        }
        catch (Exception ex)
        {
            StartupBootstrapLog.Write("WindowsAppIcon: AppWindow.SetIcon failed", ex);
        }

        ApplyWin32TitleBarIcon(nativeWindow, iconPath);
        StartupBootstrapLog.Write($"WindowsAppIcon: icon applied path={iconPath}");
    }

    static void ApplyWin32TitleBarIcon(WinUiWindow nativeWindow, string iconPath)
    {
        try
        {
            var hwnd = WindowNative.GetWindowHandle(nativeWindow);
            var targets = new[] { hwnd, GetRootWindow(hwnd) };
            var small = LoadImage(IntPtr.Zero, iconPath, ImageIcon, 16, 16, LoadFromFile);
            var large = LoadImage(IntPtr.Zero, iconPath, ImageIcon, 32, 32, LoadFromFile);
            foreach (var target in targets.Distinct())
            {
                if (target == IntPtr.Zero)
                {
                    continue;
                }

                if (small != IntPtr.Zero)
                {
                    SendMessage(target, WmSetIcon, (IntPtr)IconSmall, small);
                }

                if (large != IntPtr.Zero)
                {
                    SendMessage(target, WmSetIcon, (IntPtr)IconBig, large);
                }
            }
        }
        catch (Exception ex)
        {
            StartupBootstrapLog.Write("WindowsAppIcon: WM_SETICON failed", ex);
        }
    }

    static IntPtr GetRootWindow(IntPtr hwnd)
    {
        var root = GetAncestor(hwnd, GaRoot);
        return root != IntPtr.Zero ? root : hwnd;
    }

    internal static string? ResolveBrandPngPath()
    {
        foreach (var path in EnumerateDiskPngCandidates())
        {
            if (File.Exists(path))
            {
                return Path.GetFullPath(path);
            }
        }

        return MaterializeEmbeddedAsset(".png");
    }

    static string? ResolveIconPath()
    {
        foreach (var path in EnumerateDiskIconCandidates())
        {
            if (File.Exists(path))
            {
                return Path.GetFullPath(path);
            }
        }

        return MaterializeEmbeddedAsset(".ico");
    }

    static IEnumerable<string> EnumerateDiskIconCandidates()
    {
        var baseDir = AppContext.BaseDirectory;
        yield return Path.Combine(baseDir, "logo.ico");
        yield return Path.Combine(baseDir, "appicon.ico");
        yield return Path.Combine(baseDir, "Resources", "AppIcon", "appicon.ico");

        var exePath = Environment.ProcessPath;
        if (!string.IsNullOrWhiteSpace(exePath))
        {
            yield return exePath;
        }
    }

    static IEnumerable<string> EnumerateDiskPngCandidates()
    {
        var baseDir = AppContext.BaseDirectory;
        yield return Path.Combine(baseDir, "appicon.png");
        yield return Path.Combine(baseDir, "Resources", "AppIcon", "appicon.png");
    }

    internal static Uri ToFileUri(string path)
    {
        var full = Path.GetFullPath(path);
        return new Uri($"file:///{full.Replace('\\', '/').TrimStart('/')}");
    }

    static string? MaterializeEmbeddedAsset(string extension)
    {
        var resourceName = $"HuaGuang.Monitor.Resources.AppIcon.appicon{extension}";
        var assembly = typeof(WindowsAppIcon).Assembly;
        using var stream = assembly.GetManifestResourceStream(resourceName);
        if (stream is null)
        {
            return null;
        }

        var directory = Path.Combine(Path.GetTempPath(), "HuaGuang.Monitor", "brand");
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, $"appicon{extension}");
        try
        {
            if (File.Exists(path))
            {
                var existingTime = File.GetLastWriteTimeUtc(path);
                var asmPath = assembly.Location;
                if (!string.IsNullOrWhiteSpace(asmPath) && File.Exists(asmPath))
                {
                    var asmTime = File.GetLastWriteTimeUtc(asmPath);
                    if (existingTime >= asmTime)
                    {
                        return path;
                    }
                }
            }

            using var file = File.Create(path);
            stream.CopyTo(file);
            return path;
        }
        catch (Exception ex)
        {
            StartupBootstrapLog.Write($"WindowsAppIcon: materialize {extension} failed", ex);
            return null;
        }
    }

    const int WmSetIcon = 0x0080;
    const int IconSmall = 0;
    const int IconBig = 1;
    const uint GaRoot = 2;
    const uint ImageIcon = 1;
    const uint LoadFromFile = 0x0010;

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern IntPtr LoadImage(
        IntPtr hInst,
        string lpszName,
        uint uType,
        int cxDesired,
        int cyDesired,
        uint fuLoad);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    static extern IntPtr GetAncestor(IntPtr hwnd, uint flags);
}
