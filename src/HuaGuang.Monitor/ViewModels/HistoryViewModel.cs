using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HuaGuang.Monitor.Services;

namespace HuaGuang.Monitor.ViewModels;

public partial class HistoryViewModel : ObservableObject
{
    readonly HistoryStore _store;
    readonly SettingsStore _settings;

    bool _suppressFilterRefresh;
    int _totalCount;
    int _currentOffset;
    DateTimeOffset _queryFrom;
    DateTimeOffset _queryTo;
    string? _queryDeviceFilter;
    List<string> _preferredTags = [];
    List<HistoryTableColumn> _fixedColumns = [];

    public HistoryViewModel(HistoryStore store, SettingsStore settings)
    {
        _store = store;
        _settings = settings;
        RangeOptions = ["最近 24 小时", "最近 7 天", "最近 30 天", "自定义时间"];
        SelectedRange = RangeOptions[0];
        DeviceOptions = ["全部设备"];
        SelectedDevice = DeviceOptions[0];
        CustomStartDate = DateTime.Today.AddDays(-1);
        CustomEndDate = DateTime.Today;
        CustomEndTime = TimeSpan.FromHours(23) + TimeSpan.FromMinutes(59) + TimeSpan.FromSeconds(59);
    }

    public string[] RangeOptions { get; }
    public ObservableCollection<string> DeviceOptions { get; } = [];

    [ObservableProperty] ObservableCollection<HistoryTableRow> tableRows = [];
    [ObservableProperty] string headerLine = string.Empty;
    [ObservableProperty] string selectedRange = "最近 24 小时";
    [ObservableProperty] string selectedDevice = "全部设备";
    [ObservableProperty] string summaryText = "点「刷新」加载历史数据。";
    [ObservableProperty] string statusMessage = string.Empty;
    [ObservableProperty] bool isBusy;
    [ObservableProperty] bool isLoadingMore;
    [ObservableProperty] bool canLoadMore;
    [ObservableProperty] bool showEmpty;
    [ObservableProperty] bool useCustomStart = true;
    [ObservableProperty] bool useCustomEnd = true;
    [ObservableProperty] DateTime customStartDate;
    [ObservableProperty] TimeSpan customStartTime;
    [ObservableProperty] DateTime customEndDate;
    [ObservableProperty] TimeSpan customEndTime;
    [ObservableProperty] bool showCustomTimeFilters;

