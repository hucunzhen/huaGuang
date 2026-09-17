namespace HuaGuang.Monitor.Services.LogExport;

public sealed class LogExportLocationChoice
{
    public required string SettingsStorageValue { get; init; }
    public required string DisplayPath { get; init; }
    internal object? PlatformState { get; init; }
}

public sealed class LogExportDeliveryResult
{
    public required string DisplayPath { get; init; }
    public required string SettingsStorageValue { get; init; }
}

/// <summary>系统目录选择（含 Android USB 存储）并将 zip 写入所选位置。</summary>
public interface ILogExportLocationService
{
    Task<LogExportLocationChoice?> PickDirectoryAsync(string? previousLocationHint);

    Task<LogExportDeliveryResult> WriteZipAsync(
        LogExportLocationChoice location,
        string localZipPath,
        string zipFileName);
}
