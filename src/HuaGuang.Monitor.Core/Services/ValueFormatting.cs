using System.Text.Json;
using HuaGuang.Monitor.Models;

namespace HuaGuang.Monitor.Services;

public static class ValueFormatting
{
    public static int ResolvePrecision(PlcTag tag, int globalDefault) =>
        Math.Clamp(tag.DisplayPrecision ?? globalDefault, 0, 4);

    public static bool SupportsPrecision(PlcTag tag) =>
        tag.DataType is TagDataType.Float32 or TagDataType.Int16 or TagDataType.UInt16
            or TagDataType.Int32 or TagDataType.UInt32;

    public static object ApplyDisplayPrecision(PlcTag tag, object value, int globalDefault)
    {
        if (!SupportsPrecision(tag) || !TryAsDouble(value, out var number))
        {
            return value;
        }

        var precision = ResolvePrecision(tag, globalDefault);
        return Math.Round(number, precision, MidpointRounding.AwayFromZero);
    }

    public static object ApplyTemperaturePrecision(PlcTag tag, object value, int globalDefault) =>
        ApplyDisplayPrecision(tag, value, globalDefault);

    public static string FormatDisplay(PlcTag tag, object? value, int globalDefault)
    {
        if (value is null)
        {
            return "—";
        }

        if (RunStatusFormatting.IsRunStatusTag(tag))
        {
            return RunStatusFormatting.GetStatusText(value);
        }

        if (SwitchStatusFormatting.TryFormatDisplayText(tag, value, out var switchText))
        {
            return switchText;
        }

        if (value is bool flag)
        {
            return flag ? "开" : "关";
        }

        if (SupportsPrecision(tag) && TryAsDouble(value, out var number))
        {
            var precision = ResolvePrecision(tag, globalDefault);
            return number.ToString($"F{precision}");
        }

        return value.ToString() ?? "—";
    }

    public static bool TryAsDouble(object? value, out double number)
    {
        switch (value)
        {
            case null:
                number = 0;
                return false;
            case JsonElement element when element.ValueKind == JsonValueKind.Number:
                number = element.GetDouble();
                return true;
            case JsonElement textElement when textElement.ValueKind == JsonValueKind.String &&
                                              double.TryParse(textElement.GetString(), out number):
                return true;
            case double d:
                number = d;
                return true;
            case float f:
                number = f;
                return true;
            case decimal m:
                number = (double)m;
                return true;
            default:
                try
                {
                    number = Convert.ToDouble(value);
                    return true;
                }
                catch
                {
                    number = 0;
                    return false;
                }
        }
    }

    public static object ResolveManualValue(PlcTag tag)
    {
        var text = tag.ManualValue?.Trim() ?? string.Empty;
        if (tag.DataType == TagDataType.String)
        {
            return text;
        }

        if (string.IsNullOrEmpty(text))
        {
            return tag.DataType == TagDataType.Bool ? false : 0;
        }

        return tag.DataType switch
        {
            TagDataType.Bool => text is "1" or "true" or "True" or "开" or "ON" or "on",
            TagDataType.Int16 => Convert.ToInt16(double.Parse(text)),
            TagDataType.UInt16 => Convert.ToUInt16(double.Parse(text)),
            TagDataType.Int32 => Convert.ToInt32(double.Parse(text)),
            TagDataType.UInt32 => Convert.ToUInt32(double.Parse(text)),
            _ => double.Parse(text)
        };
    }
}
