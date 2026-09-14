namespace HuaGuang.Monitor.Platforms.Windows;

/// <summary>
/// 工控场景：点标题栏 × 仅隐藏窗口，进程与心跳继续；任务管理器结束进程时不会写入「正常退出」标记。
/// </summary>
static class WindowsWindowCloseGuard
{
    static bool _closeGuardAttached;

    public static void Apply(Microsoft.Maui.Controls.Window window)
    {
        if (_closeGuardAttached)
        {
            return;
        }

        window.HandlerChanged += OnHandlerChanged;

        void OnHandlerChanged(object? sender, EventArgs e)
        {
            if (window.Handler?.PlatformView is not Microsoft.UI.Xaml.Window nativeWindow)
            {
                return;
            }

            window.HandlerChanged -= OnHandlerChanged;
            _closeGuardAttached = true;

            nativeWindow.AppWindow.Closing += (_, args) =>
            {
                if (WindowsUiShutdownState.IsProgramExitRequested)
                {
                    return;
                }

                args.Cancel = true;
                nativeWindow.AppWindow.Hide();
            };
        }
    }
}

public static class WindowsUiShutdownState
{
    public static bool IsProgramExitRequested { get; set; }
}
