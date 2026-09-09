namespace HuaGuang.Monitor.Services;

/// <summary>Android/iOS：从 MauiAsset（lines/*.xlsx）解压产线 Excel 到用户目录。</summary>
public sealed class MauiBundledLineFileProvider : IBundledLineFileProvider
{
    public bool TryCopyBundledLineFile(string lineName, string destinationPath)
    {
        var assetPath = GetPackageAssetPath(lineName);
        if (assetPath is null)
        {
            return false;
        }

        try
        {
            using var source = FileSystem.OpenAppPackageFileAsync(assetPath).ConfigureAwait(false).GetAwaiter().GetResult();
            var directory = Path.GetDirectoryName(destinationPath);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            using var target = File.Create(destinationPath);
            source.CopyTo(target);
            return true;
        }
        catch (FileNotFoundException)
        {
            return false;
        }
        catch (IOException)
        {
            return false;
        }
    }

    public string? ResolveBundledTemplatePath(string lineName)
    {
        var cachePath = Path.Combine(
            AppPaths.UserLinesDirectory,
            ".bundled",
            $"{LineCatalog.GetBundledAssetName(lineName)}.xlsx");

        if (File.Exists(cachePath))
        {
            return cachePath;
        }

        return TryCopyBundledLineFile(lineName, cachePath) ? cachePath : null;
    }

    static string? GetPackageAssetPath(string lineName)
    {
        if (!LineCatalog.LineNames.Contains(lineName))
        {
            return null;
        }

        return $"lines/{LineCatalog.GetBundledAssetName(lineName)}.xlsx";
    }
}
