using HuaGuang.Monitor.Models;
using HuaGuang.Monitor.Services;
using Xunit;

namespace HuaGuang.Monitor.Tests;

public class SubscribeWebSnapshotTests
{
    [Fact]
    public void Switch_label_keeps_line_color()
    {
        var status = new PlcTag
        {
            Name = "设备状态",
            DisplayCategory = TagDisplayCategory.Switch,
            SwitchStatusLabels = "0=待机;1=运行;2=故障",
            Enabled = true
        };
        var speed = new PlcTag
        {
            Name = "车速",
            Unit = "m/min",
            DataType = TagDataType.Float32,
            DisplayPrecision = 1,
            Enabled = true
        };
        var settings = new AppSettings
        {
            LineName = "订阅大屏",
            OperationMode = AppOperationMode.Subscribe,
            Tags = [status, speed]
        };
        var device = new RemoteDeviceState
        {
            DeviceKey = "device",
            DeviceId = "未登记设备",
            SourceTopic = "monitor/demo",
            Tags = new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["设备状态"] = "运行",
                ["车速"] = 12.34
            }
        };

        var snapshot = SubscribeWebSnapshot.Create(settings, true, true, "", [device], detailDeviceId: device.DeviceId);

        var state = snapshot.Devices[0].Tags[0];
        Assert.Equal("开关状态", state.Group);
        Assert.Equal("运行", state.Value);
        Assert.Equal("运行", state.Indicator);
        Assert.True(state.IsSwitch);
        Assert.Equal("#3DDC97", state.Color);
        Assert.Equal("#143328", state.Background);
        var speedTag = snapshot.Devices[0].Tags[1];
        Assert.Equal("工艺参数", speedTag.Group);
        Assert.Equal("12.3", speedTag.Value);
        Assert.Equal("m/min", speedTag.Unit);
        Assert.Equal("#2EC4B6", speedTag.Color);
        Assert.Equal("#152536", speedTag.Background);
        Assert.False(speedTag.IsSwitch);
    }

    [Fact]
    public void Fleet_summary_fields_include_run_status_and_line()
    {
        var settings = new AppSettings
        {
            LineName = "订阅大屏",
            OperationMode = AppOperationMode.Subscribe,
            Tags =
            [
                new PlcTag
                {
                    Name = "运行状态",
                    DisplayCategory = TagDisplayCategory.Switch,
                    Enabled = true
                }
            ]
        };
        var device = new RemoteDeviceState
        {
            DeviceKey = "t::撒粉复合机",
            DeviceId = "撒粉复合机",
            SourceTopic = LineMqttDefaults.SafenPublishTopic,
            ReceivedAt = DateTimeOffset.Now,
            Tags = new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["运行状态"] = 1
            }
        };

        var summary = SubscribeWebSnapshot.Create(settings, true, true, "", [device]);
        var card = summary.Devices[0];
        Assert.Equal("撒粉复合机", card.DeviceId);
        Assert.Equal("撒粉复合机", card.LineName);
        Assert.Equal("开", card.RunStatus);
        Assert.Equal("#3DDC97", card.RunStatusColor);
        Assert.Equal(1, card.TagCount);
        Assert.Single(card.Tags);
        Assert.True(card.Tags[0].IsSwitch);
        Assert.Equal("运行状态", card.Tags[0].Name);

        var detail = SubscribeWebSnapshot.Create(settings, true, true, "", [device], detailDeviceId: "撒粉复合机");
        Assert.Equal(["撒粉复合机"], detail.DeviceIds);
        Assert.Single(detail.Devices);
        Assert.Single(detail.Devices[0].Tags);
        Assert.Equal("运行状态", detail.Devices[0].Tags[0].Name);
    }

    [Fact]
    public void Fleet_devices_ordered_flame_compound_then_hongbei_dingxing_qimao()
    {
        var settings = new AppSettings
        {
            LineName = "订阅大屏",
            OperationMode = AppOperationMode.Subscribe,
            Tags = []
        };
        RemoteDeviceState Device(string id, string topic) => new()
        {
            DeviceKey = "t::" + id,
            DeviceId = id,
            SourceTopic = topic,
            ReceivedAt = DateTimeOffset.Now,
            Tags = new Dictionary<string, object?>(StringComparer.Ordinal)
        };

        var devices = new[]
        {
            Device(LineCatalog.QimaoZl028aLineName, LineMqttDefaults.QimaoZl028aPublishTopic),
            Device(LineCatalog.Dingxing10LineName, LineMqttDefaults.Dingxing10PublishTopic),
            Device("撒粉复合机", LineMqttDefaults.SafenPublishTopic),
            Device(LineCatalog.HongbeiLineName, LineMqttDefaults.HongbeiPublishTopic),
            Device(LineCatalog.ZhidahyLineName, LineMqttDefaults.ZhidahyPublishTopic),
            Device(LineCatalog.Ruian1LineName, LineMqttDefaults.Ruian1PublishTopic),
            Device("平板复合机", LineMqttDefaults.PingbanPublishTopic),
            Device("未登记机台", "monitor/unknown/telemetry")
        };

        var summary = SubscribeWebSnapshot.Create(settings, true, true, "", devices);
        Assert.Equal(
            [
                LineCatalog.ZhidahyLineName,
                "平板复合机",
                "撒粉复合机",
                "未登记机台",
                LineCatalog.HongbeiLineName,
                LineCatalog.Ruian1LineName,
                LineCatalog.Dingxing10LineName,
                LineCatalog.QimaoZl028aLineName
            ],
            summary.DeviceIds);
        Assert.Equal(summary.DeviceIds, summary.Devices.Select(device => device.DeviceId).ToList());
    }

    [Fact]
    public void Dingxing11_detail_builds_from_seed_catalog()
    {
        var previous = SubscribeLineCatalog.LoadTags;
        try
        {
            var line = LineExcelConfigService.CreateSeedSettings(LineCatalog.Dingxing11LineName);
            SubscribeLineCatalog.LoadTags = name =>
                string.Equals(name, LineCatalog.Dingxing11LineName, StringComparison.Ordinal)
                    ? line.Tags
                    : null;

            var settings = new AppSettings
            {
                LineName = LineCatalog.SubscribeDashboardLineName,
                OperationMode = AppOperationMode.Subscribe,
                Tags = []
            };
            var remote = new Dictionary<string, object?>(StringComparer.Ordinal);
            foreach (var tag in line.Tags.Where(t => t.Enabled))
            {
                if (!MqttFieldMappingCatalog.SharedByTagName.TryGetValue(tag.Name, out var field))
                {
                    field = tag.Name;
                }

                remote[field!] = tag.Name.Contains("温度", StringComparison.Ordinal) ? 180 : 42;
            }

            var device = new RemoteDeviceState
            {
                DeviceKey = "t::" + LineCatalog.Dingxing11LineName,
                DeviceId = LineCatalog.Dingxing11LineName,
                SourceTopic = LineMqttDefaults.Dingxing11PublishTopic,
                ReceivedAt = DateTimeOffset.Now,
                Tags = remote
            };

            var detail = SubscribeWebSnapshot.Create(
                settings, true, true, "", [device], detailDeviceId: LineCatalog.Dingxing11LineName);
            Assert.Single(detail.Devices);
            Assert.True(
                !detail.Devices[0].Status.Contains("解析失败", StringComparison.Ordinal),
                detail.Devices[0].Status);
            Assert.True(detail.Devices[0].Tags.Count >= 10, $"tagCount={detail.Devices[0].Tags.Count}");
        }
        finally
        {
            SubscribeLineCatalog.LoadTags = previous;
        }
    }

    [Fact]
    public void Device_without_设备状态_still_opens_summary_and_detail()
    {
        var settings = new AppSettings
        {
            LineName = "订阅大屏",
            OperationMode = AppOperationMode.Subscribe,
            Tags =
            [
                new PlcTag
                {
                    Name = "车速",
                    Unit = "m/min",
                    DataType = TagDataType.Float32,
                    DisplayPrecision = 1,
                    Enabled = true
                },
                new PlcTag
                {
                    Name = "运行状态",
                    DisplayCategory = TagDisplayCategory.Switch,
                    Enabled = true
                }
            ]
        };
        var device = new RemoteDeviceState
        {
            DeviceKey = "t::无状态机台",
            DeviceId = "无状态机台",
            SourceTopic = "monitor/demo/telemetry",
            ReceivedAt = DateTimeOffset.Now,
            Tags = new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["车速"] = 8.5,
                ["运行状态"] = 1
                // 故意没有「设备状态」
            }
        };

        var summary = SubscribeWebSnapshot.Create(settings, true, true, "", [device]);
        Assert.Single(summary.Devices);
        Assert.Equal("无状态机台", summary.Devices[0].DeviceId);
        Assert.Equal("开", summary.Devices[0].RunStatus);
        Assert.Contains(summary.Devices[0].Tags, tag => tag.Name == "运行状态");
        Assert.DoesNotContain(summary.Devices[0].Tags, tag => tag.Name == "设备状态");

        var detail = SubscribeWebSnapshot.Create(settings, true, true, "", [device], detailDeviceId: "无状态机台");
        Assert.Single(detail.Devices);
        Assert.Equal("无状态机台", detail.Devices[0].DeviceId);
        Assert.False(string.IsNullOrWhiteSpace(detail.Devices[0].Status));
        Assert.Contains(detail.Devices[0].Tags, tag => tag.Name == "车速" && tag.Value == "8.5");
        Assert.DoesNotContain(detail.Devices[0].Tags, tag => tag.Name == "设备状态");
    }
}
