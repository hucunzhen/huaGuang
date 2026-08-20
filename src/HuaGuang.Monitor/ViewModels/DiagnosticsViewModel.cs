using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HuaGuang.Monitor.Diagnostics;
using HuaGuang.Monitor.Services;

namespace HuaGuang.Monitor.ViewModels;

public partial class DiagnosticsViewModel : ObservableObject
{
    readonly SubscriptionService _subscription;
    readonly DashboardViewModel _dashboard;

    public DiagnosticsViewModel(SubscriptionService subscription, DashboardViewModel dashboard)
    {
        _subscription = subscription;
        _dashboard = dashboard;
    }

    public ObservableCollection<DiagnosticResult> Results { get; } = [];

    [ObservableProperty] string summaryText = "尚未运行测试";
    [ObservableProperty] bool isRunning;
    [ObservableProperty] string statusMessage = "运行核心测试与订阅模拟，检查软件是否正常工作。";

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
}
