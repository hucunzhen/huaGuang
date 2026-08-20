namespace HuaGuang.Monitor.Models;

public enum AppOperationMode
{
    Acquisition,
    Subscribe
}

public enum TagSource
{
    Plc,
    Manual
}

public enum TagDataType
{
    Bool,
    Int16,
    UInt16,
    Int32,
    UInt32,
    Float32,
    String
}

public enum ModbusTable
{
    Coil,
    DiscreteInput,
    HoldingRegister,
    InputRegister
}

public enum ByteOrder
{
    /// <summary>大端，高字在前。</summary>
    ABCD,
    /// <summary>字交换，西门子 / 多数国产 PLC 浮点常用。</summary>
    CDAB,
    /// <summary>字节交换。</summary>
    BADC,
    /// <summary>小端。</summary>
    DCBA
}
