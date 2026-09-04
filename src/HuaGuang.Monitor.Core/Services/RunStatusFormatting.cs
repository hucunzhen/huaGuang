using HuaGuang.Monitor.Models;

namespace HuaGuang.Monitor.Services;

/// <summary>
/// 运行状态点位：0=关，1=开，2=待机。
/// </summary>
public static class RunStatusFormatting
{
    public const string TagName = "运行状态";

    public static bool IsRunStatusTag(PlcTag tag) =>
        string.Equals(tag.Name, TagName, StringComparison.Ordinal);

    public static void MigrateTags(IEnumerable<PlcTag> tags)
    {
        foreach (var tag in tags.Where(IsRunStatusTag))
        {
            tag.DataType = TagDataType.Int16;
            tag.DisplayCategory ??= TagDisplayCategory.Switch;
        }
    }

    public static int? TryGetCode(object? value) => value switch
    {
        null => null,
        bool flag => flag ? 1 : 0,
        int number => number,
        short number => number,
        long number => (int)number,
        byte number => number,
        uint number => (int)number,
        ushort number => number,
        double number => (int)Math.Round(number),
        float number => (int)Math.Round(number),
        decimal number => (int)Math.Round(number),
        string text => text switch
        {
            "开" or "ON" or "on" => 1,
            "关" or "OFF" or "off" => 0,
            "待机" => 2,
            _ => int.TryParse(text, out var parsed) ? parsed : null
        },
        _ => int.TryParse(Convert.ToString(value), out var parsed) ? parsed : null
    };

    public static string GetStatusText(object? value) =>
        TryGetCode(value) is int code ? GetStatusText(code) : "—";

    public static string GetStatusText(int code) => code switch
    {
        0 => "关",
        1 => "开",
        2 => "待机",
        _ => $"未知({code})"
    };

    public static string GetIndicatorText(object? value) =>
        TryGetCode(value) is int code ? GetIndicatorText(code) : "—";

    public static string GetIndicatorText(int code) => code switch
    {
        0 => "关",
        1 => "开",
        2 => "待",
        _ => "?"
    };

    public static string GetAccentColor(object? value) =>
        TryGetCode(value) is int code ? GetAccentColor(code) : "#8AA0B5";

    public static string GetAccentColor(int code) => code switch
    {
        0 => "#FF6B6B",
        1 => "#3DDC97",
        2 => "#FFB347",
        _ => "#8AA0B5"
    };

    public static string GetBackgroundColor(object? value) =>
        TryGetCode(value) is int code ? GetBackgroundColor(code) : "#152536";

    public static string GetBackgroundColor(int code) => code switch
    {
        0 => "#331E24",
        1 => "#143328",
        2 => "#332A14",
        _ => "#152536"
    };
}
