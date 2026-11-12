using System.Net.Sockets;
using System.Reflection;
using HuaGuang.Monitor.Models;
using HuaGuang.Monitor.Protocols;
using HuaGuang.Monitor.Services;
using HuaGuang.Monitor.Services.Logging;
using Microsoft.Extensions.Logging;
using S7.Net;

namespace HuaGuang.Monitor.Protocols;

public sealed class S7PlcClient : IPlcClient
{
    readonly object _gate = new();
    readonly ILogger<S7PlcClient> _logger;
    Plc? _plc;
    int _timeoutMs = 2000;

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

        PlcSettingsHelper.Normalize(settings);
        var timeoutMs = Math.Clamp(settings.TimeoutMs, 500, 10_000);
        var port = settings.Port > 0 ? settings.Port : 102;
        var handshake = ResolveHandshake(settings.CpuType, settings.Rack, settings.Slot);
        var plc = new Plc(handshake.Cpu, settings.Host, port, handshake.Rack, handshake.Slot)
        {
            ReadTimeout = timeoutMs,
            WriteTimeout = timeoutMs
        };

        // S7.Net 的 Open() 不看 ReadTimeout，取消令牌也停不掉已经开始的握手。
        // 到时关掉底层套接字，采集线程才能在 TimeoutMs 内返回并继续读其他 PLC。
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(timeoutMs);
        using var abort = timeoutCts.Token.Register(() => AbortSocket(plc));
        try
        {
            await plc.OpenAsync(timeoutCts.Token).ConfigureAwait(false);
            if (!plc.IsConnected || (timeoutCts.IsCancellationRequested && !cancellationToken.IsCancellationRequested))
            {
                throw new TimeoutException($"S7 连接超过 {timeoutMs} ms 未完成：{settings.Host}:{port}");
            }

            lock (_gate)
            {
                _plc = plc;
                _timeoutMs = timeoutMs;
            }

            _logger.LogInformation(
                "PLC 已连接 {Plc} cpu={Cpu} handshake={Handshake} rack={Rack} slot={Slot} port={Port}",
                LogFormatting.DescribePlc(settings),
                settings.CpuType,
                handshake.Cpu,
                handshake.Rack,
                handshake.Slot,
                port);
        }
        catch (Exception ex)
        {
            Release(plc);
            if (ex is OperationCanceledException && cancellationToken.IsCancellationRequested)
            {
                throw;
            }

            var timedOut = timeoutCts.IsCancellationRequested && !cancellationToken.IsCancellationRequested;
            var failure = timedOut
                ? new TimeoutException($"S7 连接超过 {timeoutMs} ms 未完成，已放弃：{settings.Host}:{port}", ex)
                : ex;
            _logger.LogWarning(
                failure,
                "PLC 连接失败 {Plc} cpu={Cpu} handshake={Handshake} rack={Rack} slot={Slot} port={Port}",
                LogFormatting.DescribePlc(settings),
                settings.CpuType,
                handshake.Cpu,
                handshake.Rack,
                handshake.Slot,
                port);

            var networkHint = DescribeNetworkFailure(failure);
            throw new InvalidOperationException(
                $"S7 连接失败：{failure.Message}{networkHint}（CPU={settings.CpuType}→{handshake.Cpu}，{settings.Host}:{port}，rack={handshake.Rack}，slot={handshake.Slot}。S7-200 SMART 按 S7-1200 握手、rack 0 slot 1；1200/1500 常见 rack 0 slot 0 或 1）",
                failure);
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
        ReadOneAsync(tag, cancellationToken);

    public async Task<IReadOnlyDictionary<string, object?>> ReadTagsAsync(
        IReadOnlyList<PlcTag> tags,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (tags.Count == 0)
        {
            return new Dictionary<string, object?>(StringComparer.Ordinal);
        }

        Plc plc;
        int timeoutMs;
        lock (_gate)
        {
            plc = _plc ?? throw new InvalidOperationException("PLC 未连接");
            timeoutMs = _timeoutMs;
        }

        var result = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var tag in tags)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (tag.IsManual || tag.IsComputed || string.IsNullOrWhiteSpace(tag.Name))
            {
                continue;
            }

            try
            {
                result[tag.Name] = await ReadBoundedAsync(plc, tag, timeoutMs, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex) when (IsLinkFailure(ex))
            {
                Release(plc);
                lock (_gate)
                {
                    if (ReferenceEquals(_plc, plc))
                    {
                        _plc = null;
                    }
                }

                throw new TimeoutException($"S7 读取超过 {timeoutMs} ms 未完成，已断开：{tag.Name}", ex);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    ex,
                    "S7 读取失败 tag={TagName} address={Address}",
                    tag.Name,
                    tag.DisplayAddress);
            }
        }

