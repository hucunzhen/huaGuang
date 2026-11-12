using System.Globalization;
using ClosedXML.Excel;
using HuaGuang.Monitor.Models;

namespace HuaGuang.Monitor.Services;

/// <summary>把历史表格写成 xlsx。时间为本地 yyyy-MM-dd HH:mm:ss，数值单元格写成数字。</summary>
public sealed class HistoryExcelWriter : IDisposable
{
    public const int ExportPageSize = 400;
    const string EmptyMark = "—";

    readonly XLWorkbook _workbook = new();
    readonly IXLWorksheet _sheet;
    int _nextRow = 2;

    public HistoryExcelWriter(IReadOnlyList<HistoryTableColumn> columns)
    {
        Columns = columns;
        _sheet = _workbook.Worksheets.Add("历史数据");
        _sheet.Cell(1, 1).Value = "时间";
        _sheet.Cell(1, 2).Value = "设备";
        for (var i = 0; i < columns.Count; i++)
        {
            _sheet.Cell(1, i + 3).Value = columns[i].HeaderText;
        }

        var lastColumn = Math.Max(2, columns.Count + 2);
        _sheet.Range(1, 1, 1, lastColumn).Style.Font.Bold = true;
        _sheet.SheetView.FreezeRows(1);
        _sheet.Column(1).Width = 20;
        _sheet.Column(2).Width = 18;
        for (var i = 0; i < columns.Count; i++)
        {
            _sheet.Column(i + 3).Width = Math.Clamp(columns[i].HeaderText.Length + 4, 12, 28);
        }
    }

    public IReadOnlyList<HistoryTableColumn> Columns { get; }

    public void AddRows(IReadOnlyList<HistoryTableRow> rows)
    {
        foreach (var row in rows)
        {
            _sheet.Cell(_nextRow, 1).Value = FormatTime(row);
            _sheet.Cell(_nextRow, 2).Value = row.DeviceId;
            for (var i = 0; i < Columns.Count; i++)
            {
                var text = i < row.TagCells.Count ? row.TagCells[i].Text : string.Empty;
                WriteValue(_sheet.Cell(_nextRow, i + 3), text);
            }

            _nextRow++;
        }
    }

    public void Save(string path)
    {
        var lastColumn = Math.Max(2, Columns.Count + 2);
        if (_nextRow > 2)
        {
            _sheet.Range(1, 1, _nextRow - 1, lastColumn).SetAutoFilter();
        }

        _workbook.SaveAs(path);
    }

    public void Dispose() => _workbook.Dispose();

    static string FormatTime(HistoryTableRow row) =>
        row.RecordedAt == default
            ? row.RecordedAtText
            : row.RecordedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss");

    static void WriteValue(IXLCell cell, string text)
    {
        if (string.IsNullOrWhiteSpace(text) || text == EmptyMark || text == "-")
        {
            return;
        }

        if (double.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out var number) ||
            double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out number))
        {
            cell.Value = number;
            return;
        }

        cell.Value = text;
    }
}
