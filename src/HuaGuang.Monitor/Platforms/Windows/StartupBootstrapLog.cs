namespace HuaGuang.Monitor.Platforms.Windows;

static class StartupBootstrapLog
{
    public static void Write(string stage, Exception? ex = null) =>
        Services.Logging.CrashExitLogger.WriteBootstrap(stage, ex);
}
