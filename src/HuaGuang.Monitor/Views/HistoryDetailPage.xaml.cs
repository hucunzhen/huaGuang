using HuaGuang.Monitor.ViewModels;

namespace HuaGuang.Monitor.Views;

public partial class HistoryDetailPage : ContentPage
{
    public HistoryDetailPage() : this(MauiProgram.Services.GetRequiredService<HistoryDetailViewModel>())
    {
    }

    public HistoryDetailPage(HistoryDetailViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = viewModel;
    }
}
