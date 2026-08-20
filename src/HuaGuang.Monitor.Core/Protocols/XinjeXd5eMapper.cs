using System.Globalization;
using System.Text.RegularExpressions;
using HuaGuang.Monitor.Models;

namespace HuaGuang.Monitor.Protocols;

public readonly record struct XinjeResolvedAddress(ModbusTable Table, ushort Address, bool IsBit, string Normalized);

/// <summary>
/// 信捷 XD5E（含 XD5E-60T10）内部软元件 → Modbus TCP 地址。
/// X/Y 按八进制编号：X7 的下一个是 X10，不是 X8。
/// </summary>
public static partial class XinjeXd5eMapper
{
    static readonly Regex AddressPattern = AddressRegex();

    public static bool TryResolve(string? input, out XinjeResolvedAddress resolved, out string error)
    {
        resolved = default;
        error = string.Empty;
        if (string.IsNullOrWhiteSpace(input))
        {
            error = "请填写信捷地址，例如 D100、M0、X20。";
            return false;
        }

        var match = AddressPattern.Match(input.Trim());
        if (!match.Success)
        {
            error = "地址格式应为 元件+编号，例如 D100、HD0、M10、X20、Y0。";
            return false;
        }

        var prefix = match.Groups[1].Value.ToUpperInvariant();
        if (!int.TryParse(match.Groups[2].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var number))
        {
            error = "元件编号无效。";
            return false;
        }

        if (prefix is "X" or "Y")
        {
            if (!TryResolveXy(prefix, number, out var xyAddress, out error))
            {
                return false;
            }

            resolved = new XinjeResolvedAddress(ModbusTable.Coil, xyAddress, true, $"{prefix}{number}");
            return true;
        }

        foreach (var range in WordAndBitRanges)
        {
            if (range.Prefix != prefix || number < range.Min || number > range.Max)
            {
                continue;
            }

            var modbus = range.ModbusOfMin + (number - range.Min);
            if (modbus is < 0 or > ushort.MaxValue)
            {
                error = $"{prefix}{number} 超出 Modbus 地址范围。";
                return false;
            }

            resolved = new XinjeResolvedAddress(range.Table, (ushort)modbus, range.IsBit, $"{prefix}{number}");
            return true;
        }

        error = $"不支持的 XD5E 元件 {prefix}{number}。常用：D/HD/M/X/Y/T/C/S。";
        return false;
    }

    public static void ApplyTo(PlcTag tag)
    {
        if (!TryResolve(tag.XinjeAddress, out var resolved, out var error))
        {
            throw new InvalidOperationException(error);
        }

        tag.XinjeAddress = resolved.Normalized;
        tag.Table = resolved.Table;
        tag.Address = resolved.Address;
        if (resolved.IsBit)
        {
            tag.DataType = TagDataType.Bool;
        }
    }

    static bool TryResolveXy(string prefix, int number, out ushort address, out string error)
    {
        address = 0;
        error = string.Empty;
        var (main, ext1, ext2, ext3) = prefix == "X"
            ? (20480, 20736, 22736, 23536)
            : (24576, 24832, 26832, 27632);

        if (TryOctalInRange(number, 0, 77, out var mainOffset))
        {
            address = (ushort)(main + mainOffset);
            return true;
        }

        if (number is >= 10000 and <= 11777 && TryOctalDigits(number - 10000, out var ext1Offset) && ext1Offset <= 1023)
        {
            address = (ushort)(ext1 + ext1Offset);
            return true;
        }

        if (number is >= 20000 and <= 20177 && TryOctalDigits(number - 20000, out var ext2Offset) && ext2Offset <= 127)
        {
            address = (ushort)(ext2 + ext2Offset);
            return true;
        }

        if (number is >= 30000 and <= 30077 && TryOctalInRange(number - 30000, 0, 77, out var ext3Offset))
        {
            address = (ushort)(ext3 + ext3Offset);
            return true;
        }

        error = $"{prefix}{number} 不是合法的八进制点号。本体范围 {prefix}0–{prefix}77（XD5E-60T10 输入约 X0–X43，输出约 Y0–Y27）。";
        return false;
    }

