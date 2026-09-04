using ClosedXML.Excel;
using HuaGuang.Monitor.Models;
using HuaGuang.Monitor.Protocols;
using HuaGuang.Monitor.Services;
using SyncPlanningExcel;

var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
var previewOnly = args.Contains("--preview", StringComparer.OrdinalIgnoreCase);
var createLines = args.Contains("--create-lines", StringComparer.OrdinalIgnoreCase);
var dumpLine = GetArgValue(args, "--dump");
var positionalArgs = args
    .Where(arg => !arg.StartsWith("--", StringComparison.Ordinal))
    .Where(arg => !string.Equals(arg, dumpLine, StringComparison.Ordinal))
    .ToArray();
var planningPath = positionalArgs.Length > 0
    ? Path.GetFullPath(positionalArgs[0])
    : Path.Combine(root, "华光数据地址规划.xlsx");
var linesDir = positionalArgs.Length > 1
    ? Path.GetFullPath(positionalArgs[1])
    : Path.Combine(root, "config", "lines");
var templatePath = Path.Combine(linesDir, "先河热熔胶复合机.xlsx");

if (previewOnly)
{
    Environment.ExitCode = PreviewDiff.Run(planningPath, linesDir);
    return;
}

if (!string.IsNullOrWhiteSpace(dumpLine))
{
    DumpCompare.Run(linesDir, dumpLine, planningPath);
    return;
}

Console.WriteLine($"规划: {planningPath}");
Console.WriteLine($"产线目录: {linesDir}");

ApplyXianhe(Path.Combine(linesDir, "先河热熔胶复合机.xlsx"), ReadPlannedIp(planningPath, "先河热熔胶复合机") ?? "172.14.1.200");
ApplyHuadi(Path.Combine(linesDir, "华迪热熔胶复合机.xlsx"), ReadPlannedIp(planningPath, "华迪热熔胶复合机") ?? "172.14.1.201");

if (createLines)
{
    CreateNewLine(planningPath, templatePath, linesDir, "撒粉复合机", ReadPlannedIp(planningPath, "撒粉复合机") ?? "172.14.1.202");
    CreateNewLine(planningPath, templatePath, linesDir, "平板复合机", ReadPlannedIp(planningPath, "平板复合机") ?? "172.14.1.203");
    CreateNewLine(planningPath, templatePath, linesDir, "C型火焰复合机", ReadPlannedIp(planningPath, "C型火焰复合机") ?? "172.14.1.204");
}

Console.WriteLine("完成（仅更新先河/华迪；不新增热熔胶机温度；保留产品货号）。");

static string? GetArgValue(string[] args, string key)
{
    for (var i = 0; i < args.Length; i++)
    {
        if (!args[i].Equals(key, StringComparison.OrdinalIgnoreCase))
        {
            continue;
        }

        return i + 1 < args.Length ? args[i + 1] : null;
    }

    return null;
}

static void ApplyXianhe(string filePath, string plcHost)
{
    var settings = LoadLine(filePath, "先河热熔胶复合机");
    settings.Plc.Host = plcHost;
    EnsureInjectionTag(settings.Tags);
    EnsureProductSkuTag(settings.Tags);
    EnsureCurrentInjectionMachineTag(settings.Tags);
    EnsureCurrentInjectionGroup(settings.Tags);
    MqttFieldMappingCatalog.ApplyDefaults(settings.Tags, settings.LineName);
    SaveLine(settings, filePath);
    Console.WriteLine($"已更新: {filePath}");
}

static void ApplyHuadi(string filePath, string plcHost)
{
    var settings = LoadLine(filePath, "华迪热熔胶复合机");
    settings.Plc.Host = plcHost;
    settings.Tags.RemoveAll(tag => tag.Name is "上卷出转速率" or "下卷出转速率");

    UpsertPlcTag(settings.Tags, "上展开转速率", "D1080");
    UpsertPlcTag(settings.Tags, "下展开转速率", "D1120");

    var speed = settings.Tags.FirstOrDefault(tag => tag.Name == "车速");
    if (speed is not null)
    {
        speed.XinjeAddress = "D18";
        XinjeXd5eMapper.ApplyTo(speed);
    }

    EnsureInjectionTag(settings.Tags);
    EnsureProductSkuTag(settings.Tags);
    EnsureCurrentInjectionMachineTag(settings.Tags);
    EnsureCurrentInjectionGroup(settings.Tags);
    MqttFieldMappingCatalog.ApplyDefaults(settings.Tags, settings.LineName);
    SaveLine(settings, filePath);
    Console.WriteLine($"已更新: {filePath}");
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
            return sheet.Cell(row, 2).GetString().Trim();
        }
    }

    return null;
}

static void CreateNewLine(string planningPath, string templatePath, string linesDir, string lineName, string plcHost)
{
    var destination = Path.Combine(linesDir, $"{lineName}.xlsx");
    var settings = LoadLine(templatePath, lineName);
    settings.LineName = lineName;
    settings.DeviceId = lineName;
    settings.Plc.Host = plcHost;
    settings.Mqtt.Topic = LineMqttDefaults.ResolvePublishTopic(lineName);
    settings.Mqtt.ClientId = LineMqttDefaults.ResolveClientIdForLine(lineName);
    settings.Tags = BuildTagsFromPlanning(planningPath, lineName);
    MqttFieldMappingCatalog.ApplyDefaults(settings.Tags, lineName);
    SaveLine(settings, destination);
    Console.WriteLine($"已新建: {destination}");
}

