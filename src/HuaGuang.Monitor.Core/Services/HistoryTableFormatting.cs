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
    public const double MinColumnWidth = 48;
    public const double CellHorizontalPadding = 12;
    /// <summary>数据区纵向滚动条占位，表头需预留同宽以免列错位。</summary>
    public const double VerticalScrollBarGutter = 12;

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
    public static double EstimateContentWidth(int tagColumnCount) =>
        EstimateContentWidth(TimeColumnWidth, DeviceColumnWidth, Enumerable.Repeat(TagColumnWidth, tagColumnCount));

    public static double EstimateContentWidth(double timeWidth, double deviceWidth, IEnumerable<double> tagWidths)
    {
        var tags = tagWidths.ToList();
        var total = timeWidth + ColumnSpacing + deviceWidth;
        if (tags.Count == 0)
        {
            return total + DeleteColumnWidth;
        }

        total += ColumnSpacing + tags.Sum() + (tags.Count - 1) * ColumnSpacing + ColumnSpacing + DeleteColumnWidth;
        return total;
    }

    /// <summary>按表头与样本数据估算列宽（中文按双宽字符计）。</summary>
    public static double EstimateTextWidth(string referenceText, IEnumerable<string>? dataSamples = null, double minWidth = MinColumnWidth, double fontSize = 12)
    {
        var width = EstimateSingleTextBlock(referenceText, fontSize);
        if (dataSamples is not null)
        {
            foreach (var sample in dataSamples)
            {
                width = Math.Max(width, EstimateSingleTextBlock(sample, fontSize));
            }
        }

        return Math.Max(minWidth, Math.Ceiling(width + CellHorizontalPadding));
    }

    static double EstimateSingleTextBlock(string text, double fontSize)
    {
        if (string.IsNullOrEmpty(text))
        {
            return 0;
        }

        var max = 0d;
        foreach (var line in text.Split('\n'))
        {
            var lineWidth = 0d;
            foreach (var ch in line)
            {
                lineWidth += ch > 255 ? fontSize * 1.05 : fontSize * 0.62;
            }

            max = Math.Max(max, lineWidth);
        }

        return max;
    }
}
