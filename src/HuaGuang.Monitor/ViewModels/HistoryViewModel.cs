using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HuaGuang.Monitor.Services;
using HuaGuang.Monitor.Views;

namespace HuaGuang.Monitor.ViewModels;

public partial class HistoryViewModel : ObservableObject
{
    readonly HistoryStore _store;
    readonly SettingsStore _settings;

    public HistoryViewModel(HistoryStore store, SettingsStore settings)
    {
        _store = store;
        _settings = settings;
        RangeOptions = ["最近 24 小时", "最近 7 天", "最近 30 天"];
        SelectedRange = RangeOptions[0];
        DeviceOptions = ["全部设备"];
        SelectedDevice = DeviceOptions[0];
    }

    public string[] RangeOptions { get; }
    public ObservableCollection<string> DeviceOptions { get; } = [];
    public ObservableCollection<HistorySampleSummary> Samples { get; } = [];

    [ObservableProperty] string selectedRange = "最近 24 小时";
    [ObservableProperty] string selectedDevice = "全部设备";
    [ObservableProperty] string summaryText = "加载中…";
    [ObservableProperty] string statusMessage = string.Empty;
    [ObservableProperty] bool isBusy;
    [ObservableProperty] bool showEmpty;

    public async Task InitializeAsync()
    {
        if (!_settings.Current.EnableHistoryRecording)
        {
            SummaryText = "历史记录已关闭，可在「设置」中开启。";
            ShowEmpty = true;
            return;
        }

        await RefreshAsync();
    }

    [RelayCommand]
    async Task RefreshAsync()
    {
        if (IsBusy)
        {
            return;
        }

        IsBusy = true;
        try
        {
            var (from, to) = ResolveRange(SelectedRange);
            var stats = await _store.GetStatsAsync().ConfigureAwait(false);
            var devices = await _store.GetDeviceIdsAsync().ConfigureAwait(false);

            DeviceOptions.Clear();
            DeviceOptions.Add("全部设备");
            foreach (var device in devices)
            {
                DeviceOptions.Add(device);
            }

            if (!DeviceOptions.Contains(SelectedDevice))
            {
                SelectedDevice = "全部设备";
            }

            var query = new HistoryQuery
            {
                From = from,
                To = to,
                DeviceId = SelectedDevice == "全部设备" ? null : SelectedDevice,
                Limit = 300
            };
            var rows = await _store.QueryAsync(query).ConfigureAwait(false);

            Samples.Clear();
            foreach (var row in rows)
            {
                Samples.Add(row);
            }

            ShowEmpty = Samples.Count == 0;
            SummaryText = stats.SampleCount == 0
                ? "暂无历史数据。启动采集或订阅后会自动记录。"
                : $"共 {stats.SampleCount} 条记录 · 当前筛选 {Samples.Count} 条 · 保留 {_settings.Current.HistoryRetentionDays} 天";
            StatusMessage = string.Empty;
        }
        catch (Exception ex)
        {
            StatusMessage = ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    Task OpenSampleAsync(HistorySampleSummary? sample)
    {
        if (sample is null)
        {
            return Task.CompletedTask;
        }

        return Shell.Current.GoToAsync($"{nameof(HistoryDetailPage)}?id={sample.Id}");
    }

    partial void OnSelectedRangeChanged(string value) =>
        MainThread.BeginInvokeOnMainThread(async () => await RefreshAsync());

    partial void OnSelectedDeviceChanged(string value) =>
        MainThread.BeginInvokeOnMainThread(async () => await RefreshAsync());

    static (DateTimeOffset From, DateTimeOffset To) ResolveRange(string selected) =>
        selected switch
        {
            "最近 7 天" => (DateTimeOffset.Now.AddDays(-7), DateTimeOffset.Now),
            "最近 30 天" => (DateTimeOffset.Now.AddDays(-30), DateTimeOffset.Now),
            _ => (DateTimeOffset.Now.AddHours(-24), DateTimeOffset.Now)
        };
}
