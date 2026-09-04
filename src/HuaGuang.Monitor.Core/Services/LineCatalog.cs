using HuaGuang.Monitor.Models;
using HuaGuang.Monitor.Protocols;

namespace HuaGuang.Monitor.Services;

/// <summary>
/// 来自产线数据地址规划：仅用于<strong>新建</strong>产线 Excel 时的默认种子。
/// 已存在的 config/lines 点表以 Excel 为准，不会随此类变更自动覆盖。
/// </summary>
public static class LineCatalog
{
    public const string ProductSkuTagName = "产品货号";
    public const int Version = 12;

    public static IReadOnlyList<string> LineNames { get; } =
    [
        "先河热熔胶复合机",
        "华迪热熔胶复合机",
        "撒粉复合机",
        "平板复合机",
        "C型火焰复合机"
    ];

    public static void Apply(AppSettings settings, string lineName)
    {
        var line = Resolve(lineName);
        settings.LineName = line.Name;
        settings.DeviceId = line.Name;
        settings.AddressCatalogVersion = Version;
        settings.Plc.Model = "XD5E-60T10";
        settings.Plc.Host = line.Host;
        settings.Plc.Port = 502;
        settings.MqttPayload = MqttFieldMappingCatalog.CreatePropertiesPayloadProfile();
        LineMqttDefaults.ApplyBroker(settings.Mqtt);
        settings.Mqtt.Topic = line.MqttTopic;
        LineMqttDefaults.ApplySubscribeTopics(settings);
        settings.Mqtt.ClientId = LineMqttDefaults.ResolveClientIdForLine(line.Name);
        settings.Tags = line.Tags.Select(CloneAndResolve).ToList();
        MqttFieldMappingCatalog.ApplyDefaults(settings.Tags, lineName);
    }

    public static LineProfile Resolve(string? lineName) => lineName switch
    {
        "华迪热熔胶复合机" => Huadi,
        "撒粉复合机" => Safen,
        "平板复合机" => Pingban,
        "C型火焰复合机" => Cyhy,
        _ => Xianhe
    };

    /// <summary>Android 资源文件名仅 ASCII（aapt 不支持中文路径）。</summary>
    public static string GetBundledAssetName(string lineName) => lineName switch
    {
        "华迪热熔胶复合机" => "huadi",
        "撒粉复合机" => "safen",
        "平板复合机" => "pingban",
        "C型火焰复合机" => "cyhy",
        _ => "xianhe"
    };

    public static LineProfile Xianhe { get; } = new(
        "先河热熔胶复合机",
        "172.14.1.200",
        LineMqttDefaults.XianhePublishTopic,
        SharedTags(includeExpandRate: true));

    public static LineProfile Huadi { get; } = new(
        "华迪热熔胶复合机",
        "172.14.1.201",
        LineMqttDefaults.HuadiPublishTopic,
        HuadiTags());

    static List<PlcTag> SharedTags(bool includeExpandRate)
    {
        var tags = new List<PlcTag>
        {
            RunStatus("运行状态", "D1000"),
            Real("油温机温度", "D6200", "℃"),
            Real("车速", "D1030"),
            Real("上卷出转速率", "D1040"),
            Real("下卷出转速率", "D1050"),
            Real("上胶轮间隙", "D1060"),
            Real("铁合轮间隙", "D1070")
        };

        if (includeExpandRate)
        {
            tags.Add(Real("上展开转速率", "D1080"));
        }

        tags.Add(Real("卷曲张力", "D1090"));
        tags.Add(TemperatureInt16("当前注胶机编号", "D1130"));
        tags.Add(Real("当前工作胶盘温度", "D6100", "℃"));
        tags.Add(Real("当前工作胶管温度", "D6120", "℃"));
        tags.Add(Real("当前工作胶枪温度", "D6140", "℃"));

        tags.Add(ManualString("胶辊型号"));
        tags.Add(ManualString("胶水型号"));
        tags.Add(ManualString(ProductSkuTagName, useScannerInput: true));
        tags.Add(ManualReal("门幅", "mm"));
        tags.Add(ManualReal("厚度", "mm"));
        return tags;
    }

