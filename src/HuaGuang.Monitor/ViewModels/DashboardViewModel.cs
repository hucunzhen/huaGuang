using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HuaGuang.Monitor.Models;
using HuaGuang.Monitor.Services;

namespace HuaGuang.Monitor.ViewModels;

public partial class DashboardViewModel : ObservableObject, IDisposable
{
    readonly AcquisitionService _acquisition;
    readonly SettingsStore _settings;

    public DashboardViewModel(AcquisitionService acquisition, SettingsStore settings)
    {
        _acquisition = acquisition;
        _settings = settings;
        _acquisition.TagsUpdated += OnTagsUpdated;
        _acquisition.ConnectionChanged += OnConnectionChanged;
        RebuildRows();
        RefreshStatus();
    }

    public ObservableCollection<TagRowViewModel> Tags { get; } = [];

    [ObservableProperty]
    bool isRunning;

    [ObservableProperty]
    bool plcConnected;

    [ObservableProperty]
    bool mqttConnected;

    [ObservableProperty]
    string lastError = string.Empty;

    [ObservableProperty]
    string lastPayload = "尚未发布";

    [ObservableProperty]
    string toggleText = "启动采集";

    [ObservableProperty]
    string deviceId = "LINE-01";

    [ObservableProperty]
    string topicPreview = string.Empty;

    [ObservableProperty]
    string modeText = "模拟模式";

    public void Reload()
    {
        RebuildRows();
        RefreshStatus();
    }

    [RelayCommand]
    async Task ToggleAsync()
    {
        try
        {
            if (_acquisition.IsRunning)
            {
                await _acquisition.StopAsync();
            }
            else
            {
                RebuildRows();
                await _acquisition.StartAsync();
            }
        }
        catch (Exception ex)
        {
            LastError = ex.Message;
        }

        RefreshStatus();
    }

    void OnTagsUpdated(object? sender, IReadOnlyList<TagSnapshot> snapshots)
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

    void OnConnectionChanged(object? sender, EventArgs e) =>
        MainThread.BeginInvokeOnMainThread(RefreshStatus);

    void RebuildRows()
    {
        Tags.Clear();
        foreach (var tag in _settings.Current.Tags.Where(t => t.Enabled))
        {
            Tags.Add(new TagRowViewModel(tag));
        }
    }

    void RefreshStatus()
    {
        var settings = _settings.Current;
        IsRunning = _acquisition.IsRunning;
        PlcConnected = _acquisition.PlcConnected;
        MqttConnected = _acquisition.MqttConnected;
        ToggleText = IsRunning ? "停止采集" : "启动采集";
        DeviceId = settings.DeviceId;
        TopicPreview = settings.Mqtt.Topic.Replace("{deviceId}", settings.DeviceId, StringComparison.OrdinalIgnoreCase);
        ModeText = settings.UseSimulator ? "模拟模式（未连真实 PLC）" : "PLC 实采模式";
        LastError = _acquisition.LastError;
        if (!string.IsNullOrWhiteSpace(_acquisition.LastPayload))
        {
            LastPayload = _acquisition.LastPayload;
        }
    }

    public void Dispose()
    {
        _acquisition.TagsUpdated -= OnTagsUpdated;
        _acquisition.ConnectionChanged -= OnConnectionChanged;
    }
}
