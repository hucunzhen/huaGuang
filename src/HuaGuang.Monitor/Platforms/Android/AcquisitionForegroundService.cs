using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.OS;
using AndroidX.Core.App;

namespace HuaGuang.Monitor.Platforms.Android;

[Service(
    Name = "com.industrial.monitor.AcquisitionForegroundService",
    Exported = false,
    ForegroundServiceType = ForegroundService.TypeDataSync)]
public sealed class AcquisitionForegroundService : Service
{
    internal const int NotificationId = 91001;
    internal const string ChannelId = "com.industrial.monitor.acquisition";
    internal const string ActionStart = "com.industrial.monitor.action.ACQUISITION_FG_START";
    internal const string ActionStop = "com.industrial.monitor.action.ACQUISITION_FG_STOP";

    public override IBinder? OnBind(Intent? intent) => null;

    public override StartCommandResult OnStartCommand(Intent? intent, StartCommandFlags flags, int startId)
    {
        if (string.Equals(intent?.Action, ActionStop, StringComparison.Ordinal))
        {
            if (Build.VERSION.SdkInt >= BuildVersionCodes.N)
            {
                StopForeground(StopForegroundFlags.Remove);
            }
            else
            {
                StopForeground(true);
            }

            StopSelf();
            return StartCommandResult.NotSticky;
        }

        EnsureNotificationChannel();
        var notification = BuildNotification(GetString(Resource.String.acquisition_fg_notification_title));
        StartForeground(NotificationId, notification);
        return StartCommandResult.NotSticky;
    }

    void EnsureNotificationChannel()
    {
        if (Build.VERSION.SdkInt < BuildVersionCodes.O)
        {
            return;
        }

        var manager = (NotificationManager?)GetSystemService(NotificationService);
        if (manager?.GetNotificationChannel(ChannelId) is not null)
        {
            return;
        }

        var channel = new NotificationChannel(
            ChannelId,
            GetString(Resource.String.acquisition_fg_notification_channel),
            NotificationImportance.Low)
        {
            Description = GetString(Resource.String.acquisition_fg_notification_channel_desc)
        };
        channel.SetShowBadge(false);
        manager?.CreateNotificationChannel(channel);
    }

    Notification BuildNotification(string title)
    {
        var launchIntent = PackageManager?.GetLaunchIntentForPackage(PackageName!);
        var contentIntent = PendingIntent.GetActivity(
            this,
            0,
            launchIntent,
            PendingIntentFlags.Immutable | PendingIntentFlags.UpdateCurrent);

        var smallIcon = ApplicationInfo?.Icon ?? global::Android.Resource.Drawable.StatSysDownloadDone;

        var builder = new NotificationCompat.Builder(this, ChannelId)
            .SetContentTitle(title)
            .SetContentText(GetString(Resource.String.acquisition_fg_notification_text))
            .SetSmallIcon(smallIcon)
            .SetOngoing(true)
            .SetOnlyAlertOnce(true)
            .SetContentIntent(contentIntent)
            .SetCategory(NotificationCompat.CategoryService);

        if (Build.VERSION.SdkInt >= BuildVersionCodes.Q)
        {
            builder.SetForegroundServiceBehavior(NotificationCompat.ForegroundServiceImmediate);
        }

        return builder.Build()!;
    }
}

static class AcquisitionForegroundServiceStarter
{
    public static void Start(Context context)
    {
        AcquisitionNotificationPermission.EnsureRequested();
        var intent = new Intent(context, typeof(AcquisitionForegroundService));
        intent.SetAction(AcquisitionForegroundService.ActionStart);
        if (Build.VERSION.SdkInt >= BuildVersionCodes.O)
        {
            context.StartForegroundService(intent);
        }
        else
        {
            context.StartService(intent);
        }
    }

    public static void Stop(Context context)
    {
        var intent = new Intent(context, typeof(AcquisitionForegroundService));
        intent.SetAction(AcquisitionForegroundService.ActionStop);
        context.StartService(intent);
    }
}

static class AcquisitionNotificationPermission
{
    public static void EnsureRequested()
    {
        if (Build.VERSION.SdkInt < BuildVersionCodes.Tiramisu)
        {
            return;
        }

        var activity = Microsoft.Maui.ApplicationModel.Platform.CurrentActivity;
        if (activity is null)
        {
            return;
        }

        if (AndroidX.Core.Content.ContextCompat.CheckSelfPermission(activity, global::Android.Manifest.Permission.PostNotifications)
            == Permission.Granted)
        {
            return;
        }

        AndroidX.Core.App.ActivityCompat.RequestPermissions(
            activity,
            [global::Android.Manifest.Permission.PostNotifications],
            AcquisitionForegroundService.NotificationId);
    }
}
