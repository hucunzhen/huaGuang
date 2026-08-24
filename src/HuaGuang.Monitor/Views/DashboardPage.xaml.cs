using HuaGuang.Monitor.ViewModels;

namespace HuaGuang.Monitor.Views;

public partial class DashboardPage : MonitorPageBase
{
    DashboardViewModel? _viewModel;

    public DashboardPage() : this(MauiProgram.Services.GetRequiredService<DashboardViewModel>())
    {
    }

    public DashboardPage(DashboardViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = viewModel;
        _viewModel = viewModel;
        viewModel.RequestProductSkuFocus = FocusProductSkuEntry;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        if (BindingContext is DashboardViewModel viewModel)
        {
            _viewModel = viewModel;
            viewModel.RequestProductSkuFocus = FocusProductSkuEntry;
            viewModel.Reload();
            await viewModel.TryAutoStartAsync();
            await FocusProductSkuEntryDelayedAsync();
        }
    }

    async void OnProductSkuCompleted(object? sender, EventArgs e)
    {
        if (_viewModel?.SubmitProductSkuCommand.CanExecute(null) == true)
        {
            await _viewModel.SubmitProductSkuCommand.ExecuteAsync(null);
        }
    }

    void FocusProductSkuEntry()
    {
        _ = FocusProductSkuEntryDelayedAsync();
    }

    async Task FocusProductSkuEntryDelayedAsync()
    {
        if (_viewModel?.ShowProductSkuScanner != true)
        {
            return;
        }

        await Task.Delay(150);
        ProductSkuEntry?.Focus();
    }

    void OnFullScreenClicked(object? sender, EventArgs e) => ToggleFullScreen();
}
