using System.Diagnostics;
using System.Text.Json;
using HuaGuang.Monitor.Models;

namespace HuaGuang.Monitor.Services;

/// <summary>采集绑定产线互斥：同一产线同一时刻只允许一个采集实例（含主窗口）。</summary>
public static class LineAcquisitionOccupancy
{
    public static string SelfInstanceId => MonitorProcessInstance.Id ?? "primary";

    public static string? LastError { get; private set; }

    static string DirectoryPath => Path.Combine(AppPaths.SharedDataDirectory, "line-occupancy");

    public static bool TryClaim(string lineName, out string error)
    {
        error = string.Empty;
        if (string.IsNullOrWhiteSpace(lineName))
        {
            error = "产线名称为空，无法占用。";
            LastError = error;
            return false;
        }

        Directory.CreateDirectory(DirectoryPath);
        var path = LockPath(lineName);
        var existing = Read(path);
        if (existing is not null && IsLive(existing) && !IsSelfInstance(existing))
        {
            error =
                $"产线「{lineName}」已由实例 {existing.InstanceId}（PID {existing.ProcessId}）占用采集，请换一条产线或关闭该窗口。";
            LastError = error;
            return false;
        }

        var record = new OccupancyRecord
        {
            InstanceId = SelfInstanceId,
            ProcessId = Environment.ProcessId,
            LineName = lineName,
            ClaimedUtc = DateTimeOffset.UtcNow
        };
        File.WriteAllText(path, JsonSerializer.Serialize(record, new JsonSerializerOptions { WriteIndented = true }));
        LastError = null;
        return true;
    }

    public static void Release(string? lineName)
    {
        if (string.IsNullOrWhiteSpace(lineName))
        {
            return;
        }

        var path = LockPath(lineName);
        var existing = Read(path);
        if (existing is null || !IsSelfInstance(existing))
        {
            return;
        }

        try
        {
            File.Delete(path);
        }
        catch
        {
        }
    }

    public static void ReleaseAllForSelf()
    {
        if (!Directory.Exists(DirectoryPath))
        {
            return;
        }

        foreach (var file in Directory.EnumerateFiles(DirectoryPath, "*.json"))
        {
            var existing = Read(file);
            if (existing is null || !IsSelfInstance(existing) || existing.ProcessId != Environment.ProcessId)
            {
                continue;
            }

            try
            {
                File.Delete(file);
            }
            catch
            {
            }
        }
    }

    public static bool TrySync(string lineName, AppOperationMode mode, out string error)
    {
        ReleaseAllForSelf();
        if (mode != AppOperationMode.Acquisition)
        {
            error = string.Empty;
            LastError = null;
            return true;
        }

        return TryClaim(lineName, out error);
    }

    static string LockPath(string lineName)
    {
        var safe = string.Concat(lineName.Select(ch => Path.GetInvalidFileNameChars().Contains(ch) ? '_' : ch));
        return Path.Combine(DirectoryPath, safe + ".json");
    }

    static OccupancyRecord? Read(string path)
    {
        try
        {
            if (!File.Exists(path))
            {
                return null;
            }

            return JsonSerializer.Deserialize<OccupancyRecord>(File.ReadAllText(path));
        }
        catch
        {
            return null;
        }
    }

    static bool IsSelfInstance(OccupancyRecord record) =>
        string.Equals(record.InstanceId, SelfInstanceId, StringComparison.OrdinalIgnoreCase);

    static bool IsLive(OccupancyRecord record)
    {
        if (record.ProcessId <= 0)
        {
            return false;
        }

        try
        {
            using var process = Process.GetProcessById(record.ProcessId);
            return !process.HasExited;
        }
        catch
        {
            return false;
        }
    }

    sealed class OccupancyRecord
    {
        public string InstanceId { get; set; } = "primary";
        public int ProcessId { get; set; }
        public string LineName { get; set; } = string.Empty;
        public DateTimeOffset ClaimedUtc { get; set; }
    }
}
