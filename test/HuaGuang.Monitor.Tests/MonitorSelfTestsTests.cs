using HuaGuang.Monitor.Diagnostics;
using HuaGuang.Monitor.Models;
using HuaGuang.Monitor.Services;
using Xunit;

namespace HuaGuang.Monitor.Tests;

public class MonitorCoreTestsTests
{
    [Fact]
    public void CoreTests_AllPass()
    {
        var results = MonitorCoreTests.RunAll();
        Assert.All(results, result => Assert.True(result.Passed, $"{result.Name}: {result.Message}"));
    }

    [Fact]
    public void TagDisplayOrder_MatchesCatalogOrder()
    {
        var catalog = new List<PlcTag>
        {
            new() { Name = "车速", Enabled = true },
            new() { Name = "油温机温度", Enabled = true }
        };
        var remote = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["油温机温度"] = 165,
            ["车速"] = 45
        };

        var ordered = TagDisplayOrder.OrderRemoteTags(remote, catalog).Select(entry => entry.Name).ToList();
        Assert.Equal(["车速", "油温机温度"], ordered);
    }

    [Fact]
    public void MqttTopicMatcher_MatchesSingleLevelWildcard()
    {
        Assert.True(MqttTopicMatcher.IsMatch("monitor/line-a/telemetry", "monitor/+/telemetry"));
    }
}
