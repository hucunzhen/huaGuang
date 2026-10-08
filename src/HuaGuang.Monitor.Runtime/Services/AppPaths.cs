using System.Security.AccessControl;
using System.Security.Principal;

namespace HuaGuang.Monitor.Services;

public interface IAppDataPaths
{
    string UserDataDirectory { get; }
}

public static class AppPaths
{
    public const string PackageId = "com.industrial.monitor";

    static IAppDataPaths? _paths;

    public static void Configure(IAppDataPaths paths) => _paths = paths;

    public static string UserDataDirectory =>
        _paths?.UserDataDirectory
        ?? throw new InvalidOperationException("AppPaths.Configure must be called at startup.");

    /// <summary>共享数据根（产线 Excel）；多开实例的历史/日志在 <see cref="UserDataDirectory"/>。</summary>
    public static string SharedDataDirectory =>
        _paths is WindowsAppDataPaths
            ? WindowsSharedDataDirectory.Resolve()
            : UserDataDirectory;

    public static string HistoryDatabasePath => Path.Combine(UserDataDirectory, "history.db");

    /// <summary>按日拆分的 SQLite 目录（<c>history/yyyy-MM-dd.db</c>）。旧版单文件仍为 <see cref="HistoryDatabasePath"/>。</summary>
    public static string HistoryDirectory => Path.Combine(UserDataDirectory, "history");

    public static bool IsUsableHistoryDirectory(string? configured)
    {
        if (string.IsNullOrWhiteSpace(configured))
        {
            return false;
        }

        var trimmed = configured.Trim();
        return !trimmed.StartsWith("content:", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>解析历史保存位置。留空或不可用路径（如 Android 文档树 URI）时使用默认目录。</summary>
    public static (string ShardDirectory, string LegacyDatabasePath) ResolveHistoryLocation(string? configured)
    {
        if (!IsUsableHistoryDirectory(configured))
        {
            return (HistoryDirectory, HistoryDatabasePath);
        }

        var root = Path.GetFullPath(configured!.Trim());
        return (root, Path.Combine(root, "history.db"));
    }

    public static string UserLinesDirectory => Path.Combine(SharedDataDirectory, "lines");

    public static string LogDirectory => Path.Combine(UserDataDirectory, "logs");

    /// <summary>日志文件名前缀：后台服务 <c>runtime</c>，UI 进程 <c>runtime-ui</c>。</summary>
    public static string RuntimeLogPrefix { get; private set; } = "runtime";

    public static void ConfigureRuntimeLogging(string logPrefix)
    {
        if (string.IsNullOrWhiteSpace(logPrefix))
        {
            throw new ArgumentException("Log prefix is required.", nameof(logPrefix));
        }

        RuntimeLogPrefix = logPrefix.Trim();
    }

    public static string CurrentRuntimeLogFile =>
        Path.Combine(LogDirectory, $"{RuntimeLogPrefix}-{DateTime.Now:yyyyMMdd}.log");

    public static string DefaultLogExportDirectory =>
        Path.Combine(UserDataDirectory, "log-export");

    public static string ResolveLogExportDirectory(string? configured) =>
        string.IsNullOrWhiteSpace(configured)
            ? DefaultLogExportDirectory
            : configured.Trim();
}

public sealed class WindowsAppDataPaths : IAppDataPaths
{
    public string UserDataDirectory
    {
        get
        {
            var shared = WindowsSharedDataDirectory.Resolve();
            var directory = MonitorProcessInstance.ResolveUserDataDirectory(shared);
            if (!string.Equals(directory, shared, StringComparison.OrdinalIgnoreCase))
            {
                Directory.CreateDirectory(directory);
                Directory.CreateDirectory(Path.Combine(directory, "logs"));
                EnsureInteractiveUsersCanWrite(directory);
            }

            return directory;
        }
    }

    /// <summary>启动时调用：创建 Data/logs 并写入 path-origin 标记。</summary>
    public static void WarmUp() => WindowsSharedDataDirectory.WarmUp();

    public static string ResolveSharedDirectory() => WindowsSharedDataDirectory.Resolve();

    internal static void EnsureInteractiveUsersCanWrite(string directory)
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        try
        {
            Directory.CreateDirectory(directory);
            var dirInfo = new DirectoryInfo(directory);
            var security = dirInfo.GetAccessControl();
            var users = new SecurityIdentifier(WellKnownSidType.BuiltinUsersSid, null);
            security.AddAccessRule(new FileSystemAccessRule(
                users,
                FileSystemRights.Modify | FileSystemRights.Read | FileSystemRights.Write,
                InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
                PropagationFlags.None,
                AccessControlType.Allow));
            dirInfo.SetAccessControl(security);
        }
        catch
        {
        }
    }
}

/// <summary>Windows 共享数据目录：优先 ProgramData，不可用时回退到 LocalAppData / Temp。</summary>
public static class WindowsSharedDataDirectory
{
    static readonly Lock Gate = new();
    static string? _resolvedDataDirectory;

