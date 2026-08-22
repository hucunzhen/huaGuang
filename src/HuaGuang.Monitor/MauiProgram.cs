using HuaGuang.Monitor.Messaging;
using HuaGuang.Monitor.Protocols;
using HuaGuang.Monitor.Services;
using HuaGuang.Monitor.ViewModels;
using HuaGuang.Monitor.Views;
using Microsoft.Extensions.Logging;

namespace HuaGuang.Monitor;

public static class MauiProgram
{
	public static IServiceProvider Services { get; private set; } = default!;

	public static MauiApp CreateMauiApp()
	{
		var builder = MauiApp.CreateBuilder();
		builder
			.UseMauiApp<App>()
			.ConfigureFonts(fonts =>
			{
				fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
				fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
			});

#if DEBUG
		builder.Logging.AddDebug();
#endif

		builder.Services.AddSingleton<SettingsStore>();
#if WINDOWS
		builder.Services.AddSingleton<IStartupRegistration, Platforms.Windows.WindowsStartupRegistration>();
		builder.Services.AddSingleton<IPlatformFullScreenPresenter, Platforms.Windows.WindowsFullScreenPresenter>();
#elif ANDROID
		builder.Services.AddSingleton<IStartupRegistration, NoOpStartupRegistration>();
		builder.Services.AddSingleton<IPlatformFullScreenPresenter, Platforms.Android.AndroidFullScreenPresenter>();
#else
		builder.Services.AddSingleton<IStartupRegistration, NoOpStartupRegistration>();
		builder.Services.AddSingleton<IPlatformFullScreenPresenter, NoOpFullScreenPresenter>();
#endif
		builder.Services.AddSingleton<FullScreenService>();
		builder.Services.AddSingleton(sp =>
			new HistoryStore(Path.Combine(FileSystem.AppDataDirectory, "history.db")));
		builder.Services.AddSingleton<HistoryRecorder>();
		builder.Services.AddSingleton<IPlcClient, ModbusTcpPlcClient>();
		builder.Services.AddSingleton<IMqttPublisher, MqttPublisher>();
		builder.Services.AddSingleton<AcquisitionService>();
		builder.Services.AddSingleton<SubscriptionService>();
		builder.Services.AddSingleton<DashboardViewModel>();
		builder.Services.AddTransient<DiagnosticsViewModel>();
		builder.Services.AddTransient<SettingsViewModel>();
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
		var store = Services.GetRequiredService<SettingsStore>();
		store.LoadAsync().GetAwaiter().GetResult();
		LineConfigPaths.EnsureDefaultExcelFiles();
		Services.GetRequiredService<IStartupRegistration>().Apply(store.Current.StartWithWindows);
		Services.GetRequiredService<HistoryRecorder>().InitializeAsync().GetAwaiter().GetResult();
		return app;
	}
}
