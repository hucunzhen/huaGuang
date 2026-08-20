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
#else
		builder.Services.AddSingleton<IStartupRegistration, NoOpStartupRegistration>();
#endif
		builder.Services.AddSingleton<IPlcClient, ModbusTcpPlcClient>();
		builder.Services.AddSingleton<IMqttPublisher, MqttPublisher>();
		builder.Services.AddSingleton<AcquisitionService>();
		builder.Services.AddSingleton<SubscriptionService>();
		builder.Services.AddSingleton<DashboardViewModel>();
		builder.Services.AddTransient<SettingsViewModel>();
		builder.Services.AddTransient<TagsViewModel>();
		builder.Services.AddTransient<TagEditViewModel>();
		builder.Services.AddTransient<DashboardPage>();
		builder.Services.AddTransient<SettingsPage>();
		builder.Services.AddTransient<TagsPage>();
		builder.Services.AddTransient<TagEditPage>();

		var app = builder.Build();
		Services = app.Services;
		var store = Services.GetRequiredService<SettingsStore>();
		store.LoadAsync().GetAwaiter().GetResult();
		Services.GetRequiredService<IStartupRegistration>().Apply(store.Current.StartWithWindows);
		return app;
	}
}
