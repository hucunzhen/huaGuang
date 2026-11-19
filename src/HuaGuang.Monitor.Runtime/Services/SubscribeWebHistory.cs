using System.Globalization;
using HuaGuang.Monitor.Models;

namespace HuaGuang.Monitor.Services;

public sealed class SubscribeWebHistoryFilter
{
    public DateTimeOffset From { get; init; }
    public DateTimeOffset To { get; init; }
    public string? DeviceId { get; init; }
    public string? Tag { get; init; }
    public int Page { get; init; } = 1;
}

public sealed class SubscribeWebHistoryPage
{
    public int Total { get; init; }
    public int Page { get; init; }
    public int PageSize { get; init; }
    public DateTimeOffset From { get; init; }
    public DateTimeOffset To { get; init; }
    public string Device { get; init; } = string.Empty;
    public string Tag { get; init; } = string.Empty;
    public bool Recording { get; init; }
    public int RetentionDays { get; init; }
    public bool ColumnsTruncated { get; init; }
    public List<string> Devices { get; init; } = [];
    public List<string> Columns { get; init; } = [];
    public List<SubscribeWebHistoryRow> Rows { get; init; } = [];
}

public sealed class SubscribeWebHistoryRow
{
    public string Time { get; init; } = string.Empty;
    public string Device { get; init; } = string.Empty;
    public List<string> Values { get; init; } = [];
}

public sealed class SubscribeWebExportFile : IDisposable
{
    public required string Path { get; init; }
    public required string DownloadName { get; init; }
    public int RowCount { get; init; }

