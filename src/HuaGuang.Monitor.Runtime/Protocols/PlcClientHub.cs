using HuaGuang.Monitor.Models;
using HuaGuang.Monitor.Services;
using HuaGuang.Monitor.Services.Logging;
using Microsoft.Extensions.Logging;

namespace HuaGuang.Monitor.Protocols;

/// <summary>按产线 PLC 列表维护多条连接，点位按所属 PLC 分别读取。</summary>
public sealed class PlcClientHub : IAsyncDisposable
{
    readonly ILoggerFactory _loggerFactory;
    readonly ILogger<PlcClientHub> _logger;
    readonly object _gate = new();
    readonly Dictionary<string, Session> _sessions = new(StringComparer.Ordinal);

    public PlcClientHub(ILoggerFactory loggerFactory, ILogger<PlcClientHub> logger)
    {
        _loggerFactory = loggerFactory;
        _logger = logger;
    }

    public bool IsConnected
    {
        get
        {
            lock (_gate)
            {
                return _sessions.Values.Any(session => session.Client.IsConnected);
            }
        }
    }

    public string LastError
    {
        get
        {
            lock (_gate)
            {
                var errors = _sessions.Values
                    .Select(session => session.LastError)
                    .Where(error => !string.IsNullOrWhiteSpace(error))
                    .ToList();
                return errors.Count == 0 ? string.Empty : string.Join("；", errors);
            }
        }
    }

    public bool AreUsedConnected(AppSettings settings, IEnumerable<PlcTag> plcTags)
    {
        var used = PlcEndpointCatalog.GetUsedByTags(settings, plcTags);
        if (used.Count == 0)
        {
            return true;
        }

        lock (_gate)
        {
            return used.All(endpoint =>
                _sessions.TryGetValue(endpoint.Id, out var session) && session.Client.IsConnected);
        }
    }

    public async Task DisconnectAsync()
    {
        List<Session> sessions;
        lock (_gate)
        {
            sessions = _sessions.Values.ToList();
            _sessions.Clear();
        }

        foreach (var session in sessions)
        {
            await session.Client.DisposeAsync().ConfigureAwait(false);
        }
    }

    public async Task<bool> EnsureConnectedAsync(
        AppSettings settings,
        IReadOnlyList<PlcTag> plcTags,
        CancellationToken cancellationToken)
    {
        PlcEndpointCatalog.Normalize(settings);
        var used = PlcEndpointCatalog.GetUsedByTags(settings, plcTags);
        if (used.Count == 0)
        {
            await DisconnectAsync().ConfigureAwait(false);
            return true;
        }

        await DropUnusedAsync(used.Select(endpoint => endpoint.Id)).ConfigureAwait(false);

        var anyConnected = false;
        var anyFailed = false;
        foreach (var endpoint in used)
        {
            var session = GetOrCreateSession(endpoint);
            if (session.Client.IsConnected && session.Fingerprint == Fingerprint(endpoint))
            {
                anyConnected = true;
                continue;
            }

            if (DateTimeOffset.UtcNow < session.RetryAfter)
            {
                anyFailed = true;
                continue;
            }

            try
            {
                if (session.Fingerprint != Fingerprint(endpoint))
                {
                    await session.Client.DisposeAsync().ConfigureAwait(false);
                    session = ReplaceSession(endpoint);
                }

                await session.Client.ConnectAsync(endpoint.ToSettings(), cancellationToken).ConfigureAwait(false);
                session.LastError = string.Empty;
                session.RetryAfter = DateTimeOffset.MinValue;
                anyConnected = true;
                _logger.LogInformation(
                    "PLC 已连接 name={Name} {Plc}",
                    endpoint.Name,
                    LogFormatting.DescribePlc(endpoint.ToSettings()));
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                anyFailed = true;
                session.LastError = $"{endpoint.Name}: {ex.Message}";
                session.RetryAfter = DateTimeOffset.UtcNow.AddSeconds(5);
                _logger.LogWarning(
                    ex,
                    "PLC 连接失败 name={Name} {Plc}",
                    endpoint.Name,
                    LogFormatting.DescribePlc(endpoint.ToSettings()));
                try
                {
                    await session.Client.DisconnectAsync().ConfigureAwait(false);
                }
                catch
                {
                }
            }
        }

        return anyConnected || !anyFailed;
    }

