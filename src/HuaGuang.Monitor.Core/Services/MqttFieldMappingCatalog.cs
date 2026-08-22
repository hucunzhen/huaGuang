using HuaGuang.Monitor.Models;

namespace HuaGuang.Monitor.Services;

/// <summary>
/// 热熔胶复合机 MQTT 字段映射（参考 config/热熔胶复合机字段映射.xlsx）。
/// id = 报文 JSON 键，name = 点表点位名称。
/// </summary>
public static class MqttFieldMappingCatalog
{
    public const string ReferenceFileName = "热熔胶复合机字段映射.xlsx";

    public static IReadOnlyDictionary<string, string> SharedByTagName { get; } =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["运行状态"] = "run_status",
            ["热溶胶盘温度（热熔胶机1）"] = "rrjwd1",
            ["胶管温度（热熔胶机1）"] = "jgwd1",
            ["胶枪温度（热熔胶机1）"] = "jqwd1",
            ["热溶胶盘温度（热熔胶机2）"] = "rrjwd2",
            ["胶管温度（热熔胶机2）"] = "jgwd2",
            ["胶枪温度（热熔胶机2）"] = "jqwd2",
            ["热溶胶盘温度（热熔胶机3）"] = "rrjwd3",
            ["胶管温度（热熔胶机3）"] = "jgwd3",
            ["胶枪温度（热熔胶机3）"] = "jqwd3",
            ["油温机温度"] = "ywjwd",
            ["车速"] = "speed",
            ["上卷出转速率"] = "sjcsl",
            ["下卷出转速率"] = "xjcsl",
            ["上胶轮间隙"] = "sjljx",
            ["铁合轮间隙"] = "thljx",
            ["上展开转速率"] = "szksl",
            ["卷曲张力"] = "jqzl",
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

    public static void ApplyDefaults(IList<PlcTag> tags, string? lineName = null)
    {
        var includeExpandRate = lineName != "华迪热熔胶复合机";
        foreach (var tag in tags)
        {
            if (!SharedByTagName.TryGetValue(tag.Name, out var mqttField))
            {
                continue;
            }

            if (!includeExpandRate && mqttField == "szksl")
            {
                continue;
            }

            tag.MqttField = mqttField;
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
