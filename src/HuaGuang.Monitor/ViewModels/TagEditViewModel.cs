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
    readonly DashboardViewModel _dashboard;
    string? _tagId;

    public TagEditViewModel(SettingsStore store, AcquisitionService acquisition, DashboardViewModel dashboard)
    {
        _store = store;
        _acquisition = acquisition;
        _dashboard = dashboard;
        ResetForNew();
    }

    public string[] DataTypes { get; } = Enum.GetNames<TagDataType>();
    public string[] ByteOrders { get; } = Enum.GetNames<ByteOrder>();

    [ObservableProperty] string title = "新增点位";
    [ObservableProperty] string name = string.Empty;
    [ObservableProperty] string unit = string.Empty;
    [ObservableProperty] bool enabled = true;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ResolvedHint))]
    string xinjeAddress = "D0";
    [ObservableProperty] string dataTypeName = nameof(TagDataType.Float32);
    [ObservableProperty] string byteOrderName = nameof(Models.ByteOrder.CDAB);
    [ObservableProperty] string scale = "1";
    [ObservableProperty] string offset = "0";
    [ObservableProperty] string statusMessage = string.Empty;

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
                XinjeAddress = string.IsNullOrWhiteSpace(tag.XinjeAddress) ? $"D{tag.Address}" : tag.XinjeAddress;
                DataTypeName = tag.DataType.ToString();
                ByteOrderName = tag.ByteOrder.ToString();
                Scale = tag.Scale.ToString("0.####");
                Offset = tag.Offset.ToString("0.####");
                return;
            }
        }

        ResetForNew();
    }

    [RelayCommand]
    async Task SaveAsync()
    {
        if (_acquisition.IsRunning)
        {
            StatusMessage = "请先停止采集再修改点位。";
            return;
        }

        if (string.IsNullOrWhiteSpace(Name))
        {
            StatusMessage = "请填写点位名称。";
            return;
        }

        if (!XinjeXd5eMapper.TryResolve(XinjeAddress, out _, out var addressError))
        {
            StatusMessage = addressError;
            return;
        }

        if (!Enum.TryParse<TagDataType>(DataTypeName, out var dataType) ||
            !Enum.TryParse<ByteOrder>(ByteOrderName, out var byteOrder))
        {
            StatusMessage = "点位类型无效。";
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

        var tag = _store.Current.Tags.FirstOrDefault(t => t.Id == _tagId) ?? new PlcTag();
        tag.Name = Name.Trim();
        tag.Unit = Unit.Trim();
        tag.Enabled = Enabled;
        tag.XinjeAddress = XinjeAddress.Trim();
        tag.DataType = dataType;
        tag.ByteOrder = byteOrder;
        tag.Scale = scale;
        tag.Offset = offset;
        XinjeXd5eMapper.ApplyTo(tag);

        if (_store.Current.Tags.All(t => t.Id != tag.Id))
        {
            _store.Current.Tags.Add(tag);
        }

        await _store.SaveAsync(_store.Current);
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
        XinjeAddress = "D0";
        DataTypeName = nameof(TagDataType.Float32);
        ByteOrderName = nameof(Models.ByteOrder.CDAB);
        Scale = "1";
        Offset = "0";
        StatusMessage = string.Empty;
    }
}
