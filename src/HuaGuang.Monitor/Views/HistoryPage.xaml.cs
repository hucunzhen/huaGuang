using HuaGuang.Monitor.ViewModels;

namespace HuaGuang.Monitor.Views;

public partial class HistoryPage : MonitorPageBase
{
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
        }
    }
}
