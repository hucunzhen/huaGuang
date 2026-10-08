using System.Globalization;
using System.Runtime.CompilerServices;
using HuaGuang.Monitor.Models;
using Microsoft.Data.Sqlite;

namespace HuaGuang.Monitor.Services;

/// <summary>
/// 历史按本地日历日分文件（<c>{stem}/yyyy-MM-dd.db</c>），避免单库无限增大。
/// 读/删只打开时间范围内的文件；整日过期可直接删文件。旧版 <c>history.db</c> 仍可读。
/// </summary>
public sealed class HistoryStore
{
    /// <summary>SQLite 单语句绑定参数上限约 999；分页查询 tag_values 时分批 IN。</summary>
    internal const int MaxInClauseParameters = 500;

    const long SampleIdStride = 1_000_000_000L;

    const string SchemaSql =
        """
        CREATE TABLE IF NOT EXISTS telemetry_samples (
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            recorded_at TEXT NOT NULL,
            source_timestamp TEXT,
            device_id TEXT NOT NULL,
            source_topic TEXT,
            operation_mode TEXT NOT NULL,
            quality TEXT,
            plc_host TEXT,
            simulator INTEGER,
            payload_json TEXT
        );

        CREATE TABLE IF NOT EXISTS tag_values (
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            sample_id INTEGER NOT NULL,
            tag_id TEXT,
            tag_name TEXT NOT NULL,
            unit TEXT,
            value_real REAL,
            value_text TEXT,
            value_kind TEXT NOT NULL,
            quality TEXT,
            FOREIGN KEY(sample_id) REFERENCES telemetry_samples(id) ON DELETE CASCADE
        );

        CREATE INDEX IF NOT EXISTS idx_samples_recorded_at ON telemetry_samples(recorded_at);
        CREATE INDEX IF NOT EXISTS idx_samples_device ON telemetry_samples(device_id, recorded_at);
        CREATE INDEX IF NOT EXISTS idx_tag_values_sample ON tag_values(sample_id);
        """;

    string _legacyPath = "";
    string _shardDirectory = "";
    readonly SemaphoreSlim _gate = new(1, 1);

    public HistoryStore(string databasePath)
    {
        ApplyDatabasePath(databasePath);
    }

    public string ShardDirectory => _shardDirectory;

    public string LegacyDatabasePath => _legacyPath;

