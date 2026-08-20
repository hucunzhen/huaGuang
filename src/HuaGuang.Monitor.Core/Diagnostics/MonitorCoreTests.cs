using System.Diagnostics;
using System.Text.Json;
using HuaGuang.Monitor.Models;
using HuaGuang.Monitor.Protocols;
using HuaGuang.Monitor.Services;

namespace HuaGuang.Monitor.Diagnostics;

public static class MonitorCoreTests
{
    public static IReadOnlyList<DiagnosticResult> RunAll() =>
    [
        Run("MQTT 主题匹配 +", TestTopicPlusMatch),
        Run("MQTT 主题匹配 #", TestTopicHashMatch),
        Run("订阅主题去重", TestSubscribeTopicNormalize),
        Run("点位显示顺序", TestTagDisplayOrder),
        Run("信捷地址解析 D6000", TestXinjeAddress),
        Run("Float32 字节序", TestRegisterConverter),
        Run("数值显示精度", TestValueFormatting),
        Run("产线点位数量", TestLineCatalog),
        Run("设置读写", TestSettingsRoundTrip),
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

    static void TestTopicPlusMatch()
    {
        AssertTrue(MqttTopicMatcher.IsMatch("monitor/line-a/telemetry", "monitor/+/telemetry"));
        AssertFalse(MqttTopicMatcher.IsMatch("monitor/line-a/status", "monitor/+/telemetry"));
    }

    static void TestTopicHashMatch()
    {
        AssertTrue(MqttTopicMatcher.IsMatch("monitor/line-a/telemetry/extra", "monitor/#"));
        AssertFalse(MqttTopicMatcher.IsMatch("other/line-a/telemetry", "monitor/#"));
    }

    static void TestSubscribeTopicNormalize()
    {
        var topics = SubscribeTopicHelper.NormalizeTopics([" monitor/a ", "MONITOR/A", ""]);
        AssertTrue(topics.Count == 1);
        AssertTrue(topics[0] == "monitor/a");
    }

    static void TestTagDisplayOrder()
    {
        var catalog = new List<PlcTag>
        {
            new() { Name = "车速", Enabled = true },
            new() { Name = "热溶胶盘温度（热熔胶机1）", Enabled = true },
            new() { Name = "门幅", Enabled = true }
        };
        var remote = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["门幅"] = 1200,
            ["车速"] = 45.2,
            ["热溶胶盘温度（热熔胶机1）"] = 180.5
        };

        var ordered = TagDisplayOrder.OrderRemoteTags(remote, catalog).Select(entry => entry.Name).ToList();
        AssertTrue(ordered.SequenceEqual(["车速", "热溶胶盘温度（热熔胶机1）", "门幅"]));
    }

    static void TestXinjeAddress()
    {
        AssertTrue(XinjeXd5eMapper.TryResolve("D6000", out var resolved, out _));
        AssertTrue(resolved.Table == ModbusTable.HoldingRegister);
    }

    static void TestRegisterConverter()
    {
        var value = RegisterConverter.ToValue([0x0000, 0x0000], TagDataType.Float32, ByteOrder.CDAB);
        AssertTrue(value is float);
        AssertTrue(Math.Abs(Convert.ToSingle(value)) < 0.001);
    }

    static void TestValueFormatting()
    {
        var tag = new PlcTag { Name = "车速", DataType = TagDataType.Float32, DisplayPrecision = 2 };
        AssertTrue(ValueFormatting.FormatDisplay(tag, 45.234, 1) == "45.23");
    }

    static void TestLineCatalog()
    {
        var settings = new AppSettings();
        LineCatalog.Apply(settings, LineCatalog.Xianhe.Name);
        AssertTrue(settings.Tags.Count >= 15);
        AssertTrue(settings.Tags.Any(tag => tag.Name.Contains("温度", StringComparison.Ordinal)));
    }

    static void TestSettingsRoundTrip()
    {
        var tempPath = Path.Combine(Path.GetTempPath(), $"huaguang-test-{Guid.NewGuid():N}.json");
        try
        {
            var original = new AppSettings();
            LineCatalog.Apply(original, LineCatalog.Xianhe.Name);
            original.DeviceId = "TEST-DEVICE";
            var json = JsonSerializer.Serialize(original);
            File.WriteAllText(tempPath, json);

            var loaded = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(tempPath));
            AssertTrue(loaded is not null);
            AssertTrue(loaded!.DeviceId == "TEST-DEVICE");
            AssertTrue(loaded.Tags.Count == original.Tags.Count);
        }
        finally
        {
            if (File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }
        }
    }

    static void AssertTrue(bool condition)
    {
        if (!condition)
        {
            throw new InvalidOperationException("断言失败");
        }
    }

    static void AssertFalse(bool condition) => AssertTrue(!condition);
}
