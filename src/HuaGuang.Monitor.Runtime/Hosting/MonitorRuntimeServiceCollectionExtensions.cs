using HuaGuang.Monitor.Messaging;
using HuaGuang.Monitor.Models;
using HuaGuang.Monitor.Protocols;
using HuaGuang.Monitor.Services;
using HuaGuang.Monitor.Services.Logging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace HuaGuang.Monitor.Hosting;

public static class MonitorRuntimeServiceCollectionExtensions
{
    public static IServiceCollection AddMonitorRuntimeCore(
        this IServiceCollection services,
        string logDirectory)
    {
        services.AddSingleton<IAcquisitionBackgroundGuard, NoOpAcquisitionBackgroundGuard>();
        services.AddSingleton(_ => new HistoryStore(AppPaths.HistoryDatabasePath));
        services.AddSingleton<HistoryRecorder>();
        services.AddSingleton<IPlcClient, ModbusTcpPlcClient>();
        services.AddSingleton<IMqttPublisher, MqttPublisher>();
        services.AddSingleton<MqttOutboundService>();
        services.AddSingleton<AcquisitionService>();
        services.AddSingleton<SubscriptionService>();
        services.AddSingleton<RuntimeLogStore>();
        services.AddLogging(builder =>
        {
            builder.SetMinimumLevel(LogLevel.Information);
            builder.AddInMemoryRuntimeLogger(LogLevel.Information);
            builder.AddRuntimeFileLogger(logDirectory);
        });
        return services;
    }

    public static IServiceCollection AddMonitorRuntimeLocal(this IServiceCollection services)
    {
        services.AddSingleton<IMonitorAcquisition>(sp => sp.GetRequiredService<AcquisitionService>());
        services.AddSingleton<IMonitorSubscription>(sp => sp.GetRequiredService<SubscriptionService>());
        return services;
    }

    public static IServiceCollection AddMonitorRuntimeRemote(this IServiceCollection services)
    {
        services.AddSingleton<RemoteMonitorAcquisition>();
        services.AddSingleton<RemoteMonitorSubscription>();
        services.AddSingleton<IMonitorAcquisition>(sp => sp.GetRequiredService<RemoteMonitorAcquisition>());
        services.AddSingleton<IMonitorSubscription>(sp => sp.GetRequiredService<RemoteMonitorSubscription>());
        return services;
    }

    public static IServiceCollection AddMonitorRuntimeAdaptive(this IServiceCollection services)
    {
        services.AddSingleton<RemoteMonitorAcquisition>();
        services.AddSingleton<RemoteMonitorSubscription>();
        services.AddSingleton<IMonitorAcquisition, AdaptiveMonitorAcquisition>();
        services.AddSingleton<IMonitorSubscription, AdaptiveMonitorSubscription>();
        return services;
    }
}

public sealed class MonitorConfigWatcher : BackgroundService
{
    readonly SettingsStore _settings;
    readonly AcquisitionService _acquisition;
    readonly SubscriptionService _subscription;
    readonly ILogger<MonitorConfigWatcher> _logger;
    FileSystemWatcher? _watcher;

    public MonitorConfigWatcher(
        SettingsStore settings,
        AcquisitionService acquisition,
        SubscriptionService subscription,
        ILogger<MonitorConfigWatcher> logger)
    {
        _settings = settings;
        _acquisition = acquisition;
        _subscription = subscription;
        _logger = logger;
    }

    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var directory = LineConfigPaths.LinesDirectory;
        Directory.CreateDirectory(directory);
        _watcher = new FileSystemWatcher(directory)
        {
            IncludeSubdirectories = false,
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName
        };
        _watcher.Changed += (_, _) => _ = ReloadSafeAsync();
        _watcher.Created += (_, _) => _ = ReloadSafeAsync();
        _watcher.Renamed += (_, _) => _ = ReloadSafeAsync();
        _watcher.EnableRaisingEvents = true;
        return Task.Delay(Timeout.Infinite, stoppingToken);
    }

    async Task ReloadSafeAsync()
    {
        try
        {
            await Task.Delay(300).ConfigureAwait(false);
            var wasRunning = _acquisition.IsRunning || _subscription.IsRunning;
            await _settings.LoadAsync().ConfigureAwait(false);
            if (!wasRunning)
            {
                return;
            }

            if (_settings.Current.OperationMode == AppOperationMode.Subscribe)
            {
                await _subscription.RefreshTopicsAsync().ConfigureAwait(false);
            }

            _logger.LogInformation("检测到配置文件变化，已重新加载");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "配置文件热加载失败");
        }
    }

    public override void Dispose()
    {
        _watcher?.Dispose();
        base.Dispose();
    }
}

public sealed class MonitorAutoStartWorker : BackgroundService
{
    readonly SettingsStore _settings;
    readonly AcquisitionService _acquisition;
    readonly SubscriptionService _subscription;
    readonly ILogger<MonitorAutoStartWorker> _logger;

    public MonitorAutoStartWorker(
        SettingsStore settings,
        AcquisitionService acquisition,
        SubscriptionService subscription,
        ILogger<MonitorAutoStartWorker> logger)
    {
        _settings = settings;
        _acquisition = acquisition;
        _subscription = subscription;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Delay(1500, stoppingToken).ConfigureAwait(false);
        if (!_settings.Current.AutoStartAcquisition)
        {
            return;
        }

        try
        {
            if (_settings.Current.OperationMode == AppOperationMode.Subscribe)
            {
                await _subscription.StartAsync().ConfigureAwait(false);
            }
            else
            {
                await _acquisition.StartAsync().ConfigureAwait(false);
            }

            _logger.LogInformation("后台服务已自动启动采集/订阅");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "后台服务自动启动失败");
        }
    }
}
