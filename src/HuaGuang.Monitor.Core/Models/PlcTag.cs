namespace HuaGuang.Monitor.Models;

public sealed class PlcTag
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = string.Empty;
    public string Unit { get; set; } = string.Empty;
    public string XinjeAddress { get; set; } = "D0";
    public bool Enabled { get; set; } = true;
    public TagSource Source { get; set; } = TagSource.Plc;
    public string ManualValue { get; set; } = string.Empty;
    public ModbusTable Table { get; set; } = ModbusTable.HoldingRegister;
    public ushort Address { get; set; }
    public TagDataType DataType { get; set; } = TagDataType.Float32;
    public ByteOrder ByteOrder { get; set; } = ByteOrder.CDAB;
    public double Scale { get; set; } = 1;
    public double Offset { get; set; }
    /// <summary>显示与 MQTT 小数位数（0–4）。null 表示使用设置里的全局默认。</summary>
    public int? DisplayPrecision { get; set; }

    /// <summary>MQTT 报文 tags 对象中的字段名；留空则使用点位名称。</summary>
    public string MqttField { get; set; } = string.Empty;

    /// <summary>监控页显示分组；留空则按数据类型/名称自动推断（产线 Excel「点表·显示分组」）。</summary>
    public TagDisplayCategory? DisplayCategory { get; set; }

    /// <summary>手动文本点位是否在监控页支持 USB 扫码枪输入。</summary>
    public bool UseScannerInput { get; set; }

    public string DisplayAddress => Source == TagSource.Manual
        ? "手动输入"
        : string.IsNullOrWhiteSpace(XinjeAddress)
            ? $"{Table}:{Address}"
            : XinjeAddress;

    public bool IsManual => Source == TagSource.Manual;

    public bool IsTemperature =>
        Unit.Contains('℃', StringComparison.Ordinal) ||
        Name.Contains("温度", StringComparison.Ordinal);
}
