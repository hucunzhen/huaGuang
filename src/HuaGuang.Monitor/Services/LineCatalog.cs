using HuaGuang.Monitor.Models;
using HuaGuang.Monitor.Protocols;

namespace HuaGuang.Monitor.Services;

/// <summary>
/// 来自产线数据地址规划：PLC 点位为机台获取；胶辊型号、胶水型号、门幅、厚度为手动填写。
/// </summary>
public static class LineCatalog
{
    public const int Version = 4;

    public static IReadOnlyList<string> LineNames { get; } =
    [
        "先河热熔胶复合机",
        "华迪热熔胶复合机"
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
        settings.Tags = line.Tags.Select(CloneAndResolve).ToList();
    }

    public static LineProfile Resolve(string? lineName) =>
        lineName == "华迪热熔胶复合机" ? Huadi : Xianhe;

    public static LineProfile Xianhe { get; } = new(
        "先河热熔胶复合机",
        "192.168.6.10",
        SharedTags(includeExpandRate: true));

    public static LineProfile Huadi { get; } = new(
        "华迪热熔胶复合机",
        "192.168.6.20",
        SharedTags(includeExpandRate: false));

    static List<PlcTag> SharedTags(bool includeExpandRate)
    {
        var tags = new List<PlcTag>
        {
            Bool("运行状态", "D1000"),
            Real("热溶胶盘温度（热熔胶机1）", "D6000", "℃"),
            Real("胶管温度（热熔胶机1）", "D6002", "℃"),
            Real("胶枪温度（热熔胶机1）", "D6004", "℃"),
            Real("热溶胶盘温度（热熔胶机2）", "D6006", "℃"),
            Real("胶管温度（热熔胶机2）", "D6008", "℃"),
            Real("胶枪温度（热熔胶机2）", "D6010", "℃"),
            Real("热溶胶盘温度（热熔胶机3）", "D6012", "℃"),
            Real("胶管温度（热熔胶机3）", "D6014", "℃"),
            Real("胶枪温度（热熔胶机3）", "D6016", "℃"),
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
        tags.Add(Real("当前工作胶盘温度", "D6100", "℃"));
        tags.Add(Real("当前工作胶管温度", "D6120", "℃"));
        tags.Add(Real("当前工作胶枪温度", "D6140", "℃"));

        tags.Add(ManualString("胶辊型号"));
        tags.Add(ManualString("胶水型号"));
        tags.Add(ManualReal("门幅", "mm"));
        tags.Add(ManualReal("厚度", "mm"));
        return tags;
    }

    static PlcTag Bool(string name, string address) => new()
    {
        Name = name,
        XinjeAddress = address,
        DataType = TagDataType.Bool
    };

    static PlcTag Real(string name, string address, string unit = "") => new()
    {
        Name = name,
        Unit = unit,
        XinjeAddress = address,
        DataType = TagDataType.Float32,
        ByteOrder = ByteOrder.CDAB
    };

    static PlcTag ManualString(string name, string defaultValue = "") => new()
    {
        Name = name,
        Source = TagSource.Manual,
        DataType = TagDataType.String,
        ManualValue = defaultValue
    };

    static PlcTag ManualReal(string name, string unit = "", string defaultValue = "") => new()
    {
        Name = name,
        Unit = unit,
        Source = TagSource.Manual,
        DataType = TagDataType.Float32,
        ManualValue = defaultValue
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
            DisplayPrecision = source.DisplayPrecision
        };

        if (tag.Source != TagSource.Manual)
        {
            XinjeXd5eMapper.ApplyTo(tag);
        }

        return tag;
    }
}

public sealed record LineProfile(string Name, string Host, IReadOnlyList<PlcTag> Tags);
