using System.Text.Json;

namespace HuaGuang.Monitor.Services.Watchdog;

/// <summary>共享心跳与正常退出标记，供 UI、采集服务与守护服务读写。</summary>
public static class WatchdogStateStore
{
    static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    static readonly Lock Gate = new();

    public static string StateDirectory =>
        Path.Combine(AppPaths.UserDataDirectory, "watchdog");

    static string PathFor(string role) =>
        Path.Combine(StateDirectory, $"{SanitizeRole(role)}.json");

    public static void WriteHeartbeat(string role)
    {
        lock (Gate)
        {
            Directory.CreateDirectory(StateDirectory);
            var state = ReadUnsafe(role) ?? new WatchdogRoleState { Role = role };
            state.Role = role;
            state.ProcessId = Environment.ProcessId;
            state.LastHeartbeatUtc = DateTimeOffset.UtcNow;
            state.GracefulShutdownUtc = null;
            WriteUnsafe(role, state);
        }
    }

    public static void MarkGracefulShutdown(string role)
    {
        lock (Gate)
        {
            Directory.CreateDirectory(StateDirectory);
            var state = ReadUnsafe(role) ?? new WatchdogRoleState { Role = role };
            state.Role = role;
            state.ProcessId = Environment.ProcessId;
            state.GracefulShutdownUtc = DateTimeOffset.UtcNow;
            state.LastHeartbeatUtc = DateTimeOffset.UtcNow;
            WriteUnsafe(role, state);
        }
    }

    public static WatchdogRoleState? Read(string role)
    {
        lock (Gate)
        {
            WatchdogRoleState? best = null;
            foreach (var directory in EnumerateWatchdogStateDirectories())
            {
                var state = ReadFromDirectory(directory, role);
                if (state is null)
                {
                    continue;
                }

                if (best is null || state.LastHeartbeatUtc > best.LastHeartbeatUtc)
                {
                    best = state;
                }
            }

            return best;
        }
    }

    static WatchdogRoleState? ReadUnsafe(string role) =>
        ReadFromDirectory(StateDirectory, role);

    static WatchdogRoleState? ReadFromDirectory(string directory, string role)
    {
        var path = Path.Combine(directory, $"{SanitizeRole(role)}.json");
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<WatchdogRoleState>(File.ReadAllText(path), JsonOptions);
        }
        catch
        {
            return null;
        }
    }

    static IEnumerable<string> EnumerateWatchdogStateDirectories()
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var dataRoot in EnumerateDataRoots())
        {
            var dir = Path.Combine(dataRoot, "watchdog");
            if (seen.Add(dir))
            {
                yield return dir;
            }
        }
    }

    static IEnumerable<string> EnumerateDataRoots()
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        string? YieldData(string? dataDir)
        {
            if (string.IsNullOrWhiteSpace(dataDir))
            {
                return null;
            }

            dataDir = dataDir.Trim().TrimEnd('\\', '/');
            return seen.Add(dataDir) ? dataDir : null;
        }

        if (YieldData(AppPaths.UserDataDirectory) is { } primary)
        {
            yield return primary;
        }

        var programData = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            AppPaths.PackageId,
            "Data");
        if (YieldData(programData) is { } pd)
        {
            yield return pd;
        }

        var localAppData = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            AppPaths.PackageId,
            "Data");
        if (YieldData(localAppData) is { } lad)
        {
            yield return lad;
        }
    }

    static void WriteUnsafe(string role, WatchdogRoleState state)
    {
        var path = PathFor(role);
        File.WriteAllText(path, JsonSerializer.Serialize(state, JsonOptions));
    }

    static string SanitizeRole(string role)
    {
        var trimmed = role.Trim();
        foreach (var ch in Path.GetInvalidFileNameChars())
        {
            trimmed = trimmed.Replace(ch, '_');
        }

        return string.IsNullOrWhiteSpace(trimmed) ? "unknown" : trimmed;
    }
}
