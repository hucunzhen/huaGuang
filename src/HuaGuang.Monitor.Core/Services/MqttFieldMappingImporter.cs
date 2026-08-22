using ClosedXML.Excel;
using HuaGuang.Monitor.Models;

namespace HuaGuang.Monitor.Services;

/// <summary>
/// 读取「字段映射」或独立映射 xlsx（列 id / name）。
/// </summary>
public static class MqttFieldMappingImporter
{
    public const string FieldMappingSheetName = "字段映射";

    public static bool IsFieldMappingWorkbook(XLWorkbook workbook) =>
        !workbook.Worksheets.TryGetWorksheet(LineExcelConfigService.ConfigSheetName, out _) &&
        TryFindMappingSheet(workbook, out _);

    public static int Apply(AppSettings settings, string filePath)
    {
        using var workbook = new XLWorkbook(filePath);
        return Apply(settings, workbook);
    }

    public static int Apply(AppSettings settings, XLWorkbook workbook)
    {
        if (!TryFindMappingSheet(workbook, out var sheet))
        {
            throw new InvalidOperationException("找不到字段映射工作表（需包含 id、name 列）。");
        }

        var rows = ReadMappingRows(sheet);
        return MqttFieldMappingCatalog.ApplyRows(settings.Tags, rows);
    }

    public static int ApplyFromWorkbookTags(AppSettings settings, XLWorkbook workbook)
    {
        if (!workbook.Worksheets.TryGetWorksheet(FieldMappingSheetName, out var sheet))
        {
            return 0;
        }

        return MqttFieldMappingCatalog.ApplyRows(settings.Tags, ReadMappingRows(sheet));
    }

    public static bool TryFindMappingSheet(XLWorkbook workbook, out IXLWorksheet sheet)
    {
        if (workbook.Worksheets.TryGetWorksheet(FieldMappingSheetName, out sheet!) &&
            HasMappingHeaders(sheet))
        {
            return true;
        }

        foreach (var worksheet in workbook.Worksheets)
        {
            if (HasMappingHeaders(worksheet))
            {
                sheet = worksheet;
                return true;
            }
        }

        sheet = null!;
        return false;
    }

    static bool HasMappingHeaders(IXLWorksheet sheet)
    {
        var idColumn = FindColumn(sheet, 1, "id", "mqtt字段", "mqtt_field", "字段", "键");
        var nameColumn = FindColumn(sheet, 1, "name", "名称", "点位名称", "点位", "点表名称");
        return idColumn > 0 && nameColumn > 0;
    }

    static int FindColumn(IXLWorksheet sheet, int headerRow, params string[] candidates)
    {
        var lastColumn = sheet.LastColumnUsed()?.ColumnNumber() ?? 1;
        for (var col = 1; col <= lastColumn; col++)
        {
            var header = sheet.Cell(headerRow, col).GetString().Trim();
            if (candidates.Any(candidate =>
                    string.Equals(header, candidate, StringComparison.OrdinalIgnoreCase)))
            {
                return col;
            }
        }

        return -1;
    }

    public static IEnumerable<(string Id, string Name)> ReadMappingRows(IXLWorksheet sheet)
    {
        var idColumn = FindColumn(sheet, 1, "id", "mqtt字段", "mqtt_field", "字段", "键");
        var nameColumn = FindColumn(sheet, 1, "name", "名称", "点位名称", "点位", "点表名称");
        if (idColumn <= 0 || nameColumn <= 0)
        {
            yield break;
        }

        var lastRow = sheet.LastRowUsed()?.RowNumber() ?? 1;
        for (var row = 2; row <= lastRow; row++)
        {
            var id = sheet.Cell(row, idColumn).GetString().Trim();
            var name = sheet.Cell(row, nameColumn).GetString().Trim();
            if (string.IsNullOrWhiteSpace(id) && string.IsNullOrWhiteSpace(name))
            {
                continue;
            }

            yield return (id, name);
        }
    }
}
