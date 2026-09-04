using HuaGuang.Monitor.Models;
using HuaGuang.Monitor.Services;

var outputDir = args.Length > 0 && !args[0].StartsWith('-')
    ? Path.GetFullPath(args[0])
    : Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "config", "lines"));

if (args.Contains("--export-mapping-ref"))
{
    var repoRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
    var referencePath = Path.Combine(repoRoot, "config", MqttFieldMappingCatalog.ReferenceFileName);
    LineExcelConfigService.ExportReferenceFieldMapping(referencePath, LineCatalog.Xianhe.Name);
    Console.WriteLine(referencePath);
    return 0;
}

if (args.Contains("--inspect"))
{
    var files = args.Where(arg => arg.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase)).ToArray();
    if (files.Length == 0)
    {
        files = Directory.GetFiles(outputDir, "*.xlsx");
    }

    foreach (var file in files)
    {
        if (Path.GetFileName(file).StartsWith("~$", StringComparison.Ordinal))
        {
            continue;
        }

        var settings = new AppSettings();
        LineExcelConfigService.Apply(settings, file);
        Console.WriteLine($"=== {file} ===");
        foreach (var tag in settings.Tags.Where(tag =>
                     tag.Name.Contains("当前工作", StringComparison.Ordinal) ||
                     tag.Name.Contains("胶盘", StringComparison.Ordinal)))
        {
            Console.WriteLine(
                $"{tag.Name} | src={tag.Source} | addr={tag.XinjeAddress} | type={tag.DataType} | manual={tag.ManualValue}");
        }
    }

    return 0;
}

if (args.Contains("--patch"))
{
    Directory.CreateDirectory(outputDir);
    var exitCode = 0;
    foreach (var file in Directory.GetFiles(outputDir, "*.xlsx"))
    {
        if (Path.GetFileName(file).StartsWith("~$", StringComparison.Ordinal))
        {
            continue;
        }

        try
        {
            LineExcelConfigService.ApplyLineFileMaintenance(file);
            Console.WriteLine($"已维护: {file}");
        }
        catch (Exception ex)
        {
            exitCode = 1;
            Console.Error.WriteLine($"失败: {file} ({ex.Message})");
        }
    }

    return exitCode;
}

Directory.CreateDirectory(outputDir);

var seedExitCode = 0;
var force = args.Contains("--force");
foreach (var lineName in LineCatalog.LineNames)
{
    var settings = LineExcelConfigService.CreateSeedSettings(lineName);
    var path = Path.Combine(outputDir, $"{lineName}.xlsx");
    if (File.Exists(path) && !force)
    {
        Console.WriteLine($"跳过（已存在）: {path}");
        Console.WriteLine("  局部更新: dotnet run --project tools/GenerateLineExcel -- --patch");
        Console.WriteLine("  整本重建: dotnet run --project tools/GenerateLineExcel -- --force");
        continue;
    }

    try
    {
        LineExcelConfigService.Export(settings, path);
        Console.WriteLine(path);
    }
    catch (IOException ex)
    {
        seedExitCode = 1;
        var fallback = Path.Combine(outputDir, $"{lineName}.new.xlsx");
        LineExcelConfigService.Export(settings, fallback);
        Console.Error.WriteLine($"WARN: 无法覆盖 {path}（{ex.Message}），已写入 {fallback}");
    }
}

return seedExitCode;
