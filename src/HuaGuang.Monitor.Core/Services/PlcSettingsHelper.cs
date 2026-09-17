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

            ApplyS7RackSlotDefaults(plc);
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

    public static int RecommendedDefaultSlot(string? cpuType)
    {
        var cpu = NormalizeCpuKey(cpuType);
        return cpu is "S7300" or "S7400" ? 2 : 0;
    }

    static void ApplyS7RackSlotDefaults(PlcSettings plc)
    {
        var cpu = NormalizeCpuKey(plc.CpuType);
        plc.Rack = Math.Clamp(plc.Rack, 0, 7);

        switch (cpu)
        {
            case "S71200":
            case "S71500":
                // S7.Net / ISO-on-TCP：1200/1500 常用 rack 0 slot 0（旧配置默认 slot 1 会导致连接失败）
                if (plc.Rack == 0 && plc.Slot == 1)
                {
                    plc.Slot = 0;
                }

                break;
            case "S7300":
            case "S7400":
                if (plc.Slot is 0 or 1)
                {
                    plc.Rack = 0;
                    plc.Slot = 2;
                }

                break;
        }

        plc.Slot = Math.Clamp(plc.Slot, 0, 31);
    }

    static string NormalizeCpuKey(string? cpuType)
    {
        if (string.IsNullOrWhiteSpace(cpuType))
        {
            return "S71200";
        }

        return cpuType.Trim().ToUpperInvariant()
            .Replace("-", string.Empty, StringComparison.Ordinal)
            .Replace(" ", string.Empty, StringComparison.Ordinal);
    }
}
