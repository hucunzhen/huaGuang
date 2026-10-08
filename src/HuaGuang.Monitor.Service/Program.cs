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
        WindowsAppDataPaths.WarmUp();
        Directory.CreateDirectory(AppPaths.LogDirectory);
        AppPaths.ConfigureRuntimeLogging(
            MonitorProcessInstance.IsIsolated
                ? $"runtime-{MonitorProcessInstance.Id}"
                : "runtime");
        CrashExitLogger.WriteBootstrap($"dataDir={AppPaths.UserDataDirectory} logDir={AppPaths.LogDirectory} instance={MonitorProcessInstance.Id ?? "primary"}");

        var builder = Host.CreateApplicationBuilder();
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

        builder.Services.AddMonitorRuntimeCore(AppPaths.LogDirectory);
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
        var logger = host.Services.GetRequiredService<ILoggerFactory>().CreateLogger("ServiceStartup");
        logger.LogInformation(
            "工业监控后台服务启动 dataDir={DataDir} logFile={LogFile} instance={Instance} line={Line} mode={Mode}",
            AppPaths.UserDataDirectory,
            AppPaths.CurrentRuntimeLogFile,
            MonitorProcessInstance.Id ?? "primary",
            MonitorProcessInstance.StartupLineName ?? "(unset)",
            MonitorProcessInstance.StartupMode?.ToString() ?? "(unset)");

        var store = host.Services.GetRequiredService<SettingsStore>();
        if (!store.TryLoad())
        {
            logger.LogCritical(
                "产线 Excel 加载失败，后台服务仍将启动以便在界面中修复 error={Error}",
                store.LastLoadError);
        }

        try
        {
            var (historyShard, historyLegacy) = AppPaths.ResolveHistoryLocation(store.Current.HistoryDirectory);
            host.Services.GetRequiredService<HistoryStore>().Relocate(historyShard, historyLegacy);
            logger.LogInformation("历史保存目录 {Shard}", historyShard);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "应用历史保存目录失败，将使用默认目录");
        }

        var history = host.Services.GetRequiredService<HistoryRecorder>();
        _ = Task.Run(async () =>
        {
            try
            {
                await history.InitializeAsync().ConfigureAwait(false);
                logger.LogInformation("历史库已在后台就绪");
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "历史库后台初始化失败（采集/订阅仍继续）");
            }
        });

        host.Run();
    }
}
