namespace HuaGuang.Monitor.Services.Watchdog;

static class WatchdogDiagLog
{
    public static void Write(string message)
    {
        try
        {
            Directory.CreateDirectory(AppPaths.LogDirectory);
            var path = Path.Combine(AppPaths.LogDirectory, "watchdog-supervisor.log");
            File.AppendAllText(path, $"[{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss zzz}] {message}{Environment.NewLine}");
        }
        catch
        {
        }
    }
}
