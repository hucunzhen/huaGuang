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

    public static string SettingsFilePath => Path.Combine(UserDataDirectory, "settings.json");

    public static string HistoryDatabasePath => Path.Combine(UserDataDirectory, "history.db");

    public static string UserLinesDirectory => Path.Combine(UserDataDirectory, "lines");

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
}

public sealed class WindowsAppDataPaths : IAppDataPaths
{
    public string UserDataDirectory => ResolveSharedDirectory();

    public static string ResolveSharedDirectory()
    {
        var programDataDir = GetProgramDataDirectory();
        var legacyDir = GetLegacyUserDirectory();
        Directory.CreateDirectory(programDataDir);
        EnsureInteractiveUsersCanWrite(programDataDir);
        EnsureInteractiveUsersCanWrite(Path.Combine(programDataDir, "lines"));
        EnsureInteractiveUsersCanWrite(Path.Combine(programDataDir, "logs"));

        // 服务可能先创建 logs 目录，不能因此跳过从 LocalAppData 迁移产线 Excel。
        if (!HasLineConfig(programDataDir))
        {
            if (HasLineConfig(legacyDir) || HasUserData(legacyDir))
            {
                CopyDirectory(legacyDir, programDataDir);
            }
        }

        return programDataDir;
    }

    static string GetProgramDataDirectory() =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            AppPaths.PackageId,
            "Data");

    static string GetLegacyUserDirectory()
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var candidates = new[]
        {
            Path.Combine(localAppData, AppPaths.PackageId, "Data"),
            Path.Combine(localAppData, AppPaths.PackageId)
        };

        foreach (var candidate in candidates)
        {
            if (Directory.Exists(candidate))
            {
                return candidate;
            }
        }

        return candidates[0];
    }

    static bool HasLineConfig(string directory)
    {
        var linesDir = Path.Combine(directory, "lines");
        if (!Directory.Exists(linesDir))
        {
            return false;
        }

        return Directory.EnumerateFiles(linesDir, "*.xlsx")
            .Any(path => !Path.GetFileName(path).StartsWith("~", StringComparison.Ordinal));
    }

    static bool HasUserData(string directory) =>
        Directory.Exists(directory) &&
        (HasLineConfig(directory) ||
         File.Exists(Path.Combine(directory, "history.db")) ||
         Directory.Exists(Path.Combine(directory, "logs")));

    static void CopyDirectory(string sourceDir, string targetDir)
    {
        Directory.CreateDirectory(targetDir);
        foreach (var directory in Directory.GetDirectories(sourceDir, "*", SearchOption.AllDirectories))
        {
            Directory.CreateDirectory(directory.Replace(sourceDir, targetDir, StringComparison.OrdinalIgnoreCase));
        }

        foreach (var file in Directory.GetFiles(sourceDir, "*", SearchOption.AllDirectories))
        {
            var targetFile = file.Replace(sourceDir, targetDir, StringComparison.OrdinalIgnoreCase);
            Directory.CreateDirectory(Path.GetDirectoryName(targetFile)!);
            File.Copy(file, targetFile, overwrite: true);
        }
    }

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
