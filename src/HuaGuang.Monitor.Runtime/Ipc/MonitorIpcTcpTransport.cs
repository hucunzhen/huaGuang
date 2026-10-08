using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace HuaGuang.Monitor.Ipc;

static class MonitorIpcStreamSession
{
    internal static async Task HandleAsync(Stream stream, Func<MonitorIpcRequest, CancellationToken, Task<MonitorIpcResponse>> dispatch, CancellationToken cancellationToken)
    {
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: false, bufferSize: 4096, leaveOpen: true);
        var requestLine = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(requestLine))
        {
            return;
        }

        MonitorIpcResponse response;
        try
        {
            var request = JsonSerializer.Deserialize<MonitorIpcRequest>(requestLine, MonitorIpcJson.Options)
                ?? throw new InvalidOperationException("无效请求");
            response = await dispatch(request, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            response = new MonitorIpcResponse { Success = false, Error = ex.Message };
        }

        var responseLine = JsonSerializer.Serialize(response, MonitorIpcJson.Options) + "\n";
        var bytes = Encoding.UTF8.GetBytes(responseLine);
        await stream.WriteAsync(bytes, cancellationToken).ConfigureAwait(false);
    }
}

static class MonitorIpcTcpTransport
{
    internal static async Task RunServerAsync(
        Func<MonitorIpcRequest, CancellationToken, Task<MonitorIpcResponse>> dispatch,
        ILogger logger,
        CancellationToken stoppingToken)
    {
        TcpListener listener;
        try
        {
            listener = new TcpListener(IPAddress.Loopback, MonitorIpcConstants.CurrentTcpPort);
            listener.Start();
        }
        catch (Exception ex)
        {
            logger.LogWarning(
                ex,
                "IPC TCP 端口 127.0.0.1:{Port} 不可用，将仅使用命名管道",
                MonitorIpcConstants.CurrentTcpPort);
            try
            {
                await Task.Delay(Timeout.Infinite, stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
            }

            return;
        }

        logger.LogInformation("IPC TCP 已监听 127.0.0.1:{Port}", MonitorIpcConstants.CurrentTcpPort);

        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                var client = await listener.AcceptTcpClientAsync(stoppingToken).ConfigureAwait(false);
                _ = HandleClientSafeAsync(client, dispatch, logger, stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
        finally
        {
            listener.Stop();
        }
    }

    static async Task HandleClientSafeAsync(
        TcpClient client,
        Func<MonitorIpcRequest, CancellationToken, Task<MonitorIpcResponse>> dispatch,
        ILogger logger,
        CancellationToken stoppingToken)
    {
        try
        {
            await using var stream = client.GetStream();
            await MonitorIpcStreamSession.HandleAsync(stream, dispatch, stoppingToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "IPC TCP 连接处理失败");
        }
        finally
        {
            client.Dispose();
        }
    }

    internal static Task<MonitorIpcResponse> SendAsync(MonitorIpcRequest request, TimeSpan timeout, CancellationToken cancellationToken) =>
        SendAsync(request, timeout, MonitorIpcConstants.CurrentTcpPort, cancellationToken);

    internal static async Task<MonitorIpcResponse> SendAsync(
        MonitorIpcRequest request,
        TimeSpan timeout,
        int tcpPort,
        CancellationToken cancellationToken)
    {
        using var client = new TcpClient();
        using var connectCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        connectCts.CancelAfter(timeout);
        await ConnectLoopbackAsync(client, tcpPort, connectCts.Token).ConfigureAwait(false);

        await using var stream = client.GetStream();
        var requestLine = JsonSerializer.Serialize(request, MonitorIpcJson.Options) + "\n";
        var requestBytes = Encoding.UTF8.GetBytes(requestLine);
        await stream.WriteAsync(requestBytes, connectCts.Token).ConfigureAwait(false);

        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: false, bufferSize: 4096, leaveOpen: true);
        var responseLine = await reader.ReadLineAsync(connectCts.Token).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(responseLine))
        {
            return new MonitorIpcResponse { Success = false, Error = "服务无响应" };
        }

        return JsonSerializer.Deserialize<MonitorIpcResponse>(responseLine, MonitorIpcJson.Options)
            ?? new MonitorIpcResponse { Success = false, Error = "响应解析失败" };
    }

    /// <summary>
    /// ConnectAsync 在 Windows 上对未监听端口可能忽略取消令牌，一直等到系统 TCP 超时（约 20 秒），
    /// 订阅大屏等独立实例会把 UI 线程卡住。超时时 Dispose 以立刻断开。
    /// </summary>
    static async Task ConnectLoopbackAsync(TcpClient client, int tcpPort, CancellationToken cancellationToken)
    {
        var connect = client.ConnectAsync(IPAddress.Loopback, tcpPort);
        var abort = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var registration = cancellationToken.Register(() => abort.TrySetResult());
        var finished = await Task.WhenAny(connect, abort.Task).ConfigureAwait(false);
        if (finished != connect)
        {
            try
            {
                client.Close();
            }
            catch
            {
            }

            cancellationToken.ThrowIfCancellationRequested();
            throw new TimeoutException($"IPC TCP 连接超时 127.0.0.1:{tcpPort}");
        }

        await connect.ConfigureAwait(false);
    }
}
