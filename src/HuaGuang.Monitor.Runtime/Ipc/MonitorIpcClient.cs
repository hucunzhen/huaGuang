using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace HuaGuang.Monitor.Ipc;

public static class MonitorIpcJson
{
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };
}

public sealed class MonitorIpcClient
{
    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(5);
    public static readonly TimeSpan CommandTimeout = TimeSpan.FromSeconds(30);

    readonly TimeSpan _timeout;

    public MonitorIpcClient(TimeSpan? timeout = null) =>
        _timeout = timeout ?? DefaultTimeout;

    public static bool WaitForServiceAvailable(TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (IsServiceAvailable())
            {
                return true;
            }

            Thread.Sleep(500);
        }

        return IsServiceAvailable();
    }

    public static bool IsServiceAvailable()
    {
        try
        {
            var client = new MonitorIpcClient(TimeSpan.FromSeconds(2));
            var response = client.SendAsync(new MonitorIpcRequest { Command = MonitorIpcCommand.Ping })
                .GetAwaiter()
                .GetResult();
            return response.Success;
        }
        catch
        {
            return false;
        }
    }

    public static string DescribeConnectionFailure()
    {
        try
        {
            var response = new MonitorIpcClient(TimeSpan.FromSeconds(3))
                .SendAsync(new MonitorIpcRequest { Command = MonitorIpcCommand.Ping })
                .GetAwaiter()
                .GetResult();
            if (response.Success)
            {
                return "后台 IPC 可连接，但命令执行失败。";
            }

            return response.Error ?? "后台 IPC 无响应。";
        }
        catch (Exception ex)
        {
            return ex.Message;
        }
    }

    public Task<MonitorIpcResponse> SendAsync(MonitorIpcRequest request, CancellationToken cancellationToken = default) =>
        SendAsync(request, timeout: null, cancellationToken);

    public async Task<MonitorIpcResponse> SendAsync(
        MonitorIpcRequest request,
        TimeSpan? timeout,
        CancellationToken cancellationToken = default)
    {
        var effectiveTimeout = timeout ?? _timeout;
        if (OperatingSystem.IsWindows())
        {
            Exception? tcpError = null;
            try
            {
                return await MonitorIpcTcpTransport.SendAsync(request, effectiveTimeout, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                tcpError = ex;
            }

            try
            {
                return await SendViaPipeAsync(request, effectiveTimeout, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception pipeError)
            {
                return new MonitorIpcResponse
                {
                    Success = false,
                    Error = FormatConnectionError(pipeError, tcpError)
                };
            }
        }

        try
        {
            return await SendViaPipeAsync(request, effectiveTimeout, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception pipeError)
        {
            return new MonitorIpcResponse
            {
                Success = false,
                Error = FormatConnectionError(pipeError, tcpError: null)
            };
        }
    }

    static string FormatConnectionError(Exception pipeError, Exception? tcpError)
    {
        if (tcpError is null)
        {
            return $"IPC 连接失败：{pipeError.Message}";
        }

        return $"IPC 连接失败（TCP：{tcpError.Message}；管道：{pipeError.Message}）";
    }

    async Task<MonitorIpcResponse> SendViaPipeAsync(
        MonitorIpcRequest request,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        await using var pipe = new NamedPipeClientStream(
            ".",
            MonitorIpcConstants.PipeName,
            PipeDirection.InOut,
            PipeOptions.Asynchronous);

        using var connectCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        connectCts.CancelAfter(timeout);
        await pipe.ConnectAsync(connectCts.Token).ConfigureAwait(false);

        var requestLine = JsonSerializer.Serialize(request, MonitorIpcJson.Options) + "\n";
        var requestBytes = Encoding.UTF8.GetBytes(requestLine);
        await pipe.WriteAsync(requestBytes, connectCts.Token).ConfigureAwait(false);

        using var reader = new StreamReader(pipe, Encoding.UTF8, leaveOpen: true);
        var responseLine = await reader.ReadLineAsync(connectCts.Token).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(responseLine))
        {
            return new MonitorIpcResponse { Success = false, Error = "服务无响应" };
        }

        return JsonSerializer.Deserialize<MonitorIpcResponse>(responseLine, MonitorIpcJson.Options)
            ?? new MonitorIpcResponse { Success = false, Error = "响应解析失败" };
    }
}
