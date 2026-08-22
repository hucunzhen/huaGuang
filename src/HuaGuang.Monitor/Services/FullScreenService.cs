using System.ComponentModel;

namespace HuaGuang.Monitor.Services;

public sealed class FullScreenService : INotifyPropertyChanged
{
    readonly IPlatformFullScreenPresenter _presenter;
    bool _isFullScreen;

    public FullScreenService(IPlatformFullScreenPresenter presenter) =>
        _presenter = presenter;

    public event PropertyChangedEventHandler? PropertyChanged;
    public event EventHandler? FullScreenChanged;

    public bool IsFullScreen => _isFullScreen;

    public string ToggleLabel => _isFullScreen ? "退出全屏" : "全屏";

    public void Toggle() => SetFullScreen(!_isFullScreen);

    public void SetFullScreen(bool enabled)
    {
        if (_isFullScreen == enabled)
        {
            return;
        }

        if (enabled)
        {
            _presenter.Enter();
            if (Shell.Current is not null)
            {
                Shell.SetTabBarIsVisible(Shell.Current, false);
            }
        }
        else
        {
            _presenter.Exit();
            if (Shell.Current is not null)
            {
                Shell.SetTabBarIsVisible(Shell.Current, true);
            }
        }

        _isFullScreen = enabled;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsFullScreen)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ToggleLabel)));
        FullScreenChanged?.Invoke(this, EventArgs.Empty);
    }
}
