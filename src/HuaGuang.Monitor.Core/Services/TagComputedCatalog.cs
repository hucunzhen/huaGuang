using HuaGuang.Monitor.Models;

namespace HuaGuang.Monitor.Services;

public static class TagComputedCatalog
{
    public static bool TryBuildPlan(IReadOnlyList<PlcTag> tags, out TagComputedPlan plan, out string error)
    {
        plan = TagComputedPlan.Empty;
        error = string.Empty;

        var byName = new Dictionary<string, PlcTag>(StringComparer.Ordinal);
        foreach (var tag in tags)
        {
            if (string.IsNullOrWhiteSpace(tag.Name))
            {
                continue;
            }

            if (byName.ContainsKey(tag.Name))
            {
                error = $"点位名称重复：「{tag.Name}」。";
                return false;
            }

            byName[tag.Name] = tag;
        }

        var computed = tags.Where(t => t.IsComputed).ToList();
        if (computed.Count == 0)
        {
            plan = new TagComputedPlan([], new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal));
            return true;
        }

        var dependencies = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);
        foreach (var tag in computed)
        {
            if (string.IsNullOrWhiteSpace(tag.Expression))
            {
                error = $"计算点位「{tag.Name}」缺少表达式。";
                return false;
            }

            var refs = TagExpressionEngine.ExtractReferences(tag.Expression);
            foreach (var reference in refs)
            {
                if (!byName.ContainsKey(reference))
                {
                    error = $"计算点位「{tag.Name}」引用了不存在的点位「{reference}」。";
                    return false;
                }

                if (string.Equals(reference, tag.Name, StringComparison.Ordinal))
                {
                    error = $"计算点位「{tag.Name}」不能引用自身。";
                    return false;
                }
            }

            dependencies[tag.Name] = refs;
        }

        if (!TrySortComputed(computed, dependencies, out var order, out error))
        {
            return false;
        }

        plan = new TagComputedPlan(order, dependencies);
        return true;
    }

    static bool TrySortComputed(
        IReadOnlyList<PlcTag> computed,
        IReadOnlyDictionary<string, IReadOnlyList<string>> dependencies,
        out IReadOnlyList<PlcTag> order,
        out string error)
    {
        error = string.Empty;
        var computedNames = new HashSet<string>(computed.Select(t => t.Name), StringComparer.Ordinal);
        var inDegree = computed.ToDictionary(t => t.Name, _ => 0, StringComparer.Ordinal);
        var edges = computed.ToDictionary(t => t.Name, _ => new List<string>(), StringComparer.Ordinal);

        foreach (var tag in computed)
        {
            foreach (var reference in dependencies[tag.Name])
            {
                if (!computedNames.Contains(reference))
                {
                    continue;
                }

                edges[reference].Add(tag.Name);
                inDegree[tag.Name]++;
            }
        }

        var queue = new Queue<string>(inDegree.Where(pair => pair.Value == 0).Select(pair => pair.Key));
        var sortedNames = new List<string>();
        while (queue.Count > 0)
        {
            var name = queue.Dequeue();
            sortedNames.Add(name);
            foreach (var next in edges[name])
            {
                inDegree[next]--;
                if (inDegree[next] == 0)
                {
                    queue.Enqueue(next);
                }
            }
        }

        if (sortedNames.Count != computed.Count)
        {
            error = "计算点位存在循环引用，请检查表达式中的 [名称] 依赖。";
            order = [];
            return false;
        }

        var byName = computed.ToDictionary(t => t.Name, StringComparer.Ordinal);
        order = sortedNames.Select(name => byName[name]).ToList();
        return true;
    }
}

public sealed class TagComputedPlan
{
    public static TagComputedPlan Empty { get; } = new([], new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal));

    public TagComputedPlan(
        IReadOnlyList<PlcTag> evaluationOrder,
        IReadOnlyDictionary<string, IReadOnlyList<string>> dependencies)
    {
        EvaluationOrder = evaluationOrder;
        Dependencies = dependencies;
    }

    public IReadOnlyList<PlcTag> EvaluationOrder { get; }
    public IReadOnlyDictionary<string, IReadOnlyList<string>> Dependencies { get; }
}
