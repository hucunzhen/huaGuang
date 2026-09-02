using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HuaGuang.Monitor.Models;
using HuaGuang.Monitor.Services;
using Microsoft.Extensions.Logging;

namespace HuaGuang.Monitor.ViewModels;

public partial class DashboardViewModel : ObservableObject, IDisposable
{
    public const string AllRemoteDevicesKey = "__all__";

    readonly IMonitorAcquisition _acquisition;
    readonly IMonitorSubscription _subscription;
    readonly SettingsStore _settings;
    readonly ILogger<DashboardViewModel> _logger;
    static bool _autoStartAttempted;
    List<string> _cachedDeviceKeys = [];
    readonly Dictionary<string, TagRowViewModel> _rowLookup = new(StringComparer.Ordinal);
    readonly Dictionary<string, Dictionary<string, TagRowViewModel>> _multiRowLookup = new(StringComparer.Ordinal);

    public DashboardViewModel(
        IMonitorAcquisition acquisition,
        IMonitorSubscription subscription,
        SettingsStore settings,
        ILogger<DashboardViewModel> logger)
    {
        _acquisition = acquisition;
        _subscription = subscription;
        _settings = settings;
        _logger = logger;
        _acquisition.TagsUpdated += OnAcquisitionTagsUpdated;
        _acquisition.ConnectionChanged += OnServiceConnectionChanged;
        _subscription.DevicesUpdated += OnSubscriptionDevicesUpdated;
        _subscription.ConnectionChanged += OnServiceConnectionChanged;
        RebuildTopicFilters();
        RebuildRows();
        RefreshStatus();
    }

    public ObservableCollection<TagGroupViewModel> TagGroups { get; } = [];
    public ObservableCollection<TagRowViewModel> SwitchStatusTags { get; } = [];
    public ObservableCollection<RemoteDevicePanelViewModel> RemoteDevicePanels { get; } = [];
    public ObservableCollection<RemoteDeviceItem> RemoteDeviceItems { get; } = [];
    public ObservableCollection<string> TopicFilters { get; } = [];

    [ObservableProperty] bool isRunning;
    [ObservableProperty] bool plcConnected;
    [ObservableProperty] bool mqttConnected;
    [ObservableProperty] string lastError = string.Empty;
    [ObservableProperty] string toggleText = "启动采集";
    [ObservableProperty] string deviceId = "LINE-01";
    [ObservableProperty] string topicPreview = string.Empty;
    [ObservableProperty] string modeText = "模拟模式";
    [ObservableProperty] bool isSubscribeMode;
    [ObservableProperty] bool isAcquisitionMode = true;
    [ObservableProperty] bool showRemoteDevicePicker;
    [ObservableProperty] RemoteDeviceItem? selectedRemoteDeviceItem;
    [ObservableProperty] string selectedTopicFilter = SubscribeTopicHelper.AllTopicsLabel;
    [ObservableProperty] string newSubscribeTopic = string.Empty;
    [ObservableProperty] string emptyTagsHint = "还没有启用的点位，请到“点位”页添加。";
    [ObservableProperty] bool showEmptyTagsHint = true;
    [ObservableProperty] string scannerInputDraft = string.Empty;
    [ObservableProperty] bool showScannerInput;
    [ObservableProperty] string scannerInputTitle = string.Empty;

    PlcTag? _activeScannerTag;

    public Action? RequestScannerFocus { get; set; }
    public Action? RequestScannerInputMethodCycle { get; set; }

    public bool ShowMultiDeviceDashboard => IsSubscribeMode && IsAllDevicesSelected;
    public bool ShowSingleDeviceDashboard => !ShowMultiDeviceDashboard;

    public bool HasSwitchStatusTags => SwitchStatusTags.Count > 0;

    bool IsAllDevicesSelected => SelectedRemoteDeviceItem?.Key == AllRemoteDevicesKey;

    public void Reload()
    {
        CloseScannerInputIfTagMissing();
        RebuildTopicFilters();
        RefreshStatus();
        RebuildRows();
        ApplyLiveAcquisitionData();
    }

    void CloseScannerInputIfTagMissing()
    {
        if (_activeScannerTag is null)
        {
            return;
        }

        if (_settings.Current.Tags.Any(tag => tag.Id == _activeScannerTag.Id))
        {
            return;
        }

        ShowScannerInput = false;
        _activeScannerTag = null;
        ScannerInputTitle = string.Empty;
        ScannerInputDraft = string.Empty;
        LastError = string.Empty;
    }