        return result;
    }

    async Task<object> ReadOneAsync(PlcTag tag, CancellationToken cancellationToken)
    {
        Plc plc;
        int timeoutMs;
        lock (_gate)
        {
            plc = _plc ?? throw new InvalidOperationException("PLC 未连接");
            timeoutMs = _timeoutMs;
        }

        return await ReadBoundedAsync(plc, tag, timeoutMs, cancellationToken).ConfigureAwait(false);
    }

    async Task<object> ReadBoundedAsync(Plc plc, PlcTag tag, int timeoutMs, CancellationToken cancellationToken)
    {
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(timeoutMs);
        using var abort = timeoutCts.Token.Register(() => AbortSocket(plc));
        try
        {
            return await ReadCoreAsync(plc, tag, timeoutCts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException($"S7 读取超过 {timeoutMs} ms 未完成：{tag.Name}");
        }
    }

    static async Task<object> ReadCoreAsync(Plc plc, PlcTag tag, CancellationToken cancellationToken)
    {
        if (!SiemensS7AddressMapper.TryResolve(tag.XinjeAddress, tag.DataType, out var resolved, out var error))
        {
            throw new InvalidOperationException(error);
        }

        var dataType = resolved.IsBit ? TagDataType.Bool : tag.DataType;
        var byteCount = S7ByteConverter.ByteCount(dataType);
        var area = MapArea(resolved.Area);
        var bytes = await plc.ReadBytesAsync(area, resolved.DbNumber, resolved.ByteOffset, byteCount, cancellationToken)
            .ConfigureAwait(false);
        var raw = S7ByteConverter.ToValue(bytes, 0, dataType, tag.ByteOrder, resolved.BitOffset);
        return dataType == TagDataType.Bool ? raw : RegisterConverter.ApplyScale(raw, tag);
    }

    static void AbortSocket(Plc plc)
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        for (var type = plc.GetType(); type is not null; type = type.BaseType)
        {
            foreach (var field in type.GetFields(flags))
            {
                if (!typeof(TcpClient).IsAssignableFrom(field.FieldType))
                {
                    continue;
                }

                try
                {
                    if (field.GetValue(plc) is TcpClient client)
                    {
                        client.Close();
                    }
                }
                catch
                {
                    // 套接字可能已经关掉
                }
            }
        }
    }

    static void Release(Plc plc)
    {
        AbortSocket(plc);
        try
        {
            plc.Close();
        }
        catch
        {
            // 忽略清理时的二次异常
        }
    }

    static bool IsLinkFailure(Exception ex)
    {
        for (Exception? current = ex; current is not null; current = current.InnerException)
        {
            if (current is TimeoutException or IOException or SocketException or ObjectDisposedException)
            {
                return true;
            }
        }

        return false;
    }

    static DataType MapArea(S7MemoryArea area) => area switch
    {
        S7MemoryArea.Input => DataType.Input,
        S7MemoryArea.Output => DataType.Output,
        S7MemoryArea.Memory => DataType.Memory,
        S7MemoryArea.DataBlock => DataType.DataBlock,
        _ => throw new ArgumentOutOfRangeException(nameof(area), area, "不支持的 S7 区域")
    };

    static string DescribeNetworkFailure(Exception ex)
    {
        for (Exception? walk = ex; walk is not null; walk = walk.InnerException)
        {
            if (walk is SocketException socket && socket.ErrorCode == 10060)
            {
                return "。TCP 连接超时：工控机到 PLC 的 102 端口无响应，请核对 PLC 实际 IP、子网、网线/交换机、Windows 防火墙及 PLC 是否 RUN。";
            }
        }

        return string.Empty;
    }

    /// <summary>
    /// S7-200 SMART 的以太网握手与 S7-1200 相同（现场 S7BlockMonitor 已验证）。
    /// <see cref="CpuType.S7200"/> 的 TSAP 会被 SMART 在 COTP 阶段复位（10054）。
    /// 配置里 slot 为 0 时改用 slot 1，与该工具的默认机架槽位一致。
    /// </summary>
    public static (CpuType Cpu, short Rack, short Slot) ResolveHandshake(string? cpuType, int rack, int slot)
    {
        var resolvedRack = (short)Math.Clamp(rack, 0, 7);
        var resolvedSlot = (short)Math.Clamp(slot, 0, 31);
        if (IsS7200Smart(cpuType) && resolvedSlot == 0)
        {
            resolvedSlot = 1;
        }

        return (ParseCpuType(cpuType), resolvedRack, resolvedSlot);
    }

    static bool IsS7200Smart(string? cpuType)
    {
        var text = cpuType?.Trim().ToUpperInvariant().Replace(" ", "", StringComparison.Ordinal).Replace("-", "", StringComparison.Ordinal)
                   ?? string.Empty;
        return text is "S7200SMART";
    }

    static CpuType ParseCpuType(string? cpuType)
    {
        var text = cpuType?.Trim().ToUpperInvariant().Replace(" ", "", StringComparison.Ordinal).Replace("-", "", StringComparison.Ordinal)
                   ?? string.Empty;
        return text switch
        {
            "S7200" => CpuType.S7200,
            "S7200SMART" => CpuType.S71200,
            "S7300" => CpuType.S7300,
            "S7400" => CpuType.S7400,
            "S71500" => CpuType.S71500,
            "S71200" => CpuType.S71200,
            _ => CpuType.S71200
        };
    }

    public async ValueTask DisposeAsync() => await DisconnectAsync().ConfigureAwait(false);
}
