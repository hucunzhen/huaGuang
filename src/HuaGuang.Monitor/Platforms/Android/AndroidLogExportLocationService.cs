using Android.Provider;
using HuaGuang.Monitor.Services.LogExport;
using Microsoft.Maui.ApplicationModel;
using AUri = Android.Net.Uri;

namespace HuaGuang.Monitor.Platforms.Android;

public sealed class AndroidLogExportLocationService : ILogExportLocationService
{
    public async Task<LogExportLocationChoice?> PickDirectoryAsync(string? previousLocationHint)
    {
        var activity = Platform.CurrentActivity as MainActivity;
        if (activity is null)
        {
            throw new InvalidOperationException("无法打开目录选择器：Activity 不可用。");
        }

        var persistedTree = AndroidSafTreeUri.TryParsePersistedTree(previousLocationHint);
        var initial = AndroidSafTreeUri.ToInitialPickerUri(persistedTree);

        var treeUri = await activity.PickDocumentTreeAsync(initial).ConfigureAwait(false);
        if (treeUri is null)
        {
            return null;
        }

        AndroidSafTreeUri.TryTakePersistablePermission(activity, treeUri);

        var display = DescribeTreeUri(treeUri);
        return new LogExportLocationChoice
        {
            SettingsStorageValue = treeUri.ToString()!,
            DisplayPath = display,
            PlatformState = treeUri
        };
    }

    public Task<LogExportDeliveryResult> WriteZipAsync(
        LogExportLocationChoice location,
        string localZipPath,
        string zipFileName)
    {
        var treeUri = ResolveTreeUri(location);
        var resolver = Platform.CurrentActivity?.ContentResolver
                       ?? throw new InvalidOperationException("ContentResolver 不可用。");

        var safeName = AndroidSafTreeUri.ToSafFileName(zipFileName);
        var parentUri = AndroidSafTreeUri.ToCreateDocumentParent(treeUri);

        AUri? docUri = null;
        Exception? lastError = null;
        foreach (var parent in new[] { parentUri, treeUri })
        {
            try
            {
                docUri = DocumentsContract.CreateDocument(resolver, parent, "application/zip", safeName);
                if (docUri is not null)
                {
                    break;
                }
            }
            catch (Exception ex)
            {
                lastError = ex;
            }
        }

        if (docUri is null)
        {
            throw new IOException(
                lastError is null
                    ? "无法在所选目录创建 zip 文件。"
                    : $"无法在所选目录创建 zip 文件：{lastError.Message}");
        }

        using (var input = File.OpenRead(localZipPath))
        using (var output = resolver.OpenOutputStream(docUri)
                                  ?? throw new IOException("无法写入所选目录。"))
        {
            input.CopyTo(output);
        }

        var display = $"{location.DisplayPath}/{safeName}";
        return Task.FromResult(new LogExportDeliveryResult
        {
            DisplayPath = display,
            SettingsStorageValue = treeUri.ToString()!
        });
    }

    static AUri ResolveTreeUri(LogExportLocationChoice location)
    {
        if (location.PlatformState is AUri platformUri)
        {
            return platformUri;
        }

        var parsed = AndroidSafTreeUri.TryParsePersistedTree(location.SettingsStorageValue)
                     ?? throw new InvalidOperationException(
                         "所选目录无效，请重新选择（Android 请用系统目录选择器选 USB/SD 卡，不要手填路径）。");

        return parsed;
    }

    static string DescribeTreeUri(AUri treeUri) =>
        treeUri.LastPathSegment ?? treeUri.ToString() ?? "已选存储";
}
