using HuaGuang.Monitor.Services;

namespace HuaGuang.Monitor.Models;

/// <summary>一条产线上的一台 PLC（协议、地址可与其它台不同）。</summary>
public sealed class PlcEndpoint
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "PLC 1";
    public bool Enabled { get; set; } = true;
    public PlcProtocol Protocol { get; set; } = PlcProtocol.ModbusTcp;
    public string Model { get; set; } = "XD5E-60T10";
    public string Host { get; set; } = "192.168.6.10";
    public int Port { get; set; } = 502;
    public byte Station { get; set; } = 1;
    public int TimeoutMs { get; set; } = 2000;
    public int Rack { get; set; }
    public int Slot { get; set; }
    public string CpuType { get; set; } = "S71200";

    public PlcSettings ToSettings()
    {
        var settings = new PlcSettings
        {
            Protocol = Protocol,
            Model = Model,
            Host = Host.Trim(),
            Port = Port,
            Station = Station,
            TimeoutMs = TimeoutMs,
            Rack = Rack,
            Slot = Slot,
            CpuType = CpuType
        };
        PlcSettingsHelper.Normalize(settings);
        return settings;
    }

    public static PlcEndpoint FromSettings(PlcSettings settings, string? name = null, string? id = null)
    {
        PlcSettingsHelper.Normalize(settings);
        return new PlcEndpoint
        {
            Id = string.IsNullOrWhiteSpace(id) ? Guid.NewGuid().ToString("N") : id,
            Name = string.IsNullOrWhiteSpace(name) ? "PLC 1" : name,
            Enabled = true,
            Protocol = settings.Protocol,
            Model = settings.Model,
            Host = settings.Host,
            Port = settings.Port,
            Station = settings.Station,
            TimeoutMs = settings.TimeoutMs,
            Rack = settings.Rack,
            Slot = settings.Slot,
            CpuType = settings.CpuType
        };
    }

    public void ApplyTo(PlcSettings settings)
    {
        var normalized = ToSettings();
        settings.Protocol = normalized.Protocol;
        settings.Model = normalized.Model;
        settings.Host = normalized.Host;
        settings.Port = normalized.Port;
        settings.Station = normalized.Station;
        settings.TimeoutMs = normalized.TimeoutMs;
        settings.Rack = normalized.Rack;
        settings.Slot = normalized.Slot;
        settings.CpuType = normalized.CpuType;
    }
}
