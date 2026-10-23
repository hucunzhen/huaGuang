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

            // 保留真实原因（事务号错乱、半包等），勿只报“超时”，否则现场无法区分。
            throw new TimeoutException($"Modbus 通信失败（限时 {timeoutMs} ms）：{ex.Message}", ex);
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
