using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.OS;
using AUri = Android.Net.Uri;
using AndroidX.Activity.Result;
using AndroidX.Activity.Result.Contract;
using AndroidX.Core.View;
using HuaGuang.Monitor.Platforms.Android;

namespace HuaGuang.Monitor;

[Activity(
	Theme = "@style/Maui.SplashTheme",
	MainLauncher = true,
	Exported = true,
	LaunchMode = LaunchMode.SingleTop,
	ConfigurationChanges = ConfigChanges.ScreenSize | ConfigChanges.Orientation | ConfigChanges.UiMode | ConfigChanges.ScreenLayout | ConfigChanges.SmallestScreenSize | ConfigChanges.Density)]
public class MainActivity : MauiAppCompatActivity
{
	ActivityResultLauncher? _documentTreeLauncher;
	TaskCompletionSource<AUri?>? _documentTreePickTask;

	protected override void OnCreate(Bundle? savedInstanceState)
	{
		base.OnCreate(savedInstanceState);

		_documentTreeLauncher = RegisterForActivityResult(
			new ActivityResultContracts.OpenDocumentTree(),
			new DocumentTreeCallback(this));

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

	internal Task<AUri?> PickDocumentTreeAsync(AUri? initialTreeUri)
	{
		if (_documentTreeLauncher is null)
		{
			throw new InvalidOperationException("目录选择器尚未初始化。");
		}

		_documentTreePickTask = new TaskCompletionSource<AUri?>(TaskCreationOptions.RunContinuationsAsynchronously);
		try
		{
			_documentTreeLauncher.Launch(initialTreeUri);
		}
		catch (Java.Lang.IllegalArgumentException)
		{
			_documentTreeLauncher.Launch(null);
		}

		return _documentTreePickTask.Task;
	}

	internal void CompleteDocumentTreePick(AUri? uri) =>
		_documentTreePickTask?.TrySetResult(uri);

	sealed class DocumentTreeCallback : Java.Lang.Object, IActivityResultCallback
	{
		readonly MainActivity _activity;

		public DocumentTreeCallback(MainActivity activity) => _activity = activity;

		public void OnActivityResult(Java.Lang.Object? result)
		{
			var uri = result as AUri;
			if (uri is not null)
			{
				AndroidSafTreeUri.TryTakePersistablePermission(_activity, uri);
			}

			_activity.CompleteDocumentTreePick(uri);
		}
	}
}
