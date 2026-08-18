namespace HuaGuang.Monitor.Models;

public sealed class AppSettings
{
    public string DeviceId { get; set; } = "先河热熔胶复合机";
    public string LineName { get; set; } = "先河热熔胶复合机";
    public int AddressCatalogVersion { get; set; }
    public int ScanIntervalMs { get; set; } = 1000;
    public bool UseSimulator { get; set; } = true;
    public PlcSettings Plc { get; set; } = new();
    public MqttSettings Mqtt { get; set; } = new();
    public List<PlcTag> Tags { get; set; } = [];
}

public sealed class PlcSettings
{
    public string Model { get; set; } = "XD5E-60T10";
    public string Host { get; set; } = "192.168.6.10";
    public int Port { get; set; } = 502;
    public byte Station { get; set; } = 1;
    public int TimeoutMs { get; set; } = 2000;
}

public sealed class MqttSettings
{
    public string Host { get; set; } = "127.0.0.1";
    public int Port { get; set; } = 1883;
    public string ClientId { get; set; } = string.Empty;
    public string Username { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public bool UseTls { get; set; }
    public int Qos { get; set; }
    public string Topic { get; set; } = "huaguang/{deviceId}/telemetry";
}
