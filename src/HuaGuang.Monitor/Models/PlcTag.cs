namespace HuaGuang.Monitor.Models;

public sealed class PlcTag
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = string.Empty;
    public string Unit { get; set; } = string.Empty;
    public string XinjeAddress { get; set; } = "D0";
    public bool Enabled { get; set; } = true;
    public ModbusTable Table { get; set; } = ModbusTable.HoldingRegister;
    public ushort Address { get; set; }
    public TagDataType DataType { get; set; } = TagDataType.Float32;
    public ByteOrder ByteOrder { get; set; } = ByteOrder.CDAB;
    public double Scale { get; set; } = 1;
    public double Offset { get; set; }

    public string DisplayAddress => string.IsNullOrWhiteSpace(XinjeAddress)
        ? $"{Table}:{Address}"
        : XinjeAddress;
}
