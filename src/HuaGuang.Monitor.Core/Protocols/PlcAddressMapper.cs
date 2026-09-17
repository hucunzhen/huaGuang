using HuaGuang.Monitor.Models;

namespace HuaGuang.Monitor.Protocols;

public static class PlcAddressMapper
{
    public static bool TryResolve(PlcProtocol protocol, string? input, TagDataType dataType, out string hint, out string error)
    {
        hint = string.Empty;
        error = string.Empty;
        if (protocol == PlcProtocol.S7)
        {
            if (!SiemensS7AddressMapper.TryResolve(input, dataType, out var resolved, out error))
            {
                return false;
            }

            hint = SiemensS7AddressMapper.Describe(resolved);
            return true;
        }

        if (!XinjeXd5eMapper.TryResolve(input, out var resolvedModbus, out error))
        {
            return false;
        }

        hint = resolvedModbus.IsBit
            ? $"XD5E 线圈  {resolvedModbus.Normalized} → {resolvedModbus.Address}"
            : $"XD5E 保持寄存器  {resolvedModbus.Normalized} → {resolvedModbus.Address}";
        return true;
    }

    public static bool TryApplyTo(PlcTag tag, PlcProtocol protocol, out string error)
    {
        error = string.Empty;
        if (tag.Source == TagSource.Manual)
        {
            return true;
        }

        if (protocol == PlcProtocol.S7)
        {
            if (!SiemensS7AddressMapper.TryResolve(tag.XinjeAddress, tag.DataType, out var resolved, out error))
            {
                error = EnhanceS7LoadError(tag.XinjeAddress, error);
                return false;
            }

            if (resolved.IsBit)
            {
                tag.DataType = TagDataType.Bool;
            }

            return true;
        }

        if (!XinjeXd5eMapper.TryResolve(tag.XinjeAddress, out var modbus, out error))
        {
            return false;
        }

        tag.XinjeAddress = modbus.Normalized;
        tag.Table = modbus.Table;
        tag.Address = modbus.Address;
        if (modbus.IsBit)
        {
            tag.DataType = TagDataType.Bool;
        }

        return true;
    }

    public static void ApplyTo(PlcTag tag, PlcProtocol protocol)
    {
        if (!TryApplyTo(tag, protocol, out var error))
        {
            throw new InvalidOperationException($"点位「{tag.Name}」：{error}");
        }
    }

    static string EnhanceS7LoadError(string? address, string error)
    {
        var text = address?.Trim() ?? string.Empty;
        if (text.Length >= 2
            && (text[0] is 'D' or 'd')
            && text[1..].All(static c => char.IsDigit(c)))
        {
            return $"{error} 当前为信捷 Modbus 型 D 地址；若 PLC 仍是 XD5E，请把产线 Excel「PLC协议」改回 Modbus TCP，或改为 S7 地址（如 DB1.DBD0、IW64）。";
        }

        return error;
    }

    public static string AddressLabel(PlcProtocol protocol) =>
        protocol == PlcProtocol.S7 ? "S7 地址" : "信捷地址（XD5E）";

    public static string AddressPlaceholder(PlcProtocol protocol) =>
        protocol == PlcProtocol.S7 ? "IW64 / ID64 / I0.0 / DB1.DBD0" : "D100 / M0 / X20 / Y0";

    public static string AddressHelp(PlcProtocol protocol) =>
        protocol == PlcProtocol.S7
            ? "输入区 IW**（字）、ID**（双字/Real）、I*.*（位）；输出 QW**/QD**；亦支持 %IW64、EW64（等同 IW）。Float32 请用 ID** 或 DBD**。"
            : "M/X/Y 会自动按位读取。";
}
