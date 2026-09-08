using HuaGuang.Monitor.Platforms.Windows;
using HuaGuang.Monitor.Services.Logging;
using Microsoft.UI.Xaml;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace HuaGuang.Monitor.WinUI;

/// <summary>
/// Provides application-specific behavior to supplement the default Application class.
/// </summary>
public partial class App : MauiWinUIApplication
{
	static App()
	{
		CrashExitLogger.RegisterEarly("ui");
	}

	/// <summary>
	/// Initializes the singleton application object.  This is the first line of authored code
	/// executed, and as such is the logical equivalent of main() or WinMain().
	/// </summary>
	public App()
	{
		UnhandledException += OnWinUiUnhandledException;
		StartupBootstrapLog.Write("WinUI.App ctor: before InitializeComponent");
		this.InitializeComponent();
		StartupBootstrapLog.Write("WinUI.App ctor: after InitializeComponent");
	}

	static void OnWinUiUnhandledException(object sender, Microsoft.UI.Xaml.UnhandledExceptionEventArgs e)
	{
		CrashExitLogger.Record("WinUI.UnhandledException", e.Exception, fatal: true);
	}

	protected override MauiApp CreateMauiApp()
	{
		try
		{
			StartupBootstrapLog.Write("WinUI.App: CreateMauiApp");
			return MauiProgram.CreateMauiApp();
		}
		catch (Exception ex)
		{
			CrashExitLogger.Record("CreateMauiApp", ex, fatal: true);
			throw;
		}
	}
}