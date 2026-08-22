namespace HuaGuang.Monitor.Services;

public static class HistoryTableFormatting
{
    public const int MaxColumns = 16;
    public const int PageSize = 40;
    public const int TimeWidth = 14;
    public const int DeviceWidth = 10;
    public const int TagWidth = 8;

    public static string FormatHeaderLine(IReadOnlyList<HistoryTableColumn> columns)
    {
        var parts = new List<string>
        {
            Pad("时间", TimeWidth),
            Pad("设备", DeviceWidth)
        };
        parts.AddRange(columns.Select(column => Pad(ShortName(column.TagName), TagWidth)));
        return string.Join(" ", parts);
    }

    public static string FormatDataLine(string time, string device, IReadOnlyList<string> cells)
    {
        var parts = new List<string>
        {
            Pad(time, TimeWidth),
            Pad(device, DeviceWidth)
        };
        parts.AddRange(cells.Select(cell => Pad(cell, TagWidth)));
        return string.Join(" ", parts);
    }

    static string ShortName(string name) =>
        name.Length <= TagWidth ? name : name[..Math.Max(1, TagWidth - 1)] + "…";

    static string Pad(string text, int width)
    {
        if (string.IsNullOrEmpty(text))
        {
            return new string(' ', width);
        }

        if (text.Length > width)
        {
            return width <= 1 ? text[..width] : text[..(width - 1)] + "…";
        }

        return text.PadRight(width);
    }
}
