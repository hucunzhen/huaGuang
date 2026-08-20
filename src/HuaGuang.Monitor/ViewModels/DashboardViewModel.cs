using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HuaGuang.Monitor.Models;
using HuaGuang.Monitor.Services;

namespace HuaGuang.Monitor.ViewModels;

public partial class DashboardViewModel : ObservableObject, IDisposable
{
    readonly AcquisitionService _acquisition;
    readonly SubscriptionService _subscription;
    readonly SettingsStore _settings;
    static bool _autoStartAttempted;

    public DashboardViewModel(
        AcquisitionService acquisition,
        SubscriptionService subscription,
        SettingsStore settings)
    {
        _acquisition = acquisition;
        _subscription = subscription;
        _settings = settings;
        _acquisition.TagsUpdated += OnAcquisitionTagsUpdated;
        _acquisition.ConnectionChanged += OnServiceConnectionChanged;
        _subscription.DevicesUpdated += OnSubscriptionDevicesUpdated;
        _subscription.ConnectionChanged += OnServiceConnectionChanged;
        RebuildTopicFilters();
        RebuildRows();
        RefreshStatus();
    }

    public ObservableCollection<TagRowViewModel> Tags { get; } = [];
    public ObservableCollection<RemoteDeviceItem> RemoteDeviceItems { get; } = [];
    public ObservableCollection<string> TopicFilters { get; } = [];

    [ObservableProperty] bool isRunning;
    [ObservableProperty] bool plcConnected;
    [ObservableProperty] bool mqttConnected;
    [ObservableProperty] string lastError = string.Empty;
    [ObservableProperty] string lastPayload = "尚未收到数据";
    [ObservableProperty] string toggleText = "启动采集";
    [ObservableProperty] string deviceId = "LINE-01";
    [ObservableProperty] string topicPreview = string.Empty;
    [ObservableProperty] string modeText = "模拟模式";
    [ObservableProperty] string publishNote = string.Empty;
    [ObservableProperty] bool isSubscribeMode;
    [ObservableProperty] bool isAcquisitionMode = true;
    [ObservableProperty] bool showRemoteDevicePicker;
    [ObservableProperty] RemoteDeviceItem? selectedRemoteDeviceItem;
    [ObservableProperty] string selectedTopicFilter = SubscribeTopicHelper.AllTopicsLabel;
    [ObservableProperty] string newSubscribeTopic = string.Empty;
    [ObservableProperty] string emptyTagsHint = "还没有启用的点位，请到“点位”页添加。";

    public void Reload()
    {
        RebuildTopicFilters();
        RefreshStatus();
        RebuildRows();
    }

    public async Task TryAutoStartAsync()
    {
        RefreshStatus();

        if (_autoStartAttempted || !_settings.Current.AutoStartAcquisition)
        {
            return;
        }

        if (IsRunning)
        {
            _autoStartAttempted = true;
            return;
        }

        _autoStartAttempted = true;

        try
        {
            RebuildRows();
            if (IsSubscribeMode)
            {
                await _subscription.StartAsync();
            }
            else
            {
                await _acquisition.StartAsync();
            }
        }
        catch (Exception ex)
        {
            LastError = ex.Message;
        }

        RefreshStatus();
    }

    [RelayCommand]
    async Task ToggleAsync()
    {
        try
        {
            if (IsRunning)
            {
                if (IsSubscribeMode)
                {
                    await _subscription.StopAsync();
                }
                else
                {
                    await _acquisition.StopAsync();
                }
            }
            else
            {
                RebuildRows();
                if (IsSubscribeMode)
                {
                    await _subscription.StartAsync();
                }
                else
                {
                    await _acquisition.StartAsync();
                }
            }
        }
        catch (Exception ex)
        {
            LastError = ex.Message;
        }

        RefreshStatus();
    }

    [RelayCommand]
    async Task AddSubscribeTopicAsync()
    {
        var topic = NewSubscribeTopic.Trim();
        if (string.IsNullOrWhiteSpace(topic))
        {
            LastError = "请填写要添加的订阅主题。";
            return;
        }

        var settings = _settings.Current;
        SubscribeTopicHelper.Migrate(settings);
        if (settings.SubscribeTopics.Any(existing => existing.Equals(topic, StringComparison.OrdinalIgnoreCase)))
        {
            LastError = "该订阅主题已存在。";
            SelectedTopicFilter = topic;
            return;
        }

        settings.SubscribeTopics.Add(topic);
        settings.SubscribeTopic = settings.SubscribeTopics[0];
        await _settings.SaveAsync(settings);

        RebuildTopicFilters();
        SelectedTopicFilter = topic;
        NewSubscribeTopic = string.Empty;
        LastError = string.Empty;

        if (_subscription.IsRunning)
        {
            try
            {
                await _subscription.RefreshTopicsAsync();
            }
            catch (Exception ex)
            {
                LastError = ex.Message;
            }
        }

        RefreshStatus();
    }

