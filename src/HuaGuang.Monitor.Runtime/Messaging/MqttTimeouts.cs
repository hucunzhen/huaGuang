namespace HuaGuang.Monitor.Messaging;

internal static class MqttTimeouts
{
    public static readonly TimeSpan Connect = TimeSpan.FromSeconds(10);
    public static readonly TimeSpan Publish = TimeSpan.FromSeconds(5);
}
