using Android.OS;
using HuaGuang.Monitor.Services;

namespace HuaGuang.Monitor.Platforms.Android;

/// <summary>
/// 使用 SystemClock.ElapsedRealtime，避免部分 Android 设备上 Stopwatch 计时不准。
/// </summary>
sealed class AndroidScanMonotonicClock : IScanMonotonicClock
{
    readonly long _startRealtimeMs = SystemClock.ElapsedRealtime();

    public double ElapsedMs => SystemClock.ElapsedRealtime() - _startRealtimeMs;
}
