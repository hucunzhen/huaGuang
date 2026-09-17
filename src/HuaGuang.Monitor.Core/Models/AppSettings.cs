namespace HuaGuang.Monitor.Models;

using HuaGuang.Monitor.Services;

public sealed class AppSettings
{
    public string DeviceId { get; set; } = "先河热熔胶复合机";
    public string LineName { get; set; } = "先河热熔胶复合机";
    public int AddressCatalogVersion { get; set; }
    public int ScanIntervalMs { get; set; } = 2_000;
    /// <summary>MQTT 数据发布最小间隔（毫秒），与 PLC 采集周期独立。</summary>
    public int PublishIntervalMs { get; set; } = 60_000;
    public bool UseSimulator { get; set; } = true;
    /// <summary>0 = 仅按发布周期上报；大于 0 时，任一温度点位变化达到该值（℃）也会触发 MQTT 发布。</summary>
    public double TemperaturePublishThresholdC { get; set; }
    /// <summary>全局默认显示与 MQTT 小数位数（0–4），适用于温度及各类模拟量；点位未单独设置时使用。</summary>
    public const int DefaultTemperaturePrecision = 2;

    public int TemperaturePrecision { get; set; } = DefaultTemperaturePrecision;
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
    /// <summary>多个 MQTT 发布目标；采集模式下会同时向所有已启用目标发送。</summary>
    public List<MqttEndpoint> MqttEndpoints { get; set; } = [];
    public MqttPayloadProfile MqttPayload { get; set; } = new();
    public List<PlcTag> Tags { get; set; } = [];
    /// <summary>是否记录采集/订阅数据到本地 SQLite。</summary>
    public bool EnableHistoryRecording { get; set; } = true;
    /// <summary>历史数据保留天数；超出后自动清理。</summary>
    public int HistoryRetentionDays { get; set; } = 1;
    /// <summary>诊断页「打包日志」输出目录；为空时使用数据目录下 log-export。</summary>
    public string LogExportDirectory { get; set; } = string.Empty;
    /// <summary>一次性迁移标记；避免每次启动重复覆盖用户配置。</summary>
    public int SettingsMigrationVersion { get; set; }

    /// <summary>最近一次从产线 Excel 加载时的非致命告警（如地址与协议不匹配而被禁用的点位）。</summary>
    public List<string> ConfigLoadWarnings { get; set; } = [];
}

public sealed class PlcSettings
{
    public PlcProtocol Protocol { get; set; } = PlcProtocol.ModbusTcp;
    public string Model { get; set; } = "XD5E-60T10";
    public string Host { get; set; } = "192.168.6.10";
    public int Port { get; set; } = 502;
    public byte Station { get; set; } = 1;
    public int TimeoutMs { get; set; } = 2000;
    /// <summary>S7 机架号（Rack）。</summary>
    public int Rack { get; set; }
    /// <summary>S7 槽位（Slot）；S7-1200/1500 在 S7.Net 中通常为 0，S7-300/400 常为 2。</summary>
    public int Slot { get; set; }
    /// <summary>S7 CPU 类型，如 S71200、S71500。</summary>
    public string CpuType { get; set; } = "S71200";
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
