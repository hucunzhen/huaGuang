using System.Net.Sockets;
using HuaGuang.Monitor.Models;
using NModbus;

namespace HuaGuang.Monitor.Protocols;

public sealed class ModbusTcpPlcClient : IPlcClient
{
    readonly object _gate = new();
    TcpClient? _tcpClient;
    IModbusMaster? _master;
    byte _station = 1;

    public bool IsConnected => _tcpClient is { Connected: true } && _master is not null;

    public async Task ConnectAsync(PlcSettings settings, CancellationToken cancellationToken)
    {
        await DisconnectAsync().ConfigureAwait(false);

        var client = new TcpClient { NoDelay = true };
        try
        {
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(Math.Max(500, settings.TimeoutMs));
            await client.ConnectAsync(settings.Host, settings.Port, timeoutCts.Token).ConfigureAwait(false);
            client.ReceiveTimeout = settings.TimeoutMs;
            client.SendTimeout = settings.TimeoutMs;

            lock (_gate)
            {
                _tcpClient = client;
                _master = new ModbusFactory().CreateMaster(client);
                _station = settings.Station;
            }
        }
        catch
        {
            client.Dispose();
            throw;
        }
    }

    public Task DisconnectAsync()
    {
        lock (_gate)
        {
            try
            {
                _master?.Dispose();
            }
            catch
            {
                // 忽略断开时的二次释放
            }

            _master = null;
            try
            {
                _tcpClient?.Dispose();
            }
            catch
            {
                // 忽略断开时的二次释放
            }

            _tcpClient = null;
        }

        return Task.CompletedTask;
    }

    public Task<object> ReadAsync(PlcTag tag, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.Run(() => ReadCore(tag), cancellationToken);
    }

    object ReadCore(PlcTag tag)
    {
        IModbusMaster master;
        byte station;
        lock (_gate)
        {
            master = _master ?? throw new InvalidOperationException("PLC 未连接");
            station = _station;
        }

        var table = tag.Table;
        var address = tag.Address;
        var dataType = tag.DataType;
        if (!string.IsNullOrWhiteSpace(tag.XinjeAddress))
        {
            if (!XinjeXd5eMapper.TryResolve(tag.XinjeAddress, out var resolved, out var error))
            {
                throw new InvalidOperationException(error);
            }

            table = resolved.Table;
            address = resolved.Address;
            if (resolved.IsBit)
            {
                dataType = TagDataType.Bool;
            }
        }

        if (table is ModbusTable.Coil or ModbusTable.DiscreteInput)
        {
            var coils = table == ModbusTable.DiscreteInput
                ? master.ReadInputs(station, address, 1)
                : master.ReadCoils(station, address, 1);
            return coils[0];
        }

        var count = (ushort)RegisterConverter.RegisterCount(dataType);
        var registers = table == ModbusTable.InputRegister
            ? master.ReadInputRegisters(station, address, count)
            : master.ReadHoldingRegisters(station, address, count);

        var raw = RegisterConverter.ToValue(registers, dataType, tag.ByteOrder);
        if (dataType == TagDataType.Bool)
        {
            return raw;
        }

        return RegisterConverter.ApplyScale(raw, tag);
    }

    public async ValueTask DisposeAsync() => await DisconnectAsync().ConfigureAwait(false);
}