    public void Dispose()
    {
        try
        {
            if (File.Exists(Path))
            {
                File.Delete(Path);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}

/// <summary>订阅网页的历史查询和导出。只读，复用历史库和 Excel 写入。</summary>
public static class SubscribeWebHistory
{
    public const int MaxRangeDays = 31;
    public const int MaxExportRows = 20_000;
    public const int PageSize = HistoryTableFormatting.PageSize;

    public static bool TryParse(Uri? url, DateTimeOffset now, out SubscribeWebHistoryFilter filter, out string error)
    {
        filter = new SubscribeWebHistoryFilter { From = now.AddHours(-24), To = now };
        error = string.Empty;
        var query = ParseQuery(url);
        query.TryGetValue("from", out var fromText);
        query.TryGetValue("to", out var toText);
        var hasFrom = !string.IsNullOrWhiteSpace(fromText);
        var hasTo = !string.IsNullOrWhiteSpace(toText);
        var from = now.AddHours(-24);
        var to = now;
        if (hasFrom && !TryParseLocal(fromText!, out from))
        {
            error = "开始时间无效。";
            return false;
        }

        if (hasTo && !TryParseLocal(toText!, out to))
        {
            error = "结束时间无效。";
            return false;
        }

        if (hasFrom && !hasTo)
        {
            to = now;
        }
        else if (!hasFrom && hasTo)
        {
            from = to.AddHours(-24);
        }

        if (from > to)
        {
            error = "开始时间不能晚于结束时间。";
            return false;
        }

        if (to - from > TimeSpan.FromDays(MaxRangeDays))
        {
            error = $"单次最多查询 {MaxRangeDays} 天。";
            return false;
        }

        var page = 1;
        if (query.TryGetValue("page", out var pageText) && !string.IsNullOrWhiteSpace(pageText))
        {
            if (!int.TryParse(pageText, NumberStyles.None, CultureInfo.InvariantCulture, out page) || page < 1)
            {
                error = "页码无效。";
                return false;
            }
        }

        query.TryGetValue("tag", out var tag);
        tag = tag?.Trim();
        if (tag is { Length: > 80 })
        {
            error = "点位筛选过长。";
            return false;
        }

        filter = new SubscribeWebHistoryFilter
        {
            From = from,
            To = to,
            DeviceId = NormalizeDevice(query.TryGetValue("device", out var device) ? device : null),
            Tag = string.IsNullOrWhiteSpace(tag) ? null : tag,
            Page = page
        };
        return true;
    }

    public static async Task<SubscribeWebHistoryPage> QueryAsync(
        HistoryStore store,
        AppSettings settings,
        SubscribeWebHistoryFilter filter)
    {
        var countQuery = ToQuery(filter, limit: 1, offset: 0);
        var total = await store.CountMatchingAsync(countQuery).ConfigureAwait(false);
        var devices = await store.GetDeviceIdsAsync().ConfigureAwait(false);
        var pageCount = Math.Max(1, (int)Math.Ceiling(total / (double)PageSize));
        var page = Math.Clamp(filter.Page, 1, pageCount);
        var (columns, truncated) = await BuildColumnsAsync(store, settings, filter).ConfigureAwait(false);
        var rows = new List<SubscribeWebHistoryRow>();
        if (total > 0)
        {
            var table = await store.QueryTableAsync(
                ToQuery(filter, PageSize, (page - 1) * PageSize),
                settings.TemperaturePrecision,
                preferredTagOrder: null,
                fixedColumns: columns.Count > 0 ? columns : null,
                tagUnitHints: Units(settings),
                catalogTags: Catalog(settings, filter.DeviceId),
                mqttProfile: settings.MqttPayload).ConfigureAwait(false);
            var useColumns = columns.Count > 0 || string.IsNullOrWhiteSpace(filter.Tag);
            if (useColumns && columns.Count == 0)
            {
                columns = table.Columns.ToList();
            }

            foreach (var row in table.Rows)
            {
                var values = useColumns
                    ? row.TagCells.Select(cell => string.IsNullOrWhiteSpace(cell.Text) ? "—" : cell.Text).ToList()
                    : [];
                rows.Add(new SubscribeWebHistoryRow
                {
                    Time = row.RecordedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss"),
                    Device = row.DeviceId,
                    Values = values
                });
            }
        }

        return new SubscribeWebHistoryPage
        {
            Total = total,
            Page = page,
            PageSize = PageSize,
            From = filter.From,
            To = filter.To,
            Device = filter.DeviceId ?? string.Empty,
            Tag = filter.Tag ?? string.Empty,
            Recording = settings.EnableHistoryRecording,
            RetentionDays = settings.HistoryRetentionDays,
            ColumnsTruncated = truncated,
            Devices = devices.ToList(),
            Columns = columns.Select(column => column.HeaderText).ToList(),
            Rows = rows
        };
    }

    public static async Task<SubscribeWebExportFile> ExportAsync(
        HistoryStore store,
        AppSettings settings,
        SubscribeWebHistoryFilter filter,
        CancellationToken cancellationToken)
    {
        var total = await store.CountMatchingAsync(ToQuery(filter, limit: 1, offset: 0)).ConfigureAwait(false);
        var (columns, _) = total == 0
            ? ([], false)
            : await BuildColumnsAsync(store, settings, filter).ConfigureAwait(false);
        var refusal = ExportRefusal(filter, total, columns.Count);
        if (refusal is not null)
        {
            throw new InvalidOperationException(refusal);
        }

        var path = Path.Combine(Path.GetTempPath(), $"huaguang-web-{Guid.NewGuid():N}.xlsx");
        var downloadName = BuildDownloadName(filter.DeviceId);
        HistoryExcelWriter? writer = null;
        try
        {
            var offset = 0;
            IReadOnlyList<HistoryTableColumn> fixedColumns = columns;
            while (offset < total)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var table = await store.QueryTableAsync(
                    ToQuery(filter, HistoryExcelWriter.ExportPageSize, offset),
                    settings.TemperaturePrecision,
                    preferredTagOrder: null,
                    fixedColumns: fixedColumns.Count > 0 ? fixedColumns : null,
                    tagUnitHints: Units(settings),
                    catalogTags: Catalog(settings, filter.DeviceId),
                    mqttProfile: settings.MqttPayload).ConfigureAwait(false);
                if (table.Rows.Count == 0)
                {
                    break;
                }

                if (writer is null)
                {
                    if (fixedColumns.Count == 0)
                    {
                        fixedColumns = table.Columns.ToList();
                    }

                    writer = new HistoryExcelWriter(fixedColumns);
                }

                writer.AddRows(table.Rows);
                offset += table.Rows.Count;
            }

            writer ??= new HistoryExcelWriter(fixedColumns);
            writer.Save(path);
        }
        catch
        {
            try
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch (IOException)
            {
            }

            throw;
        }
        finally
        {
            writer?.Dispose();
        }

        return new SubscribeWebExportFile
        {
            Path = path,
            DownloadName = downloadName,
            RowCount = total
        };
    }

    internal static string? ExportRefusal(SubscribeWebHistoryFilter filter, int count, int columnCount)
    {
        if (count == 0)
        {
            return "当前筛选条件下没有可导出的记录。";
        }

        if (count > MaxExportRows)
        {
            return $"共 {count} 条，超过 {MaxExportRows} 条。请缩小时间范围或指定设备。";
        }

        if (!string.IsNullOrWhiteSpace(filter.Tag) && columnCount == 0)
        {
            return $"没有名称包含「{filter.Tag}」的点位。";
        }

        return null;
    }

    static async Task<(List<HistoryTableColumn> Columns, bool Truncated)> BuildColumnsAsync(
        HistoryStore store,
        AppSettings settings,
        SubscribeWebHistoryFilter filter)
    {
        var names = await store.GetDistinctTagNamesAsync(
            ToQuery(filter, limit: 1, offset: 0),
            Catalog(settings, filter.DeviceId),
            settings.MqttPayload).ConfigureAwait(false);
        var preferred = settings.Tags
            .Where(tag => tag.Enabled && !string.IsNullOrWhiteSpace(tag.Name))
            .Select(tag => tag.Name)
            .ToList();
        var ordered = new List<string>();
        foreach (var name in preferred)
        {
            if (!MatchesTag(name, filter.Tag) || !names.Contains(name, StringComparer.Ordinal))
            {
                continue;
            }

            if (!ordered.Contains(name, StringComparer.Ordinal))
            {
                ordered.Add(name);
            }
        }

        foreach (var name in names.OrderBy(name => name, StringComparer.Ordinal))
        {
            if (!MatchesTag(name, filter.Tag) || ordered.Contains(name, StringComparer.Ordinal))
            {
                continue;
            }

            ordered.Add(name);
        }

        var truncated = ordered.Count > HistoryTableFormatting.MaxColumns;
        return (HistoryTableFormatting.CreateColumns(ordered, Units(settings)), truncated);
    }

    static bool MatchesTag(string name, string? tag) =>
        string.IsNullOrWhiteSpace(tag) || name.Contains(tag, StringComparison.OrdinalIgnoreCase);

    static HistoryQuery ToQuery(SubscribeWebHistoryFilter filter, int limit, int offset) =>
        new()
        {
            From = filter.From,
            To = filter.To,
            DeviceId = filter.DeviceId,
            Limit = limit,
            Offset = offset
        };

    static IReadOnlyList<PlcTag> Catalog(AppSettings settings, string? deviceId) =>
        string.IsNullOrWhiteSpace(deviceId)
            ? settings.Tags
            : SubscribeLineCatalog.ResolveTags(deviceId, null, settings.Tags);

    static Dictionary<string, string?> Units(AppSettings settings) =>
        settings.Tags
            .Where(tag => tag.Enabled && !string.IsNullOrWhiteSpace(tag.Name))
            .GroupBy(tag => tag.Name, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => (string?)group.First().Unit, StringComparer.Ordinal);

    static string? NormalizeDevice(string? device)
    {
        var text = device?.Trim();
        return string.IsNullOrWhiteSpace(text) || text == "全部设备" ? null : text;
    }

    static bool TryParseLocal(string text, out DateTimeOffset value)
    {
        var formats = new[]
        {
            "yyyy-MM-ddTHH:mm",
            "yyyy-MM-ddTHH:mm:ss",
            "yyyy-MM-dd HH:mm",
            "yyyy-MM-dd HH:mm:ss",
            "yyyy-MM-dd"
        };
        if (DateTime.TryParseExact(text.Trim(), formats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var local))
        {
            value = new DateTimeOffset(DateTime.SpecifyKind(local, DateTimeKind.Unspecified), TimeZoneInfo.Local.GetUtcOffset(local));
            return true;
        }

        return DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out value);
    }

    static Dictionary<string, string> ParseQuery(Uri? url)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var query = url?.Query;
        if (string.IsNullOrEmpty(query))
        {
            return result;
        }

        var text = query[0] == '?' ? query[1..] : query;
        foreach (var part in text.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var eq = part.IndexOf('=');
            var key = Uri.UnescapeDataString(eq < 0 ? part : part[..eq]);
            var value = eq < 0 ? string.Empty : Uri.UnescapeDataString(part[(eq + 1)..].Replace('+', ' '));
            result[key] = value;
        }

        return result;
    }

    static string BuildDownloadName(string? deviceId)
    {
        var devicePart = string.IsNullOrWhiteSpace(deviceId) ? "全部设备" : deviceId;
        var invalid = Path.GetInvalidFileNameChars();
        var sanitized = new string(devicePart.Select(ch => invalid.Contains(ch) ? '_' : ch).ToArray()).Trim();
        if (sanitized.Length > 40)
        {
            sanitized = sanitized[..40];
        }

        if (string.IsNullOrWhiteSpace(sanitized))
        {
            sanitized = "全部设备";
        }

        return $"历史数据-{sanitized}-{DateTime.Now:yyyyMMdd-HHmmss}.xlsx";
    }
}
