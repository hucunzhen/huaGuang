using HuaGuang.Monitor.Models;

namespace HuaGuang.Monitor.Services;

public static class TagComputedEvaluator
{
    public static bool TryEvaluateTag(
        PlcTag tag,
        IReadOnlyDictionary<string, object?> values,
        out object result,
        out string error)
    {
        result = 0d;
        error = string.Empty;
        if (!tag.IsComputed)
        {
            error = "不是计算点位。";
            return false;
        }

        if (!TagExpressionEngine.TryEvaluate(tag.Expression, values, out var number, out error))
        {
            return false;
        }

        var scaled = number * tag.Scale + tag.Offset;
        result = tag.DataType switch
        {
            TagDataType.Bool => scaled != 0,
            TagDataType.Int16 => Convert.ToInt16(Math.Clamp(scaled, short.MinValue, short.MaxValue)),
            TagDataType.UInt16 => Convert.ToUInt16(Math.Clamp(scaled, 0, ushort.MaxValue)),
            TagDataType.Int32 => Convert.ToInt32(Math.Clamp(scaled, int.MinValue, int.MaxValue)),
            TagDataType.UInt32 => Convert.ToUInt32(Math.Clamp(Math.Floor(scaled), 0, uint.MaxValue)),
            _ => scaled
        };
        return true;
    }
}
