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
        string logDirectory,
        bool addFileLogger = true,
        bool addInMemoryLogger = true)
    {
        SubscribeLineTagLoader.Register();
        services.AddSingleton<IAcquisitionBackgroundGuard, NoOpAcquisitionBackgroundGuard>();
        services.AddSingleton(_ => new HistoryStore(AppPaths.HistoryDatabasePath));
        services.AddSingleton<HistoryRecorder>();
        services.AddSingleton<ModbusTcpPlcClient>();
        services.AddSingleton<S7PlcClient>();
        services.AddSingleton<IPlcClient, PlcClientRouter>();
        services.AddSingleton<PlcClientHub>();
        services.AddSingleton<IMqttPublisher, MqttPublisher>();
        services.AddSingleton<MqttOutboundService>();
        services.AddSingleton<AcquisitionService>();
        services.AddSingleton<SubscriptionService>();
        services.AddSingleton<RuntimeLogStore>();
        services.AddLogging(builder =>
        {
            builder.SetMinimumLevel(LogLevel.Information);
            if (addInMemoryLogger)
            {
                builder.AddInMemoryRuntimeLogger(LogLevel.Information);
            }

            if (addFileLogger)
            {
                builder.AddRuntimeFileLogger(logDirectory);
            }
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
    readonly HistoryRecorder _history;
    readonly ILogger<MonitorConfigWatcher> _logger;
    readonly object _debounceGate = new();
    FileSystemWatcher? _watcher;
    CancellationTokenSource? _debounceCts;
    int _reloading;

    public MonitorConfigWatcher(
        SettingsStore settings,
        AcquisitionService acquisition,
        SubscriptionService subscription,
        HistoryRecorder history,
        ILogger<MonitorConfigWatcher> logger)
    {
        _settings = settings;
        _acquisition = acquisition;
        _subscription = subscription;
        _history = history;
        _logger = logger;
    }

    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var directory = LineConfigPaths.LinesDirectory;
        Directory.CreateDirectory(directory);
        _watcher = new FileSystemWatcher(directory)
        {
            Filter = "*.xlsx",
            IncludeSubdirectories = false,
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.Size
        };
        _watcher.Changed += (_, _) => ScheduleReload(stoppingToken);
        _watcher.Created += (_, _) => ScheduleReload(stoppingToken);
        _watcher.Renamed += (_, _) => ScheduleReload(stoppingToken);
        _watcher.EnableRaisingEvents = true;
        return Task.Delay(Timeout.Infinite, stoppingToken);
    }

    void ScheduleReload(CancellationToken stoppingToken)
    {
        CancellationToken token;
        lock (_debounceGate)
        {
            _debounceCts?.Cancel();
            _debounceCts?.Dispose();
            _debounceCts = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
            token = _debounceCts.Token;
        }

        _ = ReloadSafeAsync(token);
    }

    async Task ReloadSafeAsync(CancellationToken debounceToken)
    {
        try
        {
            await Task.Delay(800, debounceToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        if (Interlocked.CompareExchange(ref _reloading, 1, 0) != 0)
        {
            return;
        }

        try
        {
            var changed = await _settings.LoadAsyncIfChanged().ConfigureAwait(false);
            if (!string.IsNullOrWhiteSpace(_settings.LastLoadError))
            {
                _logger.LogWarning("配置文件热加载失败 error={Error}", _settings.LastLoadError);
                return;
            }

            if (!changed)
            {
                return;
            }

            var wasRunning = _acquisition.IsRunning || _subscription.IsRunning;
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
        finally
        {
            Interlocked.Exchange(ref _reloading, 0);
        }
    }

    public override void Dispose()
    {
        lock (_debounceGate)
        {
            _debounceCts?.Cancel();
            _debounceCts?.Dispose();
            _debounceCts = null;
        }

        _watcher?.Dispose();
        base.Dispose();
    }
}

public sealed class MonitorAutoStartWorker : BackgroundService
{
    readonly SettingsStore _settings;
    readonly AcquisitionService _acquisition;
    readonly SubscriptionService _subscription;
    readonly HistoryStore _historyStore;
    readonly HistoryRecorder _history;
    readonly ILogger<MonitorAutoStartWorker> _logger;

    public MonitorAutoStartWorker(
        SettingsStore settings,
        AcquisitionService acquisition,
        SubscriptionService subscription,
        HistoryStore historyStore,
        HistoryRecorder history,
        ILogger<MonitorAutoStartWorker> logger)
    {
        _settings = settings;
        _acquisition = acquisition;
        _subscription = subscription;
        _historyStore = historyStore;
        _history = history;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Yield();
        _logger.LogInformation(
            "工业监控后台服务启动 dataDir={DataDir} logFile={LogFile} instance={Instance} line={Line} mode={Mode}",
            AppPaths.UserDataDirectory,
            AppPaths.CurrentRuntimeLogFile,
            MonitorProcessInstance.Id ?? "primary",
            MonitorProcessInstance.StartupLineName ?? "(unset)",
            MonitorProcessInstance.StartupMode?.ToString() ?? "(unset)");

        if (!_settings.TryLoad())
        {
            _logger.LogCritical(
                "产线 Excel 加载失败，后台服务仍将启动以便在界面中修复 error={Error}",
                _settings.LastLoadError);
        }

        try
        {
            var (historyShard, historyLegacy) = AppPaths.ResolveHistoryLocation(_settings.Current.HistoryDirectory);
            _historyStore.Relocate(historyShard, historyLegacy);
            _logger.LogInformation("历史保存目录 {Shard}", historyShard);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "应用历史保存目录失败，将使用默认目录");
        }

        try
        {
            await _history.InitializeAsync().ConfigureAwait(false);
            _logger.LogInformation("历史库已在后台就绪");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "历史库后台初始化失败（采集/订阅仍继续）");
        }

        await Task.Delay(800, stoppingToken).ConfigureAwait(false);

        var stagger = MonitorProcessInstance.ResolveBootStagger(MonitorProcessInstance.Id);
        if (stagger > TimeSpan.Zero)
        {
            _logger.LogInformation(
                "开机错峰 {DelayMs:0}ms 后再连 PLC instance={Instance}",
                stagger.TotalMilliseconds,
                MonitorProcessInstance.Id ?? "primary");
            try
            {
                await Task.Delay(stagger, stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }

        var isolatedHostAutoStart = InstanceHostProfileStore.ShouldStartRuntimeOnHostBoot(_settings.Current);
        if (!isolatedHostAutoStart && MonitorRuntimeOperatorControl.IsPausedByOperator)
        {
            _logger.LogInformation("操作员已停止采集/订阅（持久化），跳过服务自动启动");
            return;
        }

        if (!isolatedHostAutoStart)
        {
            _logger.LogInformation(
                "未勾选自动运行，跳过服务自动启动 isolated={Isolated} startWithWindows={StartWithWindows} autoRun={AutoRun}",
                MonitorProcessInstance.IsIsolated,
                _settings.Current.StartWithWindows,
                _settings.Current.AutoStartAcquisition);
            return;
        }

        if (!MonitorProcessInstance.IsIsolated
            && InstanceHostProfileStore.HasAutoStartFor(_settings.Current.LineName, _settings.Current.OperationMode))
        {
            _logger.LogInformation(
                "独立实例已负责 {Line} 开机，跳过主服务对该产线自动采集/订阅",
                _settings.Current.LineName);
            return;
        }

        if (!LineConfigPaths.HasConfirmedActiveLine())
        {
            _logger.LogInformation("尚未选择产线，跳过服务自动启动");
            return;
        }

        try
        {
            _logger.LogInformation(
                "准备自动启动采集/订阅 line={Line} mode={Mode} isolated={Isolated}",
                _settings.Current.LineName,
                _settings.Current.OperationMode,
                MonitorProcessInstance.IsIsolated);
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
