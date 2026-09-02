using HuaGuang.Monitor.Models;
using HuaGuang.Monitor.Services;
using MQTTnet;
using MQTTnet.Protocol;

namespace HuaGuang.Monitor.Messaging;

static class MqttConnectionFactory
{
    public static MqttClientOptions BuildOptions(MqttSettings settings, string? lineName = null)
    {
        var clientId = LineMqttDefaults.ResolveClientId(settings, lineName);

        var builder = new MqttClientOptionsBuilder()
            .WithTcpServer(settings.Host, settings.Port)
            .WithClientId(clientId)
            .WithCleanSession()
            .WithTimeout(MqttTimeouts.Connect)
            .WithKeepAlivePeriod(TimeSpan.FromSeconds(30))
            .WithProtocolVersion(MQTTnet.Formatter.MqttProtocolVersion.V311);

        var (username, password) = LineMqttDefaults.ResolveCredentials(settings);
        if (!string.IsNullOrWhiteSpace(username))
        {
            builder.WithCredentials(username, password);
        }

        if (settings.UseTls)
        {
            builder.WithTlsOptions(tls => tls.UseTls());
        }

        return builder.Build();
    }

    public static async Task ConnectClientAsync(
        IMqttClient client,
        MqttClientOptions options,
        MqttSettings settings,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(timeout);

        MqttClientConnectResult result;
        try
        {
            result = await client.ConnectAsync(options, timeoutCts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException(
                $"MQTT 连接超时（{timeout.TotalSeconds:G} 秒）：{settings.Host}:{settings.Port}");
        }

        if (result.ResultCode == MqttClientConnectResultCode.Success)
        {
            return;
        }

        var (username, _) = LineMqttDefaults.ResolveCredentials(settings);
        var userHint = string.IsNullOrWhiteSpace(username) ? "未配置用户名" : username;
        var clientHint = string.IsNullOrWhiteSpace(settings.ClientId) ? "未配置 ClientId" : settings.ClientId.Trim();
        var detail = string.IsNullOrWhiteSpace(result.ReasonString)
            ? result.ResultCode.ToString()
            : $"{result.ResultCode} — {result.ReasonString}";
        throw new InvalidOperationException(
            $"MQTT 连接失败：{detail}（{settings.Host}:{settings.Port}，ClientId {clientHint}，账号 {userHint}）");
    }
}
