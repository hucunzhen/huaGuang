using Android.App;
using Android.Content.PM;
using Android.OS;
using AndroidX.Core.View;

namespace HuaGuang.Monitor;

[Activity(
	Theme = "@style/Maui.SplashTheme",
	MainLauncher = true,
	Exported = true,
	LaunchMode = LaunchMode.SingleTop,
	ConfigurationChanges = ConfigChanges.ScreenSize | ConfigChanges.Orientation | ConfigChanges.UiMode | ConfigChanges.ScreenLayout | ConfigChanges.SmallestScreenSize | ConfigChanges.Density)]
public class MainActivity : MauiAppCompatActivity
{
	protected override void OnCreate(Bundle? savedInstanceState)
	{
		base.OnCreate(savedInstanceState);

		if (Window is null)
		{
			return;
		}

		var background = global::Android.Graphics.Color.ParseColor("#0B1522");
		var tabBar = global::Android.Graphics.Color.ParseColor("#101C28");
		Window.SetStatusBarColor(background);
		if (Build.VERSION.SdkInt >= BuildVersionCodes.O)
		{
			Window.SetNavigationBarColor(tabBar);
		}

		WindowCompat.SetDecorFitsSystemWindows(Window, true);
	}
}
