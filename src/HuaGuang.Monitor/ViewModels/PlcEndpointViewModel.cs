using CommunityToolkit.Mvvm.ComponentModel;
using HuaGuang.Monitor.Models;
using HuaGuang.Monitor.Services;

namespace HuaGuang.Monitor.ViewModels;

public partial class PlcEndpointViewModel : ObservableObject
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    [ObservableProperty] string name = "PLC 1";
    [ObservableProperty] bool enabled = true;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsS7Plc))]
    [NotifyPropertyChangedFor(nameof(IsModbusPlc))]
    string selectedProtocol = "Modbus TCP";
    [ObservableProperty] string model = "XD5E-60T10";
    [ObservableProperty] string host = "192.168.6.10";
    [ObservableProperty] string port = "502";
    [ObservableProperty] string station = "1";
    [ObservableProperty] string rack = "0";
    [ObservableProperty] string slot = "0";
    [ObservableProperty] string cpuType = "S71200";
    [ObservableProperty] string timeoutMs = "2000";

    public string[] ProtocolOptions { get; } = ["Modbus TCP", "西门子 S7"];
    public string[] S7CpuTypeOptions { get; } = ["S71200", "S71500", "S7300", "S7400", "S7200Smart"];
    public bool IsS7Plc => SelectedProtocol == "西门子 S7";
    public bool IsModbusPlc => !IsS7Plc;

    partial void OnSelectedProtocolChanged(string value)
    {
        if (IsS7Plc && (Port is "502" or ""))
        {
            Port = "102";
            if (string.IsNullOrWhiteSpace(Model) || Model.Contains("XD", StringComparison.OrdinalIgnoreCase))
            {
                Model = "S7-1200";
            }

            return;
        }

        if (IsModbusPlc && (Port is "102" or ""))
        {
            Port = "502";
            if (string.IsNullOrWhiteSpace(Model) || Model.Contains("S7", StringComparison.OrdinalIgnoreCase))
            {
                Model = "XD5E-60T10";
            }
        }
    }

    public static PlcEndpointViewModel FromModel(PlcEndpoint endpoint) => new()
    {
        Id = endpoint.Id,
        Name = endpoint.Name,
        Enabled = endpoint.Enabled,
        SelectedProtocol = PlcSettingsHelper.FormatProtocol(endpoint.Protocol),
        Model = endpoint.Model,
        Host = endpoint.Host,
        Port = endpoint.Port.ToString(),
        Station = endpoint.Station.ToString(),
        Rack = endpoint.Rack.ToString(),
        Slot = endpoint.Slot.ToString(),
        CpuType = endpoint.CpuType,
        TimeoutMs = endpoint.TimeoutMs.ToString()
    };

    public PlcEndpoint ToModel()
    {
        var protocol = SelectedProtocol == "西门子 S7" ? PlcProtocol.S7 : PlcProtocol.ModbusTcp;
        var cpu = string.IsNullOrWhiteSpace(CpuType) ? "S71200" : CpuType.Trim();
        var endpoint = new PlcEndpoint
        {
            Id = string.IsNullOrWhiteSpace(Id) ? Guid.NewGuid().ToString("N") : Id,
            Name = string.IsNullOrWhiteSpace(Name) ? "PLC" : Name.Trim(),
            Enabled = Enabled,
            Protocol = protocol,
            Model = string.IsNullOrWhiteSpace(Model)
                ? protocol == PlcProtocol.S7 ? "S7-1200" : "XD5E-60T10"
                : Model.Trim(),
            Host = Host.Trim(),
            Port = ParseInt(Port, protocol == PlcProtocol.S7 ? 102 : 502, 1, 65535),
            Station = (byte)ParseInt(Station, 1, 1, 247),
            CpuType = cpu,
            Rack = ParseInt(Rack, 0, 0, 7),
            Slot = ParseInt(Slot, PlcSettingsHelper.RecommendedDefaultSlot(cpu), 0, 31),
            TimeoutMs = ParseInt(TimeoutMs, 2000, 200, 10_000)
        };
        var normalized = endpoint.ToSettings();
        endpoint.ApplyTo(normalized);
        return endpoint;
    }

    static int ParseInt(string text, int fallback, int min, int max)
    {
        if (!int.TryParse(text, out var value))
        {
            value = fallback;
        }

        return Math.Clamp(value, min, max);
    }
}
