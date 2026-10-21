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

            if (remainingMs > 2)
            {
                Thread.Sleep(Math.Min((int)Math.Ceiling(remainingMs), 250));
            }
            else
            {
                Thread.Sleep(1);
            }
        }

        cancellationToken.ThrowIfCancellationRequested();
    }
}
