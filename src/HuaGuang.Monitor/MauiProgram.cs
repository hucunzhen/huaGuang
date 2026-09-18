using HuaGuang.Monitor.Hosting;
using HuaGuang.Monitor.Ipc;
using HuaGuang.Monitor.Services;
using HuaGuang.Monitor.Services.Logging;
using HuaGuang.Monitor.Services.Watchdog;
using HuaGuang.Monitor.ViewModels;
using HuaGuang.Monitor.Views;
using Microsoft.Extensions.Logging;

namespace HuaGuang.Monitor;

public static class MauiProgram
{
	public static IServiceProvider Services { get; private set; } = default!;
	public static bool UsesWindowsBackgroundService { get; private set; }

#if WINDOWS
	static volatile int _windowsServiceProbeCompleted;
	static Task? _windowsServiceProbeTask;
#endif

	public static bool IsWindowsBackgroundServiceAvailable() =>
#if WINDOWS
		Volatile.Read(ref _windowsServiceProbeCompleted) == 1 && UsesWindowsBackgroundService;
#else
		false;
#endif

#if WINDOWS
	public static Task EnsureWindowsServiceProbeAsync()
	{
		if (Volatile.Read(ref _windowsServiceProbeCompleted) == 1)
		{
			return Task.CompletedTask;
		}

		_windowsServiceProbeTask ??= Task.Run(() =>
		{
			try
			{
				UsesWindowsBackgroundService = MonitorIpcClient.IsServiceAvailable();
			}
			catch
			{
				UsesWindowsBackgroundService = false;
			}
			finally
			{
				Volatile.Write(ref _windowsServiceProbeCompleted, 1);
			}
		});

		return _windowsServiceProbeTask;
	}
#endif

