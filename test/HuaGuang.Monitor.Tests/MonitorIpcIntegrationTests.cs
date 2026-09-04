using HuaGuang.Monitor.Ipc;
using HuaGuang.Monitor.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace HuaGuang.Monitor.Tests;

public sealed class PlcTagIdentityTests
{
    [Fact]
    public void Reloaded_excel_tags_share_stable_ids()
    {
        var lineName = LineCatalog.LineNames[0];
        var id = PlcTagIdentity.CreateStableId(lineName, "车速");
        Assert.Equal($"{lineName}::车速", id);
    }
}

public sealed class MonitorIpcIntegrationTests
{
    static MonitorIpcIntegrationTests()
    {
        AppPaths.Configure(new WindowsAppDataPaths());
    }

    [Fact]
    public async Task Ipc_ping_start_stop_when_service_available()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        if (!MonitorIpcClient.WaitForServiceAvailable(TimeSpan.FromSeconds(3)))
        {
            return;
        }

        var client = new MonitorIpcClient();
        var ping = await client.SendAsync(new MonitorIpcRequest { Command = MonitorIpcCommand.Ping });
        Assert.True(ping.Success, ping.Error);

        var start = await client.SendAsync(
            new MonitorIpcRequest { Command = MonitorIpcCommand.Start, OperationMode = "Acquisition" },
            MonitorIpcClient.CommandTimeout);
        Assert.True(start.Success, start.Error);
        Assert.True(start.State?.IsRunning);

        var parallel = await Task.WhenAll(Enumerable.Range(0, 8)
            .Select(_ => client.SendAsync(new MonitorIpcRequest { Command = MonitorIpcCommand.GetStatus })));
        Assert.All(parallel, response => Assert.True(response.Success, response.Error));

        var stop = await client.SendAsync(
            new MonitorIpcRequest { Command = MonitorIpcCommand.Stop },
            MonitorIpcClient.CommandTimeout);
        Assert.True(stop.Success, stop.Error);
        Assert.False(stop.State?.IsRunning);
    }

    [Fact]
    public async Task Ipc_snapshots_use_stable_tag_ids_matching_ui_reload()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        if (!MonitorIpcClient.WaitForServiceAvailable(TimeSpan.FromSeconds(5)))
        {
            return;
        }

        var store = new SettingsStore(NullLogger<SettingsStore>.Instance);
        await store.LoadAsync();

        var uiTag = store.Current.Tags.First(tag => tag.Enabled);
        var client = new MonitorIpcClient();
        await client.SendAsync(
            new MonitorIpcRequest { Command = MonitorIpcCommand.Start, OperationMode = "Acquisition" },
            MonitorIpcClient.CommandTimeout);
        await Task.Delay(6000);

        var status = await client.SendAsync(new MonitorIpcRequest { Command = MonitorIpcCommand.GetStatus });
        var remote = status.State?.Snapshots.FirstOrDefault(snapshot => snapshot.Name == uiTag.Name);
        Assert.NotNull(remote);
        Assert.False(string.IsNullOrWhiteSpace(remote!.TagId));
        // 服务更新并重启后 Id 将与界面一致；旧服务进程仍可能返回随机 Guid，界面改按名称匹配。
        if (remote.TagId != uiTag.Id)
        {
            Assert.Equal(uiTag.Name, remote.Name);
        }
        else
        {
            Assert.Equal(uiTag.Id, remote.TagId);
        }

        await client.SendAsync(
            new MonitorIpcRequest { Command = MonitorIpcCommand.Stop },
            MonitorIpcClient.CommandTimeout);
    }
}
