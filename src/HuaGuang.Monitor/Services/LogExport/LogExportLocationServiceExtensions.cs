namespace HuaGuang.Monitor.Services.LogExport;

public static class LogExportLocationServiceExtensions
{
    public static async Task<LogExportDeliveryResult?> PickDirectoryAndDeliverZipAsync(
        this ILogExportLocationService service,
        string localZipPath,
        string zipFileName,
        string? previousLocationHint)
    {
        var pick = await service.PickDirectoryAsync(previousLocationHint).ConfigureAwait(false);
        if (pick is null)
        {
            return null;
        }

        return await service.WriteZipAsync(pick, localZipPath, zipFileName).ConfigureAwait(false);
    }
}