    public void Relocate(string shardDirectory, string legacyDatabasePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(shardDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(legacyDatabasePath);
        var shard = Path.GetFullPath(shardDirectory);
        var legacy = Path.GetFullPath(legacyDatabasePath);
        _gate.Wait();
        try
        {
            _shardDirectory = shard;
            _legacyPath = legacy;
        }
        finally
        {
            _gate.Release();
        }
    }

    void ApplyDatabasePath(string databasePath)
    {
        _legacyPath = Path.GetFullPath(databasePath);
        var directory = Path.GetDirectoryName(_legacyPath)
            ?? throw new InvalidOperationException("历史库路径无效。");
        var stem = Path.GetFileNameWithoutExtension(_legacyPath);
        if (string.IsNullOrWhiteSpace(stem))
        {
            stem = "history";
        }

        _shardDirectory = Path.Combine(directory, stem);
    }

    public async Task InitializeAsync()
    {
        Directory.CreateDirectory(_shardDirectory);
        if (!File.Exists(_legacyPath))
        {
            return;
        }

        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            await using var connection = OpenConnection(_legacyPath);
            await EnsureSchemaAsync(connection).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<long> AppendAsync(HistorySampleWriteRequest request)
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            var day = ShardDay(request.RecordedAt);
            var path = ShardPath(day);
            Directory.CreateDirectory(_shardDirectory);
            await using var connection = OpenConnection(path);
            await EnsureSchemaAsync(connection).ConfigureAwait(false);
            await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync().ConfigureAwait(false);

            long localId;
            await using (var insertSample = connection.CreateCommand())
            {
                insertSample.Transaction = transaction;
                insertSample.CommandText =
                    """
                    INSERT INTO telemetry_samples
                    (recorded_at, source_timestamp, device_id, source_topic, operation_mode, quality, plc_host, simulator, payload_json)
                    VALUES ($recorded_at, $source_timestamp, $device_id, $source_topic, $operation_mode, $quality, $plc_host, $simulator, $payload_json);
                    SELECT last_insert_rowid();
                    """;
                insertSample.Parameters.AddWithValue("$recorded_at", request.RecordedAt.UtcDateTime.ToString("O"));
                insertSample.Parameters.AddWithValue("$source_timestamp", ToDbDateTime(request.SourceTimestamp));
                insertSample.Parameters.AddWithValue("$device_id", request.DeviceId);
                insertSample.Parameters.AddWithValue("$source_topic", (object?)request.SourceTopic ?? DBNull.Value);
                insertSample.Parameters.AddWithValue("$operation_mode", request.OperationMode.ToString());
                insertSample.Parameters.AddWithValue("$quality", (object?)request.Quality ?? DBNull.Value);
                insertSample.Parameters.AddWithValue("$plc_host", (object?)request.PlcHost ?? DBNull.Value);
                insertSample.Parameters.AddWithValue("$simulator", request.Simulator.HasValue ? request.Simulator.Value ? 1 : 0 : DBNull.Value);
                insertSample.Parameters.AddWithValue("$payload_json", (object?)request.PayloadJson ?? DBNull.Value);
                localId = (long)(await insertSample.ExecuteScalarAsync().ConfigureAwait(false) ?? 0L);
            }

            await using (var insertTag = connection.CreateCommand())
            {
                insertTag.Transaction = transaction;
                insertTag.CommandText =
                    """
                    INSERT INTO tag_values
                    (sample_id, tag_id, tag_name, unit, value_real, value_text, value_kind, quality)
                    VALUES ($sample_id, $tag_id, $tag_name, $unit, $value_real, $value_text, $value_kind, $quality);
                    """;
                var sampleParam = insertTag.Parameters.Add("$sample_id", SqliteType.Integer);
                var tagIdParam = insertTag.Parameters.Add("$tag_id", SqliteType.Text);
                var tagNameParam = insertTag.Parameters.Add("$tag_name", SqliteType.Text);
                var unitParam = insertTag.Parameters.Add("$unit", SqliteType.Text);
                var valueRealParam = insertTag.Parameters.Add("$value_real", SqliteType.Real);
                var valueTextParam = insertTag.Parameters.Add("$value_text", SqliteType.Text);
                var valueKindParam = insertTag.Parameters.Add("$value_kind", SqliteType.Text);
                var qualityParam = insertTag.Parameters.Add("$quality", SqliteType.Text);

                foreach (var tag in request.Tags)
                {
                    var encoded = EncodeValue(tag.Value);
                    sampleParam.Value = localId;
                    tagIdParam.Value = (object?)tag.TagId ?? DBNull.Value;
                    tagNameParam.Value = tag.Name;
                    unitParam.Value = string.IsNullOrWhiteSpace(tag.Unit) ? DBNull.Value : tag.Unit;
                    valueRealParam.Value = encoded.Real.HasValue ? encoded.Real.Value : DBNull.Value;
                    valueTextParam.Value = encoded.Text is null ? DBNull.Value : encoded.Text;
                    valueKindParam.Value = encoded.Kind;
                    qualityParam.Value = tag.Quality;
                    await insertTag.ExecuteNonQueryAsync().ConfigureAwait(false);
                }
            }

            await transaction.CommitAsync().ConfigureAwait(false);
            return EncodeSampleId(day, localId);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<int> CountMatchingAsync(HistoryQuery query)
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            var total = 0;
            foreach (var path in EnumeratePathsForQuery(query))
            {
                await using var connection = OpenConnection(path);
                await EnsureSchemaAsync(connection).ConfigureAwait(false);
                total += await CountOnConnectionAsync(connection, query).ConfigureAwait(false);
            }

            return total;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<IReadOnlyList<HistorySampleSummary>> QueryAsync(HistoryQuery query)
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            var skip = Math.Max(0, query.Offset);
            var take = Math.Max(0, query.Limit);
            if (take == 0)
            {
                return [];
            }

            var results = new List<HistorySampleSummary>(Math.Min(take, 256));
            await using var stream = IterateNewestAsync(query, CancellationToken.None).GetAsyncEnumerator();
            while (await stream.MoveNextAsync().ConfigureAwait(false))
            {
                if (skip > 0)
                {
                    skip--;
                    continue;
                }

                results.Add(stream.Current);
                if (results.Count >= take)
                {
                    break;
                }
            }

            return results;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<HistoryTableData> QueryTableAsync(
        HistoryQuery query,
        int temperaturePrecision,
        IReadOnlyList<string>? preferredTagOrder = null,
        IReadOnlyList<HistoryTableColumn>? fixedColumns = null,
        IReadOnlyDictionary<string, string?>? tagUnitHints = null,
        IReadOnlyList<PlcTag>? catalogTags = null,
        MqttPayloadProfile? mqttProfile = null)
    {
        var samples = await QueryAsync(query).ConfigureAwait(false);
        if (samples.Count == 0)
        {
            return HistoryTableData.Empty;
        }

        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            var valuesBySample = new Dictionary<long, Dictionary<string, string>>();
            var tagUnits = new Dictionary<string, string?>(StringComparer.Ordinal);
            foreach (var group in samples.GroupBy(sample => ResolvePath(sample.Id)))
            {
                if (group.Key is null || !File.Exists(group.Key))
                {
                    continue;
                }

                await using var connection = OpenConnection(group.Key);
                await EnsureSchemaAsync(connection).ConfigureAwait(false);
                var localIds = group.Select(sample => DecodeLocalId(sample.Id)).ToList();
                for (var offset = 0; offset < localIds.Count; offset += MaxInClauseParameters)
                {
                    var chunk = localIds.Skip(offset).Take(MaxInClauseParameters).ToList();
                    await using var command = connection.CreateCommand();
                    command.CommandText = BuildInClause(
                        """
                        SELECT sample_id, tag_name, unit, value_real, value_text, value_kind, quality
                        FROM tag_values
                        WHERE sample_id IN 
                        """,
                        chunk,
                        command).TrimEnd(';') + " ORDER BY sample_id, tag_name;";

                    await using var reader = await command.ExecuteReaderAsync().ConfigureAwait(false);
                    while (await reader.ReadAsync().ConfigureAwait(false))
                    {
                        var sampleId = EncodeSampleId(group.Key, reader.GetInt64(0));
                        var tagName = reader.GetString(1);
                        var displayName = HistoryTagNameResolver.ResolveDisplayName(tagName, catalogTags, mqttProfile);
                        var unit = reader.IsDBNull(2) ? string.Empty : reader.GetString(2);
                        var tag = new PlcTag
                        {
                            Name = displayName,
                            Unit = unit,
                            DataType = InferDataType(reader.GetString(5))
                        };
                        var value = DecodeValue(
                            reader.IsDBNull(3) ? null : reader.GetDouble(3),
                            reader.IsDBNull(4) ? null : reader.GetString(4),
                            reader.GetString(5));
                        var display = ValueFormatting.FormatDisplay(tag, value, temperaturePrecision);

                        if (!valuesBySample.TryGetValue(sampleId, out var map))
                        {
                            map = new Dictionary<string, string>(StringComparer.Ordinal);
                            valuesBySample[sampleId] = map;
                        }

                        map[displayName] = display;
                        if (!tagUnits.ContainsKey(displayName) && !string.IsNullOrWhiteSpace(unit))
                        {
                            tagUnits[displayName] = unit;
                        }
                    }
                }
            }

            if (tagUnitHints is not null)
            {
                foreach (var pair in tagUnitHints)
                {
                    if (!tagUnits.ContainsKey(pair.Key) && !string.IsNullOrWhiteSpace(pair.Value))
                    {
                        tagUnits[pair.Key] = pair.Value;
                    }
                }
            }

            var columns = fixedColumns is { Count: > 0 }
                ? fixedColumns.ToList()
                : BuildTableColumns(
                    valuesBySample,
                    preferredTagOrder,
                    tagUnits,
                    includeAbsentPreferred: string.IsNullOrWhiteSpace(query.DeviceId));
            var rows = new List<HistoryTableRow>(samples.Count);
            foreach (var sample in samples)
            {
                valuesBySample.TryGetValue(sample.Id, out var map);
                map ??= new Dictionary<string, string>(StringComparer.Ordinal);
                var recordedAtText = sample.RecordedAt.ToLocalTime().ToString("MM-dd HH:mm:ss");
                var tagCells = columns
                    .Select(column => new HistoryTableCell
                    {
                        Text = map.TryGetValue(column.TagName, out var value) ? value : "—",
                        Width = column.Width
                    })
                    .ToList();
                rows.Add(new HistoryTableRow
                {
                    SampleId = sample.Id,
                    RecordedAtText = recordedAtText,
                    DeviceId = sample.DeviceId,
                    TagCells = tagCells
                });
            }

            return new HistoryTableData
            {
                Columns = columns,
                Rows = rows,
                HeaderLine = HistoryTableFormatting.FormatHeaderLine(columns)
            };
        }
        finally
        {
            _gate.Release();
        }
    }

    static List<HistoryTableColumn> BuildTableColumns(
        Dictionary<long, Dictionary<string, string>> valuesBySample,
        IReadOnlyList<string>? preferredTagOrder,
        Dictionary<string, string?> tagUnits,
        bool includeAbsentPreferred)
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var map in valuesBySample.Values)
        {
            foreach (var name in map.Keys)
            {
                names.Add(name);
            }
        }

