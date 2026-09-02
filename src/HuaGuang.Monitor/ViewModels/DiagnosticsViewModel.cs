using System.Collections.ObjectModel;
using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HuaGuang.Monitor.Diagnostics;
using HuaGuang.Monitor.Messaging;
using HuaGuang.Monitor.Models;
using HuaGuang.Monitor.Services;
using HuaGuang.Monitor.Services.Logging;

namespace HuaGuang.Monitor.ViewModels;

public partial class DiagnosticsViewModel : ObservableObject, IDisposable
{
    const int MaxVisibleLogEntries = 800;

    readonly RuntimeLogStore _logStore;
    readonly IMonitorAcquisition _acquisition;
    readonly IMonitorSubscription _subscription;
    readonly SettingsStore _settings;
    readonly DashboardViewModel _dashboard;

    public DiagnosticsViewModel(
        RuntimeLogStore logStore,
        IMonitorAcquisition acquisition,
        IMonitorSubscription subscription,
        SettingsStore settings,
        DashboardViewModel dashboard)
    {
        _logStore = logStore;
        _acquisition = acquisition;
        _subscription = subscription;
        _settings = settings;
        _dashboard = dashboard;
        AppVersionText = AppVersionInfo.Display;
        LogPathText = AppPaths.CurrentRuntimeLogFile;
    }

    public string AppVersionText { get; }
    public string LogPathText { get; }

    public ObservableCollection<RuntimeLogEntry> LogEntries { get; } = [];
    public ObservableCollection<DiagnosticResult> Results { get; } = [];

    [ObservableProperty] string serviceStatusText = "服务状态加载中…";
    [ObservableProperty] string logSummaryText = "0 条日志";
    [ObservableProperty] bool showAllLogCategories;
    [ObservableProperty] string summaryText = "尚未运行测试";
    [ObservableProperty] bool isRunning;
    [ObservableProperty] string statusMessage = "采集/推送日志在此查看；下方可进行软件自检。";

    public void OnAppearing()
    {
        _logStore.EntryAdded += OnLogEntryAdded;
        _acquisition.ConnectionChanged += OnServiceStateChanged;
        _subscription.ConnectionChanged += OnServiceStateChanged;
        RefreshServiceStatus();
        ReloadLogs();
    }

    public void OnDisappearing()
    {
        _logStore.EntryAdded -= OnLogEntryAdded;
        _acquisition.ConnectionChanged -= OnServiceStateChanged;
        _subscription.ConnectionChanged -= OnServiceStateChanged;
    }

    void OnServiceStateChanged(object? sender, EventArgs e) =>
        MainThread.BeginInvokeOnMainThread(RefreshServiceStatus);

    void OnLogEntryAdded(object? sender, EventArgs e)
    {
        var snapshot = _logStore.Snapshot();
        if (snapshot.Count == 0)
        {
            return;
        }

        var latest = snapshot[^1];
        if (!ShowAllLogCategories && !RuntimeLogStore.IsAcquisitionOrMqttCategory(latest.Category))
        {
            return;
        }

        MainThread.BeginInvokeOnMainThread(() =>
        {
            LogEntries.Add(latest);
            TrimVisibleLogs();
            LogSummaryText = $"{LogEntries.Count} 条日志";
        });
    }

    partial void OnShowAllLogCategoriesChanged(bool value) => ReloadLogs();

    [RelayCommand]
    void RefreshLogs()
    {
        RefreshServiceStatus();
        ReloadLogs();
    }

    [RelayCommand]
    void ClearLogs()
    {
        _logStore.Clear();
        LogEntries.Clear();
        LogSummaryText = "0 条日志";
    }

    [RelayCommand]
    void OpenLogFolder()
    {
        StatusMessage = $"日志文件：{LogPathText}";
    }

    void ReloadLogs()
    {
        LogEntries.Clear();
        foreach (var entry in _logStore.Snapshot())
        {
            if (ShowAllLogCategories || RuntimeLogStore.IsAcquisitionOrMqttCategory(entry.Category))
            {
                LogEntries.Add(entry);
            }
        }

        TrimVisibleLogs();
        LogSummaryText = $"{LogEntries.Count} 条日志";
    }

    void TrimVisibleLogs()
    {
        while (LogEntries.Count > MaxVisibleLogEntries)
        {
            LogEntries.RemoveAt(0);
        }
    }

