using HuaGuang.Monitor.Hosting;
using HuaGuang.Monitor.Ipc;
using HuaGuang.Monitor.Services;
using HuaGuang.Monitor.Services.Logging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace HuaGuang.Monitor.Service;

public static class Program
{
    public static void Main(string[] args)
    {
        AppPaths.Configure(new WindowsAppDataPaths());
        Directory.CreateDirectory(AppPaths.LogDirectory);

        var builder = Host.CreateApplicationBuilder(args);
        builder.Services.AddWindowsService(options =>
        {
            options.ServiceName = MonitorIpcConstants.ServiceName;
        });

        builder.Services.AddSingleton<SettingsStore>();
        builder.Services.AddMonitorRuntimeCore(AppPaths.LogDirectory);
        builder.Services.AddHostedService<MonitorIpcServer>();
        builder.Services.AddHostedService<MonitorConfigWatcher>();
        builder.Services.AddHostedService<MonitorAutoStartWorker>();

        var host = builder.Build();
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
