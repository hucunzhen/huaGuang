using HuaGuang.Monitor.Models;

namespace HuaGuang.Monitor.Services;

/// <summary>当前注胶机编号与当前工作温度归入「温度监测」并在组内相邻展示。</summary>
public static class CurrentInjectionFormatting
{
    public static bool IsRelatedTag(PlcTag tag) =>
        tag.Name is "当前注胶机编号" or "当前工作胶盘温度" or "当前工作胶管温度" or "当前工作胶枪温度";

    public static int GetTagSortOrder(string? name) => name switch
    {
        "当前注胶机编号" => 0,
        "当前工作胶盘温度" => 1,
        "当前工作胶管温度" => 2,
        "当前工作胶枪温度" => 3,
        null or "" => 100,
        _ => 100
    };

    /// <summary>
    /// 温度组内仅将「当前注胶…」相关点位前置；其余点位保持传入顺序（产线 Excel 点表顺序），
    /// 避免按名称字典序把「烘箱10」排到「烘箱1」前面。
    /// </summary>
    public static IEnumerable<T> OrderByDisplay<T>(
        TagDisplayCategory category,
        IEnumerable<T> items,
        Func<T, string> nameSelector)
    {
        if (category != TagDisplayCategory.Temperature)
        {
            return items;
        }

        var list = items as IList<T> ?? items.ToList();
        var related = new T?[4];
        var others = new List<T>(list.Count);
        foreach (var item in list)
        {
            var order = GetTagSortOrder(nameSelector(item));
            if (order is >= 0 and < 4)
            {
                related[order] = item;
            }
            else
            {
                others.Add(item);
            }
        }

        return EnumerateRelatedThenOthers(related, others);
    }

    static IEnumerable<T> EnumerateRelatedThenOthers<T>(T?[] related, List<T> others)
    {
        foreach (var item in related)
        {
            if (item is not null)
            {
                yield return item;
            }
        }

        foreach (var item in others)
        {
            yield return item;
        }
    }
}
