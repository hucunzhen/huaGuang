using System.Net.Sockets;
using HuaGuang.Monitor.Models;
using HuaGuang.Monitor.Services.Logging;
using Microsoft.Extensions.Logging;
using NModbus;

namespace HuaGuang.Monitor.Protocols;

public sealed class ModbusTcpPlcClient : IPlcClient
{
    readonly object _gate = new();
    readonly ILogger<ModbusTcpPlcClient> _logger;
    TcpClient? _tcpClient;
    IModbusMaster? _master;
    byte _station = 1;
    int _timeoutMs = 2000;

    public ModbusTcpPlcClient(ILogger<ModbusTcpPlcClient> logger) => _logger = logger;

    public bool IsConnected => _tcpClient is { Connected: true } && _master is not null;

    public async Task ConnectAsync(PlcSettings settings, CancellationToken cancellationToken)
    {
        await DisconnectAsync().ConfigureAwait(false);

        var timeoutMs = Math.Clamp(settings.TimeoutMs, 500, 10_000);
        var client = new TcpClient { NoDelay = true };

        try
        {
            var connectTask = client.ConnectAsync(settings.Host, settings.Port);
            var completed = await Task.WhenAny(connectTask, Task.Delay(timeoutMs, cancellationToken)).ConfigureAwait(false);
            if (completed != connectTask)
            {
                try
                {
                    client.Close();
                }
                catch
                {
                    // 强制关闭以中断仍在进行的 TCP 连接
                }

                throw new TimeoutException($"PLC 连接超时（{timeoutMs} ms）：{settings.Host}:{settings.Port}");
            }

            await connectTask.ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();

            client.ReceiveTimeout = timeoutMs;
            client.SendTimeout = timeoutMs;

            lock (_gate)
            {
                _tcpClient = client;
                _master = new ModbusFactory().CreateMaster(client);
                _station = settings.Station;
                _timeoutMs = timeoutMs;
            }

            _logger.LogInformation("PLC 已连接 {Plc}", LogFormatting.DescribePlc(settings));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "PLC 连接失败 {Plc}", LogFormatting.DescribePlc(settings));
            try
            {
                client.Close();
            }
            catch
            {
                // 忽略清理时的二次异常
            }

            client.Dispose();
            throw;
        }
    }

    public Task DisconnectAsync()
    {
        var wasConnected = false;
        lock (_gate)
        {
            wasConnected = _tcpClient is { Connected: true };
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

        if (wasConnected)
        {
            _logger.LogInformation("PLC 已断开");
        }

        return Task.CompletedTask;
    }

    public Task<object> ReadAsync(PlcTag tag, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(ReadCore(tag));
    }

    public Task<IReadOnlyDictionary<string, object?>> ReadTagsAsync(IReadOnlyList<PlcTag> tags, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (tags.Count == 0)
        {
            return Task.FromResult<IReadOnlyDictionary<string, object?>>(new Dictionary<string, object?>(StringComparer.Ordinal));
        }

        IModbusMaster master;
        byte station;
        int timeoutMs;
        lock (_gate)
        {
            master = _master ?? throw new InvalidOperationException("PLC 未连接");
            station = _station;
            timeoutMs = _timeoutMs;
        }

        var values = ModbusTagBatchReader.ReadTags(
            master,
            station,
            tags,
            timeoutMs,
            AbortOnTimeout);
        var result = values.ToDictionary(pair => pair.Key, pair => (object?)pair.Value, StringComparer.Ordinal);
        foreach (var tag in tags)
        {
            if (tag.IsManual || string.IsNullOrWhiteSpace(tag.Name) || result.ContainsKey(tag.Name))
            {
                continue;
            }

            _logger.LogWarning(
                "批量读未返回点位 {TagName}（{Address}），改为单独读取",
                tag.Name,
                tag.DisplayAddress);
            result[tag.Name] = ReadCore(tag);
        }

        return Task.FromResult<IReadOnlyDictionary<string, object?>>(result);
    }

    void AbortOnTimeout()
    {
        _logger.LogWarning("PLC 读超时，强制断开连接");
        lock (_gate)
        {
            try
            {
                _tcpClient?.Close();
            }
            catch
            {
                // 强制断开以结束阻塞中的 socket 读
            }

            _master = null;
            _tcpClient = null;
        }
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
