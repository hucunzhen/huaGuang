using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HuaGuang.Monitor.Models;
using HuaGuang.Monitor.Services;

namespace HuaGuang.Monitor.ViewModels;

public partial class SettingsViewModel : ObservableObject
{
    readonly SettingsStore _store;
    readonly AcquisitionService _acquisition;
    readonly SubscriptionService _subscription;
    readonly IStartupRegistration _startup;
    readonly DashboardViewModel _dashboard;

    public SettingsViewModel(
        SettingsStore store,
        AcquisitionService acquisition,
        SubscriptionService subscription,
        IStartupRegistration startup,
        DashboardViewModel dashboard)
    {
        _store = store;
        _acquisition = acquisition;
        _subscription = subscription;
        _startup = startup;
        _dashboard = dashboard;
        LoadFrom(_store.Current);
        SelectedLineName = string.IsNullOrWhiteSpace(_store.Current.LineName)
            ? LineCatalog.LineNames[0]
            : _store.Current.LineName;
    }

    public IReadOnlyList<string> LineNames => LineCatalog.LineNames;
    public string[] OperationModes { get; } = ["采集模式", "订阅模式"];
    public ObservableCollection<string> SubscribeTopics { get; } = [];

    [ObservableProperty] string selectedOperationMode = "采集模式";
    [ObservableProperty] string newSubscribeTopic = string.Empty;
    [ObservableProperty] string selectedLineName = "先河热熔胶复合机";
    [ObservableProperty] string deviceId = "先河热熔胶复合机";
    [ObservableProperty] string scanIntervalMs = "60000";
    [ObservableProperty] string temperaturePublishThresholdC = "0";
    [ObservableProperty] string temperaturePrecision = "1";
    [ObservableProperty] bool useSimulator = true;
    [ObservableProperty] bool startWithWindows = true;
    [ObservableProperty] bool autoStartAcquisition = true;
    [ObservableProperty] bool enableHistoryRecording = true;
    [ObservableProperty] string historyRetentionDays = "14";

    public bool StartupSupported => _startup.IsSupported;
    public bool IsSubscribeSettings => SelectedOperationMode == "订阅模式";
    public bool IsAcquisitionSettings => SelectedOperationMode == "采集模式";

    partial void OnSelectedOperationModeChanged(string value)
    {
        OnPropertyChanged(nameof(IsSubscribeSettings));
        OnPropertyChanged(nameof(IsAcquisitionSettings));
    }

    [ObservableProperty] string plcHost = "192.168.6.10";
    [ObservableProperty] string plcPort = "502";
    [ObservableProperty] string station = "1";
    [ObservableProperty] string plcTimeoutMs = "2000";

    [ObservableProperty] string mqttHost = LineMqttDefaults.Host;
    [ObservableProperty] string mqttPort = LineMqttDefaults.Port.ToString();
    [ObservableProperty] string mqttClientId = string.Empty;
    [ObservableProperty] string mqttUsername = LineMqttDefaults.Username;
    [ObservableProperty] string mqttPassword = LineMqttDefaults.Password;
    [ObservableProperty] bool mqttUseTls;
    [ObservableProperty] string mqttQos = "0";
    [ObservableProperty] string mqttTopic = LineMqttDefaults.XianhePublishTopic;

    [ObservableProperty] string statusMessage = string.Empty;

    public string LineExcelPath => LineConfigPaths.GetExcelPath(SelectedLineName);

