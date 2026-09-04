using ClosedXML.Excel;
using HuaGuang.Monitor.Models;
using HuaGuang.Monitor.Services;

namespace SyncPlanningExcel;

static class DumpCompare
{
    public static void Run(string linesDir, string lineName, string planningPath)
    {
        var file = Path.Combine(linesDir, $"{lineName}.xlsx");
        var settings = LineExcelConfigService.LoadLineExcelFromFile(file, null, null);
        Console.WriteLine($"=== {lineName} IP={settings.Plc.Host} tags={settings.Tags.Count} ===");
        foreach (var tag in settings.Tags.OrderBy(t => t.Name))
        {
            var addr = tag.Source == TagSource.Plc ? tag.XinjeAddress : "(manual)";
            var category = TagDisplayCategoryHelper.Resolve(tag);
            Console.WriteLine($"{tag.Name} | {tag.Source} | {addr} | {tag.DataType} | {TagDisplayCategoryHelper.GetTitle(category)}");
        }

        using var wb = new XLWorkbook(planningPath);
        var sheet = wb.Worksheet(lineName);
        Console.WriteLine($"=== planning {lineName} ===");
        var last = sheet.LastRowUsed()?.RowNumber() ?? 1;
        for (var row = 2; row <= last; row++)
        {
            var n = sheet.Cell(row, 1).GetString().Trim();
            if (string.IsNullOrWhiteSpace(n))
            {
                continue;
            }

            var s = sheet.Cell(row, 2).GetString().Trim();
            var a = sheet.Cell(row, 3).GetString().Trim();
            var d = sheet.Cell(row, 4).GetString().Trim();
            Console.WriteLine($"{n} | {s} | {a} | {d}");
        }
    }
}
