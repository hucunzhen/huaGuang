using HuaGuang.Monitor.Models;
using HuaGuang.Monitor.Services;

var outputDir = args.Length > 0
    ? Path.GetFullPath(args[0])
    : Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "config", "lines"));

Directory.CreateDirectory(outputDir);

var exitCode = 0;
foreach (var lineName in LineCatalog.LineNames)
{
    var settings = LineExcelConfigService.CreateSeedSettings(lineName);
    var path = Path.Combine(outputDir, $"{lineName}.xlsx");
    try
    {
        LineExcelConfigService.Export(settings, path);
        Console.WriteLine(path);
    }
    catch (IOException ex)
    {
        exitCode = 1;
        var fallback = Path.Combine(outputDir, $"{lineName}.new.xlsx");
        LineExcelConfigService.Export(settings, fallback);
        Console.Error.WriteLine($"WARN: 无法覆盖 {path}（{ex.Message}），已写入 {fallback}");
    }
}

return exitCode;
