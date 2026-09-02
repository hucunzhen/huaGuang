namespace HuaGuang.Monitor.Services;

/// <summary>扫码输入时临时切换到英文输入法，Dispose 后恢复。</summary>
public interface IScannerInputMethodGuard
{
    IDisposable EnterEnglishInputMode(Entry? entry = null);
}

public sealed class NoOpScannerInputMethodGuard : IScannerInputMethodGuard
{
    public IDisposable EnterEnglishInputMode(Entry? entry = null) => EmptyScope.Instance;

    internal sealed class EmptyScope : IDisposable
    {
        public static readonly EmptyScope Instance = new();
        public void Dispose()
        {
        }
    }
}
