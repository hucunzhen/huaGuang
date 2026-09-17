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
		window.Activated += OnFirstWindowActivated;
#if WINDOWS
		Platforms.Windows.WindowsUiActivation.StartServer(window);
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

	static int _configErrorAlertShown;

	void OnFirstWindowActivated(object? sender, EventArgs e)
	{
		if (Interlocked.Exchange(ref _configErrorAlertShown, 1) != 0)
		{
			return;
		}

		var store = MauiProgram.Services.GetRequiredService<SettingsStore>();
		var error = store.LastLoadError;
		var warnings = store.Current.ConfigLoadWarnings;
		if (string.IsNullOrWhiteSpace(error) && warnings.Count == 0)
		{
			return;
		}

		_ = MainThread.InvokeOnMainThreadAsync(async () =>
		{
			var body = new System.Text.StringBuilder();
			if (!string.IsNullOrWhiteSpace(error))
			{
				body.AppendLine(error);
				body.AppendLine();
				body.AppendLine("请到「设置」修正 PLC 协议与点表，或直接编辑产线 Excel。");
			}

			if (warnings.Count > 0)
			{
				body.AppendLine();
				body.Append("配置告警（部分点位已禁用）：");
				body.AppendLine();
				foreach (var warning in warnings.Take(5))
				{
					body.AppendLine("• " + warning);
				}

				if (warnings.Count > 5)
				{
					body.AppendLine($"… 另有 {warnings.Count - 5} 条，见运行日志。");
				}
			}

			body.AppendLine();
			body.Append("日志目录：");
			body.AppendLine(AppPaths.LogDirectory);

			await Shell.Current.DisplayAlert("产线配置需要检查", body.ToString(), "知道了").ConfigureAwait(true);
		});
	}
}