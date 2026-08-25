using HuaGuang.Monitor.Models;

namespace HuaGuang.Monitor.Protocols;

public interface IPlcClient : IAsyncDisposable
{
    bool IsConnected { get; }
    Task ConnectAsync(PlcSettings settings, CancellationToken cancellationToken);
    Task DisconnectAsync();
    Task<object> ReadAsync(PlcTag tag, CancellationToken cancellationToken);
    Task<IReadOnlyDictionary<string, object?>> ReadTagsAsync(IReadOnlyList<PlcTag> tags, CancellationToken cancellationToken);
}
