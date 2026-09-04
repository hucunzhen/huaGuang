namespace HuaGuang.Monitor.Services;

/// <summary>
/// 确保 PLC 采集/MQTT 推送在独立后台进程中运行（Windows UI 使用）。
/// </summary>
public interface IBackgroundRuntimeLauncher
{
    bool EnsureRunning(TimeSpan? timeout = null);

    /// <summary>后台服务进程或 Windows 服务是否已在运行。</summary>
    bool IsBackgroundPresent();
}

public sealed class NoOpBackgroundRuntimeLauncher : IBackgroundRuntimeLauncher
{
    public bool EnsureRunning(TimeSpan? timeout = null) => false;

    public bool IsBackgroundPresent() => false;
}
