using HuaGuang.Monitor.Ipc;
using HuaGuang.Monitor.Models;

namespace HuaGuang.Monitor.Services;

static class RemoteDeviceStateHelper
{
    public static bool HasSameContent(RemoteDeviceState existing, RemoteDeviceStateDto dto) =>
        existing.ReceivedAt == dto.ReceivedAt &&
        existing.Timestamp == dto.Timestamp &&
        existing.Quality == dto.Quality &&
        existing.PlcHost == dto.PlcHost &&
        existing.Simulator == dto.Simulator &&
        TagsEqual(existing.Tags, dto.Tags);

    public static void ApplyDto(RemoteDeviceState target, RemoteDeviceStateDto dto)
    {
        target.Timestamp = dto.Timestamp;
        target.Quality = dto.Quality;
        target.PlcHost = dto.PlcHost;
        target.Simulator = dto.Simulator;
        target.ReceivedAt = dto.ReceivedAt;
        ApplyTags(target.Tags, dto.Tags);
    }

    public static RemoteDeviceState FromDto(RemoteDeviceStateDto dto) => new()
    {
        DeviceKey = dto.DeviceKey,
        DeviceId = dto.DeviceId,
        SourceTopic = dto.SourceTopic,
        Timestamp = dto.Timestamp,
        Quality = dto.Quality,
        PlcHost = dto.PlcHost,
        Simulator = dto.Simulator,
        ReceivedAt = dto.ReceivedAt,
        Tags = CopyTags(dto.Tags)
    };

    static void ApplyTags(Dictionary<string, object?> target, IReadOnlyDictionary<string, object?> source)
    {
        target.Clear();
        foreach (var pair in source)
        {
            target[pair.Key] = JsonValueNormalizer.Normalize(pair.Value);
        }
    }

    static Dictionary<string, object?> CopyTags(IReadOnlyDictionary<string, object?> source)
    {
        var tags = new Dictionary<string, object?>(source.Count, StringComparer.Ordinal);
        foreach (var pair in source)
        {
            tags[pair.Key] = JsonValueNormalizer.Normalize(pair.Value);
        }

        return tags;
    }

    static bool TagsEqual(
        IReadOnlyDictionary<string, object?> left,
        IReadOnlyDictionary<string, object?> right)
    {
        if (left.Count != right.Count)
        {
            return false;
        }

        foreach (var pair in left)
        {
            if (!right.TryGetValue(pair.Key, out var other))
            {
                return false;
            }

            if (!ValuesEqual(pair.Value, other))
            {
                return false;
            }
        }

        return true;
    }

    static bool ValuesEqual(object? left, object? right)
    {
        left = JsonValueNormalizer.Normalize(left);
        right = JsonValueNormalizer.Normalize(right);
        if (left is null || right is null)
        {
            return left is null && right is null;
        }

        if (left.Equals(right))
        {
            return true;
        }

        return left is IFormattable && right is IFormattable &&
               left.ToString() == right.ToString();
    }
}
