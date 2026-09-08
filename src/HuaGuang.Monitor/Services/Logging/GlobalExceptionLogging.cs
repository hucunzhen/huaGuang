using Microsoft.Extensions.Logging;

namespace HuaGuang.Monitor.Services.Logging;

public static class GlobalExceptionLogging
{
    public static void Register(IServiceProvider services)
    {
        var loggerFactory = services.GetRequiredService<ILoggerFactory>();
        CrashExitLogger.Register(loggerFactory);
    }
}
