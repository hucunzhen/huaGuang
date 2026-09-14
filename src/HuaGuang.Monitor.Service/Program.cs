using HuaGuang.Monitor.Hosting;
using HuaGuang.Monitor.Ipc;
using HuaGuang.Monitor.Services;
using HuaGuang.Monitor.Services.Logging;
using HuaGuang.Monitor.Services.Watchdog;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace HuaGuang.Monitor.Service;

public static class Program
{
    public static void Main(string[] args)
    {
        CrashExitLogger.RegisterEarly("service");
        AppPaths.Configure(new WindowsAppDataPaths());
        WindowsAppDataPaths.WarmUp();
        Directory.CreateDirectory(AppPaths.LogDirectory);
        CrashExitLogger.WriteBootstrap($"dataDir={AppPaths.UserDataDirectory} logDir={AppPaths.LogDirectory}");

        var builder = Host.CreateApplicationBuilder(args);
        builder.Services.AddWindowsService(options =>
        {
            options.ServiceName = MonitorIpcConstants.ServiceName;
        });

        builder.Services.AddSingleton<SettingsStore>();
        builder.Services.AddSingleton(sp => new WatchdogSupervisor(
            sp.GetRequiredService<SettingsStore>(),
            sp.GetRequiredService<ILogger<WatchdogSupervisor>>(),
            WatchdogSupervisor.ResolveInstallRoot(),
            WindowsUiWatchPolicy.ShouldWatchUi));
        builder.Services.AddMonitorRuntimeCore(AppPaths.LogDirectory);
        builder.Services.AddHostedService<MonitorIpcServer>();
        builder.Services.AddHostedService<UiSupervisorWorker>();
        builder.Services.AddHostedService<MonitorConfigWatcher>();
        builder.Services.AddHostedService<MonitorAutoStartWorker>();
        builder.Services.AddHostedService(sp =>
            new WatchdogRoleHeartbeatWorker(
                WatchdogConstants.AcquisitionRole,
                sp.GetRequiredService<ILogger<WatchdogRoleHeartbeatWorker>>()));

        var host = builder.Build();
        CrashExitLogger.Register(host.Services.GetRequiredService<ILoggerFactory>());
        CrashExitLogger.SetContext(typeof(Program).Assembly.GetName().Version?.ToString(), "service");
        var logger = host.Services.GetRequiredService<ILoggerFactory>().CreateLogger("ServiceStartup");
        logger.LogInformation(
            "工业监控后台服务启动 dataDir={DataDir} logFile={LogFile}",
            AppPaths.UserDataDirectory,
            AppPaths.CurrentRuntimeLogFile);

        var store = host.Services.GetRequiredService<SettingsStore>();
        store.LoadAsync().GetAwaiter().GetResult();
        host.Services.GetRequiredService<HistoryRecorder>().InitializeAsync().GetAwaiter().GetResult();
        host.Run();
    }
}
