namespace HuaGuang.Monitor.Services;

public static class AppPaths
{
    public const string PackageId = "com.industrial.monitor";

    public static string UserDataDirectory => FileSystem.AppDataDirectory;

    public static string SettingsFilePath => Path.Combine(UserDataDirectory, "settings.json");

    public static string HistoryDatabasePath => Path.Combine(UserDataDirectory, "history.db");

    public static string UserLinesDirectory => Path.Combine(UserDataDirectory, "lines");
}
