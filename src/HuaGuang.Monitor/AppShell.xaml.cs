using HuaGuang.Monitor.Services;
using HuaGuang.Monitor.Views;

namespace HuaGuang.Monitor;

public partial class AppShell : Shell
{
	static bool _defaultFullScreenApplied;

	public AppShell()
	{
		InitializeComponent();
		Routing.RegisterRoute(nameof(TagEditPage), typeof(TagEditPage));
		Routing.RegisterRoute(nameof(HistoryDetailPage), typeof(HistoryDetailPage));
		Loaded += OnLoadedApplyDefaultFullScreen;
	}

	void OnLoadedApplyDefaultFullScreen(object? sender, EventArgs e)
	{
		Loaded -= OnLoadedApplyDefaultFullScreen;
		if (_defaultFullScreenApplied)
		{
			return;
		}

		_defaultFullScreenApplied = true;
		MauiProgram.Services.GetRequiredService<FullScreenService>().SetFullScreen(true);
	}
}
