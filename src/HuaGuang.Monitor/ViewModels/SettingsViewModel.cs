using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HuaGuang.Monitor.Models;
using HuaGuang.Monitor.Services;

namespace HuaGuang.Monitor.ViewModels;

public partial class SettingsViewModel : ObservableObject
{
    readonly SettingsStore _store;
    readonly AcquisitionService _acquisition;

    public SettingsViewModel(SettingsStore store, AcquisitionService acquisition)
    {
        _store = store;
        _acquisition = acquisition;
        LoadFrom(_store.Current);
        SelectedLineName = string.IsNullOrWhiteSpace(_store.Current.LineName)
            ? LineCatalog.LineNames[0]
            : _store.Current.LineName;
    }

    public IReadOnlyList<string> LineNames => LineCatalog.LineNames;

    [ObservableProperty] string selectedLineName = "先河热熔胶复合机";
    [ObservableProperty] string deviceId = "先河热熔胶复合机";
    [ObservableProperty] string scanIntervalMs = "1000";
    [ObservableProperty] bool useSimulator = true;

    [ObservableProperty] string plcHost = "192.168.6.10";
    [ObservableProperty] string plcPort = "502";
    [ObservableProperty] string station = "1";
    [ObservableProperty] string plcTimeoutMs = "2000";

    [ObservableProperty] string mqttHost = "127.0.0.1";
    [ObservableProperty] string mqttPort = "1883";
    [ObservableProperty] string mqttClientId = string.Empty;
    [ObservableProperty] string mqttUsername = string.Empty;
    [ObservableProperty] string mqttPassword = string.Empty;
    [ObservableProperty] bool mqttUseTls;
    [ObservableProperty] string mqttQos = "0";
    [ObservableProperty] string mqttTopic = "huaguang/{deviceId}/telemetry";

    [ObservableProperty] string statusMessage = string.Empty;

    public void Reload() => LoadFrom(_store.Current);

    [RelayCommand]
    async Task SaveAsync()
    {
        try
        {
            if (_acquisition.IsRunning)
            {
                StatusMessage = "请先停止采集再保存设置。";
                return;
            }

            var settings = _store.Current;
            settings.DeviceId = string.IsNullOrWhiteSpace(DeviceId) ? SelectedLineName : DeviceId.Trim();
            settings.LineName = SelectedLineName;
            settings.ScanIntervalMs = ParseInt(ScanIntervalMs, 1000, 200, 60_000);
            settings.UseSimulator = UseSimulator;
            settings.Plc.Host = PlcHost.Trim();
            settings.Plc.Port = ParseInt(PlcPort, 502, 1, 65535);
            settings.Plc.Station = (byte)ParseInt(Station, 1, 1, 247);
            settings.Plc.TimeoutMs = ParseInt(PlcTimeoutMs, 2000, 200, 30_000);
            settings.Mqtt.Host = MqttHost.Trim();
            settings.Mqtt.Port = ParseInt(MqttPort, 1883, 1, 65535);
            settings.Mqtt.ClientId = MqttClientId.Trim();
            settings.Mqtt.Username = MqttUsername.Trim();
            settings.Mqtt.Password = MqttPassword;
            settings.Mqtt.UseTls = MqttUseTls;
            settings.Mqtt.Qos = ParseInt(MqttQos, 0, 0, 2);
            settings.Mqtt.Topic = string.IsNullOrWhiteSpace(MqttTopic)
                ? "huaguang/{deviceId}/telemetry"
                : MqttTopic.Trim();

            await _store.SaveAsync(settings);
            StatusMessage = "设置已保存。";
        }
        catch (Exception ex)
        {
            StatusMessage = $"保存失败：{ex.Message}";
        }
    }

    [RelayCommand]
    async Task ApplyLineAsync()
    {
        if (_acquisition.IsRunning)
        {
            StatusMessage = "请先停止采集再切换产线点表。";
            return;
        }

        var settings = _store.Current;
        LineCatalog.Apply(settings, SelectedLineName);
        PlcHost = settings.Plc.Host;
        DeviceId = settings.DeviceId;
        await _store.SaveAsync(settings);
        StatusMessage = $"已载入《华光数据地址规划》：{settings.LineName}，共 {settings.Tags.Count} 个机台点位。";
    }

    void LoadFrom(AppSettings settings)
    {
        DeviceId = settings.DeviceId;
        SelectedLineName = string.IsNullOrWhiteSpace(settings.LineName)
            ? LineCatalog.LineNames[0]
            : settings.LineName;
        ScanIntervalMs = settings.ScanIntervalMs.ToString();
        UseSimulator = settings.UseSimulator;
        PlcHost = settings.Plc.Host;
        PlcPort = settings.Plc.Port.ToString();
        Station = settings.Plc.Station.ToString();
        PlcTimeoutMs = settings.Plc.TimeoutMs.ToString();
        MqttHost = settings.Mqtt.Host;
        MqttPort = settings.Mqtt.Port.ToString();
        MqttClientId = settings.Mqtt.ClientId;
        MqttUsername = settings.Mqtt.Username;
        MqttPassword = settings.Mqtt.Password;
        MqttUseTls = settings.Mqtt.UseTls;
        MqttQos = settings.Mqtt.Qos.ToString();
        MqttTopic = settings.Mqtt.Topic;
        StatusMessage = string.Empty;
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
