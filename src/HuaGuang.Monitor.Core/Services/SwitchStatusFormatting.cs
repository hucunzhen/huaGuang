using HuaGuang.Monitor.Models;

namespace HuaGuang.Monitor.Services;

/// <summary>开关类点位显示与 MQTT 发布统一为中文文本（开/关/待机）。</summary>
public static class SwitchStatusFormatting
{
    public static string FormatDisplayText(PlcTag tag, object? value) =>
        TryFormatDisplayText(tag, value, out var text) ? text : "—";

    public static string FormatIndicatorText(PlcTag tag, object? value)
    {
        if (!TryFormatDisplayText(tag, value, out var text))
        {
            return "—";
        }

        return text == "待机" ? "待" : text;
    }

    public static bool TryFormatDisplayText(PlcTag tag, object? value, out string text) =>
        TryFormatMqttText(tag, value, out text);

    public static bool TryFormatMqttText(PlcTag tag, object? value, out string text)
    {
        text = string.Empty;
        if (value is null)
        {
            return false;
        }

        if (value is string existing && existing is "开" or "关" or "待机")
        {
            text = existing;
            return true;
        }

        if (RunStatusFormatting.IsRunStatusTag(tag))
        {
            text = RunStatusFormatting.TryGetCode(value) switch
            {
                0 => "关",
                1 => "开",
                2 => "待机",
                _ => "关"
            };
            return true;
        }

        if (tag.DataType == TagDataType.Bool || value is bool)
        {
            text = IsOn(value) ? "开" : "关";
            return true;
        }

        return false;
    }

    static bool IsOn(object? value) => value switch
    {
        bool flag => flag,
        int number => number != 0,
        short number => number != 0,
        long number => number != 0,
        byte number => number != 0,
        uint number => number != 0,
        ushort number => number != 0,
        double number => number != 0,
        float number => number != 0,
        decimal number => number != 0,
        string text => text is "1" or "true" or "True" or "开" or "ON" or "on",
        _ => false
    };
}
