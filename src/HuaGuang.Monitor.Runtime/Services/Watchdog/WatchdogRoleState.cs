namespace HuaGuang.Monitor.Services.Watchdog;

public sealed class WatchdogRoleState
{
    public string Role { get; set; } = string.Empty;
    public int ProcessId { get; set; }
    public DateTimeOffset LastHeartbeatUtc { get; set; }
    public DateTimeOffset? GracefulShutdownUtc { get; set; }
}