    public Task InitializeAsync()
    {
        if (!_settings.Current.EnableHistoryRecording)
        {
            SummaryText = "历史记录已关闭，可在「设置」中开启。";
            ShowEmpty = true;
        }
        else if (TableRows.Count == 0)
        {
            ShowEmpty = true;
        }

        return Task.CompletedTask;
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
            var (from, to) = ResolveQueryRange();
            _queryFrom = from;
            _queryTo = to;
            _queryDeviceFilter = SelectedDevice == "全部设备" ? null : SelectedDevice;
            _currentOffset = 0;
            _fixedColumns = [];

            var countQuery = BuildCountQuery();
            _totalCount = await _store.CountMatchingAsync(countQuery).ConfigureAwait(false);
            var devices = await _store.GetDeviceIdsAsync().ConfigureAwait(false);
            _preferredTags = _settings.Current.Tags
                .Where(tag => tag.Enabled)
                .Select(tag => tag.Name)
                .Take(HistoryTableFormatting.MaxColumns)
                .ToList();

            var table = await LoadPageAsync(0).ConfigureAwait(false);

            await MainThread.InvokeOnMainThreadAsync(() =>
            {
                ApplyFilterOptions(devices, _queryDeviceFilter);
                TableRows = new ObservableCollection<HistoryTableRow>(table.Rows);
                _fixedColumns = table.Columns.ToList();
                HeaderLine = table.HeaderLine;
                ShowEmpty = TableRows.Count == 0;
                UpdateSummary();
                StatusMessage = string.Empty;
                UpdateLoadMoreState(table.Rows.Count);
            });
        }
        catch (Exception ex)
        {
            await MainThread.InvokeOnMainThreadAsync(() => StatusMessage = ex.Message);
        }
        finally
        {
            await MainThread.InvokeOnMainThreadAsync(() => IsBusy = false);
        }
    }

    partial void OnSelectedRangeChanged(string value) =>
        ShowCustomTimeFilters = value == "自定义时间";

    [RelayCommand]
    async Task LoadMoreAsync()
    {
        if (IsBusy || IsLoadingMore || !CanLoadMore)
        {
            return;
        }

        IsLoadingMore = true;
        try
        {
            _currentOffset += HistoryTableFormatting.PageSize;
            var table = await LoadPageAsync(_currentOffset).ConfigureAwait(false);
            var batchCount = table.Rows.Count;

            await MainThread.InvokeOnMainThreadAsync(() =>
            {
                foreach (var row in table.Rows)
                {
                    TableRows.Add(row);
                }

                UpdateSummary();
                UpdateLoadMoreState(batchCount);
            });
        }
        catch (Exception ex)
        {
            await MainThread.InvokeOnMainThreadAsync(() => StatusMessage = ex.Message);
            _currentOffset = Math.Max(0, _currentOffset - HistoryTableFormatting.PageSize);
        }
        finally
        {
            await MainThread.InvokeOnMainThreadAsync(() => IsLoadingMore = false);
        }
    }

    async Task<HistoryTableData> LoadPageAsync(int offset) =>
        await _store.QueryTableAsync(
            new HistoryQuery
            {
                From = _queryFrom,
                To = _queryTo,
                DeviceId = _queryDeviceFilter,
                Limit = HistoryTableFormatting.PageSize,
                Offset = offset
            },
            _settings.Current.TemperaturePrecision,
            _preferredTags,
            _fixedColumns.Count > 0 ? _fixedColumns : null).ConfigureAwait(false);

    void UpdateLoadMoreState(int? lastBatchCount = null)
    {
        var hasMoreByCount = TableRows.Count < _totalCount;
        var receivedFullPage = lastBatchCount is null || lastBatchCount >= HistoryTableFormatting.PageSize;
        CanLoadMore = hasMoreByCount && receivedFullPage;
    }

    void UpdateSummary()
    {
        SummaryText = _totalCount == 0
            ? "暂无历史数据。启动采集或订阅后会自动记录。"
            : CanLoadMore
                ? $"共 {_totalCount} 条 · 已加载 {TableRows.Count} 条 · 下滑加载更多"
                : $"共 {_totalCount} 条 · 已全部加载";
    }

    HistoryQuery BuildCountQuery() =>
        new()
        {
            From = _queryFrom,
            To = _queryTo,
            DeviceId = _queryDeviceFilter
        };

    void ApplyFilterOptions(IReadOnlyList<string> devices, string? deviceFilter)
    {
        _suppressFilterRefresh = true;
        try
        {
            DeviceOptions.Clear();
            DeviceOptions.Add("全部设备");
            foreach (var device in devices)
            {
                DeviceOptions.Add(device);
            }

            if (deviceFilter is not null && DeviceOptions.Contains(deviceFilter))
            {
                SelectedDevice = deviceFilter;
            }
            else if (!DeviceOptions.Contains(SelectedDevice))
            {
                SelectedDevice = "全部设备";
            }
        }
        finally
        {
            _suppressFilterRefresh = false;
        }
    }

    [RelayCommand]
    async Task DeleteRowAsync(HistoryTableRow? row)
    {
        if (row is null || IsBusy)
        {
            return;
        }

        var confirm = await Shell.Current.DisplayAlertAsync(
            "删除历史记录",
            $"确定删除 {row.RecordedAtText} 的记录？",
            "删除",
            "取消");
        if (!confirm)
        {
            return;
        }

        IsBusy = true;
        try
        {
            if (await _store.DeleteSampleAsync(row.SampleId).ConfigureAwait(false))
            {
                _totalCount = Math.Max(0, _totalCount - 1);
                await MainThread.InvokeOnMainThreadAsync(() =>
                {
                    TableRows.Remove(row);
                    ShowEmpty = TableRows.Count == 0;
                    StatusMessage = "已删除 1 条记录";
                    UpdateSummary();
                    UpdateLoadMoreState();
                });
            }
            else
            {
                StatusMessage = "删除失败，记录可能已不存在。";
            }
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
    async Task ClearFilteredAsync()
    {
        if (IsBusy)
        {
            return;
        }

        var (from, to) = ResolveQueryRange();
        var deviceLabel = SelectedDevice == "全部设备" ? "全部设备" : SelectedDevice;
        var rangeLabel = BuildFilterRangeLabel(from, to);
        var confirm = await Shell.Current.DisplayAlertAsync(
            "删除历史记录",
            $"确定删除「{rangeLabel} · {deviceLabel}」下的所有历史记录？此操作不可恢复。",
            "删除",
            "取消");
        if (!confirm)
        {
            return;
        }

        IsBusy = true;
        try
        {
            var deleted = await _store.DeleteMatchingAsync(new HistoryQuery
            {
                From = from,
                To = to,
                DeviceId = SelectedDevice == "全部设备" ? null : SelectedDevice
            }).ConfigureAwait(false);
            StatusMessage = deleted == 0 ? "没有可删除的记录。" : $"已删除 {deleted} 条记录，点「刷新」查看最新。";
            ClearDisplayedTable();
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
    async Task ClearAllAsync()
    {
        if (IsBusy)
        {
            return;
        }

        var stats = await _store.GetStatsAsync().ConfigureAwait(false);
        if (stats.SampleCount == 0)
        {
            StatusMessage = "没有可删除的记录。";
            return;
        }

        var confirm = await Shell.Current.DisplayAlertAsync(
            "清空历史数据",
            $"确定删除全部 {stats.SampleCount} 条历史记录？此操作不可恢复。",
            "清空",
            "取消");
        if (!confirm)
        {
            return;
        }

        IsBusy = true;
        try
        {
            var deleted = await _store.DeleteAllAsync().ConfigureAwait(false);
            StatusMessage = $"已删除 {deleted} 条记录，点「刷新」查看最新。";
            ClearDisplayedTable();
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

    void ClearDisplayedTable()
    {
        TableRows.Clear();
        HeaderLine = string.Empty;
        _fixedColumns = [];
        _currentOffset = 0;
        _totalCount = 0;
        CanLoadMore = false;
        ShowEmpty = true;
        SummaryText = "点「刷新」重新加载历史数据。";
    }

    static (DateTimeOffset From, DateTimeOffset To) ResolvePresetRange(string selected) =>
        selected switch
        {
            "最近 7 天" => (DateTimeOffset.Now.AddDays(-7), DateTimeOffset.Now),
            "最近 30 天" => (DateTimeOffset.Now.AddDays(-30), DateTimeOffset.Now),
            _ => (DateTimeOffset.Now.AddHours(-24), DateTimeOffset.Now)
        };

    (DateTimeOffset From, DateTimeOffset To) ResolveQueryRange()
    {
        if (SelectedRange != "自定义时间")
        {
            return ResolvePresetRange(SelectedRange);
        }

        if (!UseCustomStart && !UseCustomEnd)
        {
            throw new InvalidOperationException("自定义时间至少需设置开始或结束时间。");
        }

        var from = UseCustomStart
            ? ToLocalOffset(CustomStartDate, CustomStartTime)
            : new DateTimeOffset(2000, 1, 1, 0, 0, 0, TimeZoneInfo.Local.GetUtcOffset(new DateTime(2000, 1, 1)));
        var to = UseCustomEnd
            ? ToLocalOffset(CustomEndDate, CustomEndTime)
            : DateTimeOffset.Now;

        if (from > to)
        {
            throw new InvalidOperationException("开始时间不能晚于结束时间。");
        }

        return (from, to);
    }

    static DateTimeOffset ToLocalOffset(DateTime date, TimeSpan time)
    {
        var local = date.Date + time;
        return new DateTimeOffset(local, TimeZoneInfo.Local.GetUtcOffset(local));
    }

    string BuildFilterRangeLabel(DateTimeOffset from, DateTimeOffset to)
    {
        if (SelectedRange != "自定义时间")
        {
            return SelectedRange;
        }

        var startText = UseCustomStart ? from.ToLocalTime().ToString("yyyy-MM-dd HH:mm") : "最早";
        var endText = UseCustomEnd ? to.ToLocalTime().ToString("yyyy-MM-dd HH:mm") : "现在";
        return $"{startText} ~ {endText}";
    }
}
