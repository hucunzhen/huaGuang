using HuaGuang.Monitor.Services;
using Microsoft.UI.Windowing;

namespace HuaGuang.Monitor.Platforms.Windows;

public sealed class WindowsFullScreenPresenter : IPlatformFullScreenPresenter
{
    public void Enter() =>
        GetAppWindow()?.SetPresenter(AppWindowPresenterKind.FullScreen);

    public void Exit() =>
        GetAppWindow()?.SetPresenter(AppWindowPresenterKind.Default);

    static AppWindow? GetAppWindow()
    {
        var window = Application.Current?.Windows.FirstOrDefault();
        if (window?.Handler?.PlatformView is not Microsoft.UI.Xaml.Window nativeWindow)
        {
            return null;
        }

        return nativeWindow.AppWindow;
    }
}
