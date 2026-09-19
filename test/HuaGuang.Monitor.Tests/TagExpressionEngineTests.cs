using HuaGuang.Monitor.Models;
using HuaGuang.Monitor.Services;
using Xunit;

namespace HuaGuang.Monitor.Tests;

public sealed class TagExpressionEngineTests
{
    [Fact]
    public void Expression_evaluates_arithmetic_and_references()
    {
        var values = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["A"] = 10d,
            ["B"] = 4d,
            ["运行"] = true
        };

        Assert.True(TagExpressionEngine.TryEvaluate("[A]+[B]*2", values, out var result, out var err1), err1);
        Assert.Equal(18, result, 3);

        Assert.True(TagExpressionEngine.TryEvaluate("([A]-[B])/[A]*100", values, out result, out var percentError), percentError);
        Assert.Equal(60, result, 3);
    }

    [Fact]
    public void Computed_plan_orders_dependencies()
    {
        var tags = new List<PlcTag>
        {
            new() { Name = "A", Source = TagSource.Plc },
            new() { Name = "B", Source = TagSource.Computed, Expression = "[A]+1" },
            new() { Name = "C", Source = TagSource.Computed, Expression = "[B]*2" }
        };
        Assert.True(TagComputedCatalog.TryBuildPlan(tags, out var plan, out var error), error);
        Assert.Equal(["B", "C"], plan.EvaluationOrder.Select(t => t.Name));
    }
}
