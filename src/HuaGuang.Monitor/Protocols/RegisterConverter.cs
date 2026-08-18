using HuaGuang.Monitor.Models;

namespace HuaGuang.Monitor.Protocols;

public static class RegisterConverter
{
    public static int RegisterCount(TagDataType type) => type switch
    {
        TagDataType.Bool => 1,
        TagDataType.Int16 or TagDataType.UInt16 => 1,
        TagDataType.Int32 or TagDataType.UInt32 or TagDataType.Float32 => 2,
        _ => throw new ArgumentOutOfRangeException(nameof(type), type, "不支持的数据类型")
    };

    public static object ToValue(ushort[] registers, TagDataType type, ByteOrder order)
    {
        return type switch
        {
            TagDataType.Int16 => unchecked((short)registers[0]),
            TagDataType.UInt16 => registers[0],
            TagDataType.Int32 => unchecked((int)ToUInt32(registers, order)),
            TagDataType.UInt32 => ToUInt32(registers, order),
            TagDataType.Float32 => ToSingle(registers, order),
            _ => throw new ArgumentOutOfRangeException(nameof(type), type, "不支持的数据类型")
        };
    }

    public static object ApplyScale(object raw, PlcTag tag)
    {
        if (tag.DataType == TagDataType.Bool)
        {
            return raw;
        }

        var engineering = Convert.ToDouble(raw) * tag.Scale + tag.Offset;
        if (tag.DataType == TagDataType.Float32 || Math.Abs(tag.Scale - 1) > double.Epsilon || Math.Abs(tag.Offset) > double.Epsilon)
        {
            return Math.Round(engineering, 4);
        }

        return tag.DataType switch
        {
            TagDataType.Int16 => Convert.ToInt16(engineering),
            TagDataType.UInt16 => Convert.ToUInt16(engineering),
            TagDataType.Int32 => Convert.ToInt32(engineering),
            TagDataType.UInt32 => Convert.ToUInt32(engineering),
            _ => engineering
        };
    }

    static uint ToUInt32(ushort[] registers, ByteOrder order)
    {
        var first = registers[0];
        var second = registers[1];
        return order switch
        {
            ByteOrder.ABCD => ((uint)first << 16) | second,
            ByteOrder.CDAB => ((uint)second << 16) | first,
            ByteOrder.BADC => ((uint)SwapBytes(first) << 16) | SwapBytes(second),
            ByteOrder.DCBA => ((uint)SwapBytes(second) << 16) | SwapBytes(first),
            _ => ((uint)first << 16) | second
        };
    }

    static float ToSingle(ushort[] registers, ByteOrder order) =>
        BitConverter.UInt32BitsToSingle(ToUInt32(registers, order));

    static ushort SwapBytes(ushort value) => (ushort)((value >> 8) | (value << 8));
}
