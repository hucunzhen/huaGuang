using HuaGuang.Monitor.Ipc;
using HuaGuang.Monitor.Services;

AppPaths.Configure(new WindowsAppDataPaths());

Console.WriteLine($"DataDir: {AppPaths.UserDataDirectory}");
Console.WriteLine("Waiting for IPC service...");

if (!MonitorIpcClient.WaitForServiceAvailable(TimeSpan.FromSeconds(15)))
{
    Console.Error.WriteLine($"FAIL: IPC unavailable: {MonitorIpcClient.DescribeConnectionFailure()}");
    return 1;
}

Console.WriteLine("OK: Ping via IsServiceAvailable");

var client = new MonitorIpcClient();
var ping = await client.SendAsync(new MonitorIpcRequest { Command = MonitorIpcCommand.Ping });
Console.WriteLine($"Ping: success={ping.Success} error={ping.Error ?? "(none)"} running={ping.State?.IsRunning}");

var status = await client.SendAsync(new MonitorIpcRequest { Command = MonitorIpcCommand.GetStatus });
Console.WriteLine($"GetStatus: success={status.Success} snapshots={status.State?.Snapshots.Count ?? 0}");

var start = await client.SendAsync(
    new MonitorIpcRequest
    {
        Command = MonitorIpcCommand.Start,
        OperationMode = "Acquisition"
    },
    MonitorIpcClient.CommandTimeout);
Console.WriteLine($"Start: success={start.Success} error={start.Error ?? "(none)"} running={start.State?.IsRunning}");

if (!start.Success || start.State?.IsRunning != true)
{
    Console.Error.WriteLine("FAIL: Start acquisition via IPC");
    return 2;
}

await Task.Delay(6000);

var live = await client.SendAsync(new MonitorIpcRequest { Command = MonitorIpcCommand.GetStatus });
Console.WriteLine($"Live: snapshots={live.State?.Snapshots.Count} mqtt={live.State?.MqttConnected} simulatorMode={live.State?.LastError ?? "(none)"}");
foreach (var snapshot in live.State?.Snapshots.Take(5) ?? [])
{
    Console.WriteLine($"  {snapshot.Name}={snapshot.Value}");
}

var parallel = Enumerable.Range(0, 10)
    .Select(_ => client.SendAsync(new MonitorIpcRequest { Command = MonitorIpcCommand.GetStatus }))
    .ToArray();
await Task.WhenAll(parallel);
Console.WriteLine($"OK: {parallel.Length} parallel GetStatus calls");

var stop = await client.SendAsync(
    new MonitorIpcRequest { Command = MonitorIpcCommand.Stop },
    MonitorIpcClient.CommandTimeout);
Console.WriteLine($"Stop: success={stop.Success} running={stop.State?.IsRunning}");

Console.WriteLine("ALL IPC SMOKE TESTS PASSED");
return 0;
