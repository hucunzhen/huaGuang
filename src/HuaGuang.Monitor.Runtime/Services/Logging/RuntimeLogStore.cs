using Microsoft.Extensions.Logging;

namespace HuaGuang.Monitor.Services.Logging;

/// <summary>内存运行日志环形缓冲，供诊断页实时查看。</summary>
public sealed class RuntimeLogStore
{
    const int MaxEntries = 2000;

    readonly Lock _gate = new();
    readonly List<RuntimeLogEntry> _entries = [];

    public event EventHandler? EntryAdded;

    public void Add(LogLevel level, string category, string message)
    {
        var entry = new RuntimeLogEntry
        {
            Timestamp = DateTimeOffset.Now,
            Level = level,
            Category = RuntimeLogEntry.ShortenCategory(category),
            Message = message
        };

        lock (_gate)
        {
            _entries.Add(entry);
            if (_entries.Count > MaxEntries)
            {
                _entries.RemoveRange(0, _entries.Count - MaxEntries);
            }
        }

        EntryAdded?.Invoke(this, EventArgs.Empty);
    }

    public IReadOnlyList<RuntimeLogEntry> Snapshot()
    {
        lock (_gate)
        {
            return _entries.ToList();
        }
    }

    public void Clear()
    {
        lock (_gate)
        {
            _entries.Clear();
        }

        EntryAdded?.Invoke(this, EventArgs.Empty);
    }

    public static bool IsAcquisitionOrMqttCategory(string category) =>
        category.Contains("AcquisitionService", StringComparison.Ordinal) ||
        category.Contains("MqttOutboundService", StringComparison.Ordinal) ||
        category.Contains("SubscriptionService", StringComparison.Ordinal) ||
        category.Contains("ModbusTcpPlcClient", StringComparison.Ordinal) ||
        category.Contains("MqttPublisher", StringComparison.Ordinal) ||
        category.StartsWith("Messaging.", StringComparison.Ordinal);
}
