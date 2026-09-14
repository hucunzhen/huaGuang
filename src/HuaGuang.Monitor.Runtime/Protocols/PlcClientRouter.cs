using HuaGuang.Monitor.Models;
using HuaGuang.Monitor.Services;

namespace HuaGuang.Monitor.Protocols;

/// <summary>按当前配置的 PLC 协议路由到 Modbus 或 S7 客户端。</summary>
public sealed class PlcClientRouter : IPlcClient
{
    readonly SettingsStore _settingsStore;
    readonly ModbusTcpPlcClient _modbus;
    readonly S7PlcClient _s7;

    public PlcClientRouter(
        SettingsStore settingsStore,
        ModbusTcpPlcClient modbus,
        S7PlcClient s7)
    {
        _settingsStore = settingsStore;
        _modbus = modbus;
        _s7 = s7;
    }

    IPlcClient Active
    {
        get
        {
            var protocol = _settingsStore.Current.Plc.Protocol;
            return protocol == PlcProtocol.S7 ? _s7 : _modbus;
        }
    }

    public bool IsConnected => Active.IsConnected;

    public Task ConnectAsync(PlcSettings settings, CancellationToken cancellationToken) =>
        Select(settings).ConnectAsync(settings, cancellationToken);

    public async Task DisconnectAsync()
    {
        await _modbus.DisconnectAsync().ConfigureAwait(false);
        await _s7.DisconnectAsync().ConfigureAwait(false);
    }

    public Task<object> ReadAsync(PlcTag tag, CancellationToken cancellationToken) =>
        Active.ReadAsync(tag, cancellationToken);

    public Task<IReadOnlyDictionary<string, object?>> ReadTagsAsync(
        IReadOnlyList<PlcTag> tags,
        CancellationToken cancellationToken) =>
        Active.ReadTagsAsync(tags, cancellationToken);

    IPlcClient Select(PlcSettings settings) =>
        settings.Protocol == PlcProtocol.S7 ? _s7 : _modbus;

    public async ValueTask DisposeAsync() => await DisconnectAsync().ConfigureAwait(false);
}