    partial void OnSelectedRemoteDeviceItemChanged(RemoteDeviceItem? value) =>
        MainThread.BeginInvokeOnMainThread(RebuildRemoteRows);

    partial void OnSelectedTopicFilterChanged(string value) =>
        MainThread.BeginInvokeOnMainThread(() =>
        {
            RefreshRemoteDevices();
            RebuildRemoteRows();
            PublishNote = BuildRemoteStatusNote();
        });

    void OnAcquisitionTagsUpdated(object? sender, IReadOnlyList<TagSnapshot> snapshots)
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            foreach (var snapshot in snapshots)
            {
                var row = Tags.FirstOrDefault(t => t.Id == snapshot.TagId);
                row?.Apply(snapshot);
            }

            LastPayload = string.IsNullOrWhiteSpace(_acquisition.LastPayload)
                ? "尚未发布"
                : _acquisition.LastPayload;
            PublishNote = _acquisition.LastPublishNote;
            if (!string.IsNullOrWhiteSpace(_acquisition.LastError))
            {
                LastError = _acquisition.LastError;
            }
            else if (snapshots.All(s => s.Quality == "Good"))
            {
                LastError = string.Empty;
            }
        });
    }

    void OnSubscriptionDevicesUpdated(object? sender, EventArgs e) =>
        MainThread.BeginInvokeOnMainThread(() =>
        {
            RefreshRemoteDevices();
            RebuildRemoteRows();
            LastPayload = string.IsNullOrWhiteSpace(_subscription.LastPayload)
                ? LastPayload
                : _subscription.LastPayload;
            PublishNote = BuildRemoteStatusNote();
            LastError = _subscription.LastError;
            ModeText = $"订阅模式 · 在线 {GetFilteredDevices().Count()} 台";
        });

    void OnServiceConnectionChanged(object? sender, EventArgs e) =>
        MainThread.BeginInvokeOnMainThread(RefreshStatus);

    void RebuildTopicFilters()
    {
        var previous = SelectedTopicFilter;
        TopicFilters.Clear();
        TopicFilters.Add(SubscribeTopicHelper.AllTopicsLabel);
        foreach (var topic in SubscribeTopicHelper.NormalizeTopics(_settings.Current.SubscribeTopics))
        {
            TopicFilters.Add(topic);
        }

        if (TopicFilters.Contains(previous))
        {
            SelectedTopicFilter = previous;
        }
        else
        {
            SelectedTopicFilter = SubscribeTopicHelper.AllTopicsLabel;
        }
    }

    IEnumerable<RemoteDeviceState> GetFilteredDevices() =>
        _subscription.GetDevices(SelectedTopicFilter);

    void RebuildRows()
    {
        if (_settings.Current.OperationMode == AppOperationMode.Subscribe)
        {
            RebuildRemoteRows();
            return;
        }

        Tags.Clear();
        foreach (var tag in _settings.Current.Tags.Where(t => t.Enabled))
        {
            Tags.Add(new TagRowViewModel(tag, _settings.Current.TemperaturePrecision));
        }
    }

    void RebuildRemoteRows()
    {
        Tags.Clear();
        if (!IsSubscribeMode || SelectedRemoteDeviceItem is null)
        {
            return;
        }

        if (!_subscription.Devices.TryGetValue(SelectedRemoteDeviceItem.Key, out var device))
        {
            return;
        }

        var precision = _settings.Current.TemperaturePrecision;
        foreach (var (name, value) in device.Tags.OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            var tag = new PlcTag
            {
                Name = name,
                DataType = value is string ? TagDataType.String : TagDataType.Float32
            };
            var row = new TagRowViewModel(tag, precision, device.SourceTopic);
            row.Apply(new TagSnapshot
            {
                TagId = name,
                Name = name,
                Value = value,
                Quality = device.Quality,
                Timestamp = device.Timestamp
            });
            Tags.Add(row);
        }
    }

    void RefreshRemoteDevices()
    {
        var previousKey = SelectedRemoteDeviceItem?.Key;
        RemoteDeviceItems.Clear();

        foreach (var device in GetFilteredDevices())
        {
            RemoteDeviceItems.Add(new RemoteDeviceItem
            {
                Key = device.DeviceKey,
                Label = SelectedTopicFilter == SubscribeTopicHelper.AllTopicsLabel
                    ? device.DisplayLabel
                    : device.DeviceId
            });
        }

        ShowRemoteDevicePicker = RemoteDeviceItems.Count > 0;
        if (RemoteDeviceItems.Count == 0)
        {
            SelectedRemoteDeviceItem = null;
            DeviceId = "等待遥测…";
            return;
        }

        SelectedRemoteDeviceItem =
            RemoteDeviceItems.FirstOrDefault(item => item.Key == previousKey) ??
            RemoteDeviceItems[0];
        DeviceId = SelectedRemoteDeviceItem.DeviceIdFromLabel();
    }

    string BuildRemoteStatusNote()
    {
        if (!IsSubscribeMode || !_subscription.IsRunning)
        {
            return string.Empty;
        }

        var filteredCount = GetFilteredDevices().Count();
        if (filteredCount == 0)
        {
            return SelectedTopicFilter == SubscribeTopicHelper.AllTopicsLabel
                ? "已连接 Broker，等待其他设备发布遥测…"
                : $"主题「{SelectedTopicFilter}」暂无设备数据。";
        }

        if (SelectedRemoteDeviceItem is not null &&
            _subscription.Devices.TryGetValue(SelectedRemoteDeviceItem.Key, out var device))
        {
            var host = string.IsNullOrWhiteSpace(device.PlcHost) ? "—" : device.PlcHost;
            var mode = device.Simulator ? "模拟" : "PLC";
            return $"当前主题 {SelectedTopicFilter} · 设备 {filteredCount} 台 · {device.DeviceId} · {mode} · PLC {host} · 更新 {device.ReceivedAt.ToLocalTime():HH:mm:ss}";
        }

        return $"当前主题 {SelectedTopicFilter} · 设备 {filteredCount} 台";
    }

    void RefreshStatus()
    {
        var settings = _settings.Current;
        IsSubscribeMode = settings.OperationMode == AppOperationMode.Subscribe;
        IsAcquisitionMode = !IsSubscribeMode;

        if (IsSubscribeMode)
        {
            IsRunning = _subscription.IsRunning;
            PlcConnected = false;
            MqttConnected = _subscription.IsConnected;
            ToggleText = IsRunning ? "停止订阅" : "启动订阅";
            var topics = SubscribeTopicHelper.NormalizeTopics(settings.SubscribeTopics);
            TopicPreview = _subscription.IsRunning && _subscription.ActiveSubscribeTopics.Count > 0
                ? $"已订阅：{string.Join("，", _subscription.ActiveSubscribeTopics)}"
                : $"订阅主题：{string.Join("，", topics)}";
            ModeText = $"订阅模式 · 在线 {GetFilteredDevices().Count()} 台";
            EmptyTagsHint = _subscription.IsRunning
                ? "等待遥测数据…可在上方切换主题或添加新主题。"
                : "请启动订阅，并确认 Broker 地址正确。";
            PublishNote = BuildRemoteStatusNote();
            LastError = _subscription.LastError;
            if (!string.IsNullOrWhiteSpace(_subscription.LastPayload))
            {
                LastPayload = _subscription.LastPayload;
            }

            RefreshRemoteDevices();
            return;
        }

        IsRunning = _acquisition.IsRunning;
        PlcConnected = _acquisition.PlcConnected;
        MqttConnected = _acquisition.MqttConnected;
        ToggleText = IsRunning ? "停止采集" : "启动采集";
        DeviceId = settings.DeviceId;
        TopicPreview = $"发布主题：{settings.Mqtt.Topic.Replace("{deviceId}", settings.DeviceId, StringComparison.OrdinalIgnoreCase)}";
        ModeText = settings.UseSimulator ? "采集模式 · 模拟数据" : "采集模式 · PLC 实采";
        EmptyTagsHint = "还没有启用的点位，请到“点位”页添加。";
        if (settings.TemperaturePublishThresholdC > 0)
        {
            ModeText += $" · 温度变化 ≥ {settings.TemperaturePublishThresholdC:G}℃ 才发布";
        }

        PublishNote = _acquisition.LastPublishNote;
        LastError = _acquisition.LastError;
        if (!string.IsNullOrWhiteSpace(_acquisition.LastPayload))
        {
            LastPayload = _acquisition.LastPayload;
        }

        ShowRemoteDevicePicker = false;
    }

    public void Dispose()
    {
        _acquisition.TagsUpdated -= OnAcquisitionTagsUpdated;
        _acquisition.ConnectionChanged -= OnServiceConnectionChanged;
        _subscription.DevicesUpdated -= OnSubscriptionDevicesUpdated;
        _subscription.ConnectionChanged -= OnServiceConnectionChanged;
    }
}

static file class RemoteDeviceItemExtensions
{
    public static string DeviceIdFromLabel(this RemoteDeviceItem item)
    {
        var label = item.Label;
        var splitIndex = label.IndexOf(" (", StringComparison.Ordinal);
        return splitIndex > 0 ? label[..splitIndex] : label;
    }
}
