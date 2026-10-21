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
        MonitorProcessInstance.Initialize();
        AppPaths.Configure(new WindowsAppDataPaths());
        MonitorProcessInstance.RecoverIsolatedIdentityFromWindowsService();
        MonitorProcessInstance.ApplyPersistedHostProfile();
        MonitorProcessInstance.RemoveIsolatedLoginStartup();
        WindowsAppDataPaths.WarmUp();
        Directory.CreateDirectory(AppPaths.LogDirectory);
        AppPaths.ConfigureRuntimeLogging(
            MonitorProcessInstance.IsIsolated
                ? $"runtime-{MonitorProcessInstance.Id}"
                : "runtime");
        CrashExitLogger.WriteBootstrap($"dataDir={AppPaths.UserDataDirectory} logDir={AppPaths.LogDirectory} instance={MonitorProcessInstance.Id ?? "primary"}");

        var builder = Host.CreateApplicationBuilder();
        builder.Logging.ClearProviders();
        builder.Logging.AddFilter("Microsoft", LogLevel.Warning);
        builder.Logging.AddFilter("Microsoft.Hosting.Lifetime", LogLevel.Information);
        builder.Logging.AddFilter("Microsoft.Extensions.Logging.EventLog", LogLevel.Error);
        builder.Services.AddWindowsService(options =>
        {
            options.ServiceName = MonitorIpcConstants.CurrentServiceName;
        });

        builder.Services.AddSingleton<SettingsStore>();
        if (!MonitorProcessInstance.IsIsolated)
        {
            builder.Services.AddSingleton(sp => new WatchdogSupervisor(
                sp.GetRequiredService<SettingsStore>(),
                sp.GetRequiredService<ILogger<WatchdogSupervisor>>(),
                WatchdogSupervisor.ResolveInstallRoot(),
                WindowsUiWatchPolicy.ShouldWatchUi));
        }

        builder.Services.AddMonitorRuntimeCore(AppPaths.LogDirectory, addInMemoryLogger: false);
        builder.Services.AddHostedService<MonitorIpcServer>();
        if (!MonitorProcessInstance.IsIsolated)
        {
            builder.Services.AddHostedService<UiSupervisorWorker>();
        }

        builder.Services.AddHostedService<MonitorConfigWatcher>();
        builder.Services.AddHostedService<MonitorAutoStartWorker>();
        if (!MonitorProcessInstance.IsIsolated)
        {
            builder.Services.AddHostedService(sp =>
                new WatchdogRoleHeartbeatWorker(
                    WatchdogConstants.AcquisitionRole,
                    sp.GetRequiredService<ILogger<WatchdogRoleHeartbeatWorker>>()));
        }

        var host = builder.Build();
        CrashExitLogger.Register(host.Services.GetRequiredService<ILoggerFactory>());
        CrashExitLogger.SetContext(typeof(Program).Assembly.GetName().Version?.ToString(), "service");
        // 必须立刻 host.Run()：Excel/历史库若放在此之前，开机多实例会超过 SCM 30 秒超时（1053）。
        host.Run();
    }
}
