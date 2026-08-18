using HuaGuang.Monitor.ViewModels;

namespace HuaGuang.Monitor.Views;

public partial class SettingsPage : ContentPage
{
    public SettingsPage() : this(MauiProgram.Services.GetRequiredService<SettingsViewModel>())
    {
    }

    public SettingsPage(SettingsViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = viewModel;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        if (BindingContext is SettingsViewModel viewModel)
        {
            viewModel.Reload();
        }
    }
}
