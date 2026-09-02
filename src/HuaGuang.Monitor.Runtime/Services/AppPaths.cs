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

    public static string CurrentRuntimeLogFile =>
        Path.Combine(LogDirectory, $"runtime-{DateTime.Now:yyyyMMdd}.log");
}

public sealed class WindowsAppDataPaths : IAppDataPaths
{
    public string UserDataDirectory => ResolveExistingOrDefault();

    public static string ResolveExistingOrDefault()
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var candidates = new[]
        {
            Path.Combine(localAppData, AppPaths.PackageId, "Data"),
            Path.Combine(localAppData, AppPaths.PackageId)
        };

        foreach (var candidate in candidates)
        {
            if (Directory.Exists(candidate) &&
                (File.Exists(Path.Combine(candidate, "history.db")) ||
                 Directory.Exists(Path.Combine(candidate, "lines")) ||
                 Directory.Exists(Path.Combine(candidate, "logs"))))
            {
                return candidate;
            }
        }

        return candidates[0];
    }
}
