namespace HuaGuang.Monitor.Models;

/// <summary>
/// MQTT 遥测 JSON 报文结构配置（可在 Excel「MQTT报文」工作表编辑）。
/// </summary>
public sealed class MqttPayloadProfile
{
    /// <summary>json</summary>
    public string PayloadFormat { get; set; } = "json";

    /// <summary>点位容器路径，如 properties、tags、data.tags</summary>
    public string TagsPath { get; set; } = "properties";

    /// <summary>留空则不写入报文；设备编号仍用于 MQTT 主题。</summary>
    public string DeviceIdPath { get; set; } = string.Empty;
    public string TimestampPath { get; set; } = string.Empty;

    /// <summary>iso8601 / unix_ms / unix_s。TimestampPath 留空时不写入报文。</summary>
    public string TimestampFormat { get; set; } = "iso8601";

    public string QualityPath { get; set; } = string.Empty;
    public string PlcHostPath { get; set; } = string.Empty;
    public string SimulatorPath { get; set; } = string.Empty;

    /// <summary>点表 MQTT 字段留空时，是否用点位名称作为 JSON 键。</summary>
    public bool UseTagNameWhenFieldEmpty { get; set; }
}
