using HuaGuang.Monitor.Models;

namespace HuaGuang.Monitor.Services;

/// <summary>当前注胶机编号与当前工作温度归入「温度监测」并在组内相邻展示。</summary>
public static class CurrentInjectionFormatting
{
    public static bool IsRelatedTag(PlcTag tag) =>
        tag.Name is "当前注胶机编号" or "当前工作胶盘温度" or "当前工作胶管温度" or "当前工作胶枪温度";

    public static int GetTagSortOrder(string name) => name switch
    {
        "当前注胶机编号" => 0,
        "当前工作胶盘温度" => 1,
        "当前工作胶管温度" => 2,
        "当前工作胶枪温度" => 3,
        _ => 100
    };

    public static IEnumerable<T> OrderByDisplay<T>(
        TagDisplayCategory category,
        IEnumerable<T> items,
        Func<T, string> nameSelector)
    {
        if (category != TagDisplayCategory.Temperature)
        {
            return items;
        }

        return items
            .OrderBy(item => GetTagSortOrder(nameSelector(item)))
            .ThenBy(nameSelector, StringComparer.Ordinal);
    }
}
