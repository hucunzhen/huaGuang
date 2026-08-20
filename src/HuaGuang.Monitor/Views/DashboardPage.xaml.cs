using HuaGuang.Monitor.ViewModels;

namespace HuaGuang.Monitor.Views;

public partial class DashboardPage : ContentPage
{
    public DashboardPage() : this(MauiProgram.Services.GetRequiredService<DashboardViewModel>())
    {
    }

    public DashboardPage(DashboardViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = viewModel;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        if (BindingContext is DashboardViewModel viewModel)
        {
            viewModel.Reload();
            await viewModel.TryAutoStartAsync();
        }
    }
}
