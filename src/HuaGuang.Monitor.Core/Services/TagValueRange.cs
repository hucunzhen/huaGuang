using HuaGuang.Monitor.Models;

namespace HuaGuang.Monitor.Services;

/// <summary>点位有效值范围校验：超出则视为读取不成功。</summary>
public static class TagValueRange
{
    public static bool HasRange(PlcTag tag) =>
        tag.ValidMin is not null || tag.ValidMax is not null;

    public static bool TryValidateConfig(PlcTag tag, out string error)
    {
        error = string.Empty;
        if (tag.ValidMin is double min && tag.ValidMax is double max && min > max)
        {
            error = "有效下限不能大于有效上限。";
            return false;
        }

        return true;
    }

    /// <summary>
    /// 对数值点位校验范围；无范围配置、Bool/字符串或无法解析为数字时直接通过。
    /// </summary>
    public static bool TryValidateValue(PlcTag tag, object? value, out string error)
    {
        error = string.Empty;
        if (!HasRange(tag))
        {
            return true;
        }

        if (tag.DataType is TagDataType.Bool or TagDataType.String)
        {
            return true;
        }

        if (!ValueFormatting.TryAsDouble(value, out var number))
        {
            error = "无法解析为数值，无法做范围校验。";
            return false;
        }

        if (tag.ValidMin is double min && number < min)
        {
            error = $"值 {FormatNumber(number)} 低于有效下限 {FormatNumber(min)}。";
            return false;
        }

        if (tag.ValidMax is double max && number > max)
        {
            error = $"值 {FormatNumber(number)} 高于有效上限 {FormatNumber(max)}。";
            return false;
        }

        return true;
    }

    public static void EnsureInRangeOrThrow(PlcTag tag, object? value)
    {
        if (!TryValidateValue(tag, value, out var error))
        {
            throw new InvalidOperationException(error);
        }
    }

    public static string Describe(PlcTag tag)
    {
        if (tag.ValidMin is double min && tag.ValidMax is double max)
        {
            return $"[{FormatNumber(min)}, {FormatNumber(max)}]";
        }

        if (tag.ValidMin is double onlyMin)
        {
            return $">= {FormatNumber(onlyMin)}";
        }

        if (tag.ValidMax is double onlyMax)
        {
            return $"<= {FormatNumber(onlyMax)}";
        }

        return "不限";
    }

    static string FormatNumber(double number)
    {
        if (Math.Abs(number - Math.Round(number)) < 1e-9)
        {
            return ((long)Math.Round(number)).ToString();
        }

        return number.ToString("0.####");
    }
}
