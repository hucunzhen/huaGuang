#if WINDOWS
using HuaGuang.Monitor.Ipc;

namespace HuaGuang.Monitor.Services;

public static class RuntimeSettingsSync
{
    public static async Task ReloadBackgroundServiceAsync(CancellationToken cancellationToken = default)
    {
        if (!MonitorIpcClient.IsServiceAvailable())
        {
            throw new InvalidOperationException(
                $"后台采集服务 IPC 不可用：{MonitorIpcClient.DescribeConnectionFailure()}");
        }

        var response = await new MonitorIpcClient().SendAsync(
            new MonitorIpcRequest { Command = MonitorIpcCommand.ReloadSettings },
            MonitorIpcClient.CommandTimeout,
            cancellationToken).ConfigureAwait(false);
        if (!response.Success)
        {
            throw new InvalidOperationException(response.Error ?? "后台服务配置同步失败。");
        }
    }
}
#else
namespace HuaGuang.Monitor.Services;

public static class RuntimeSettingsSync
{
    public static Task ReloadBackgroundServiceAsync(CancellationToken cancellationToken = default) =>
        Task.CompletedTask;
}
#endif
