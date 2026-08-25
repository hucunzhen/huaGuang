using Android.OS;
using HuaGuang.Monitor.Services;
using Application = Android.App.Application;

namespace HuaGuang.Monitor.Platforms.Android;

public sealed class AndroidAcquisitionBackgroundGuard : IAcquisitionBackgroundGuard
{
    public IDisposable Begin()
    {
        var powerManager = (PowerManager?)Application.Context.GetSystemService(global::Android.Content.Context.PowerService);
        var wakeLock = powerManager?.NewWakeLock(WakeLockFlags.Partial, "HuaGuang.Monitor:Acquisition");
        wakeLock?.Acquire();
        return new WakeLockLease(wakeLock);
    }

    sealed class WakeLockLease(PowerManager.WakeLock? wakeLock) : IDisposable
    {
        public void Dispose()
        {
            if (wakeLock is null)
            {
                return;
            }

            if (wakeLock.IsHeld)
            {
                wakeLock.Release();
            }

            wakeLock.Dispose();
        }
    }
}
