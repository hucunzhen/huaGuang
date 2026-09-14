using HuaGuang.Monitor.Models;
using HuaGuang.Monitor.Services;
using MQTTnet;
using MQTTnet.Protocol;

namespace HuaGuang.Monitor.Messaging;

static class MqttConnectionFactory
{
    public static MqttClientOptions BuildOptions(MqttSettings settings, string? lineName = null)
    {
        _ = lineName;
        var clientId = settings.ClientId?.Trim() ?? string.Empty;
        var username = settings.Username?.Trim() ?? string.Empty;
        var password = MqttCredentialNormalizer.NormalizePassword(settings.Password);

        var builder = new MqttClientOptionsBuilder()
            .WithTcpServer(settings.Host, settings.Port)
            .WithClientId(clientId)
            .WithCleanSession()
            .WithTimeout(MqttTimeouts.Connect)
            .WithKeepAlivePeriod(TimeSpan.FromSeconds(30))
            .WithProtocolVersion(MQTTnet.Formatter.MqttProtocolVersion.V311);

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

        var userHint = string.IsNullOrWhiteSpace(settings.Username) ? "未配置用户名" : settings.Username.Trim();
        var tlsHint = settings.UseTls ? "TLS" : "明文";
        var clientHint = string.IsNullOrWhiteSpace(settings.ClientId) ? "未配置 ClientId" : settings.ClientId.Trim();
        var detail = string.IsNullOrWhiteSpace(result.ReasonString)
            ? result.ResultCode.ToString()
            : $"{result.ResultCode} — {result.ReasonString}";
        var pwdLen = MqttCredentialNormalizer.NormalizePassword(settings.Password).Length;
        var authHint = result.ResultCode == MqttClientConnectResultCode.NotAuthorized
            ? " Broker 拒绝了用户名/密码/ClientId 组合；请核对「MQTT目标」该行与平台开户信息一致（含 TLS、ClientId 是否绑定账号），并确认现场运行时 Excel 与本地测试文件相同。"
            : string.Empty;
        throw new InvalidOperationException(
            $"MQTT 连接失败：{detail}（{settings.Host}:{settings.Port}，ClientId {clientHint}，账号 {userHint}，passwordLength={pwdLen}，{tlsHint}）{authHint}");
    }
}