    public const string OriginFileName = "path-origin.txt";

    public static void WarmUp()
    {
        var dataDir = Resolve();
        Directory.CreateDirectory(Path.Combine(dataDir, "logs"));
    }

    public static string Resolve()
    {
        if (_resolvedDataDirectory is not null)
        {
            return _resolvedDataDirectory;
        }

        lock (Gate)
        {
            if (_resolvedDataDirectory is not null)
            {
                return _resolvedDataDirectory;
            }

            foreach (var (dataDir, origin) in EnumerateCandidates())
            {
                if (!TryPrepareTree(dataDir, origin))
                {
                    continue;
                }

                _resolvedDataDirectory = dataDir;
                return dataDir;
            }

            var emergency = Path.Combine(Path.GetTempPath(), AppPaths.PackageId, "Data");
            TryPrepareTree(emergency, "temp");
            _resolvedDataDirectory = emergency;
            return emergency;
        }
    }

    public static string ResolveLogDirectory() => Path.Combine(Resolve(), "logs");

    static IEnumerable<(string DataDirectory, string Origin)> EnumerateCandidates()
    {
        foreach (var root in EnumerateProgramDataRoots())
        {
            yield return (Path.Combine(root, AppPaths.PackageId, "Data"), "programdata");
        }

        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (!string.IsNullOrWhiteSpace(localAppData))
        {
            yield return (Path.Combine(localAppData.Trim(), AppPaths.PackageId, "Data"), "localappdata");
        }
    }

    static IEnumerable<string> EnumerateProgramDataRoots()
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var roots = new List<string>();
        void TryAdd(string? root)
        {
            if (string.IsNullOrWhiteSpace(root))
            {
                return;
            }

            root = root.Trim().TrimEnd('\\', '/');
            if (root.Length == 0 || !seen.Add(root))
            {
                return;
            }

            roots.Add(root);
        }

        TryAdd(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData));
        TryAdd(Environment.GetEnvironmentVariable("PROGRAMDATA"));
        TryAdd(Environment.GetEnvironmentVariable("ALLUSERSPROFILE"));
        if (OperatingSystem.IsWindows())
        {
            TryAdd(@"C:\ProgramData");
        }

        return roots;
    }

    static bool TryPrepareTree(string dataDirectory, string origin)
    {
        try
        {
            var logsDirectory = Path.Combine(dataDirectory, "logs");
            var linesDirectory = Path.Combine(dataDirectory, "lines");
            Directory.CreateDirectory(logsDirectory);
            Directory.CreateDirectory(linesDirectory);
            WindowsAppDataPaths.EnsureInteractiveUsersCanWrite(dataDirectory);
            WindowsAppDataPaths.EnsureInteractiveUsersCanWrite(linesDirectory);
            WindowsAppDataPaths.EnsureInteractiveUsersCanWrite(logsDirectory);

            var probe = Path.Combine(logsDirectory, $".write-probe-{Guid.NewGuid():N}");
            File.WriteAllText(probe, "ok");
            File.Delete(probe);

            File.WriteAllText(
                Path.Combine(dataDirectory, OriginFileName),
                $"{origin}{Environment.NewLine}{dataDirectory}{Environment.NewLine}{DateTimeOffset.Now:O}");

            return true;
        }
        catch
        {
            return false;
        }
    }
}
