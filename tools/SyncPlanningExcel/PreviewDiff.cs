using ClosedXML.Excel;
using HuaGuang.Monitor.Models;
using HuaGuang.Monitor.Services;

namespace SyncPlanningExcel;

static class PreviewDiff
{
    public static int Run(string planningPath, string linesDir)
    {
        using var planning = new XLWorkbook(planningPath);
        var lineSheets = planning.Worksheets
            .Select(sheet => sheet.Name)
            .Where(name => !name.StartsWith("Sheet", StringComparison.OrdinalIgnoreCase))
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToList();

        Console.WriteLine($"规划文件: {planningPath}");
        Console.WriteLine($"产线目录: {linesDir}");
        Console.WriteLine($"规划工作表 ({lineSheets.Count}): {string.Join("、", lineSheets)}");
        Console.WriteLine();

        var exitCode = 0;
        foreach (var lineName in lineSheets)
        {
            exitCode |= PrintLineDiff(planningPath, linesDir, lineName);
        }

        var existingFiles = Directory.Exists(linesDir)
            ? Directory.GetFiles(linesDir, "*.xlsx").Select(Path.GetFileNameWithoutExtension).ToHashSet(StringComparer.Ordinal)
            : [];
        foreach (var orphan in existingFiles.Except(lineSheets, StringComparer.Ordinal).OrderBy(name => name, StringComparer.Ordinal))
        {
            Console.WriteLine($"## {orphan}");
            Console.WriteLine("  [仅本地存在，规划表无对应工作表]");
            Console.WriteLine();
        }

        return exitCode;
    }

    static int PrintLineDiff(string planningPath, string linesDir, string lineName)
    {
        var lineFile = Path.Combine(linesDir, $"{lineName}.xlsx");
        var planned = BuildPlannedTags(planningPath, lineName);
        var plannedIp = ReadPlannedIp(planningPath, lineName);

        Console.WriteLine($"## {lineName}");

        if (!File.Exists(lineFile))
        {
            Console.WriteLine("  [新建产线 Excel]");
            if (!string.IsNullOrWhiteSpace(plannedIp))
            {
                Console.WriteLine($"  PLC IP: (无) -> {plannedIp}");
            }

            PrintTagList("  规划点位", planned);
            Console.WriteLine();
            return 1;
        }

        var current = LoadCurrentTags(lineFile);
        var currentIp = LoadCurrentIp(lineFile);

        if (!string.IsNullOrWhiteSpace(plannedIp) &&
            !string.Equals(plannedIp, currentIp, StringComparison.OrdinalIgnoreCase))
        {
            Console.WriteLine($"  PLC IP: {currentIp ?? "(空)"} -> {plannedIp}");
        }

        CompareTags(current, planned);
        Console.WriteLine();
        return 0;
    }

    static void CompareTags(Dictionary<string, TagSnapshot> current, Dictionary<string, TagSnapshot> planned)
    {
        var allNames = current.Keys.Concat(planned.Keys).Distinct(StringComparer.Ordinal).OrderBy(name => name, StringComparer.Ordinal);
        var added = new List<string>();
        var removed = new List<string>();
        var changed = new List<string>();

        foreach (var name in allNames)
        {
            var hasCurrent = current.TryGetValue(name, out var cur);
            var hasPlanned = planned.TryGetValue(name, out var plan);
            if (!hasCurrent && hasPlanned)
            {
                added.Add(FormatTag(plan!));
                continue;
            }

            if (hasCurrent && !hasPlanned)
            {
                removed.Add(FormatTag(cur!));
                continue;
            }

            if (!hasCurrent || !hasPlanned)
            {
                continue;
            }

            if (!cur!.Equals(plan!))
            {
                changed.Add($"{name}: {FormatTag(cur)} -> {FormatTag(plan!)}");
            }
        }

        if (added.Count == 0 && removed.Count == 0 && changed.Count == 0)
        {
            Console.WriteLine("  (点位无变化)");
            return;
        }

        if (added.Count > 0)
        {
            Console.WriteLine("  + 新增:");
            foreach (var item in added)
            {
                Console.WriteLine($"      {item}");
            }
        }

        if (removed.Count > 0)
        {
            Console.WriteLine("  - 删除:");
            foreach (var item in removed)
            {
                Console.WriteLine($"      {item}");
            }
        }

        if (changed.Count > 0)
        {
            Console.WriteLine("  ~ 变更:");
            foreach (var item in changed)
            {
                Console.WriteLine($"      {item}");
            }
        }
    }

