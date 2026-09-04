using System.Net.Sockets;
using System.Text;
using HuaGuang.Monitor.Services;
using MQTTnet;
using MQTTnet.Protocol;

var line = "xianhe";
var publishTest = true;
var timeoutSeconds = 5;

for (var i = 0; i < args.Length; i++)
{
    switch (args[i])
    {
        case "--line" when i + 1 < args.Length:
            line = args[++i];
            break;
        case "--no-publish":
            publishTest = false;
            break;
        case "--timeout" when i + 1 < args.Length && int.TryParse(args[i + 1], out var seconds):
            timeoutSeconds = Math.Clamp(seconds, 2, 30);
            i++;
            break;
        case "--help":
            PrintHelp();
            return 0;
    }
}

var host = LineMqttDefaults.Host;
var port = LineMqttDefaults.Port;
var username = LineMqttDefaults.Username;
var password = LineMqttDefaults.Password;
var topic = line.Equals("huadi", StringComparison.OrdinalIgnoreCase)
    ? LineMqttDefaults.HuadiPublishTopic
    : LineMqttDefaults.XianhePublishTopic;

Console.OutputEncoding = Encoding.UTF8;
Console.WriteLine("=== MQTT 连接测试 ===");
Console.WriteLine($"Broker : {host}:{port}");
Console.WriteLine($"账号   : {username}");
Console.WriteLine($"主题   : {topic}");
Console.WriteLine($"超时   : {timeoutSeconds}s");
Console.WriteLine();

var exitCode = 0;

Console.Write("[1/3] TCP 端口 … ");
if (await TryTcpConnectAsync(host, port, TimeSpan.FromSeconds(timeoutSeconds)))
{
    Console.WriteLine("OK");
}
else
{
    Console.WriteLine("失败");
    Console.WriteLine("  → 检查 IP/端口、防火墙、是否在同一网段。");
    exitCode = 1;
}

Console.Write("[2/3] MQTT 登录 … ");
try
{
    using var client = new MqttClientFactory().CreateMqttClient();
    using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(timeoutSeconds));

    var options = new MqttClientOptionsBuilder()
        .WithTcpServer(host, port)
        .WithClientId($"mqtt-test-{Guid.NewGuid():N}"[..22])
        .WithCredentials(username, password)
        .WithCleanSession()
        .WithTimeout(TimeSpan.FromSeconds(timeoutSeconds))
        .WithKeepAlivePeriod(TimeSpan.FromSeconds(30))
        .WithProtocolVersion(MQTTnet.Formatter.MqttProtocolVersion.V311)
        .Build();

    var connectResult = await client.ConnectAsync(options, timeoutCts.Token);
    if (connectResult.ResultCode != MqttClientConnectResultCode.Success)
    {
        Console.WriteLine($"失败 ({connectResult.ResultCode})");
        if (!string.IsNullOrWhiteSpace(connectResult.ReasonString))
        {
            Console.WriteLine($"  → {connectResult.ReasonString}");
        }

        exitCode = 1;
    }
    else
    {
        Console.WriteLine("OK");

        if (publishTest)
        {
            Console.Write("[3/3] 发布测试报文 … ");
            var payload = $@"{{""properties"":{{""mqtt_test"":1,""line"":""{line}""}}}}";
            var message = new MqttApplicationMessageBuilder()
                .WithTopic(topic)
                .WithPayload(payload)
                .WithQualityOfServiceLevel(MqttQualityOfServiceLevel.AtMostOnce)
                .Build();

            await client.PublishAsync(message, timeoutCts.Token);
            Console.WriteLine("OK");
            Console.WriteLine($"  载荷: {payload}");
        }
        else
        {
            Console.WriteLine("[3/3] 跳过发布（--no-publish）");
        }

        await client.DisconnectAsync();
    }
}
catch (Exception ex)
{
    Console.WriteLine("失败");
    Console.WriteLine($"  → {ex.Message}");
    exitCode = 1;
}

Console.WriteLine();
Console.WriteLine(exitCode == 0 ? "结果: 全部通过" : "结果: 存在失败项");
return exitCode;

static async Task<bool> TryTcpConnectAsync(string host, int port, TimeSpan timeout)
{
    using var client = new TcpClient();
    using var timeoutCts = new CancellationTokenSource(timeout);
    try
    {
        await client.ConnectAsync(host, port, timeoutCts.Token);
        return true;
    }
    catch
    {
        return false;
    }
}

static void PrintHelp()
{
    Console.WriteLine("""
        MQTT 连接测试（默认账号来自 LineMqttDefaults.cs）

        用法:
          dotnet run --project tools/MqttConnectionTest -- [选项]

        选项:
          --line xianhe|huadi   测试主题（默认 xianhe）
          --no-publish          仅测连接，不发测试报文
          --timeout <秒>        超时，2–30（默认 5）
          --help                显示帮助
        """);
}
