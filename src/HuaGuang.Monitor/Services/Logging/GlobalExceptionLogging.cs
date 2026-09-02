using Microsoft.Extensions.Logging;

namespace HuaGuang.Monitor.Services.Logging;

public static class GlobalExceptionLogging
{
    public static void Register(IServiceProvider services)
    {
        var logger = services.GetRequiredService<ILoggerFactory>().CreateLogger("Global");

        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            var exception = args.ExceptionObject as Exception
                ?? new Exception(args.ExceptionObject?.ToString() ?? "unknown");
            logger.LogCritical(
                exception,
                "AppDomain 未处理异常 IsTerminating={IsTerminating}",
                args.IsTerminating);
        };

        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            logger.LogError(args.Exception, "未观察到的 Task 异常");
            args.SetObserved();
        };
    }
}
