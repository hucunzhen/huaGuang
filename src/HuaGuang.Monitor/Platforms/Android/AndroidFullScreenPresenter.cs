using Android.Views;
using HuaGuang.Monitor.Services;

namespace HuaGuang.Monitor.Platforms.Android;

public sealed class AndroidFullScreenPresenter : IPlatformFullScreenPresenter
{
    public void Enter()
    {
        var window = Platform.CurrentActivity?.Window;
        if (window is null)
        {
            return;
        }

        if (OperatingSystem.IsAndroidVersionAtLeast(30))
        {
            AndroidX.Core.View.WindowCompat.SetDecorFitsSystemWindows(window, false);
            var controller = window.InsetsController;
            if (controller is not null)
            {
                controller.Hide(WindowInsets.Type.SystemBars());
                controller.SystemBarsBehavior = (int)WindowInsetsControllerBehavior.ShowTransientBarsBySwipe;
            }

            return;
        }

#pragma warning disable CS0618
        window.DecorView.SystemUiVisibility = (StatusBarVisibility)(int)(
            SystemUiFlags.ImmersiveSticky |
            SystemUiFlags.Fullscreen |
            SystemUiFlags.HideNavigation |
            SystemUiFlags.LayoutStable |
            SystemUiFlags.LayoutFullscreen |
            SystemUiFlags.LayoutHideNavigation);
#pragma warning restore CS0618
    }

    public void Exit()
    {
        var window = Platform.CurrentActivity?.Window;
        if (window is null)
        {
            return;
        }

        if (OperatingSystem.IsAndroidVersionAtLeast(30))
        {
            AndroidX.Core.View.WindowCompat.SetDecorFitsSystemWindows(window, true);
            window.InsetsController?.Show(WindowInsets.Type.SystemBars());
            return;
        }

#pragma warning disable CS0618
        window.DecorView.SystemUiVisibility = StatusBarVisibility.Visible;
#pragma warning restore CS0618
    }
}
