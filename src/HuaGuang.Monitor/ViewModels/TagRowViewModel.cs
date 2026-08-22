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
        Category = TagDisplayCategoryHelper.Resolve(tag);
    }

    public string Id { get; }
    public string Name { get; }
    public string Unit { get; }
    public string AddressText { get; }
    public TagDisplayCategory Category { get; }

    public bool IsBool => Category == TagDisplayCategory.Switch;

    public bool IsEditableSetting => Category == TagDisplayCategory.Setting && _tag.IsManual;

    public string CategoryAccentColor => TagDisplayCategoryHelper.GetAccentColor(Category);

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DisplayValue))]
    [NotifyPropertyChangedFor(nameof(BoolStatusText))]
    [NotifyPropertyChangedFor(nameof(BoolIndicatorText))]
    [NotifyPropertyChangedFor(nameof(BoolIsOn))]
    [NotifyPropertyChangedFor(nameof(ValueColor))]
    [NotifyPropertyChangedFor(nameof(CardBorderColor))]
    [NotifyPropertyChangedFor(nameof(CardBackgroundColor))]
    object? value;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(QualityColor))]
    string quality = "—";

    [ObservableProperty]
    string updatedAt = "—";

    public string DisplayValue => ValueFormatting.FormatDisplay(_tag, Value, _globalPrecision);

    public bool? BoolIsOn => Value is bool flag ? flag : null;

    public string BoolStatusText => Value switch
    {
        bool flag => flag ? "运行中" : "已停止",
        null => "—",
        _ => DisplayValue
    };

    public string BoolIndicatorText => Value is bool flag ? (flag ? "开" : "关") : "—";

    public Color ValueColor
    {
        get
        {
            if (Value is bool flag)
                return Color.FromArgb(flag ? "#3DDC97" : "#FF6B6B");

            return Color.FromArgb(CategoryAccentColor);
        }
    }

    public Color CardBorderColor
    {
        get
        {
            if (Value is bool flag)
                return Color.FromArgb(flag ? "#3DDC97" : "#FF6B6B");

            return Color.FromArgb(CategoryAccentColor);
        }
    }

    public Color CardBackgroundColor
    {
        get
        {
            if (Value is bool flag)
                return Color.FromArgb(flag ? "#143328" : "#331E24");

            return Category switch
            {
                TagDisplayCategory.Temperature => Color.FromArgb("#1E2430"),
                TagDisplayCategory.Process => Color.FromArgb("#152536"),
                TagDisplayCategory.Setting => Color.FromArgb("#182030"),
                _ => Color.FromArgb("#152536")
            };
        }
    }

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
