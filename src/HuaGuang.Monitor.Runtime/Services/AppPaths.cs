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
        Directory.CreateDirectory(programDataDir);
        EnsureInteractiveUsersCanWrite(programDataDir);
        EnsureInteractiveUsersCanWrite(Path.Combine(programDataDir, "lines"));
        EnsureInteractiveUsersCanWrite(Path.Combine(programDataDir, "logs"));

        return programDataDir;
    }

    static string GetProgramDataDirectory() =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            AppPaths.PackageId,
            "Data");

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