	public static MauiApp CreateMauiApp()
	{
		CrashExitLogger.RegisterEarly("ui");
#if WINDOWS
		AppPaths.Configure(new WindowsAppDataPaths());
		AppPaths.ConfigureRuntimeLogging("runtime-ui");
		WindowsAppDataPaths.WarmUp();
		Directory.CreateDirectory(AppPaths.LogDirectory);
		Platforms.Windows.WindowsInstallerExitHelper.ClearFlagIfPresent();
		CrashExitLogger.WriteBootstrap($"dataDir={AppPaths.UserDataDirectory} logDir={AppPaths.LogDirectory}");
		WatchdogHeartbeat.Start(WatchdogConstants.UiRole);
#else
		AppPaths.Configure(new MauiAppDataPaths());
#endif

#if ANDROID
		ScanMonotonicClock.ConfigureFactory(static () => new Platforms.Android.AndroidScanMonotonicClock());
		BundledLineFileProviderRegistry.Configure(new MauiBundledLineFileProvider());
#endif

		var builder = MauiApp.CreateBuilder();
		builder
			.UseMauiApp<App>()
			.ConfigureFonts(fonts =>
			{
				fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
				fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
			});

#if DEBUG
		builder.Logging.SetMinimumLevel(LogLevel.Debug);
		builder.Logging.AddDebug();
		builder.Logging.AddInMemoryRuntimeLogger(LogLevel.Debug);
#else
		builder.Logging.SetMinimumLevel(LogLevel.Information);
		builder.Logging.AddInMemoryRuntimeLogger(LogLevel.Information);
#endif
		builder.Logging.AddRuntimeFileLogger(AppPaths.LogDirectory);

		builder.Services.AddSingleton<SettingsStore>();
#if WINDOWS
		// 不在 WinUI 初始化阶段阻塞；服务探测延后到首屏加载后。
#endif

		RegisterMonitorRuntime(builder.Services);
#if ANDROID
		builder.Services.AddSingleton<IAcquisitionBackgroundGuard, Platforms.Android.AndroidAcquisitionBackgroundGuard>();
		builder.Services.AddSingleton<Services.LogExport.ILogExportLocationService, Platforms.Android.AndroidLogExportLocationService>();
#endif
#if WINDOWS
		builder.Services.AddSingleton<Services.LogExport.ILogExportLocationService, Platforms.Windows.WindowsLogExportLocationService>();
		builder.Services.AddSingleton<IStartupRegistration, Platforms.Windows.WindowsStartupRegistration>();
		builder.Services.AddSingleton<IPlatformFullScreenPresenter, Platforms.Windows.WindowsFullScreenPresenter>();
		builder.Services.AddSingleton<IScannerInputMethodGuard, Platforms.Windows.WindowsScannerInputMethodGuard>();
#elif ANDROID
		builder.Services.AddSingleton<IStartupRegistration, NoOpStartupRegistration>();
		builder.Services.AddSingleton<IPlatformFullScreenPresenter, Platforms.Android.AndroidFullScreenPresenter>();
		builder.Services.AddSingleton<IScannerInputMethodGuard, Platforms.Android.AndroidScannerInputMethodGuard>();
#else
		builder.Services.AddSingleton<IStartupRegistration, NoOpStartupRegistration>();
		builder.Services.AddSingleton<IPlatformFullScreenPresenter, NoOpFullScreenPresenter>();
		builder.Services.AddSingleton<IScannerInputMethodGuard, NoOpScannerInputMethodGuard>();
#endif
		builder.Services.AddSingleton<FullScreenService>();
		builder.Services.AddSingleton(_ => new HistoryStore(AppPaths.HistoryDatabasePath));
		builder.Services.AddSingleton<DashboardViewModel>();
		builder.Services.AddTransient<DiagnosticsViewModel>();
		builder.Services.AddSingleton<SettingsViewModel>();
		builder.Services.AddTransient<TagsViewModel>();
		builder.Services.AddTransient<TagEditViewModel>();
		builder.Services.AddTransient<HistoryViewModel>();
		builder.Services.AddTransient<HistoryDetailViewModel>();
		builder.Services.AddTransient<DashboardPage>();
		builder.Services.AddTransient<DiagnosticsPage>();
		builder.Services.AddTransient<SettingsPage>();
		builder.Services.AddTransient<TagsPage>();
		builder.Services.AddTransient<HistoryPage>();
		builder.Services.AddTransient<HistoryDetailPage>();
		builder.Services.AddTransient<TagEditPage>();

		var app = builder.Build();
		Services = app.Services;
		GlobalExceptionLogging.Register(Services);
		CrashExitLogger.SetContext(AppVersionInfo.Display, "ui");
#if WINDOWS
		Platforms.Windows.StartupBootstrapLog.Write("CreateMauiApp: before settings load");
		_ = EnsureWindowsServiceProbeAsync();
#endif
		var startupLogger = Services.GetRequiredService<ILoggerFactory>().CreateLogger("Startup");
		startupLogger.LogInformation(
			"应用启动 version={Version} dataDir={DataDir} logFile={LogFile} windowsService={UsesService}",
			AppVersionInfo.Display,
			AppPaths.UserDataDirectory,
			AppPaths.CurrentRuntimeLogFile,
			UsesWindowsBackgroundService);
		var store = Services.GetRequiredService<SettingsStore>();
		if (!store.TryLoad())
		{
			startupLogger.LogCritical(
				"产线 Excel 加载失败，界面以降级模式启动 error={Error} logDir={LogDir}",
				store.LastLoadError,
				AppPaths.LogDirectory);
#if WINDOWS
			Platforms.Windows.StartupBootstrapLog.Write(
				$"CreateMauiApp: settings load FAILED: {store.LastLoadError}");
#endif
		}
		else
		{
#if WINDOWS
			Platforms.Windows.StartupBootstrapLog.Write("CreateMauiApp: settings loaded");
#endif
			startupLogger.LogInformation(
				"配置已加载 line={LineName} mode={Mode} deviceId={DeviceId} simulator={Simulator}",
				store.Current.LineName,
				store.Current.OperationMode,
				store.Current.DeviceId,
				store.Current.UseSimulator);
		}
		Services.GetRequiredService<IStartupRegistration>().Apply(store.Current.StartWithWindows);
#if WINDOWS
		_ = Task.Run(async () =>
		{
			try
			{
				await Services.GetRequiredService<HistoryRecorder>().InitializeAsync().ConfigureAwait(false);
				Platforms.Windows.StartupBootstrapLog.Write("CreateMauiApp: history store ready (deferred)");
			}
			catch (Exception ex)
			{
				Platforms.Windows.StartupBootstrapLog.Write("CreateMauiApp: history init failed (deferred)", ex);
			}
		});
		Platforms.Windows.StartupBootstrapLog.Write("CreateMauiApp: complete");
#else
		Services.GetRequiredService<HistoryRecorder>().InitializeAsync().GetAwaiter().GetResult();
#endif

		return app;
	}

	static void RegisterMonitorRuntime(IServiceCollection services)
	{
#if WINDOWS
		services.AddMonitorRuntimeCore(AppPaths.LogDirectory, addFileLogger: false);
		services.AddSingleton<IBackgroundRuntimeLauncher, Platforms.Windows.WindowsBackgroundRuntimeLauncher>();
		services.AddMonitorRuntimeAdaptive();
		return;
#endif
		services.AddMonitorRuntimeCore(AppPaths.LogDirectory, addFileLogger: false);
		services.AddSingleton<IBackgroundRuntimeLauncher, NoOpBackgroundRuntimeLauncher>();
		services.AddMonitorRuntimeLocal();
	}
}
