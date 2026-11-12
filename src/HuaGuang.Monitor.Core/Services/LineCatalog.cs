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
    public const int Version = 17;

    public const string S7TestLineName = "S7测试产线";
    public const string ZhidahyLineName = "智达火焰复合机";
    public const string ShanghaihyLineName = "上海火焰复合机";
    public const string MeshyLineName = "MES火焰复合机";
    public const string SubscribeDashboardLineName = "订阅大屏";

    public const string QimaoZl028aLineName = "起毛机ZL-028A";
    public const string QimaoZl028bLineName = "起毛机ZL-028B";
    public const string QimaoZl028cLineName = "起毛机ZL-028C";
    public const string QimaoZl027aLineName = "起毛机ZL-027A";
    public const string QimaoZl027bLineName = "起毛机ZL-027B";
    public const string QimaoZl027cLineName = "起毛机ZL-027C";
    public const string Dingxing10LineName = "定型机10号";
    public const string Dingxing11LineName = "定型机11号";
    public const string HongbeiLineName = "烘焙机";

    public static IReadOnlyList<string> QimaoLineNames { get; } =
    [
        QimaoZl028aLineName,
        QimaoZl028bLineName,
        QimaoZl028cLineName,
        QimaoZl027aLineName,
        QimaoZl027bLineName,
        QimaoZl027cLineName
    ];

    public static IReadOnlyList<string> LineNames { get; } =
    [
        "先河热熔胶复合机",
        "华迪热熔胶复合机",
        "撒粉复合机",
        "平板复合机",
        "C型火焰复合机",
        ZhidahyLineName,
        ShanghaihyLineName,
        MeshyLineName,
        QimaoZl028aLineName,
        QimaoZl028bLineName,
        QimaoZl028cLineName,
        QimaoZl027aLineName,
        QimaoZl027bLineName,
        QimaoZl027cLineName,
        Dingxing10LineName,
        Dingxing11LineName,
        HongbeiLineName,
        S7TestLineName,
        SubscribeDashboardLineName
    ];

    public static bool IsQimaoLine(string? lineName) =>
        TryResolveLineName(lineName, out var resolved)
        && QimaoLineNames.Contains(resolved);

    public static IReadOnlyList<string> DingxingLineNames { get; } =
    [
        Dingxing10LineName,
        Dingxing11LineName,
        HongbeiLineName
    ];

    public static bool IsDingxingLine(string? lineName) =>
        TryResolveLineName(lineName, out var resolved)
        && DingxingLineNames.Contains(resolved);

    public static bool UsesS7Protocol(string? lineName) =>
        string.Equals(lineName, S7TestLineName, StringComparison.Ordinal)
        || IsQimaoLine(lineName)
        || IsDingxingLine(lineName);

    public static IReadOnlyList<string> AcquisitionLineNames { get; } =
        LineNames.Where(name => !IsSubscribeDashboard(name)).ToArray();

    public static bool IsSubscribeDashboard(string? lineName) =>
        TryResolveLineName(lineName, out var resolved)
        && resolved == SubscribeDashboardLineName;

    public static void Apply(AppSettings settings, string lineName)
    {
        if (IsSubscribeDashboard(lineName) || lineName == SubscribeDashboardLineName)
        {
            ApplySubscribeDashboard(settings);
            return;
        }

        var line = Resolve(lineName);
        settings.LineName = line.Name;
        settings.DeviceId = line.Name;
        settings.AddressCatalogVersion = Version;
        ApplyPlcDefaults(settings, lineName, line);
        settings.MqttPayload = MqttFieldMappingCatalog.CreatePropertiesPayloadProfile();
        LineMqttDefaults.ApplyBroker(settings.Mqtt);
        settings.Mqtt.Topic = line.MqttTopic;
        LineMqttDefaults.ApplySubscribeTopics(settings);
        settings.Mqtt.ClientId = LineMqttDefaults.ResolveClientIdForLine(line.Name);
        settings.MqttEndpoints = [MqttEndpoint.FromSettings(settings.Mqtt, "默认")];
        MqttSubscribeAccount.ApplyBrokerFromPublish(settings.SubscribeMqtt, settings.Mqtt);
        settings.Tags = line.Tags.Select(tag => CloneAndResolve(tag, lineName)).ToList();
        MqttFieldMappingCatalog.ApplyDefaults(settings.Tags, lineName);
    }

    static void ApplyPlcDefaults(AppSettings settings, string lineName, LineProfile line)
    {
        if (lineName == S7TestLineName)
        {
            settings.Plc.Protocol = PlcProtocol.S7;
            settings.Plc.Model = "S7-1515";
            settings.Plc.Host = line.Host;
            settings.Plc.Port = 102;
            settings.Plc.Rack = 0;
            settings.Plc.Slot = 0;
            settings.Plc.CpuType = "S71500";
            settings.Plc.TimeoutMs = 3000;
            settings.UseSimulator = false;
            PlcSettingsHelper.Normalize(settings.Plc);
            return;
        }

        if (IsQimaoLine(lineName))
        {
            settings.Plc.Protocol = PlcProtocol.S7;
            settings.Plc.Model = "MA876AH";
            settings.Plc.Host = line.Host;
            settings.Plc.Port = 102;
            settings.Plc.Rack = 0;
            settings.Plc.Slot = 1;
            settings.Plc.CpuType = "S7200SMART";
            settings.Plc.TimeoutMs = 3000;
            settings.UseSimulator = false;
            PlcSettingsHelper.Normalize(settings.Plc);
            return;
        }

        if (IsDingxingLine(lineName))
        {
            settings.Plc.Protocol = PlcProtocol.S7;
            settings.Plc.Model = "S7-1500";
            settings.Plc.Host = line.Host;
            settings.Plc.Port = 102;
            settings.Plc.Rack = 0;
            settings.Plc.Slot = 0;
            settings.Plc.CpuType = "S71500";
            settings.Plc.TimeoutMs = 3000;
            settings.UseSimulator = false;
            PlcSettingsHelper.Normalize(settings.Plc);
            return;
        }

        settings.Plc.Protocol = PlcProtocol.ModbusTcp;
        settings.Plc.Model = "XD5E-60T10";
        settings.Plc.Host = line.Host;
        settings.Plc.Port = 502;
        settings.UseSimulator = false;
        PlcSettingsHelper.Normalize(settings.Plc);
    }

    static void ApplySubscribeDashboard(AppSettings settings)
    {
        settings.LineName = SubscribeDashboardLineName;
        settings.DeviceId = SubscribeDashboardLineName;
        settings.OperationMode = AppOperationMode.Subscribe;
        settings.AddressCatalogVersion = Version;
        settings.UseSimulator = false;
        settings.StartWithWindows = false;
        settings.Tags = [];
        settings.Plc.Host = string.Empty;
        settings.Plc.Port = 502;
        PlcSettingsHelper.Normalize(settings.Plc);
        settings.MqttPayload = MqttFieldMappingCatalog.CreatePropertiesPayloadProfile();
        LineMqttDefaults.ApplyBroker(settings.Mqtt);
        settings.Mqtt.Topic = string.Empty;
        settings.Mqtt.ClientId = MqttSubscribeAccount.DefaultClientId;
        settings.MqttEndpoints = [];
        LineMqttDefaults.ApplySubscribeTopics(settings);
        MqttSubscribeAccount.ApplyBrokerFromPublish(settings.SubscribeMqtt, settings.Mqtt);
        settings.SubscribeMqtt.ClientId = MqttSubscribeAccount.DefaultClientId;
    }

    public static LineProfile Resolve(string? lineName) => lineName switch
    {
        "华迪热熔胶复合机" => Huadi,
        "撒粉复合机" => Safen,
        "平板复合机" => Pingban,
        "C型火焰复合机" => Cyhy,
        ZhidahyLineName => Zhidahy,
        ShanghaihyLineName => Shanghaihy,
        MeshyLineName => Meshy,
        QimaoZl028aLineName => QimaoZl028a,
        QimaoZl028bLineName => QimaoZl028b,
        QimaoZl028cLineName => QimaoZl028c,
        QimaoZl027aLineName => QimaoZl027a,
        QimaoZl027bLineName => QimaoZl027b,
        QimaoZl027cLineName => QimaoZl027c,
        Dingxing10LineName => Dingxing10,
        Dingxing11LineName => Dingxing11,
        HongbeiLineName => Hongbei,
        S7TestLineName => S7Test,
        SubscribeDashboardLineName => SubscribeDashboard,
        _ => Xianhe
    };

    /// <summary>Android 资源文件名仅 ASCII（aapt 不支持中文路径）。</summary>
    public static string GetBundledAssetName(string lineName) => lineName switch
    {
        "华迪热熔胶复合机" => "huadi",
        "撒粉复合机" => "safen",
        "平板复合机" => "pingban",
        "C型火焰复合机" => "cyhy",
        ZhidahyLineName => "zhidahy",
        ShanghaihyLineName => "shanghaihy",
        MeshyLineName => "meshy",
        QimaoZl028aLineName => "zl028a",
        QimaoZl028bLineName => "zl028b",
        QimaoZl028cLineName => "zl028c",
        QimaoZl027aLineName => "zl027a",
        QimaoZl027bLineName => "zl027b",
        QimaoZl027cLineName => "zl027c",
        Dingxing10LineName => "dxj10",
        Dingxing11LineName => "dxj11",
        HongbeiLineName => "hongbei",
        S7TestLineName => "s7test",
        SubscribeDashboardLineName => "subscribe",
        _ => "xianhe"
    };

    public static LineProfile SubscribeDashboard { get; } = new(
        SubscribeDashboardLineName,
        string.Empty,
        string.Empty,
        []);

    public static LineProfile S7Test { get; } = new(
        S7TestLineName,
        "192.168.0.10",
        LineMqttDefaults.S7TestPublishTopic,
        S7TestTags());

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

    public static LineProfile Zhidahy { get; } = new(
        ZhidahyLineName,
        "172.14.1.210",
        LineMqttDefaults.ZhidahyPublishTopic,
        ZhidahyTags());

    public static LineProfile Shanghaihy { get; } = new(
        ShanghaihyLineName,
        "172.14.1.211",
        LineMqttDefaults.ShanghaihyPublishTopic,
        ShanghaihyTags());

    public static LineProfile Meshy { get; } = new(
        MeshyLineName,
        "172.14.1.212",
        LineMqttDefaults.MeshyPublishTopic,
        MeshyTags());

    /// <summary>强力起毛机 MA876A：厂内编号 / PLC IP 来自确认版点表。</summary>
    public static LineProfile QimaoZl028a { get; } = new(
        QimaoZl028aLineName,
        "172.14.3.204",
        LineMqttDefaults.QimaoZl028aPublishTopic,
        QimaoMa876aTags());

    public static LineProfile QimaoZl028b { get; } = new(
        QimaoZl028bLineName,
        "172.14.3.202",
        LineMqttDefaults.QimaoZl028bPublishTopic,
        QimaoMa876aTags());

    public static LineProfile QimaoZl028c { get; } = new(
        QimaoZl028cLineName,
        "172.14.3.200",
        LineMqttDefaults.QimaoZl028cPublishTopic,
        QimaoMa876aTags());

    public static LineProfile QimaoZl027a { get; } = new(
        QimaoZl027aLineName,
        "172.14.3.219",
        LineMqttDefaults.QimaoZl027aPublishTopic,
        QimaoMa876aTags());

    public static LineProfile QimaoZl027b { get; } = new(
        QimaoZl027bLineName,
        "172.14.3.216",
        LineMqttDefaults.QimaoZl027bPublishTopic,
        QimaoMa876aTags());

    public static LineProfile QimaoZl027c { get; } = new(
        QimaoZl027cLineName,
        "172.14.3.213",
        LineMqttDefaults.QimaoZl027cPublishTopic,
        QimaoMa876aTags());

    /// <summary>定型机 10 号、11 号和烘焙机共用 2025043 点表。点表只给了 192.168.100.2，三条先用这个地址。</summary>
    public static LineProfile Dingxing10 { get; } = new(
        Dingxing10LineName,
        "192.168.100.2",
        LineMqttDefaults.Dingxing10PublishTopic,
        DingxingHongbeiTags());

    public static LineProfile Dingxing11 { get; } = new(
        Dingxing11LineName,
        "192.168.100.2",
        LineMqttDefaults.Dingxing11PublishTopic,
        DingxingHongbeiTags());

    public static LineProfile Hongbei { get; } = new(
        HongbeiLineName,
        "192.168.100.2",
        LineMqttDefaults.HongbeiPublishTopic,
        DingxingHongbeiTags());

    static List<PlcTag> QimaoMa876aTags() =>
    [
        new PlcTag
        {
            Name = "设备状态",
            XinjeAddress = "VW6000",
            DataType = TagDataType.Int16,
            DisplayCategory = TagDisplayCategory.Switch,
            SwitchStatusLabels = "0=待机;1=运行;2=故障"
        },
        S7ScaledInt16("锡林转速", "VW6002", "r/min", scale: 1),
        S7ScaledInt16("后导急停延时", "VW6006", "s", scale: 0.1),
        S7ScaledInt16("上导辊张力", "VW6012", "%", scale: 0.1),
        S7ScaledInt16("后导辊张力", "VW6014", "%", scale: 0.1),
        S7ScaledInt16("顺针辊转速", "VW6016", "r/min", scale: 1),
        S7ScaledInt16("逆针辊转速", "VW6018", "r/min", scale: 1),
        S7ScaledInt16("布速", "VW6020", "m/min", scale: 0.1),
        S7ScaledInt16("前导辊张力", "VW6022", "%", scale: 0.1),
        S7ScaledInt16("锡林微调", "VW6026", "r/min", scale: 1),
        S7ScaledInt16("扩幅转速", "VW6028", "r/min", scale: 1),
        new PlcTag
        {
            Name = "设备运行时间",
            Unit = "h",
            XinjeAddress = "VD6030",
            DataType = TagDataType.Int32,
            ByteOrder = ByteOrder.ABCD,
            Scale = 1,
            DisplayCategory = TagDisplayCategory.Process
        }
    ];

    static PlcTag S7ScaledInt16(string name, string address, string unit, double scale) => new()
    {
        Name = name,
        Unit = unit,
        XinjeAddress = address,
        DataType = TagDataType.Int16,
        Scale = scale,
        DisplayCategory = TagDisplayCategory.Process
    };

    static List<PlcTag> DingxingHongbeiTags() =>
    [
        S7RangedInt16("1#排风实际速度", "DB55.DBW0", "%", 30, 100),
        S7RangedInt16("2#排风实际速度", "DB55.DBW2", "%", 30, 100),
        S7RangedInt16("1#门幅实际值", "DB55.DBW4", "CM", 60, 220),
        S7RangedInt16("2#门幅实际值", "DB55.DBW6", "CM", 60, 220),
        S7RangedInt16("3#门幅实际值", "DB55.DBW8", "CM", 60, 220),
        S7RangedInt16("4#门幅实际值", "DB55.DBW10", "CM", 60, 220),
        S7RangedInt16("5#门幅实际值", "DB55.DBW12", "CM", 60, 220),
        S7RangedInt16("6#门幅实际值", "DB55.DBW14", "CM", 60, 220),
        S7RangedInt16("7#门幅实际值", "DB55.DBW16", "CM", 60, 220),
        S7RangedInt16("8#门幅实际值", "DB55.DBW18", "CM", 60, 220),
        S7RangedInt16("1+2上风实际速度", "DB55.DBW28", "%", 30, 100),
        S7RangedInt16("3+4上风实际速度", "DB55.DBW30", "%", 30, 100),
        S7RangedInt16("5+6上风实际速度", "DB55.DBW32", "%", 30, 100),
        S7RangedInt16("7+8上风实际速度", "DB55.DBW34", "%", 30, 100),
        S7RangedInt16("9+10上风实际速度", "DB55.DBW36", "%", 30, 100),
        S7RangedInt16("1+2下风实际速度", "DB55.DBW38", "%", 30, 100),
        S7RangedInt16("3+4下风实际速度", "DB55.DBW40", "%", 30, 100),
        S7RangedInt16("5+6下风实际速度", "DB55.DBW42", "%", 30, 100),
        S7RangedInt16("7+8下风实际速度", "DB55.DBW44", "%", 30, 100),
        S7RangedInt16("9+10下风实际速度", "DB55.DBW46", "%", 30, 100),
        S7RangedInt16("第一箱温度", "DB55.DBW48", "℃", 0, 250, TagDisplayCategory.Temperature),
        S7RangedInt16("第二箱温度", "DB55.DBW50", "℃", 0, 250, TagDisplayCategory.Temperature),
        S7RangedInt16("第三箱温度", "DB55.DBW52", "℃", 0, 250, TagDisplayCategory.Temperature),
        S7RangedInt16("第四箱温度", "DB55.DBW54", "℃", 0, 250, TagDisplayCategory.Temperature),
        S7RangedInt16("第五箱温度", "DB55.DBW56", "℃", 0, 250, TagDisplayCategory.Temperature),
        S7RangedInt16("第六箱温度", "DB55.DBW58", "℃", 0, 250, TagDisplayCategory.Temperature),
        S7RangedInt16("第七箱温度", "DB55.DBW60", "℃", 0, 250, TagDisplayCategory.Temperature),
        S7RangedInt16("第八箱温度", "DB55.DBW62", "℃", 0, 250, TagDisplayCategory.Temperature),
        S7RangedInt16("第九箱温度", "DB55.DBW64", "℃", 0, 250, TagDisplayCategory.Temperature),
        S7RangedInt16("第十箱温度", "DB55.DBW66", "℃", 0, 250, TagDisplayCategory.Temperature),
        S7RangedInt16("机器实际速度", "DB55.DBW68", "M/MIN", 0, 100),
        S7RangedInt16("进布实际张力", "DB55.DBW70", "N", 0, 300),
        S7RangedInt16("轧车实际张力", "DB55.DBW72", "N", 0, 300),
        S7RangedInt16("下超喂实际值", "DB55.DBW74", "%", -300, 300, scale: 0.1),
        S7RangedInt16("上超喂实际值", "DB55.DBW76", "%", -150, 800, scale: 0.1),
        S7RangedInt16("出布超喂实际值", "DB55.DBW78", "%", -200, 500, scale: 0.1),
        S7RangedInt16("摆布超喂实际值", "DB55.DBW80", "%", -300, 300, scale: 0.1),
        S7RangedInt16("提布实际张力", "DB55.DBW114", "N", 0, 300),
        S7RangedInt16("表面打卷超喂实际值", "DB55.DBW116", "%", -300, 300, scale: 0.1),
        S7RangedInt16("冷水棍超喂实际值", "DB55.DBW118", "%", -300, 300, scale: 0.1),
        S7RangedInt16("J-BOX超喂实际值", "DB55.DBW120", "%", -300, 300, scale: 0.1),
        S7RangedInt16("2#出布超喂实际值", "DB55.DBW122", "%", -300, 300, scale: 0.1)
    ];

    static PlcTag S7RangedInt16(
        string name,
        string address,
        string unit,
        double validMin,
        double validMax,
        TagDisplayCategory? category = null,
        double scale = 1) => new()
    {
        Name = name,
        Unit = unit,
        XinjeAddress = address,
        DataType = TagDataType.Int16,
        Scale = scale,
        ValidMin = validMin,
        ValidMax = validMax,
        DisplayCategory = category ?? TagDisplayCategory.Process
    };

    static List<PlcTag> FlameCommonManuals() =>
    [
        ManualReal("切刀间距"),
        ManualReal("门幅", "mm"),
        ManualReal("厚度", "mm"),
        ManualString("流转卡号", useScannerInput: true)
    ];

    static List<PlcTag> ZhidahyTags()
    {
        var tags = new List<PlcTag>
        {
            FlameDeviceStatus(),
            Real("海绵送料1", "D51"),
            Real("海绵送料2", "D53"),
            Real("成品速度", "D67"),
            Real("主机上", "D61"),
            Real("主机中", "D63"),
            Real("主机下", "D65"),
            Real("面料送料", "D55"),
            Real("针板轨道", "D57"),
            Real("上火口火力", "D35", "%"),
            Real("上隔距", "D71", "mm"),
            Real("下隔距", "D73", "mm"),
            Real("计米", "D39")
        };
        tags.AddRange(FlameCommonManuals());
        return tags;
    }

    static List<PlcTag> ShanghaihyTags()
    {
        var tags = new List<PlcTag>
        {
            FlameDeviceStatus(),
            Real("海绵送料1", "D51"),
            Real("海绵送料2", "D53"),
            Real("面料送料", "D55"),
            Real("针板轨道", "D57"),
            Real("主机上", "D61"),
            Real("主机中", "D63"),
            Real("主机下", "D65"),
            Real("成品1速度", "D67"),
            Real("成品2速度", "D69"),
            Real("上火口火力", "D35", "%"),
            Real("下火口火力", "D37", "%"),
            Real("上隔距", "D71", "mm"),
            Real("下隔距", "D73", "mm"),
            Real("计米", "D39")
        };
        tags.AddRange(FlameCommonManuals());
        return tags;
    }

    static List<PlcTag> MeshyTags()
    {
        var tags = new List<PlcTag>
        {
            FlameDeviceStatus(),
            Real("海绵送料1", "D51"),
            Real("海绵送料2", "D53"),
            Real("面料送料", "D55"),
            Real("针板轨道", "D57"),
            Real("下火口送料", "D59"),
            Real("主机上", "D61"),
            Real("主机中", "D63"),
            Real("主机下", "D65"),
            Real("成品1速度", "D67"),
            Real("成品2速度", "D69"),
            Real("上隔距", "D71", "mm"),
            Real("下隔距", "D73", "mm"),
            Real("上火口火力", "D35", "%"),
            Real("下火口火力", "D37", "%"),
            Real("计米", "D39")
        };
        tags.AddRange(FlameCommonManuals());
        return tags;
    }

    static PlcTag FlameDeviceStatus() => new()
    {
        Name = "设备状态",
        XinjeAddress = "D1",
        DataType = TagDataType.Int16,
        DisplayCategory = TagDisplayCategory.Switch
    };

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

    static List<PlcTag> S7TestTags() =>
    [
        S7Word("运行状态", "DB1.DBW0", TagDisplayCategory.Switch),
        S7Real("DB温度Real", "DB1.DBD4", "℃"),
        S7Real("DB工艺Real", "DB1.DBD8"),
        new PlcTag
        {
            Name = "温度示例加10",
            Source = TagSource.Computed,
            Expression = "[DB温度Real]+10",
            DataType = TagDataType.Float32,
            Unit = "℃",
            Enabled = false,
            DisplayCategory = TagDisplayCategory.Temperature
        },
        // I/Q 区地址因 CPU/模块而异，默认禁用；在 TIA 确认偏移后再启用。
        S7Word("输入字IW64", "IW64", enabled: false),
        S7Real("输入Real_ID100", "ID100", enabled: false),
        S7Bool("输入位I00", "I0.0", enabled: false),
        ManualString("测试备注", defaultValue: "S7 联调")
    ];

    static PlcTag S7Bool(string name, string address, bool enabled = true) => new()
    {
        Name = name,
        XinjeAddress = address,
        DataType = TagDataType.Bool,
        Enabled = enabled,
        DisplayCategory = TagDisplayCategory.Switch
    };

    static PlcTag S7Word(string name, string address, TagDisplayCategory? category = null, bool enabled = true) => new()
    {
        Name = name,
        XinjeAddress = address,
        DataType = TagDataType.Int16,
        Enabled = enabled,
        DisplayCategory = category ?? TagDisplayCategory.Process
    };

    static PlcTag S7Real(string name, string address, string unit = "", bool enabled = true) => new()
    {
        Name = name,
        Unit = unit,
        XinjeAddress = address,
        DataType = TagDataType.Float32,
        Enabled = enabled,
        ByteOrder = ByteOrder.ABCD,
        DisplayCategory = unit.Contains('℃', StringComparison.Ordinal) ||
                         name.Contains("温度", StringComparison.Ordinal)
            ? TagDisplayCategory.Temperature
            : TagDisplayCategory.Process
    };

    static PlcTag CloneAndResolve(PlcTag source, string lineName)
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
            SwitchStatusLabels = source.SwitchStatusLabels,
            ValidMin = source.ValidMin,
            ValidMax = source.ValidMax,
            UseScannerInput = source.UseScannerInput,
            Expression = source.Expression,
            Scale = source.Scale,
            Offset = source.Offset,
            Quadratic = source.Quadratic,
            Enabled = source.Enabled
        };

        if (tag.IsPlc)
        {
            if (UsesS7Protocol(lineName))
            {
                PlcAddressMapper.ApplyTo(tag, PlcProtocol.S7);
            }
            else
            {
                XinjeXd5eMapper.ApplyTo(tag);
            }
        }

        return tag;
    }

    public static string GetShortDisplayName(string? lineName) => lineName switch
    {
        "华迪热熔胶复合机" => "华迪",
        "撒粉复合机" => "撒粉",
        "平板复合机" => "平板",
        "C型火焰复合机" => "C型火焰",
        ZhidahyLineName => "智达",
        ShanghaihyLineName => "上海",
        MeshyLineName => "MES",
        QimaoZl028aLineName => "ZL028A",
        QimaoZl028bLineName => "ZL028B",
        QimaoZl028cLineName => "ZL028C",
        QimaoZl027aLineName => "ZL027A",
        QimaoZl027bLineName => "ZL027B",
        QimaoZl027cLineName => "ZL027C",
        Dingxing10LineName => "定型10",
        Dingxing11LineName => "定型11",
        HongbeiLineName => "烘焙",
        S7TestLineName => "S7测试",
        SubscribeDashboardLineName => "订阅大屏",
        "先河热熔胶复合机" => "先河",
        _ => string.IsNullOrWhiteSpace(lineName) ? "产线" : lineName.Trim()
    };

    public static bool TryResolveLineName(string? input, out string lineName)
    {
        lineName = string.Empty;
        if (string.IsNullOrWhiteSpace(input))
        {
            return false;
        }

        var text = input.Trim();
        foreach (var name in LineNames)
        {
            if (name.Equals(text, StringComparison.OrdinalIgnoreCase))
            {
                lineName = name;
                return true;
            }
        }

        var key = text.Replace(" ", "", StringComparison.Ordinal).ToUpperInvariant();
        lineName = key switch
        {
            "先河" or "XIANHE" or "XHRRJFHJ" => "先河热熔胶复合机",
            "华迪" or "HUADI" or "HDRRJFHJ" => "华迪热熔胶复合机",
            "撒粉" or "SAFEN" or "SFHFJ" => "撒粉复合机",
            "平板" or "PINGBAN" or "PBHFJ" => "平板复合机",
            "C型" or "C型火焰" or "CYHY" or "CYHYFJ" => "C型火焰复合机",
            "智达" or "智达机" or "智达火焰" or "ZHIDAHY" or "ZDHYFJ" => ZhidahyLineName,
            "上海" or "上海机" or "进口机" or "进口机-上海机" or "上海火焰" or "SHANGHAIHY" or "SHHYFJ" => ShanghaihyLineName,
            "MES" or "复合机MES" or "MES火焰" or "MESHY" or "MESHYFJ" => MeshyLineName,
            "ZL028A" or "起毛机ZL-028A" or "起毛028A" or "QMZL028A" => QimaoZl028aLineName,
            "ZL028B" or "起毛机ZL-028B" or "起毛028B" or "QMZL028B" => QimaoZl028bLineName,
            "ZL028C" or "起毛机ZL-028C" or "起毛028C" or "QMZL028C" => QimaoZl028cLineName,
            "ZL027A" or "起毛机ZL-027A" or "起毛027A" or "QMZL027A" => QimaoZl027aLineName,
            "ZL027B" or "起毛机ZL-027B" or "起毛027B" or "QMZL027B" => QimaoZl027bLineName,
            "ZL027C" or "起毛机ZL-027C" or "起毛027C" or "QMZL027C" => QimaoZl027cLineName,
            "定型机10号" or "定型10" or "定型机10" or "DXJ10" or "HGDXJ10" => Dingxing10LineName,
            "定型机11号" or "定型11" or "定型机11" or "DXJ11" or "HGDXJ11" => Dingxing11LineName,
            "烘焙机" or "烘焙" or "HONGPEI" or "HGHBJ" => HongbeiLineName,
            "S7" or "S7TEST" or "S7测试" => S7TestLineName,
            "订阅" or "订阅大屏" or "SUBSCRIBE" or "SUB" => SubscribeDashboardLineName,
            _ => string.Empty
        };
        return lineName.Length > 0;
    }

    public static string ToCommandAlias(string? lineName)
    {
        if (!TryResolveLineName(lineName, out var resolved))
        {
            return string.IsNullOrWhiteSpace(lineName) ? "xianhe" : lineName.Trim();
        }

        return resolved switch
        {
            "华迪热熔胶复合机" => "huadi",
            "撒粉复合机" => "safen",
            "平板复合机" => "pingban",
            "C型火焰复合机" => "cyhy",
            ZhidahyLineName => "zhidahy",
            ShanghaihyLineName => "shanghaihy",
            MeshyLineName => "meshy",
            QimaoZl028aLineName => "zl028a",
            QimaoZl028bLineName => "zl028b",
            QimaoZl028cLineName => "zl028c",
            QimaoZl027aLineName => "zl027a",
            QimaoZl027bLineName => "zl027b",
            QimaoZl027cLineName => "zl027c",
            Dingxing10LineName => "dxj10",
            Dingxing11LineName => "dxj11",
            HongbeiLineName => "hongbei",
            S7TestLineName => "s7",
            SubscribeDashboardLineName => "subscribe",
            _ => "xianhe"
        };
    }
}

public sealed record LineProfile(string Name, string Host, string MqttTopic, IReadOnlyList<PlcTag> Tags);
