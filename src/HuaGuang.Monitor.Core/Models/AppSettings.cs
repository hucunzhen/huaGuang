namespace HuaGuang.Monitor.Models;

using HuaGuang.Monitor.Services;

public sealed class AppSettings
{
    public string DeviceId { get; set; } = "先河热熔胶复合机";
    public string LineName { get; set; } = "先河热熔胶复合机";
    public int AddressCatalogVersion { get; set; }
    public int ScanIntervalMs { get; set; } = 1000;
    public bool UseSimulator { get; set; } = true;
    /// <summary>0 = 每次扫描都发布；大于 0 时，任一温度点位变化达到该值（℃）才发布 MQTT。</summary>
    public double TemperaturePublishThresholdC { get; set; }
    /// <summary>全局默认显示与 MQTT 小数位数（0–4）；点位未单独设置时使用。</summary>
    public int TemperaturePrecision { get; set; } = 1;
    /// <summary>Windows 登录后自动启动本程序。</summary>
    public bool StartWithWindows { get; set; } = true;
    /// <summary>程序启动后自动开始采集。</summary>
    public bool AutoStartAcquisition { get; set; } = true;
    /// <summary>采集模式或订阅模式。</summary>
    public AppOperationMode OperationMode { get; set; } = AppOperationMode.Acquisition;
    /// <summary>订阅模式下监听的 MQTT 主题，支持 + / # 通配。</summary>
    public List<string> SubscribeTopics { get; set; } =
    [
        LineMqttDefaults.XianhePublishTopic,
        LineMqttDefaults.HuadiPublishTopic
    ];
    /// <summary>兼容旧配置。</summary>
    public string SubscribeTopic { get; set; } = LineMqttDefaults.XianhePublishTopic;
    public PlcSettings Plc { get; set; } = new();
    public MqttSettings Mqtt { get; set; } = new();
    public MqttPayloadProfile MqttPayload { get; set; } = new();
    public List<PlcTag> Tags { get; set; } = [];
    /// <summary>是否记录采集/订阅数据到本地 SQLite。</summary>
    public bool EnableHistoryRecording { get; set; } = true;
    /// <summary>历史数据保留天数；超出后自动清理。</summary>
    public int HistoryRetentionDays { get; set; } = 14;
    /// <summary>一次性迁移标记；避免每次启动重复覆盖用户配置。</summary>
    public int SettingsMigrationVersion { get; set; }
}

public sealed class PlcSettings
{
    public string Model { get; set; } = "XD5E-60T10";
    public string Host { get; set; } = "192.168.6.10";
    public int Port { get; set; } = 502;
    public byte Station { get; set; } = 1;
    public int TimeoutMs { get; set; } = 2000;
}

public sealed class MqttSettings
{
    public string Host { get; set; } = LineMqttDefaults.Host;
    public int Port { get; set; } = LineMqttDefaults.Port;
    public string ClientId { get; set; } = string.Empty;
    public string Username { get; set; } = LineMqttDefaults.Username;
    public string Password { get; set; } = LineMqttDefaults.Password;
    public bool UseTls { get; set; }
    public int Qos { get; set; }
    public string Topic { get; set; } = LineMqttDefaults.XianhePublishTopic;
}
