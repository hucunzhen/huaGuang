using Android.OS;
using HuaGuang.Monitor.Services;
using Application = Android.App.Application;

namespace HuaGuang.Monitor.Platforms.Android;

public sealed class AndroidAcquisitionBackgroundGuard : IAcquisitionBackgroundGuard
{
    public IDisposable Begin()
    {
        var context = Application.Context;
        AcquisitionForegroundServiceStarter.Start(context);

        var powerManager = (PowerManager?)context.GetSystemService(global::Android.Content.Context.PowerService);
        var wakeLock = powerManager?.NewWakeLock(WakeLockFlags.Partial, "HuaGuang.Monitor:Acquisition");
        wakeLock?.Acquire();
        return new AcquisitionBackgroundLease(context, wakeLock);
    }

    sealed class AcquisitionBackgroundLease(global::Android.Content.Context context, PowerManager.WakeLock? wakeLock) : IDisposable
    {
        public void Dispose()
        {
            if (wakeLock is not null)
            {
                if (wakeLock.IsHeld)
                {
                    wakeLock.Release();
                }

                wakeLock.Dispose();
            }

            AcquisitionForegroundServiceStarter.Stop(context);
        }
    }
}
