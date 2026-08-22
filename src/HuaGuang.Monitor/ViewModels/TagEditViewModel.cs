using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HuaGuang.Monitor.Models;
using HuaGuang.Monitor.Protocols;
using HuaGuang.Monitor.Services;

namespace HuaGuang.Monitor.ViewModels;

public partial class TagEditViewModel : ObservableObject, IQueryAttributable
{
    readonly SettingsStore _store;
    readonly AcquisitionService _acquisition;
    readonly SubscriptionService _subscription;
    readonly DashboardViewModel _dashboard;
    string? _tagId;

    public TagEditViewModel(
        SettingsStore store,
        AcquisitionService acquisition,
        SubscriptionService subscription,
        DashboardViewModel dashboard)
    {
        _store = store;
        _acquisition = acquisition;
        _subscription = subscription;
        _dashboard = dashboard;
        ResetForNew();
    }

    public string[] DataTypes { get; } = Enum.GetNames<TagDataType>();
    public string[] PlcDataTypes { get; } = Enum.GetNames<TagDataType>()
        .Where(name => name != nameof(TagDataType.String))
        .ToArray();
    public string[] ByteOrders { get; } = Enum.GetNames<ByteOrder>();
    public string[] SourceTypes { get; } = ["PLC 采集", "手动输入"];
    public string[] DisplayCategoryOptions { get; } =
    [
        "自动推断",
        TagDisplayCategoryHelper.GetTitle(TagDisplayCategory.Switch),
        TagDisplayCategoryHelper.GetTitle(TagDisplayCategory.Temperature),
        TagDisplayCategoryHelper.GetTitle(TagDisplayCategory.Process),
        TagDisplayCategoryHelper.GetTitle(TagDisplayCategory.Setting),
        TagDisplayCategoryHelper.GetTitle(TagDisplayCategory.Other),
    ];

