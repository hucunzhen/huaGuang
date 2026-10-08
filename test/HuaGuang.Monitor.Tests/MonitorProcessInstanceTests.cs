using HuaGuang.Monitor.Models;
using HuaGuang.Monitor.Services;
using Xunit;

namespace HuaGuang.Monitor.Tests;

public sealed class MonitorProcessInstanceTests
{
    [Fact]
    public void Parse_instance_from_equals_argument()
    {
        Assert.Equal("acq-xianhe", MonitorProcessInstance.ParseId(["--instance=acq-xianhe"]));
        Assert.Equal("sub-hall", MonitorProcessInstance.ParseId(["--instance", "sub-hall"]));
        Assert.Null(MonitorProcessInstance.ParseId([]));
    }

    [Fact]
    public void Isolated_data_dir_is_under_instances()
    {
        var shared = Path.Combine(Path.GetTempPath(), "monitor-shared");
        Assert.Equal(
            Path.Combine(shared, "instances", "sub-hall"),
            MonitorProcessInstance.ResolveUserDataDirectory(shared, "sub-hall"));
        Assert.Equal(shared, MonitorProcessInstance.ResolveUserDataDirectory(shared, null));
    }

    [Fact]
    public void Rejects_invalid_instance_id()
    {
        Assert.False(MonitorProcessInstance.TrySanitize("../x", out _, out _));
        Assert.False(MonitorProcessInstance.TrySanitize("", out _, out _));
        Assert.Throws<InvalidOperationException>(() =>
            MonitorProcessInstance.ParseId(["--instance", "bad id"]));
    }

    [Fact]
    public void Parses_line_and_mode_aliases()
    {
        Assert.Equal("先河热熔胶复合机", MonitorProcessInstance.ParseLine(["--line", "xianhe"]));
        Assert.Equal("C型火焰复合机", MonitorProcessInstance.ParseLine(["--line=C型火焰"]));
        Assert.Equal("S7测试产线", MonitorProcessInstance.ParseLine(["--line", "s7"]));
        Assert.Equal(AppOperationMode.Acquisition, MonitorProcessInstance.ParseMode(["--mode", "采集"]));
        Assert.Equal(AppOperationMode.Subscribe, MonitorProcessInstance.ParseMode(["--mode=subscribe"]));
        Assert.Throws<InvalidOperationException>(() => MonitorProcessInstance.ParseLine(["--line", "unknown"]));
        Assert.Throws<InvalidOperationException>(() => MonitorProcessInstance.ParseMode(["--mode", "other"]));
    }

    [Fact]
    public void Isolated_service_and_ipc_names_are_unique()
    {
        MonitorProcessInstance.ResetForTests("acq-xianhe");
        try
        {
            Assert.Equal("HuaGuangMonitor-acq-xianhe", MonitorProcessInstance.WindowsServiceName);
            Assert.Equal("HuaGuangMonitor-acq-xianhe", MonitorProcessInstance.WindowsServiceNameFor("acq-xianhe"));
            Assert.Equal("HuaGuang.Monitor.Runtime.v1.acq-xianhe", MonitorProcessInstance.IpcPipeNameFor("acq-xianhe"));
            Assert.Equal(MonitorProcessInstance.IpcTcpPort, MonitorProcessInstance.IpcTcpPortFor("acq-xianhe"));
            Assert.NotEqual(18788, MonitorProcessInstance.IpcTcpPort);
            Assert.Contains("--instance acq-xianhe", MonitorProcessInstance.FormatHostArguments(), StringComparison.Ordinal);
            Assert.Contains("--mode acquisition", MonitorProcessInstance.FormatHostArguments(), StringComparison.Ordinal);
        }
        finally
        {
            MonitorProcessInstance.ResetForTests(null);
        }
    }

    [Fact]
    public void Subscribe_instance_host_args_use_subscribe_mode()
    {
        MonitorProcessInstance.ResetForTests("sub");
        try
        {
            var args = MonitorProcessInstance.FormatHostArguments(AppOperationMode.Subscribe, "先河热熔胶复合机");
            Assert.Contains("--instance sub", args, StringComparison.Ordinal);
            Assert.Contains("--mode subscribe", args, StringComparison.Ordinal);
            Assert.DoesNotContain("--mode acquisition", args, StringComparison.Ordinal);
        }
        finally
        {
            MonitorProcessInstance.ResetForTests(null);
        }
    }

    [Fact]
    public void Isolated_host_args_use_chosen_line_not_catalog_default()
    {
        MonitorProcessInstance.ResetForTests("acq-huadi");
        try
        {
            var args = MonitorProcessInstance.FormatHostArguments(
                AppOperationMode.Acquisition,
                "华迪热熔胶复合机");
            Assert.Contains("--line huadi", args, StringComparison.Ordinal);
            Assert.DoesNotContain("--line xianhe", args, StringComparison.Ordinal);
        }
        finally
        {
            MonitorProcessInstance.ResetForTests(null);
        }
    }

    [Fact]
    public void FormatHostArgumentsFor_does_not_need_process_identity()
    {
        var args = MonitorProcessInstance.FormatHostArgumentsFor(
            "acq-huadi",
            AppOperationMode.Acquisition,
            "华迪热熔胶复合机");
        Assert.Contains("--instance acq-huadi", args, StringComparison.Ordinal);
        Assert.Contains("--line huadi", args, StringComparison.Ordinal);
        Assert.Contains("--mode acquisition", args, StringComparison.Ordinal);
    }

    [Fact]
    public void Discovers_pingban_acquisition_host_from_line_settings()
    {
        var settings = new AppSettings
        {
            LineName = "平板复合机",
            OperationMode = AppOperationMode.Acquisition,
            StartWithWindows = true,
            AutoStartAcquisition = true
        };
        Assert.True(InstanceHostProfileStore.TryCreateDiscoveredProfile(settings, out var profile));
        Assert.Equal("acq-pingban", profile.InstanceId);
        Assert.Equal("平板复合机", profile.LineName);
        Assert.Equal(nameof(AppOperationMode.Acquisition), profile.OperationMode);
        Assert.True(profile.AutoStart);
    }

    [Fact]
    public void Discovers_subscribe_dashboard_host()
    {
        var settings = new AppSettings
        {
            LineName = "平板复合机",
            OperationMode = AppOperationMode.Subscribe,
            StartWithWindows = true,
            AutoStartAcquisition = true
        };
        Assert.True(InstanceHostProfileStore.TryCreateDiscoveredProfile(settings, out var profile));
        Assert.Equal("sub", profile.InstanceId);
        Assert.Equal(nameof(AppOperationMode.Subscribe), profile.OperationMode);
    }

    [Fact]
    public void Does_not_discover_when_boot_flags_off()
    {
        var settings = new AppSettings
        {
            LineName = "平板复合机",
            StartWithWindows = false,
            AutoStartAcquisition = true
        };
        Assert.False(InstanceHostProfileStore.TryCreateDiscoveredProfile(settings, out _));
    }
}
