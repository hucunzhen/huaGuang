using HuaGuang.Monitor.Services;
using HuaGuang.Monitor.Services.Logging;
using HuaGuang.Monitor.Services.Watchdog;
using Microsoft.Extensions.DependencyInjection;

namespace HuaGuang.Monitor;

public partial class App : Application
{
	public App()
	{
		CrashExitLogger.RegisterEarly("ui");
		InitializeComponent();
		UserAppTheme = AppTheme.Dark;
	}

	protected override Window CreateWindow(IActivationState? activationState)
	{
		var window = new Window(new AppShell());
#if WINDOWS
		Platforms.Windows.WindowsAppIcon.Apply(window);
		Platforms.Windows.WindowsWindowCloseGuard.Apply(window);
		window.Destroying += (_, _) =>
		{
			CrashExitLogger.Record("Window.Destroying", null, fatal: false, "UI window closing");
			Platforms.Windows.WindowsRuntimeHandoff.TryHandoffAcquisitionToService();
		};
		AppDomain.CurrentDomain.ProcessExit += (_, _) => Platforms.Windows.WindowsRuntimeHandoff.TryHandoffAcquisitionToService();
#endif
		return window;
	}
}