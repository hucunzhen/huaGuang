using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using HuaGuang.Monitor.Models;
using HuaGuang.Monitor.Services;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace HuaGuang.Monitor.Ipc;

public sealed class MonitorIpcServer : BackgroundService
{
    readonly ILogger<MonitorIpcServer> _logger;
    readonly SettingsStore _settings;
    readonly AcquisitionService _acquisition;
    readonly SubscriptionService _subscription;

    public MonitorIpcServer(
        ILogger<MonitorIpcServer> logger,
        SettingsStore settings,
        AcquisitionService acquisition,
        SubscriptionService subscription)
    {
        _logger = logger;
        _settings = settings;
        _acquisition = acquisition;
        _subscription = subscription;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation(
            "IPC 服务已启动 pipe={PipeName} tcp=127.0.0.1:{TcpPort}",
            MonitorIpcConstants.PipeName,
            MonitorIpcConstants.TcpPort);

        var pipeTask = RunPipeServerAsync(stoppingToken);
        var tcpTask = MonitorIpcTcpTransport.RunServerAsync(DispatchAsync, _logger, stoppingToken);
        await Task.WhenAll(pipeTask, tcpTask).ConfigureAwait(false);
    }

    async Task RunPipeServerAsync(CancellationToken stoppingToken)
    {
        var handlers = new List<Task>();
        while (!stoppingToken.IsCancellationRequested)
        {
            var pipe = MonitorIpcPipeFactory.CreateServerStream();
            try
            {
                await pipe.WaitForConnectionAsync(stoppingToken).ConfigureAwait(false);
                handlers.RemoveAll(static task => task.IsCompleted);
                handlers.Add(HandlePipeClientAsync(pipe, stoppingToken));
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                await pipe.DisposeAsync().ConfigureAwait(false);
                break;
            }
            catch (Exception ex)
            {
                await pipe.DisposeAsync().ConfigureAwait(false);
                _logger.LogWarning(ex, "IPC 管道等待连接失败");
            }
        }

        if (handlers.Count > 0)
        {
            await Task.WhenAll(handlers).ConfigureAwait(false);
        }
    }