    public async Task<IReadOnlyDictionary<string, object?>> ReadTagsAsync(
        AppSettings settings,
        IReadOnlyList<PlcTag> tags,
        CancellationToken cancellationToken)
    {
        var groups = tags
            .GroupBy(tag => PlcEndpointCatalog.Resolve(settings, tag.PlcId).Id, StringComparer.Ordinal)
            .ToList();
        var merged = new Dictionary<string, object?>(StringComparer.Ordinal);
        var reads = new List<Task<(IReadOnlyDictionary<string, object?> Values, string? Error)>>();
        foreach (var group in groups)
        {
            Session? session;
            lock (_gate)
            {
                _sessions.TryGetValue(group.Key, out session);
            }

            if (session is null || !session.Client.IsConnected)
            {
                continue;
            }

            var client = session.Client;
            var batch = group.ToList();
            reads.Add(ReadGroupAsync(client, batch, cancellationToken));
        }

        var results = await Task.WhenAll(reads).ConfigureAwait(false);
        foreach (var (values, error) in results)
        {
            foreach (var pair in values)
            {
                merged[pair.Key] = pair.Value;
            }

            if (!string.IsNullOrWhiteSpace(error))
            {
                _logger.LogWarning("{Error}", error);
            }
        }

        return merged;
    }

    static async Task<(IReadOnlyDictionary<string, object?> Values, string? Error)> ReadGroupAsync(
        IPlcClient client,
        IReadOnlyList<PlcTag> tags,
        CancellationToken cancellationToken)
    {
        try
        {
            var values = await client.ReadTagsAsync(tags, cancellationToken).ConfigureAwait(false);
            return (values, null);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return (new Dictionary<string, object?>(StringComparer.Ordinal), ex.Message);
        }
    }

    Session GetOrCreateSession(PlcEndpoint endpoint)
    {
        lock (_gate)
        {
            if (_sessions.TryGetValue(endpoint.Id, out var existing)
                && existing.Fingerprint == Fingerprint(endpoint))
            {
                existing.Endpoint = endpoint;
                return existing;
            }

            if (existing is not null)
            {
                return existing;
            }

            var session = CreateSession(endpoint);
            _sessions[endpoint.Id] = session;
            return session;
        }
    }

    Session ReplaceSession(PlcEndpoint endpoint)
    {
        lock (_gate)
        {
            var session = CreateSession(endpoint);
            _sessions[endpoint.Id] = session;
            return session;
        }
    }

    Session CreateSession(PlcEndpoint endpoint)
    {
        IPlcClient client = endpoint.Protocol == PlcProtocol.S7
            ? new S7PlcClient(_loggerFactory.CreateLogger<S7PlcClient>())
            : new ModbusTcpPlcClient(_loggerFactory.CreateLogger<ModbusTcpPlcClient>());
        return new Session
        {
            Endpoint = endpoint,
            Client = client,
            Fingerprint = Fingerprint(endpoint)
        };
    }

    async Task DropUnusedAsync(IEnumerable<string> keepIds)
    {
        var keep = keepIds.ToHashSet(StringComparer.Ordinal);
        List<Session> dropped;
        lock (_gate)
        {
            dropped = _sessions
                .Where(pair => !keep.Contains(pair.Key))
                .Select(pair => pair.Value)
                .ToList();
            foreach (var session in dropped)
            {
                _sessions.Remove(session.Endpoint.Id);
            }
        }

        foreach (var session in dropped)
        {
            await session.Client.DisposeAsync().ConfigureAwait(false);
        }
    }

    static string Fingerprint(PlcEndpoint endpoint) =>
        string.Join('|',
            endpoint.Id,
            endpoint.Protocol,
            endpoint.Host.Trim(),
            endpoint.Port,
            endpoint.Station,
            endpoint.Rack,
            endpoint.Slot,
            endpoint.TimeoutMs,
            endpoint.CpuType);

    public async ValueTask DisposeAsync() => await DisconnectAsync().ConfigureAwait(false);

    sealed class Session
    {
        public required PlcEndpoint Endpoint { get; set; }
        public required IPlcClient Client { get; init; }
        public required string Fingerprint { get; init; }
        public DateTimeOffset RetryAfter { get; set; }
        public string LastError { get; set; } = string.Empty;
    }
}
