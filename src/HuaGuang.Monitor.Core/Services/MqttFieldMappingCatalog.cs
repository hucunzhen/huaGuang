using HuaGuang.Monitor.Models;

namespace HuaGuang.Monitor.Services;

/// <summary>
/// 产线 MQTT 字段默认映射（参考 config/热熔胶复合机字段映射.xlsx）。
/// 仅用于<strong>新建/导出</strong> Excel 或点表 MQTT 列为空时的补全；已配置的 Excel「字段映射」优先。
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
            // 平台当前工作温度键为 rrjwd/jgwd/jqwd
            ["当前工作胶盘温度"] = "rrjwd",
            ["当前工作胶管温度"] = "jgwd",
            ["当前工作胶枪温度"] = "jqwd",
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
            ["流转卡号"] = "lzkh",
            ["设备状态"] = "sbzt",
            ["海绵送料1"] = "hmsl1",
            ["海绵送料2"] = "hmsl2",
            ["面料送料"] = "mlsl",
            ["针板轨道"] = "zbqd",
            ["下火口送料"] = "xhksl",
            ["主机上"] = "zjs",
            ["主机中"] = "zjz",
            ["主机下"] = "zjx",
            ["成品速度"] = "cpsd",
            ["成品1速度"] = "cp1sd",
            ["成品2速度"] = "cp2sd",
            ["上火口火力"] = "shkhl",
            ["下火口火力"] = "xhkhl",
            ["上隔距"] = "sgj",
            ["下隔距"] = "xgj",
            ["计米"] = "jm",
            // 强力起毛机 MA876A
            ["锡林转速"] = "xlzs",
            ["后导急停延时"] = "hdjtys",
            ["上导辊张力"] = "sdgzl",
            ["后导辊张力"] = "hdgzl",
            ["顺针辊转速"] = "szgzs",
            ["逆针辊转速"] = "nzgzs",
            ["布速"] = "bs",
            ["前导辊张力"] = "qdgzl",
            ["锡林微调"] = "xlwt",
            ["扩幅转速"] = "kfzs",
            ["设备运行时间"] = "sbyxsj",
            // 定型机10号、定型机11号、烘焙机（同一套点）
            ["1#排风实际速度"] = "pf1sd",
            ["2#排风实际速度"] = "pf2sd",
            ["1#门幅实际值"] = "mf1sz",
            ["2#门幅实际值"] = "mf2sz",
            ["3#门幅实际值"] = "mf3sz",
            ["4#门幅实际值"] = "mf4sz",
            ["5#门幅实际值"] = "mf5sz",
            ["6#门幅实际值"] = "mf6sz",
            ["7#门幅实际值"] = "mf7sz",
            ["8#门幅实际值"] = "mf8sz",
            ["1+2上风实际速度"] = "sf12sd",
            ["3+4上风实际速度"] = "sf34sd",
            ["5+6上风实际速度"] = "sf56sd",
            ["7+8上风实际速度"] = "sf78sd",
            ["9+10上风实际速度"] = "sf910sd",
            ["1+2下风实际速度"] = "xf12sd",
            ["3+4下风实际速度"] = "xf34sd",
            ["5+6下风实际速度"] = "xf56sd",
            ["7+8下风实际速度"] = "xf78sd",
            ["9+10下风实际速度"] = "xf910sd",
            ["第一箱温度"] = "yxwd1",
            ["第二箱温度"] = "yxwd2",
            ["第三箱温度"] = "yxwd3",
            ["第四箱温度"] = "yxwd4",
            ["第五箱温度"] = "yxwd5",
            ["第六箱温度"] = "yxwd6",
            ["第七箱温度"] = "yxwd7",
            ["第八箱温度"] = "yxwd8",
            ["第九箱温度"] = "yxwd9",
            ["第十箱温度"] = "yxwd10",
            ["机器实际速度"] = "jqsjsd",
            ["进布实际张力"] = "jbsjzl",
            ["轧车实际张力"] = "zcsjzl",
            ["下超喂实际值"] = "xcwsjz",
            ["上超喂实际值"] = "scwsjz",
            ["出布超喂实际值"] = "cbcwsjz",
            ["摆布超喂实际值"] = "bbcwsjz",
            ["提布实际张力"] = "tbsjzl",
            ["表面打卷超喂实际值"] = "bmdjcwsjz",
            ["冷水棍超喂实际值"] = "lsgcwsjz",
            ["J-BOX超喂实际值"] = "jboxcwsjz",
            ["2#出布超喂实际值"] = "cb2cwsjz",
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

    /// <summary>现为 properties 容器报文。</summary>
    public static MqttPayloadProfile CreateFlatPayloadProfile() => CreatePropertiesPayloadProfile();

    public static bool TryResolveDefault(string tagName, out string mqttField) =>
        SharedByTagName.TryGetValue(tagName, out mqttField!);

    public static bool TryResolveTagNameByField(string mqttField, out string tagName)
    {
        if (string.IsNullOrWhiteSpace(mqttField))
        {
            tagName = string.Empty;
            return false;
        }

        var trimmed = mqttField.Trim();
        foreach (var pair in SharedByTagName)
        {
            if (string.Equals(pair.Value, trimmed, StringComparison.OrdinalIgnoreCase))
            {
                tagName = pair.Key;
                return true;
            }
        }

        tagName = string.Empty;
        return false;
    }

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
