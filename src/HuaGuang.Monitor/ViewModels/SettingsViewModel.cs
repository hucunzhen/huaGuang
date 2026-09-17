using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HuaGuang.Monitor.Ipc;
using HuaGuang.Monitor.Models;
using HuaGuang.Monitor.Services;
using HuaGuang.Monitor.Services.LogExport;
using HuaGuang.Monitor.Services.Logging;
using Microsoft.Extensions.Logging;

namespace HuaGuang.Monitor.ViewModels;

public partial class SettingsViewModel : ObservableObject
{
    readonly SettingsStore _store;
    readonly IMonitorAcquisition _acquisition;
    readonly IMonitorSubscription _subscription;
    readonly IStartupRegistration _startup;
    readonly DashboardViewModel _dashboard;
    readonly ILogger<SettingsViewModel> _logger;
    readonly ILogExportLocationService _logExport;
    bool _isApplyingLine;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanChangeLinePicker))]
    bool isSwitchingLine;
    bool _isLoadingSettings;

    public SettingsViewModel(
        SettingsStore store,
        IMonitorAcquisition acquisition,
        IMonitorSubscription subscription,
        IStartupRegistration startup,
        DashboardViewModel dashboard,
        ILogExportLocationService logExport,
        ILogger<SettingsViewModel> logger)
    {
        _store = store;
        _acquisition = acquisition;
        _subscription = subscription;
        _startup = startup;
        _dashboard = dashboard;
        _logExport = logExport;
        _logger = logger;
        MqttEndpoints.CollectionChanged += OnMqttEndpointsCollectionChanged;
        _isApplyingLine = true;
        LoadFrom(_store.Current);
        SelectedLineName = string.IsNullOrWhiteSpace(_store.Current.LineName)
            ? LineCatalog.LineNames[0]
            : _store.Current.LineName;
        _isApplyingLine = false;
    }

    public IReadOnlyList<string> LineNames => LineCatalog.LineNames;
    public string[] OperationModes { get; } = ["采集模式", "订阅模式"];
    public ObservableCollection<string> SubscribeTopics { get; } = [];
    public ObservableCollection<MqttEndpointViewModel> MqttEndpoints { get; } = [];

    [ObservableProperty] string selectedOperationMode = "采集模式";
    [ObservableProperty] string newSubscribeTopic = string.Empty;
    [ObservableProperty] string selectedLineName = "先河热熔胶复合机";
    [ObservableProperty] string deviceId = "先河热熔胶复合机";
    [ObservableProperty] string scanIntervalMs = "2000";
    [ObservableProperty] string publishIntervalMs = "60000";
    [ObservableProperty] string temperaturePublishThresholdC = "0";
    [ObservableProperty] string temperaturePrecision = AppSettings.DefaultTemperaturePrecision.ToString();
    [ObservableProperty] bool useSimulator = true;
    [ObservableProperty] bool startWithWindows = true;
    [ObservableProperty] bool autoStartAcquisition = true;
    [ObservableProperty] bool enableHistoryRecording = true;
    [ObservableProperty] string historyRetentionDays = "1";
    [ObservableProperty] string logExportDirectory = string.Empty;

    public string LogExportDirectoryHint =>
        "留空时打包会弹出系统目录选择；Android 请在侧栏选 USB 存储或 SD 卡。";

    public bool StartupSupported => _startup.IsSupported;
    public bool IsSubscribeSettings => SelectedOperationMode == "订阅模式";
    public bool IsAcquisitionSettings => SelectedOperationMode == "采集模式";

    partial void OnSelectedOperationModeChanged(string value)
    {
        OnPropertyChanged(nameof(IsSubscribeSettings));
        OnPropertyChanged(nameof(IsAcquisitionSettings));
    }

    [ObservableProperty] string selectedPlcProtocol = "Modbus TCP";
    [ObservableProperty] string plcModel = "XD5E-60T10";
    [ObservableProperty] string plcHost = "192.168.6.10";
    [ObservableProperty] string plcPort = "502";
    [ObservableProperty] string station = "1";
    [ObservableProperty] string plcRack = "0";
    [ObservableProperty] string plcSlot = "1";
    [ObservableProperty] string plcCpuType = "S71200";
    [ObservableProperty] string plcTimeoutMs = "2000";

    public string[] PlcProtocolOptions { get; } = ["Modbus TCP", "西门子 S7"];
    public string[] S7CpuTypeOptions { get; } = ["S71200", "S71500", "S7300", "S7400", "S7200Smart"];
    public bool IsS7Plc => SelectedPlcProtocol == "西门子 S7";
    public bool IsModbusPlc => !IsS7Plc;
    public string PlcSectionTitle => PlcSettingsHelper.PlcSectionTitle(new PlcSettings
    {
        Protocol = IsS7Plc ? PlcProtocol.S7 : PlcProtocol.ModbusTcp,
        Model = PlcModel,
        CpuType = PlcCpuType
    });

    partial void OnSelectedPlcProtocolChanged(string value)
    {
        OnPropertyChanged(nameof(IsS7Plc));
        OnPropertyChanged(nameof(IsModbusPlc));
        OnPropertyChanged(nameof(PlcSectionTitle));
    }

    partial void OnPlcModelChanged(string value) => OnPropertyChanged(nameof(PlcSectionTitle));
    partial void OnPlcCpuTypeChanged(string value) => OnPropertyChanged(nameof(PlcSectionTitle));

    [ObservableProperty] string statusMessage = string.Empty;

    public bool CanChangeLinePicker => !IsSwitchingLine;

    public string LineExcelPath => LineConfigPaths.GetLineExcelPath(SelectedLineName);

    partial void OnSelectedLineNameChanged(string value)
    {
        OnPropertyChanged(nameof(LineExcelPath));
        if (_isApplyingLine || IsSwitchingLine || string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        if (IsServiceRunning())
        {
            _isApplyingLine = true;
            SelectedLineName = _store.Current.LineName;
            _isApplyingLine = false;
            StatusMessage = "请先停止采集/订阅再切换产线。";
            return;
        }

        _ = LoadSelectedLineFromExcelSilentAsync(value);
    }

    async Task LoadSelectedLineFromExcelSilentAsync(string lineName)
    {
        if (IsSwitchingLine)
        {
            return;
        }

        IsSwitchingLine = true;
        StatusMessage = "正在切换产线，请稍候…";
        try
        {
            var preserve = _store.Current;
            var filePath = LineConfigPaths.GetLineExcelPath(lineName);
            var templatePath = LineConfigPaths.ResolveShippedLineExcelPath(lineName);
            var settings = await Task.Run(() =>
                    LineExcelConfigService.SwitchLine(lineName, filePath, templatePath, preserve))
                .ConfigureAwait(false);
            await _store.SaveAsync(settings).ConfigureAwait(false);
            await ApplyLoadedSettingsOnMainThreadAsync(
                    settings,
                    $"已切换产线：{lineName}（PLC {settings.Plc.Host}，ClientId {settings.Mqtt.ClientId}）")
                .ConfigureAwait(false);
            await MainThread.InvokeOnMainThreadAsync(() => _dashboard.Reload()).ConfigureAwait(false);
            _logger.LogInformation(
                "产线已切换 line={LineName} plc={Plc} mqtt={Mqtt}",
                lineName,
                LogFormatting.DescribePlc(settings.Plc),
                LogFormatting.DescribeMqtt(settings.Mqtt, lineName));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "切换产线失败 line={LineName}", lineName);
            await MainThread.InvokeOnMainThreadAsync(() =>
            {
                _isApplyingLine = true;
                SelectedLineName = _store.Current.LineName;
                _isApplyingLine = false;
                StatusMessage = $"切换产线失败：{ex.Message}";
            }).ConfigureAwait(false);
        }
        finally
        {
            await MainThread.InvokeOnMainThreadAsync(() => IsSwitchingLine = false).ConfigureAwait(false);
        }
    }

    public void Reload()
    {
        if (IsSwitchingLine)
        {
            return;
        }

        LoadFrom(_store.Current);
    }

    async Task ApplyLoadedSettingsOnMainThreadAsync(AppSettings settings, string successMessage)
    {
        await MainThread.InvokeOnMainThreadAsync(() =>
        {
            _isApplyingLine = true;
            LoadFrom(settings);
            _isApplyingLine = false;
            StatusMessage = successMessage;
        });
    }

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
            var lineName = SelectedLineName;
            var filePath = LineConfigPaths.GetLineExcelPath(lineName);
            var templatePath = LineConfigPaths.ResolveShippedLineExcelPath(lineName);
            var settings = await Task.Run(() =>
                    LineExcelConfigService.LoadLineExcel(lineName, filePath, templatePath))
                .ConfigureAwait(false);
            await _store.SaveAsync(settings).ConfigureAwait(false);
            var plcCount = settings.Tags.Count(t => !t.IsManual);
            var manualCount = settings.Tags.Count(t => t.IsManual);
            await ApplyLoadedSettingsOnMainThreadAsync(
                settings,
                $"已从 Excel 载入：{settings.LineName}，PLC {plcCount} 个，手动 {manualCount} 个。");
            await MainThread.InvokeOnMainThreadAsync(() => _dashboard.Reload());
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
            if (!TryValidateMqttSettings(out var validationMessage))
            {
                StatusMessage = validationMessage;
                return;
            }

            var settings = BuildPendingSettings();
            settings.Tags = _store.Current.Tags;
            settings.MqttPayload = _store.Current.MqttPayload;
            await _store.SaveAsync(settings);
            StatusMessage = $"已保存到 Excel：{LineConfigPaths.GetLineExcelPath(settings.LineName)}";
            await Task.CompletedTask;
        }
        catch (Exception ex)
        {
            StatusMessage = $"导出 Excel 失败：{ex.Message}";
        }
    }

    [RelayCommand]
    async Task PickLogExportDirectoryAsync()
    {
        try
        {
            var pick = await _logExport.PickDirectoryAsync(LogExportDirectory).ConfigureAwait(false);
            if (pick is null)
            {
                StatusMessage = "未选择目录。";
                return;
            }

            LogExportDirectory = pick.SettingsStorageValue;
            StatusMessage = $"已选择输出目录：{pick.DisplayPath}。点「保存设置」后记住该位置（Android 含 USB 存储）。";
        }
        catch (Exception ex)
        {
            StatusMessage = $"选择目录失败：{ex.Message}";
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

            var destPath = LineConfigPaths.GetLineExcelPath(SelectedLineName);
            LineExcelConfigService.ImportToLineFile(tempPath, destPath, SelectedLineName);
            File.Delete(tempPath);

            var lineName = SelectedLineName;
            var templatePath = LineConfigPaths.ResolveShippedLineExcelPath(lineName);
            settings = await Task.Run(() =>
                    LineExcelConfigService.LoadLineExcel(lineName, destPath, templatePath))
                .ConfigureAwait(false);
            await _store.SaveAsync(settings).ConfigureAwait(false);
            await ApplyLoadedSettingsOnMainThreadAsync(settings, $"已导入并保存到产线文件：{destPath}");
            await MainThread.InvokeOnMainThreadAsync(() => _dashboard.Reload());
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
        settings.ScanIntervalMs = ParseInt(ScanIntervalMs, 2_000, AcquisitionTiming.MinIntervalMs, AcquisitionTiming.MaxScanIntervalMs);
        settings.PublishIntervalMs = ParseInt(PublishIntervalMs, 60_000, AcquisitionTiming.MinIntervalMs, AcquisitionTiming.MaxPublishIntervalMs);
        settings.TemperaturePublishThresholdC = ParseDouble(TemperaturePublishThresholdC, 0, 0, 100);
        settings.TemperaturePrecision = ParseInt(TemperaturePrecision, AppSettings.DefaultTemperaturePrecision, 0, 4);
        settings.UseSimulator = UseSimulator;
        ApplyPlcSettingsTo(settings);
        ApplyMqttEndpointsToSettings(settings);
        return settings;
    }

    bool TryValidateMqttSettings(out string message)
    {
        if (MqttEndpoints.Count == 0)
        {
            message = "请至少添加一个 MQTT 目标。";
            return false;
        }

        foreach (var endpoint in MqttEndpoints.Where(e => e.Enabled))
        {
            if (string.IsNullOrWhiteSpace(endpoint.ClientId))
            {
                message = $"请填写 MQTT 目标「{endpoint.Name}」的 ClientId。";
                return false;
            }

            if (string.IsNullOrWhiteSpace(endpoint.Username))
            {
                message = $"请填写 MQTT 目标「{endpoint.Name}」的用户名。";
                return false;
            }

            if (string.IsNullOrWhiteSpace(endpoint.Password))
            {
                message = $"请填写 MQTT 目标「{endpoint.Name}」的密码。";
                return false;
            }

            if (string.IsNullOrWhiteSpace(endpoint.Host))
            {
                message = $"请填写 MQTT 目标「{endpoint.Name}」的 Broker 地址。";
                return false;
            }
        }

        message = string.Empty;
        return true;
    }

    [RelayCommand]
    void AddMqttEndpoint()
    {
        var endpoint = new MqttEndpointViewModel
        {
            Name = $"目标 {MqttEndpoints.Count + 1}"
        };
        endpoint.CopyDefaultsFromLine(SelectedLineName);
        MqttEndpoints.Add(endpoint);
    }

    [RelayCommand]
    void RemoveMqttEndpoint(MqttEndpointViewModel? endpoint)
    {
        if (endpoint is null || !MqttEndpoints.Contains(endpoint))
        {
            return;
        }

        if (MqttEndpoints.Count <= 1)
        {
            StatusMessage = "至少保留一个 MQTT 目标。";
            return;
        }

        MqttEndpoints.Remove(endpoint);
    }

    void ApplyMqttEndpointsToSettings(AppSettings settings)
    {
        settings.MqttEndpoints = MqttEndpoints.Select(endpoint => endpoint.ToModel()).ToList();
        MqttEndpointCatalog.Normalize(settings);
    }

    void OnMqttEndpointsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems is not null)
        {
            foreach (MqttEndpointViewModel endpoint in e.OldItems)
            {
                endpoint.PropertyChanged -= OnMqttEndpointPropertyChanged;
            }
        }

        if (e.NewItems is not null)
        {
            foreach (MqttEndpointViewModel endpoint in e.NewItems)
            {
                endpoint.PropertyChanged += OnMqttEndpointPropertyChanged;
            }
        }

        SyncMqttEndpointsToStore();
    }

    void OnMqttEndpointPropertyChanged(object? sender, PropertyChangedEventArgs e) =>
        SyncMqttEndpointsToStore();

    /// <summary>
    /// 设置页 MQTT 列表与内存配置同步，避免只改 UI 未点「保存设置」时被点表等保存写回旧 Excel。
    /// </summary>
    void SyncMqttEndpointsToStore()
    {
        if (_isApplyingLine || IsSwitchingLine || _isLoadingSettings)
        {
            return;
        }

        ApplyMqttEndpointsToSettings(_store.Current);
    }

    void ApplyPlcSettingsTo(AppSettings settings)
    {
        settings.Plc.Protocol = SelectedPlcProtocol == "西门子 S7" ? PlcProtocol.S7 : PlcProtocol.ModbusTcp;
        settings.Plc.Model = string.IsNullOrWhiteSpace(PlcModel)
            ? settings.Plc.Protocol == PlcProtocol.S7 ? "S7-1200" : "XD5E-60T10"
            : PlcModel.Trim();
        settings.Plc.Host = PlcHost.Trim();
        settings.Plc.Port = ParseInt(
            PlcPort,
            settings.Plc.Protocol == PlcProtocol.S7 ? 102 : 502,
            1,
            65535);
        settings.Plc.Station = (byte)ParseInt(Station, 1, 1, 247);
        settings.Plc.CpuType = string.IsNullOrWhiteSpace(PlcCpuType) ? "S71200" : PlcCpuType.Trim();
        settings.Plc.Rack = ParseInt(PlcRack, 0, 0, 7);
        settings.Plc.Slot = ParseInt(
            PlcSlot,
            PlcSettingsHelper.RecommendedDefaultSlot(settings.Plc.CpuType),
            0,
            31);
        settings.Plc.TimeoutMs = ParseInt(PlcTimeoutMs, 2000, 200, 10_000);
        PlcSettingsHelper.Normalize(settings.Plc);
    }

    [RelayCommand]
    async Task SaveAsync()
    {
        try
        {
            if (!TryValidateMqttSettings(out var validationMessage))
            {
                StatusMessage = validationMessage;
                return;
            }

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
            settings.ScanIntervalMs = ParseInt(ScanIntervalMs, 2_000, AcquisitionTiming.MinIntervalMs, AcquisitionTiming.MaxScanIntervalMs);
            settings.PublishIntervalMs = ParseInt(PublishIntervalMs, 60_000, AcquisitionTiming.MinIntervalMs, AcquisitionTiming.MaxPublishIntervalMs);
            settings.TemperaturePublishThresholdC = ParseDouble(TemperaturePublishThresholdC, 0, 0, 100);
            settings.TemperaturePrecision = ParseInt(TemperaturePrecision, AppSettings.DefaultTemperaturePrecision, 0, 4);
            settings.UseSimulator = UseSimulator;
            settings.StartWithWindows = StartWithWindows;
            settings.AutoStartAcquisition = AutoStartAcquisition;
            settings.EnableHistoryRecording = EnableHistoryRecording;
            settings.HistoryRetentionDays = ParseInt(HistoryRetentionDays, 1, 1, 365);
            settings.LogExportDirectory = LogExportDirectory.Trim();
            ApplyPlcSettingsTo(settings);
            ApplyMqttEndpointsToSettings(settings);

            _startup.Apply(settings.StartWithWindows);
            await _store.SaveAsync(settings);
            await NotifyRuntimeReloadAsync();
            await MainThread.InvokeOnMainThreadAsync(() => _dashboard.Reload());
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
        _isLoadingSettings = true;
        try
        {
            LoadFromCore(settings);
        }
        finally
        {
            _isLoadingSettings = false;
        }
    }

    void LoadFromCore(AppSettings settings)
    {
        DeviceId = settings.DeviceId;
        SelectedOperationMode = settings.OperationMode == AppOperationMode.Subscribe ? "订阅模式" : "采集模式";
        SubscribeTopics.Clear();
        foreach (var topic in settings.SubscribeTopics)
        {
            SubscribeTopics.Add(topic);
        }

        SelectedLineName = string.IsNullOrWhiteSpace(settings.LineName)
            ? LineCatalog.LineNames[0]
            : settings.LineName;
        ScanIntervalMs = settings.ScanIntervalMs.ToString();
        PublishIntervalMs = settings.PublishIntervalMs.ToString();
        TemperaturePublishThresholdC = settings.TemperaturePublishThresholdC.ToString("G");
        TemperaturePrecision = settings.TemperaturePrecision.ToString();
        UseSimulator = settings.UseSimulator;
        StartWithWindows = settings.StartWithWindows;
        AutoStartAcquisition = settings.AutoStartAcquisition;
        EnableHistoryRecording = settings.EnableHistoryRecording;
        HistoryRetentionDays = settings.HistoryRetentionDays.ToString();
        LogExportDirectory = settings.LogExportDirectory ?? string.Empty;
        PlcSettingsHelper.Normalize(settings.Plc);
        SelectedPlcProtocol = PlcSettingsHelper.FormatProtocol(settings.Plc.Protocol);
        PlcModel = settings.Plc.Model;
        PlcHost = settings.Plc.Host;
        PlcPort = settings.Plc.Port.ToString();
        Station = settings.Plc.Station.ToString();
        PlcRack = settings.Plc.Rack.ToString();
        PlcSlot = settings.Plc.Slot.ToString();
        PlcCpuType = settings.Plc.CpuType;
        PlcTimeoutMs = settings.Plc.TimeoutMs.ToString();
        MqttEndpointCatalog.Normalize(settings);
        MqttEndpoints.Clear();
        foreach (var endpoint in settings.MqttEndpoints)
        {
            MqttEndpoints.Add(MqttEndpointViewModel.FromModel(endpoint));
        }

        if (MqttEndpoints.Count == 0)
        {
            var fallback = MqttEndpointViewModel.FromModel(MqttEndpoint.FromSettings(settings.Mqtt, "默认"));
            MqttEndpoints.Add(fallback);
        }

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

    async Task NotifyRuntimeReloadAsync()
    {
#if WINDOWS
        if (!MauiProgram.IsWindowsBackgroundServiceAvailable())
        {
            return;
        }

        try
        {
            await new MonitorIpcClient().SendAsync(new MonitorIpcRequest
            {
                Command = MonitorIpcCommand.ReloadSettings
            });
        }
        catch
        {
        }
#endif
    }
}
