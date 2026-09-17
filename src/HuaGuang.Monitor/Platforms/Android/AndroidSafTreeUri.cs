using Android.Content;
using Android.Provider;
using AUri = Android.Net.Uri;

namespace HuaGuang.Monitor.Platforms.Android;

static class AndroidSafTreeUri
{
    public static AUri? TryParsePersistedTree(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var trimmed = value.Trim();
        if (!trimmed.StartsWith("content://", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        try
        {
            var uri = AUri.Parse(trimmed);
            return DocumentsContract.IsTreeUri(uri) ? uri : null;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>OPEN_DOCUMENT_TREE 的 EXTRA_INITIAL_URI 需为 tree 下的 document URI。</summary>
    public static AUri? ToInitialPickerUri(AUri? treeUri)
    {
        if (treeUri is null)
        {
            return null;
        }

        try
        {
            if (!DocumentsContract.IsTreeUri(treeUri))
            {
                return treeUri;
            }

            var treeId = DocumentsContract.GetTreeDocumentId(treeUri);
            return DocumentsContract.BuildDocumentUriUsingTree(treeUri, treeId);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>CreateDocument 的父目录 URI（多数设备 / USB 需此格式）。</summary>
    public static AUri ToCreateDocumentParent(AUri treeUri)
    {
        if (!DocumentsContract.IsTreeUri(treeUri))
        {
            return treeUri;
        }

        var treeId = DocumentsContract.GetTreeDocumentId(treeUri);
        return DocumentsContract.BuildDocumentUriUsingTree(treeUri, treeId);
    }

    public static void TryTakePersistablePermission(global::Android.App.Activity activity, AUri treeUri)
    {
        try
        {
            var flags = ActivityFlags.GrantReadUriPermission | ActivityFlags.GrantWriteUriPermission;
            activity.ContentResolver?.TakePersistableUriPermission(treeUri, flags);
        }
        catch
        {
        }
    }

    /// <summary>SAF 创建文件时仅用 ASCII 文件名，避免 USB 报 Invalid URI。</summary>
    public static string ToSafFileName(string? suggestedZipFileName)
    {
        var stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
        var fallback = $"huaGuang-logs-{stamp}.zip";
        if (string.IsNullOrWhiteSpace(suggestedZipFileName))
        {
            return fallback;
        }

        var name = Path.GetFileName(suggestedZipFileName);
        var builder = new System.Text.StringBuilder(name.Length);
        foreach (var ch in name)
        {
            if (ch is >= 'a' and <= 'z' or >= 'A' and <= 'Z' or >= '0' and <= '9' or '.' or '-' or '_')
            {
                builder.Append(ch);
            }
        }

        var ascii = builder.ToString();
        if (ascii.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) && ascii.Length > 4)
        {
            return ascii;
        }

        return fallback;
    }
}
