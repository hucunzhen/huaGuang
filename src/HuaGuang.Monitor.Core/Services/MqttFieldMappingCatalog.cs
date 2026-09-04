using HuaGuang.Monitor.Models;

namespace HuaGuang.Monitor.Services;

/// <summary>
/// 产线 MQTT 字段映射（参考 config/热熔胶复合机字段映射.xlsx）。
/// id = 报文 JSON 键，name = 点表点位名称。
/// </summary>
public static class MqttFieldMappingCatalog
{
    public const string ReferenceFileName = "热熔胶复合机字段映射.xlsx";

    /// <summary>各产线共用或按点位名称唯一的默认映射。</summary>
    public static IReadOnlyDictionary<string, string> SharedByTagName { get; } =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["运行状态"] = "run_status",
            ["油温机温度"] = "ywjwd",
            ["车速"] = "speed",
            ["上卷出转速率"] = "sjcsl",
            ["下卷出转速率"] = "xjcsl",
            ["上胶轮间隙"] = "sjljx",
            ["铁合轮间隙"] = "thljx",
            ["上展开转速率"] = "szksl",
            ["下展开转速率"] = "xzksl",
            ["卷曲张力"] = "jqzl",
            ["注胶量"] = "zjl",
            ["当前注胶机编号"] = "zjjbh",
            // 当前工作温度沿用平台原 rrjwd1/jgwd1/jqwd1 键（替代已移除的热熔胶机1~3）
            ["当前工作胶盘温度"] = "rrjwd1",
            ["当前工作胶管温度"] = "jgwd1",
            ["当前工作胶枪温度"] = "jqwd1",
            ["胶辊型号"] = "jgxh",
            ["胶水型号"] = "jsxh",
            ["产品货号"] = "cphh",
            ["门幅"] = "mf",
            ["厚度"] = "hd",
            // C型火焰复合机
            ["海绵喂入张力"] = "hmwrzl",
            ["上夹距"] = "sjj",
            ["下夹距"] = "xjj",
            ["上火口距离"] = "shkjjl",
            ["下火口距离"] = "xhkjjl",
            ["上煤气流量"] = "smqll",
            ["下煤气流量"] = "xmqll",
            ["成品喂入速度"] = "cpwrsd",
            ["收卷牵引速度"] = "sjqysd",
            ["收卷系数"] = "sjxs",
            ["切刀间距"] = "qdjj",
            // 撒粉复合机
            ["撒粉型号"] = "sfxh",
            ["撒粉量"] = "sfl",
            ["烘箱温度1"] = "hxwd1",
            ["烘箱温度2"] = "hxwd2",
            ["主辊筒温度"] = "zgtwd",
            ["主辊筒车速"] = "zgtcs",
            ["上压辊1（闭合/打开）"] = "syg1",
            ["上压辊1间隙"] = "syg1jx",
            ["下压辊2（闭合/打开）"] = "xyg2",
            ["下压辊2间隙"] = "xyg2jx",
            // 平板复合机
            ["上半区加热（区域1）"] = "sbqjr1",
            ["上半区加热（区域2）"] = "sbqjr2",
            ["上半区加热（区域3）"] = "sbqjr3",
            ["下半区加热（区域1）"] = "xbqjr1",
            ["下半区加热（区域2）"] = "xbqjr2",
            ["下半区加热（区域3）"] = "xbqjr3",
            ["平面间隙"] = "pmjx",
            ["下压距离"] = "xyjl",
            ["压力"] = "yl",
            ["运行速度"] = "yxsd",
        };

    public static MqttPayloadProfile CreatePropertiesPayloadProfile() => new()
    {
        PayloadFormat = "json",
        TagsPath = "properties",
        DeviceIdPath = string.Empty,
        TimestampPath = string.Empty,
        QualityPath = string.Empty,
        PlcHostPath = string.Empty,
        SimulatorPath = string.Empty,
        UseTagNameWhenFieldEmpty = false
    };

    /// <summary>兼容旧名；现为 properties 容器报文。</summary>
    public static MqttPayloadProfile CreateFlatPayloadProfile() => CreatePropertiesPayloadProfile();

    public static void NormalizeLegacyProfile(MqttPayloadProfile profile)
    {
        if (string.IsNullOrWhiteSpace(profile.TagsPath) ||
            string.Equals(profile.TagsPath, "tags", StringComparison.OrdinalIgnoreCase))
        {
            profile.TagsPath = "properties";
        }

        if (string.Equals(profile.DeviceIdPath, "deviceId", StringComparison.OrdinalIgnoreCase))
        {
            profile.DeviceIdPath = string.Empty;
        }

        if (string.Equals(profile.TimestampPath, "timestamp", StringComparison.OrdinalIgnoreCase))
        {
            profile.TimestampPath = string.Empty;
        }

        if (string.Equals(profile.QualityPath, "quality", StringComparison.OrdinalIgnoreCase))
        {
            profile.QualityPath = string.Empty;
        }

        if (string.Equals(profile.PlcHostPath, "plcHost", StringComparison.OrdinalIgnoreCase))
        {
            profile.PlcHostPath = string.Empty;
        }

        if (string.Equals(profile.SimulatorPath, "simulator", StringComparison.OrdinalIgnoreCase))
        {
            profile.SimulatorPath = string.Empty;
        }
    }

    public static bool TryResolveDefault(string tagName, out string mqttField) =>
        SharedByTagName.TryGetValue(tagName, out mqttField!);

    public static void ApplyDefaults(IList<PlcTag> tags, string? lineName = null)
    {
        _ = lineName;
        foreach (var tag in tags)
        {
            if (!string.IsNullOrWhiteSpace(tag.MqttField))
            {
                continue;
            }

            if (TryResolveDefault(tag.Name, out var mqttField))
            {
                tag.MqttField = mqttField;
            }
        }
    }

    public static int ApplyRows(IList<PlcTag> tags, IEnumerable<(string Id, string Name)> rows)
    {
        var byName = tags.ToDictionary(tag => tag.Name, StringComparer.Ordinal);
        var applied = 0;
        foreach (var (id, name) in rows)
        {
            if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(name))
            {
                continue;
            }

            if (!byName.TryGetValue(name.Trim(), out var tag))
            {
                continue;
            }

            tag.MqttField = id.Trim();
            applied++;
        }

        return applied;
    }

    public static string? ResolveReferencePath(string? repoRoot = null)
    {
        repoRoot ??= FindRepoRoot();
        if (repoRoot is null)
        {
            return null;
        }

        var path = Path.Combine(repoRoot, "config", ReferenceFileName);
        return File.Exists(path) ? path : null;
    }

    static string? FindRepoRoot()
    {
        var dir = AppContext.BaseDirectory;
        for (var i = 0; i < 8; i++)
        {
            if (Directory.Exists(Path.Combine(dir, "config")) &&
                File.Exists(Path.Combine(dir, "config", ReferenceFileName)))
            {
                return dir;
            }

            var parent = Directory.GetParent(dir);
            if (parent is null)
            {
                break;
            }

            dir = parent.FullName;
        }

        return null;
    }
}
