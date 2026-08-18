using CommunityToolkit.Mvvm.ComponentModel;
using HuaGuang.Monitor.Models;

namespace HuaGuang.Monitor.ViewModels;

public partial class TagRowViewModel : ObservableObject
{
    public TagRowViewModel(PlcTag tag)
    {
        Id = tag.Id;
        Name = tag.Name;
        Unit = tag.Unit;
        AddressText = tag.DisplayAddress;
    }

    public string Id { get; }
    public string Name { get; }
    public string Unit { get; }
    public string AddressText { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DisplayValue))]
    object? value;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(QualityColor))]
    string quality = "—";

    [ObservableProperty]
    string updatedAt = "—";

    public string DisplayValue => Value switch
    {
        null => "—",
        bool flag => flag ? "开" : "关",
        double number => number.ToString("0.####"),
        float number => number.ToString("0.####"),
        _ => Value.ToString() ?? "—"
    };

    public Color QualityColor => Quality switch
    {
        "Good" => Color.FromArgb("#3DDC97"),
        "Bad" => Color.FromArgb("#FF6B6B"),
        _ => Color.FromArgb("#8AA0B5")
    };

    public void Apply(TagSnapshot snapshot)
    {
        Value = snapshot.Value;
        Quality = snapshot.Quality;
        UpdatedAt = snapshot.Timestamp.ToLocalTime().ToString("HH:mm:ss");
    }
}
