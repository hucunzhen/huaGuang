using HuaGuang.Monitor.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace HuaGuang.Monitor.Views;

public partial class DiagnosticsPage : MonitorPageBase
{
    readonly DiagnosticsViewModel _viewModel;

    public DiagnosticsPage() : this(MauiProgram.Services.GetRequiredService<DiagnosticsViewModel>())
    {
    }

    public DiagnosticsPage(DiagnosticsViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        BindingContext = viewModel;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        _viewModel.OnAppearing();
    }

    protected override void OnDisappearing()
    {
        _viewModel.OnDisappearing();
        base.OnDisappearing();
    }
}
