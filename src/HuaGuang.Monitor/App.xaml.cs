using Microsoft.Extensions.DependencyInjection;

namespace HuaGuang.Monitor;

public partial class App : Application
{
	public App()
	{
		InitializeComponent();
		UserAppTheme = AppTheme.Dark;
	}

	protected override Window CreateWindow(IActivationState? activationState)
	{
		var window = new Window(new AppShell());
#if WINDOWS
		Platforms.Windows.WindowsAppIcon.Apply(window);
#endif
		return window;
	}
}