static AppSettings LoadLine(string filePath, string lineName)
{
    var settings = LineExcelConfigService.LoadLineExcelFromFile(filePath, templateFilePath: null, expectedLineName: null);
    settings.LineName = lineName;
    settings.DeviceId = lineName;
    return settings;
}

static void SaveLine(AppSettings settings, string filePath)
{
    settings.AddressCatalogVersion = LineCatalog.Version;
    LineExcelConfigService.Export(settings, filePath);
}

static void EnsureInjectionTag(IList<PlcTag> tags)
{
    if (tags.Any(tag => tag.Name == "注胶量"))
    {
        return;
    }

    tags.Add(new PlcTag
    {
        Name = "注胶量",
        Source = TagSource.Manual,
        DataType = TagDataType.Float32,
        ManualValue = string.Empty,
        DisplayCategory = TagDisplayCategory.Setting,
        Enabled = true
    });
}

static void EnsureProductSkuTag(IList<PlcTag> tags)
{
    if (tags.Any(tag => tag.Name == LineCatalog.ProductSkuTagName))
    {
        return;
    }

    tags.Add(new PlcTag
    {
        Name = LineCatalog.ProductSkuTagName,
        Source = TagSource.Manual,
        DataType = TagDataType.String,
        ManualValue = string.Empty,
        DisplayCategory = TagDisplayCategory.Setting,
        UseScannerInput = true,
        Enabled = true
    });
}

static void EnsureCurrentInjectionGroup(IList<PlcTag> tags)
{
    foreach (var tag in tags.Where(CurrentInjectionFormatting.IsRelatedTag))
    {
        tag.DisplayCategory = TagDisplayCategory.Temperature;
    }
}

static void EnsureCurrentInjectionMachineTag(IList<PlcTag> tags) =>
    UpsertPlcIntTag(tags, "当前注胶机编号", "D1130");

static void UpsertPlcIntTag(IList<PlcTag> tags, string name, string address)
{
    var tag = tags.FirstOrDefault(item => item.Name == name);
    if (tag is null)
    {
        tag = new PlcTag { Name = name, Enabled = true };
        tags.Add(tag);
    }

    tag.Source = TagSource.Plc;
    tag.XinjeAddress = address;
    tag.DataType = TagDataType.Int16;
    tag.DisplayCategory = TagDisplayCategory.Temperature;
    XinjeXd5eMapper.ApplyTo(tag);
}

static void UpsertPlcTag(IList<PlcTag> tags, string name, string address)
{
    var tag = tags.FirstOrDefault(item => item.Name == name);
    if (tag is null)
    {
        tag = new PlcTag { Name = name, Enabled = true };
        tags.Add(tag);
    }

    tag.Source = TagSource.Plc;
    tag.XinjeAddress = address;
    tag.DataType = TagDataType.Float32;
    tag.ByteOrder = ByteOrder.CDAB;
    tag.DisplayCategory = TagDisplayCategoryHelper.InferCategory(tag);
    XinjeXd5eMapper.ApplyTo(tag);
}

static List<PlcTag> BuildTagsFromPlanning(string planningPath, string sheetName)
{
    using var workbook = new XLWorkbook(planningPath);
    if (!workbook.Worksheets.TryGetWorksheet(sheetName, out var sheet))
    {
        throw new InvalidOperationException($"规划表缺少工作表「{sheetName}」。");
    }

    var tags = new List<PlcTag>();
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

        tags.Add(CreateTagFromPlanning(name, sourceText, address, dataTypeText));
    }

    if (tags.Count == 0)
    {
        throw new InvalidOperationException($"规划表「{sheetName}」未解析到任何点位。");
    }

    return tags;
}

static bool IsManualSource(string sourceText) =>
    sourceText.Contains('手', StringComparison.Ordinal) &&
    !sourceText.Contains("机台获取", StringComparison.Ordinal);

static PlcTag CreateTagFromPlanning(string name, string sourceText, string address, string dataTypeText)
{
    var manual = IsManualSource(sourceText);
    var tag = new PlcTag
    {
        Name = name,
        Enabled = true,
        Source = manual ? TagSource.Manual : TagSource.Plc
    };

    if (RunStatusFormatting.IsRunStatusTag(tag) ||
        dataTypeText.Equals("INT", StringComparison.OrdinalIgnoreCase))
    {
        tag.Source = TagSource.Plc;
        tag.DataType = TagDataType.Int16;
        tag.XinjeAddress = address;
        tag.DisplayCategory = TagDisplayCategory.Switch;
        XinjeXd5eMapper.ApplyTo(tag);
        return tag;
    }

    if (manual)
    {
        tag.DataType = name.Contains("型号", StringComparison.Ordinal)
            ? TagDataType.String
            : TagDataType.Float32;
        tag.ManualValue = string.Empty;
        tag.DisplayCategory = TagDisplayCategory.Setting;
        return tag;
    }

    tag.DataType = TagDataType.Float32;
    tag.ByteOrder = ByteOrder.CDAB;
    tag.XinjeAddress = address;
    tag.Unit = name.Contains("温度", StringComparison.Ordinal) ? "℃" : string.Empty;
    tag.DisplayCategory = TagDisplayCategoryHelper.InferCategory(tag);
    XinjeXd5eMapper.ApplyTo(tag);
    return tag;
}
