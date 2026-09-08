using HuaGuang.Monitor.ViewModels;

namespace HuaGuang.Monitor.Views;

public partial class HistoryPage : MonitorPageBase
{
    const double MinListHeight = 120;

    public HistoryPage() : this(MauiProgram.Services.GetRequiredService<HistoryViewModel>())
    {
    }

    public HistoryPage(HistoryViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = viewModel;
        TableHost.SizeChanged += (_, _) => UpdateHistoryListHeight();
        TableHorizontalScroll.SizeChanged += (_, _) => UpdateHistoryListHeight();
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        if (BindingContext is HistoryViewModel viewModel)
        {
            await viewModel.InitializeAsync();
            await viewModel.RefreshOnAppearAsync();
        }

        UpdateHistoryListHeight();
    }

    protected override void OnSizeAllocated(double width, double height)
    {
        base.OnSizeAllocated(width, height);
        UpdateHistoryListHeight();
    }

    void UpdateHistoryListHeight()
    {
        if (HistoryRowsView is null || TableHorizontalScroll is null)
        {
            return;
        }

        var viewportHeight = TableHorizontalScroll.Height;
        if (viewportHeight <= 0)
        {
            return;
        }

        const double headerReserve = 44;
        HistoryRowsView.HeightRequest = Math.Max(MinListHeight, viewportHeight - headerReserve);
    }
}
