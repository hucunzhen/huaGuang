using System.IO.Compression;
using System.Text;

namespace HuaGuang.Monitor.Services.Logging;

public sealed record LogBundleResult(string ZipPath, string DestinationDirectory, int FileCount);

/// <summary>将运行日志、崩溃日志等打包为 zip 并写入指定目录。</summary>
public static class LogBundleExporter
{
    static readonly string[] LogPatterns =
    [
        "runtime*.log",
        "runtime-ui*.log",
        "crash*.log",
        "exit*.log",
        "bootstrap*.log"
    ];

    public static LogBundleResult CreateBundle(
        string destinationDirectory,
        string? deviceLabel = null,
        string? appVersion = null,
        int maxAgeDays = 14)
    {
        if (string.IsNullOrWhiteSpace(destinationDirectory))
        {
            throw new ArgumentException("输出目录不能为空。", nameof(destinationDirectory));
        }

        Directory.CreateDirectory(destinationDirectory);

        var logDirectory = AppPaths.LogDirectory;
        var dataDirectory = AppPaths.UserDataDirectory;
        Directory.CreateDirectory(logDirectory);

        var cutoff = DateTime.Now.AddDays(-Math.Clamp(maxAgeDays, 1, 365));
        var files = CollectFiles(logDirectory, dataDirectory, cutoff);
        if (files.Count == 0)
        {
            throw new InvalidOperationException($"日志目录中没有可打包的文件：{logDirectory}");
        }

        var stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
        var safeLabel = SanitizeFileName(string.IsNullOrWhiteSpace(deviceLabel) ? "monitor" : deviceLabel.Trim());
        var zipPath = Path.Combine(destinationDirectory, $"huaGuang-logs-{safeLabel}-{stamp}.zip");

        if (File.Exists(zipPath))
        {
            File.Delete(zipPath);
        }

        var manifest = BuildManifest(files, logDirectory, dataDirectory, appVersion, zipPath);
        using (var archive = ZipFile.Open(zipPath, ZipArchiveMode.Create))
        {
            var manifestEntry = archive.CreateEntry("bundle-info.txt", CompressionLevel.Optimal);
            using (var writer = new StreamWriter(manifestEntry.Open(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: true)))
            {
                writer.Write(manifest);
            }

            foreach (var (fullPath, entryName) in files)
            {
                AddFileEntry(archive, fullPath, entryName);
            }
        }

        return new LogBundleResult(zipPath, destinationDirectory, files.Count);
    }

    static List<(string FullPath, string EntryName)> CollectFiles(
        string logDirectory,
        string dataDirectory,
        DateTime cutoffLocal)
    {
        var results = new List<(string, string)>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var pattern in LogPatterns)
        {
            if (!Directory.Exists(logDirectory))
            {
                continue;
            }

            foreach (var path in Directory.EnumerateFiles(logDirectory, pattern))
            {
                if (!seen.Add(path))
                {
                    continue;
                }

                if (File.GetLastWriteTime(path) < cutoffLocal)
                {
                    continue;
                }

                results.Add((path, Path.Combine("logs", Path.GetFileName(path))));
            }
        }

        var originFile = Path.Combine(dataDirectory, WindowsSharedDataDirectory.OriginFileName);
        if (File.Exists(originFile) && seen.Add(originFile))
        {
            results.Add((originFile, WindowsSharedDataDirectory.OriginFileName));
        }

        results.Sort((a, b) => string.Compare(a.Item2, b.Item2, StringComparison.OrdinalIgnoreCase));
        return results;
    }

    static void AddFileEntry(ZipArchive archive, string sourcePath, string entryName)
    {
        try
        {
            var entry = archive.CreateEntry(entryName, CompressionLevel.Optimal);
            using var input = new FileStream(
                sourcePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete);
            using var output = entry.Open();
            input.CopyTo(output);
        }
        catch (Exception ex)
        {
            var fallback = archive.CreateEntry($"{entryName}.copy-error.txt", CompressionLevel.Optimal);
            using var writer = new StreamWriter(fallback.Open(), Encoding.UTF8);
            writer.WriteLine($"无法读取 {sourcePath}：{ex.Message}");
        }
    }

    static string BuildManifest(
        IReadOnlyList<(string FullPath, string EntryName)> files,
        string logDirectory,
        string dataDirectory,
        string? appVersion,
        string zipPath)
    {
        var builder = new StringBuilder();
        builder.AppendLine("华光工业监控 · 日志包");
        builder.AppendLine($"生成时间：{DateTimeOffset.Now:O}");
        if (!string.IsNullOrWhiteSpace(appVersion))
        {
            builder.AppendLine($"程序版本：{appVersion}");
        }

        builder.AppendLine($"数据目录：{dataDirectory}");
        builder.AppendLine($"日志目录：{logDirectory}");
        builder.AppendLine($"输出文件：{zipPath}");
        builder.AppendLine($"包含 {files.Count} 个文件：");
        foreach (var (fullPath, entryName) in files)
        {
            long length;
            DateTime writeTime;
            try
            {
                var info = new FileInfo(fullPath);
                length = info.Length;
                writeTime = info.LastWriteTime;
            }
            catch
            {
                length = -1;
                writeTime = DateTime.MinValue;
            }

            builder.AppendLine($"  · {entryName} ({length} bytes, {writeTime:yyyy-MM-dd HH:mm:ss})");
        }

        return builder.ToString();
    }

    static string SanitizeFileName(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var builder = new StringBuilder(value.Length);
        foreach (var ch in value)
        {
            builder.Append(invalid.Contains(ch) ? '_' : ch);
        }

        var sanitized = builder.ToString().Trim();
        return sanitized.Length == 0 ? "monitor" : sanitized[..Math.Min(sanitized.Length, 48)];
    }
}
