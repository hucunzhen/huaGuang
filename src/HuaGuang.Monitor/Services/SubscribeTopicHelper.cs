namespace HuaGuang.Monitor.Services;

using HuaGuang.Monitor.Models;

public static class MqttTopicMatcher
{
    public static bool IsMatch(string topic, string filter)
    {
        if (string.IsNullOrWhiteSpace(topic) || string.IsNullOrWhiteSpace(filter))
        {
            return false;
        }

        if (string.Equals(topic, filter, StringComparison.Ordinal))
        {
            return true;
        }

        var topicParts = topic.Split('/', StringSplitOptions.RemoveEmptyEntries);
        var filterParts = filter.Split('/', StringSplitOptions.RemoveEmptyEntries);

        for (var i = 0; i < filterParts.Length; i++)
        {
            if (filterParts[i] == "#")
            {
                return true;
            }

            if (i >= topicParts.Length)
            {
                return false;
            }

            if (filterParts[i] != "+" && !string.Equals(filterParts[i], topicParts[i], StringComparison.Ordinal))
            {
                return false;
            }
        }

        return filterParts.Length == topicParts.Length;
    }
}

public static class SubscribeTopicHelper
{
    public const string AllTopicsLabel = "全部";

    public static IReadOnlyList<string> NormalizeTopics(IEnumerable<string>? topics)
    {
        if (topics is null)
        {
            return ["monitor/+/telemetry"];
        }

        var normalized = topics
            .Where(topic => !string.IsNullOrWhiteSpace(topic))
            .Select(topic => topic.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        return normalized.Count == 0 ? ["monitor/+/telemetry"] : normalized;
    }

    public static void Migrate(AppSettings settings)
    {
        if (settings.SubscribeTopics.Count == 0 && !string.IsNullOrWhiteSpace(settings.SubscribeTopic))
        {
            settings.SubscribeTopics.Add(settings.SubscribeTopic.Trim());
        }

        settings.SubscribeTopics = NormalizeTopics(settings.SubscribeTopics).ToList();
    }
}
