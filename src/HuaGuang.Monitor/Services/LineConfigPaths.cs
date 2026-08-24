using HuaGuang.Monitor.Models;

namespace HuaGuang.Monitor.Services;

public static class LineConfigPaths
{
    /// <summary>与程序同目录的 lines 文件夹（Windows 安装/解包目录，便于现场编辑）。</summary>
    public static string InstallLinesDirectory => Path.Combine(AppContext.BaseDirectory, "lines");

    /// <summary>用户可写目录（Android 或安装目录不可写时使用）。</summary>
    public static string UserLinesDirectory => AppPaths.UserLinesDirectory;

    public static string LinesDirectory => UsesInstallLinesDirectory()
        ? InstallLinesDirectory
        : UserLinesDirectory;

    public static string GetExcelPath(string lineName) =>
        Path.Combine(LinesDirectory, $"{lineName}.xlsx");

    public static void EnsureDefaultExcelFiles()
    {
        Directory.CreateDirectory(LinesDirectory);

        foreach (var lineName in LineCatalog.LineNames)
        {
            SyncLineFromShippedSource(lineName);
            var path = GetExcelPath(lineName);
            LineExcelConfigService.EnsureLineFile(path, lineName);

#if WINDOWS
            if (UsesInstallLinesDirectory())
            {
                var installPath = Path.Combine(InstallLinesDirectory, $"{lineName}.xlsx");
                if (!PathsEqual(installPath, path))
                {
                    LineExcelConfigService.EnsureLineFile(installPath, lineName);
                }
            }
#endif
        }

        if (!UsesInstallLinesDirectory())
        {
            return;
        }

        foreach (var lineName in LineCatalog.LineNames)
        {
            var installPath = Path.Combine(InstallLinesDirectory, $"{lineName}.xlsx");
            if (File.Exists(installPath))
            {
                continue;
            }

            Directory.CreateDirectory(InstallLinesDirectory);
            if (TryCopyBundledLineFile(lineName, installPath))
            {
                continue;
            }

            LineExcelConfigService.EnsureLineFile(installPath, lineName);
        }
    }

    public static void SyncCurrentLine(AppSettings settings)
    {
        var path = GetExcelPath(settings.LineName);
        LineExcelConfigService.EnsureLineFile(path, settings.LineName);
        if (File.Exists(path))
        {
            LineExcelConfigService.Apply(settings, path);
        }
    }

    public static void SaveCurrentLine(AppSettings settings)
    {
        var path = GetExcelPath(settings.LineName);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        LineExcelConfigService.Export(settings, path);
    }

    static void SyncLineFromShippedSource(string lineName)
    {
        var destination = GetExcelPath(lineName);
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);

        if (File.Exists(destination))
        {
            return;
        }

#if WINDOWS
        var installPath = Path.Combine(InstallLinesDirectory, $"{lineName}.xlsx");
        if (File.Exists(installPath) && !PathsEqual(installPath, destination))
        {
            File.Copy(installPath, destination, overwrite: true);
            return;
        }
#endif

        TryCopyBundledLineFile(lineName, destination);
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
        var assetPath = $"lines/{LineCatalog.GetBundledAssetName(lineName)}.xlsx";
        try
        {
            using var stream = FileSystem.OpenAppPackageFileAsync(assetPath).GetAwaiter().GetResult();
            Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
            using var destination = File.Create(destinationPath);
            stream.CopyTo(destination);
            return true;
        }
        catch
        {
            return false;
        }
    }
}