    partial void OnSelectedLineNameChanged(string value)
    {
        OnPropertyChanged(nameof(LineExcelPath));
        if (IsServiceRunning() || string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        _ = LoadSelectedLineFromExcelSilentAsync();
    }

    async Task LoadSelectedLineFromExcelSilentAsync()
    {
        try
        {
            var path = LineConfigPaths.GetExcelPath(SelectedLineName);
            LineExcelConfigService.EnsureLineFile(path, SelectedLineName);
            var settings = _store.Current;
            settings.LineName = SelectedLineName;
            LineExcelConfigService.Apply(settings, path);
            await _store.SaveAsync(settings);
            LoadFrom(settings);
            _dashboard.Reload();
        }
        catch
        {
            // 切换产线时静默失败，用户可手动点「从 Excel 载入」
        }
    }

    public void Reload() => LoadFrom(_store.Current);

    [RelayCommand]
    void AddSubscribeTopic()
    {
        var topic = NewSubscribeTopic.Trim();
        if (string.IsNullOrWhiteSpace(topic))
        {
            StatusMessage = "请填写订阅主题。";
            return;
        }

        if (SubscribeTopics.Any(existing => existing.Equals(topic, StringComparison.OrdinalIgnoreCase)))
        {
            StatusMessage = "该订阅主题已存在。";
            return;
        }

        SubscribeTopics.Add(topic);
        NewSubscribeTopic = string.Empty;
        StatusMessage = "已添加到列表，保存后生效。";
    }

    [RelayCommand]
    void RemoveSubscribeTopic(string topic)
    {
        if (string.IsNullOrWhiteSpace(topic))
        {
            return;
        }

        var match = SubscribeTopics.FirstOrDefault(existing =>
            existing.Equals(topic, StringComparison.OrdinalIgnoreCase));
        if (match is null)
        {
            return;
        }

        SubscribeTopics.Remove(match);
        StatusMessage = SubscribeTopics.Count == 0
            ? "至少保留一个订阅主题，保存前请再添加。"
            : "已从列表移除，保存后生效。";
    }

    [RelayCommand]
    async Task LoadLineFromExcelAsync()
    {
        if (IsServiceRunning())
        {
            StatusMessage = "请先停止采集/订阅再载入产线配置。";
            return;
        }

        try
        {
            var path = LineConfigPaths.GetExcelPath(SelectedLineName);
            LineExcelConfigService.EnsureLineFile(path, SelectedLineName);

            var settings = _store.Current;
            settings.LineName = SelectedLineName;
            LineExcelConfigService.Apply(settings, path);
            await _store.SaveAsync(settings);
            LoadFrom(settings);
            _dashboard.Reload();
            var plcCount = settings.Tags.Count(t => !t.IsManual);
            var manualCount = settings.Tags.Count(t => t.IsManual);
            StatusMessage = $"已从 Excel 载入：{settings.LineName}，PLC {plcCount} 个，手动 {manualCount} 个。";
        }
        catch (Exception ex)
        {
            StatusMessage = $"载入 Excel 失败：{ex.Message}";
        }
    }

    [RelayCommand]
    async Task SaveLineToExcelAsync()
    {
        if (IsServiceRunning())
        {
            StatusMessage = "请先停止采集/订阅再导出 Excel。";
            return;
        }

        try
        {
            var settings = BuildPendingSettings();
            settings.Tags = _store.Current.Tags;
            settings.MqttPayload = _store.Current.MqttPayload;
            var path = LineConfigPaths.GetExcelPath(SelectedLineName);
            LineExcelConfigService.Export(settings, path);
            StatusMessage = $"已保存到 Excel：{path}";
            await Task.CompletedTask;
        }
        catch (Exception ex)
        {
            StatusMessage = $"导出 Excel 失败：{ex.Message}";
        }
    }

    [RelayCommand]
    async Task ImportLineExcelFromFileAsync()
    {
        if (IsServiceRunning())
        {
            StatusMessage = "请先停止采集/订阅再导入 Excel。";
            return;
        }

        try
        {
            var file = await FilePicker.Default.PickAsync(new PickOptions
            {
                PickerTitle = "选择产线 Excel 配置",
                FileTypes = ExcelFileType
            });
            if (file is null)
            {
                return;
            }

            var settings = _store.Current;
            await using var stream = await file.OpenReadAsync();
            var tempPath = Path.Combine(Path.GetTempPath(), $"line-import-{Guid.NewGuid():N}.xlsx");
            await using (var fileStream = File.Create(tempPath))
            {
                await stream.CopyToAsync(fileStream);
            }

            var destPath = LineConfigPaths.GetExcelPath(SelectedLineName);
            LineExcelConfigService.ImportToLineFile(tempPath, destPath, SelectedLineName);
            File.Delete(tempPath);

            settings.LineName = SelectedLineName;
            LineExcelConfigService.Apply(settings, destPath);
            await _store.SaveAsync(settings);
            LoadFrom(settings);
            _dashboard.Reload();
            StatusMessage = $"已导入并保存到产线文件：{destPath}";
        }
        catch (Exception ex)
        {
            StatusMessage = $"导入 Excel 失败：{ex.Message}";
        }
    }

    static FilePickerFileType ExcelFileType { get; } = new(new Dictionary<DevicePlatform, IEnumerable<string>>
    {
        { DevicePlatform.WinUI, [".xlsx"] },
        { DevicePlatform.Android, ["application/vnd.openxmlformats-officedocument.spreadsheetml.sheet"] },
        { DevicePlatform.iOS, ["org.openxmlformats.spreadsheetml.sheet"] },
        { DevicePlatform.MacCatalyst, ["org.openxmlformats.spreadsheetml.sheet"] },
    });

    AppSettings BuildPendingSettings()
    {
        var settings = _store.Current;
        settings.LineName = SelectedLineName;
        settings.DeviceId = string.IsNullOrWhiteSpace(DeviceId) ? SelectedLineName : DeviceId.Trim();
        settings.ScanIntervalMs = ParseInt(ScanIntervalMs, 60_000, 200, 60_000);
        settings.TemperaturePublishThresholdC = ParseDouble(TemperaturePublishThresholdC, 0, 0, 100);
        settings.TemperaturePrecision = ParseInt(TemperaturePrecision, 1, 0, 4);
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
            ? "monitor/{deviceId}/telemetry"
            : MqttTopic.Trim();
        return settings;
    }

    [RelayCommand]
    async Task SaveAsync()
    {
        try
        {
            var running = IsServiceRunning();

            var settings = _store.Current;
            settings.OperationMode = SelectedOperationMode == "订阅模式"
                ? AppOperationMode.Subscribe
                : AppOperationMode.Acquisition;

            var topics = SubscribeTopicHelper.NormalizeTopics(SubscribeTopics).ToList();
            if (settings.OperationMode == AppOperationMode.Subscribe && topics.Count == 0)
            {
                StatusMessage = "订阅模式请至少保留一个主题。";
                return;
            }

            settings.SubscribeTopics = topics;
            settings.SubscribeTopic = topics.Count > 0 ? topics[0] : "monitor/+/telemetry";
            settings.DeviceId = string.IsNullOrWhiteSpace(DeviceId) ? SelectedLineName : DeviceId.Trim();
            settings.LineName = SelectedLineName;
            settings.ScanIntervalMs = ParseInt(ScanIntervalMs, 60_000, 200, 60_000);
            settings.TemperaturePublishThresholdC = ParseDouble(TemperaturePublishThresholdC, 0, 0, 100);
            settings.TemperaturePrecision = ParseInt(TemperaturePrecision, 1, 0, 4);
            settings.UseSimulator = UseSimulator;
            settings.StartWithWindows = StartWithWindows;
            settings.AutoStartAcquisition = AutoStartAcquisition;
            settings.EnableHistoryRecording = EnableHistoryRecording;
            settings.HistoryRetentionDays = ParseInt(HistoryRetentionDays, 14, 1, 365);
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
                ? "monitor/{deviceId}/telemetry"
                : MqttTopic.Trim();

            _startup.Apply(settings.StartWithWindows);
            LineConfigPaths.SaveCurrentLine(settings);
            await _store.SaveAsync(settings);
            _dashboard.Reload();
            StatusMessage = running
                ? "设置已保存。部分项需停止采集/订阅后重新启动才会生效。"
                : settings.StartWithWindows && _startup.IsSupported
                    ? "设置已保存，已启用开机启动。"
                    : "设置已保存。";
        }
        catch (Exception ex)
        {
            StatusMessage = $"保存失败：{ex.Message}";
        }
    }

    void LoadFrom(AppSettings settings)
    {
        DeviceId = settings.DeviceId;
        SelectedOperationMode = settings.OperationMode == AppOperationMode.Subscribe ? "订阅模式" : "采集模式";
        SubscribeTopics.Clear();
        SubscribeTopicHelper.Migrate(settings);
        foreach (var topic in settings.SubscribeTopics)
        {
            SubscribeTopics.Add(topic);
        }

        SelectedLineName = string.IsNullOrWhiteSpace(settings.LineName)
            ? LineCatalog.LineNames[0]
            : settings.LineName;
        ScanIntervalMs = settings.ScanIntervalMs.ToString();
        TemperaturePublishThresholdC = settings.TemperaturePublishThresholdC.ToString("G");
        TemperaturePrecision = settings.TemperaturePrecision.ToString();
        UseSimulator = settings.UseSimulator;
        StartWithWindows = settings.StartWithWindows;
        AutoStartAcquisition = settings.AutoStartAcquisition;
        EnableHistoryRecording = settings.EnableHistoryRecording;
        HistoryRetentionDays = settings.HistoryRetentionDays.ToString();
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

    static double ParseDouble(string text, double fallback, double min, double max)
    {
        if (!double.TryParse(text, out var value))
        {
            value = fallback;
        }

        return Math.Clamp(value, min, max);
    }

    bool IsServiceRunning() => _acquisition.IsRunning || _subscription.IsRunning;
}