    void RefreshServiceStatus()
    {
        var settings = _settings.Current;
        var builder = new StringBuilder();

        if (settings.OperationMode == AppOperationMode.Subscribe)
        {
            builder.AppendLine($"订阅模式 · {( _subscription.IsRunning ? "运行中" : "已停止")}");
            builder.AppendLine($"MQTT {( _subscription.IsConnected ? "已连接" : "未连接")}");
            if (!string.IsNullOrWhiteSpace(_subscription.LastError))
            {
                builder.AppendLine($"最近错误：{_subscription.LastError}");
            }

            if (!string.IsNullOrWhiteSpace(_subscription.LastPayload))
            {
                builder.AppendLine($"最近报文：{LogFormatting.Truncate(_subscription.LastPayload, 240)}");
            }
        }
        else
        {
            builder.AppendLine($"采集模式 · {(settings.UseSimulator ? "模拟" : "PLC")} · {( _acquisition.IsRunning ? "运行中" : "已停止")}");
            builder.AppendLine($"PLC {(settings.UseSimulator ? "模拟" : _acquisition.PlcConnected ? "已连接" : "未连接")} · MQTT {(_acquisition.MqttConnected ? "已连接" : "未连接")}");

            if (_acquisition.IsRunning)
            {
                builder.AppendLine(
                    $"周期 {_acquisition.ActiveScanIntervalMs / 1000.0:G}s · 已完成 {_acquisition.CycleCount} 次 · 待发送 {_acquisition.MqttPendingCount}");
                if (_acquisition.LastCycleCompletedAt is { } completedAt)
                {
                    builder.AppendLine($"最近刷新 {completedAt.LocalDateTime:HH:mm:ss} · PLC {_acquisition.LastPlcElapsedMs:0}ms · 等待 {_acquisition.LastWaitElapsedMs:0}ms");
                }
            }

            if (!string.IsNullOrWhiteSpace(_acquisition.LastError))
            {
                builder.AppendLine($"最近错误：{_acquisition.LastError}");
            }

            if (!string.IsNullOrWhiteSpace(_acquisition.LastPublishNote))
            {
                builder.AppendLine($"推送说明：{_acquisition.LastPublishNote}");
            }

            if (!string.IsNullOrWhiteSpace(_acquisition.LastPayload))
            {
                builder.AppendLine($"最近报文：{LogFormatting.Truncate(_acquisition.LastPayload, 240)}");
            }
        }

        ServiceStatusText = builder.ToString().TrimEnd();
    }

    [RelayCommand]
    async Task RunCoreTestsAsync()
    {
        if (IsRunning)
        {
            return;
        }

        IsRunning = true;
        StatusMessage = "正在运行核心测试…";
        Results.Clear();

        try
        {
            await Task.Run(() =>
            {
                foreach (var result in MonitorSelfTests.RunCoreTests())
                {
                    MainThread.BeginInvokeOnMainThread(() => Results.Add(result));
                }
            });

            UpdateSummary();
            StatusMessage = "核心测试完成。";
        }
        finally
        {
            IsRunning = false;
        }
    }

    [RelayCommand]
    async Task RunAllTestsAsync()
    {
        if (IsRunning)
        {
            return;
        }

        IsRunning = true;
        StatusMessage = "正在运行全部测试（含订阅模拟）…";
        Results.Clear();

        try
        {
            await Task.Run(() =>
            {
                foreach (var result in MonitorSelfTests.RunCoreTests())
                {
                    MainThread.BeginInvokeOnMainThread(() => Results.Add(result));
                }

                foreach (var result in MonitorSelfTests.RunIntegrationTests(_subscription, _dashboard))
                {
                    MainThread.BeginInvokeOnMainThread(() => Results.Add(result));
                }
            });

            UpdateSummary();
            StatusMessage = "全部测试完成。";
        }
        finally
        {
            IsRunning = false;
        }
    }

    [RelayCommand]
    async Task RunStressTestAsync()
    {
        if (IsRunning)
        {
            return;
        }

        IsRunning = true;
        StatusMessage = "正在运行压力测试（500 次遥测更新）…";

        try
        {
            var result = await Task.Run(() =>
            {
                try
                {
                    for (var i = 0; i < 500; i++)
                    {
                        _subscription.InjectTelemetry(
                            "monitor/stress/telemetry",
                            $$"""
                            {
                              "deviceId": "STRESS",
                              "timestamp": "2026-08-20T08:00:00Z",
                              "quality": "Good",
                              "tags": { "车速": {{i % 100}}.5, "门幅": 1200.0 }
                            }
                            """);
                    }

                    return new DiagnosticResult
                    {
                        Name = "压力测试 500 次",
                        Passed = _subscription.Devices.Count <= 64,
                        Message = $"设备缓存 {_subscription.Devices.Count} 条，无异常",
                        Duration = TimeSpan.Zero
                    };
                }
                catch (Exception ex)
                {
                    return new DiagnosticResult
                    {
                        Name = "压力测试 500 次",
                        Passed = false,
                        Message = ex.Message,
                        Duration = TimeSpan.Zero
                    };
                }
            });

            Results.Add(result);
            UpdateSummary();
            StatusMessage = result.Passed ? "压力测试通过。" : "压力测试失败。";
        }
        finally
        {
            IsRunning = false;
        }
    }

    void UpdateSummary()
    {
        var passed = Results.Count(result => result.Passed);
        var failed = Results.Count - passed;
        SummaryText = failed == 0
            ? $"全部通过：{passed} 项"
            : $"通过 {passed} 项，失败 {failed} 项";
    }

    public void Dispose() => OnDisappearing();
}
