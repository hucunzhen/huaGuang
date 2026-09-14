using HuaGuang.Monitor.Models;

namespace HuaGuang.Monitor.Protocols;

public static class S7ByteConverter
{
    public static int ByteCount(TagDataType type) => type switch
    {
        TagDataType.Bool => 1,
        TagDataType.Int16 or TagDataType.UInt16 => 2,
        TagDataType.Int32 or TagDataType.UInt32 or TagDataType.Float32 => 4,
        _ => throw new ArgumentOutOfRangeException(nameof(type), type, "不支持的数据类型")
    };

    public static object ToValue(byte[] buffer, int offset, TagDataType type, ByteOrder order, int bitOffset = 0)
    {
        if (type == TagDataType.Bool)
        {
            return ((buffer[offset] >> bitOffset) & 1) == 1;
        }

        if (type is TagDataType.Int16 or TagDataType.UInt16)
        {
            var registers = new[] { ReadUInt16(buffer, offset, order) };
            return RegisterConverter.ToValue(registers, type, ByteOrder.ABCD);
        }

        var pair = new[]
        {
            ReadUInt16(buffer, offset, order),
            ReadUInt16(buffer, offset + 2, order)
        };
        return RegisterConverter.ToValue(pair, type, order);
    }

    static ushort ReadUInt16(byte[] buffer, int offset, ByteOrder order)
    {
        var ab = (ushort)((buffer[offset] << 8) | buffer[offset + 1]);
        return order switch
        {
            ByteOrder.BADC or ByteOrder.DCBA => SwapBytes(ab),
            _ => ab
        };
    }

    static ushort SwapBytes(ushort value) => (ushort)((value >> 8) | (value << 8));
}
