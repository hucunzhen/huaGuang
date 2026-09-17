using System.Net.Sockets;

namespace HuaGuang.Monitor.Protocols;

internal static class ModbusIoTimeout
{
    /// <summary>
    /// 在采集线程上同步执行 Modbus IO（依赖 TcpClient Send/ReceiveTimeout）。
    /// 不再使用 Task.Run + Wait 包一层超时，避免超时后后台读仍操作连接导致「连上即断」。
    /// </summary>
    public static T Run<T>(Func<T> operation, int timeoutMs, Action? onTimeout = null)
    {
        if (timeoutMs <= 0)
        {
            return operation();
        }

        try
        {
            return operation();
        }
        catch (Exception ex) when (IsTransportFailure(ex))
        {
            onTimeout?.Invoke();
            if (ex is TimeoutException)
            {
                throw;
            }

            throw new TimeoutException($"Modbus 通信超时或中断（{timeoutMs} ms）。", ex);
        }
    }

    static bool IsTransportFailure(Exception ex)
    {
        for (Exception? current = ex; current is not null; current = current.InnerException)
        {
            if (current is TimeoutException or IOException or SocketException)
            {
                return true;
            }
        }

        return false;
    }
}
