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
    readonly TimeSpan _timeout;

    public MonitorIpcClient(TimeSpan? timeout = null) =>
        _timeout = timeout ?? TimeSpan.FromSeconds(3);

    public static bool IsServiceAvailable()
    {
        try
        {
            var client = new MonitorIpcClient(TimeSpan.FromMilliseconds(800));
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

    public async Task<MonitorIpcResponse> SendAsync(MonitorIpcRequest request, CancellationToken cancellationToken = default)
    {
        await using var pipe = new NamedPipeClientStream(
            ".",
            MonitorIpcConstants.PipeName,
            PipeDirection.InOut,
            PipeOptions.Asynchronous);

        using var connectCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        connectCts.CancelAfter(_timeout);
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