    [ObservableProperty] string title = "新增点位";
    [ObservableProperty] string name = string.Empty;
    [ObservableProperty] string unit = string.Empty;
    [ObservableProperty] bool enabled = true;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsPlcSource))]
    [NotifyPropertyChangedFor(nameof(IsManualSource))]
    string selectedSourceType = "PLC 采集";
    [ObservableProperty] string manualValue = string.Empty;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ResolvedHint))]
    string xinjeAddress = "D0";
    [ObservableProperty] string dataTypeName = nameof(TagDataType.Float32);
    [ObservableProperty] string byteOrderName = nameof(Models.ByteOrder.CDAB);
    [ObservableProperty] string scale = "1";
    [ObservableProperty] string offset = "0";
    [ObservableProperty] string displayPrecision = string.Empty;
    [ObservableProperty] string mqttField = string.Empty;
    [ObservableProperty] string selectedDisplayCategoryOption = "自动推断";
    [ObservableProperty] string statusMessage = string.Empty;

    public bool IsPlcSource => SelectedSourceType == "PLC 采集";
    public bool IsManualSource => SelectedSourceType == "手动输入";
    public bool ShowPrecision =>
        DataTypeName != nameof(TagDataType.String) && DataTypeName != nameof(TagDataType.Bool);

    partial void OnDataTypeNameChanged(string value) => OnPropertyChanged(nameof(ShowPrecision));

    partial void OnSelectedSourceTypeChanged(string value)
    {
        if (value == "PLC 采集" && DataTypeName == nameof(TagDataType.String))
        {
            DataTypeName = nameof(TagDataType.Float32);
        }
    }

    public string ResolvedHint
    {
        get
        {
            if (!XinjeXd5eMapper.TryResolve(XinjeAddress, out var resolved, out var error))
            {
                return error;
            }

            return resolved.IsBit
                ? $"XD5E 线圈  {resolved.Normalized} → {resolved.Address}"
                : $"XD5E 保持寄存器  {resolved.Normalized} → {resolved.Address}";
        }
    }

    public void ApplyQueryAttributes(IDictionary<string, object> query)
    {
        if (query.TryGetValue("id", out var value) && value is string id && !string.IsNullOrWhiteSpace(id))
        {
            _tagId = id;
            var tag = _store.Current.Tags.FirstOrDefault(t => t.Id == id);
            if (tag is not null)
            {
                Title = "编辑点位";
                Name = tag.Name;
                Unit = tag.Unit;
                Enabled = tag.Enabled;
                SelectedSourceType = tag.IsManual ? "手动输入" : "PLC 采集";
                ManualValue = tag.ManualValue;
                XinjeAddress = string.IsNullOrWhiteSpace(tag.XinjeAddress) ? $"D{tag.Address}" : tag.XinjeAddress;
                DataTypeName = tag.DataType.ToString();
                ByteOrderName = tag.ByteOrder.ToString();
                Scale = tag.Scale.ToString("0.####");
                Offset = tag.Offset.ToString("0.####");
                DisplayPrecision = tag.DisplayPrecision?.ToString() ?? string.Empty;
                MqttField = tag.MqttField;
                SelectedDisplayCategoryOption = tag.DisplayCategory is { } category
                    ? TagDisplayCategoryHelper.ToLabel(category)
                    : "自动推断";
                return;
            }
        }

        ResetForNew();
    }

    [RelayCommand]
    async Task SaveAsync()
    {
        if (_acquisition.IsRunning || _subscription.IsRunning)
        {
            StatusMessage = "请先停止采集/订阅再修改点位。";
            return;
        }

        if (string.IsNullOrWhiteSpace(Name))
        {
            StatusMessage = "请填写点位名称。";
            return;
        }

        if (!Enum.TryParse<TagDataType>(DataTypeName, out var dataType) ||
            !Enum.TryParse<ByteOrder>(ByteOrderName, out var byteOrder))
        {
            StatusMessage = "点位类型无效。";
            return;
        }

        var tag = _store.Current.Tags.FirstOrDefault(t => t.Id == _tagId) ?? new PlcTag();
        tag.Name = Name.Trim();
        tag.Unit = Unit.Trim();
        tag.Enabled = Enabled;
        tag.DataType = dataType;
        tag.ByteOrder = byteOrder;

        if (!TryParseOptionalPrecision(DisplayPrecision, out var precision, out var precisionError))
        {
            StatusMessage = precisionError;
            return;
        }

        tag.DisplayPrecision = precision;
        tag.MqttField = MqttField.Trim();
        tag.DisplayCategory = SelectedDisplayCategoryOption == "自动推断"
            ? null
            : TagDisplayCategoryHelper.TryParseLabel(SelectedDisplayCategoryOption, out var category)
                ? category
                : TagDisplayCategoryHelper.InferCategory(tag);

        if (IsManualSource)
        {
            tag.Source = TagSource.Manual;
            tag.ManualValue = ManualValue.Trim();
            if (string.IsNullOrWhiteSpace(tag.ManualValue))
            {
                StatusMessage = "请填写手动值。";
                return;
            }

            if (dataType != TagDataType.String)
            {
                try
                {
                    _ = ValueFormatting.ResolveManualValue(tag);
                }
                catch
                {
                    StatusMessage = "手动值与数据类型不匹配。";
                    return;
                }
            }
        }
        else
        {
            if (!XinjeXd5eMapper.TryResolve(XinjeAddress, out _, out var addressError))
            {
                StatusMessage = addressError;
                return;
            }

            if (!double.TryParse(Scale, out var scale))
            {
                scale = 1;
            }

            if (!double.TryParse(Offset, out var offset))
            {
                offset = 0;
            }

            tag.Source = TagSource.Plc;
            tag.ManualValue = string.Empty;
            tag.XinjeAddress = XinjeAddress.Trim();
            tag.Scale = scale;
            tag.Offset = offset;
            XinjeXd5eMapper.ApplyTo(tag);
        }

        if (_store.Current.Tags.All(t => t.Id != tag.Id))
        {
            _store.Current.Tags.Add(tag);
        }

        await _store.SaveAsync(_store.Current);
        LineConfigPaths.SaveCurrentLine(_store.Current);
        _dashboard.Reload();
        await Shell.Current.GoToAsync("..");
    }

    void ResetForNew()
    {
        _tagId = null;
        Title = "新增点位";
        Name = string.Empty;
        Unit = string.Empty;
        Enabled = true;
        SelectedSourceType = "手动输入";
        ManualValue = string.Empty;
        XinjeAddress = "D0";
        DataTypeName = nameof(TagDataType.String);
        ByteOrderName = nameof(Models.ByteOrder.CDAB);
        Scale = "1";
        Offset = "0";
        DisplayPrecision = string.Empty;
        MqttField = string.Empty;
        SelectedDisplayCategoryOption = "自动推断";
        StatusMessage = string.Empty;
    }

    static bool TryParseOptionalPrecision(string text, out int? precision, out string error)
    {
        precision = null;
        error = string.Empty;
        if (string.IsNullOrWhiteSpace(text))
        {
            return true;
        }

        if (!int.TryParse(text.Trim(), out var value))
        {
            error = "精度应为 0–4 的整数，或留空使用全局默认。";
            return false;
        }

        if (value is < 0 or > 4)
        {
            error = "精度范围 0–4。";
            return false;
        }

        precision = value;
        return true;
    }
}
