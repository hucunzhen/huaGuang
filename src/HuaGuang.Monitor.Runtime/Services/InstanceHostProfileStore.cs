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
    public static bool HasExplicitAutoStartInstance() => ListSavedAutoStart().Count > 0;

    /// <summary>
    /// 仅返回用户在独立窗口里明确勾选并保存过的开机档案。
    /// 不再从产线 Excel「开机自动启动」推导——Excel 默认多为「是」，否则会把所有产线都拉起来抢 PLC。
    /// </summary>
    public static IReadOnlyList<InstanceHostProfile> ListAutoStart() => ListSavedAutoStart();

    static List<InstanceHostProfile> ListSavedAutoStart()
    {
        var list = new List<InstanceHostProfile>();
        try
        {
            if (!Directory.Exists(DirectoryPath))
            {
                return list;
            }

            foreach (var path in Directory.EnumerateFiles(DirectoryPath, "*.json"))
            {
                var id = Path.GetFileNameWithoutExtension(path);
                if (TryLoad(id, out var profile) && profile.AutoStart == true)
                {
                    list.Add(profile);
                }
            }
        }
        catch
        {
            return list;
        }

        return list;
    }

    /// <summary>已有独立实例负责该产线开机时，主服务不要再按同一产线自动采集/订阅。</summary>
    public static bool HasAutoStartInstance() => ListAutoStart().Count > 0;

    public static bool HasAutoStartFor(string? lineName, AppOperationMode operationMode)
    {
        foreach (var profile in ListAutoStart())
        {
            if (!Matches(profile, lineName, operationMode))
            {
                continue;
            }

            return true;
        }

        return false;
    }

    /// <summary>
    /// 独立实例勾了「开机自动启动本实例后台」后，Windows 服务进程起来就必须自动采集/订阅。
    /// 主实例仍看「启动后自动运行」。
    /// </summary>
    public static bool ShouldStartRuntimeOnHostBoot(AppSettings settings)
    {
        if (MonitorProcessInstance.IsIsolated)
        {
            if (settings.StartWithWindows)
            {
                return true;
            }

            var id = MonitorProcessInstance.Id;
            if (!string.IsNullOrWhiteSpace(id) && TryLoad(id, out var profile) && profile.AutoStart == true)
            {
                return true;
            }
        }

        return settings.AutoStartAcquisition;
    }

    public static bool TryCreateDiscoveredProfile(AppSettings settings, out InstanceHostProfile profile)
    {
        profile = new InstanceHostProfile();
        if (settings.StartWithWindows != true)
        {
            return false;
        }

        if (settings.OperationMode == AppOperationMode.Subscribe
            || LineCatalog.IsSubscribeDashboard(settings.LineName))
        {
            profile = new InstanceHostProfile
            {
                InstanceId = "sub",
                LineName = LineCatalog.SubscribeDashboardLineName,
                OperationMode = nameof(AppOperationMode.Subscribe),
                AutoStart = true
            };
            return true;
        }

        if (!LineCatalog.TryResolveLineName(settings.LineName, out var line))
        {
            return false;
        }

        profile = new InstanceHostProfile
        {
            InstanceId = "acq-" + LineCatalog.ToCommandAlias(line),
            LineName = line,
            OperationMode = nameof(AppOperationMode.Acquisition),
            AutoStart = true
        };
        return true;
    }

    static IEnumerable<InstanceHostProfile> DiscoverFromLineWorkbooks()
    {
        string directory;
        try
        {
            directory = AppPaths.UserLinesDirectory;
        }
        catch
        {
            yield break;
        }

        if (!Directory.Exists(directory))
        {
            yield break;
        }

        foreach (var path in Directory.EnumerateFiles(directory, "*.xlsx"))
        {
            AppSettings settings;
            try
            {
                settings = LineExcelConfigService.LoadConfig(path);
            }
            catch
            {
                continue;
            }

            if (!TryCreateDiscoveredProfile(settings, out var profile))
            {
                continue;
            }

            yield return profile;
        }
    }

    static bool Matches(InstanceHostProfile profile, string? lineName, AppOperationMode operationMode)
    {
        var profileMode = Enum.TryParse(profile.OperationMode, ignoreCase: true, out AppOperationMode parsed)
            ? parsed
            : string.Equals(profile.InstanceId, "sub", StringComparison.OrdinalIgnoreCase)
                ? AppOperationMode.Subscribe
                : AppOperationMode.Acquisition;
        if (profileMode != operationMode)
        {
            return false;
        }

        if (operationMode == AppOperationMode.Subscribe)
        {
            return true;
        }

        if (string.IsNullOrWhiteSpace(lineName) || string.IsNullOrWhiteSpace(profile.LineName))
        {
            return false;
        }

        if (!LineCatalog.TryResolveLineName(lineName, out var want)
            || !LineCatalog.TryResolveLineName(profile.LineName, out var have))
        {
            return false;
        }

        return string.Equals(want, have, StringComparison.Ordinal);
    }

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
