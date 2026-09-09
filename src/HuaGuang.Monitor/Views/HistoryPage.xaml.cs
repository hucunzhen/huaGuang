using HuaGuang.Monitor.ViewModels;

namespace HuaGuang.Monitor.Views;

public partial class HistoryPage : MonitorPageBase
{
    bool _syncingHorizontalScroll;

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
        if (BindingContext is HistoryViewModel viewModel)
        {
            await viewModel.InitializeAsync();
            await viewModel.RefreshOnAppearAsync();
        }
    }

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
