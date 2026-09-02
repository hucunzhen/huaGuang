using HuaGuang.Monitor.Hosting;
using HuaGuang.Monitor.Ipc;
using HuaGuang.Monitor.Services;
using HuaGuang.Monitor.Services.Logging;
using HuaGuang.Monitor.ViewModels;
using HuaGuang.Monitor.Views;
using Microsoft.Extensions.Logging;

namespace HuaGuang.Monitor;

public static class MauiProgram
{
	public static IServiceProvider Services { get; private set; } = default!;
	public static bool UsesWindowsBackgroundService { get; private set; }

	public static MauiApp CreateMauiApp()
	{
#if WINDOWS
		AppPaths.Configure(new WindowsAppDataPaths());
#else
		AppPaths.Configure(new MauiAppDataPaths());
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
		UsesWindowsBackgroundService = MonitorIpcClient.IsServiceAvailable();
#endif

		RegisterMonitorRuntime(builder.Services);
#if ANDROID
		builder.Services.AddSingleton<IAcquisitionBackgroundGuard, Platforms.Android.AndroidAcquisitionBackgroundGuard>();
#endif
#if WINDOWS
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
		var startupLogger = Services.GetRequiredService<ILoggerFactory>().CreateLogger("Startup");
		startupLogger.LogInformation(
			"应用启动 version={Version} dataDir={DataDir} logFile={LogFile} windowsService={UsesService}",
			AppVersionInfo.Display,
			AppPaths.UserDataDirectory,
			AppPaths.CurrentRuntimeLogFile,
			UsesWindowsBackgroundService);
		var store = Services.GetRequiredService<SettingsStore>();
		store.LoadAsync().GetAwaiter().GetResult();
		startupLogger.LogInformation(
			"配置已加载 line={LineName} mode={Mode} deviceId={DeviceId} simulator={Simulator}",
			store.Current.LineName,
			store.Current.OperationMode,
			store.Current.DeviceId,
			store.Current.UseSimulator);
		Services.GetRequiredService<IStartupRegistration>().Apply(store.Current.StartWithWindows);
		if (!UsesWindowsBackgroundService)
		{
			Services.GetRequiredService<HistoryRecorder>().InitializeAsync().GetAwaiter().GetResult();
		}

		return app;
	}

	static void RegisterMonitorRuntime(IServiceCollection services)
	{
#if WINDOWS
		if (UsesWindowsBackgroundService)
		{
			services.AddMonitorRuntimeRemote();
			return;
		}
#endif
		services.AddMonitorRuntimeCore(AppPaths.LogDirectory);
		services.AddMonitorRuntimeLocal();
	}
}
