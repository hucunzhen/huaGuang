namespace HuaGuang.Monitor.Services.LogExport;

/// <summary>无系统目录选择器时：使用配置路径或默认 log-export。</summary>
public sealed class FallbackLogExportLocationService : ILogExportLocationService
{
    public Task<LogExportLocationChoice?> PickDirectoryAsync(string? previousLocationHint)
    {
        var path = AppPaths.ResolveLogExportDirectory(
            previousLocationHint is not null && !previousLocationHint.StartsWith("content://", StringComparison.OrdinalIgnoreCase)
                ? previousLocationHint
                : null);

        return Task.FromResult<LogExportLocationChoice?>(new LogExportLocationChoice
        {
            SettingsStorageValue = path,
            DisplayPath = path
        });
    }

    public Task<LogExportDeliveryResult> WriteZipAsync(
        LogExportLocationChoice location,
        string localZipPath,
        string zipFileName)
    {
        var directory = location.SettingsStorageValue;
        Directory.CreateDirectory(directory);
        var targetPath = Path.Combine(directory, zipFileName);
        if (File.Exists(targetPath))
        {
            File.Delete(targetPath);
        }

        File.Copy(localZipPath, targetPath, overwrite: true);
        return Task.FromResult(new LogExportDeliveryResult
        {
            DisplayPath = targetPath,
            SettingsStorageValue = directory
        });
    }
}
