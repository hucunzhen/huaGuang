using HuaGuang.Monitor.ViewModels;
#if WINDOWS
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.System;
#endif

namespace HuaGuang.Monitor.Views;

public partial class HistoryPage : MonitorPageBase
{
    bool _syncingHorizontalScroll;
#if WINDOWS
    bool _tableMouseWheelHooked;
#endif

    public HistoryPage() : this(MauiProgram.Services.GetRequiredService<HistoryViewModel>())
    {
    }

    public HistoryPage(HistoryViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = viewModel;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
#if WINDOWS
        HookTableMouseWheel();
#endif
        if (BindingContext is HistoryViewModel viewModel)
        {
            await viewModel.InitializeAsync();
            await viewModel.RefreshOnAppearAsync();
        }
    }

#if WINDOWS
    void HookTableMouseWheel()
    {
        if (_tableMouseWheelHooked)
        {
            return;
        }

        if (BodyHorizontalScroll.Handler?.PlatformView is not ScrollViewer horizontalViewer)
        {
            BodyHorizontalScroll.HandlerChanged -= OnBodyHorizontalHandlerChanged;
            BodyHorizontalScroll.HandlerChanged += OnBodyHorizontalHandlerChanged;
            return;
        }

        BodyHorizontalScroll.HandlerChanged -= OnBodyHorizontalHandlerChanged;
        horizontalViewer.PointerWheelChanged -= OnBodyHorizontalPointerWheelChanged;
        horizontalViewer.PointerWheelChanged += OnBodyHorizontalPointerWheelChanged;
        _tableMouseWheelHooked = true;
    }

    void OnBodyHorizontalHandlerChanged(object? sender, EventArgs e) => HookTableMouseWheel();

    void OnBodyHorizontalPointerWheelChanged(object sender, PointerRoutedEventArgs e)
    {
        try
        {
            if (sender is not ScrollViewer)
            {
                return;
            }

            var delta = e.GetCurrentPoint((UIElement)sender).Properties.MouseWheelDelta;
            if (delta == 0)
            {
                return;
            }

            if (e.KeyModifiers.HasFlag(VirtualKeyModifiers.Shift))
            {
                var nextX = Math.Max(0, BodyHorizontalScroll.ScrollX - delta);
                BodyHorizontalScroll.ScrollToAsync(nextX, 0, false);
            }
            else
            {
                var nextY = Math.Max(0, BodyVerticalScroll.ScrollY - delta);
                BodyVerticalScroll.ScrollToAsync(0, nextY, false);
            }

            e.Handled = true;
        }
        catch
        {
            // 滚轮转发失败时不影响页面其它交互。
        }
    }
#endif

    void OnHeaderHorizontalScrolled(object? sender, ScrolledEventArgs e) =>
        SyncHorizontalScroll(sourceScrollX: e.ScrollX, fromHeader: true);

    void OnBodyHorizontalScrolled(object? sender, ScrolledEventArgs e) =>
        SyncHorizontalScroll(sourceScrollX: e.ScrollX, fromHeader: false);

    void SyncHorizontalScroll(double sourceScrollX, bool fromHeader)
    {
        if (_syncingHorizontalScroll)
        {
            return;
        }

        _syncingHorizontalScroll = true;
        try
        {
            if (fromHeader)
            {
                BodyHorizontalScroll.ScrollToAsync(sourceScrollX, 0, false);
            }
            else
            {
                HeaderHorizontalScroll.ScrollToAsync(sourceScrollX, 0, false);
            }
        }
        finally
        {
            _syncingHorizontalScroll = false;
        }
    }
}
