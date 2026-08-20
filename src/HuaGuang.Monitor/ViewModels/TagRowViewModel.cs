using CommunityToolkit.Mvvm.ComponentModel;
using HuaGuang.Monitor.Models;
using HuaGuang.Monitor.Services;

namespace HuaGuang.Monitor.ViewModels;

public partial class TagRowViewModel : ObservableObject
{
    readonly PlcTag _tag;
    readonly int _globalPrecision = 1;

    public TagRowViewModel(PlcTag tag, int globalPrecision = 1, string? addressOverride = null)
    {
        _tag = tag;
        _globalPrecision = globalPrecision;
        Id = tag.Id;
        Name = tag.Name;
        Unit = tag.Unit;
        AddressText = addressOverride ?? tag.DisplayAddress;
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

    public string DisplayValue => ValueFormatting.FormatDisplay(_tag, Value, _globalPrecision);

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
