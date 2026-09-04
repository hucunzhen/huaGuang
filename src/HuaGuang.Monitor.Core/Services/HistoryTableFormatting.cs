namespace HuaGuang.Monitor.Services;

public static class HistoryTableFormatting
{
    /// <summary>历史表格最多显示的点位列数（支持横向滚动）。</summary>
    public const int MaxColumns = 64;
    public const int PageSize = 40;
    public const int TimeWidth = 14;
    public const int DeviceWidth = 10;
    public const int TagWidth = 8;

    public const double ColumnSpacing = 8;
    public const double TimeColumnWidth = 118;
    public const double DeviceColumnWidth = 88;
    public const double TagColumnWidth = 76;
    public const double DeleteColumnWidth = 48;

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

    /// <summary>表格内容区最小宽度（像素），供横向滚动容器使用。</summary>
    public static double EstimateContentWidth(int tagColumnCount)
    {
        if (tagColumnCount <= 0)
        {
            return TimeColumnWidth + ColumnSpacing + DeviceColumnWidth + DeleteColumnWidth;
        }

        return TimeColumnWidth
               + ColumnSpacing
               + DeviceColumnWidth
               + ColumnSpacing
               + tagColumnCount * TagColumnWidth
               + (tagColumnCount - 1) * ColumnSpacing
               + ColumnSpacing
               + DeleteColumnWidth;
    }

    public static double EstimateContentWidth(string headerLine, double charWidth = 7.6) =>
        EstimateContentWidth(Math.Max(0, CountTagColumns(headerLine)));

    static int CountTagColumns(string headerLine)
    {
        if (string.IsNullOrWhiteSpace(headerLine))
        {
            return 0;
        }

        var prefix = Pad("时间", TimeWidth) + " " + Pad("设备", DeviceWidth);
        if (headerLine.Length <= prefix.Length)
        {
            return 0;
        }

        var remainder = headerLine[prefix.Length..].Trim();
        if (string.IsNullOrEmpty(remainder))
        {
            return 0;
        }

        return remainder.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;
    }
}
