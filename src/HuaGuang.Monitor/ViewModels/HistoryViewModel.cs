using System.Collections.ObjectModel;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HuaGuang.Monitor.Models;
using HuaGuang.Monitor.Services;
using HuaGuang.Monitor.Services.LogExport;

namespace HuaGuang.Monitor.ViewModels;

public partial class HistoryViewModel : ObservableObject
{
#if !WINDOWS
    const string ExcelMimeType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";
#endif

    readonly HistoryStore _store;
    readonly SettingsStore _settings;
#if !WINDOWS
    readonly ILogExportLocationService _exportLocation;
#endif

    bool _suppressFilterRefresh;
    bool _refreshQueued;
    int _deviceRefreshSerial;
    bool _hasActiveQuery;
    int _totalCount;
    DateTimeOffset _queryFrom;
    DateTimeOffset _queryTo;
    string? _queryDeviceFilter;
    List<string> _preferredTags = [];
    Dictionary<string, string?> _tagUnitHints = new(StringComparer.Ordinal);
    List<HistoryTableColumn> _fixedColumns = [];
    List<HistoryTableColumn> _widthSubscriptions = [];
    IReadOnlyList<PlcTag> _catalogTags = [];
    MqttPayloadProfile _mqttProfile = new();

    public HistoryViewModel(HistoryStore store, SettingsStore settings, ILogExportLocationService exportLocation)
    {
        _store = store;
        _settings = settings;
#if WINDOWS
        _ = exportLocation;
#else
        _exportLocation = exportLocation;
#endif
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
    [ObservableProperty] ObservableCollection<HistoryTableColumn> tableColumns = [];
    [ObservableProperty] double timeColumnWidth = HistoryTableFormatting.TimeColumnWidth;
    [ObservableProperty] double deviceColumnWidth = HistoryTableFormatting.DeviceColumnWidth;
    [ObservableProperty] double tableContentMinWidth;
    [ObservableProperty] string selectedRange = "最近 24 小时";
    [ObservableProperty] string selectedDevice = "全部设备";
    [ObservableProperty] string summaryText = "点「刷新」加载历史数据。";
    [ObservableProperty] string statusMessage = string.Empty;
    [ObservableProperty] bool isBusy;
    [ObservableProperty] bool showEmpty;
    [ObservableProperty] int currentPage = 1;
    [ObservableProperty] int totalPages = 1;
    [ObservableProperty] string pageNumberInput = "1";
    [ObservableProperty] bool canGoToPreviousPage;
    [ObservableProperty] bool canGoToNextPage;
    [ObservableProperty] bool showPagination;
    [ObservableProperty] bool useCustomStart = true;
    [ObservableProperty] bool useCustomEnd = true;
    [ObservableProperty] DateTime customStartDate;
    [ObservableProperty] TimeSpan customStartTime;
    [ObservableProperty] DateTime customEndDate;
    [ObservableProperty] TimeSpan customEndTime;
    [ObservableProperty] bool showCustomTimeFilters;
    [ObservableProperty] bool isExporting;
    [ObservableProperty] double exportProgress;
    [ObservableProperty] string exportProgressText = string.Empty;

    public bool ShowBusySpinner => IsBusy && !IsExporting;

    partial void OnIsExportingChanged(bool value) => OnPropertyChanged(nameof(ShowBusySpinner));

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

    /// <summary>切换回历史页时仅在尚未加载数据时自动刷新，避免重复重绘卡死。</summary>
    public Task RefreshOnAppearAsync() =>
        TableRows.Count == 0 && _settings.Current.EnableHistoryRecording
            ? RefreshDataAsync()
            : Task.CompletedTask;

    [RelayCommand]
    Task RefreshAsync() => RefreshDataAsync();

    [RelayCommand]
    async Task ExportAsync()
    {
        if (IsBusy)
        {
            return;
        }

        IsBusy = true;
        string? tempPath = null;
        var previousSummary = SummaryText;
        try
        {
            await _settings.LoadAsyncIfChanged();
            var (from, to) = ResolveQueryRange();
            var selectedDevice = SelectedDevice;
            var deviceFilter = ResolveDeviceFilter(selectedDevice);
            var count = await _store.CountMatchingAsync(new HistoryQuery
            {
                From = from,
                To = to,
                DeviceId = deviceFilter
            });
            if (count == 0)
            {
                StatusMessage = "当前筛选条件下没有可导出的记录。";
                return;
            }

            var fileName = BuildExportFileName(deviceFilter);
            var destination = await PickExportDestinationAsync(fileName);
            if (destination is null)
            {
                return;
            }

            StatusMessage = string.Empty;
            SummaryText = $"正在导出 {count} 条…";
            IsExporting = true;
            ReportExportProgress(0, count);

            var catalogTags = selectedDevice is "全部设备" or null
                ? _settings.Current.Tags
                : SubscribeLineCatalog.ResolveTags(selectedDevice, null, _settings.Current.Tags);
            var preferredTags = _settings.Current.Tags
                .Where(tag => tag.Enabled)
                .Select(tag => tag.Name)
                .ToList();
            var tagUnits = _settings.Current.Tags
                .Where(tag => tag.Enabled)
                .GroupBy(tag => tag.Name, StringComparer.Ordinal)
                .ToDictionary(group => group.Key, group => (string?)group.First().Unit, StringComparer.Ordinal);
            var precision = _settings.Current.TemperaturePrecision;
            var mqttProfile = _settings.Current.MqttPayload ?? new MqttPayloadProfile();

            tempPath = Path.Combine(Path.GetTempPath(), $"huaguang-history-{Guid.NewGuid():N}.xlsx");
            IReadOnlyList<HistoryTableColumn>? fixedColumns = null;
            HistoryExcelWriter? writer = null;
            var written = 0;
            try
            {
                var offset = 0;
                while (offset < count)
                {
                    var table = await _store.QueryTableAsync(
                        new HistoryQuery
                        {
                            From = from,
                            To = to,
                            DeviceId = deviceFilter,
                            Limit = HistoryExcelWriter.ExportPageSize,
                            Offset = offset
                        },
                        precision,
                        preferredTags,
                        fixedColumns,
                        tagUnits,
                        catalogTags,
                        mqttProfile);
                    if (table.Rows.Count == 0)
                    {
                        break;
                    }

                    fixedColumns ??= table.Columns;
                    writer ??= new HistoryExcelWriter(fixedColumns);
                    writer.AddRows(table.Rows);
                    written += table.Rows.Count;
                    offset += table.Rows.Count;
                    ReportExportProgress(written, count);
                    if (table.Rows.Count < HistoryExcelWriter.ExportPageSize)
                    {
                        break;
                    }
                }

                if (writer is null || written == 0)
                {
                    SummaryText = previousSummary;
                    StatusMessage = "当前筛选条件下没有可导出的记录。";
                    return;
                }

                ReportExportProgress(written, count, "正在写入 Excel…", 0.95);
                await Task.Yield();
                var saving = writer;
                writer = null;
                var exportPath = tempPath;
                await Task.Run(() =>
                {
                    try
                    {
                        saving.Save(exportPath);
                    }
                    finally
                    {
                        saving.Dispose();
                    }
                });
            }
            finally
            {
                writer?.Dispose();
            }

            ReportExportProgress(written, count, "正在保存文件…", 0.98);
            await Task.Yield();
            var delivered = await Task.Run(() => destination.DeliverAsync(tempPath, fileName));
            ReportExportProgress(written, count, "导出完成", 1);
            SummaryText = $"已导出 {written} 条到 {delivered}";
        }
        catch (Exception ex)
        {
            SummaryText = previousSummary;
            StatusMessage = ex.Message;
        }
        finally
        {
            IsBusy = false;
            IsExporting = false;
            ExportProgress = 0;
            ExportProgressText = string.Empty;
            if (tempPath is not null)
            {
                try
                {
                    File.Delete(tempPath);
                }
                catch (IOException)
                {
                }
            }
        }
    }

    void ReportExportProgress(int written, int total, string? phase = null, double? progress = null)
    {
        var fraction = total <= 0 ? 0 : Math.Clamp(written / (double)total, 0, 1);
        ExportProgress = progress ?? fraction * 0.9;
        ExportProgressText = phase ?? $"已导出 {written} / {total}";
    }

    async Task<ExportDestination?> PickExportDestinationAsync(string fileName)
    {
#if WINDOWS
        var picked = await PickWindowsExcelPathAsync(Path.GetFileNameWithoutExtension(fileName));
        if (string.IsNullOrWhiteSpace(picked))
        {
            return null;
        }

        if (!picked.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase))
        {
            picked += ".xlsx";
        }

        var path = picked;
        return new ExportDestination((temp, _) =>
        {
            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.Copy(temp, path, overwrite: true);
            return Task.FromResult(path);
        });
#else
        var choice = await _exportLocation.PickDirectoryAsync(_settings.Current.LogExportDirectory);
        if (choice is null)
        {
            return null;
        }

        return new ExportDestination(async (temp, name) =>
        {
            var delivered = await _exportLocation.WriteFileAsync(choice, temp, name, ExcelMimeType);
            return delivered.DisplayPath;
        });
#endif
    }

#if WINDOWS
    static async Task<string?> PickWindowsExcelPathAsync(string suggestedName)
    {
        var window = Microsoft.Maui.Controls.Application.Current?.Windows.FirstOrDefault()?.Handler?.PlatformView
                     as Microsoft.UI.Xaml.Window;
        if (window is null)
        {
            throw new InvalidOperationException("无法打开保存对话框：窗口不可用。");
        }

        var picker = new global::Windows.Storage.Pickers.FileSavePicker
        {
            SuggestedStartLocation = global::Windows.Storage.Pickers.PickerLocationId.DocumentsLibrary,
            SuggestedFileName = suggestedName
        };
        picker.FileTypeChoices.Add("Excel 工作簿", new List<string> { ".xlsx" });
        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(window);
        WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);
        var file = await picker.PickSaveFileAsync();
        return file?.Path;
    }
