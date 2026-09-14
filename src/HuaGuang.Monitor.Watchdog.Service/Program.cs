using HuaGuang.Monitor.Services;
using HuaGuang.Monitor.Services.Logging;
using HuaGuang.Monitor.Services.Watchdog;
using HuaGuang.Monitor.Watchdog.Service;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace HuaGuang.Monitor.Watchdog.Service;

public static class Program
{
    public static void Main(string[] args)
    {
        CrashExitLogger.RegisterEarly("watchdog");
        AppPaths.Configure(new WindowsAppDataPaths());
        WindowsAppDataPaths.WarmUp();
        Directory.CreateDirectory(AppPaths.LogDirectory);
        CrashExitLogger.WriteBootstrap($"dataDir={AppPaths.UserDataDirectory} logDir={AppPaths.LogDirectory}");

        var installRoot = WatchdogSupervisor.ResolveInstallRoot();
        var builder = Host.CreateApplicationBuilder(args);
        builder.Services.AddWindowsService(options =>
        {
            options.ServiceName = WatchdogConstants.WatchdogWindowsServiceName;
        });

        builder.Services.AddSingleton<SettingsStore>();
        builder.Services.AddSingleton(_ => new WatchdogSupervisor(
            _.GetRequiredService<SettingsStore>(),
            _.GetRequiredService<ILogger<WatchdogSupervisor>>(),
            installRoot,
            WindowsUiWatchPolicy.ShouldWatchUi));
        builder.Services.AddHostedService<WatchdogSupervisorWorker>();
        builder.Services.AddHostedService(sp =>
            new WatchdogRoleHeartbeatWorker(
                WatchdogConstants.WatchdogRole,
                sp.GetRequiredService<ILogger<WatchdogRoleHeartbeatWorker>>()));

        var host = builder.Build();
        CrashExitLogger.Register(host.Services.GetRequiredService<ILoggerFactory>());
        CrashExitLogger.SetContext(typeof(Program).Assembly.GetName().Version?.ToString(), "watchdog");
        host.Services.GetRequiredService<SettingsStore>().LoadAsync().GetAwaiter().GetResult();
        host.Run();
    }
}
