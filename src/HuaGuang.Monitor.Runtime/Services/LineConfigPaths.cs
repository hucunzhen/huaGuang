using HuaGuang.Monitor.Models;

namespace HuaGuang.Monitor.Services;

/// <summary>
/// 产线 Excel 路径。安装包内 <c>lines/{产线名}.xlsx</c> 来自仓库 <c>config/lines</c>，为点表与配置的唯一来源。
/// </summary>
public static class LineConfigPaths
{
    /// <summary>与程序同目录的 lines（Windows 安装目录，内容与 config/lines 一致）。</summary>
    public static string InstallLinesDirectory => Path.Combine(AppContext.BaseDirectory, "lines");

    /// <summary>用户可写目录（Android 或安装目录不可写时使用）。</summary>
    public static string UserLinesDirectory => AppPaths.UserLinesDirectory;

    public static string LinesDirectory => UsesInstallLinesDirectory()
        ? InstallLinesDirectory
        : UserLinesDirectory;

    public static string ActiveLineFilePath => Path.Combine(LinesDirectory, "当前产线.txt");

    public static string GetLineExcelPath(string lineName) =>
        Path.Combine(LinesDirectory, $"{lineName}.xlsx");

    /// <summary>安装包随带的原始 Excel（config/lines 发布副本，只读参考）。</summary>
    public static string? ResolveShippedLineExcelPath(string lineName)
    {
        var installPath = Path.Combine(InstallLinesDirectory, $"{lineName}.xlsx");
        if (File.Exists(installPath))
        {
            return installPath;
        }

        return null;
    }

    public static string ReadActiveLineName()
    {
        if (File.Exists(ActiveLineFilePath))
        {
            var name = File.ReadAllText(ActiveLineFilePath).Trim();
            if (LineCatalog.LineNames.Contains(name))
            {
                return name;
            }
        }

        return LineCatalog.LineNames[0];
    }

    public static void WriteActiveLineName(string lineName)
    {
        Directory.CreateDirectory(LinesDirectory);
        File.WriteAllText(ActiveLineFilePath, lineName);
    }

    public static void EnsureAllLineExcels()
    {
        Directory.CreateDirectory(LinesDirectory);
        foreach (var lineName in LineCatalog.LineNames)
        {
            EnsureLineExcel(lineName);
        }
    }

    public static void EnsureLineExcel(string lineName)
    {
        Directory.CreateDirectory(LinesDirectory);
        var path = GetLineExcelPath(lineName);
        if (File.Exists(path))
        {
            LineExcelConfigService.EnsureLineFile(path, lineName, ResolveShippedLineExcelPath(lineName));
            return;
        }

        if (TryCopyShippedLineExcel(lineName, path))
        {
            LineExcelConfigService.EnsureLineFile(path, lineName, ResolveShippedLineExcelPath(lineName));
            return;
        }

        if (TryCopyBundledLineFile(lineName, path))
        {
            LineExcelConfigService.EnsureLineFile(path, lineName, ResolveShippedLineExcelPath(lineName));
        }
    }

    public static void SaveLine(AppSettings settings)
    {
        Directory.CreateDirectory(LinesDirectory);
        WriteActiveLineName(settings.LineName);
        LineExcelConfigService.Export(settings, GetLineExcelPath(settings.LineName));
    }

    /// <summary>兼容旧调用。</summary>
    public static string ConfigFilePath => GetLineExcelPath(ReadActiveLineName());

    public static string GetExcelPath(string? lineName = null) =>
        GetLineExcelPath(string.IsNullOrWhiteSpace(lineName) ? ReadActiveLineName() : lineName);

    static bool TryCopyShippedLineExcel(string lineName, string destinationPath)
    {
        var shipped = ResolveShippedLineExcelPath(lineName);
        if (shipped is null || PathsEqual(shipped, destinationPath))
        {
            return false;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
        File.Copy(shipped, destinationPath, overwrite: true);
        return true;
    }

    static bool UsesInstallLinesDirectory()
    {
#if WINDOWS
        return IsDirectoryWritable(InstallLinesDirectory);
#else
        return false;
#endif
    }

    static bool IsDirectoryWritable(string directory)
    {
        try
        {
            Directory.CreateDirectory(directory);
            var probe = Path.Combine(directory, $".write_probe_{Guid.NewGuid():N}");
            File.WriteAllText(probe, "1");
            File.Delete(probe);
            return true;
        }
        catch
        {
            return false;
        }
    }

    static bool PathsEqual(string left, string right) =>
        string.Equals(
            Path.GetFullPath(left),
            Path.GetFullPath(right),
            StringComparison.OrdinalIgnoreCase);

    static bool TryCopyBundledLineFile(string lineName, string destinationPath)
    {
        _ = lineName;
        _ = destinationPath;
        return false;
    }
}
