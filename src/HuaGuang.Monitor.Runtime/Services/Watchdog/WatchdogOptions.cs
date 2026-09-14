using System.Text.Json;

namespace HuaGuang.Monitor.Services.Watchdog;

public sealed class WatchdogOptions
{
    public bool Enabled { get; set; } = true;
    public int PollIntervalSeconds { get; set; } = 20;
    public bool ProtectUi { get; set; } = true;
    public bool ProtectAcquisitionService { get; set; } = true;
    public bool EnsureAcquisitionRunning { get; set; } = true;
    public int RestartCooldownSeconds { get; set; } = 60;
    public int GracefulShutdownWindowSeconds { get; set; } = 180;
    public int CrashLogLookbackMinutes { get; set; } = 30;

    static string ConfigPath => Path.Combine(AppPaths.UserDataDirectory, "watchdog-config.json");

    public static WatchdogOptions Load()
    {
        try
        {
            if (!File.Exists(ConfigPath))
            {
                return new WatchdogOptions();
            }

            var json = File.ReadAllText(ConfigPath);
            return JsonSerializer.Deserialize<WatchdogOptions>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            }) ?? new WatchdogOptions();
        }
        catch
        {
            return new WatchdogOptions();
        }
    }
}
