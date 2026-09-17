using HuaGuang.Monitor.Services.LogExport;
using WinRT.Interop;

namespace HuaGuang.Monitor.Platforms.Windows;

public sealed class WindowsLogExportLocationService : ILogExportLocationService
{
    public async Task<LogExportLocationChoice?> PickDirectoryAsync(string? previousLocationHint)
    {
        var window = Microsoft.Maui.Controls.Application.Current?.Windows.FirstOrDefault()?.Handler?.PlatformView
                     as Microsoft.UI.Xaml.Window;
        if (window is null)
        {
            throw new InvalidOperationException("无法打开目录选择器：窗口不可用。");
        }

        var picker = new global::Windows.Storage.Pickers.FolderPicker
        {
            SuggestedStartLocation = global::Windows.Storage.Pickers.PickerLocationId.DocumentsLibrary
        };
        picker.FileTypeFilter.Add("*");

        var hwnd = WindowNative.GetWindowHandle(window);
        InitializeWithWindow.Initialize(picker, hwnd);

        var folder = await picker.PickSingleFolderAsync();
        if (folder is null)
        {
            return null;
        }

        var path = folder.Path;
        return new LogExportLocationChoice
        {
            SettingsStorageValue = path,
            DisplayPath = path,
            PlatformState = path
        };
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
