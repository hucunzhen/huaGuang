using HuaGuang.Monitor.Models;

namespace HuaGuang.Monitor.Services;

/// <summary>
/// 运行状态为关（关机）时，默认不再读其它 PLC 点。运行状态点本身始终读取。
/// 待机和开仍采集。读不到运行状态时不跳过。
/// </summary>
public static class TagShutdownAcquire
{
    public const string ExcelColumn = "关机时采集";

    public static bool IsMachineOff(object? runStatusValue) =>
        RunStatusFormatting.TryGetCode(runStatusValue) == 0;

    public static bool ShouldRead(PlcTag tag, bool machineOff)
    {
        if (!tag.IsPlc || RunStatusFormatting.IsRunStatusTag(tag) || !machineOff)
        {
            return true;
        }

        return tag.ReadWhenShutdown;
    }

    public static bool DependsOnSkipped(PlcTag tag, IReadOnlySet<string> skippedNames)
    {
        if (skippedNames.Count == 0 || string.IsNullOrWhiteSpace(tag.Expression))
        {
            return false;
        }

        foreach (var name in skippedNames)
        {
            if (tag.Expression.Contains($"[{name}]", StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }
}