    static List<PlcTag> HuadiTags()
    {
        var tags = SharedTags(includeExpandRate: false);
        tags.RemoveAll(tag => tag.Name is "上卷出转速率" or "下卷出转速率");
        UpsertPlc(ref tags, Real("上展开转速率", "D1080"));
        UpsertPlc(ref tags, Real("下展开转速率", "D1120"));
        var speed = tags.First(tag => tag.Name == "车速");
        speed.XinjeAddress = "D18";
        XinjeXd5eMapper.ApplyTo(speed);
        tags.Add(ManualReal("注胶量"));
        return tags;
    }

    public static LineProfile Safen { get; } = new(
        "撒粉复合机",
        "172.14.1.202",
        LineMqttDefaults.SafenPublishTopic,
        []);

    public static LineProfile Pingban { get; } = new(
        "平板复合机",
        "172.14.1.203",
        LineMqttDefaults.PingbanPublishTopic,
        []);

    public static LineProfile Cyhy { get; } = new(
        "C型火焰复合机",
        "172.14.1.204",
        LineMqttDefaults.CyhyPublishTopic,
        []);

    static void UpsertPlc(ref List<PlcTag> tags, PlcTag tag)
    {
        tags.RemoveAll(item => item.Name == tag.Name);
        tags.Add(tag);
    }

    static PlcTag PlcInt16(string name, string address) => new()
    {
        Name = name,
        XinjeAddress = address,
        DataType = TagDataType.Int16,
        DisplayCategory = TagDisplayCategory.Process
    };

    static PlcTag TemperatureInt16(string name, string address) => new()
    {
        Name = name,
        XinjeAddress = address,
        DataType = TagDataType.Int16,
        DisplayCategory = TagDisplayCategory.Temperature
    };

    static PlcTag RunStatus(string name, string address) => new()
    {
        Name = name,
        XinjeAddress = address,
        DataType = TagDataType.Int16,
        DisplayCategory = TagDisplayCategory.Switch
    };

    static PlcTag Real(string name, string address, string unit = "") => new()
    {
        Name = name,
        Unit = unit,
        XinjeAddress = address,
        DataType = TagDataType.Float32,
        ByteOrder = ByteOrder.CDAB,
        DisplayCategory = unit.Contains('℃', StringComparison.Ordinal) ||
                         name.Contains("温度", StringComparison.Ordinal)
            ? TagDisplayCategory.Temperature
            : TagDisplayCategory.Process
    };

    static PlcTag ManualString(string name, string defaultValue = "", bool useScannerInput = false) => new()
    {
        Name = name,
        Source = TagSource.Manual,
        DataType = TagDataType.String,
        ManualValue = defaultValue,
        DisplayCategory = TagDisplayCategory.Setting,
        UseScannerInput = useScannerInput
    };

    static PlcTag ManualReal(string name, string unit = "", string defaultValue = "") => new()
    {
        Name = name,
        Unit = unit,
        Source = TagSource.Manual,
        DataType = TagDataType.Float32,
        ManualValue = defaultValue,
        DisplayCategory = TagDisplayCategory.Setting
    };

    static PlcTag CloneAndResolve(PlcTag source)
    {
        var tag = new PlcTag
        {
            Name = source.Name,
            Unit = source.Unit,
            XinjeAddress = source.XinjeAddress,
            DataType = source.DataType,
            ByteOrder = source.ByteOrder,
            Source = source.Source,
            ManualValue = source.ManualValue,
            DisplayPrecision = source.DisplayPrecision,
            MqttField = source.MqttField,
            DisplayCategory = source.DisplayCategory,
            UseScannerInput = source.UseScannerInput
        };

        if (tag.Source != TagSource.Manual)
        {
            XinjeXd5eMapper.ApplyTo(tag);
        }

        return tag;
    }
}

public sealed record LineProfile(string Name, string Host, string MqttTopic, IReadOnlyList<PlcTag> Tags);
