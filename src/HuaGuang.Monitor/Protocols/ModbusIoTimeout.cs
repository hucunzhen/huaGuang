namespace HuaGuang.Monitor.Protocols;

internal static class ModbusIoTimeout
{
    public static T Run<T>(Func<T> operation, int timeoutMs, Action? onTimeout = null)
    {
        if (timeoutMs <= 0)
        {
            return operation();
        }

        var task = Task.Run(operation);
        if (task.Wait(timeoutMs))
        {
            return task.GetAwaiter().GetResult();
        }

        onTimeout?.Invoke();
        throw new TimeoutException($"Modbus 通信超时（{timeoutMs} ms）。");
    }
}
