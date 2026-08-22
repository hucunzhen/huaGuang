using System.Collections.ObjectModel;
using HuaGuang.Monitor.Models;
using HuaGuang.Monitor.Services;

namespace HuaGuang.Monitor.ViewModels;

public sealed class TagGroupViewModel
{
    public TagGroupViewModel(TagDisplayCategory category)
    {
        Category = category;
        Title = TagDisplayCategoryHelper.GetTitle(category);
        AccentColor = TagDisplayCategoryHelper.GetAccentColor(category);
    }

    public TagDisplayCategory Category { get; }
    public string Title { get; }
    public string AccentColor { get; }
    public Color AccentBrush => Color.FromArgb(AccentColor);
    public ObservableCollection<TagRowViewModel> Tags { get; } = [];
}
