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
            listener = new TcpListener(IPAddress.Loopback, MonitorIpcConstants.TcpPort);
            listener.Start();
        }
        catch (Exception ex)
        {
            logger.LogWarning(
                ex,
                "IPC TCP 端口 127.0.0.1:{Port} 不可用，将仅使用命名管道",
                MonitorIpcConstants.TcpPort);
            try
            {
                await Task.Delay(Timeout.Infinite, stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
            }

            return;
        }

        logger.LogInformation("IPC TCP 已监听 127.0.0.1:{Port}", MonitorIpcConstants.TcpPort);

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

    internal static async Task<MonitorIpcResponse> SendAsync(MonitorIpcRequest request, TimeSpan timeout, CancellationToken cancellationToken)
    {
        using var client = new TcpClient();
        using var connectCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        connectCts.CancelAfter(timeout);
        await client.ConnectAsync(IPAddress.Loopback, MonitorIpcConstants.TcpPort, connectCts.Token).ConfigureAwait(false);

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
}
