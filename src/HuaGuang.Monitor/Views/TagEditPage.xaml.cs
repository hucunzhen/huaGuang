using HuaGuang.Monitor.ViewModels;

namespace HuaGuang.Monitor.Views;

public partial class TagEditPage : ContentPage
{
    public TagEditPage() : this(MauiProgram.Services.GetRequiredService<TagEditViewModel>())
    {
    }

    public TagEditPage(TagEditViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = viewModel;
    }
}
