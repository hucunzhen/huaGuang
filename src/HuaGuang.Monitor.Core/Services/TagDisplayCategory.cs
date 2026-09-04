using HuaGuang.Monitor.Models;

namespace HuaGuang.Monitor.Services;

public static class TagDisplayCategoryHelper
{
    public static TagDisplayCategory Resolve(PlcTag tag, object? value = null) =>
        tag.DisplayCategory ?? InferCategory(tag, value);

    public static TagDisplayCategory InferCategory(PlcTag tag, object? value = null)
    {
        if (RunStatusFormatting.IsRunStatusTag(tag))
            return TagDisplayCategory.Switch;

        if (tag.DataType == TagDataType.Bool || value is bool)
            return TagDisplayCategory.Switch;

        if (CurrentInjectionFormatting.IsRelatedTag(tag))
            return TagDisplayCategory.Temperature;

        if (tag.IsManual)
            return TagDisplayCategory.Setting;

        if (tag.IsTemperature)
            return TagDisplayCategory.Temperature;

        if (tag.DataType is TagDataType.Float32 or TagDataType.Int16 or TagDataType.UInt16
            or TagDataType.Int32 or TagDataType.UInt32)
            return TagDisplayCategory.Process;

        return TagDisplayCategory.Other;
    }

    public static string GetTitle(TagDisplayCategory category) => category switch
    {
        TagDisplayCategory.Switch => "开关状态",
        TagDisplayCategory.Temperature => "温度监测",
        TagDisplayCategory.Process => "工艺参数",
        TagDisplayCategory.Setting => "设定参数",
        _ => "其他"
    };

    public static string GetAccentColor(TagDisplayCategory category) => category switch
    {
        TagDisplayCategory.Switch => "#3DDC97",
        TagDisplayCategory.Temperature => "#FF8C42",
        TagDisplayCategory.Process => "#2EC4B6",
        TagDisplayCategory.Setting => "#7B9FD4",
        _ => "#8AA0B5"
    };

    public static int GetSortOrder(TagDisplayCategory category) => (int)category;

    public static string ToLabel(TagDisplayCategory category) => GetTitle(category);

    public static bool TryParseLabel(string? text, out TagDisplayCategory category)
    {
        category = TagDisplayCategory.Other;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var normalized = text.Trim();
        foreach (TagDisplayCategory value in Enum.GetValues<TagDisplayCategory>())
        {
            if (normalized.Equals(value.ToString(), StringComparison.OrdinalIgnoreCase) ||
                normalized.Equals(GetTitle(value), StringComparison.Ordinal))
            {
                category = value;
                return true;
            }
        }

        return false;
    }
}
