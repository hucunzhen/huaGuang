using HuaGuang.Monitor.ViewModels;

namespace HuaGuang.Monitor.Views;

public partial class DiagnosticsPage : MonitorPageBase
{
    public DiagnosticsPage() : this(MauiProgram.Services.GetRequiredService<DiagnosticsViewModel>())
    {
    }

    public DiagnosticsPage(DiagnosticsViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = viewModel;
    }
}
