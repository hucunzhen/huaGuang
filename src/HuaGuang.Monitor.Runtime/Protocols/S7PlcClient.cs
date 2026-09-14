using HuaGuang.Monitor.Models;
using HuaGuang.Monitor.Protocols;
using HuaGuang.Monitor.Services.Logging;
using Microsoft.Extensions.Logging;
using S7.Net;

namespace HuaGuang.Monitor.Protocols;

public sealed class S7PlcClient : IPlcClient
{
    readonly object _gate = new();
    readonly ILogger<S7PlcClient> _logger;
    Plc? _plc;

    public S7PlcClient(ILogger<S7PlcClient> logger) => _logger = logger;

    public bool IsConnected
    {
        get
        {
            lock (_gate)
            {
                return _plc?.IsConnected == true;
            }
        }
    }

    public async Task ConnectAsync(PlcSettings settings, CancellationToken cancellationToken)
    {
        await DisconnectAsync().ConfigureAwait(false);

        var timeoutMs = Math.Clamp(settings.TimeoutMs, 500, 10_000);
        var cpu = ParseCpuType(settings.CpuType);
        var plc = new Plc(cpu, settings.Host, (short)settings.Rack, (short)settings.Slot)
        {
            ReadTimeout = timeoutMs,
            WriteTimeout = timeoutMs
        };

        try
        {
            await Task.Run(() => plc.Open(), cancellationToken).ConfigureAwait(false);
            lock (_gate)
            {
                _plc = plc;
            }

            _logger.LogInformation("PLC 已连接 {Plc}", LogFormatting.DescribePlc(settings));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "PLC 连接失败 {Plc}", LogFormatting.DescribePlc(settings));
            try
            {
                plc.Close();
            }
            catch
            {
                // 忽略清理时的二次异常
            }

            throw;
        }
    }

    public Task DisconnectAsync()
    {
        Plc? plc;
        lock (_gate)
        {
            plc = _plc;
            _plc = null;
        }

        if (plc is null)
        {
            return Task.CompletedTask;
        }

        try
        {
            if (plc.IsConnected)
            {
                plc.Close();
                _logger.LogInformation("PLC 已断开");
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "PLC 断开时出现异常");
        }

        return Task.CompletedTask;
    }

    public Task<object> ReadAsync(PlcTag tag, CancellationToken cancellationToken) =>
        Task.FromResult(ReadCore(tag));

    public async Task<IReadOnlyDictionary<string, object?>> ReadTagsAsync(
        IReadOnlyList<PlcTag> tags,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (tags.Count == 0)
        {
            return new Dictionary<string, object?>(StringComparer.Ordinal);
        }

        var result = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var tag in tags)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (tag.IsManual || string.IsNullOrWhiteSpace(tag.Name))
            {
                continue;
            }

            try
            {
                result[tag.Name] = await Task.Run(() => ReadCore(tag), cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    ex,
                    "S7 读取失败 tag={TagName} address={Address}",
                    tag.Name,
                    tag.DisplayAddress);
                throw;
            }
        }

        return result;
    }

    object ReadCore(PlcTag tag)
    {
        Plc plc;
        lock (_gate)
        {
            plc = _plc ?? throw new InvalidOperationException("PLC 未连接");
        }

        if (!SiemensS7AddressMapper.TryResolve(tag.XinjeAddress, tag.DataType, out var resolved, out var error))
        {
            throw new InvalidOperationException(error);
        }

        var dataType = resolved.IsBit ? TagDataType.Bool : tag.DataType;
        var byteCount = S7ByteConverter.ByteCount(dataType);
        var area = MapArea(resolved.Area);
        var bytes = plc.ReadBytes(area, resolved.DbNumber, resolved.ByteOffset, byteCount);
        var raw = S7ByteConverter.ToValue(bytes, 0, dataType, tag.ByteOrder, resolved.BitOffset);
        return dataType == TagDataType.Bool ? raw : RegisterConverter.ApplyScale(raw, tag);
    }

    static DataType MapArea(S7MemoryArea area) => area switch
    {
        S7MemoryArea.Input => DataType.Input,
        S7MemoryArea.Output => DataType.Output,
        S7MemoryArea.Memory => DataType.Memory,
        S7MemoryArea.DataBlock => DataType.DataBlock,
        _ => throw new ArgumentOutOfRangeException(nameof(area), area, "不支持的 S7 区域")
    };

    static CpuType ParseCpuType(string? cpuType) => cpuType?.Trim().ToUpperInvariant() switch
    {
        "S7200" or "S7200SMART" or "S7-200" or "S7-200SMART" => CpuType.S7200,
        "S7300" or "S7-300" => CpuType.S7300,
        "S7400" or "S7-400" => CpuType.S7400,
        "S71500" or "S7-1500" => CpuType.S71500,
        _ => CpuType.S71200
    };

    public async ValueTask DisposeAsync() => await DisconnectAsync().ConfigureAwait(false);
}
