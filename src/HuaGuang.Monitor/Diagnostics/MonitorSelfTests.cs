using System.Diagnostics;
using HuaGuang.Monitor.Controls;
using HuaGuang.Monitor.Diagnostics;
using HuaGuang.Monitor.Services;
using HuaGuang.Monitor.ViewModels;

namespace HuaGuang.Monitor.Diagnostics;

public static class MonitorSelfTests
{
    public static IReadOnlyList<DiagnosticResult> RunCoreTests()
    {
        var results = MonitorCoreTests.RunAll().ToList();
        results.Add(Run("FitGrid 列数计算", TestFitGridColumns));
        return results;
    }

    public static IReadOnlyList<DiagnosticResult> RunIntegrationTests(
        SubscriptionService subscription,
        DashboardViewModel dashboard) =>
    [
        Run("遥测 JSON 解析", () => TestTelemetryParse(subscription)),
        Run("订阅增量更新", () => TestSubscribeUpdates(subscription)),
        Run("多设备同时订阅", () => TestMultiDeviceSubscribe(subscription)),
        Run("设备缓存上限", () => TestDeviceCacheLimit(subscription)),
    ];

    static DiagnosticResult Run(string name, Action test)
    {
        var stopwatch = Stopwatch.StartNew();
        try
        {
            test();
            stopwatch.Stop();
            return new DiagnosticResult
            {
                Name = name,
                Passed = true,
                Message = "通过",
                Duration = stopwatch.Elapsed
            };
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            return new DiagnosticResult
            {
                Name = name,
                Passed = false,
                Message = ex.Message,
                Duration = stopwatch.Elapsed
            };
        }
    }

    static void TestFitGridColumns()
    {
        var columns = FitGrid.ChooseColumns(20, 1200, 800, 176, 136);
        if (columns < 2 || columns > 20)
        {
            throw new InvalidOperationException("断言失败");
        }
    }

    static void TestTelemetryParse(SubscriptionService subscription)
    {
        subscription.InjectTelemetry(
            "monitor/test-line/telemetry",
            """
            {
              "deviceId": "TEST-LINE",
              "timestamp": "2026-08-20T08:00:00Z",
              "simulator": true,
              "plcHost": "127.0.0.1",
              "quality": "Good",
              "tags": {
                "车速": 45.2,
                "热溶胶盘温度（热熔胶机1）": 180.5
              }
            }
            """);

        var device = subscription.Devices.Values.FirstOrDefault(entry => entry.DeviceId == "TEST-LINE")
            ?? throw new InvalidOperationException("未解析到设备");
        if (device.Tags.Count != 2 || !device.Tags.ContainsKey("车速"))
        {
            throw new InvalidOperationException("断言失败");
        }
    }

    static void TestSubscribeUpdates(SubscriptionService subscription)
    {
        for (var i = 0; i < 200; i++)
        {
            subscription.InjectTelemetry(
                "monitor/ui-test/telemetry",
                $$"""
                {
                  "deviceId": "UI-TEST",
                  "timestamp": "2026-08-20T08:00:00Z",
                  "quality": "Good",
                  "tags": { "车速": {{i}}.5, "门幅": 1000.0 }
                }
                """);
        }

        var device = subscription.Devices.Values.FirstOrDefault(entry => entry.DeviceId == "UI-TEST")
            ?? throw new InvalidOperationException("未解析到设备");
        if (!device.Tags.TryGetValue("车速", out var speed) || speed is not double)
        {
            throw new InvalidOperationException("断言失败");
        }
    }

    static void TestMultiDeviceSubscribe(SubscriptionService subscription)
    {
        subscription.InjectTelemetry(
            "monitor/line-a/telemetry",
            """
            {
              "deviceId": "LINE-A",
              "timestamp": "2026-08-20T08:00:00Z",
              "quality": "Good",
              "tags": { "车速": 10.0 }
            }
            """);
        subscription.InjectTelemetry(
            "monitor/line-b/telemetry",
            """
            {
              "deviceId": "LINE-B",
              "timestamp": "2026-08-20T08:00:00Z",
              "quality": "Good",
              "tags": { "车速": 20.0 }
            }
            """);

        var devices = subscription.Devices.Values
            .Where(entry => entry.DeviceId is "LINE-A" or "LINE-B")
            .ToList();
        if (devices.Count != 2)
        {
            throw new InvalidOperationException($"应同时跟踪 2 台设备，实际 {devices.Count}");
        }
    }

    static void TestDeviceCacheLimit(SubscriptionService subscription)
    {
        for (var i = 0; i < 80; i++)
        {
            subscription.InjectTelemetry(
                $"monitor/cache-{i}/telemetry",
                $$"""
                {
                  "deviceId": "DEV-{{i}}",
                  "timestamp": "2026-08-20T08:00:00Z",
                  "quality": "Good",
                  "tags": { "车速": {{i}} }
                }
                """);
        }

        if (subscription.Devices.Count > 64)
        {
            throw new InvalidOperationException($"设备缓存超限：{subscription.Devices.Count}");
        }
    }
}
