using HuaGuang.Monitor.Models;
using HuaGuang.Monitor.Services;

namespace HuaGuang.Monitor.ViewModels;

public sealed class TagConfigViewModel
{
    public TagConfigViewModel(PlcTag tag)
    {
        Tag = tag;
    }

    public PlcTag Tag { get; }

    public TagDisplayCategory Category => TagDisplayCategoryHelper.Resolve(Tag);

    public string DisplayCategoryLabel => Tag.DisplayCategory is null
        ? $"{TagDisplayCategoryHelper.ToLabel(Category)}（自动）"
        : TagDisplayCategoryHelper.ToLabel(Category);

    public Color AccentColor => Color.FromArgb(TagDisplayCategoryHelper.GetAccentColor(Category));

    public string SourceLabel => Tag.IsManual ? "手动输入" : "PLC 采集";

    public string MqttFieldLabel => string.IsNullOrWhiteSpace(Tag.MqttField)
        ? "（按名称或映射表）"
        : Tag.MqttField;

    public string EnabledLabel => Tag.Enabled ? "启用" : "停用";

    public string TypeSummary =>
        $"{SourceLabel} · {Tag.DataType} · {(string.IsNullOrWhiteSpace(Tag.Unit) ? "—" : Tag.Unit)}";

    public string MqttFieldSummary => $"MQTT 字段：{MqttFieldLabel}";

    public Color EnabledColor => Tag.Enabled
        ? Color.FromArgb("#3DDC97")
        : Color.FromArgb("#8AA0B5");
}