    async Task HandlePipeClientAsync(NamedPipeServerStream pipe, CancellationToken stoppingToken)
    {
        try
        {
            await using (pipe)
            {
                await MonitorIpcStreamSession.HandleAsync(pipe, DispatchAsync, stoppingToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "IPC 管道连接处理失败");
        }
    }

    async Task<MonitorIpcResponse> DispatchAsync(MonitorIpcRequest request, CancellationToken cancellationToken)
    {
        _ = cancellationToken;

        switch (request.Command)
        {
            case MonitorIpcCommand.Ping:
                return Ok(new MonitorRuntimeState
                {
                    OperationMode = _settings.Current.OperationMode.ToString(),
                    IsRunning = _acquisition.IsRunning || _subscription.IsRunning
                });

            case MonitorIpcCommand.GetStatus:
                return Ok(BuildState(_settings, _acquisition, _subscription, request.TopicFilter));

            case MonitorIpcCommand.Start:
                await _settings.LoadAsync().ConfigureAwait(false);
                if (ResolveSubscribeStart(request, _settings.Current))
                {
                    if (_acquisition.IsRunning)
                    {
                        await _acquisition.StopAsync().ConfigureAwait(false);
                    }

                    await _subscription.StartAsync().ConfigureAwait(false);
                }
                else
                {
                    if (_subscription.IsRunning)
                    {
                        await _subscription.StopAsync().ConfigureAwait(false);
                    }

                    await _acquisition.StartAsync().ConfigureAwait(false);
                }

                return Ok(BuildState(_settings, _acquisition, _subscription, request.TopicFilter));

            case MonitorIpcCommand.Stop:
                await _acquisition.StopAsync().ConfigureAwait(false);
                await _subscription.StopAsync().ConfigureAwait(false);
                return Ok(BuildState(_settings, _acquisition, _subscription, request.TopicFilter));

            case MonitorIpcCommand.ReloadSettings:
                await _settings.LoadAsync().ConfigureAwait(false);
                return Ok(BuildState(_settings, _acquisition, _subscription, request.TopicFilter));

            case MonitorIpcCommand.RequestPublish:
                _acquisition.RequestImmediatePublish();
                return Ok(BuildState(_settings, _acquisition, _subscription, request.TopicFilter));

            case MonitorIpcCommand.RefreshTopics:
                await _subscription.RefreshTopicsAsync().ConfigureAwait(false);
                return Ok(BuildState(_settings, _acquisition, _subscription, request.TopicFilter));

            case MonitorIpcCommand.InjectTelemetry:
                _subscription.InjectTelemetry(request.Topic ?? string.Empty, request.Payload ?? string.Empty);
                return Ok(BuildState(_settings, _acquisition, _subscription, request.TopicFilter));

            default:
                return new MonitorIpcResponse { Success = false, Error = $"未知命令 {request.Command}" };
        }
    }

    static MonitorIpcResponse Ok(MonitorRuntimeState state) =>
        new() { Success = true, State = state };

    static bool ResolveSubscribeStart(MonitorIpcRequest request, AppSettings settings)
    {
        if (!string.IsNullOrWhiteSpace(request.OperationMode) &&
            Enum.TryParse<AppOperationMode>(request.OperationMode, ignoreCase: true, out var requested))
        {
            return requested == AppOperationMode.Subscribe;
        }

        return settings.OperationMode == AppOperationMode.Subscribe;
    }

    static bool ResolveSubscribeStatus(AppSettings settings, AcquisitionService acquisition, SubscriptionService subscription)
    {
        if (subscription.IsRunning)
        {
            return true;
        }

        if (acquisition.IsRunning)
        {
            return false;
        }

        return settings.OperationMode == AppOperationMode.Subscribe;
    }

    static MonitorRuntimeState BuildState(
        SettingsStore settings,
        AcquisitionService acquisition,
        SubscriptionService subscription,
        string? topicFilter)
    {
        var isSubscribe = ResolveSubscribeStatus(settings.Current, acquisition, subscription);
        var state = new MonitorRuntimeState
        {
            OperationMode = settings.Current.OperationMode.ToString(),
            IsRunning = isSubscribe ? subscription.IsRunning : acquisition.IsRunning,
            PlcConnected = isSubscribe ? false : acquisition.PlcConnected,
            MqttConnected = isSubscribe ? subscription.IsConnected : acquisition.MqttConnected,
            LastError = isSubscribe ? subscription.LastError : acquisition.LastError,
            LastPayload = isSubscribe ? subscription.LastPayload : acquisition.LastPayload,
            ActiveSubscribeTopics = subscription.ActiveSubscribeTopics
        };

        if (isSubscribe)
        {
            state.Devices = subscription.GetDevices(topicFilter)
                .Select(ToDto)
                .ToList();
            return state;
        }

        state.MqttPendingCount = acquisition.MqttPendingCount;
        state.LastPublishNote = acquisition.LastPublishNote;
        state.LastPlcElapsedMs = acquisition.LastPlcElapsedMs;
        state.LastWaitElapsedMs = acquisition.LastWaitElapsedMs;
        state.ActiveScanIntervalMs = acquisition.ActiveScanIntervalMs;
        state.CycleCount = acquisition.CycleCount;
        state.LastCycleCompletedAt = acquisition.LastCycleCompletedAt;
        state.LastPublishTime = acquisition.LastPublishTime;
        state.Snapshots = acquisition.LastSnapshots.Values
            .Select(snapshot => new TagSnapshotState
            {
                TagId = snapshot.TagId,
                Name = snapshot.Name,
                Unit = snapshot.Unit,
                Value = snapshot.Value,
                Quality = snapshot.Quality,
                Timestamp = snapshot.Timestamp
            })
            .ToList();
        return state;
    }

    static RemoteDeviceStateDto ToDto(RemoteDeviceState device) => new()
    {
        DeviceKey = device.DeviceKey,
        DeviceId = device.DeviceId,
        SourceTopic = device.SourceTopic,
        Timestamp = device.Timestamp,
        Quality = device.Quality,
        PlcHost = device.PlcHost,
        Simulator = device.Simulator,
        Tags = new Dictionary<string, object?>(device.Tags, StringComparer.Ordinal),
        ReceivedAt = device.ReceivedAt
    };
}
