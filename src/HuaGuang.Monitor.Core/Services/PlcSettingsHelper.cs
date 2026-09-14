using HuaGuang.Monitor.Models;

namespace HuaGuang.Monitor.Services;

public static class PlcSettingsHelper
{
    public static void Normalize(PlcSettings plc)
    {
        if (plc.Protocol == PlcProtocol.S7)
        {
            if (plc.Port <= 0 || plc.Port == 502)
            {
                plc.Port = 102;
            }

            if (string.IsNullOrWhiteSpace(plc.CpuType))
            {
                plc.CpuType = "S71200";
            }

            return;
        }

        if (plc.Port <= 0)
        {
            plc.Port = 502;
        }
    }

    public static PlcProtocol ParseProtocol(string? text, string? model = null)
    {
        if (!string.IsNullOrWhiteSpace(text))
        {
            var normalized = text.Trim();
            if (normalized.Contains("S7", StringComparison.OrdinalIgnoreCase)
                || normalized.Contains("西门子", StringComparison.Ordinal))
            {
                return PlcProtocol.S7;
            }

            if (normalized.Contains("Modbus", StringComparison.OrdinalIgnoreCase)
                || normalized.Contains("信捷", StringComparison.Ordinal))
            {
                return PlcProtocol.ModbusTcp;
            }
        }

        return InferProtocolFromModel(model);
    }

    public static PlcProtocol InferProtocolFromModel(string? model)
    {
        if (string.IsNullOrWhiteSpace(model))
        {
            return PlcProtocol.ModbusTcp;
        }

        var normalized = model.Trim();
        if (normalized.Contains("S7", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("1200", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("1500", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("300", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("400", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("西门子", StringComparison.Ordinal))
        {
            return PlcProtocol.S7;
        }

        return PlcProtocol.ModbusTcp;
    }

    public static string FormatProtocol(PlcProtocol protocol) =>
        protocol == PlcProtocol.S7 ? "西门子 S7" : "Modbus TCP";

    public static string PlcSectionTitle(PlcSettings plc) =>
        plc.Protocol == PlcProtocol.S7
            ? $"PLC · 西门子 {plc.CpuType}"
            : $"PLC · {plc.Model}";
}