#endif

    static string BuildExportFileName(string? deviceFilter)
    {
        var devicePart = string.IsNullOrWhiteSpace(deviceFilter) ? "全部设备" : deviceFilter;
        var invalid = Path.GetInvalidFileNameChars();
        var sanitized = new string(devicePart.Select(ch => invalid.Contains(ch) ? '_' : ch).ToArray()).Trim();
        if (sanitized.Length > 40)
        {
            sanitized = sanitized[..40];
        }

        if (string.IsNullOrWhiteSpace(sanitized))
        {
            sanitized = "全部设备";
        }

        return $"历史数据-{sanitized}-{DateTime.Now:yyyyMMdd-HHmmss}.xlsx";
    }

    sealed class ExportDestination
    {
        readonly Func<string, string, Task<string>> _deliver;

        public ExportDestination(Func<string, string, Task<string>> deliver) => _deliver = deliver;

        public Task<string> DeliverAsync(string tempPath, string fileName) => _deliver(tempPath, fileName);
    }

    async Task RefreshDataAsync()
    {
        if (IsBusy)
        {
            _refreshQueued = true;
            return;
        }

        await RunOnUiAsync(() => IsBusy = true).ConfigureAwait(false);
        try
        {
            do
            {
                _refreshQueued = false;
                var selectedDevice = SelectedDevice;
                await Task.Delay(1).ConfigureAwait(false);

                await _settings.LoadAsyncIfChanged().ConfigureAwait(false);

                var (from, to) = ResolveQueryRange();
                _queryFrom = from;
                _queryTo = to;
                _queryDeviceFilter = ResolveDeviceFilter(selectedDevice);
                _fixedColumns = [];

                var catalogTags = selectedDevice is "全部设备" or null
                    ? _settings.Current.Tags
                    : SubscribeLineCatalog.ResolveTags(selectedDevice, null, _settings.Current.Tags);
                var mqttProfile = _settings.Current.MqttPayload ?? new MqttPayloadProfile();
                var countQuery = BuildCountQuery();
                _totalCount = await _store.CountMatchingAsync(countQuery).ConfigureAwait(false);
                var devices = await _store.GetDeviceIdsAsync().ConfigureAwait(false);
                _catalogTags = catalogTags;
                _mqttProfile = mqttProfile;
                _preferredTags = _settings.Current.Tags
                    .Where(tag => tag.Enabled)
                    .Select(tag => tag.Name)
                    .ToList();
                _tagUnitHints = _settings.Current.Tags
                    .Where(tag => tag.Enabled)
                    .GroupBy(tag => tag.Name, StringComparer.Ordinal)
                    .ToDictionary(group => group.Key, group => (string?)group.First().Unit, StringComparer.Ordinal);

                var table = await LoadPageAsync(0).ConfigureAwait(false);
                _hasActiveQuery = true;

                await RunOnUiAsync(() =>
                {
                    CurrentPage = 1;
                    ApplyFilterOptions(devices, selectedDevice == "全部设备" ? null : selectedDevice);
                    ReplaceTableColumns(table.Columns);
                    ApplyPageRows(table.Rows);
                    ShowEmpty = _totalCount == 0;
                    StatusMessage = string.Empty;
                    UpdatePaginationState();
                }).ConfigureAwait(false);
            }
            while (_refreshQueued);
        }
        catch (Exception ex)
        {
            await RunOnUiAsync(() => StatusMessage = ex.Message).ConfigureAwait(false);
        }
        finally
        {
            await RunOnUiAsync(() => IsBusy = false).ConfigureAwait(false);
        }
    }

    partial void OnSelectedRangeChanged(string value) =>
        ShowCustomTimeFilters = value == "自定义时间";

    partial void OnSelectedDeviceChanged(string value)
    {
        if (_suppressFilterRefresh || string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        if (!_settings.Current.EnableHistoryRecording)
        {
            return;
        }

        var serial = Interlocked.Increment(ref _deviceRefreshSerial);
        _ = RefreshAfterDeviceSelectedAsync(serial);
    }

    async Task RefreshAfterDeviceSelectedAsync(int serial)
    {
        try
        {
            await Task.Delay(200).ConfigureAwait(false);
            if (serial != Volatile.Read(ref _deviceRefreshSerial))
            {
                return;
            }

            await RefreshDataAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            await RunOnUiAsync(() => StatusMessage = ex.Message).ConfigureAwait(false);
        }
    }

    partial void OnIsBusyChanged(bool value)
    {
        OnPropertyChanged(nameof(ShowBusySpinner));
        PreviousPageCommand.NotifyCanExecuteChanged();
        NextPageCommand.NotifyCanExecuteChanged();
        GoToPageCommand.NotifyCanExecuteChanged();
    }

    partial void OnTimeColumnWidthChanged(double value) => RecalculateTableContentMinWidth();

    partial void OnDeviceColumnWidthChanged(double value) => RecalculateTableContentMinWidth();

    bool CanExecutePageNavigation() => !IsBusy && _hasActiveQuery && _totalCount > 0;

    [RelayCommand(CanExecute = nameof(CanExecutePreviousPage))]
    Task PreviousPageAsync() => GoToPageAsync(CurrentPage - 1);

    bool CanExecutePreviousPage() => CanExecutePageNavigation() && CurrentPage > 1;

    [RelayCommand(CanExecute = nameof(CanExecuteNextPage))]
    Task NextPageAsync() => GoToPageAsync(CurrentPage + 1);

    bool CanExecuteNextPage() => CanExecutePageNavigation() && CurrentPage < TotalPages;

    [RelayCommand(CanExecute = nameof(CanExecutePageNavigation))]
    Task GoToPageAsync()
    {
        if (!int.TryParse(PageNumberInput?.Trim(), out var page))
        {
            StatusMessage = "请输入有效页码。";
            return Task.CompletedTask;
        }

        return GoToPageAsync(page);
    }

    async Task GoToPageAsync(int targetPage, bool allowWhileBusy = false)
    {
        if (!allowWhileBusy && IsBusy)
        {
            return;
        }

        if (!_hasActiveQuery || _totalCount <= 0)
        {
            if (!_hasActiveQuery)
            {
                StatusMessage = "请先点「刷新」加载数据。";
            }

            return;
        }

        TotalPages = CalculateTotalPages();
        var page = Math.Clamp(targetPage, 1, TotalPages);
        if (page == CurrentPage && TableRows.Count > 0)
        {
            PageNumberInput = page.ToString();
            UpdatePaginationState();
            return;
        }

        IsBusy = true;
        try
        {
            var offset = (page - 1) * HistoryTableFormatting.PageSize;
            var table = await LoadPageAsync(offset).ConfigureAwait(false);

            await RunOnUiAsync(() =>
            {
                CurrentPage = page;
                ApplyPageRows(table.Rows);
                ShowEmpty = _totalCount == 0;
                StatusMessage = string.Empty;
                UpdatePaginationState();
            }).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            await RunOnUiAsync(() => StatusMessage = ex.Message).ConfigureAwait(false);
        }
        finally
        {
            await RunOnUiAsync(() => IsBusy = false).ConfigureAwait(false);
        }
    }

    void ApplyPageRows(IReadOnlyList<HistoryTableRow> rows)
    {
        ApplyColumnLayout(rows);
        TableRows.Clear();
        foreach (var row in rows)
        {
            TableRows.Add(row);
        }

        UpdateSummary();
    }

    void ReplaceTableColumns(IReadOnlyList<HistoryTableColumn> columns)
    {
        UnsubscribeColumnWidthChanges();
        TableColumns.Clear();
        foreach (var column in columns)
        {
            TableColumns.Add(column);
        }

        _fixedColumns = TableColumns.ToList();
        SubscribeColumnWidthChanges();
    }

    int CalculateTotalPages() =>
        _totalCount <= 0
            ? 1
            : (int)Math.Ceiling(_totalCount / (double)HistoryTableFormatting.PageSize);

    void UpdatePaginationState()
    {
        TotalPages = CalculateTotalPages();
        if (CurrentPage > TotalPages)
        {
            CurrentPage = TotalPages;
        }

        PageNumberInput = CurrentPage.ToString();
        CanGoToPreviousPage = CurrentPage > 1;
        CanGoToNextPage = CurrentPage < TotalPages;
        ShowPagination = _totalCount > 0;
        UpdateSummary();
        PreviousPageCommand.NotifyCanExecuteChanged();
        NextPageCommand.NotifyCanExecuteChanged();
        GoToPageCommand.NotifyCanExecuteChanged();
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
            _fixedColumns.Count > 0 ? _fixedColumns : null,
            _tagUnitHints,
            _catalogTags,
            _mqttProfile).ConfigureAwait(false);

    void UpdateSummary()
    {
        var deviceLabel = string.IsNullOrWhiteSpace(_queryDeviceFilter) ? "全部设备" : _queryDeviceFilter;
        SummaryText = _totalCount == 0
            ? $"「{deviceLabel}」暂无历史数据。"
            : TotalPages <= 1
                ? $"「{deviceLabel}」共 {_totalCount} 条"
                : $"「{deviceLabel}」共 {_totalCount} 条 · 第 {CurrentPage}/{TotalPages} 页 · 本页 {TableRows.Count} 条";
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
            if (!DeviceOptionsMatch(devices))
            {
                DeviceOptions.Clear();
                DeviceOptions.Add("全部设备");
                foreach (var device in devices)
                {
                    if (!DeviceOptions.Contains(device))
                    {
                        DeviceOptions.Add(device);
                    }
                }
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

    bool DeviceOptionsMatch(IReadOnlyList<string> devices)
    {
        if (DeviceOptions.Count != devices.Count + 1 || DeviceOptions[0] != "全部设备")
        {
            return false;
        }

        for (var i = 0; i < devices.Count; i++)
        {
            if (!string.Equals(DeviceOptions[i + 1], devices[i], StringComparison.Ordinal))
            {
                return false;
            }
        }

        return true;
    }

    string? ResolveDeviceFilter(string selected) =>
        selected == "全部设备" ? null : selected;

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
                var reloadPage = CurrentPage;
                if (_totalCount == 0)
                {
                    await RunOnUiAsync(ClearDisplayedTable).ConfigureAwait(false);
                    StatusMessage = "已删除 1 条记录";
                    return;
                }

                TotalPages = CalculateTotalPages();
                if (reloadPage > TotalPages)
                {
                    reloadPage = TotalPages;
                }

                await GoToPageAsync(reloadPage, allowWhileBusy: true).ConfigureAwait(false);
                StatusMessage = "已删除 1 条记录";
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
                DeviceId = ResolveDeviceFilter(SelectedDevice)
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
        UnsubscribeColumnWidthChanges();
        TableRows.Clear();
        TableColumns.Clear();
        TableContentMinWidth = 0;
        TimeColumnWidth = HistoryTableFormatting.TimeColumnWidth;
        DeviceColumnWidth = HistoryTableFormatting.DeviceColumnWidth;
        _fixedColumns = [];
        _tagUnitHints = new Dictionary<string, string?>(StringComparer.Ordinal);
        _totalCount = 0;
        _hasActiveQuery = false;
        CurrentPage = 1;
        TotalPages = 1;
        PageNumberInput = "1";
        CanGoToPreviousPage = false;
        CanGoToNextPage = false;
        ShowPagination = false;
        ShowEmpty = true;
        SummaryText = "点「刷新」重新加载历史数据。";
        PreviousPageCommand.NotifyCanExecuteChanged();
        NextPageCommand.NotifyCanExecuteChanged();
        GoToPageCommand.NotifyCanExecuteChanged();
    }

    void ApplyColumnLayout(IReadOnlyList<HistoryTableRow> rows)
    {
        TimeColumnWidth = HistoryTableFormatting.EstimateHeaderColumnWidth(
            "时间",
            rows.Select(row => row.RecordedAtText),
            HistoryTableFormatting.TimeColumnWidth);
        DeviceColumnWidth = HistoryTableFormatting.EstimateHeaderColumnWidth(
            "设备",
            rows.Select(row => row.DeviceId),
            HistoryTableFormatting.DeviceColumnWidth);

        for (var index = 0; index < _fixedColumns.Count; index++)
        {
            var column = _fixedColumns[index];
            column.Width = HistoryTableFormatting.EstimateHeaderColumnWidth(
                column.HeaderText,
                rows.Select(row => index < row.TagCells.Count ? row.TagCells[index].Text : "—"),
                HistoryTableFormatting.TagColumnWidth);
        }

        foreach (var row in rows)
        {
            ApplyRowTagWidths(row);
        }

        RecalculateTableContentMinWidth();
    }

    void ApplyRowTagWidths(HistoryTableRow row)
    {
        for (var index = 0; index < row.TagCells.Count && index < _fixedColumns.Count; index++)
        {
            row.TagCells[index].Width = _fixedColumns[index].Width;
        }
    }

    void RecalculateTableContentMinWidth() =>
        TableContentMinWidth = HistoryTableFormatting.EstimateContentWidth(
            TimeColumnWidth,
            DeviceColumnWidth,
            _fixedColumns.Select(column => column.Width));

    void SubscribeColumnWidthChanges()
    {
        UnsubscribeColumnWidthChanges();
        foreach (var column in TableColumns)
        {
            column.PropertyChanged += OnColumnWidthChanged;
            _widthSubscriptions.Add(column);
        }
    }

    void UnsubscribeColumnWidthChanges()
    {
        foreach (var column in _widthSubscriptions)
        {
            column.PropertyChanged -= OnColumnWidthChanged;
        }

        _widthSubscriptions.Clear();
    }

    void OnColumnWidthChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(HistoryTableColumn.Width) || sender is not HistoryTableColumn column)
        {
            return;
        }

        var index = _fixedColumns.IndexOf(column);
        if (index < 0)
        {
            return;
        }

        foreach (var row in TableRows)
        {
            if (index < row.TagCells.Count)
            {
                row.TagCells[index].Width = column.Width;
            }
        }

        RecalculateTableContentMinWidth();
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

    static Task RunOnUiAsync(Action action)
    {
        if (MainThread.IsMainThread)
        {
            action();
            return Task.CompletedTask;
        }

        return MainThread.InvokeOnMainThreadAsync(action);
    }
}
