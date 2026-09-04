using HuaGuang.Monitor.Models;

namespace HuaGuang.Monitor.Services;

/// <summary>
/// 点位 Id 在 UI 与后台服务之间必须一致；Excel 不含 Id 列时用产线名+点位名生成稳定 Id。
/// </summary>
public static class PlcTagIdentity
{
    public static string CreateStableId(string lineName, string tagName) =>
        $"{lineName.Trim()}::{tagName.Trim()}";

    public static void AssignStableIds(AppSettings settings)
    {
        var lineName = string.IsNullOrWhiteSpace(settings.LineName)
            ? LineCatalog.LineNames[0]
            : settings.LineName;

        foreach (var tag in settings.Tags)
        {
            tag.Id = CreateStableId(lineName, tag.Name);
        }
    }

    public static void AssignStableId(PlcTag tag, string lineName) =>
        tag.Id = CreateStableId(lineName, tag.Name);
}
