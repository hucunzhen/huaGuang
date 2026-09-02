using HuaGuang.Monitor.Models;

namespace HuaGuang.Monitor.Services;

public static class TagScannerHelper
{
    public static bool SupportsScannerInput(PlcTag tag) =>
        tag.IsManual &&
        tag.DataType == TagDataType.String &&
        (tag.UseScannerInput ||
         string.Equals(tag.Name, LineCatalog.ProductSkuTagName, StringComparison.Ordinal));

    public static bool CanConfigureScannerInput(PlcTag tag) =>
        tag.IsManual && tag.DataType == TagDataType.String;
}
