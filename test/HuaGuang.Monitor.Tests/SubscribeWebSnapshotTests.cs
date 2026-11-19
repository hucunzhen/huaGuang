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

        var snapshot = SubscribeWebSnapshot.Create(settings, true, true, "", [device]);

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
}
