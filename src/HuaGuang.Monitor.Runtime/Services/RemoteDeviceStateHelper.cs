using HuaGuang.Monitor.Ipc;
using HuaGuang.Monitor.Models;

namespace HuaGuang.Monitor.Services;

static class RemoteDeviceStateHelper
{
    /// <summary>
    /// 点位内容和质量是否相同。忽略 ReceivedAt/Timestamp：
    /// 每次遥测都会刷新时间，不能据此触发整页刷新。
    /// </summary>
    public static bool HasSameContent(RemoteDeviceState existing, RemoteDeviceStateDto dto) =>
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

    /// <summary>仅同步到达时间，不改动点位字典。</summary>
    public static void ApplyArrivalOnly(RemoteDeviceState target, RemoteDeviceStateDto dto)
    {
        target.ReceivedAt = dto.ReceivedAt;
        target.Timestamp = dto.Timestamp;
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

    /// <summary>给网页/IPC 用的只读快照，避免与遥测写线程争用同一 Dictionary。</summary>
    public static RemoteDeviceState Clone(RemoteDeviceState source) => new()
    {
        DeviceKey = source.DeviceKey,
        DeviceId = source.DeviceId,
        SourceTopic = source.SourceTopic,
        Timestamp = source.Timestamp,
        Quality = source.Quality,
        PlcHost = source.PlcHost,
        Simulator = source.Simulator,
        ReceivedAt = source.ReceivedAt,
        TagCountHint = source.Tags.Count,
        Tags = CopyTags(source.Tags)
    };

    /// <summary>机台汇总：只带运行状态相关键，避免每次网页轮询拷贝全部点位。</summary>
    public static RemoteDeviceState CloneForFleetCard(RemoteDeviceState source)
    {
        var tags = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var pair in source.Tags)
        {
            if (!IsRunStatusKey(pair.Key))
            {
                continue;
            }

            tags[pair.Key] = JsonValueNormalizer.Normalize(pair.Value);
        }

        return new RemoteDeviceState
        {
            DeviceKey = source.DeviceKey,
            DeviceId = source.DeviceId,
            SourceTopic = source.SourceTopic,
            Timestamp = source.Timestamp,
            Quality = source.Quality,
            PlcHost = source.PlcHost,
            Simulator = source.Simulator,
            ReceivedAt = source.ReceivedAt,
            TagCountHint = source.Tags.Count,
            Tags = tags
        };
    }

    static bool IsRunStatusKey(string key) =>
        key.Equals(RunStatusFormatting.TagName, StringComparison.OrdinalIgnoreCase) ||
        key.Equals("run_status", StringComparison.OrdinalIgnoreCase);

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

            if (!JsonValueNormalizer.ValuesEqual(pair.Value, other))
            {
                return false;
            }
        }

        return true;
    }
}