    static bool TryOctalInRange(int written, int min, int max, out int linear)
    {
        linear = 0;
        if (written < min || written > max || !TryOctalDigits(written, out linear))
        {
            return false;
        }

        return true;
    }

    static bool TryOctalDigits(int written, out int linear)
    {
        linear = 0;
        try
        {
            linear = Convert.ToInt32(written.ToString(CultureInfo.InvariantCulture), 8);
            return true;
        }
        catch (ArgumentException)
        {
            return false;
        }
        catch (FormatException)
        {
            return false;
        }
    }

    static readonly AreaRange[] WordAndBitRanges =
    [
        new("HSCD", 0, 39, 50304, ModbusTable.HoldingRegister, false),
        new("HSD", 0, 1023, 47232, ModbusTable.HoldingRegister, false),
        new("HTD", 0, 1023, 48256, ModbusTable.HoldingRegister, false),
        new("HCD", 0, 1023, 49280, ModbusTable.HoldingRegister, false),
        new("ETD", 0, 39, 40960, ModbusTable.HoldingRegister, false),
        new("SFD", 0, 4095, 58560, ModbusTable.HoldingRegister, false),
        new("SEM", 0, 127, 49280, ModbusTable.Coil, true),
        new("HSC", 0, 39, 59648, ModbusTable.Coil, true),
        new("HM", 0, 6143, 49408, ModbusTable.Coil, true),
        new("HS", 0, 999, 55552, ModbusTable.Coil, true),
        new("HT", 0, 1023, 57600, ModbusTable.Coil, true),
        new("HC", 0, 1023, 58624, ModbusTable.Coil, true),
        new("SM", 0, 4095, 36864, ModbusTable.Coil, true),
        new("ET", 0, 39, 49152, ModbusTable.Coil, true),
        new("ID", 0, 99, 20480, ModbusTable.HoldingRegister, false),
        new("ID", 10000, 11599, 20736, ModbusTable.HoldingRegister, false),
        new("ID", 20000, 20199, 22736, ModbusTable.HoldingRegister, false),
        new("ID", 30000, 30099, 23536, ModbusTable.HoldingRegister, false),
        new("QD", 0, 99, 24576, ModbusTable.HoldingRegister, false),
        new("QD", 10000, 11599, 24832, ModbusTable.HoldingRegister, false),
        new("QD", 20000, 20199, 26832, ModbusTable.HoldingRegister, false),
        new("QD", 30000, 30099, 27632, ModbusTable.HoldingRegister, false),
        new("SD", 0, 4095, 28672, ModbusTable.HoldingRegister, false),
        new("TD", 0, 4095, 32768, ModbusTable.HoldingRegister, false),
        new("CD", 0, 4095, 36864, ModbusTable.HoldingRegister, false),
        new("FD", 0, 8199, 50368, ModbusTable.HoldingRegister, false),
        new("FS", 0, 47, 62656, ModbusTable.HoldingRegister, false),
        new("HD", 0, 6143, 41088, ModbusTable.HoldingRegister, false),
        new("M", 0, 20479, 0, ModbusTable.Coil, true),
        new("S", 0, 7999, 28672, ModbusTable.Coil, true),
        new("T", 0, 4095, 40960, ModbusTable.Coil, true),
        new("C", 0, 4095, 45056, ModbusTable.Coil, true),
        new("D", 0, 20479, 0, ModbusTable.HoldingRegister, false)
    ];

    readonly record struct AreaRange(string Prefix, int Min, int Max, int ModbusOfMin, ModbusTable Table, bool IsBit);

    [GeneratedRegex(@"^([A-Za-z]+)(\d+)$", RegexOptions.CultureInvariant)]
    private static partial Regex AddressRegex();
}
