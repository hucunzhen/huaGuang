namespace HuaGuang.Monitor.Services;

/// <summary>采集运行期间保持后台能力（Android：前台服务 + WakeLock）。</summary>
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
