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
        _logger.LogInformation("IPC 服务已启动 pipe={PipeName}", MonitorIpcConstants.PipeName);
        while (!stoppingToken.IsCancellationRequested)
        {
            await using var pipe = new NamedPipeServerStream(
                MonitorIpcConstants.PipeName,
                PipeDirection.InOut,
                NamedPipeServerStream.MaxAllowedServerInstances,
                PipeTransmissionMode.Byte,
                PipeOptions.Asynchronous);

            try
            {
                await pipe.WaitForConnectionAsync(stoppingToken).ConfigureAwait(false);
                await HandleClientAsync(pipe, stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "IPC 连接处理失败");
            }
        }
    }

    async Task HandleClientAsync(NamedPipeServerStream pipe, CancellationToken cancellationToken)
    {
        using var reader = new StreamReader(pipe, Encoding.UTF8, leaveOpen: true);
        var requestLine = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(requestLine))
        {
            return;
        }

        MonitorIpcResponse response;
        try
        {
            var request = JsonSerializer.Deserialize<MonitorIpcRequest>(requestLine, MonitorIpcJson.Options)
                ?? throw new InvalidOperationException("无效请求");
            response = await DispatchAsync(request, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            response = new MonitorIpcResponse { Success = false, Error = ex.Message };
        }

        var responseLine = JsonSerializer.Serialize(response, MonitorIpcJson.Options) + "\n";
        var bytes = Encoding.UTF8.GetBytes(responseLine);
        await pipe.WriteAsync(bytes, cancellationToken).ConfigureAwait(false);
    }

    async Task<MonitorIpcResponse> DispatchAsync(MonitorIpcRequest request, CancellationToken cancellationToken)
    {
        _ = cancellationToken;

        switch (request.Command)
        {
            case MonitorIpcCommand.Ping:
                return Ok(BuildState(_settings, _acquisition, _subscription, request.TopicFilter));

            case MonitorIpcCommand.GetStatus:
                return Ok(BuildState(_settings, _acquisition, _subscription, request.TopicFilter));

            case MonitorIpcCommand.Start:
                if (_settings.Current.OperationMode == AppOperationMode.Subscribe)
                {
                    await _subscription.StartAsync().ConfigureAwait(false);
                }
                else
                {
                    await _acquisition.StartAsync().ConfigureAwait(false);
                }

                return Ok(BuildState(_settings, _acquisition, _subscription, request.TopicFilter));

            case MonitorIpcCommand.Stop:
                if (_settings.Current.OperationMode == AppOperationMode.Subscribe)
                {
                    await _subscription.StopAsync().ConfigureAwait(false);
                }
                else
                {
                    await _acquisition.StopAsync().ConfigureAwait(false);
                }

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

    static MonitorRuntimeState BuildState(
        SettingsStore settings,
        AcquisitionService acquisition,
        SubscriptionService subscription,
        string? topicFilter)
    {
        var isSubscribe = settings.Current.OperationMode == AppOperationMode.Subscribe;
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
