namespace HuaGuang.Monitor.Services;

/// <summary>采集运行期间保持后台计时/网络不被系统过度节流（Android 使用 WakeLock）。</summary>
public interface IAcquisitionBackgroundGuard
{
    IDisposable Begin();
}

public sealed class NoOpAcquisitionBackgroundGuard : IAcquisitionBackgroundGuard
{
    public IDisposable Begin() => EmptyDisposable.Instance;

    sealed class EmptyDisposable : IDisposable
    {
        public static readonly EmptyDisposable Instance = new();
        public void Dispose()
        {
        }
    }
}
