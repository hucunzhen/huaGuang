namespace HuaGuang.Monitor.Services;

using HuaGuang.Monitor.Models;

/// <summary>已从产线点表废弃、维护时应自动移除的点位。</summary>
public static class DeprecatedLineTags
{
    public static readonly string[] PerMachineGlueTemperatureNames =
    [
        "热溶胶盘温度（热熔胶机1）",
        "胶管温度（热熔胶机1）",
        "胶枪温度（热熔胶机1）",
        "热溶胶盘温度（热熔胶机2）",
        "胶管温度（热熔胶机2）",
        "胶枪温度（热熔胶机2）",
        "热溶胶盘温度（热熔胶机3）",
        "胶管温度（热熔胶机3）",
        "胶枪温度（热熔胶机3）"
    ];

    static readonly HashSet<string> PerMachineGlueTemperatureSet =
        new(PerMachineGlueTemperatureNames, StringComparer.Ordinal);

    public static bool IsPerMachineGlueTemperature(string name) =>
        PerMachineGlueTemperatureSet.Contains(name);

    public static int RemoveFrom(IList<PlcTag> tags)
    {
        var removed = 0;
        for (var index = tags.Count - 1; index >= 0; index--)
        {
            if (!IsPerMachineGlueTemperature(tags[index].Name))
            {
                continue;
            }

            tags.RemoveAt(index);
            removed++;
        }

        return removed;
    }
}
