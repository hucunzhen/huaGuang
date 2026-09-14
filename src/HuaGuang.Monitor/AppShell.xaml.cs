using HuaGuang.Monitor.Views;

namespace HuaGuang.Monitor;

public partial class AppShell : Shell
{
	public AppShell()
	{
		InitializeComponent();
		Routing.RegisterRoute(nameof(TagEditPage), typeof(TagEditPage));
		Routing.RegisterRoute(nameof(HistoryDetailPage), typeof(HistoryDetailPage));
	}
}