        var ordered = new List<string>();
        if (preferredTagOrder is not null)
        {
            foreach (var name in preferredTagOrder)
            {
                if (ordered.Contains(name, StringComparer.Ordinal))
                {
                    continue;
                }

                if (!includeAbsentPreferred && !names.Contains(name))
                {
                    continue;
                }

                ordered.Add(name);
                names.Remove(name);
            }
        }

        ordered.AddRange(names.OrderBy(name => name, StringComparer.Ordinal));
        if (ordered.Count > HistoryTableFormatting.MaxColumns)
        {
            ordered = ordered.Take(HistoryTableFormatting.MaxColumns).ToList();
        }

        return ordered.Select(name =>
        {
            tagUnits.TryGetValue(name, out var unit);
            var header = HistoryTableFormatting.FormatColumnHeader(name, unit);
            return new HistoryTableColumn
            {
                TagName = name,
                HeaderText = header,
                Width = HistoryTableFormatting.EstimateHeaderColumnWidth(header, minWidth: HistoryTableFormatting.TagColumnWidth)
            };
        }).ToList();
    }

    public async Task<HistorySampleDetail?> GetDetailAsync(
        long sampleId,
        int temperaturePrecision,
        IReadOnlyList<PlcTag>? catalogTags = null,
        MqttPayloadProfile? mqttProfile = null)
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            var path = ResolvePath(sampleId);
            if (path is null || !File.Exists(path))
            {
                return null;
            }

            var localId = DecodeLocalId(sampleId);
            await using var connection = OpenConnection(path);
            await EnsureSchemaAsync(connection).ConfigureAwait(false);
            await using var sampleCommand = connection.CreateCommand();
            sampleCommand.CommandText =
                """
                SELECT id, recorded_at, source_timestamp, device_id, operation_mode, quality, source_topic, payload_json
                FROM telemetry_samples
                WHERE id = $id;
                """;
            sampleCommand.Parameters.AddWithValue("$id", localId);

            HistorySampleDetail? detail;
            await using (var reader = await sampleCommand.ExecuteReaderAsync().ConfigureAwait(false))
            {
                if (!await reader.ReadAsync().ConfigureAwait(false))
                {
                    return null;
                }

                var mode = Enum.TryParse<AppOperationMode>(reader.GetString(4), out var parsed)
                    ? parsed
                    : AppOperationMode.Acquisition;
                detail = new HistorySampleDetail
                {
                    Id = EncodeSampleId(path, reader.GetInt64(0)),
                    RecordedAt = ParseDbDateTime(reader.GetString(1)),
                    SourceTimestamp = reader.IsDBNull(2) ? null : ParseDbDateTime(reader.GetString(2)),
                    DeviceId = reader.GetString(3),
                    OperationModeLabel = ModeLabel(mode),
                    Quality = reader.IsDBNull(5) ? "—" : reader.GetString(5),
                    SourceTopic = reader.IsDBNull(6) ? null : reader.GetString(6),
                    PayloadJson = reader.IsDBNull(7) ? null : reader.GetString(7)
                };
            }

            var tagsByName = new Dictionary<string, HistoryTagValueRow>(StringComparer.Ordinal);
            await using (var tagCommand = connection.CreateCommand())
            {
                tagCommand.CommandText =
                    """
                    SELECT tag_name, unit, value_real, value_text, value_kind, quality
                    FROM tag_values
                    WHERE sample_id = $id
                    ORDER BY tag_name;
                    """;
                tagCommand.Parameters.AddWithValue("$id", localId);
                await using var reader = await tagCommand.ExecuteReaderAsync().ConfigureAwait(false);
                while (await reader.ReadAsync().ConfigureAwait(false))
                {
                    var storedName = reader.GetString(0);
                    var displayName = HistoryTagNameResolver.ResolveDisplayName(storedName, catalogTags, mqttProfile);
                    var tag = new PlcTag
                    {
                        Name = displayName,
                        Unit = reader.IsDBNull(1) ? string.Empty : reader.GetString(1),
                        DataType = InferDataType(reader.GetString(4))
                    };
                    var value = DecodeValue(
                        reader.IsDBNull(2) ? null : reader.GetDouble(2),
                        reader.IsDBNull(3) ? null : reader.GetString(3),
                        reader.GetString(4));
                    tagsByName[displayName] = new HistoryTagValueRow
                    {
                        TagName = tag.Name,
                        Unit = string.IsNullOrWhiteSpace(tag.Unit) ? null : tag.Unit,
                        DisplayValue = ValueFormatting.FormatDisplay(tag, value, temperaturePrecision),
                        Quality = reader.IsDBNull(5) ? "Good" : reader.GetString(5)
                    };
                }
            }

            var tags = tagsByName.Values.OrderBy(row => row.TagName, StringComparer.Ordinal).ToList();
            return new HistorySampleDetail
            {
                Id = detail.Id,
                RecordedAt = detail.RecordedAt,
                SourceTimestamp = detail.SourceTimestamp,
                DeviceId = detail.DeviceId,
                OperationModeLabel = detail.OperationModeLabel,
                Quality = detail.Quality,
                SourceTopic = detail.SourceTopic,
                PayloadJson = detail.PayloadJson,
                Tags = tags
            };
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<IReadOnlyList<string>> GetDeviceIdsAsync()
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            var results = new SortedSet<string>(StringComparer.Ordinal);
            foreach (var path in EnumerateExistingDatabasePaths())
            {
                await using var connection = OpenConnection(path);
                await EnsureSchemaAsync(connection).ConfigureAwait(false);
                await using var command = connection.CreateCommand();
                command.CommandText = "SELECT DISTINCT device_id FROM telemetry_samples ORDER BY device_id;";
                await using var reader = await command.ExecuteReaderAsync().ConfigureAwait(false);
                while (await reader.ReadAsync().ConfigureAwait(false))
                {
                    results.Add(reader.GetString(0));
                }
            }

            return results.ToList();
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<IReadOnlyList<string>> GetDistinctTagNamesAsync(
        HistoryQuery query,
        IReadOnlyList<PlcTag>? catalogTags = null,
        MqttPayloadProfile? mqttProfile = null)
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            var results = new HashSet<string>(StringComparer.Ordinal);
            foreach (var path in EnumeratePathsForQuery(query))
            {
                await using var connection = OpenConnection(path);
                await EnsureSchemaAsync(connection).ConfigureAwait(false);
                await using var command = connection.CreateCommand();
                ApplySampleFilter(
                    command,
                    """
                    SELECT DISTINCT t.tag_name
                    FROM tag_values t
                    INNER JOIN telemetry_samples s ON s.id = t.sample_id
                    """,
                    query,
                    " ORDER BY t.tag_name;");
                await using var reader = await command.ExecuteReaderAsync().ConfigureAwait(false);
                while (await reader.ReadAsync().ConfigureAwait(false))
                {
                    results.Add(HistoryTagNameResolver.ResolveDisplayName(
                        reader.GetString(0),
                        catalogTags,
                        mqttProfile));
                }
            }

            return results.OrderBy(name => name, StringComparer.Ordinal).ToList();
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<IReadOnlyDictionary<string, string?>> GetDistinctTagUnitsAsync(
        HistoryQuery query,
        IReadOnlyList<PlcTag>? catalogTags = null,
        MqttPayloadProfile? mqttProfile = null)
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            var results = new Dictionary<string, string?>(StringComparer.Ordinal);
            foreach (var path in EnumeratePathsForQuery(query))
            {
                await using var connection = OpenConnection(path);
                await EnsureSchemaAsync(connection).ConfigureAwait(false);
                await using var command = connection.CreateCommand();
                ApplySampleFilter(
                    command,
                    """
                    SELECT t.tag_name, MAX(COALESCE(t.unit, '')) AS unit
                    FROM tag_values t
                    INNER JOIN telemetry_samples s ON s.id = t.sample_id
                    """,
                    query,
                    " GROUP BY t.tag_name ORDER BY t.tag_name;");
                await using var reader = await command.ExecuteReaderAsync().ConfigureAwait(false);
                while (await reader.ReadAsync().ConfigureAwait(false))
                {
                    var displayName = HistoryTagNameResolver.ResolveDisplayName(
                        reader.GetString(0),
                        catalogTags,
                        mqttProfile);
                    var unit = reader.GetString(1);
                    var normalizedUnit = string.IsNullOrWhiteSpace(unit) ? null : unit;
                    if (!results.ContainsKey(displayName) || results[displayName] is null)
                    {
                        results[displayName] = normalizedUnit;
                    }
                }
            }

            return results;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<int> PruneOlderThanAsync(DateTimeOffset cutoff)
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            var deleted = 0;
            var cutoffDay = ShardDay(cutoff);
            if (Directory.Exists(_shardDirectory))
            {
                foreach (var path in Directory.GetFiles(_shardDirectory, "*.db"))
                {
                    if (!TryParseShardDay(path, out var day))
                    {
                        continue;
                    }

                    if (day < cutoffDay)
                    {
                        await DeleteDatabaseFileAsync(path).ConfigureAwait(false);
                        continue;
                    }

                    if (day == cutoffDay)
                    {
                        deleted += await DeleteWhereAsync(
                            path,
                            "recorded_at < $cutoff",
                            command => command.Parameters.AddWithValue("$cutoff", cutoff.UtcDateTime.ToString("O")))
                            .ConfigureAwait(false);
                        await DeleteFileIfEmptyAsync(path).ConfigureAwait(false);
                    }
                }
            }

            if (File.Exists(_legacyPath))
            {
                var legacyInfo = new FileInfo(_legacyPath);
                if (legacyInfo.Length >= 8 * 1024 * 1024)
                {
                    await DeleteDatabaseFileAsync(_legacyPath).ConfigureAwait(false);
                }
                else
                {
                    deleted += await DeleteWhereAsync(
                        _legacyPath,
                        "recorded_at < $cutoff",
                        command => command.Parameters.AddWithValue("$cutoff", cutoff.UtcDateTime.ToString("O")))
                        .ConfigureAwait(false);
                    await DeleteFileIfEmptyAsync(_legacyPath).ConfigureAwait(false);
                }
            }

            return deleted;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<bool> DeleteSampleAsync(long sampleId)
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            var path = ResolvePath(sampleId);
            if (path is null || !File.Exists(path))
            {
                return false;
            }

            var localId = DecodeLocalId(sampleId);
            var deleted = await DeleteWhereAsync(
                path,
                "id = $id",
                command => command.Parameters.AddWithValue("$id", localId)).ConfigureAwait(false);
            await DeleteFileIfEmptyAsync(path).ConfigureAwait(false);
            return deleted > 0;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<int> DeleteMatchingAsync(HistoryQuery query)
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            var where = "recorded_at >= $from AND recorded_at <= $to";
            if (!string.IsNullOrWhiteSpace(query.DeviceId))
            {
                where = "device_id = $device_id AND " + where;
            }

            var deleted = 0;
            foreach (var path in EnumeratePathsForQuery(query).ToList())
            {
                deleted += await DeleteWhereAsync(
                    path,
                    where,
                    command =>
                    {
                        command.Parameters.AddWithValue("$from", query.From.UtcDateTime.ToString("O"));
                        command.Parameters.AddWithValue("$to", query.To.UtcDateTime.ToString("O"));
                        if (!string.IsNullOrWhiteSpace(query.DeviceId))
                        {
                            command.Parameters.AddWithValue("$device_id", query.DeviceId);
                        }
                    }).ConfigureAwait(false);
                await DeleteFileIfEmptyAsync(path).ConfigureAwait(false);
            }

            return deleted;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<int> DeleteAllAsync()
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            var deleted = 0;
            foreach (var path in EnumerateExistingDatabasePaths().ToList())
            {
                deleted += await CountAllSamplesAsync(path).ConfigureAwait(false);
                await DeleteDatabaseFileAsync(path).ConfigureAwait(false);
            }

            return deleted;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<(int SampleCount, DateTimeOffset? Oldest, DateTimeOffset? Newest)> GetStatsAsync()
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            var count = 0;
            DateTimeOffset? oldest = null;
            DateTimeOffset? newest = null;
            foreach (var path in EnumerateExistingDatabasePaths())
            {
                await using var connection = OpenConnection(path);
                await EnsureSchemaAsync(connection).ConfigureAwait(false);
                await using var command = connection.CreateCommand();
                command.CommandText =
                    """
                    SELECT COUNT(*), MIN(recorded_at), MAX(recorded_at)
                    FROM telemetry_samples;
                    """;
                await using var reader = await command.ExecuteReaderAsync().ConfigureAwait(false);
                if (!await reader.ReadAsync().ConfigureAwait(false))
                {
                    continue;
                }

                count += reader.GetInt32(0);
                if (!reader.IsDBNull(1))
                {
                    var value = ParseDbDateTime(reader.GetString(1));
                    if (oldest is null || value < oldest)
                    {
                        oldest = value;
                    }
                }

                if (!reader.IsDBNull(2))
                {
                    var value = ParseDbDateTime(reader.GetString(2));
                    if (newest is null || value > newest)
                    {
                        newest = value;
                    }
                }
            }

            return (count, oldest, newest);
        }
        finally
        {
            _gate.Release();
        }
    }

    async IAsyncEnumerable<HistorySampleSummary> IterateNewestAsync(
        HistoryQuery query,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await using var shard = EnumerateShardSummariesAsync(query, cancellationToken).GetAsyncEnumerator(cancellationToken);
        await using var legacy = EnumerateSummariesOnPathAsync(
            File.Exists(_legacyPath) ? _legacyPath : null,
            query,
            isLegacy: true,
            cancellationToken).GetAsyncEnumerator(cancellationToken);

        var hasShard = await shard.MoveNextAsync().ConfigureAwait(false);
        var hasLegacy = await legacy.MoveNextAsync().ConfigureAwait(false);
        while (hasShard || hasLegacy)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!hasLegacy || (hasShard && shard.Current.RecordedAt >= legacy.Current.RecordedAt))
            {
                yield return shard.Current;
                hasShard = await shard.MoveNextAsync().ConfigureAwait(false);
            }
            else
            {
                yield return legacy.Current;
                hasLegacy = await legacy.MoveNextAsync().ConfigureAwait(false);
            }
        }
    }

    async IAsyncEnumerable<HistorySampleSummary> EnumerateShardSummariesAsync(
        HistoryQuery query,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        foreach (var day in InclusiveDays(query.From, query.To).OrderByDescending(day => day))
        {
            var path = ShardPath(day);
            if (!File.Exists(path))
            {
                continue;
            }

            await foreach (var row in EnumerateSummariesOnPathAsync(path, query, isLegacy: false, cancellationToken)
                               .ConfigureAwait(false))
            {
                yield return row;
            }
        }
    }

    async IAsyncEnumerable<HistorySampleSummary> EnumerateSummariesOnPathAsync(
        string? path,
        HistoryQuery query,
        bool isLegacy,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        if (path is null || !File.Exists(path))
        {
            yield break;
        }

        await using var connection = OpenConnection(path);
        await EnsureSchemaAsync(connection).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        ApplySampleFilter(
            command,
            """
            SELECT s.id, s.recorded_at, s.device_id, s.operation_mode, s.quality, s.source_topic,
                   COUNT(t.id) AS tag_count
            FROM telemetry_samples s
            LEFT JOIN tag_values t ON t.sample_id = s.id
            """,
            query,
            """
             GROUP BY s.id
             ORDER BY s.recorded_at DESC;
            """);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            var mode = Enum.TryParse<AppOperationMode>(reader.GetString(3), out var parsed)
                ? parsed
                : AppOperationMode.Acquisition;
            var localId = reader.GetInt64(0);
            yield return new HistorySampleSummary
            {
                Id = isLegacy ? localId : EncodeSampleId(path, localId),
                RecordedAt = ParseDbDateTime(reader.GetString(1)),
                DeviceId = reader.GetString(2),
                OperationModeLabel = ModeLabel(mode),
                Quality = reader.IsDBNull(4) ? "—" : reader.GetString(4),
                SourceTopic = reader.IsDBNull(5) ? null : reader.GetString(5),
                TagCount = reader.GetInt32(6)
            };
        }
    }

    static async Task<int> CountOnConnectionAsync(SqliteConnection connection, HistoryQuery query)
    {
        await using var command = connection.CreateCommand();
        ApplySampleFilter(command, "SELECT COUNT(*) FROM telemetry_samples s", query, string.Empty);
        var result = await command.ExecuteScalarAsync().ConfigureAwait(false);
        return Convert.ToInt32(result, CultureInfo.InvariantCulture);
    }

    static void ApplySampleFilter(SqliteCommand command, string fromClause, HistoryQuery query, string suffix)
    {
        command.CommandText = fromClause + " WHERE s.recorded_at >= $from AND s.recorded_at <= $to";
        command.Parameters.AddWithValue("$from", query.From.UtcDateTime.ToString("O"));
        command.Parameters.AddWithValue("$to", query.To.UtcDateTime.ToString("O"));
        if (!string.IsNullOrWhiteSpace(query.DeviceId))
        {
            command.CommandText += " AND s.device_id = $device_id";
            command.Parameters.AddWithValue("$device_id", query.DeviceId);
        }

        command.CommandText += suffix;
    }

    static async Task<int> DeleteSamplesWhereAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string sampleWhereClause,
        Action<SqliteCommand>? configure)
    {
        await using var deleteSamples = connection.CreateCommand();
        deleteSamples.Transaction = transaction;
        deleteSamples.CommandText = $"DELETE FROM telemetry_samples WHERE {sampleWhereClause};";
        configure?.Invoke(deleteSamples);
        return await deleteSamples.ExecuteNonQueryAsync().ConfigureAwait(false);
    }

    async Task<int> DeleteWhereAsync(string path, string where, Action<SqliteCommand> configure)
    {
        await using var connection = OpenConnection(path);
        await EnsureSchemaAsync(connection).ConfigureAwait(false);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync().ConfigureAwait(false);
        var deleted = await DeleteSamplesWhereAsync(connection, transaction, where, configure).ConfigureAwait(false);
        await transaction.CommitAsync().ConfigureAwait(false);
        return deleted;
    }

    async Task<int> CountAllSamplesAsync(string path)
    {
        await using var connection = OpenConnection(path);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM telemetry_samples;";
        var result = await command.ExecuteScalarAsync().ConfigureAwait(false);
        return Convert.ToInt32(result, CultureInfo.InvariantCulture);
    }

    async Task DeleteFileIfEmptyAsync(string path)
    {
        if (!File.Exists(path) || await CountAllSamplesAsync(path).ConfigureAwait(false) > 0)
        {
            return;
        }

        await DeleteDatabaseFileAsync(path).ConfigureAwait(false);
    }

    async Task DeleteDatabaseFileAsync(string path)
    {
        try
        {
            await using (var connection = OpenConnection(path))
            {
                SqliteConnection.ClearPool(connection);
            }

            File.Delete(path);
            TryDelete(path + "-wal");
            TryDelete(path + "-shm");
        }
        catch
        {
        }
    }

    static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
        }
    }

    static string BuildInClause(string prefix, IReadOnlyList<long> ids, SqliteCommand command)
    {
        var placeholders = new string[ids.Count];
        for (var i = 0; i < ids.Count; i++)
        {
            var name = $"$id{i}";
            placeholders[i] = name;
            command.Parameters.AddWithValue(name, ids[i]);
        }

        return prefix + "(" + string.Join(", ", placeholders) + ");";
    }

    IEnumerable<string> EnumeratePathsForQuery(HistoryQuery query)
    {
        foreach (var day in InclusiveDays(query.From, query.To))
        {
            var path = ShardPath(day);
            if (File.Exists(path))
            {
                yield return path;
            }
        }

        if (File.Exists(_legacyPath))
        {
            yield return _legacyPath;
        }
    }

    IEnumerable<string> EnumerateExistingDatabasePaths()
    {
        if (Directory.Exists(_shardDirectory))
        {
            foreach (var path in Directory.GetFiles(_shardDirectory, "*.db").OrderBy(path => path, StringComparer.Ordinal))
            {
                yield return path;
            }
        }

        if (File.Exists(_legacyPath))
        {
            yield return _legacyPath;
        }
    }

    static IEnumerable<DateOnly> InclusiveDays(DateTimeOffset from, DateTimeOffset to)
    {
        var start = ShardDay(from);
        var end = ShardDay(to);
        if (end < start)
        {
            (start, end) = (end, start);
        }

        for (var day = start; day <= end; day = day.AddDays(1))
        {
            yield return day;
        }
    }

    string ShardPath(DateOnly day) => Path.Combine(_shardDirectory, $"{day:yyyy-MM-dd}.db");

    static DateOnly ShardDay(DateTimeOffset timestamp) => DateOnly.FromDateTime(timestamp.ToLocalTime().Date);

    static bool TryParseShardDay(string path, out DateOnly day) =>
        DateOnly.TryParseExact(
            Path.GetFileNameWithoutExtension(path),
            "yyyy-MM-dd",
            CultureInfo.InvariantCulture,
            DateTimeStyles.None,
            out day);

    string? ResolvePath(long sampleId)
    {
        if (sampleId < SampleIdStride)
        {
            return File.Exists(_legacyPath) ? _legacyPath : null;
        }

        var day = DateOnly.FromDayNumber((int)(sampleId / SampleIdStride));
        return ShardPath(day);
    }

    long EncodeSampleId(string path, long localId)
    {
        if (string.Equals(path, _legacyPath, StringComparison.OrdinalIgnoreCase))
        {
            return localId;
        }

        return TryParseShardDay(path, out var day) ? EncodeSampleId(day, localId) : localId;
    }

    static long EncodeSampleId(DateOnly day, long localId) => (day.DayNumber * SampleIdStride) + localId;

    static long DecodeLocalId(long sampleId) => sampleId < SampleIdStride ? sampleId : sampleId % SampleIdStride;

    static SqliteConnection OpenConnection(string path)
    {
        var connection = new SqliteConnection($"Data Source={path};Cache=Shared;Default Timeout=5");
        connection.Open();
        using var pragma = connection.CreateCommand();
        pragma.CommandText = "PRAGMA foreign_keys = ON;";
        pragma.ExecuteNonQuery();
        return connection;
    }

    static async Task EnsureSchemaAsync(SqliteConnection connection)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = SchemaSql;
        await command.ExecuteNonQueryAsync().ConfigureAwait(false);
    }

    static string ModeLabel(AppOperationMode mode) =>
        mode == AppOperationMode.Subscribe ? "订阅" : "采集";

    static object? ToDbDateTime(DateTimeOffset? value) =>
        value.HasValue ? value.Value.UtcDateTime.ToString("O") : DBNull.Value;

    static DateTimeOffset ParseDbDateTime(string text) =>
        DateTimeOffset.Parse(text, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);

    static (double? Real, string? Text, string Kind) EncodeValue(object? value) =>
        value switch
        {
            null => (null, null, "null"),
            bool flag => (null, flag ? "true" : "false", "bool"),
            string text => (null, text, "string"),
            double number => (number, null, "number"),
            float number => (number, null, "number"),
            int number => (number, null, "number"),
            long number => (number, null, "number"),
            decimal number => ((double)number, null, "number"),
            _ => (null, Convert.ToString(value, CultureInfo.InvariantCulture), "string")
        };

    static object? DecodeValue(double? real, string? text, string kind) =>
        kind switch
        {
            "null" => null,
            "bool" => bool.TryParse(text, out var flag) && flag,
            "number" when real.HasValue => real.Value,
            _ => text
        };

    static TagDataType InferDataType(string kind) =>
        kind switch
        {
            "bool" => TagDataType.Bool,
            "string" => TagDataType.String,
            _ => TagDataType.Float32
        };
}