    /// <summary>切换回监控页时刷新状态并恢复当前值，不重建点位结构。</summary>
    public void RefreshOnAppear()
    {
        RefreshStatus();
        ApplyLiveAcquisitionData();
    }

    public async Task TryAutoStartAsync()
    {
        RefreshStatus();

        if (_autoStartAttempted || !_settings.Current.AutoStartAcquisition || MauiProgram.UsesWindowsBackgroundService)
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
            _logger.LogWarning(ex, "自动启动失败");
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
            _logger.LogWarning(ex, "启动/停止失败");
        }

        RefreshStatus();
    }

    [RelayCommand]
    async Task SubmitScannerInputAsync()
    {
        if (IsSubscribeMode || _activeScannerTag is null)
        {
            return;
        }

        var tag = _activeScannerTag;
        var trimmed = NormalizeScannerInput(ScannerInputDraft);
        if (string.IsNullOrWhiteSpace(trimmed))
        {
            LastError = $"「{tag.Name}」不能为空。";
            return;
        }

        _rowLookup.TryGetValue(tag.Id, out var row);
        var saved = await SaveManualSettingAsync(tag, trimmed, row).ConfigureAwait(true);
        if (saved)
        {
            ScannerInputDraft = trimmed;
            RequestScannerInputMethodCycle?.Invoke();
            RequestScannerFocus?.Invoke();
        }
    }

    [RelayCommand]
    void CancelScannerInput()
    {
        ShowScannerInput = false;
        if (_activeScannerTag is not null)
        {
            ScannerInputDraft = _activeScannerTag.ManualValue ?? string.Empty;
        }

        _activeScannerTag = null;
        ScannerInputTitle = string.Empty;
        LastError = string.Empty;
        RequestScannerInputMethodCycle?.Invoke();
    }

    void OpenScannerInput(PlcTag tag)
    {
        if (IsSubscribeMode || !TagScannerHelper.SupportsScannerInput(tag))
        {
            return;
        }

        _activeScannerTag = tag;
        ScannerInputDraft = tag.ManualValue ?? string.Empty;
        ScannerInputTitle = $"{tag.Name}（支持 USB 扫码枪，扫后自动回车）";
        ShowScannerInput = true;
        RequestScannerFocus?.Invoke();
    }

    [RelayCommand]
    async Task EditSettingTagAsync(TagRowViewModel? row)
    {
        if (row is null || !row.IsEditableSetting || IsSubscribeMode)
        {
            return;
        }

        var settings = _settings.Current;
        var tag = settings.Tags.FirstOrDefault(item => item.Id == row.Id);
        if (tag is null || !tag.IsManual)
        {
            return;
        }

        if (TagScannerHelper.SupportsScannerInput(tag))
        {
            OpenScannerInput(tag);
            return;
        }

        var unitHint = string.IsNullOrWhiteSpace(tag.Unit) ? string.Empty : $"（单位 {tag.Unit}）";
        var input = await Shell.Current.DisplayPromptAsync(
            tag.Name,
            $"输入新的设定值{unitHint}",
            initialValue: tag.ManualValue,
            maxLength: 200,
            keyboard: tag.DataType == TagDataType.String ? Keyboard.Text : Keyboard.Numeric).ConfigureAwait(true);
        if (input is null)
        {
            return;
        }

        await SaveManualSettingAsync(tag, input.Trim(), row).ConfigureAwait(true);
    }

    async Task<bool> SaveManualSettingAsync(PlcTag tag, string rawValue, TagRowViewModel? row)
    {
        var trimmed = NormalizeScannerInput(rawValue);
        if (string.IsNullOrWhiteSpace(trimmed))
        {
            LastError = $"「{tag.Name}」设定值不能为空。";
            return false;
        }

        var settings = _settings.Current;
        var previousValue = tag.ManualValue;
        tag.ManualValue = trimmed;
        if (tag.DataType != TagDataType.String)
        {
            try
            {
                _ = ValueFormatting.ResolveManualValue(tag);
            }
            catch
            {
                tag.ManualValue = previousValue;
                LastError = $"「{tag.Name}」设定值与数据类型不匹配。";
                return false;
            }
        }

        try
        {
            await _settings.SaveAsync(settings).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            tag.ManualValue = previousValue;
            LastError = $"保存设定值失败：{ex.Message}";
            return false;
        }

        if (row is not null)
        {
            var value = ValueFormatting.ResolveManualValue(tag);
            value = ValueFormatting.ApplyDisplayPrecision(tag, value, settings.TemperaturePrecision);
            row.Apply(new TagSnapshot
            {
                TagId = tag.Id,
                Name = tag.Name,
                Unit = tag.Unit,
                Value = value,
                Quality = "Good",
                Timestamp = DateTimeOffset.Now
            });
        }

        if (_acquisition.IsRunning)
        {
            _acquisition.RequestImmediatePublish();
        }

        LastError = string.Empty;
        return true;
    }

    static string NormalizeScannerInput(string input) =>
        input.Trim().TrimEnd('\r', '\n', '\t');

    void SyncScannerDraft()
    {
        if (!ShowScannerInput)
        {
            ScannerInputDraft = _activeScannerTag?.ManualValue ?? string.Empty;
        }
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
                _logger.LogWarning(ex, "刷新订阅主题失败");
            }
        }

        RefreshStatus();
    }

    partial void OnSelectedRemoteDeviceItemChanged(RemoteDeviceItem? value) =>
        MainThread.BeginInvokeOnMainThread(() =>
        {
            NotifyRemoteLayoutChanged();
            RebuildRemoteView();
        });

    partial void OnSelectedTopicFilterChanged(string value) =>
        MainThread.BeginInvokeOnMainThread(() =>
        {
            _cachedDeviceKeys = [];
            RefreshRemoteDevices();
            RebuildRemoteView();
        });

    partial void OnIsSubscribeModeChanged(bool value)
    {
        NotifyRemoteLayoutChanged();
        ShowScannerInput = false;
        _activeScannerTag = null;
        ScannerInputTitle = string.Empty;
    }

    partial void OnIsAcquisitionModeChanged(bool value)
    {
        ShowScannerInput = false;
        _activeScannerTag = null;
        ScannerInputTitle = string.Empty;
    }

    void NotifyRemoteLayoutChanged()
    {
        OnPropertyChanged(nameof(ShowMultiDeviceDashboard));
        OnPropertyChanged(nameof(ShowSingleDeviceDashboard));
    }

    void OnAcquisitionTagsUpdated(object? sender, IReadOnlyList<TagSnapshot> snapshots)
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            foreach (var snapshot in snapshots)
            {
                if (_rowLookup.TryGetValue(snapshot.TagId, out var row))
                {
                    row.Apply(snapshot);
                }
            }

            RefreshStatus();
        });
    }

    void OnSubscriptionDevicesUpdated(object? sender, EventArgs e) =>
        MainThread.BeginInvokeOnMainThread(() =>
        {
            if (HasDeviceListChanged())
            {
                RefreshRemoteDevices();
                RebuildRemoteView();
            }
            else
            {
                UpdateRemoteView();
            }

            ModeText = BuildModeText();
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
        RemoteDevicePanels.Clear();
        _multiRowLookup.Clear();

        if (_settings.Current.OperationMode == AppOperationMode.Subscribe)
        {
            RebuildRemoteView();
            return;
        }

        var rows = _settings.Current.Tags
            .Where(t => t.Enabled)
            .Select(tag => new TagRowViewModel(tag, _settings.Current.TemperaturePrecision))
            .ToList();
        RebuildGroupedRows(rows);
    }

    void RebuildGroupedRows(IReadOnlyList<TagRowViewModel> rows)
    {
        _rowLookup.Clear();
        GroupedCollectionHelper.ClearDashboardGroups(TagGroups);
        SwitchStatusTags.Clear();

        foreach (var row in rows.Where(item => item.Category == TagDisplayCategory.Switch))
        {
            SwitchStatusTags.Add(row);
            _rowLookup[row.Id] = row;
        }

        foreach (var group in rows
                     .Where(item => item.Category != TagDisplayCategory.Switch)
                     .GroupBy(row => row.Category)
                     .OrderBy(g => TagDisplayCategoryHelper.GetSortOrder(g.Key)))
        {
            var groupViewModel = new TagGroupViewModel(group.Key);
            foreach (var row in group)
            {
                groupViewModel.Tags.Add(row);
                _rowLookup[row.Id] = row;
            }

            TagGroups.Add(groupViewModel);
        }

        ShowEmptyTagsHint = rows.Count == 0;
        NotifySwitchStatusChanged();
        SyncScannerDraft();
        ApplyLiveAcquisitionData();
    }

    void ApplyLiveAcquisitionData()
    {
        if (IsSubscribeMode)
        {
            return;
        }

        var settings = _settings.Current;
        foreach (var snapshot in _acquisition.LastSnapshots.Values)
        {
            if (_rowLookup.TryGetValue(snapshot.TagId, out var row))
            {
                row.Apply(snapshot);
            }
        }

        foreach (var tag in settings.Tags.Where(tag => tag.Enabled && tag.IsManual))
        {
            if (_acquisition.LastSnapshots.ContainsKey(tag.Id) ||
                !_rowLookup.TryGetValue(tag.Id, out var row))
            {
                continue;
            }

            try
            {
                row.Apply(new TagSnapshot
                {
                    TagId = tag.Id,
                    Name = tag.Name,
                    Unit = tag.Unit,
                    Value = ValueFormatting.ResolveManualValue(tag),
                    Quality = "Good",
                    Timestamp = DateTimeOffset.Now
                });
            }
            catch
            {
            }
        }
    }

    int TotalTagCount => TagGroups.Sum(group => group.Tags.Count) + SwitchStatusTags.Count;

    void ClearGroupedRows()
    {
        _rowLookup.Clear();
        GroupedCollectionHelper.ClearDashboardGroups(TagGroups);
        SwitchStatusTags.Clear();
        ShowEmptyTagsHint = true;
        NotifySwitchStatusChanged();
        CloseScannerInputIfTagMissing();
        SyncScannerDraft();
    }

    void NotifySwitchStatusChanged() =>
        OnPropertyChanged(nameof(HasSwitchStatusTags));

    IReadOnlyList<TagRowViewModel> GetFlatRows() =>
        SwitchStatusTags.Concat(TagGroups.SelectMany(group => group.Tags)).ToList();

    void RebuildRemoteView()
    {
        RemoteDevicePanels.Clear();
        _multiRowLookup.Clear();

        if (!IsSubscribeMode)
        {
            return;
        }

        if (IsAllDevicesSelected)
        {
            ClearGroupedRows();
            foreach (var device in GetFilteredDevices())
            {
                RemoteDevicePanels.Add(CreatePanel(device));
            }

            ShowEmptyTagsHint = RemoteDevicePanels.Count == 0;
            UpdateDeviceHeader();
            return;
        }

        RebuildRemoteRows();
    }

    void UpdateRemoteView()
    {
        if (!IsSubscribeMode)
        {
            return;
        }

        if (IsAllDevicesSelected)
        {
            UpdateAllRemotePanels();
            UpdateDeviceHeader();
            return;
        }

        UpdateRemoteRows();
        UpdateDeviceHeader();
    }

    RemoteDevicePanelViewModel CreatePanel(RemoteDeviceState device)
    {
        var rows = BuildRowsForDevice(device);
        var panel = new RemoteDevicePanelViewModel
        {
            DeviceKey = device.DeviceKey,
            DeviceId = device.DeviceId,
            StatusText = BuildPanelStatus(device)
        };
        PopulatePanelGroups(panel, rows);
        _multiRowLookup[device.DeviceKey] = rows.ToDictionary(row => row.Id, StringComparer.Ordinal);
        return panel;
    }

    void UpdateAllRemotePanels()
    {
        var devices = GetFilteredDevices().ToList();
        if (devices.Count == 0)
        {
            RemoteDevicePanels.Clear();
            _multiRowLookup.Clear();
            ShowEmptyTagsHint = true;
            return;
        }

        if (RemoteDevicePanels.Count != devices.Count ||
            devices.Any(device => RemoteDevicePanels.All(panel => panel.DeviceKey != device.DeviceKey)))
        {
            RebuildRemoteView();
            return;
        }

        foreach (var device in devices)
        {
            var panel = RemoteDevicePanels.First(item => item.DeviceKey == device.DeviceKey);
            panel.StatusText = BuildPanelStatus(device);
            UpdatePanelRows(panel, device);
        }

        ShowEmptyTagsHint = false;
    }

    void UpdatePanelRows(RemoteDevicePanelViewModel panel, RemoteDeviceState device)
    {
        if (!_multiRowLookup.TryGetValue(panel.DeviceKey, out var lookup))
        {
            RebuildRemoteView();
            return;
        }

        var orderedTags = OrderRemoteTags(device.Tags).ToList();
        var flatRows = panel.TagGroups.SelectMany(group => group.Tags).ToList();
        if (flatRows.Count == orderedTags.Count &&
            flatRows.Zip(orderedTags, (row, entry) => row.Name == entry.Name).All(match => match))
        {
            ApplyRemoteValuesToRows(flatRows, device, orderedTags);
            return;
        }

        var rows = BuildRowsForDevice(device);
        PopulatePanelGroups(panel, rows);
        _multiRowLookup[panel.DeviceKey] = rows.ToDictionary(row => row.Id, StringComparer.Ordinal);
    }

    void PopulatePanelGroups(RemoteDevicePanelViewModel panel, IReadOnlyList<TagRowViewModel> rows)
    {
        panel.TagGroups.Clear();
        foreach (var group in rows
                     .GroupBy(row => row.Category)
                     .OrderBy(g => TagDisplayCategoryHelper.GetSortOrder(g.Key)))
        {
            var groupViewModel = new TagGroupViewModel(group.Key);
            foreach (var row in group)
            {
                groupViewModel.Tags.Add(row);
            }

            panel.TagGroups.Add(groupViewModel);
        }
    }

    List<TagRowViewModel> BuildRowsForDevice(RemoteDeviceState device)
    {
        var precision = _settings.Current.TemperaturePrecision;
        var rows = new List<TagRowViewModel>();
        foreach (var (name, value, catalogTag) in OrderRemoteTags(device.Tags))
        {
            var tag = catalogTag ?? CreateRemoteTag(name, value);
            var row = new TagRowViewModel(tag, precision, device.SourceTopic);
            row.Apply(new TagSnapshot
            {
                TagId = tag.Id,
                Name = name,
                Value = value,
                Quality = device.Quality,
                Timestamp = device.Timestamp
            });
            rows.Add(row);
        }

        return rows;
    }

    static string BuildPanelStatus(RemoteDeviceState device)
    {
        var host = string.IsNullOrWhiteSpace(device.PlcHost) ? "—" : device.PlcHost;
        var mode = device.Simulator ? "模拟" : "PLC";
        return $"{device.SourceTopic} · {mode} · PLC {host} · 更新 {device.ReceivedAt.ToLocalTime():HH:mm:ss}";
    }

    void UpdateDeviceHeader()
    {
        if (!IsSubscribeMode)
        {
            return;
        }

        if (IsAllDevicesSelected)
        {
            var count = GetFilteredDevices().Count();
            DeviceId = count == 0 ? "等待遥测…" : $"{count} 台设备";
            return;
        }

        DeviceId = SelectedRemoteDeviceItem?.DeviceIdFromLabel() ?? "等待遥测…";
    }

    void RebuildRemoteRows()
    {
        ClearGroupedRows();
        RemoteDevicePanels.Clear();
        _multiRowLookup.Clear();

        if (!IsSubscribeMode || SelectedRemoteDeviceItem is null || IsAllDevicesSelected)
        {
            ShowEmptyTagsHint = !IsSubscribeMode || SelectedRemoteDeviceItem is null;
            UpdateDeviceHeader();
            return;
        }

        if (!_subscription.Devices.TryGetValue(SelectedRemoteDeviceItem.Key, out var device))
        {
            ShowEmptyTagsHint = true;
            UpdateDeviceHeader();
            return;
        }

        PopulateRemoteRows(device, OrderRemoteTags(device.Tags));
        ShowEmptyTagsHint = TotalTagCount == 0;
        UpdateDeviceHeader();
    }

    void UpdateRemoteRows()
    {
        if (!IsSubscribeMode || SelectedRemoteDeviceItem is null || IsAllDevicesSelected)
        {
            if (TotalTagCount > 0)
            {
                ClearGroupedRows();
            }

            return;
        }

        if (!_subscription.Devices.TryGetValue(SelectedRemoteDeviceItem.Key, out var device))
        {
            if (TotalTagCount > 0)
            {
                ClearGroupedRows();
            }

            return;
        }

        var orderedTags = OrderRemoteTags(device.Tags).ToList();
        var flatRows = GetFlatRows();
        if (flatRows.Count == orderedTags.Count &&
            flatRows.Zip(orderedTags, (row, entry) => row.Name == entry.Name).All(match => match))
        {
            ApplyRemoteValuesToRows(flatRows, device, orderedTags);
            ShowEmptyTagsHint = false;
            return;
        }

        ClearGroupedRows();
        PopulateRemoteRows(device, orderedTags);
        ShowEmptyTagsHint = TotalTagCount == 0;
    }

    void PopulateRemoteRows(
        RemoteDeviceState device,
        IEnumerable<(string Name, object? Value, PlcTag? CatalogTag)> orderedTags)
    {
        RebuildGroupedRows(BuildRowsForDevice(device));
    }

    static PlcTag CreateRemoteTag(string name, object? value) => new()
    {
        Name = name,
        DataType = value switch
        {
            bool => TagDataType.Bool,
            string => TagDataType.String,
            int or short or long or byte => RunStatusFormatting.IsRunStatusTag(new PlcTag { Name = name })
                ? TagDataType.Int16
                : TagDataType.Float32,
            _ => TagDataType.Float32
        },
        DisplayCategory = string.Equals(name, RunStatusFormatting.TagName, StringComparison.Ordinal)
            ? TagDisplayCategory.Switch
            : null
    };

    void ApplyRemoteValuesToRows(
        IReadOnlyList<TagRowViewModel> flatRows,
        RemoteDeviceState device,
        IReadOnlyList<(string Name, object? Value, PlcTag? CatalogTag)> orderedTags)
    {
        for (var i = 0; i < orderedTags.Count && i < flatRows.Count; i++)
        {
            var (_, value, _) = orderedTags[i];
            flatRows[i].Apply(new TagSnapshot
            {
                TagId = flatRows[i].Id,
                Name = flatRows[i].Name,
                Value = value,
                Quality = device.Quality,
                Timestamp = device.Timestamp
            });
        }
    }

    bool HasDeviceListChanged()
    {
        var keys = GetFilteredDevices()
            .Select(device => device.DeviceKey)
            .OrderBy(key => key, StringComparer.Ordinal)
            .ToList();
        if (_cachedDeviceKeys.SequenceEqual(keys, StringComparer.Ordinal))
        {
            return false;
        }

        _cachedDeviceKeys = keys;
        return true;
    }

    void RefreshRemoteDevices()
    {
        var previousKey = SelectedRemoteDeviceItem?.Key;
        var devices = GetFilteredDevices().ToList();
        RemoteDeviceItems.Clear();

        if (devices.Count >= 2)
        {
            RemoteDeviceItems.Add(new RemoteDeviceItem
            {
                Key = AllRemoteDevicesKey,
                Label = $"全部设备 ({devices.Count})"
            });
        }

        foreach (var device in devices)
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
            RemoteDeviceItems.FirstOrDefault(item => item.Key == AllRemoteDevicesKey) ??
            RemoteDeviceItems[0];
        UpdateDeviceHeader();
    }

    string BuildModeText()
    {
        var settings = _settings.Current;
        if (settings.OperationMode == AppOperationMode.Subscribe)
        {
            var state = _subscription.IsRunning ? "运行中" : "已停止";
            return $"订阅模式 · {state} · {GetFilteredDevices().Count()} 台";
        }

        var mode = settings.UseSimulator ? "模拟" : "PLC";
        var runState = _acquisition.IsRunning ? "运行中" : "已停止";
        return $"采集模式 · {mode} · {runState}";
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
            ModeText = BuildModeText();
            EmptyTagsHint = _subscription.IsRunning
                ? "等待遥测数据…"
                : "请启动订阅。";

            NotifyRemoteLayoutChanged();
            _cachedDeviceKeys = [];
            RefreshRemoteDevices();
            RebuildRemoteView();
            return;
        }

        RemoteDevicePanels.Clear();
        _multiRowLookup.Clear();

        IsRunning = _acquisition.IsRunning;
        PlcConnected = _acquisition.PlcConnected;
        MqttConnected = _acquisition.MqttConnected;
        ToggleText = IsRunning ? "停止采集" : "启动采集";
        DeviceId = settings.DeviceId;
        TopicPreview = $"发布主题：{settings.Mqtt.Topic.Replace("{deviceId}", settings.DeviceId, StringComparison.OrdinalIgnoreCase)}";
        ModeText = BuildModeText();
        EmptyTagsHint = "还没有启用的点位，请到“点位”页添加。";

        ShowRemoteDevicePicker = false;
    }

    IEnumerable<(string Name, object? Value, PlcTag? CatalogTag)> OrderRemoteTags(
        IReadOnlyDictionary<string, object?> remoteTags) =>
        TagDisplayOrder.OrderRemoteTags(remoteTags, _settings.Current.Tags, _settings.Current.MqttPayload);

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
        if (item.Key == DashboardViewModel.AllRemoteDevicesKey)
        {
            return item.Label;
        }

        var label = item.Label;
        var splitIndex = label.IndexOf(" (", StringComparison.Ordinal);
        return splitIndex > 0 ? label[..splitIndex] : label;
    }
}