    static void PrintTagList(string title, Dictionary<string, TagSnapshot> tags)
    {
        Console.WriteLine($"{title} ({tags.Count}):");
        foreach (var tag in tags.Values.OrderBy(item => item.Name, StringComparer.Ordinal))
        {
            Console.WriteLine($"      {FormatTag(tag)}");
        }
    }

    static string FormatTag(TagSnapshot tag) =>
        $"{tag.Name} | {tag.Source} | {tag.Address} | {tag.DataType} | {tag.DisplayCategory}";

    static Dictionary<string, TagSnapshot> LoadCurrentTags(string filePath)
    {
        var settings = LineExcelConfigService.LoadLineExcelFromFile(filePath, templateFilePath: null, expectedLineName: null);
        return settings.Tags.ToDictionary(
            tag => tag.Name,
            tag => TagSnapshot.From(tag),
            StringComparer.Ordinal);
    }

    static string? LoadCurrentIp(string filePath)
    {
        var settings = LineExcelConfigService.LoadLineExcelFromFile(filePath, templateFilePath: null, expectedLineName: null);
        return string.IsNullOrWhiteSpace(settings.Plc.Host) ? null : settings.Plc.Host.Trim();
    }

    static string? ReadPlannedIp(string planningPath, string sheetName)
    {
        using var workbook = new XLWorkbook(planningPath);
        if (!workbook.Worksheets.TryGetWorksheet(sheetName, out var sheet))
        {
            return null;
        }

        var lastRow = sheet.LastRowUsed()?.RowNumber() ?? 1;
        for (var row = 2; row <= lastRow; row++)
        {
            var name = sheet.Cell(row, 1).GetString().Trim();
            if (name.StartsWith("IP", StringComparison.OrdinalIgnoreCase))
            {
                return sheet.Cell(row, 3).GetString().Trim();
            }
        }

        return null;
    }

    static Dictionary<string, TagSnapshot> BuildPlannedTags(string planningPath, string sheetName)
    {
        using var workbook = new XLWorkbook(planningPath);
        if (!workbook.Worksheets.TryGetWorksheet(sheetName, out var sheet))
        {
            throw new InvalidOperationException($"规划表缺少工作表「{sheetName}」。");
        }

        var tags = new Dictionary<string, TagSnapshot>(StringComparer.Ordinal);
        var lastRow = sheet.LastRowUsed()?.RowNumber() ?? 1;
        for (var row = 2; row <= lastRow; row++)
        {
            var name = sheet.Cell(row, 1).GetString().Trim();
            if (string.IsNullOrWhiteSpace(name) ||
                name.Equals("PLC", StringComparison.OrdinalIgnoreCase) ||
                name.StartsWith("IP", StringComparison.OrdinalIgnoreCase) ||
                name is "子网掩码" or "网关")
            {
                continue;
            }

            var sourceText = sheet.Cell(row, 2).GetString().Trim();
            var address = sheet.Cell(row, 3).GetString().Trim();
            var dataTypeText = sheet.Cell(row, 4).GetString().Trim();
            if (string.IsNullOrWhiteSpace(address) && !IsManualSource(sourceText))
            {
                continue;
            }

            var snapshot = TagSnapshot.FromPlanning(name, sourceText, address, dataTypeText);
            tags[name] = snapshot;
        }

        return tags;
    }

    static bool IsManualSource(string sourceText) =>
        sourceText.Contains('手', StringComparison.Ordinal) &&
        !sourceText.Contains("机台获取", StringComparison.Ordinal);

    sealed record TagSnapshot(string Name, string Source, string Address, string DataType, string DisplayCategory)
    {
        public static TagSnapshot From(PlcTag tag) => new(
            tag.Name,
            tag.Source.ToString(),
            tag.Source == TagSource.Plc ? tag.XinjeAddress : tag.ManualValue ?? string.Empty,
            tag.DataType.ToString(),
            tag.DisplayCategory.ToString());

        public static TagSnapshot FromPlanning(string name, string sourceText, string address, string dataTypeText)
        {
            var manual = IsManualSource(sourceText);
            var source = manual ? "Manual" : "Plc";
            var dataType = RunStatusFormatting.IsRunStatusTag(new PlcTag { Name = name }) ||
                           dataTypeText.Equals("INT", StringComparison.OrdinalIgnoreCase)
                ? "Int16"
                : manual
                    ? (name.Contains("型号", StringComparison.Ordinal) ? "String" : "Float32")
                    : "Float32";
            var display = RunStatusFormatting.IsRunStatusTag(new PlcTag { Name = name })
                ? "Switch"
                : manual
                    ? "Setting"
                    : "Default";
            return new TagSnapshot(name, source, manual ? string.Empty : address, dataType, display);
        }
    }
}
