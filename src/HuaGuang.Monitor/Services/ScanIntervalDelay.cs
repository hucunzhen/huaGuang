namespace HuaGuang.Monitor.Services;

internal static class ScanIntervalDelay
{
    /// <summary>
    /// 阻塞等待到目标单调时钟时刻（毫秒）。
    /// </summary>
    public static void WaitUntil(double targetElapsedMs, IScanMonotonicClock clock, CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            var remainingMs = targetElapsedMs - clock.ElapsedMs;
            if (remainingMs <= 0)
            {
                break;
            }

            if (remainingMs > 100)
            {
                Thread.Sleep(Math.Min((int)remainingMs, 250));
            }
            else if (remainingMs > 2)
            {
                Thread.Sleep(1);
            }
            else
            {
                Thread.SpinWait(50);
            }
        }

        cancellationToken.ThrowIfCancellationRequested();
    }
}
