using System.Text.Json;
using HuaGuang.Monitor.Models;

namespace HuaGuang.Monitor.Services;

/// <summary>独立实例后台启动档案：即使 Windows 服务命令行丢掉 --mode，也能按订阅/采集启动。</summary>
public static class InstanceHostProfileStore
{
    public static string DirectoryPath => Path.Combine(AppPaths.SharedDataDirectory, "instance-hosts");

    public static string FilePath(string instanceId) =>
        Path.Combine(DirectoryPath, $"{instanceId}.json");

    public static void Save(
        string instanceId,
        string lineName,
        AppOperationMode operationMode,
        bool autoStart = true)
    {
        Directory.CreateDirectory(DirectoryPath);
        var json = JsonSerializer.Serialize(
            new InstanceHostProfile
            {
                InstanceId = instanceId,
                LineName = lineName,
                OperationMode = operationMode.ToString(),
                AutoStart = autoStart
            },
            new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(FilePath(instanceId), json);
    }

    /// <summary>用户在设置里明确勾选了独立实例开机（不含「打开窗口时误登记」）。</summary>
    public static bool HasExplicitAutoStartInstance()
    {
        try
        {
            if (!Directory.Exists(DirectoryPath))
            {
                return false;
            }

            foreach (var path in Directory.EnumerateFiles(DirectoryPath, "*.json"))
            {
                var id = Path.GetFileNameWithoutExtension(path);
                if (TryLoad(id, out var profile) && profile.AutoStart == true)
                {
                    return true;
                }
            }
        }
        catch
        {
            return false;
        }

        return false;
    }

    /// <summary>已有独立实例负责开机时，主服务不要再按共享目录里的默认产线自动采集。</summary>
    public static bool HasAutoStartInstance() => HasExplicitAutoStartInstance();

    public static bool TryLoad(string instanceId, out InstanceHostProfile profile)
    {
        profile = new InstanceHostProfile();
        var path = FilePath(instanceId);
        if (!File.Exists(path))
        {
            return false;
        }

        try
        {
            var loaded = JsonSerializer.Deserialize<InstanceHostProfile>(File.ReadAllText(path));
            if (loaded is null || string.IsNullOrWhiteSpace(loaded.InstanceId))
            {
                return false;
            }

            profile = loaded;
            return true;
        }
        catch
        {
            return false;
        }
    }
}

public sealed class InstanceHostProfile
{
    public string InstanceId { get; set; } = string.Empty;
    public string LineName { get; set; } = string.Empty;
    public string OperationMode { get; set; } = nameof(AppOperationMode.Acquisition);
    /// <summary>旧档案无此字段时视为 true。</summary>
    public bool? AutoStart { get; set; }
}
