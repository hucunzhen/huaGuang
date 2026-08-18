using HuaGuang.Monitor.ViewModels;

namespace HuaGuang.Monitor.Views;

public partial class TagsPage : ContentPage
{
    public TagsPage() : this(MauiProgram.Services.GetRequiredService<TagsViewModel>())
    {
    }

    public TagsPage(TagsViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = viewModel;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        if (BindingContext is TagsViewModel viewModel)
        {
            viewModel.Reload();
        }
    }
}
