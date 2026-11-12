using HuaGuang.Monitor.Models;

namespace HuaGuang.Monitor.Services;

/// <summary>开关类点位显示与 MQTT 发布统一为中文文本（可配置多档状态）。</summary>
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

        return ShortenIndicator(text);
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

        if (value is string existing
            && (existing is "开" or "关" or "待机"
                || LooksLikeKnownLabel(tag, existing)))
        {
            text = existing;
            return true;
        }

        var map = ResolveMap(tag);
        if (map is not null && RunStatusFormatting.TryGetCode(value) is int code)
        {
            text = SwitchStatusLabelsParser.FormatLabel(map, code);
            return true;
        }

        if (tag.DataType == TagDataType.Bool || value is bool)
        {
            text = IsOn(value) ? "开" : "关";
            return true;
        }

        return false;
    }

    public static string GetAccentColor(PlcTag tag, object? value) =>
        TryResolveColorCode(tag, value, out var code)
            ? GetAccentColor(code)
            : value is bool flag
                ? (flag ? "#3DDC97" : "#FF6B6B")
                : "#8AA0B5";

    public static string GetBackgroundColor(PlcTag tag, object? value) =>
        TryResolveColorCode(tag, value, out var code)
            ? GetBackgroundColor(code)
            : value is bool flag
                ? (flag ? "#143328" : "#331E24")
                : "#152536";

    /// <summary>
    /// 采集值是状态码；订阅收到的是产线点表里的中文文案。文案先按点表反查码，再上色。
    /// </summary>
    static bool TryResolveColorCode(PlcTag tag, object? value, out int code)
    {
        if (value is string text && TryResolveLabelCode(tag, text, out code))
        {
            return true;
        }

        if (RunStatusFormatting.TryGetCode(value) is int parsed)
        {
            code = parsed;
            return true;
        }

        code = 0;
        return false;
    }

    static bool TryResolveLabelCode(PlcTag tag, string text, out int code)
    {
        code = 0;
        var map = ResolveMap(tag);
        if (map is null || int.TryParse(text, out _))
        {
            return false;
        }

        foreach (var pair in map.OrderBy(pair => pair.Key))
        {
            if (string.Equals(pair.Value, text, StringComparison.Ordinal))
            {
                code = pair.Key;
                return true;
            }
        }

        return false;
    }

    public static string GetAccentColor(int code) => code switch
    {
        0 => "#FF6B6B",
        1 => "#3DDC97",
        2 => "#FFB347",
        3 => "#4DA3FF",
        4 => "#B388FF",
        _ => "#8AA0B5"
    };

    public static string GetBackgroundColor(int code) => code switch
    {
        0 => "#331E24",
        1 => "#143328",
        2 => "#332A14",
        3 => "#142433",
        4 => "#241433",
        _ => "#152536"
    };

    static IReadOnlyDictionary<int, string>? ResolveMap(PlcTag tag)
    {
        if (SwitchStatusLabelsParser.TryParse(tag.SwitchStatusLabels, out var custom, out _)
            && custom.Count > 0)
        {
            return custom;
        }

        if (RunStatusFormatting.IsRunStatusTag(tag))
        {
            return SwitchStatusLabelsParser.DefaultRunStatusMap;
        }

        return null;
    }

    static bool LooksLikeKnownLabel(PlcTag tag, string existing)
    {
        var map = ResolveMap(tag);
        return map is not null && map.Values.Any(label =>
            string.Equals(label, existing, StringComparison.Ordinal));
    }

    static string ShortenIndicator(string text) => text switch
    {
        "待机" => "待",
        "手动模式" => "手",
        "自动模式" => "自",
        "自动中" => "自",
        "清洗模式" => "洗",
        "清洗中" => "洗",
        "开" or "关" => text,
        _ when text.Length <= 2 => text,
        _ => text[..1]
    };

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
