using HuaGuang.Monitor.Models;

namespace HuaGuang.Monitor.Services;

public sealed class HistorySampleSummary
{
    public long Id { get; init; }
    public DateTimeOffset RecordedAt { get; init; }
    public string DeviceId { get; init; } = string.Empty;
    public string OperationModeLabel { get; init; } = string.Empty;
    public string Quality { get; init; } = "—";
    public int TagCount { get; init; }
    public string? SourceTopic { get; init; }

    public string RecordedAtText => RecordedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss");

    public string Subtitle =>
        string.IsNullOrWhiteSpace(SourceTopic)
            ? $"{OperationModeLabel} · {TagCount} 个点位 · {Quality}"
            : $"{OperationModeLabel} · {SourceTopic} · {TagCount} 个点位";
}

public sealed class HistoryTagValueRow
{
    public string TagName { get; init; } = string.Empty;
    public string? Unit { get; init; }
    public string DisplayValue { get; init; } = "—";
    public string Quality { get; init; } = "Good";
}

public sealed class HistorySampleDetail
{
    public long Id { get; init; }
    public DateTimeOffset RecordedAt { get; init; }
    public DateTimeOffset? SourceTimestamp { get; init; }
    public string DeviceId { get; init; } = string.Empty;
    public string OperationModeLabel { get; init; } = string.Empty;
    public string Quality { get; init; } = "—";
    public string? SourceTopic { get; init; }
    public string? PayloadJson { get; init; }
    public IReadOnlyList<HistoryTagValueRow> Tags { get; init; } = [];
}

public sealed class HistorySampleWriteRequest
{
    public DateTimeOffset RecordedAt { get; init; } = DateTimeOffset.Now;
    public DateTimeOffset? SourceTimestamp { get; init; }
    public required string DeviceId { get; init; }
    public string? SourceTopic { get; init; }
    public AppOperationMode OperationMode { get; init; }
    public string? Quality { get; init; }
    public string? PlcHost { get; init; }
    public bool? Simulator { get; init; }
    public string? PayloadJson { get; init; }
    public required IReadOnlyList<TagSnapshot> Tags { get; init; }
}

public sealed class HistoryQuery
{
    public DateTimeOffset From { get; init; }
    public DateTimeOffset To { get; init; }
    public string? DeviceId { get; init; }
    public int Limit { get; init; } = 200;
}
